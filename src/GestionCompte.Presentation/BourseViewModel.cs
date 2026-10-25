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

    /// <summary>Bilan des titres détenus : achats, dividendes et ventes partielles sur une ligne.</summary>
    public ObservableCollection<BilanTitreViewModel> TitresDetenus { get; } = new();

    /// <summary>Titres entièrement vendus, avec leur bilan.</summary>
    public ObservableCollection<BilanTitreViewModel> TitresVendus { get; } = new();

    public bool AvecTitresVendus => TitresVendus.Count > 0;

    [ObservableProperty] private BilanTitreViewModel? _selectionDetenus;
    [ObservableProperty] private BilanTitreViewModel? _selectionVendus;

    partial void OnSelectionDetenusChanged(BilanTitreViewModel? value)
    {
        if (value is not null)
            SelectionVendus = null;
        TitreSelectionne = value ?? SelectionVendus;
    }

    partial void OnSelectionVendusChanged(BilanTitreViewModel? value)
    {
        if (value is not null)
            SelectionDetenus = null;
        TitreSelectionne = value ?? SelectionDetenus;
    }

    /// <summary>Titre choisi dans un bilan : l'historique ne montre que ses opérations.</summary>
    [ObservableProperty] private BilanTitreViewModel? _titreSelectionne;

    partial void OnTitreSelectionneChanged(BilanTitreViewModel? value)
    {
        RemplirHistorique();
        OnPropertyChanged(nameof(HistoriqueFiltre));
        OnPropertyChanged(nameof(TitreHistorique));
    }

    public bool HistoriqueFiltre => TitreSelectionne is not null;

    public string TitreHistorique => TitreSelectionne is { } titre ? $"Opérations : {titre.Nom}" : "Opérations importées";

    [RelayCommand]
    private void ToutLHistorique()
    {
        SelectionDetenus = null;
        SelectionVendus = null;
    }

    /// <summary>Secteurs proposés dans les listes : ceux de base et ceux déjà saisis.</summary>
    public ObservableCollection<string> Secteurs { get; } = new(SecteursBourse.Proposes);

    /// <summary>Valeur des titres détenus par secteur.</summary>
    public IReadOnlyList<RepartitionSecteur> RepartitionSecteurs { get; private set; } = Array.Empty<RepartitionSecteur>();

    /// <summary>Alerte quand un secteur (hors ETF) pèse plus de <see cref="SeuilSecteur"/> % des titres.</summary>
    public string AlerteSecteur { get; private set; } = "";

    public bool AvecAlerteSecteur => AlerteSecteur.Length > 0;

    /// <summary>Part d'un secteur au-delà de laquelle une alerte s'affiche (choix de l'application, pas une règle).</summary>
    public const decimal SeuilSecteur = 40;

    // Idées d'investissement
    public ObservableCollection<IdeeViewModel> Idees { get; } = new();

    public bool IdeesRepliees => Portefeuille.IdeesRepliees;

    public bool AucuneIdee => Idees.Count == 0;

    [RelayCommand]
    private void BasculerIdees()
    {
        Portefeuille.IdeesRepliees = !Portefeuille.IdeesRepliees;
        OnPropertyChanged(nameof(IdeesRepliees));
        _donneesModifiees();
    }

    [RelayCommand]
    private void AjouterIdee()
    {
        Portefeuille.Idees.Add(new IdeeInvestissement("Nouvelle idée"));
        if (Portefeuille.IdeesRepliees)
            BasculerIdees();
        RemplirIdees();
        _donneesModifiees();
        ActualiserCoursCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RetirerIdee(IdeeViewModel? idee)
    {
        if (idee is null || !Portefeuille.Idees.Remove(idee.Idee))
            return;
        RemplirIdees();
        _donneesModifiees();
        ActualiserCoursCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Idée modifiée dans le tableau.</summary>
    internal void IdeeModifiee(IdeeInvestissement ancienne, IdeeInvestissement nouvelle)
    {
        var index = Portefeuille.Idees.IndexOf(ancienne);
        if (index < 0 || ancienne == nouvelle)
            return;
        Portefeuille.Idees[index] = nouvelle;
        _donneesModifiees();
        if (nouvelle.Secteur != ancienne.Secteur)
            ActualiserSecteurs();
    }

    /// <summary>Secteur d'un titre choisi dans le bilan par titre.</summary>
    internal void SecteurModifie(string isin, string? secteur)
    {
        var nouveau = string.IsNullOrWhiteSpace(secteur) || secteur.Trim() == SecteursBourse.NonClasse ? null : secteur.Trim();
        if (Portefeuille.Secteurs.GetValueOrDefault(isin) == nouveau)
            return;
        if (nouveau is null)
            Portefeuille.Secteurs.Remove(isin);
        else
            Portefeuille.Secteurs[isin] = nouveau;
        _donneesModifiees();
        CalculerRepartition();
        ActualiserSecteurs();
    }

    private void ActualiserSecteurs()
    {
        // Les secteurs saisis à la main s'ajoutent à la liste (la liste n'est jamais remplacée : les saisies en cours restent).
        void Completer()
        {
            foreach (var secteur in Portefeuille.Secteurs.Values.Concat(Portefeuille.Idees.Select(i => i.Secteur).OfType<string>()))
                if (!string.IsNullOrWhiteSpace(secteur) && !Secteurs.Contains(secteur, StringComparer.CurrentCultureIgnoreCase))
                    Secteurs.Add(secteur);
        }
        if (SynchronizationContext.Current is { } contexte)
            contexte.Post(_ => Completer(), null);
        else
            Completer();
    }

    private void CalculerRepartition()
    {
        var total = Bilan.ValeurTitres;
        RepartitionSecteurs = Bilan.Positions
            .GroupBy(p => Portefeuille.SecteurDe(p.Isin), StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new RepartitionSecteur(g.Key, g.Sum(p => p.Valeur), total == 0 ? 0 : Math.Round(g.Sum(p => p.Valeur) / total * 100, 1)))
            .OrderByDescending(x => x.Valeur)
            .ToList();
        // Un ETF est déjà diversifié : seul un secteur d'actions trop lourd est signalé.
        var lourd = RepartitionSecteurs.FirstOrDefault(x => x.Secteur != SecteursBourse.NonClasse
                                                            && !x.Secteur.StartsWith("ETF", StringComparison.OrdinalIgnoreCase)
                                                            && x.Part > SeuilSecteur);
        AlerteSecteur = lourd is null
            ? ""
            : $"{lourd.Secteur} pèse {lourd.Part.ToString("N1", Francais)} % de vos titres : une baisse de ce secteur toucherait une grosse partie du portefeuille.";
        OnPropertyChanged(nameof(RepartitionSecteurs));
        OnPropertyChanged(nameof(AlerteSecteur));
        OnPropertyChanged(nameof(AvecAlerteSecteur));
    }

    private void RemplirIdees()
    {
        Idees.Clear();
        foreach (var idee in Portefeuille.Idees)
            Idees.Add(new IdeeViewModel(this, idee));
        OnPropertyChanged(nameof(AucuneIdee));
    }

    /// <summary>Aide à la décision : indicateurs calculés sur tout le portefeuille.</summary>
    public IReadOnlyList<IndicateurBourse> Indicateurs { get; private set; } = Array.Empty<IndicateurBourse>();

    /// <summary>« Analyser avec l'Assistant » : l'application ouvre l'Assistant et lui pose la question.</summary>
    public event EventHandler<string>? AnalyseDemandee;

    public const string QuestionAnalyse =
        "Analyse mon portefeuille en bourse (répartition, frais, PEA et compte-titres, fiscalité) : quels points de vigilance vois-tu ?";

    [RelayCommand]
    private void Analyser() => AnalyseDemandee?.Invoke(this, QuestionAnalyse);

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

    /// <summary>Plus-value latente en euros (tuile) ; le pourcentage est sur la ligne du prix de revient.</summary>
    public string TexteMontantPlusValue => $"{Signe(Bilan.PlusValueLatente)} €";

    public string DetailLatent => (Bilan.PlusValueLatentePourcent is { } pourcent ? $"{Signe(pourcent)} % · " : "")
                                  + $"prix de revient {Montants.Formater(Bilan.PrixRevient)} €";

    public string TexteGainRealiseNet => $"{Signe(Bilan.GainRealiseNet)} €";

    public bool GainRealisePositif => Bilan.GainRealiseNet >= 0;

    public string TexteNetApresImpot => $"{Signe(Bilan.NetApresImpot)} €";

    public bool NetApresImpotPositif => Bilan.NetApresImpot >= 0;

    public string LibelleFlatTax => $"Flat tax {Bilan.AnneeImpot} prévue ({Pourcent(Bilan.TauxFlatTax)})";

    private static string Pourcent(decimal taux) => $"{(taux * 100).ToString("0.#", Francais)} %";

    public string TexteFlatTax => Bilan.FlatTax == 0 ? "0,00 €" : $"−{Montants.Formater(Bilan.FlatTax)} €";

    public string TexteTaxesSurRevenus => Bilan.TaxesSurRevenus + Bilan.AutresFrais == 0 ? "0,00 €"
        : $"−{Montants.Formater(Bilan.TaxesSurRevenus + Bilan.AutresFrais)} €";

    public string DetailImpot =>
        $"Flat tax : {Pourcent(Bilan.TauxFlatTax)} (12,8 % d'impôt et {Pourcent(Bilan.TauxFlatTax - 0.128m)} de prélèvements sociaux) des plus-values {Bilan.AnneeImpot} des ventes du compte-titres " +
        $"({Signe(Bilan.PlusValuesImposables)} €), 0 en cas de perte. Les dividendes sont déjà taxés par Trade Republic. " +
        "Le PEA n'est imposé qu'en cas de retrait. Prévision indicative : seul l'imprimé fiscal fait foi.";

    public string DetailGainNet =>
        $"Plus-values des ventes ({Signe(Bilan.PlusValuesRealisees)} €) + dividendes et intérêts ({Montants.Formater(Bilan.Dividendes)} €) " +
        $"− taxes retenues ({Montants.Formater(Bilan.TaxesSurRevenus + Bilan.AutresFrais)} €). Les frais d'achat et de vente sont déjà dans les plus-values.";

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
        "Ventes : montant reçu net de frais − prix de revient moyen des titres vendus (frais et taxes d'achat compris, comme pour les impôts). " +
        "Trade Republic, lui, ne compte pas les frais et taxes d'achat.";

    /// <summary>Plus-values calculées comme Trade Republic (sans les frais et taxes d'achat), vide s'il n'y a pas d'écart.</summary>
    public string TexteHorsFraisAchat => AfficherHorsFraisAchat
        ? $"{Montants.Formater(Bilan.PlusValuesHorsFraisAchat)} € hors frais d'achat (calcul Trade Republic)" : "";

    public bool AfficherHorsFraisAchat => Bilan.PlusValuesHorsFraisAchat != Bilan.PlusValuesRealisees;

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
        // Compte-titres puis PEA (bandeaux du tableau), les plus grosses lignes d'abord.
        var totaux = Bilan.Positions.GroupBy(p => p.Enveloppe).ToDictionary(g => g.Key, g => g.Sum(p => p.Valeur));
        foreach (var position in Bilan.Positions.OrderBy(p => p.Enveloppe).ThenByDescending(p => p.Valeur))
            Positions.Add(new PositionViewModel(this, position, total, totaux[position.Enveloppe]));
        var choisi = TitreSelectionne;
        SelectionDetenus = null;
        SelectionVendus = null;
        TitresDetenus.Clear();
        TitresVendus.Clear();
        foreach (var titre in Bilan.Titres)
            (titre.Solde ? TitresVendus : TitresDetenus).Add(new BilanTitreViewModel(this, titre));
        OnPropertyChanged(nameof(AvecTitresVendus));
        // Le même titre reste choisi après un recalcul (nouveau cours, import).
        bool Meme(BilanTitreViewModel t) => choisi is not null && t.Isin == choisi.Isin && t.Titre.Enveloppe == choisi.Titre.Enveloppe;
        SelectionDetenus = TitresDetenus.FirstOrDefault(Meme);
        SelectionVendus = TitresVendus.FirstOrDefault(Meme);
        RemplirHistorique();
        RemplirIdees();
        CalculerRepartition();
        ActualiserSecteurs();
    }

    private void RemplirHistorique()
    {
        Historique.Clear();
        var titre = TitreSelectionne?.Titre;
        foreach (var operation in Portefeuille.Operations
                     .Where(o => Enveloppe is null || o.Enveloppe == Enveloppe)
                     .Where(o => titre is null || o.Enveloppe == titre.Enveloppe && string.Equals(o.Isin, titre.Isin, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(o => o.Date))
            Historique.Add(new OperationBourseViewModel(operation));
    }

    private void CalculerBilan()
    {
        Bilan = CalculBourse.Calculer(Portefeuille, Enveloppe, _aujourdhui.Year);
        Indicateurs = AideDecisionBourse.Calculer(Portefeuille, _aujourdhui);
        OnPropertyChanged(nameof(Indicateurs));
        OnPropertyChanged(nameof(Vide));
        OnPropertyChanged(nameof(AEnveloppes));
        OnPropertyChanged(nameof(PlusValuePositive));
        OnPropertyChanged(nameof(GainPositif));
        OnPropertyChanged(nameof(TextePlusValue));
        OnPropertyChanged(nameof(TexteMontantPlusValue));
        OnPropertyChanged(nameof(DetailLatent));
        OnPropertyChanged(nameof(TexteGain));
        OnPropertyChanged(nameof(TexteSansCours));
        OnPropertyChanged(nameof(DetailVerse));
        OnPropertyChanged(nameof(DetailPlusValue));
        OnPropertyChanged(nameof(TexteHorsFraisAchat));
        OnPropertyChanged(nameof(TexteGainRealiseNet));
        OnPropertyChanged(nameof(GainRealisePositif));
        OnPropertyChanged(nameof(TexteNetApresImpot));
        OnPropertyChanged(nameof(NetApresImpotPositif));
        OnPropertyChanged(nameof(LibelleFlatTax));
        OnPropertyChanged(nameof(TexteFlatTax));
        OnPropertyChanged(nameof(TexteTaxesSurRevenus));
        OnPropertyChanged(nameof(DetailImpot));
        OnPropertyChanged(nameof(DetailGainNet));
        OnPropertyChanged(nameof(AfficherHorsFraisAchat));
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

    /// <summary>Importe l'export de Trade Republic ; les opérations déjà importées sont ignorées, ou corrigées si elles ont changé.</summary>
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

        var (ajoutees, corrigees) = Portefeuille.Importer(lecture.Operations);
        var deja = lecture.Operations.Count - ajoutees - corrigees;
        Statut = $"{ajoutees} opération{(ajoutees > 1 ? "s" : "")} importée{(ajoutees > 1 ? "s" : "")}"
                 + (corrigees > 0 ? $", {corrigees} corrigée{(corrigees > 1 ? "s" : "")}" : "")
                 + (deja > 0 ? $", {deja} déjà présente{(deja > 1 ? "s" : "")}" : "")
                 + (lecture.Ignorees > 0 ? $", {lecture.Ignorees} ligne{(lecture.Ignorees > 1 ? "s" : "")} illisible{(lecture.Ignorees > 1 ? "s" : "")}" : "")
                 + ".";
        if (ajoutees + corrigees > 0)
            _donneesModifiees();
        Recalculer();
    }

    private bool PeutActualiser() => !EnCours && (!Portefeuille.Vide || Portefeuille.Idees.Count > 0);

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
            for (var i = 0; i < Portefeuille.Idees.Count; i++)
            {
                var idee = Portefeuille.Idees[i];
                var recherche = string.IsNullOrWhiteSpace(idee.Recherche) ? idee.Nom : idee.Recherche.Trim();
                if (string.IsNullOrWhiteSpace(recherche) || erreur is not null)
                    continue;
                Statut = $"Recherche du cours de {idee.Nom}…";
                try
                {
                    if (await _cours.ChercherAsync(recherche, idee.Symbole) is { } cours)
                    {
                        Portefeuille.Idees[i] = idee with { Cours = cours.Valeur, DateCours = cours.Date, Symbole = cours.Symbole };
                        trouves++;
                    }
                    else
                        absents.Add(idee.Nom);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                {
                    erreur = ex is TaskCanceledException ? "Yahoo Finance ne répond pas." : ex.Message;
                    absents.Add(idee.Nom);
                }
            }
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

    internal PositionViewModel(BourseViewModel parent, PositionBourse position, decimal totalTitres, decimal totalEnveloppe = 0)
    {
        _parent = parent;
        TotalEnveloppe = totalEnveloppe;
        Position = position;
        Part = totalTitres == 0 ? 0 : Math.Round(position.Valeur / totalTitres * 100, 1);
        Symbole = parent.Portefeuille.Cours.GetValueOrDefault(position.Isin)?.Symbole;
    }

    public PositionBourse Position { get; }

    public string Nom => Position.Nom;

    public string Isin => Position.Isin;

    public string Enveloppe => Position.Enveloppe == EnveloppeBourse.Pea ? "PEA" : "Compte-titres";

    /// <summary>Valeur des titres de la même enveloppe (bandeau du tableau).</summary>
    public decimal TotalEnveloppe { get; }

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

/// <summary>Part d'un secteur dans la valeur des titres détenus.</summary>
public sealed record RepartitionSecteur(string Secteur, decimal Valeur, decimal Part)
{
    public double PartBarre => (double)Part / 100;

    public string TextePart => $"{Part.ToString("N1", CultureInfo.GetCultureInfo("fr-FR"))} %";
}

/// <summary>Ligne du bilan par titre ; le secteur se choisit dans la liste.</summary>
public sealed class BilanTitreViewModel
{
    private readonly BourseViewModel _parent;

    internal BilanTitreViewModel(BourseViewModel parent, BilanTitre titre)
    {
        _parent = parent;
        Titre = titre;
    }

    public BilanTitre Titre { get; }

    public string Nom => Titre.Nom;

    public string Isin => Titre.Isin;

    public string Enveloppe => Titre.Enveloppe == EnveloppeBourse.Pea ? "PEA" : "Compte-titres";

    public string Etat => Titre.Solde ? "Vendu" : "Détenu";

    public bool Solde => Titre.Solde;

    public decimal Achete => Titre.Achete;

    public decimal Dividendes => Titre.Dividendes;

    public decimal Vendu => Titre.Vendu;

    public bool Perte => Titre.GainVentes < 0;

    public string TexteGainVentes => Titre.GainVentesPourcent is { } pourcent
        ? $"{(Titre.GainVentes > 0 ? "+" : "")}{Montants.Formater(Titre.GainVentes)} ({(pourcent > 0 ? "+" : "")}{pourcent.ToString("N1", CultureInfo.GetCultureInfo("fr-FR"))} %)"
        : "—";

    public string Secteur
    {
        get => _parent.Portefeuille.SecteurDe(Isin);
        set => _parent.SecteurModifie(Isin, value);
    }
}

/// <summary>Idée d'investissement modifiable dans le tableau.</summary>
public sealed class IdeeViewModel : ObservableObject
{
    private readonly BourseViewModel _parent;

    internal IdeeViewModel(BourseViewModel parent, IdeeInvestissement idee)
    {
        _parent = parent;
        Idee = idee;
    }

    public IdeeInvestissement Idee { get; private set; }

    private void Modifier(IdeeInvestissement nouvelle)
    {
        var ancienne = Idee;
        Idee = nouvelle;
        _parent.IdeeModifiee(ancienne, nouvelle);
    }

    public string Nom
    {
        get => Idee.Nom;
        set => Modifier(Idee with { Nom = value?.Trim() ?? "" });
    }

    public string Secteur
    {
        get => Idee.Secteur ?? "";
        set => Modifier(Idee with { Secteur = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
    }

    /// <summary>ISIN ou code Yahoo ; changé, le cours est recherché à nouveau.</summary>
    public string Recherche
    {
        get => Idee.Recherche ?? "";
        set
        {
            var recherche = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
            if (recherche != Idee.Recherche)
                Modifier(Idee with { Recherche = recherche, Symbole = null });
        }
    }

    public string Note
    {
        get => Idee.Note ?? "";
        set => Modifier(Idee with { Note = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
    }

    public decimal? Cours => Idee.Cours;

    public string InfoCours => Idee.Cours is null
        ? "Pas encore de cours : cliquez sur « Actualiser les cours »."
        : $"Yahoo Finance, le {Idee.DateCours:dd/MM/yyyy}" + (Idee.Symbole is null ? "" : $" ({Idee.Symbole})");
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
