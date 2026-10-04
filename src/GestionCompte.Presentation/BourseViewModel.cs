using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Core;
using GestionCompte.Core.Bourse;

namespace GestionCompte.Presentation;

/// <summary>
/// Module « Bourse » : placements du compte chez un courtier (Trade Republic), importés de l'export CSV des transactions.
/// Positions au prix moyen pondéré, cours récupérés sur Yahoo Finance ou saisis à la main, plus-values et répartition.
/// </summary>
public sealed partial class BourseViewModel : ObservableObject
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");
    private readonly CompteBancaire _compte;
    private readonly IDialogues _dialogues;
    private readonly Action _donneesModifiees;
    private readonly ServiceCoursBourse _cours;
    private readonly Func<string, string> _lireFichier;
    private readonly DateTime _aujourdhui;

    /// <param name="donneesModifiees">Appelé après un import ou un changement de cours (à enregistrer).</param>
    /// <param name="lireFichier">Lecture du fichier choisi (remplacée dans les tests).</param>
    public BourseViewModel(CompteBancaire compte, IDialogues dialogues, Action donneesModifiees, ServiceCoursBourse cours,
        DateTime aujourdhui, Func<string, string>? lireFichier = null)
    {
        _compte = compte;
        _dialogues = dialogues;
        _donneesModifiees = donneesModifiees;
        _cours = cours;
        _aujourdhui = aujourdhui;
        _lireFichier = lireFichier ?? File.ReadAllText;
        Recalculer();
    }

    public CompteBancaire Compte => _compte;

    public Portefeuille Portefeuille => _compte.Portefeuille;

    public static readonly IReadOnlyList<string> NomsFiltres = new[] { "Tout", "Compte-titres", "PEA" };

    public IReadOnlyList<string> Filtres => NomsFiltres;

    /// <summary>0 : tout le portefeuille, 1 : compte-titres, 2 : PEA.</summary>
    [ObservableProperty] private int _filtre;

    partial void OnFiltreChanged(int value) => Recalculer();

    private EnveloppeBourse? Enveloppe => Filtre switch
    {
        1 => EnveloppeBourse.CompteTitres,
        2 => EnveloppeBourse.Pea,
        _ => null,
    };

    [ObservableProperty] private BilanBourse _bilan = null!;

    public ObservableCollection<PositionViewModel> Positions { get; } = new();

    public ObservableCollection<OperationBourseViewModel> Historique { get; } = new();

    public bool Vide => Portefeuille.Vide;

    public bool AEnveloppes => Portefeuille.Operations.Select(o => o.Enveloppe).Distinct().Count() > 1;

    [ObservableProperty] private bool _enCours;

    [ObservableProperty] private string _statut = "";

    public bool PlusValuePositive => Bilan.PlusValueLatente >= 0;

    public bool GainPositif => Bilan.Gain >= 0;

    public string TextePlusValue => Bilan.PlusValueLatentePourcent is { } pourcent
        ? $"{Signe(Bilan.PlusValueLatente)} € ({Signe(pourcent)} %)"
        : $"{Signe(Bilan.PlusValueLatente)} €";

    public string TexteGain => $"{Signe(Bilan.Gain)} €";

    public string TexteSansCours => Bilan.SansCours == 0 ? ""
        : $"{Bilan.SansCours} ligne{(Bilan.SansCours > 1 ? "s" : "")} sans cours : estimée{(Bilan.SansCours > 1 ? "s" : "")} à son prix d'achat. " +
          "Cliquez sur « Actualiser les cours » ou saisissez le cours dans le tableau.";

    public string DetailVerse =>
        $"Versé : {Montants.Formater(Bilan.Versements)} €, retiré : {Montants.Formater(Bilan.Retraits)} €. " +
        "Gain = valeur du portefeuille (titres et espèces) − argent versé net.";

    public string DetailPlusValue =>
        $"Valeur des titres ({Montants.Formater(Bilan.ValeurTitres)} €) − prix de revient ({Montants.Formater(Bilan.PrixRevient)} €). " +
        "Le prix de revient est le prix moyen pondéré des achats, frais et taxes compris.";

    public string DetailRealise =>
        "Ventes : montant reçu net de frais − prix de revient moyen des titres vendus.";

    private static string Signe(decimal valeur) => (valeur > 0 ? "+" : "") + Montants.Formater(valeur);

    /// <summary>Recalcule le bilan et les tableaux (après un import, un filtre ou un nouveau cours).</summary>
    public void Recalculer()
    {
        CalculerBilan();
        RemplirTableaux();
    }

    private void RemplirTableaux()
    {
        var total = Bilan.ValeurTitres;
        Positions.Clear();
        foreach (var position in Bilan.Positions)
            Positions.Add(new PositionViewModel(this, position, total));
        Historique.Clear();
        foreach (var operation in Portefeuille.Operations.Where(o => Enveloppe is null || o.Enveloppe == Enveloppe).OrderByDescending(o => o.Date))
            Historique.Add(new OperationBourseViewModel(operation));
    }

    private void CalculerBilan()
    {
        Bilan = CalculBourse.Calculer(Portefeuille, Enveloppe);
        OnPropertyChanged(nameof(Vide));
        OnPropertyChanged(nameof(AEnveloppes));
        OnPropertyChanged(nameof(PlusValuePositive));
        OnPropertyChanged(nameof(GainPositif));
        OnPropertyChanged(nameof(TextePlusValue));
        OnPropertyChanged(nameof(TexteGain));
        OnPropertyChanged(nameof(TexteSansCours));
        OnPropertyChanged(nameof(DetailVerse));
        OnPropertyChanged(nameof(DetailPlusValue));
        ViderCommand.NotifyCanExecuteChanged();
        ActualiserCoursCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Cours saisi ou corrigé dans le tableau (null : effacé).</summary>
    internal void CoursModifie(string isin, decimal? valeur, string? symbole)
    {
        var ancien = Portefeuille.Cours.GetValueOrDefault(isin);
        Portefeuille.Cours[isin] = valeur is null
            ? new CoursTitre(null, null, false, symbole)
            : new CoursTitre(valeur, _aujourdhui, true, symbole);
        if (ancien == Portefeuille.Cours[isin])
            return;
        _donneesModifiees();
        CalculerBilan();
        // Le tableau est en train de valider la saisie : ses lignes sont recréées juste après.
        if (SynchronizationContext.Current is { } contexte)
            contexte.Post(_ => RemplirTableaux(), null);
        else
            RemplirTableaux();
    }

    /// <summary>Symbole Yahoo corrigé à la main : le cours sera cherché avec lui.</summary>
    internal void SymboleModifie(string isin, string? symbole)
    {
        var ancien = Portefeuille.Cours.GetValueOrDefault(isin);
        if (ancien?.Symbole == symbole)
            return;
        Portefeuille.Cours[isin] = (ancien ?? new CoursTitre(null, null, false, null)) with { Symbole = symbole };
        _donneesModifiees();
    }

    [RelayCommand]
    private void Importer()
    {
        if (_dialogues.ChoisirExportBourse() is not { } chemin)
            return;
        Importer(chemin);
    }

    /// <summary>Importe l'export de Trade Republic ; les opérations déjà importées sont ignorées.</summary>
    public void Importer(string chemin)
    {
        LectureReleveBourse lecture;
        try
        {
            lecture = ImportTradeRepublic.Lire(_lireFichier(chemin));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            _dialogues.Erreur($"Import impossible : {ex.Message}");
            return;
        }

        var ajoutees = Portefeuille.Ajouter(lecture.Operations);
        var deja = lecture.Operations.Count - ajoutees;
        Statut = $"{ajoutees} opération{(ajoutees > 1 ? "s" : "")} importée{(ajoutees > 1 ? "s" : "")}"
                 + (deja > 0 ? $", {deja} déjà présente{(deja > 1 ? "s" : "")}" : "")
                 + (lecture.Ignorees > 0 ? $", {lecture.Ignorees} ligne{(lecture.Ignorees > 1 ? "s" : "")} illisible{(lecture.Ignorees > 1 ? "s" : "")}" : "")
                 + ".";
        if (ajoutees > 0)
            _donneesModifiees();
        Recalculer();
    }

    private bool PeutActualiser() => !EnCours && !Portefeuille.Vide;

    /// <summary>Cherche le dernier cours de chaque titre détenu sur Yahoo Finance (les cours saisis à la main sont remplacés).</summary>
    [RelayCommand(CanExecute = nameof(PeutActualiser))]
    private async Task ActualiserCoursAsync()
    {
        EnCours = true;
        ActualiserCoursCommand.NotifyCanExecuteChanged();
        var trouves = 0;
        var absents = new List<string>();
        string? erreur = null;
        try
        {
            foreach (var position in CalculBourse.Calculer(Portefeuille).Positions.GroupBy(p => p.Isin, StringComparer.OrdinalIgnoreCase))
            {
                var isin = position.Key;
                Statut = $"Recherche du cours de {position.First().Nom}…";
                var symbole = Portefeuille.Cours.GetValueOrDefault(isin)?.Symbole;
                try
                {
                    if (await _cours.ChercherAsync(isin, symbole) is { } cours)
                    {
                        Portefeuille.Cours[isin] = new CoursTitre(cours.Valeur, cours.Date, false, cours.Symbole);
                        trouves++;
                    }
                    else
                        absents.Add(position.First().Nom);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                {
                    erreur = ex is TaskCanceledException ? "Yahoo Finance ne répond pas." : ex.Message;
                    absents.Add(position.First().Nom);
                }
            }
        }
        finally
        {
            EnCours = false;
            ActualiserCoursCommand.NotifyCanExecuteChanged();
        }

        Statut = $"{trouves} cours mis à jour."
                 + (absents.Count > 0 ? $" Introuvable{(absents.Count > 1 ? "s" : "")} : {string.Join(", ", absents)} (saisissez le cours à la main)." : "")
                 + (erreur is not null ? $" {erreur}" : "");
        if (trouves > 0)
            _donneesModifiees();
        Recalculer();
    }

    private bool PeutVider() => !Portefeuille.Vide;

    [RelayCommand(CanExecute = nameof(PeutVider))]
    private void Vider()
    {
        if (!_dialogues.Confirmer("Vider le portefeuille",
                "Effacer toutes les opérations de bourse importées et les cours de ce compte ? Vous pourrez réimporter l'export de Trade Republic."))
            return;
        Portefeuille.Operations.Clear();
        Portefeuille.Cours.Clear();
        Statut = "";
        _donneesModifiees();
        Recalculer();
    }
}

/// <summary>Ligne du tableau des positions ; le cours et le symbole Yahoo se corrigent à la main.</summary>
public sealed class PositionViewModel : ObservableObject
{
    private readonly BourseViewModel _parent;

    internal PositionViewModel(BourseViewModel parent, PositionBourse position, decimal totalTitres)
    {
        _parent = parent;
        Position = position;
        Part = totalTitres == 0 ? 0 : Math.Round(position.Valeur / totalTitres * 100, 1);
        Symbole = parent.Portefeuille.Cours.GetValueOrDefault(position.Isin)?.Symbole;
    }

    public PositionBourse Position { get; }

    public string Nom => Position.Nom;

    public string Isin => Position.Isin;

    public string Enveloppe => Position.Enveloppe == EnveloppeBourse.Pea ? "PEA" : "Compte-titres";

    public decimal Quantite => Position.Quantite;

    public string TexteQuantite => Position.Quantite.ToString(Position.Quantite == decimal.Truncate(Position.Quantite) ? "N0" : "0.######",
        CultureInfo.GetCultureInfo("fr-FR"));

    public decimal Pru => Math.Round(Position.Pru, 2);

    public decimal? Cours
    {
        get => Position.Cours;
        set => _parent.CoursModifie(Isin, value, Symbole);
    }

    public string? Symbole { get; private set; }

    public string? SymboleSaisi
    {
        get => Symbole;
        set
        {
            Symbole = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
            _parent.SymboleModifie(Isin, Symbole);
            OnPropertyChanged();
        }
    }

    public string InfoCours => Position.Cours is null
        ? "Pas de cours : la ligne est estimée à son prix d'achat."
        : $"{(Position.CoursManuel ? "Saisi à la main" : "Yahoo Finance")}, le {Position.DateCours:dd/MM/yyyy}" +
          (Symbole is null ? "" : $" ({Symbole})");

    public decimal Valeur => Position.Valeur;

    public decimal PrixRevient => Position.PrixRevient;

    public decimal PlusValue => Position.PlusValue;

    public bool Perte => Position.PlusValue < 0;

    public string TextePlusValue => Position.PlusValuePourcent is { } pourcent
        ? $"{(PlusValue > 0 ? "+" : "")}{Montants.Formater(PlusValue)} ({(pourcent > 0 ? "+" : "")}{pourcent.ToString("N1", CultureInfo.GetCultureInfo("fr-FR"))} %)"
        : "—";

    /// <summary>Part de la ligne dans la valeur des titres, en %.</summary>
    public decimal Part { get; }

    public double PartBarre => (double)Part / 100;

    public string TextePart => $"{Part.ToString("N1", CultureInfo.GetCultureInfo("fr-FR"))} %";

    public string InfoPrixRevient => $"{TexteQuantite} titre(s) pour {Montants.Formater(PrixRevient)} € (frais et taxes compris), soit {Montants.Formater(Pru)} € par titre.";
}

/// <summary>Ligne de l'historique des opérations importées.</summary>
public sealed class OperationBourseViewModel
{
    internal OperationBourseViewModel(OperationBourse operation) => Operation = operation;

    public OperationBourse Operation { get; }

    public DateTime Date => Operation.Date.ToLocalTime();

    public string Enveloppe => Operation.Enveloppe == EnveloppeBourse.Pea ? "PEA" : "CTO";

    public string Type => Operation.Type switch
    {
        TypeOperationBourse.Achat => "Achat",
        TypeOperationBourse.Vente => "Vente",
        TypeOperationBourse.Versement => "Versement",
        TypeOperationBourse.Retrait => "Retrait",
        TypeOperationBourse.Dividende => "Dividende",
        TypeOperationBourse.Interets => "Intérêts",
        TypeOperationBourse.Frais => "Frais",
        TypeOperationBourse.Impot => "Impôt",
        _ => "Autre",
    };

    public string Libelle => Operation.EstTitre
        ? $"{Operation.Nom} : {Math.Abs(Operation.Quantite).ToString("0.######", CultureInfo.GetCultureInfo("fr-FR"))} × {Montants.Formater(Operation.Prix)} €"
        : Operation.Nom ?? Operation.Description;

    public decimal Montant => Operation.Net;

    public bool Negatif => Operation.Net < 0;
}
