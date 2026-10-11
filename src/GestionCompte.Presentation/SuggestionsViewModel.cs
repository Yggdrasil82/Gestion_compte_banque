using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GestionCompte.Presentation;

/// <summary>Une suggestion envoyée par le formulaire Google.</summary>
public sealed record Suggestion(DateTime? Date, string DateTexte, string Version, string Texte, bool Nouvelle)
{
    public string Entete => string.IsNullOrWhiteSpace(Version) ? DateTexte : $"{DateTexte} · version {Version}";
}

/// <summary>
/// « Suggérer une amélioration » ouvre un formulaire Google (version de l'application déjà remplie). Chez l'auteur de
/// l'application, les réponses (feuille Google Sheets publiée en CSV) sont lues à l'ouverture et listées dans l'onglet
/// Suggestions.
/// </summary>
public sealed partial class SuggestionsViewModel : ObservableObject
{
    /// <summary>Lien prérempli du formulaire ; « 0.0.0 » y est remplacé par la version de l'application. Vide = pas de lien.</summary>
    public const string FormulaireParDefaut =
        "https://docs.google.com/forms/d/e/1FAIpQLSfj2SpvczgseZ6e38JCpYITLnATG1omMMemjvcm5k1xm2iuhg/viewform?usp=pp_url&entry.256020164=0.0.0";

    private const string VersionModele = "0.0.0";

    private readonly ApparenceViewModel _apparence;
    private readonly IDialogues _dialogues;
    private readonly string _version;
    private readonly string _formulaire;
    private readonly Func<string, Task<string>> _telecharger;

    public SuggestionsViewModel(ApparenceViewModel apparence, IDialogues dialogues, string version, string? formulaire = null,
        Func<string, Task<string>>? telecharger = null)
    {
        _apparence = apparence;
        _dialogues = dialogues;
        _version = version;
        _formulaire = formulaire ?? FormulaireParDefaut;
        _telecharger = telecharger ?? TelechargerAsync;
        _lienReponses = apparence.LienReponsesSuggestions;
    }

    /// <summary>Le lien « Suggérer une amélioration » est disponible.</summary>
    public bool FormulaireDisponible => _formulaire.Length > 0;

    [RelayCommand]
    private void Suggerer()
    {
        if (FormulaireDisponible)
            _dialogues.OuvrirLien(_formulaire.Replace(VersionModele, Uri.EscapeDataString(_version)));
    }

    /// <summary>Lien CSV des réponses, saisi dans la Configuration.</summary>
    [ObservableProperty] private string _lienReponses;

    /// <summary>L'onglet Suggestions est affiché (lien des réponses réglé).</summary>
    public bool OngletDisponible => _apparence.LienReponsesSuggestions.Length > 0;

