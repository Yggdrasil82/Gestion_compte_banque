using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GestionCompte.Presentation;

/// <summary>Source d'un facteur de risque (article de presse).</summary>
public sealed record SourceVeille(string Titre, string Url);

/// <summary>Un facteur de risque de la veille : score indicatif (0 à 100), tendance et sources.</summary>
public sealed record FacteurVeille(string Nom, int Score, string Tendance, string Explication, IReadOnlyList<SourceVeille> Sources)
{
    public string ScoreTexte => $"{Score} %";

    public string Fleche => VeilleMarches.Fleche(Tendance);

    public NiveauVeille Niveau => VeilleMarches.NiveauDe(Score);
}

/// <summary>Score de risque d'un secteur (mêmes noms que les secteurs du module Bourse).</summary>
public sealed record SecteurVeille(string Nom, int Score, string Tendance);

/// <summary>Risque du portefeuille calculé sur ce PC à partir des scores de la veille et de la répartition des titres.</summary>
/// <param name="Score">Moyenne des scores des secteurs, pondérée par leur part (le score global pour les ETF et les secteurs sans score).</param>
/// <param name="PartRisqueEleve">Part du portefeuille (en %) dans des secteurs à risque élevé.</param>
/// <param name="SecteursEleves">Secteurs détenus à risque élevé, du plus lourd au plus léger.</param>
public sealed record ExpositionVeille(int Score, decimal PartRisqueEleve, IReadOnlyList<string> SecteursEleves)
{
    public NiveauVeille Niveau => VeilleMarches.NiveauDe(Score);
}

public enum NiveauVeille
{
    Faible,
    Modere,
    Eleve,
}

/// <summary>
/// Point de la veille des marchés (fichier « veille.json » d'un dépôt GitHub privé, écrit chaque matin par la veille automatique) :
/// score de risque indicatif, tendance, résumé et facteurs. Ce n'est pas une prédiction.
/// </summary>
public sealed record VeilleMarches(DateOnly Date, int Score, int? ScorePrecedent, string Tendance, bool Alerte, string Resume,
    IReadOnlyList<FacteurVeille> Facteurs, IReadOnlyList<SecteurVeille> Secteurs)
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Score à partir duquel le risque est dit élevé (alerte).</summary>
    public const int SeuilEleve = 60;

    /// <summary>Score à partir duquel le risque est dit modéré.</summary>
    public const int SeuilModere = 30;

    public NiveauVeille Niveau => NiveauDe(Score);

    public string ScoreTexte => $"{Score} %";

    public string DateTexte => "Point du " + Date.ToString("dddd d MMMM yyyy", Francais);

    public string TendanceTexte
    {
        get
        {
            var texte = Tendance switch
            {
                "hausse" => "en hausse",
                "baisse" => "en baisse",
                _ => "stable",
            };
            if (ScorePrecedent is { } avant && avant != Score)
                texte += $" ({(Score > avant ? "+" : "−")}{Math.Abs(Score - avant)} points depuis le point précédent)";
            return $"{Fleche(Tendance)} {texte}";
        }
    }

    public static NiveauVeille NiveauDe(int score) =>
        score >= SeuilEleve ? NiveauVeille.Eleve : score >= SeuilModere ? NiveauVeille.Modere : NiveauVeille.Faible;

    public static string Fleche(string tendance) => tendance switch
    {
        "hausse" => "↗",
        "baisse" => "↘",
        _ => "→",
    };

    /// <summary>Score d'un secteur du portefeuille : celui de la veille, sinon le score global (ETF, secteur non classé ou sans score).</summary>
    public int ScoreDe(string secteur) =>
        secteur.StartsWith("ETF", StringComparison.OrdinalIgnoreCase)
            ? Score
            : Secteurs.FirstOrDefault(s => string.Equals(s.Nom, secteur, StringComparison.CurrentCultureIgnoreCase))?.Score ?? Score;

    /// <summary>Croise les scores de la veille avec la répartition des titres (secteur, part en %) ; null sans titre.</summary>
    public ExpositionVeille? Exposer(IEnumerable<(string Secteur, decimal Part)> repartition)
    {
        var lignes = repartition.Where(r => r.Part > 0).ToList();
        var total = lignes.Sum(r => r.Part);
        if (total <= 0)
            return null;
        var score = (int)Math.Round(lignes.Sum(r => r.Part * ScoreDe(r.Secteur)) / total);
        var eleves = lignes.Where(r => ScoreDe(r.Secteur) >= SeuilEleve).OrderByDescending(r => r.Part).ToList();
        return new ExpositionVeille(score, Math.Round(eleves.Sum(r => r.Part) / total * 100, 1), eleves.Select(r => r.Secteur).ToList());
    }

    /// <summary>Lit le fichier de la veille ; les champs absents prennent une valeur neutre, les liens non « https » sont écartés.</summary>
    /// <exception cref="FormatException">Le fichier n'est pas un point de veille lisible.</exception>
    public static VeilleMarches Lire(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var racine = document.RootElement;
            if (racine.ValueKind != JsonValueKind.Object || !DateOnly.TryParseExact(Texte(racine, "date"), "yyyy-MM-dd", out var date))
                throw new FormatException("La date du point de veille est absente.");
            var facteurs = new List<FacteurVeille>();
            if (racine.TryGetProperty("facteurs", out var liste) && liste.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in liste.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.Object))
                {
                    var sources = new List<SourceVeille>();
                    if (f.TryGetProperty("sources", out var s) && s.ValueKind == JsonValueKind.Array)
                        foreach (var source in s.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
                            if (Texte(source, "url") is { } url && LienSur(url))
                                sources.Add(new SourceVeille(Texte(source, "titre") ?? url, url));
                    facteurs.Add(new FacteurVeille(Texte(f, "nom") ?? "Facteur", LireScore(f, "score") ?? 0, LireTendance(f),
                        Texte(f, "explication") ?? "", sources));
                }
            }
            var secteurs = new List<SecteurVeille>();
            if (racine.TryGetProperty("secteurs", out var ls) && ls.ValueKind == JsonValueKind.Array)
                foreach (var x in ls.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
                    if (Texte(x, "nom") is { } nom && LireScore(x, "score") is { } score)
                        secteurs.Add(new SecteurVeille(nom, score, LireTendance(x)));
            return new VeilleMarches(date, LireScore(racine, "score") ?? 0, LireScore(racine, "scorePrecedent"), LireTendance(racine),
                racine.TryGetProperty("alerte", out var a) && a.ValueKind == JsonValueKind.True, Texte(racine, "resume") ?? "",
                facteurs.OrderByDescending(f => f.Score).ToList(), secteurs);
        }
        catch (JsonException e)
        {
            throw new FormatException("Le fichier de la veille n'est pas lisible.", e);
        }
    }

    /// <summary>Seuls les liens https sont gardés (ils s'ouvrent dans le navigateur).</summary>
    public static bool LienSur(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static string? Texte(JsonElement e, string nom) =>
        e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } t ? t.Trim() : null;

    private static int? LireScore(JsonElement e, string nom) =>
        e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)
            ? (int)Math.Round(Math.Clamp(d, 0, 100))
            : null;

    private static string LireTendance(JsonElement e) => Texte(e, "tendance")?.ToLowerInvariant() switch
    {
        "hausse" => "hausse",
        "baisse" => "baisse",
        _ => "stable",
    };
}

