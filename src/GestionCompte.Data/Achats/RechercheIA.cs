using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GestionCompte.Core.Achats;

namespace GestionCompte.Data.Achats;

/// <summary>IA gratuite interrogée par l'application (clé personnelle saisie dans l'application).</summary>
public interface IAssistantIA
{
    SourceOffre Source { get; }

    string Nom { get; }

    /// <summary>Réponse texte à une demande ; <paramref name="avecRecherche"/> : l'IA peut chercher sur internet.</summary>
    Task<string> DemanderAsync(string demande, bool avecRecherche, CancellationToken annulation = default);

    /// <summary>
    /// Comme <see cref="DemanderAsync(string, bool, CancellationToken)"/> ; si <paramref name="repliSansRecherche"/> est faux,
    /// une recherche internet refusée par la clé est une erreur au lieu d'une réponse sans recherche.
    /// </summary>
    Task<string> DemanderAsync(string demande, bool avecRecherche, bool repliSansRecherche, CancellationToken annulation = default) =>
        DemanderAsync(demande, avecRecherche, annulation);

    /// <summary>Vrai si la dernière demande avec recherche a été faite sans recherche internet (refusée par la clé gratuite).</summary>
    bool SansRecherche => false;
}

public static class RechercheOffres
{
    /// <summary>Demande envoyée aux IA : seul le nom du produit cherché part, aucune donnée du budget.</summary>
    public static string Demande(string produit) =>
        "Recherche sur internet les offres actuelles pour acheter ce produit en France : « " + produit.Trim() + " ».\n" +
        "Donne jusqu'à 8 offres de sites marchands différents, prix TTC en euros, livrables en France, du moins cher au plus cher.\n" +
        "Réponds uniquement avec un tableau JSON, sans aucun texte autour, au format :\n" +
        "[{\"site\":\"Nom du site\",\"titre\":\"Nom exact du produit\",\"prix\":123.45,\"url\":\"https://…\",\"remarque\":\"frais de port, occasion, reconditionné, stock…\"}]\n" +
        "N'invente aucune offre ni adresse : n'inclus que des pages trouvées pendant la recherche. Si le produit est d'occasion ou reconditionné, dis-le dans la remarque.";

    public static async Task<IReadOnlyList<OffreTrouvee>> RechercherAsync(IAssistantIA ia, string produit, CancellationToken annulation = default) =>
        // Des offres sans recherche internet seraient inventées : pas de repli.
        Extraire(await ia.DemanderAsync(Demande(produit), avecRecherche: true, repliSansRecherche: false, annulation), ia.Source);

    /// <summary>Offres du tableau JSON contenu dans la réponse (même entouré de texte ou de ```json).</summary>
    public static IReadOnlyList<OffreTrouvee> Extraire(string reponse, SourceOffre source)
    {
        var debut = reponse.IndexOf('[');
        var fin = reponse.LastIndexOf(']');
        if (debut < 0 || fin <= debut)
            return Array.Empty<OffreTrouvee>();

        JsonNode? tableau;
        try
        {
            tableau = JsonNode.Parse(reponse[debut..(fin + 1)], documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            return Array.Empty<OffreTrouvee>();
        }

        var offres = new List<OffreTrouvee>();
        foreach (var element in tableau as JsonArray ?? new JsonArray())
        {
            if (element is not JsonObject o)
                continue;
            var adresse = Texte(o, "url") ?? Texte(o, "lien") ?? "";
            var prix = o["prix"] is JsonValue v
                ? v.TryGetValue<decimal>(out var d) ? d : LecturePrix.Nombre(v.ToString())
                : null;
            if (prix is not > 0 || FusionOffres.Domaine(adresse) is not { } domaine)
                continue;
            offres.Add(new OffreTrouvee
            {
                Site = Texte(o, "site") ?? domaine,
                Titre = Texte(o, "titre") ?? "",
                Prix = decimal.Round(prix.Value, 2),
                Adresse = adresse,
                Remarque = Texte(o, "remarque"),
                Sources = { source },
            });
        }
        return offres;
    }

    private static string? Texte(JsonObject o, string nom) =>
        o[nom] is JsonValue v && v.ToString().Trim() is { Length: > 0 } t ? t : null;
}

/// <summary>Gemini (Google AI Studio) : la recherche Google est intégrée à la réponse.</summary>
public sealed class Gemini : IAssistantIA
{
    /// <summary>Flash-Lite : environ 500 demandes par jour en gratuit, contre une vingtaine pour Flash.</summary>
    public const string ModeleParDefaut = "gemini-flash-lite-latest";

