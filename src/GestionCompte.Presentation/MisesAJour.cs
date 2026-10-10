using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GestionCompte.Presentation;

/// <summary>Version publiée sur GitHub (page « Releases » du dépôt), avec son exe.</summary>
/// <param name="Sha256">Empreinte de l'exe donnée par GitHub (en hexadécimal), pour vérifier le téléchargement ; null si absente.</param>
public sealed record VersionPubliee(Version Numero, string Notes, string AdresseExe, long Taille, string? Sha256);

/// <param name="Nouveautes">Nouveautés de toutes les versions plus récentes que celle installée, la plus récente en premier.</param>
public sealed record MiseAJourDisponible(VersionPubliee Derniere, string Nouveautes);

/// <summary>
/// Mises à jour de l'application : les versions validées sont publiées sur la page « Releases » du dépôt GitHub (public) ;
/// l'application y cherche une version plus récente, télécharge l'exe et le met à la place de l'actuel.
/// Les données (Documents\GestionCompte) ne sont jamais touchées.
/// </summary>
public sealed class ServiceMisesAJour
{
    public const string Depot = "Yggdrasil82/Gestion_compte_banque";
    public const string NomExe = "GestionCompte.exe";

    private readonly HttpClient _http;

    public ServiceMisesAJour(HttpClient? http = null)
    {
        _http = http ?? Data.Reseau.Client(TimeSpan.FromMinutes(5));
    }