/// <summary>Lecture du fichier de la veille dans un dépôt GitHub privé, avec une clé en lecture seule.</summary>
public class ServiceVeille
{
    public const string Fichier = "veille.json";

    private static readonly Regex FormatDepot = new(@"^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$");
    private readonly HttpClient _http;

    public ServiceVeille(HttpClient? http = null)
    {
        _http = http ?? Data.Reseau.Client(TimeSpan.FromSeconds(20));
    }

    /// <summary>Dépôt écrit « propriétaire/nom » (ex. « moncompte/veille-marches »).</summary>
    public static bool DepotValide(string depot) => FormatDepot.IsMatch(depot);

    /// <exception cref="HttpRequestException">Réponse refusée, avec un message en français.</exception>
    public virtual async Task<string> TelechargerAsync(string depot, string cle, CancellationToken annulation = default)
    {
        if (!DepotValide(depot))
            throw new HttpRequestException("Le dépôt doit s'écrire « propriétaire/nom ».");
        using var requete = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{depot}/contents/{Fichier}");
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cle);
        requete.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw+json"));
        requete.Headers.UserAgent.Add(new ProductInfoHeaderValue("MonBudget", "1.0"));
        requete.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var reponse = await _http.SendAsync(requete, annulation);
        if (reponse.IsSuccessStatusCode)
            return await reponse.Content.ReadAsStringAsync(annulation);
        throw new HttpRequestException(reponse.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "La clé GitHub est refusée (expirée ou mal copiée) : enregistrez-en une nouvelle.",
            HttpStatusCode.Forbidden => "La clé GitHub n'a pas le droit de lire ce dépôt (droit « Contents : Read-only » sur ce dépôt).",
            HttpStatusCode.NotFound => "Dépôt ou fichier de veille introuvable : vérifiez le nom du dépôt et que la clé y a accès.",
            _ => $"GitHub a répondu {(int)reponse.StatusCode} ({reponse.ReasonPhrase}).",
        });
    }
}

/// <summary>Dépôt et clé saisis pour la veille ; clé null = garder celle déjà enregistrée.</summary>
public sealed record SaisieVeille(string Depot, string? Cle);