    private readonly HttpClient _http;
    private readonly string _cle;
    private readonly string _modele;

    private readonly Action<string>? _patienter;

    /// <param name="patienter">Prévenu quand l'IA attend la fin d'une limite par minute avant un nouvel essai.</param>
    public Gemini(HttpClient http, string cle, string? modele = null, Action<string>? patienter = null)
    {
        _http = http;
        _patienter = patienter;
        _cle = cle;
        _modele = string.IsNullOrWhiteSpace(modele) ? ModeleParDefaut : modele.Trim();
    }

    public SourceOffre Source => SourceOffre.Gemini;
    public string Nom => "Gemini";

    public bool SansRecherche { get; private set; }

    public Task<string> DemanderAsync(string demande, bool avecRecherche, CancellationToken annulation = default) =>
        DemanderAsync(demande, avecRecherche, repliSansRecherche: true, annulation);

    public async Task<string> DemanderAsync(string demande, bool avecRecherche, bool repliSansRecherche, CancellationToken annulation = default)
    {
        var (statut, texte) = await ErreursIA.DemanderAvecRepliAsync(Nom, avecRecherche, repliSansRecherche,
            (recherche, essais) => ErreursIA.EnvoyerAsync(_http, () => Requete(demande, recherche), annulation, _patienter, Nom, essais),
            sans => SansRecherche = sans);
        if (statut != HttpStatusCode.OK)
            throw new HttpRequestException(ErreursIA.Message(Nom, statut, texte));

        using var json = JsonDocument.Parse(texte);
        var resultat = new StringBuilder();
        if (json.RootElement.TryGetProperty("candidates", out var candidats) && candidats.GetArrayLength() > 0
            && candidats[0].TryGetProperty("content", out var contenu) && contenu.TryGetProperty("parts", out var parties))
        {
            foreach (var partie in parties.EnumerateArray())
                if (partie.TryGetProperty("text", out var t))
                    resultat.Append(t.GetString());
        }
        return resultat.ToString();
    }

    private HttpRequestMessage Requete(string demande, bool avecRecherche)
    {
        var corps = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = demande }),
            }),
        };
        if (avecRecherche)
            corps["tools"] = new JsonArray(new JsonObject { ["google_search"] = new JsonObject() });
        var requete = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(_modele)}:generateContent")
        {
            Content = new StringContent(corps.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        requete.Headers.Add("x-goog-api-key", _cle);
        return requete;
    }
}

/// <summary>Mistral (La Plateforme) : recherche web par l'outil « web_search » des conversations.</summary>
public sealed class Mistral : IAssistantIA
{
    public const string ModeleParDefaut = "mistral-medium-latest";

    private readonly HttpClient _http;
    private readonly string _cle;
    private readonly string _modele;

    private readonly Action<string>? _patienter;

    /// <param name="patienter">Prévenu quand l'IA attend la fin d'une limite par minute avant un nouvel essai.</param>
    public Mistral(HttpClient http, string cle, string? modele = null, Action<string>? patienter = null)
    {
        _http = http;
        _patienter = patienter;
        _cle = cle;
        _modele = string.IsNullOrWhiteSpace(modele) ? ModeleParDefaut : modele.Trim();
    }

    public SourceOffre Source => SourceOffre.Mistral;
    public string Nom => "Mistral";

    public bool SansRecherche { get; private set; }

    public Task<string> DemanderAsync(string demande, bool avecRecherche, CancellationToken annulation = default) =>
        DemanderAsync(demande, avecRecherche, repliSansRecherche: true, annulation);

    public async Task<string> DemanderAsync(string demande, bool avecRecherche, bool repliSansRecherche, CancellationToken annulation = default)
    {
        var (statut, texte) = await ErreursIA.DemanderAvecRepliAsync(Nom, avecRecherche, repliSansRecherche,
            (recherche, essais) => ErreursIA.EnvoyerAsync(_http, () => Requete(demande, recherche), annulation, _patienter, Nom, essais),
            sans => SansRecherche = sans);
        if (statut != HttpStatusCode.OK)
            throw new HttpRequestException(ErreursIA.Message(Nom, statut, texte));

        using var json = JsonDocument.Parse(texte);
        var resultat = new StringBuilder();
        if (json.RootElement.TryGetProperty("outputs", out var sorties))
        {
            foreach (var sortie in sorties.EnumerateArray())
                if (sortie.TryGetProperty("type", out var type) && type.GetString() == "message.output"
                    && sortie.TryGetProperty("content", out var contenu))
                    AjouterContenu(contenu, resultat);
        }
        else if (json.RootElement.TryGetProperty("choices", out var choix) && choix.GetArrayLength() > 0
                 && choix[0].TryGetProperty("message", out var message) && message.TryGetProperty("content", out var contenu))
            AjouterContenu(contenu, resultat);
        return resultat.ToString();
    }