    public IReadOnlyList<Suggestion> Liste { get; private set; } = Array.Empty<Suggestion>();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TitreNavigation))] private int _nombreNouvelles;

    [ObservableProperty] private string _etat = "";

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ActualiserCommand))] private bool _enCours;

    public string TitreNavigation => NombreNouvelles > 0 ? $"Suggestions ({NombreNouvelles})" : "Suggestions";

    public bool Vide => Liste.Count == 0;

    /// <summary>Enregistre le lien des réponses et les lit aussitôt.</summary>
    [RelayCommand]
    private async Task EnregistrerLien()
    {
        var lien = (LienReponses ?? "").Trim();
        if (lien.Length > 0 && !lien.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _dialogues.Erreur("Le lien doit commencer par https:// (Google Sheets › Fichier › Partager › Publier sur le web › format .csv).");
            return;
        }
        _apparence.LienReponsesSuggestions = lien;
        LienReponses = lien;
        OnPropertyChanged(nameof(OngletDisponible));
        if (lien.Length == 0)
        {
            Afficher(Array.Empty<Suggestion>());
            Etat = "";
            return;
        }
        await ActualiserAsync();
    }

    private bool PeutActualiser() => !EnCours;

    [RelayCommand(CanExecute = nameof(PeutActualiser))]
    public async Task ActualiserAsync()
    {
        var lien = _apparence.LienReponsesSuggestions;
        if (lien.Length == 0 || EnCours)
            return;
        EnCours = true;
        Etat = "Lecture des suggestions…";
        try
        {
            var csv = await _telecharger(lien);
            Afficher(Lire(csv, _apparence.SuggestionsVuesJusquA));
            Etat = Liste.Count == 0
                ? "Aucune suggestion pour l'instant."
                : $"{Liste.Count} suggestion{(Liste.Count > 1 ? "s" : "")}, lues le {DateTime.Now:dd/MM à HH:mm}.";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or FormatException or IOException)
        {
            Etat = $"Les suggestions n'ont pas pu être lues : {e.Message}";
        }
        finally
        {
            EnCours = false;
        }
    }

    /// <summary>Onglet ouvert : les suggestions affichées sont désormais vues (elles restent marquées jusqu'au prochain affichage).</summary>
    public void MarquerVues()
    {
        if (Liste.FirstOrDefault(s => s.Date is not null)?.Date is { } derniere
            && (_apparence.SuggestionsVuesJusquA is null || derniere > _apparence.SuggestionsVuesJusquA))
            _apparence.SuggestionsVuesJusquA = derniere;
        NombreNouvelles = 0;
    }

    /// <summary>Affiche des réponses déjà lues (captures d'écran avec des suggestions d'exemple).</summary>
    public void AfficherReponses(string csv)
    {
        OnPropertyChanged(nameof(OngletDisponible));
        Afficher(Lire(csv, _apparence.SuggestionsVuesJusquA));
        Etat = $"{Liste.Count} suggestions, lues le 15/11 à 09:41.";
    }

    private void Afficher(IReadOnlyList<Suggestion> liste)
    {
        Liste = liste;
        NombreNouvelles = liste.Count(s => s.Nouvelle);
        OnPropertyChanged(nameof(Liste));
        OnPropertyChanged(nameof(Vide));
    }

    private static async Task<string> TelechargerAsync(string lien)
    {
        using var http = Data.Reseau.Client(TimeSpan.FromSeconds(20));
        return await http.GetStringAsync(lien);
    }

    /// <summary>
    /// Lit la feuille des réponses (CSV publié par Google Sheets) : date de la réponse en première colonne, puis les
    /// colonnes « suggestion » et « version » reconnues à leur titre. Les plus récentes d'abord.
    /// </summary>
    public static IReadOnlyList<Suggestion> Lire(string csv, DateTime? vuesJusquA)
    {
        var lignes = LireCsv(csv);
        if (lignes.Count == 0)
            return Array.Empty<Suggestion>();
        if (lignes[0].Any(c => c.TrimStart().StartsWith("<", StringComparison.Ordinal)))
            throw new FormatException("le lien ne donne pas un fichier CSV (choisissez le format .csv en publiant la feuille).");

        var titres = lignes[0].Select(t => t.Trim().ToLowerInvariant()).ToList();
        var colonneTexte = titres.FindIndex(t => t.Contains("suggestion"));
        var colonneVersion = titres.FindIndex(t => t.Contains("version"));
        if (colonneTexte < 0)
            colonneTexte = Math.Min(1, titres.Count - 1);

        var suggestions = new List<Suggestion>();
        foreach (var ligne in lignes.Skip(1))
        {
            var texte = Colonne(ligne, colonneTexte).Trim();
            if (texte.Length == 0)
                continue;
            var dateTexte = Colonne(ligne, 0).Trim();
            var date = LireDate(dateTexte);
            suggestions.Add(new Suggestion(date, date?.ToString("dd/MM/yyyy à HH:mm", CultureInfo.GetCultureInfo("fr-FR")) ?? dateTexte,
                colonneVersion >= 0 ? Colonne(ligne, colonneVersion).Trim() : "", texte,
                date is { } d && (vuesJusquA is null || d > vuesJusquA)));
        }
        return suggestions.OrderByDescending(s => s.Date ?? DateTime.MinValue).ToList();
    }

    private static string Colonne(IReadOnlyList<string> ligne, int index) => index >= 0 && index < ligne.Count ? ligne[index] : "";

    private static DateTime? LireDate(string texte)
    {
        string[] formats = { "dd/MM/yyyy HH:mm:ss", "d/M/yyyy H:mm:ss", "dd/MM/yyyy HH:mm", "yyyy-MM-dd HH:mm:ss", "M/d/yyyy H:mm:ss" };
        return DateTime.TryParseExact(texte, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    /// <summary>CSV standard : champs entre guillemets possibles, avec virgules, guillemets doublés et retours à la ligne.</summary>
    private static List<List<string>> LireCsv(string csv)
    {
        var lignes = new List<List<string>>();
        var ligne = new List<string>();
        var champ = new StringBuilder();
        var entreGuillemets = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (entreGuillemets)
            {
                if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    champ.Append('"');
                    i++;
                }
                else if (c == '"')
                    entreGuillemets = false;
                else
                    champ.Append(c);
                continue;
            }
            switch (c)
            {
                case '"':
                    entreGuillemets = true;
                    break;
                case ',':
                    ligne.Add(champ.ToString());
                    champ.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    ligne.Add(champ.ToString());
                    champ.Clear();
                    lignes.Add(ligne);
                    ligne = new List<string>();
                    break;
                default:
                    champ.Append(c);
                    break;
            }
        }
        if (champ.Length > 0 || ligne.Count > 0)
        {
            ligne.Add(champ.ToString());
            lignes.Add(ligne);
        }
        return lignes.Where(l => l.Any(c => c.Length > 0)).ToList();
    }
}
