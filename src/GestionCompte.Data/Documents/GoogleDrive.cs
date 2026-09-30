using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GestionCompte.Data.Documents;

/// <summary>Identifiant d'application Google (« ID client OAuth » de type Application de bureau), créé par l'utilisateur.</summary>
public sealed record IdentifiantsGoogle(string ClientId, string ClientSecret);

/// <summary>
/// Connexion au compte Google sans bibliothèque Google : le navigateur ouvre la page de connexion de Google,
/// qui renvoie vers un petit serveur local (127.0.0.1) ; le code reçu est échangé contre un jeton.
/// Droits demandés : « drive.file » (l'application ne voit que les fichiers qu'elle a créés), envoi de mails
/// (sans lecture de la boîte), lecture des contacts et adresse du compte.
/// </summary>
public sealed class ConnexionGoogle
{
    public const string PorteeDrive = "https://www.googleapis.com/auth/drive.file";
    public const string PorteeEnvoiMail = "https://www.googleapis.com/auth/gmail.send";
    public const string PorteeContacts = "https://www.googleapis.com/auth/contacts.readonly";
    public const string PorteeAutresContacts = "https://www.googleapis.com/auth/contacts.other.readonly";
    public const string PorteeAdresse = "email";

    public static IReadOnlyList<string> Portees { get; } =
        new[] { PorteeAdresse, PorteeDrive, PorteeEnvoiMail, PorteeContacts, PorteeAutresContacts };

    private const string AdresseInfos = "https://openidconnect.googleapis.com/v1/userinfo";
    private const string AdresseAutorisation = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string AdresseJeton = "https://oauth2.googleapis.com/token";

    private readonly HttpClient _http;
    private readonly IdentifiantsGoogle _identifiants;
    private string? _jeton;
    private DateTime _expiration;

    /// <param name="jetonRenouvellement">Jeton mémorisé lors d'une connexion précédente, ou null.</param>
    public ConnexionGoogle(HttpClient http, IdentifiantsGoogle identifiants, string? jetonRenouvellement)
    {
        _http = http;
        _identifiants = identifiants;
        JetonRenouvellement = jetonRenouvellement;
    }

    /// <summary>Jeton longue durée à mémoriser (chiffré par Windows) pour ne pas se reconnecter à chaque lancement.</summary>
    public string? JetonRenouvellement { get; private set; }

    public bool Connecte => JetonRenouvellement is not null;

    /// <summary>Droits accordés lors de la connexion (séparés par des espaces).</summary>
    public string? PorteesAccordees { get; private set; }