    private HttpRequestMessage Requete(string demande, bool avecRecherche)
    {
        var corps = avecRecherche
            ? new JsonObject
            {
                ["model"] = _modele,
                ["inputs"] = demande,
                ["tools"] = new JsonArray(new JsonObject { ["type"] = "web_search" }),
                ["store"] = false,
            }
            : new JsonObject
            {
                ["model"] = _modele,
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = demande }),
            };
        var requete = new HttpRequestMessage(HttpMethod.Post,
            avecRecherche ? "https://api.mistral.ai/v1/conversations" : "https://api.mistral.ai/v1/chat/completions")
        {
            Content = new StringContent(corps.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cle);
        return requete;
    }

    /// <summary>Le contenu est un texte, ou une liste de morceaux (texte et références des pages consultées).</summary>
    private static void AjouterContenu(JsonElement contenu, StringBuilder resultat)
    {
        if (contenu.ValueKind == JsonValueKind.String)
            resultat.Append(contenu.GetString());
        else if (contenu.ValueKind == JsonValueKind.Array)
            foreach (var morceau in contenu.EnumerateArray())
                if (morceau.TryGetProperty("type", out var t) && t.GetString() == "text" && morceau.TryGetProperty("text", out var texte))
                    resultat.Append(texte.GetString());
    }
}

