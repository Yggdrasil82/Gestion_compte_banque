using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Core;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Presentation;

/// <summary>
/// Écran Aide au budget : alertes, répartition 50/30/20, analyse des charges, suivi des enveloppes,
/// objectifs d'épargne et simulateur « Et si… ? ».
/// </summary>
public sealed partial class AideBudgetViewModel : ObservableObject
{
    /// <summary>Nombre de mois simulés pour le simulateur et la capacité d'épargne.</summary>
    public const int Horizon = 12;

    /// <summary>Nombre de mois futurs proposés comme échéance d'un objectif.</summary>
    public const int MoisProposes = 60;

    private readonly CompteBancaire _compte;
    private readonly IDialogues _dialogues;
    private readonly Action _donneesModifiees;
    private readonly Action _configurationRemplacee;
    private bool _simulationEnCours;

    /// <param name="donneesModifiees">Appelé après une modification des objectifs (à enregistrer).</param>
    /// <param name="configurationRemplacee">Appelé après une modification de la configuration depuis cet écran.</param>
    public AideBudgetViewModel(CompteBancaire compte, IDialogues dialogues, Action donneesModifiees, Action configurationRemplacee)
    {
        _compte = compte;
        _dialogues = dialogues;
        _donneesModifiees = donneesModifiees;
        _configurationRemplacee = configurationRemplacee;

        var premier = compte.ProchainMois;
        var periodes = new List<ChoixPeriode>();
        for (var (i, p) = (0, premier); i < MoisProposes; i++, p = p.Suivant())
            periodes.Add(new ChoixPeriode(p));
        Periodes = periodes;

        Objectifs = new ListeEditable<ObjectifViewModel>(
            compte.ObjectifsEpargne.Select(o => new ObjectifViewModel(o, Periodes, ObjectifModifie)),
            () => new ObjectifViewModel(new ObjectifEpargne("Nouvel objectif", 1000m, periodes[Math.Min(11, periodes.Count - 1)].Periode),
                Periodes, ObjectifModifie),
            ObjectifsReorganises);

        Simulation = compte.Configuration.Charges
            .Select(c => new LigneSimulationViewModel(c.Nom, c.Debit - c.Credit, estEnveloppe: false, LigneSimulationModifiee))
            .Concat(compte.Configuration.Enveloppes
                .Select(e => new LigneSimulationViewModel(e.Nom, e.BudgetParDefaut, estEnveloppe: true, LigneSimulationModifiee)))
            .ToList();

        Recalculer();
    }

    public IReadOnlyList<ChoixPeriode> Periodes { get; }

    // 6. Alertes
    [ObservableProperty] private IReadOnlyList<Alerte> _alertes = Array.Empty<Alerte>();

    // 2. Répartition 50/30/20
    [ObservableProperty] private IReadOnlyList<PartViewModel> _parts = Array.Empty<PartViewModel>();
    [ObservableProperty] private decimal _revenusMensuels;
    [ObservableProperty] private decimal _nonClasse;
    [ObservableProperty] private decimal _resteDisponible;

    public bool AChargesNonClassees => NonClasse > 0;

    partial void OnNonClasseChanged(decimal value) => OnPropertyChanged(nameof(AChargesNonClassees));

    // 1. Analyse des charges
    [ObservableProperty] private IReadOnlyList<ChargeAnalyseeViewModel> _charges = Array.Empty<ChargeAnalyseeViewModel>();
    [ObservableProperty] private IReadOnlyList<GroupeCharges> _groupes = Array.Empty<GroupeCharges>();
    [ObservableProperty] private decimal _totalChargesAnnuel;

    // 3. Suivi des enveloppes
    [ObservableProperty] private IReadOnlyList<SuiviEnveloppeViewModel> _suiviEnveloppes = Array.Empty<SuiviEnveloppeViewModel>();

    // 5. Objectifs d'épargne
    public ListeEditable<ObjectifViewModel> Objectifs { get; }
    [ObservableProperty] private decimal _capaciteMensuelle;
    [ObservableProperty] private decimal _totalMensualites;

