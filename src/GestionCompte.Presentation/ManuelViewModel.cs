using CommunityToolkit.Mvvm.ComponentModel;

namespace GestionCompte.Presentation;

/// <summary>Rubrique « Aide » : le manuel d'utilisation, chapitre par chapitre, avec une recherche.</summary>
public sealed partial class ManuelViewModel : ObservableObject
{
    public ManuelViewModel(IReadOnlyList<ChapitreManuel> chapitres)
    {
        Chapitres = chapitres;
        _chapitresAffiches = chapitres;
        _selection = chapitres.FirstOrDefault();
    }

    public IReadOnlyList<ChapitreManuel> Chapitres { get; }

    /// <summary>Chapitres qui contiennent le texte cherché (tous sans recherche).</summary>
    [ObservableProperty] private IReadOnlyList<ChapitreManuel> _chapitresAffiches;

    [ObservableProperty] private ChapitreManuel? _selection;

    [ObservableProperty] private string _recherche = "";

    /// <summary>Résumé de la recherche (« 3 chapitres »), vide sans recherche.</summary>
    [ObservableProperty] private string _resultat = "";

    partial void OnRechercheChanged(string value)
    {
        var mots = Mots(value);
        ChapitresAffiches = mots.Count == 0
            ? Chapitres
            : Chapitres.Where(c => mots.All(m => Normaliser(c.Texte).Contains(m, StringComparison.Ordinal))).ToList();
        Resultat = mots.Count == 0 ? ""
            : ChapitresAffiches.Count switch
            {
                0 => "Aucun chapitre ne contient ce texte.",
                1 => "1 chapitre",
                var n => $"{n} chapitres",
            };
        if (Selection is null || !ChapitresAffiches.Contains(Selection))
            Selection = ChapitresAffiches.FirstOrDefault();
    }

    /// <summary>Mots cherchés, à surligner dans le chapitre affiché.</summary>
    public IReadOnlyList<string> MotsCherches => Mots(Recherche);

    private static List<string> Mots(string texte) =>
        Normaliser(texte).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(m => m.Length >= 2).ToList();

    /// <summary>Minuscules sans accents, pour chercher « echeance » comme « Échéance ».</summary>
    public static string Normaliser(string texte)
    {
        var decompose = texte.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(decompose.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                                               != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(System.Text.NormalizationForm.FormC);
    }
}
