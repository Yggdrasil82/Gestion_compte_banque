using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Core;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;
using GestionCompte.Data;

namespace GestionCompte.Presentation;

/// <summary>Période proposée dans la liste du bilan : une année civile ou les 12 derniers mois.</summary>
public sealed record ChoixBilan(string Libelle, string Titre, PeriodeMois Debut, PeriodeMois Fin)
{
    public override string ToString() => Libelle;
}

/// <summary>
/// Module « Bilan » : totaux d'une année (ou des 12 derniers mois), graphique mois par mois, postes de dépenses
/// comparés à l'année d'avant, pistes d'économie, exports Excel et PDF.
/// </summary>
public sealed partial class BilanViewModel : ObservableObject
{
    public const string DouzeDerniersMois = "12 derniers mois";

    private readonly CompteBancaire _compte;
    private readonly PeriodeMois _moisDuJour;
    private readonly IDialogues _dialogues;

    public BilanViewModel(CompteBancaire compte, PeriodeMois moisDuJour, IDialogues dialogues)
    {
        _compte = compte;
        _moisDuJour = moisDuJour;
        _dialogues = dialogues;
        Recalculer();
    }

    [ObservableProperty] private IReadOnlyList<ChoixBilan> _choix = Array.Empty<ChoixBilan>();

    [ObservableProperty] private ChoixBilan? _selection;

    partial void OnSelectionChanged(ChoixBilan? value) => Calculer();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExporterExcelCommand), nameof(ExporterPdfCommand))]
    private ResultatBilan? _resultat;

    [ObservableProperty] private string _titre = "Bilan";
    [ObservableProperty] private string _etendue = "";
    [ObservableProperty] private string _tauxEpargne = "";
    [ObservableProperty] private string _variation = "";
    [ObservableProperty] private bool _variationNegative;
    [ObservableProperty] private string _comparaisonRevenus = "";
    [ObservableProperty] private string _comparaisonDepenses = "";
    [ObservableProperty] private string _comparaisonEpargne = "";
    [ObservableProperty] private IReadOnlyList<MoisBilanViewModel> _mois = Array.Empty<MoisBilanViewModel>();
    [ObservableProperty] private IReadOnlyList<PosteBilanViewModel> _postes = Array.Empty<PosteBilanViewModel>();
    [ObservableProperty] private IReadOnlyList<PisteEconomie> _pistes = Array.Empty<PisteEconomie>();
    [ObservableProperty] private string _gainPossible = "";
    [ObservableProperty] private string _messageExport = "";

    public bool Vide => Resultat is null or { Vide: true };

    public bool AvecMois => !Vide;

    public bool AvecPistes => Pistes.Count > 0;

    /// <summary>Recalcule la liste des périodes et le bilan affiché (après une modification des mois).</summary>
    public void Recalculer()
    {
        var douze = Bilan.DouzeDerniersMois(_moisDuJour);
        var choix = new List<ChoixBilan>
        {
            new(DouzeDerniersMois, "Bilan des 12 derniers mois", douze.Debut, douze.Fin),
        };
        choix.AddRange(Bilan.Annees(_compte).Select(a =>
            new ChoixBilan($"Année {a}", $"Bilan {a}", new PeriodeMois(a, 1), new PeriodeMois(a, 12))));

        var precedente = Selection?.Libelle;
        Choix = choix;
        // Par défaut : l'année en cours, sinon la plus récente.
        var selection = choix.FirstOrDefault(c => c.Libelle == precedente)
            ?? choix.FirstOrDefault(c => c.Libelle == $"Année {_moisDuJour.Annee}")
            ?? choix.Skip(1).FirstOrDefault()
            ?? choix[0];
        if (selection == Selection)
            Calculer();
        else
            Selection = selection;
    }

    private void Calculer()
    {
        if (Selection is not { } choix)
            return;

        var bilan = Bilan.Calculer(_compte, choix.Debut, choix.Fin);
        Resultat = bilan;
        Titre = choix.Titre;
        Etendue = bilan.Etendue;
        TauxEpargne = bilan.TauxEpargne is { } taux ? $"{Pourcentage(taux, 1)} des revenus" : "";
        Variation = $"{(bilan.Variation >= 0 ? "+" : "")}{Euros(bilan.Variation)} depuis le début ({Euros(bilan.SoldeDebut)})";
        VariationNegative = bilan.Variation < 0;

        var nombre = Math.Max(1, bilan.Mois.Count);
        ComparaisonRevenus = Comparer(bilan.Revenus / nombre, bilan.Precedent?.RevenusMensuels, bilan.Precedent);
        ComparaisonDepenses = Comparer(bilan.Depenses / nombre, bilan.Precedent?.DepensesMensuelles, bilan.Precedent);
        ComparaisonEpargne = Comparer(bilan.Epargne / nombre, bilan.Precedent?.EpargneMensuelle, bilan.Precedent);

        Mois = bilan.Mois.Select(m => new MoisBilanViewModel(m)).ToList();
        Postes = bilan.Postes.Select(p => new PosteBilanViewModel(p, bilan.Precedent is not null)).ToList();
        Pistes = bilan.Pistes;
        var gain = bilan.Pistes.Sum(p => p.GainAnnuel ?? 0m);
        GainPossible = gain > 0 ? $"jusqu'à {Euros(gain)} par an en suivant les pistes chiffrées" : "";
        MessageExport = "";
        OnPropertyChanged(nameof(Vide));
        OnPropertyChanged(nameof(AvecMois));
        OnPropertyChanged(nameof(AvecPistes));
    }

    /// <summary>Ex. « 3 406,42 € par mois, contre 3 406,42 € un an plus tôt (+0 %) ».</summary>
    private static string Comparer(decimal parMois, decimal? avant, TotauxBilan? precedent)
    {
        var texte = $"{Euros(parMois)} par mois";
        if (precedent is null || avant is not { } a)
            return texte;
        var evolution = a > 0 ? $" ({(parMois >= a ? "+" : "")}{Pourcentage((parMois - a) / a)})" : "";
        return $"{texte}, contre {Euros(a)} un an plus tôt{evolution}";
    }

    private bool PeutExporter() => Resultat is { Vide: false };

    [RelayCommand(CanExecute = nameof(PeutExporter))]
    private void ExporterExcel() => Exporter(_dialogues.ChoisirFichierExport($"{Titre}.xlsx"), ExportExcel.ExporterBilan, "Excel");

    [RelayCommand(CanExecute = nameof(PeutExporter))]
    private void ExporterPdf() => Exporter(_dialogues.ChoisirFichierPdf($"{Titre}.pdf"), ExportPdf.ExporterBilan, "PDF");

    private void Exporter(string? destination, Action<ResultatBilan, string, string> exporter, string format)
    {
        if (destination is null || Resultat is null)
            return;
        try
        {
            exporter(Resultat, Titre, destination);
            MessageExport = $"Bilan exporté : {destination}";
        }
        catch (Exception e)
        {
            _dialogues.Erreur($"Le fichier {format} n'a pas pu être créé (est-il déjà ouvert ?).\n\n{e.Message}");
        }
    }

    internal static string Euros(decimal montant) => $"{Montants.Formater(montant)} €";

    internal static string Pourcentage(decimal part, int decimales = 0) =>
        $"{decimal.Round(part * 100, decimales).ToString(decimales == 0 ? "0" : "0.0", Montants.Francais)} %";
}