    // 4. Simulateur
    public IReadOnlyList<LigneSimulationViewModel> Simulation { get; }
    [ObservableProperty] private decimal _gainMensuel;
    [ObservableProperty] private decimal _gainAnnuel;
    [ObservableProperty] private decimal _soldeAvant;
    [ObservableProperty] private decimal _soldeApres;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AppliquerSimulationCommand), nameof(ReinitialiserSimulationCommand))]
    private bool _simulationChangee;

    public bool GainPositif => GainMensuel > 0;
    public bool GainNegatif => GainMensuel < 0;

    partial void OnGainMensuelChanged(decimal value)
    {
        OnPropertyChanged(nameof(GainPositif));
        OnPropertyChanged(nameof(GainNegatif));
    }

    /// <summary>Recalcule tout l'écran à partir des données actuelles.</summary>
    public void Recalculer()
    {
        var configuration = _compte.Configuration;

        var repartition = AideBudget.Repartir(configuration);
        RevenusMensuels = repartition.RevenusMensuels;
        Parts = repartition.Parts.Select(p => new PartViewModel(p)).ToList();
        NonClasse = repartition.NonClasse;
        ResteDisponible = repartition.ResteDisponible;

        var analyse = AideBudget.AnalyserCharges(configuration);
        Charges = analyse.Charges.Select(c => new ChargeAnalyseeViewModel(c)).ToList();
        Groupes = analyse.Groupes;
        TotalChargesAnnuel = analyse.TotalMensuel * 12;

        SuiviEnveloppes = AideBudget.SuivreEnveloppes(_compte)
            .Select(s => new SuiviEnveloppeViewModel(s, AppliquerBudgetConseille))
            .ToList();

        RecalculerObjectifs();
        RecalculerSimulation();
        Alertes = AideBudget.Alertes(_compte);
    }

    private void RecalculerObjectifs()
    {
        var resultat = AideBudget.AnalyserObjectifs(_compte);
        CapaciteMensuelle = resultat.CapaciteMensuelle;
        foreach (var (vm, analyse) in Objectifs.Elements.Zip(resultat.Objectifs))
            vm.MettreAJour(analyse);
        TotalMensualites = resultat.Objectifs
            .Where(o => o.Faisabilite is not Faisabilite.Atteint and not Faisabilite.EcheancePassee)
            .Sum(o => o.Mensualite);
    }

    private void RecalculerSimulation()
    {
        var configuration = _compte.Configuration;
        var charges = configuration.Charges
            .Zip(Simulation.Where(l => !l.EstEnveloppe))
            .Where(x => x.Second.Garder)
            .Select(x => x.First with { Debit = x.Second.NouveauMontant + x.First.Credit })
            .ToList();
        var enveloppes = configuration.Enveloppes
            .Zip(Simulation.Where(l => l.EstEnveloppe))
            .Where(x => x.Second.Garder)
            .Select(x => x.First with { BudgetParDefaut = x.Second.NouveauMontant })
            .ToList();

        var resultat = AideBudget.Simuler(_compte, charges, enveloppes, Horizon);
        GainMensuel = resultat.GainMensuel;
        GainAnnuel = resultat.GainAnnuel;
        SoldeAvant = resultat.SoldeAvant;
        SoldeApres = resultat.SoldeApres;
        SimulationChangee = Simulation.Any(l => l.Modifiee);
    }

    private void LigneSimulationModifiee()
    {
        if (!_simulationEnCours)
            RecalculerSimulation();
    }

    [RelayCommand(CanExecute = nameof(SimulationChangee))]
    private void ReinitialiserSimulation()
    {
        _simulationEnCours = true;
        foreach (var ligne in Simulation)
            ligne.Reinitialiser();
        _simulationEnCours = false;
        RecalculerSimulation();
    }

    [RelayCommand(CanExecute = nameof(SimulationChangee))]
    private void AppliquerSimulation()
    {
        var retirees = Simulation.Where(l => !l.Garder).Select(l => l.Nom).ToList();
        var changees = Simulation.Where(l => l.Garder && l.NouveauMontant != l.MontantActuel)
            .Select(l => $"{l.Nom} : {Montants.Formater(l.MontantActuel)} → {Montants.Formater(l.NouveauMontant)} €").ToList();

        var message = "Modifier la configuration selon la simulation ?\n\n" +
            (retirees.Count > 0 ? $"Retirées : {string.Join(", ", retirees)}\n" : "") +
            (changees.Count > 0 ? $"Modifiées :\n• {string.Join("\n• ", changees)}\n" : "") +
            "\nLes mois déjà créés ne changent pas (utilisez « Appliquer la configuration » sur un mois si besoin).";
        if (!_dialogues.Confirmer("Appliquer la simulation", message))
            return;

        var configuration = _compte.Configuration;
        var charges = configuration.Charges.Zip(Simulation.Where(l => !l.EstEnveloppe))
            .Where(x => x.Second.Garder)
            .Select(x => x.First with { Debit = x.Second.NouveauMontant + x.First.Credit })
            .ToList();
        var enveloppes = configuration.Enveloppes.Zip(Simulation.Where(l => l.EstEnveloppe))
            .Where(x => x.Second.Garder)
            .Select(x => x.First with { BudgetParDefaut = x.Second.NouveauMontant })
            .ToList();

        configuration.Charges.Clear();
        configuration.Charges.AddRange(charges);
        configuration.Enveloppes.Clear();
        configuration.Enveloppes.AddRange(enveloppes);
        _configurationRemplacee();
    }

    private void AppliquerBudgetConseille(SuiviEnveloppeViewModel suivi)
    {
        if (suivi.Suivi.BudgetConseille is not { } conseil)
            return;
        if (!_dialogues.Confirmer("Ajuster une enveloppe",
                $"Passer le budget « {suivi.Nom} » de {Montants.Formater(suivi.Suivi.Budget)} € à {Montants.Formater(conseil)} € " +
                "pour les prochains mois ?"))
            return;

        var enveloppes = _compte.Configuration.Enveloppes;
        var index = enveloppes.FindIndex(e => CalculateurMois.MemeNom(e.Nom, suivi.Nom));
        if (index < 0)
            return;
        enveloppes[index] = enveloppes[index] with { BudgetParDefaut = conseil };
        _configurationRemplacee();
    }

    private void ObjectifModifie()
    {
        RecalculerObjectifs();
        Alertes = AideBudget.Alertes(_compte);
        _donneesModifiees();
    }

    private void ObjectifsReorganises()
    {
        _compte.ObjectifsEpargne.Clear();
        _compte.ObjectifsEpargne.AddRange(Objectifs.Elements.Select(o => o.Modele));
        ObjectifModifie();
    }
}