public static class ErreursIA
{
    /// <summary>Attente avant le premier nouvel essai quand l'IA ne donne pas de délai (Mistral gratuit : 1 demande par seconde).</summary>
    public static TimeSpan AttenteParDefaut { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Attente avant le second nouvel essai : la fin de la minute (limite de tokens par minute).</summary>
    public static TimeSpan AttenteLongue { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Au-delà, on n'attend pas : la limite est au jour.</summary>
    private static readonly TimeSpan AttenteMaximale = TimeSpan.FromSeconds(65);

    /// <summary>
    /// Envoie la demande ; si l'IA répond « trop de demandes », patiente puis réessaie (au plus deux fois : après
    /// quelques secondes, puis après la minute, en prévenant par <paramref name="patienter"/>).
    /// Renvoie le statut (OK en cas de succès) et le texte de la réponse.
    /// </summary>
    public static async Task<(HttpStatusCode Statut, string Texte)> EnvoyerAsync(HttpClient http, Func<HttpRequestMessage> creer,
        CancellationToken annulation, Action<string>? patienter = null, string nom = "", bool nouveauxEssais = true)
    {
        var attenteAnnoncee = false;
        for (var essai = 1; ; essai++)
        {
            using var requete = creer();
            using var reponse = await http.SendAsync(requete, annulation);
            var texte = await reponse.Content.ReadAsStringAsync(annulation);
            if (reponse.IsSuccessStatusCode)
                return (HttpStatusCode.OK, texte);
            if (reponse.StatusCode != HttpStatusCode.TooManyRequests || !nouveauxEssais || essai >= 3 || LimiteDuJour(texte))
                return (reponse.StatusCode, texte);
            var attente = reponse.Headers.RetryAfter?.Delta ?? DelaiDemande(texte) ?? (essai == 1 ? AttenteParDefaut : AttenteLongue);
            if (attente > AttenteMaximale)
                return (reponse.StatusCode, texte);
            if (essai == 2 || attente >= TimeSpan.FromSeconds(3))
            {
                if (attenteAnnoncee)
                    return (reponse.StatusCode, texte);
                attenteAnnoncee = true;
                patienter?.Invoke($"{nom} : limite gratuite par minute atteinte, nouvel essai dans {Math.Ceiling(attente.TotalSeconds)} s…");
            }
            await Task.Delay(attente, annulation);
        }
    }

    /// <summary>
    /// Demande avec recherche internet si voulu ; si la clé la refuse et que le repli est permis, redemande sans recherche
    /// (<paramref name="marquerSansRecherche"/> reçoit alors vrai).
    /// </summary>
    public static async Task<(HttpStatusCode Statut, string Texte)> DemanderAvecRepliAsync(string nom, bool avecRecherche, bool repliSansRecherche,
        Func<bool, bool, Task<(HttpStatusCode Statut, string Texte)>> envoyer, Action<bool> marquerSansRecherche)
    {
        marquerSansRecherche(false);
        if (!avecRecherche)
            return await envoyer(false, true);
        // Avec repli, pas d'attente sur la recherche : on passe tout de suite à la demande sans recherche.
        var avec = await envoyer(true, !repliSansRecherche);
        if (avec.Statut == HttpStatusCode.OK || !repliSansRecherche || !RechercheRefusee(avec.Statut, avec.Texte))
            return avec;
        var sans = await envoyer(false, true);
        if (sans.Statut == HttpStatusCode.OK)
            marquerSansRecherche(true);
        return sans;
    }

    /// <summary>Refus qui peut venir de la recherche internet elle-même (quota de recherche, outil non inclus) et non de la clé.</summary>
    private static bool RechercheRefusee(HttpStatusCode statut, string texte) =>
        statut switch
        {
            // Même un quota du jour peut ne concerner que la recherche : la demande sans recherche le dira sinon.
            HttpStatusCode.TooManyRequests => true,
            HttpStatusCode.BadRequest or HttpStatusCode.Forbidden => !texte.Contains("API key", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    /// <summary>Quota du jour atteint (Gemini indique « PerDay » dans le détail de l'erreur).</summary>
    public static bool LimiteDuJour(string texte) =>
        texte.Contains("PerDay", StringComparison.OrdinalIgnoreCase) || texte.Contains("per day", StringComparison.OrdinalIgnoreCase);

    /// <summary>Délai demandé par Gemini (« "retryDelay": "37s" »).</summary>
    private static TimeSpan? DelaiDemande(string texte)
    {
        var index = texte.IndexOf("\"retryDelay\"", StringComparison.Ordinal);
        if (index < 0)
            return null;
        var debut = texte.IndexOf('"', texte.IndexOf(':', index) + 1) + 1;
        var fin = texte.IndexOf('s', debut);
        return debut > 0 && fin > debut
               && double.TryParse(texte[debut..fin], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secondes)
            ? TimeSpan.FromSeconds(Math.Ceiling(secondes))
            : null;
    }

    /// <summary>Heure locale de remise à zéro des quotas du jour de Gemini (minuit, heure du Pacifique), ex. « demain à 9 h ».</summary>
    public static string RemiseAZeroGemini(DateTime maintenantUtc)
    {
        TimeZoneInfo? pacifique = null;
        foreach (var id in new[] { "America/Los_Angeles", "Pacific Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out pacifique))
                break;
        }
        if (pacifique is null)
            return "demain matin";
        var ici = TimeZoneInfo.ConvertTimeFromUtc(maintenantUtc, pacifique);
        var prochainMinuit = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(ici.Date.AddDays(1), DateTimeKind.Unspecified), pacifique);
        var local = prochainMinuit.ToLocalTime();
        var jour = local.Date == maintenantUtc.ToLocalTime().Date ? "aujourd'hui" : "demain";
        return $"{jour} à {local.Hour} h{(local.Minute > 0 ? local.Minute.ToString("00") : "")}";
    }

    public static string Message(string ia, HttpStatusCode statut, string texte, DateTime? maintenantUtc = null)
    {
        var detail = texte;
        try
        {
            using var json = JsonDocument.Parse(texte);
            var racine = json.RootElement;
            if (racine.TryGetProperty("error", out var erreur))
                racine = erreur;
            if (racine.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                detail = message.GetString() ?? texte;
        }
        catch (JsonException)
        {
        }
        if (detail.Length > 200)
            detail = detail[..200] + "…";

        if (detail.Contains("API key", StringComparison.OrdinalIgnoreCase))
            statut = HttpStatusCode.Unauthorized;
        var raison = $" Réponse de {ia} : « {detail} »";
        return statut switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"{ia} refuse la clé API (vérifiez-la dans Configuration › Intelligence artificielle). {detail}",
            HttpStatusCode.TooManyRequests when LimiteDuJour(texte) =>
                $"{ia} : quota gratuit du jour atteint, de nouveau disponible {RemiseAZeroGemini(maintenantUtc ?? DateTime.UtcNow)}.{raison}",
            HttpStatusCode.TooManyRequests when ia == "Mistral" =>
                $"Mistral : limite gratuite atteinte (1 demande par seconde et un nombre de tokens par minute, voir « Limites » dans la console Mistral) ; réessayez dans une minute.{raison}",
            HttpStatusCode.TooManyRequests => $"{ia} : limite gratuite par minute atteinte, réessayez dans une minute.{raison}",
            HttpStatusCode.NotFound => $"{ia} : modèle introuvable ({detail}).",
            _ => $"{ia} a refusé la demande ({(int)statut}) : {detail}",
        };
    }
}