    /// <summary>Adresse Gmail du compte connecté.</summary>
    public async Task<string?> AdresseAsync(CancellationToken annulation = default)
    {
        using var requete = new HttpRequestMessage(HttpMethod.Get, AdresseInfos);
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await JetonAsync(annulation));
        using var reponse = await _http.SendAsync(requete, annulation);
        if (!reponse.IsSuccessStatusCode)
            return null;
        using var json = JsonDocument.Parse(await reponse.Content.ReadAsStringAsync(annulation));
        return json.RootElement.TryGetProperty("email", out var adresse) ? adresse.GetString() : null;
    }

    /// <summary>Ouvre la page de connexion Google et attend la réponse (5 minutes au plus).</summary>
    /// <param name="ouvrirNavigateur">Ouvre l'adresse dans le navigateur de l'utilisateur.</param>
    /// <param name="etape">Annonce l'étape en cours (affichée dans la configuration).</param>
    public async Task ConnecterAsync(Action<string> ouvrirNavigateur, CancellationToken annulation = default,
        Action<string>? etape = null)
    {
        // Petit serveur local (sans HttpListener, qui demande des droits administrateur sous Windows).
        var ecoute = new TcpListener(IPAddress.Loopback, 0);
        ecoute.Start();
        try
        {
            var redirection = $"http://127.0.0.1:{((IPEndPoint)ecoute.LocalEndpoint).Port}/";
            var verificateur = Base64Url(RandomNumberGenerator.GetBytes(32));
            var defi = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verificateur)));
            var etat = Base64Url(RandomNumberGenerator.GetBytes(16));

            ouvrirNavigateur($"{AdresseAutorisation}?response_type=code&client_id={Uri.EscapeDataString(_identifiants.ClientId)}" +
                             $"&redirect_uri={Uri.EscapeDataString(redirection)}&scope={Uri.EscapeDataString(string.Join(' ', Portees))}" +
                             $"&code_challenge={defi}&code_challenge_method=S256&state={etat}&access_type=offline&prompt=consent");

            using var delai = CancellationTokenSource.CreateLinkedTokenSource(annulation);
            delai.CancelAfter(TimeSpan.FromMinutes(5));
            var retour = new TaskCompletionSource<Dictionary<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var fin = delai.Token.Register(() => retour.TrySetCanceled(delai.Token));
            // Chaque connexion est traitée à part : le navigateur en ouvre parfois à l'avance sans rien envoyer,
            // et une connexion muette ne doit pas bloquer le retour de Google qui arrive par une autre.
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!retour.Task.IsCompleted)
                        _ = TraiterConnexionAsync(await ecoute.AcceptTcpClientAsync(delai.Token), etat, retour, delai.Token);
                }
                catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    // Écoute arrêtée : connexion terminée, annulée ou délai dépassé.
                }
            });
            etape?.Invoke("Attente de la réponse de Google dans le navigateur…");
            Dictionary<string, string> requete;
            try
            {
                requete = await retour.Task;
            }
            catch (OperationCanceledException) when (!annulation.IsCancellationRequested)
            {
                throw new TimeoutException("L'application n'a pas reçu la réponse de Google dans le navigateur en 5 minutes.");
            }

            if (requete.GetValueOrDefault("code") is not { } code || requete.GetValueOrDefault("state") != etat)
                throw new InvalidOperationException($"Connexion refusée par Google ({requete.GetValueOrDefault("error") ?? "réponse inattendue"}).");

            etape?.Invoke("Code reçu de Google, validation…");
            var reponse = await DemanderJetonAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirection,
                ["code_verifier"] = verificateur,
            }, annulation);
            JetonRenouvellement = reponse.RefreshToken
                ?? throw new InvalidOperationException("Google n'a pas renvoyé de jeton de renouvellement.");
            // L'utilisateur peut décocher certains droits sur la page de Google.
            PorteesAccordees = reponse.Scope ?? string.Join(' ', Portees);
        }
        finally
        {
            ecoute.Stop();
        }
    }

    /// <summary>Durée laissée à une connexion du navigateur pour envoyer sa demande avant d'être ignorée.</summary>
    private static readonly TimeSpan AttenteDemande = TimeSpan.FromSeconds(10);

    /// <summary>Lit la demande d'une connexion ; le retour de Google (code ou erreur) termine l'attente.</summary>
    private static async Task TraiterConnexionAsync(TcpClient client, string etat,
        TaskCompletionSource<Dictionary<string, string>> retour, CancellationToken annulation)
    {
        using var _ = client;
        using var delai = CancellationTokenSource.CreateLinkedTokenSource(annulation);
        delai.CancelAfter(AttenteDemande);
        try
        {
            await using var flux = client.GetStream();
            var ligne = await LirePremiereLigneAsync(flux, delai.Token);
            // Ex. « GET /?state=…&code=… HTTP/1.1 » ; les autres demandes du navigateur (favicon…) reçoivent une page vide.
            var chemin = ligne.Split(' ').ElementAtOrDefault(1) ?? "";
            var requete = Parametres(chemin);
            var reponduAuRetour = requete.ContainsKey("code") || requete.ContainsKey("error");
            // Noté tout de suite : une coupure du navigateur pendant l'envoi de la page ne doit pas le perdre.
            if (reponduAuRetour)
                retour.TrySetResult(requete);
            var reussi = requete.GetValueOrDefault("code") is not null && requete.GetValueOrDefault("state") == etat;
            var page = !reponduAuRetour ? ""
                : reussi
                    ? "<html><body style='font-family:Segoe UI;padding:40px'><h2>Connexion réussie</h2><p>Vous pouvez fermer cette page et revenir dans Gestion compte.</p></body></html>"
                    : "<html><body style='font-family:Segoe UI;padding:40px'><h2>Connexion annulée</h2><p>Revenez dans Gestion compte pour réessayer.</p></body></html>";
            // Lit le reste de l'en-tête : fermer avec des octets non lus coupe brutalement la page sous Windows.
            while ((await LirePremiereLigneAsync(flux, delai.Token)).Length > 0) { }
            var corps = Encoding.UTF8.GetBytes(page);
            var entete = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(reponduAuRetour ? "200 OK" : "404 Not Found")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {corps.Length}\r\nConnection: close\r\n\r\n");
            await flux.WriteAsync(entete, delai.Token);
            await flux.WriteAsync(corps, delai.Token);
            client.Client.Shutdown(SocketShutdown.Send);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException or SocketException)
        {
            // Connexion muette ou coupée : ignorée.
        }
    }

    private static async Task<string> LirePremiereLigneAsync(Stream flux, CancellationToken annulation)
    {
        var octets = new List<byte>();
        var tampon = new byte[1];
        while (octets.Count < 8192 && await flux.ReadAsync(tampon, annulation) == 1 && tampon[0] != '\n')
            octets.Add(tampon[0]);
        return Encoding.ASCII.GetString(octets.ToArray()).TrimEnd('\r');
    }

    /// <summary>Paramètres d'une adresse « /?a=1&b=2 ».</summary>
    internal static Dictionary<string, string> Parametres(string chemin)
    {
        var resultat = new Dictionary<string, string>();
        var debut = chemin.IndexOf('?');
        if (debut < 0)
            return resultat;
        foreach (var paire in chemin[(debut + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var egal = paire.IndexOf('=');
            var cle = Uri.UnescapeDataString(egal < 0 ? paire : paire[..egal]);
            resultat[cle] = egal < 0 ? "" : Uri.UnescapeDataString(paire[(egal + 1)..].Replace('+', ' '));
        }
        return resultat;
    }

    /// <summary>Jeton d'accès valable (renouvelé automatiquement une heure sur deux).</summary>
    public async Task<string> JetonAsync(CancellationToken annulation = default)
    {
        if (_jeton is not null && DateTime.UtcNow < _expiration)
            return _jeton;
        if (JetonRenouvellement is null)
            throw new InvalidOperationException("Pas connecté à Google Drive.");

        var reponse = await DemanderJetonAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = JetonRenouvellement,
        }, annulation);
        _jeton = reponse.AccessToken;
        _expiration = DateTime.UtcNow.AddSeconds(Math.Max(60, reponse.ExpiresIn - 60));
        return _jeton!;
    }

    public void Deconnecter()
    {
        JetonRenouvellement = null;
        _jeton = null;
    }

    private async Task<ReponseJeton> DemanderJetonAsync(Dictionary<string, string> parametres, CancellationToken annulation)
    {
        parametres["client_id"] = _identifiants.ClientId;
        parametres["client_secret"] = _identifiants.ClientSecret;
        HttpResponseMessage envoi;
        try
        {
            envoi = await _http.PostAsync(AdresseJeton, new FormUrlEncodedContent(parametres), annulation);
        }
        catch (OperationCanceledException) when (!annulation.IsCancellationRequested)
        {
            throw new TimeoutException($"Google n'a pas répondu à la validation du code ({AdresseJeton}) dans le délai de {_http.Timeout.TotalSeconds:0} secondes.");
        }
        using var reponse = envoi;
        var texte = await reponse.Content.ReadAsStringAsync(annulation);
        if (!reponse.IsSuccessStatusCode)
        {
            if (texte.Contains("invalid_grant"))
                JetonRenouvellement = null; // accès retiré ou expiré : il faudra se reconnecter
            throw new HttpRequestException($"Google a refusé la connexion ({(int)reponse.StatusCode}) : {Extrait(texte)}");
        }
        return JsonSerializer.Deserialize<ReponseJeton>(texte)
            ?? throw new HttpRequestException("Réponse de Google illisible.");
    }

    private static string Base64Url(byte[] octets) =>
        Convert.ToBase64String(octets).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static string Extrait(string texte) => texte.Length > 300 ? texte[..300] + "…" : texte;

    private sealed record ReponseJeton(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>Coffre rangé dans un dossier « Gestion compte - Documents » du Google Drive de l'utilisateur (API Drive v3).</summary>
public sealed class StockageGoogleDrive : IStockageDocuments
{
    public const string NomDossier = "Gestion compte - Documents";
    private const string Api = "https://www.googleapis.com/drive/v3/files";
    private const string ApiEnvoi = "https://www.googleapis.com/upload/drive/v3/files";

    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<string>> _jeton;
    private string? _dossier;
    private Dictionary<string, string>? _fichiers;

    public StockageGoogleDrive(HttpClient http, Func<CancellationToken, Task<string>> jeton)
    {
        _http = http;
        _jeton = jeton;
    }

    public string Description => $"Google Drive, dossier « {NomDossier} »";

    public async Task<byte[]?> LireAsync(string nom, CancellationToken annulation = default)
    {
        var fichiers = await FichiersAsync(annulation);
        if (!fichiers.TryGetValue(nom, out var id))
            return null;
        using var reponse = await EnvoyerAsync(HttpMethod.Get, $"{Api}/{id}?alt=media", null, annulation);
        return await reponse.Content.ReadAsByteArrayAsync(annulation);
    }

    public async Task EcrireAsync(string nom, byte[] contenu, CancellationToken annulation = default)
    {
        var fichiers = await FichiersAsync(annulation);
        if (fichiers.TryGetValue(nom, out var id))
        {
            using var _ = await EnvoyerAsync(HttpMethod.Patch, $"{ApiEnvoi}/{id}?uploadType=media",
                new ByteArrayContent(contenu) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } }, annulation);
            return;
        }

        var dossier = await DossierAsync(annulation);
        var description = JsonSerializer.Serialize(new { name = nom, parents = new[] { dossier } });
        var corps = new MultipartContent("related")
        {
            new StringContent(description, Encoding.UTF8, "application/json"),
            new ByteArrayContent(contenu) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } },
        };
        using var reponse = await EnvoyerAsync(HttpMethod.Post, $"{ApiEnvoi}?uploadType=multipart&fields=id", corps, annulation);
        var cree = await LireJsonAsync<FichierDrive>(reponse, annulation);
        fichiers[nom] = cree.Id;
    }

    public async Task SupprimerAsync(string nom, CancellationToken annulation = default)
    {
        var fichiers = await FichiersAsync(annulation);
        if (!fichiers.Remove(nom, out var id))
            return;
        using var _ = await EnvoyerAsync(HttpMethod.Delete, $"{Api}/{id}", null, annulation);
    }

    public async Task<IReadOnlyList<string>> ListerAsync(CancellationToken annulation = default) =>
        (await FichiersAsync(annulation)).Keys.ToList();

    /// <summary>Dossier du coffre (créé au premier besoin).</summary>
    private async Task<string> DossierAsync(CancellationToken annulation)
    {
        if (_dossier is not null)
            return _dossier;

        var recherche = $"name = '{NomDossier}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        using (var reponse = await EnvoyerAsync(HttpMethod.Get, $"{Api}?q={Uri.EscapeDataString(recherche)}&fields=files(id,name)&spaces=drive", null, annulation))
        {
            var liste = await LireJsonAsync<ListeDrive>(reponse, annulation);
            if (liste.Files is { Count: > 0 })
                return _dossier = liste.Files[0].Id;
        }

        var description = JsonSerializer.Serialize(new { name = NomDossier, mimeType = "application/vnd.google-apps.folder" });
        using var creation = await EnvoyerAsync(HttpMethod.Post, $"{Api}?fields=id",
            new StringContent(description, Encoding.UTF8, "application/json"), annulation);
        return _dossier = (await LireJsonAsync<FichierDrive>(creation, annulation)).Id;
    }

    /// <summary>Nom → identifiant Drive des fichiers du dossier (lu une fois, puis tenu à jour).</summary>
    private async Task<Dictionary<string, string>> FichiersAsync(CancellationToken annulation)
    {
        if (_fichiers is not null)
            return _fichiers;

        var dossier = await DossierAsync(annulation);
        var fichiers = new Dictionary<string, string>();
        string? page = null;
        do
        {
            var recherche = Uri.EscapeDataString($"'{dossier}' in parents and trashed = false");
            var adresse = $"{Api}?q={recherche}&fields=nextPageToken,files(id,name)&pageSize=1000"
                          + (page is null ? "" : $"&pageToken={Uri.EscapeDataString(page)}");
            using var reponse = await EnvoyerAsync(HttpMethod.Get, adresse, null, annulation);
            var liste = await LireJsonAsync<ListeDrive>(reponse, annulation);
            foreach (var fichier in liste.Files ?? new List<FichierDrive>())
                fichiers.TryAdd(fichier.Name ?? "", fichier.Id);
            page = liste.NextPageToken;
        } while (page is not null);

        return _fichiers = fichiers;
    }

    private async Task<HttpResponseMessage> EnvoyerAsync(HttpMethod methode, string adresse, HttpContent? contenu, CancellationToken annulation)
    {
        using var requete = new HttpRequestMessage(methode, adresse) { Content = contenu };
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _jeton(annulation));
        var reponse = await _http.SendAsync(requete, annulation);
        if (reponse.IsSuccessStatusCode)
            return reponse;

        var texte = await reponse.Content.ReadAsStringAsync(annulation);
        reponse.Dispose();
        throw new HttpRequestException($"Google Drive a refusé la demande ({(int)reponse.StatusCode}) : {ConnexionGoogle.Extrait(texte)}");
    }

    private static async Task<T> LireJsonAsync<T>(HttpResponseMessage reponse, CancellationToken annulation) =>
        JsonSerializer.Deserialize<T>(await reponse.Content.ReadAsStringAsync(annulation))
        ?? throw new HttpRequestException("Réponse de Google Drive illisible.");

    private sealed record FichierDrive([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("name")] string? Name);

    private sealed record ListeDrive(
        [property: JsonPropertyName("files")] List<FichierDrive>? Files,
        [property: JsonPropertyName("nextPageToken")] string? NextPageToken);
}