/// <summary>Un mois du graphique du bilan.</summary>
public sealed class MoisBilanViewModel
{
    public MoisBilanViewModel(MoisBilan mois) => Mois = mois;

    public MoisBilan Mois { get; }

    public string LibelleCourt => new DateTime(Mois.Periode.Annee, Mois.Periode.Mois, 1).ToString("MMM yy", Montants.Francais);

    public decimal Revenus => Mois.Revenus;
    public decimal Depenses => Mois.Depenses;
    public decimal Epargne => Mois.Epargne;
}

/// <summary>Une ligne du tableau des postes du bilan.</summary>
public sealed class PosteBilanViewModel
{
    /// <param name="avecComparaison">L'année d'avant existe : un poste absent l'an dernier est marqué « nouveau ».</param>
    public PosteBilanViewModel(PosteBilan poste, bool avecComparaison)
    {
        Poste = poste;
        if (poste.Evolution is { } evolution)
            Evolution = $"{(evolution >= 0 ? "+" : "")}{BilanViewModel.Pourcentage(evolution)}";
        else if (avecComparaison && poste.MoyennePrecedente is null)
            Evolution = "nouveau";
        else
            Evolution = "";

        var hausse = poste.Evolution >= Bilan.SeuilHausse;
        var baisse = poste.Evolution <= -Bilan.SeuilHausse;
        // Pour l'épargne, une hausse est une bonne nouvelle.
        (EvolutionDefavorable, EvolutionFavorable) = poste.Type == TypePoste.Epargne ? (baisse, hausse) : (hausse, baisse);
    }

    public PosteBilan Poste { get; }

    public string Nom => Poste.Nom;
    public string Type => Bilan.NomType(Poste.Type);
    public decimal Total => Poste.Total;
    public decimal Moyenne => Poste.MoyenneMensuelle;
    public decimal? Avant => Poste.MoyennePrecedente;
    public string Part => BilanViewModel.Pourcentage(Poste.PartRevenus, 1);

    /// <summary>Largeur relative de la barre de part des revenus (0 à 1).</summary>
    public double PartBarre => (double)Math.Clamp(Poste.PartRevenus * 4, 0m, 1m);

    public string Evolution { get; }
    public bool EvolutionDefavorable { get; }
    public bool EvolutionFavorable { get; }
}
