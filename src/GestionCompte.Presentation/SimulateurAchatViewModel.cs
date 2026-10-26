using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GestionCompte.Core;
using GestionCompte.Core.Bourse;

namespace GestionCompte.Presentation;

/// <summary>Une ligne du résultat du simulateur.</summary>
public sealed record LigneSimulation(string Libelle, string Texte, bool Important = false, bool Negatif = false);

/// <summary>
/// Simulateur d'achat du module Bourse : prix d'achat et de revente souhaités, frais d'entrée et de sortie, taxe sur les
/// transactions financières et impôt, pour connaître le gain potentiel et le prix de revente minimum.
/// </summary>
public sealed partial class SimulateurAchatViewModel : ObservableObject
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");
    private readonly Func<IReadOnlyList<(string Nom, decimal? Cours)>> _titres;
    private readonly int _annee;

    /// <param name="titres">Titres détenus et idées, avec leur dernier cours (pour pré-remplir le prix d'achat).</param>
    public SimulateurAchatViewModel(Func<IReadOnlyList<(string Nom, decimal? Cours)>> titres, int annee)
    {
        _titres = titres;
        _annee = annee;
        Calculer();
    }

    public static IReadOnlyList<string> NomsEnveloppes { get; } = new[] { "Compte-titres", "PEA" };

    public IReadOnlyList<string> Enveloppes => NomsEnveloppes;

    /// <summary>Noms proposés : titres détenus et idées d'investissement.</summary>
    public IReadOnlyList<string> Noms => _titres().Select(t => t.Nom).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();

    public void ActualiserNoms() => OnPropertyChanged(nameof(Noms));

    [ObservableProperty] private string _titre = "";

    /// <summary>Un titre choisi dans la liste pré-remplit le prix d'achat avec son dernier cours.</summary>
    partial void OnTitreChanged(string value)
    {
        var cours = _titres().FirstOrDefault(t => string.Equals(t.Nom, value?.Trim(), StringComparison.CurrentCultureIgnoreCase)).Cours;
        if (cours is not null)
            PrixAchat = Math.Round(cours.Value, 2);
    }

    [ObservableProperty] private decimal? _quantite;
    [ObservableProperty] private decimal? _montant;
    [ObservableProperty] private decimal? _prixAchat;
    [ObservableProperty] private decimal? _prixVente;
    [ObservableProperty] private decimal? _fraisAchat;
    [ObservableProperty] private decimal? _fraisVente;
    [ObservableProperty] private bool _ttf;
    [ObservableProperty] private int _indexEnveloppe;

    partial void OnQuantiteChanged(decimal? value) => Calculer();
    partial void OnMontantChanged(decimal? value) => Calculer();
    partial void OnPrixAchatChanged(decimal? value) => Calculer();
    partial void OnPrixVenteChanged(decimal? value) => Calculer();
    partial void OnFraisAchatChanged(decimal? value) => Calculer();
    partial void OnFraisVenteChanged(decimal? value) => Calculer();
    partial void OnTtfChanged(bool value) => Calculer();
    partial void OnIndexEnveloppeChanged(int value) => Calculer();

    private EnveloppeBourse Enveloppe => IndexEnveloppe == 1 ? EnveloppeBourse.Pea : EnveloppeBourse.CompteTitres;

    /// <summary>Nombre de titres : saisi, ou montant à investir ÷ prix d'achat (Trade Republic achète des fractions).</summary>
    public decimal? QuantiteRetenue => Quantite is > 0 ? Quantite
        : Montant is > 0 && PrixAchat is > 0 ? Math.Round(Montant.Value / PrixAchat.Value, 6) : null;

    public ResultatSimulationAchat? Resultat { get; private set; }

    public IReadOnlyList<LigneSimulation> Lignes { get; private set; } = Array.Empty<LigneSimulation>();

    public bool AvecResultat => Resultat is not null;

    public string TexteGain => Resultat is null ? "" : $"{Signe(Resultat.GainApresImpot)} €";

    public bool GainPositif => Resultat is null || Resultat.GainApresImpot >= 0;

    public string TexteEquilibre => Resultat is null ? ""
        : $"Revendre au moins {Montants.Formater(Resultat.PrixEquilibre)} € par titre pour couvrir les frais.";

    public string AideFraisAchat =>
        $"Vide : {Montants.Formater(SimulationAchat.FraisAchatParDefaut(Enveloppe, Resultat?.MontantAchat ?? 0))} € (tarif Trade Republic)";

    public string Consigne => "Indiquez le prix d'achat, le prix de revente et le nombre de titres (ou le montant à investir).";

    private void Calculer()
    {
        var quantite = QuantiteRetenue;
        Resultat = quantite is > 0 && PrixAchat is > 0 && PrixVente is >= 0
            ? SimulationAchat.Calculer(new DemandeSimulationAchat(quantite.Value, PrixAchat.Value, PrixVente!.Value, Enveloppe, Ttf, _annee,
                FraisAchat, FraisVente))
            : null;
        Lignes = Resultat is { } r ? Detailler(r, quantite!.Value) : Array.Empty<LigneSimulation>();
        foreach (var nom in new[]
                 {
                     nameof(QuantiteRetenue), nameof(Resultat), nameof(Lignes), nameof(AvecResultat), nameof(TexteGain), nameof(GainPositif),
                     nameof(TexteEquilibre), nameof(AideFraisAchat), nameof(NoteImpot),
                 })
            OnPropertyChanged(nom);
    }

    private IReadOnlyList<LigneSimulation> Detailler(ResultatSimulationAchat r, decimal quantite)
    {
        var lignes = new List<LigneSimulation>
        {
            new($"Achat de {quantite.ToString(quantite == decimal.Truncate(quantite) ? "N0" : "0.######", Francais)} titre(s)",
                $"{Montants.Formater(r.MontantAchat)} €"),
            new("Frais d'achat", $"{Montants.Formater(r.FraisAchat)} €"),
        };
        if (r.Ttf > 0)
            lignes.Add(new("Taxe sur les transactions financières (0,4 %)", $"{Montants.Formater(r.Ttf)} €"));
        lignes.Add(new("Coût total", $"{Montants.Formater(r.CoutTotal)} €", Important: true));
        lignes.Add(new("Revente", $"{Montants.Formater(r.MontantVente)} €"));
        lignes.Add(new("Frais de vente", $"−{Montants.Formater(r.FraisVente)} €"));
        lignes.Add(new("Gain net de frais",
            $"{Signe(r.GainNet)} €" + (r.GainPourcent is { } p ? $" ({Signe(p)} %)" : ""), Important: true, Negatif: r.GainNet < 0));
        lignes.Add(Enveloppe == EnveloppeBourse.Pea
            ? new("Impôt (PEA)", "0,00 €")
            : new($"Flat tax {_annee} ({(r.TauxImpot * 100).ToString("0.#", Francais)} %)",
                r.Impot == 0 ? "0,00 €" : $"−{Montants.Formater(r.Impot)} €"));
        return lignes;
    }

    public string NoteImpot => Enveloppe == EnveloppeBourse.Pea
        ? "PEA : pas d'impôt tant que l'argent reste dans le PEA ; après 5 ans, un retrait ne paie que les prélèvements sociaux (18,6 %)."
        : "Compte-titres : flat tax sur la plus-value (prix de vente net de frais − prix d'achat frais et taxe compris), 0 en cas de perte.";

    private static string Signe(decimal valeur) => (valeur > 0 ? "+" : "") + Montants.Formater(valeur);
}