public sealed class PartViewModel
{
    public PartViewModel(PartCategorie part) => Part = part;

    public PartCategorie Part { get; }

    public string Nom => Part.Categorie switch
    {
        Categorie.Essentiel => "Essentiel",
        Categorie.Confort => "Confort",
        Categorie.Epargne => "Épargne",
        _ => "Non classé",
    };

    public Categorie Categorie => Part.Categorie;

    public decimal Montant => Part.Montant;

    /// <summary>Part des revenus, de 0 à 100.</summary>
    public double Pourcentage => (double)decimal.Round(Part.Part * 100, 1);

    public double Cible => (double)(Part.Cible * 100);

    public string Texte => $"{decimal.Round(Part.Part * 100, 0)} % (conseillé : {decimal.Round(Part.Cible * 100, 0)} %)";

    /// <summary>Au-dessus de la cible pour l'essentiel et le confort, en dessous pour l'épargne.</summary>
    public bool HorsCible => Part.Categorie == Categorie.Epargne ? Part.Part < Part.Cible : Part.Part > Part.Cible;
}

public sealed class ChargeAnalyseeViewModel
{
    public ChargeAnalyseeViewModel(ChargeAnalysee charge) => Charge = charge;

    public ChargeAnalysee Charge { get; }

    public string Nom => Charge.Nom;

    public decimal Mensuel => Charge.Mensuel;

    public decimal Annuel => Charge.Annuel;

    public string Part => $"{decimal.Round(Charge.PartRevenus * 100, 1)} %";

    public string Categorie => ChoixCategorie.De(Charge.Categorie).Nom;
}

public sealed partial class SuiviEnveloppeViewModel
{
    private readonly Action<SuiviEnveloppeViewModel> _appliquer;

    public SuiviEnveloppeViewModel(SuiviEnveloppe suivi, Action<SuiviEnveloppeViewModel> appliquer)
    {
        Suivi = suivi;
        _appliquer = appliquer;
    }

    public SuiviEnveloppe Suivi { get; }

    public string Nom => Suivi.Nom;

    public decimal Budget => Suivi.Budget;

    public decimal Moyenne => Suivi.Moyenne;

    public bool PeutAjuster => Suivi.BudgetConseille is not null;

    public bool AReduire => Suivi.Tendance == Tendance.AReduire;

    public bool AAugmenter => Suivi.Tendance == Tendance.AAugmenter;

    public string Conseil => Suivi.Tendance switch
    {
        Tendance.SansDonnees => "Pas encore de mois terminé à analyser.",
        Tendance.AReduire => $"Vous dépensez en moyenne {Montants.Formater(Suivi.Moyenne)} € : budget réductible à " +
                             $"{Montants.Formater(Suivi.BudgetConseille ?? 0)} € (économie de {Montants.Formater(Suivi.Budget - (Suivi.BudgetConseille ?? 0))} €/mois).",
        Tendance.AAugmenter => $"Vous dépensez en moyenne {Montants.Formater(Suivi.Moyenne)} € (dépassé {Suivi.Depassements} fois sur {Suivi.MoisObserves}) : " +
                               $"prévoyez {Montants.Formater(Suivi.BudgetConseille ?? 0)} € pour un prévisionnel juste.",
        _ => $"Budget adapté : {Montants.Formater(Suivi.Moyenne)} € dépensés en moyenne sur {Suivi.MoisObserves} mois.",
    };

