using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Core;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;
using GestionCompte.Data;

namespace GestionCompte.Presentation;

/// <summary>
/// Module « Crédits » : plusieurs offres comparées côte à côte, le détail de la simulation
/// choisie (tableau d'amortissement, endettement), le calcul inverse et l'ajout des échéances au prévisionnel.
/// </summary>
public sealed partial class CreditsViewModel : ObservableObject
{
    private readonly CompteBancaire _compte;
    private readonly IDialogues _dialogues;
    private readonly Action _donneesModifiees;
    private readonly Action _previsionnelModifie;

    /// <param name="donneesModifiees">Appelé après une modification des simulations (à enregistrer).</param>
    /// <param name="previsionnelModifie">Appelé après l'ajout ou le retrait des échéances (à enregistrer, prévisionnel à recalculer).</param>
    public CreditsViewModel(CompteBancaire compte, IReadOnlyList<ChoixPeriode> periodes, IDialogues dialogues,
        Action donneesModifiees, Action previsionnelModifie)
    {
        _compte = compte;
        _dialogues = dialogues;
        _donneesModifiees = donneesModifiees;
        _previsionnelModifie = previsionnelModifie;
        Periodes = periodes;

        Liste = new ListeEditable<SimulationCreditViewModel>(
            compte.SimulationsCredit.Select(Creer),
            () => Creer(new SimulationCredit($"Simulation {compte.SimulationsCredit.Count + 1}", 200000m, 3.5m, 240,
                periodes[0].Periode, 0.30m)),
            ListeReorganisee);
        Liste.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ListeEditable<SimulationCreditViewModel>.Selection))
                SelectionChangee();
        };
        Liste.Selection = Liste.Elements.FirstOrDefault();

        MensualiteMaximale = Math.Max(0m, decimal.Floor(AideBudget.CapaciteMensuelle(compte)));
    }

    public ListeEditable<SimulationCreditViewModel> Liste { get; }

    public IReadOnlyList<ChoixPeriode> Periodes { get; }

    public static readonly IReadOnlyList<string> NomsTypes = new[] { "Immobilier", "Auto / moto", "Consommation" };
    public static readonly IReadOnlyList<string> NomsUnites = new[] { "ans", "mois" };
    public static readonly IReadOnlyList<string> NomsTypesAssurance = new[] { "% par an", "€ par mois" };

    // Listes des cellules du tableau (propriétés d'instance : l'écran ne lie pas les propriétés statiques).
    public IReadOnlyList<string> Types => NomsTypes;
    public IReadOnlyList<string> Unites => NomsUnites;
    public IReadOnlyList<string> TypesAssurance => NomsTypesAssurance;

    public SimulationCreditViewModel? Selection => Liste.Selection;

    public bool ASelection => Liste.Selection is not null;

    // Calcul inverse : combien emprunter pour une mensualité donnée, aux conditions de la simulation choisie.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MontantEmpruntable))]
    [NotifyCanExecuteChangedFor(nameof(CreerDepuisMensualiteCommand))]
    private decimal _mensualiteMaximale;

    public decimal MontantEmpruntable => Selection is { } s ? Credit.MontantEmpruntable(MensualiteMaximale, s.Modele) : 0m;

    public string ConditionsInverse => Selection is { } s
        ? $"sur {s.DureeTexte} à {s.TauxAnnuel.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR"))} %, assurance comprise"
        : "Choisissez une simulation.";

    [RelayCommand(CanExecute = nameof(PeutCreerDepuisMensualite))]
    private void CreerDepuisMensualite()
    {
        var modele = Selection!.Modele;
        var copie = new SimulationCredit($"{modele.Nom} ({Montants.Formater(MensualiteMaximale)} €/mois)", MontantEmpruntable,
            modele.TauxAnnuel, modele.DureeMois, modele.PremiereEcheance, modele.Assurance, modele.TypeAssurance) { Type = modele.Type };
        var vm = Creer(copie);
        Liste.Elements.Add(vm);
        Liste.Selection = vm;
        ListeReorganisee();
    }

    private bool PeutCreerDepuisMensualite() => Selection is not null && MontantEmpruntable > 0;

    [RelayCommand(CanExecute = nameof(PeutAjouterAuPrevisionnel))]
    private void AjouterAuPrevisionnel()
    {
        var s = Selection!;
        var tableau = s.Resultat.Tableau;
        var dejaCrees = tableau.Count(l => _compte.Trouver(l.Periode) is not null);
        var message =
            $"Ajouter les {tableau.Count} échéances de « {Credit.Libelle(s.Modele)} » " +
            $"({Montants.Formater(s.Mensualite)} € par mois, de {tableau[0].Periode.Libelle} à {tableau[^1].Periode.Libelle}) ?" +
            (dejaCrees > 0 ? $"\n\n{dejaCrees} échéance(s) tombent dans des mois déjà créés : elles y sont ajoutées comme opérations non pointées." : "") +
            "\n\nVous pourrez les retirer avec « Retirer du prévisionnel ».";
        if (!_dialogues.Confirmer("Ajouter au prévisionnel", message))
            return;

        Credit.AjouterAuPrevisionnel(_compte, s.Modele);
        _previsionnelModifie();
        RafraichirTout();
    }

    private bool PeutAjouterAuPrevisionnel() => Selection is { Resultat.Tableau.Count: > 0 };

    [RelayCommand(CanExecute = nameof(PeutRetirerDuPrevisionnel))]
    private void RetirerDuPrevisionnel()
    {
        var s = Selection!;
        if (!_dialogues.Confirmer("Retirer du prévisionnel",
                $"Retirer les échéances « {Credit.Libelle(s.Modele)} » du prévisionnel et des mois (sauf celles déjà pointées) ?"))
            return;

        Credit.RetirerDuPrevisionnel(_compte, s.Modele);
        _previsionnelModifie();
        RafraichirTout();
    }

    private bool PeutRetirerDuPrevisionnel() => Selection is { EstAuPrevisionnel: true };

    [RelayCommand(CanExecute = nameof(PeutAjouterAuPrevisionnel))]
    private void Exporter()
    {
        var s = Selection!;
        var destination = _dialogues.ChoisirFichierExport($"{Credit.Libelle(s.Modele)}.xlsx");
        if (destination is null)
            return;
        try
        {
            ExportExcel.ExporterCredit(s.Modele, destination);
        }
        catch (Exception e)
        {
            _dialogues.Erreur($"Le fichier Excel n'a pas pu être créé (est-il ouvert dans Excel ?).\n\n{e.Message}");
        }
    }

    /// <summary>Recalcule les simulations (après un changement des revenus, charges ou opérations prévues).</summary>
    public void RafraichirTout()
    {
        foreach (var simulation in Liste.Elements)
            simulation.Rafraichir();
        SelectionChangee();
    }

    private SimulationCreditViewModel Creer(SimulationCredit modele) => new(modele, _compte, Periodes, SimulationModifiee);

    private void SimulationModifiee()
    {
        SelectionChangee();
        _donneesModifiees();
    }

    private void ListeReorganisee()
    {
        _compte.SimulationsCredit.Clear();
        _compte.SimulationsCredit.AddRange(Liste.Elements.Select(s => s.Modele));
        SelectionChangee();
        _donneesModifiees();
    }

    private void SelectionChangee()
    {
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(ASelection));
        OnPropertyChanged(nameof(MontantEmpruntable));
        OnPropertyChanged(nameof(ConditionsInverse));
        CreerDepuisMensualiteCommand.NotifyCanExecuteChanged();
        AjouterAuPrevisionnelCommand.NotifyCanExecuteChanged();
        RetirerDuPrevisionnelCommand.NotifyCanExecuteChanged();
        ExporterCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>Une simulation de crédit : ses conditions (modifiables) et ses résultats.</summary>