/// <summary>
/// Carte « Veille marchés » du module Bourse : dernier point de la veille, lu dans un dépôt GitHub privé.
/// Le dépôt et la clé (lecture seule) sont gardés sur ce PC, chiffrés par Windows.
/// </summary>
public sealed partial class VeilleMarchesViewModel : ObservableObject
{
    public const string SecretDepot = "veille-depot";
    public const string SecretCle = "veille-cle";

    /// <summary>Page GitHub de création d'une clé limitée à un dépôt (« fine-grained personal access token »).</summary>
    public const string AdresseCle = "https://github.com/settings/personal-access-tokens/new";

    private readonly ISecretsLocaux _secrets;
    private readonly IDialogues _dialogues;
    private readonly ServiceVeille _service;

    public VeilleMarchesViewModel(ISecretsLocaux secrets, IDialogues dialogues, ServiceVeille? service = null)
    {
        _secrets = secrets;
        _dialogues = dialogues;
        _service = service ?? new ServiceVeille();
    }

    public string? Depot => _secrets.Lire(SecretDepot);

    public bool Configuree => Depot is { Length: > 0 } && _secrets.Lire(SecretCle) is { Length: > 0 };

    /// <summary>Ni réglée ni point affiché : la carte explique comment la régler.</summary>
    public bool NonConfiguree => !Configuree && Veille is null;

    public bool AvecBoutons => !NonConfiguree;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvecVeille))]
    [NotifyPropertyChangedFor(nameof(NonConfiguree))]
    [NotifyPropertyChangedFor(nameof(AvecBoutons))]
    private VeilleMarches? _veille;

    public bool AvecVeille => Veille is not null;

    /// <summary>Message sous le titre (lecture en cours, erreur, point trop ancien).</summary>
    [ObservableProperty] private string _etat = "";

    [ObservableProperty] private bool _enCours;

    /// <summary>Détail des facteurs déplié.</summary>
    [ObservableProperty] private bool _detailOuvert;

    /// <summary>Lit le dernier point si la veille est réglée et qu'aucun point n'a encore été lu.</summary>
    public Task ChargerSiBesoinAsync() => Configuree && Veille is null && !EnCours ? ActualiserAsync() : Task.CompletedTask;

    [RelayCommand]
    private async Task ActualiserAsync()
    {
        if (!Configuree || EnCours)
            return;
        EnCours = true;
        Etat = "Lecture de la veille…";
        try
        {
            Veille = VeilleMarches.Lire(await _service.TelechargerAsync(Depot!, _secrets.Lire(SecretCle)!));
            Etat = "";
            DetailOuvert = Veille.Niveau == NiveauVeille.Eleve || Veille.Alerte;
        }
        catch (Exception e) when (e is HttpRequestException or FormatException or TaskCanceledException)
        {
            Etat = e is TaskCanceledException ? "GitHub ne répond pas : réessayez plus tard." : e.Message;
        }
        finally
        {
            EnCours = false;
        }
    }

    /// <summary>Remplace le point affiché (captures d'écran, avec des données inventées).</summary>
    public void Afficher(VeilleMarches veille)
    {
        Veille = veille;
        Etat = "";
    }

    [RelayCommand]
    private async Task Configurer()
    {
        var saisie = _dialogues.DemanderVeille(Depot ?? "", _secrets.Lire(SecretCle) is { Length: > 0 });
        if (saisie is null)
            return;
        var depot = saisie.Depot.Trim();
        if (!ServiceVeille.DepotValide(depot))
        {
            _dialogues.Erreur("Le dépôt doit s'écrire « propriétaire/nom », par exemple « moncompte/veille-marches ».");
            return;
        }
        _secrets.Ecrire(SecretDepot, depot);
        if (saisie.Cle is { Length: > 0 } cle)
            _secrets.Ecrire(SecretCle, cle.Trim());
        Veille = null;
        Notifier();
        await ActualiserAsync();
    }

    [RelayCommand]
    private void Retirer()
    {
        if (!_dialogues.Confirmer("Veille marchés", "Retirer le dépôt et la clé de la veille enregistrés sur ce PC ?"))
            return;
        _secrets.Ecrire(SecretDepot, null);
        _secrets.Ecrire(SecretCle, null);
        Veille = null;
        Etat = "";
        Notifier();
    }

    [RelayCommand]
    private void OuvrirSource(SourceVeille? source)
    {
        if (source is not null && VeilleMarches.LienSur(source.Url))
            _dialogues.OuvrirLien(source.Url);
    }

    [RelayCommand]
    private void BasculerDetail() => DetailOuvert = !DetailOuvert;

    private void Notifier()
    {
        OnPropertyChanged(nameof(Depot));
        OnPropertyChanged(nameof(Configuree));
        OnPropertyChanged(nameof(NonConfiguree));
        OnPropertyChanged(nameof(AvecBoutons));
    }
}
