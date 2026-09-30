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
    private readonly ServicesIA? _ia;
    private readonly Func<DateTime> _aujourdhui;

    /// <param name="donneesModifiees">Appelé après une modification des simulations (à enregistrer).</param>
    /// <param name="previsionnelModifie">Appelé après l'ajout ou le retrait des échéances (à enregistrer, prévisionnel à recalculer).</param>
    public CreditsViewModel(CompteBancaire compte, IReadOnlyList<ChoixPeriode> periodes, IDialogues dialogues,
        Action donneesModifiees, Action previsionnelModifie, ServicesIA? ia = null, Func<DateTime>? aujourdhui = null)
    {
        _ia = ia;
        if (_ia is not null)
            _ia.Patiente += (_, message) => { if (RechercheEnCours) StatutTaux = message; };
        _aujourdhui = aujourdhui ?? (() => DateTime.Today);
        if (ia is not null)
            ia.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IADisponibles));
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

    // ---- Taux du moment (sur internet quand la clé le permet, sinon de mémoire : taux indicatifs) ----

    public bool IADisponibles => _ia?.Disponibles == true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChercherTauxCommand))]
    private bool _rechercheEnCours;

    [ObservableProperty] private IReadOnlyList<TauxMarcheViewModel> _tauxTrouves = Array.Empty<TauxMarcheViewModel>();
    [ObservableProperty] private string _statutTaux = "";
    [ObservableProperty] private string _erreurTaux = "";

    /// <summary>Catégorie cherchée (ex. « crédit immobilier à taux fixe sur 20 ans… »).</summary>
    [ObservableProperty] private string _categorieCherchee = "";

    /// <summary>Taux d'usure trouvé pour la catégorie cherchée.</summary>
    [ObservableProperty] private decimal? _usure;

    public bool AvecTaux => TauxTrouves.Count > 0;

    partial void OnTauxTrouvesChanged(IReadOnlyList<TauxMarcheViewModel> value) => OnPropertyChanged(nameof(AvecTaux));

    partial void OnUsureChanged(decimal? value) => OnPropertyChanged(nameof(AlerteUsure));

    partial void OnCategorieChercheeChanged(string value) => OnPropertyChanged(nameof(AlerteUsure));

    /// <summary>Catégorie de la simulation sélectionnée (ce qui sera envoyé aux IA).</summary>
    public string CategorieSelection => Selection is { } s ? RechercheTaux.Categorie(s.Modele.Type, s.Modele.DureeMois, s.Modele.Montant) : "";

    /// <summary>Avertissement si le TAEG approché de la simulation dépasse le taux d'usure trouvé pour sa catégorie.</summary>
    public string AlerteUsure
    {
        get
        {
            if (Selection is not { } s || Usure is not { } usure || CategorieSelection != CategorieCherchee)
                return "";
            var taeg = RechercheTaux.TaegApproche(s.Modele);
            return taeg > usure
                ? $"Attention : avec l'assurance, le taux de « {s.Nom} » (environ {taeg.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR"))} %) dépasse le taux d'usure ({usure.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR"))} %) : une banque ne peut pas le proposer."
                : "";
        }
    }

    private bool PeutChercherTaux() => ASelection && !RechercheEnCours;

    [RelayCommand(CanExecute = nameof(PeutChercherTaux))]
    private async Task ChercherTaux()
    {
        if (Selection is null)
            return;
        var assistants = _ia?.Assistants() ?? Array.Empty<Data.Achats.IAssistantIA>();
        if (assistants.Count == 0)
        {
            ErreurTaux = _ia?.Actives == false
                ? "Les IA sont coupées (Configuration › Intelligence artificielle)."
                : "Aucune IA réglée : saisissez une clé Gemini ou Groq (Configuration › Intelligence artificielle).";
            return;
        }

        var categorie = CategorieSelection;
        var demande = RechercheTaux.Demande(categorie, _aujourdhui());
        RechercheEnCours = true;
        ErreurTaux = "";
        TauxTrouves = Array.Empty<TauxMarcheViewModel>();
        StatutTaux = $"Recherche des taux par {string.Join(" et ", assistants.Select(a => a.Nom))}…";
        try
        {
            var reponses = await Task.WhenAll(assistants.Select(async ia =>
            {
                try
                {
                    var taux = RechercheTaux.Extraire(await ia.DemanderAsync(demande, avecRecherche: true), ia.Nom);
                    if (taux is not null && ia.SansRecherche)
                    {
                        // Sans recherche, les adresses citées seraient inventées.
                        taux.SansRecherche = true;
                        taux.Liens.Clear();
                    }
                    return (ia.Nom, Taux: taux, Erreur: taux is null ? $"{ia.Nom} n'a pas trouvé de taux." : null);
                }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException)
                {
                    return (ia.Nom, Taux: (TauxMarche?)null, Erreur: e is OperationCanceledException ? $"{ia.Nom} n'a pas répondu à temps." : e.Message);
                }
            }));
            var trouves = reponses.Where(r => r.Taux is not null).Select(r => r.Taux!).ToList();
            TauxTrouves = trouves.Select(t => new TauxMarcheViewModel(t)).ToList();
            CategorieCherchee = categorie;
            Usure = RechercheTaux.Usure(trouves);
            ErreurTaux = string.Join("\n", reponses.Where(r => r.Erreur is not null).Select(r => r.Erreur));
            StatutTaux = trouves.Count == 0 ? ""
                : trouves.All(t => t.SansRecherche)
                    ? "Taux indicatifs donnés de mémoire par les IA, à vérifier auprès d'une banque ou d'un courtier : ce ne sont pas des offres."
                    : "Moyennes trouvées sur internet, à vérifier sur les sources : ce ne sont pas des offres de banque.";
        }
        finally
        {
            RechercheEnCours = false;
        }
    }

    /// <summary>Reprend le taux moyen trouvé (et l'assurance, si elle est en % par an) dans la simulation sélectionnée.</summary>
    [RelayCommand]
    private void ReprendreTaux(TauxMarcheViewModel? taux)
    {
        if (taux is null || Selection is not { } s || taux.Taux.Moyen is not { } moyen)
            return;
        s.TauxAnnuel = moyen;
        if (taux.Taux.Assurance is { } assurance && s.Modele.TypeAssurance == Core.Modeles.TypeAssurance.Pourcentage)
            s.Assurance = assurance;
        StatutTaux = $"Taux moyen de {taux.Nom} repris dans « {s.Nom} ».";
    }

    [RelayCommand]
    private void OuvrirSource(SourceTaux? source)
    {
        if (source is not null)
            _dialogues.OuvrirLien(source.Adresse);
    }

    private void SelectionChangee()
    {
        OnPropertyChanged(nameof(CategorieSelection));
        OnPropertyChanged(nameof(AlerteUsure));
        ChercherTauxCommand.NotifyCanExecuteChanged();
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

/// <summary>Taux trouvés par une IA, pour l'affichage.</summary>
public sealed class TauxMarcheViewModel
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    public TauxMarcheViewModel(TauxMarche taux) => Taux = taux;

    public TauxMarche Taux { get; }
    public string Nom => Taux.Source;
    public string Bas => Texte(Taux.Bas);
    public string Moyen => Texte(Taux.Moyen);
    public string Haut => Texte(Taux.Haut);
    public string Usure => Texte(Taux.Usure);
    public string Assurance => Texte(Taux.Assurance);
    public string Periode => Taux.Periode;
    public bool Reprenable => Taux.Moyen is not null;
    public IReadOnlyList<SourceTaux> Liens => Taux.Liens;
    public bool SansRecherche => Taux.SansRecherche;
    public string Avertissement => Taux.SansRecherche
        ? $"Taux indicatifs, non vérifiés sur internet : {Nom} a répondu de mémoire car votre clé gratuite n'inclut pas la recherche internet."
        : "";

    private static string Texte(decimal? taux) => taux is { } t ? t.ToString("0.00", Francais) + " %" : "—";
}