    [RelayCommand]
    private void Appliquer() => _appliquer(this);
}

public sealed class LigneSimulationViewModel : ObservableObject
{
    private readonly Action _modifie;
    private bool _garder = true;
    private decimal _nouveauMontant;

    public LigneSimulationViewModel(string nom, decimal montantActuel, bool estEnveloppe, Action modifie)
    {
        Nom = nom;
        MontantActuel = montantActuel;
        _nouveauMontant = montantActuel;
        EstEnveloppe = estEnveloppe;
        _modifie = modifie;
    }

    public string Nom { get; }

    public decimal MontantActuel { get; }

    public bool EstEnveloppe { get; }

    public string Type => EstEnveloppe ? "Enveloppe" : "Charge";

    /// <summary>Décoché = charge supprimée dans la simulation.</summary>
    public bool Garder
    {
        get => _garder;
        set { if (SetProperty(ref _garder, value)) { OnPropertyChanged(nameof(Modifiee)); _modifie(); } }
    }

    public decimal NouveauMontant
    {
        get => _nouveauMontant;
        set { if (SetProperty(ref _nouveauMontant, Math.Max(0m, value))) { OnPropertyChanged(nameof(Modifiee)); _modifie(); } }
    }

    public bool Modifiee => !Garder || NouveauMontant != MontantActuel;

    internal void Reinitialiser()
    {
        Garder = true;
        NouveauMontant = MontantActuel;
    }
}

public sealed class ObjectifViewModel : ObservableObject
{
    private readonly IReadOnlyList<ChoixPeriode> _periodes;
    private readonly Action _modifie;
    private AnalyseObjectif? _analyse;

    public ObjectifViewModel(ObjectifEpargne modele, IReadOnlyList<ChoixPeriode> periodes, Action modifie)
    {
        Modele = modele;
        _periodes = periodes;
        _modifie = modifie;
    }

    public ObjectifEpargne Modele { get; }

    public string Nom
    {
        get => Modele.Nom;
        set { if (SetProperty(Modele.Nom, value ?? "", Modele, (m, v) => m.Nom = v)) _modifie(); }
    }

    public decimal Montant
    {
        get => Modele.Montant;
        set { if (SetProperty(Modele.Montant, value, Modele, (m, v) => m.Montant = v)) _modifie(); }
    }

    public decimal DejaEpargne
    {
        get => Modele.DejaEpargne;
        set { if (SetProperty(Modele.DejaEpargne, value, Modele, (m, v) => m.DejaEpargne = v)) _modifie(); }
    }

    /// <summary>Mois d'échéance, parmi les mois futurs proposés.</summary>
    public ChoixPeriode? Echeance
    {
        get => _periodes.FirstOrDefault(p => p.Periode == Modele.Echeance);
        set
        {
            if (value is null || value.Periode == Modele.Echeance)
                return;
            Modele.Echeance = value.Periode;
            OnPropertyChanged();
            _modifie();
        }
    }

    public decimal Mensualite => _analyse?.Mensualite ?? 0m;

    public Faisabilite Faisabilite => _analyse?.Faisabilite ?? Faisabilite.Tenable;

    public string Statut => _analyse?.Faisabilite switch
    {
        Faisabilite.Atteint => "Atteint",
        Faisabilite.Tenable => "Tenable",
        Faisabilite.Juste => "Juste",
        Faisabilite.Difficile => "Difficile",
        Faisabilite.EcheancePassee => "Échéance passée",
        _ => "",
    };

    public string Detail => _analyse is null ? "" : _analyse.Faisabilite switch
    {
        Faisabilite.Atteint => "Montant déjà réuni.",
        Faisabilite.EcheancePassee => "Choisissez une échéance à venir.",
        _ => $"{Montants.Formater(_analyse.Mensualite)} € par mois pendant {_analyse.MoisRestants} mois",
    };

    public bool EstDifficile => Faisabilite is Faisabilite.Difficile or Faisabilite.EcheancePassee;

    public bool EstJuste => Faisabilite == Faisabilite.Juste;

    internal void MettreAJour(AnalyseObjectif analyse)
    {
        _analyse = analyse;
        OnPropertyChanged(nameof(Mensualite));
        OnPropertyChanged(nameof(Faisabilite));
        OnPropertyChanged(nameof(Statut));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(EstDifficile));
        OnPropertyChanged(nameof(EstJuste));
    }
}