    /// <summary>Version à trois chiffres (2.8.0), pour comparer la version de l'exe (2.8.0.0) et celle de GitHub.</summary>
    public static Version Normaliser(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));

    /// <summary>Cherche une version plus récente que <paramref name="actuelle"/> ; null si l'application est à jour.</summary>
    public async Task<MiseAJourDisponible?> ChercherAsync(Version actuelle, CancellationToken annulation = default)
    {
        using var requete = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Depot}/releases?per_page=30");
        requete.Headers.UserAgent.Add(new ProductInfoHeaderValue("MonBudget", Normaliser(actuelle).ToString()));
        requete.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var reponse = await _http.SendAsync(requete, annulation);
        if (!reponse.IsSuccessStatusCode)
            throw new HttpRequestException($"GitHub a répondu {(int)reponse.StatusCode} ({reponse.ReasonPhrase}).");

        var versions = Lire(await reponse.Content.ReadAsStringAsync(annulation));
        var plusRecentes = versions.Where(v => v.Numero > Normaliser(actuelle)).OrderByDescending(v => v.Numero).ToList();
        if (plusRecentes.Count == 0)
            return null;

        var nouveautes = string.Join("\n\n", plusRecentes.Select(v =>
            $"Version {v.Numero}\n{(string.IsNullOrWhiteSpace(v.Notes) ? "Corrections et améliorations." : v.Notes.Trim())}"));
        return new MiseAJourDisponible(plusRecentes[0], nouveautes);
    }

    /// <summary>Versions publiées (hors brouillons et préversions) qui ont un exe.</summary>
    public static IReadOnlyList<VersionPubliee> Lire(string json)
    {
        var versions = new List<VersionPubliee>();
        using var document = JsonDocument.Parse(json);
        foreach (var publication in document.RootElement.EnumerateArray())
        {
            if (Booleen(publication, "draft") || Booleen(publication, "prerelease"))
                continue;
            var etiquette = Texte(publication, "tag_name")?.TrimStart('v', 'V');
            if (!Version.TryParse(etiquette, out var numero))
                continue;
            if (!publication.TryGetProperty("assets", out var fichiers) || fichiers.ValueKind != JsonValueKind.Array)
                continue;
            var exe = fichiers.EnumerateArray().FirstOrDefault(f => string.Equals(Texte(f, "name"), NomExe, StringComparison.OrdinalIgnoreCase));
            if (exe.ValueKind != JsonValueKind.Object || Texte(exe, "browser_download_url") is not { } adresse)
                continue;
            var taille = exe.TryGetProperty("size", out var t) && t.TryGetInt64(out var n) ? n : 0;
            var empreinte = Texte(exe, "digest") is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest["sha256:".Length..]
                : null;
            versions.Add(new VersionPubliee(Normaliser(numero), Texte(publication, "body") ?? "", adresse, taille, empreinte));
        }
        return versions;
    }

    /// <summary>
    /// Télécharge l'exe de la nouvelle version dans <paramref name="dossier"/> (« GestionCompte.nouveau.exe ») et vérifie
    /// sa taille et son empreinte. Renvoie le chemin du fichier téléchargé.
    /// </summary>
    public async Task<string> TelechargerAsync(VersionPubliee version, string dossier, IProgress<double>? progression = null,
        CancellationToken annulation = default)
    {
        var chemin = Path.Combine(dossier, "GestionCompte.nouveau.exe");
        using var requete = new HttpRequestMessage(HttpMethod.Get, version.AdresseExe);
        requete.Headers.UserAgent.Add(new ProductInfoHeaderValue("MonBudget", version.Numero.ToString()));
        using var reponse = await _http.SendAsync(requete, HttpCompletionOption.ResponseHeadersRead, annulation);
        reponse.EnsureSuccessStatusCode();
        var total = reponse.Content.Headers.ContentLength ?? version.Taille;

        try
        {
            await using (var source = await reponse.Content.ReadAsStreamAsync(annulation))
            await using (var cible = File.Create(chemin))
            {
                var tampon = new byte[81920];
                long lus = 0;
                int n;
                while ((n = await source.ReadAsync(tampon, annulation)) > 0)
                {
                    await cible.WriteAsync(tampon.AsMemory(0, n), annulation);
                    lus += n;
                    if (total > 0)
                        progression?.Report((double)lus / total);
                }
            }

            var taille = new FileInfo(chemin).Length;
            if (version.Taille > 0 && taille != version.Taille)
                throw new InvalidDataException($"Téléchargement incomplet ({taille} octets au lieu de {version.Taille}).");
            if (version.Sha256 is { } attendue)
            {
                await using var lecture = File.OpenRead(chemin);
                var empreinte = Convert.ToHexString(await SHA256.HashDataAsync(lecture, annulation));
                if (!string.Equals(empreinte, attendue, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Le fichier téléchargé ne correspond pas à celui publié (empreinte différente).");
            }
            return chemin;
        }
        catch
        {
            try { File.Delete(chemin); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>
    /// Met le nouvel exe à la place de l'actuel. Windows permet de renommer un exe en cours d'exécution :
    /// l'actuel devient « GestionCompte.ancien.exe » (supprimé au prochain lancement). En cas d'échec, rien ne change.
    /// </summary>
    public static void Remplacer(string exeActuel, string nouveau)
    {
        var ancien = CheminAncien(exeActuel);
        if (File.Exists(ancien))
            File.Delete(ancien);
        File.Move(exeActuel, ancien);
        try
        {
            File.Move(nouveau, exeActuel);
        }
        catch
        {
            File.Move(ancien, exeActuel);
            throw;
        }
    }

    /// <summary>Supprime l'exe de la version précédente laissé par une mise à jour (ignoré s'il est encore utilisé).</summary>
    public static void NettoyerAncienneVersion(string exeActuel)
    {
        try
        {
            var ancien = CheminAncien(exeActuel);
            if (File.Exists(ancien))
                File.Delete(ancien);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string CheminAncien(string exe) =>
        Path.Combine(Path.GetDirectoryName(exe) ?? ".", Path.GetFileNameWithoutExtension(exe) + ".ancien.exe");

    private static string? Texte(JsonElement element, string nom) =>
        element.TryGetProperty(nom, out var valeur) && valeur.ValueKind == JsonValueKind.String ? valeur.GetString() : null;

    private static bool Booleen(JsonElement element, string nom) =>
        element.TryGetProperty(nom, out var valeur) && valeur.ValueKind == JsonValueKind.True;
}

/// <summary>Carte « Mises à jour » de la Configuration, et vérification au démarrage.</summary>
public sealed partial class MisesAJourViewModel : ObservableObject
{
    private readonly ServiceMisesAJour _service;
    private readonly IDialogues _dialogues;
    private readonly Version _actuelle;
    private readonly string? _exe;

    /// <param name="exe">Chemin de l'exe à remplacer ; null quand l'application n'est pas lancée depuis GestionCompte.exe (tests).</param>
    public MisesAJourViewModel(ServiceMisesAJour service, IDialogues dialogues, Version actuelle, string? exe)
    {
        _service = service;
        _dialogues = dialogues;
        _actuelle = ServiceMisesAJour.Normaliser(actuelle);
        _exe = exe;
        _statut = exe is null ? "Mises à jour disponibles seulement depuis GestionCompte.exe." : "";
    }

    public string VersionActuelle => $"Version installée : {_actuelle}";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RechercherCommand))]
    private bool _enCours;

    [ObservableProperty] private string _statut;

    /// <summary>La nouvelle version est en place : l'application doit être relancée depuis ce chemin.</summary>
    public event EventHandler<string>? RedemarrageDemande;

    /// <summary>Vérification discrète à l'ouverture : rien n'est affiché si GitHub ne répond pas ou si l'application est à jour.</summary>
    public Task VerifierAuDemarrageAsync() => VerifierAsync(silencieux: true);

    private bool PeutRechercher() => !EnCours && _exe is not null;

    [RelayCommand(CanExecute = nameof(PeutRechercher))]
    private Task Rechercher() => VerifierAsync(silencieux: false);

    private async Task VerifierAsync(bool silencieux)
    {
        if (_exe is null || EnCours)
            return;

        EnCours = true;
        try
        {
            Statut = "Recherche d'une nouvelle version…";
            MiseAJourDisponible? disponible;
            try
            {
                disponible = await _service.ChercherAsync(_actuelle);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
            {
                Statut = "Impossible de vérifier les mises à jour (pas de connexion internet ?).";
                if (!silencieux)
                    _dialogues.Erreur($"Les mises à jour n'ont pas pu être vérifiées.\n\n{e.Message}");
                return;
            }

            if (disponible is null)
            {
                Statut = $"Vous avez la dernière version ({_actuelle}). Vérifié le {DateTime.Now:dd/MM/yyyy à HH:mm}.";
                return;
            }

            var numero = disponible.Derniere.Numero;
            Statut = $"Version {numero} disponible.";
            if (!_dialogues.ProposerMiseAJour(numero.ToString(), _actuelle.ToString(), disponible.Nouveautes))
            {
                Statut = $"Version {numero} disponible : « Rechercher une mise à jour » pour l'installer.";
                return;
            }

            string telecharge;
            try
            {
                var progression = new Progress<double>(p => Statut = $"Téléchargement de la version {numero}… {p:P0}");
                telecharge = await _service.TelechargerAsync(disponible.Derniere, Path.GetDirectoryName(_exe)!, progression);
                ServiceMisesAJour.Remplacer(_exe, telecharge);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException
                                          or UnauthorizedAccessException or InvalidDataException)
            {
                Statut = $"La version {numero} n'a pas pu être installée.";
                _dialogues.Erreur($"La mise à jour n'a pas pu être installée ; la version {_actuelle} reste en place.\n\n{e.Message}" +
                                  (e is UnauthorizedAccessException
                                      ? "\n\nLe dossier de l'application est protégé : copiez GestionCompte.exe sur le Bureau, puis réessayez."
                                      : ""));
                return;
            }

            Statut = $"Version {numero} installée : redémarrage…";
            RedemarrageDemande?.Invoke(this, _exe);
        }
        finally
        {
            EnCours = false;
        }
    }
}