public sealed class SimulationCreditViewModel : ObservableObject
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");
    private readonly CompteBancaire _compte;
    private readonly IReadOnlyList<ChoixPeriode> _periodes;
    private readonly Action _modifie;
    private bool _enAnnees;

    public SimulationCreditViewModel(SimulationCredit modele, CompteBancaire compte, IReadOnlyList<ChoixPeriode> periodes, Action modifie)
    {
        Modele = modele;
        _compte = compte;
        _periodes = periodes;
        _modifie = modifie;
        _enAnnees = modele.DureeMois % 12 == 0;
        Resultat = Credit.Calculer(modele);
        Endettement = Credit.CalculerEndettement(compte.Configuration, Resultat.Mensualite);
    }

    public SimulationCredit Modele { get; }

    public ResultatCredit Resultat { get; private set; }

    public Endettement Endettement { get; private set; }

    public string Nom
    {
        get => Modele.Nom;
        set => Modifier(value ?? "", Modele.Nom, v => Modele.Nom = v);
    }

    public string Type
    {
        get => CreditsViewModel.NomsTypes[(int)Modele.Type];
        set
        {
            var index = CreditsViewModel.NomsTypes.ToList().IndexOf(value);
            if (index >= 0)
                Modifier((TypeCredit)index, Modele.Type, v => Modele.Type = v);
        }
    }

    public decimal Montant
    {
        get => Modele.Montant;
        set => Modifier(Math.Max(0m, value), Modele.Montant, v => Modele.Montant = v);
    }

    public decimal TauxAnnuel
    {
        get => Modele.TauxAnnuel;
        set => Modifier(Math.Max(0m, value), Modele.TauxAnnuel, v => Modele.TauxAnnuel = v);
    }

    /// <summary>Durée dans l'unité choisie (ans ou mois).</summary>
    public int Duree
    {
        get => _enAnnees ? Modele.DureeMois / 12 : Modele.DureeMois;
        set
        {
            var mois = Math.Clamp(_enAnnees ? value * 12 : value, 1, Credit.DureeMaximale);
            Modifier(mois, Modele.DureeMois, v => Modele.DureeMois = v);
        }
    }

    public string Unite
    {
        get => _enAnnees ? CreditsViewModel.NomsUnites[0] : CreditsViewModel.NomsUnites[1];
        set
        {
            var enAnnees = value == CreditsViewModel.NomsUnites[0];
            if (enAnnees == _enAnnees)
                return;
            // La durée saisie garde son nombre : 20 mois devient 20 ans, et inversement.
            var nombre = Duree;
            _enAnnees = enAnnees;
            OnPropertyChanged();
            Duree = nombre;
        }
    }

    public decimal Assurance
    {
        get => Modele.Assurance;
        set => Modifier(Math.Max(0m, value), Modele.Assurance, v => Modele.Assurance = v);
    }

    public string TypeAssurance
    {
        get => CreditsViewModel.NomsTypesAssurance[(int)Modele.TypeAssurance];
        set
        {
            var index = CreditsViewModel.NomsTypesAssurance.ToList().IndexOf(value);
            if (index >= 0)
                Modifier((Core.Modeles.TypeAssurance)index, Modele.TypeAssurance, v => Modele.TypeAssurance = v);
        }
    }

    public ChoixPeriode? PremiereEcheance
    {
        get => _periodes.FirstOrDefault(p => p.Periode == Modele.PremiereEcheance) ?? new ChoixPeriode(Modele.PremiereEcheance);
        set
        {
            if (value is not null)
                Modifier(value.Periode, Modele.PremiereEcheance, v => Modele.PremiereEcheance = v);
        }
    }

    public string DureeTexte => Modele.DureeMois % 12 == 0
        ? $"{Modele.DureeMois / 12} an{(Modele.DureeMois / 12 > 1 ? "s" : "")}"
        : $"{Modele.DureeMois} mois";

    public decimal Mensualite => Resultat.Mensualite;
    public decimal MensualiteHorsAssurance => Resultat.MensualiteHorsAssurance;
    public decimal AssuranceMensuelle => Resultat.AssuranceMensuelle;
    public decimal CoutInterets => Resultat.CoutInterets;
    public decimal CoutAssurance => Resultat.CoutAssurance;
    public decimal CoutTotal => Resultat.CoutTotal;
    public IReadOnlyList<LigneAmortissement> Tableau => Resultat.Tableau;

    public string TauxEndettement => Endettement.Taux is { } taux ? (taux * 100).ToString("0.0", Francais) + " %" : "—";

    public bool EndettementExcessif => Endettement.Excessif;

    public string DetailEndettement => Endettement.Revenus <= 0
        ? "Aucun revenu dans la configuration : endettement non calculable."
        : $"({Montants.Formater(Endettement.CreditsExistants)} € de crédits en cours" +
          (Endettement.NomsCredits.Count > 0 ? $" : {string.Join(", ", Endettement.NomsCredits)}" : "") +
          $" + {Montants.Formater(Endettement.NouveauCredit)} €) ÷ {Montants.Formater(Endettement.Revenus)} € de revenus. " +
          $"Les banques demandent en général {Credit.SeuilEndettement * 100:0} % au plus.";

    /// <summary>Excédent moyen par mois après ce crédit (hors ses échéances déjà au prévisionnel).</summary>
    public decimal ResteApres => Credit.CapaciteSansOperations(_compte, Credit.Libelle(Modele)) - Resultat.Mensualite;

    public bool ResteNegatif => ResteApres < 0;

    public bool EstAuPrevisionnel => Credit.EstAuPrevisionnel(_compte, Modele);

    public string EtatPrevisionnel => EstAuPrevisionnel ? "Au prévisionnel" : "";

    /// <summary>Recalcule les résultats et prévient l'écran.</summary>
    public void Rafraichir()
    {
        Resultat = Credit.Calculer(Modele);
        Endettement = Credit.CalculerEndettement(_compte.Configuration, Resultat.Mensualite);
        foreach (var nom in new[]
                 {
                     nameof(Resultat), nameof(Endettement), nameof(Mensualite), nameof(MensualiteHorsAssurance), nameof(AssuranceMensuelle),
                     nameof(CoutInterets), nameof(CoutAssurance), nameof(CoutTotal), nameof(Tableau), nameof(TauxEndettement),
                     nameof(EndettementExcessif), nameof(DetailEndettement), nameof(ResteApres), nameof(ResteNegatif),
                     nameof(EstAuPrevisionnel), nameof(EtatPrevisionnel), nameof(DureeTexte), nameof(Duree),
                 })
            OnPropertyChanged(nom);
    }

    private void Modifier<T>(T valeur, T actuelle, Action<T> affecter, [System.Runtime.CompilerServices.CallerMemberName] string? propriete = null)
    {
        if (EqualityComparer<T>.Default.Equals(valeur, actuelle))
            return;
        affecter(valeur);
        OnPropertyChanged(propriete);
        Rafraichir();
        _modifie();
    }
}
