using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Core;
using GestionCompte.Core.Bourse;
using GestionCompte.Core.Impots;
using GestionCompte.Core.Modeles;
using GestionCompte.Data.Impots;

namespace GestionCompte.Presentation;

/// <summary>
/// Module « Impôts » : estimation de l'impôt sur le revenu de l'année à partir des revenus de tous les comptes (salaires,
/// loyers) et des plus-values du module Bourse, comparaison avec le prélèvement à la source et pistes pour payer moins.
/// </summary>
public sealed partial class ImpotsViewModel : ObservableObject
{
    /// <summary>Net imposable ≈ net avant impôt × 1,03 (CSG et CRDS non déductibles, mutuelle de l'employeur).</summary>
    public const decimal CoefficientNetImposable = 1.03m;

    public static readonly IReadOnlyList<string> NomsTypes = new[] { "Non imposable", "Salaire", "Loyer location vide", "Loyer location meublée" };

    public static readonly IReadOnlyList<string> NomsSituations = new[] { "Personne seule", "Couple (marié ou pacsé)" };

    private readonly FichierImpots _fichier;
    private readonly Func<IReadOnlyList<(string Nom, CompteBancaire Compte)>> _comptes;
    private readonly int _annee;
    private bool _chargement;

    /// <param name="dossier">Dossier des données (« impots.json »).</param>
    /// <param name="comptes">Tous les comptes, lus à chaque ouverture du module.</param>
    public ImpotsViewModel(string dossier, Func<IReadOnlyList<(string Nom, CompteBancaire Compte)>> comptes, DateTime aujourdhui)
    {
        _fichier = new FichierImpots(dossier);
        _comptes = comptes;
        _annee = aujourdhui.Year;
        try
        {
            _fichier.Charger();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Erreur = $"Les réglages des impôts n'ont pas pu être lus : {e.Message}";
        }
        CreerParametres();
    }

    public int Annee => _annee;

    public string SousTitre =>
        $"Estimation de l'impôt sur les revenus {_annee} (déclarés au printemps {_annee + 1}) à partir de tous vos comptes. " +
        "Indicatif : seul l'avis d'impôt fait foi.";

    public IReadOnlyList<string> Types => NomsTypes;

    public IReadOnlyList<string> Situations => NomsSituations;

    /// <summary>Personnes du foyer à qui un salaire peut être rattaché (déduction de 10 % et plafond PER par personne).</summary>
    public IReadOnlyList<string> Personnes => PersonnesFoyer.Toutes;

    [ObservableProperty] private string _erreur = "";

    public ObservableCollection<LigneRevenuImpot> Lignes { get; } = new();

    public ObservableCollection<RevenuManuelViewModel> RevenusManuels { get; } = new();

    public ObservableCollection<ParametreImpotViewModel> Parametres { get; } = new();

    [ObservableProperty] private EstimationImpot _estimation = new();

    [ObservableProperty] private IReadOnlyList<PisteImpot> _pistes = Array.Empty<PisteImpot>();

    [ObservableProperty] private IReadOnlyList<LigneDetailImpot> _detail = Array.Empty<LigneDetailImpot>();

    /// <summary>Plus-values du compte-titres de l'année, tous comptes confondus (module Bourse).</summary>
    [ObservableProperty] private decimal _plusValues;

    public bool AvecBourse => PlusValues != 0;

    // Foyer
    public int Situation
    {
        get => Foyer.EnCouple ? 1 : 0;
        set => Modifier(() => Foyer.EnCouple = value == 1);
    }

    public int Enfants
    {
        get => Foyer.Enfants;
        set => Modifier(() => Foyer.Enfants = Math.Clamp(value, 0, 20));
    }

    public decimal TauxPrelevement
    {
        get => Foyer.TauxPrelevement;
        set => Modifier(() => Foyer.TauxPrelevement = Math.Clamp(value, 0, 60));
    }

    public decimal? FraisReels
    {
        get => Foyer.FraisReels;
        set => Modifier(() => Foyer.FraisReels = value is > 0 ? value : null);
    }

    public decimal VersementsPer
    {
        get => Foyer.VersementsPer;
        set => Modifier(() => Foyer.VersementsPer = Math.Max(0, value));
    }

    public decimal Dons
    {
        get => Foyer.Dons;
        set => Modifier(() => Foyer.Dons = Math.Max(0, value));
    }

    public decimal EmploiDomicile
    {
        get => Foyer.EmploiDomicile;
        set => Modifier(() => Foyer.EmploiDomicile = Math.Max(0, value));
    }

    public bool OptionBareme
    {
        get => Foyer.OptionBareme;
        set => Modifier(() => Foyer.OptionBareme = value);
    }

    private FoyerFiscal Foyer => _fichier.Foyer;

    private void Modifier(Action changement)
    {
        changement();
        Enregistrer();
        Recalculer();
    }

    /// <summary>Relit les comptes (à l'ouverture du module).</summary>
    public void Actualiser()
    {
        _chargement = true;
        Lignes.Clear();
        decimal plusValues = 0;
        foreach (var (nom, compte) in _comptes())
        {
            foreach (var (revenu, montant) in RevenusDeLAnnee(compte, _annee))
            {
                var affectation = _fichier.Affectations.FirstOrDefault(a => a.Compte == nom && a.Revenu == revenu);
                Lignes.Add(new LigneRevenuImpot(this, nom, revenu, montant, affectation?.Type ?? TypeParDefaut(revenu),
                    affectation?.MontantImposable, affectation?.Personne));
            }
            if (!compte.Portefeuille.Vide)
                plusValues += CalculBourse.Calculer(compte.Portefeuille, null, _annee).PlusValuesImposables;
        }
        RevenusManuels.Clear();
        foreach (var manuel in _fichier.RevenusManuels)
            RevenusManuels.Add(new RevenuManuelViewModel(this, manuel.Libelle, manuel.Type, manuel.Montant, manuel.Personne));
        PlusValues = plusValues;
        OnPropertyChanged(nameof(AvecBourse));
        _chargement = false;
        Recalculer();
    }

    /// <summary>
    /// Revenus d'un compte sur l'année : montant des mois saisis, et montant par défaut de la configuration pour les mois pas
    /// encore créés (l'année est ainsi estimée en entier).
    /// </summary>
    public static IReadOnlyList<(string Revenu, decimal Montant)> RevenusDeLAnnee(CompteBancaire compte, int annee)
    {
        var noms = compte.Configuration.Revenus.Select(r => r.Nom)
            .Concat(compte.Mois.Where(m => m.Periode.Annee == annee).SelectMany(m => m.Revenus).Select(r => r.Nom))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var resultat = new List<(string, decimal)>();
        foreach (var nom in noms)
        {
            decimal total = 0;
            for (var mois = 1; mois <= 12; mois++)
            {
                var saisi = compte.Trouver(new PeriodeMois(annee, mois));
                total += saisi is not null
                    ? saisi.Revenus.Where(r => string.Equals(r.Nom, nom, StringComparison.CurrentCultureIgnoreCase)).Sum(r => r.Montant)
                    : compte.Configuration.Revenus.Where(r => string.Equals(r.Nom, nom, StringComparison.CurrentCultureIgnoreCase))
                        .Sum(r => r.MontantParDefaut);
            }
            if (total != 0)
                resultat.Add((nom, total));
        }
        return resultat;
    }

    /// <summary>Type deviné d'après le nom du revenu (modifiable dans le module).</summary>
    public static TypeRevenuFiscal TypeParDefaut(string revenu)
    {
        var nom = revenu.ToLowerInvariant();
        if (nom.Contains("meubl"))
            return TypeRevenuFiscal.LoyerMeuble;
        if (nom.Contains("loyer") || nom.Contains("location"))
            return TypeRevenuFiscal.LoyerNu;
        if (nom.Contains("salaire") || nom.Contains("paie") || nom.Contains("paye") || nom.Contains("pension") || nom.Contains("retraite"))
            return TypeRevenuFiscal.Salaire;
        return TypeRevenuFiscal.NonImposable;
    }

    /// <summary>Salaire net imposable estimé à partir du net reçu (après prélèvement à la source).</summary>
    public decimal EstimerNetImposable(decimal netRecu)
    {
        var taux = Math.Clamp(Foyer.TauxPrelevement, 0, 60) / 100;
        return Math.Round(netRecu / (1 - taux) * CoefficientNetImposable, 0, MidpointRounding.AwayFromZero);
    }

    internal void LigneModifiee()
    {
        if (_chargement)
            return;
        Enregistrer();
        Recalculer();
    }

    private IReadOnlyList<RevenuFiscal> RevenusFiscaux() =>
        Lignes.Where(l => l.Type != TypeRevenuFiscal.NonImposable)
            .Select(l => new RevenuFiscal($"{l.Revenu} ({l.Compte})", l.Type, l.MontantRetenu, l.Personne))
            .Concat(RevenusManuels.Where(r => r.Type != TypeRevenuFiscal.NonImposable && r.Montant != 0)
                .Select(r => new RevenuFiscal(r.Libelle, r.Type, r.Montant, r.Personne)))
            .ToList();

    private void Recalculer()
    {
        if (_chargement)
            return;
        foreach (var ligne in Lignes)
            ligne.ActualiserEstimation();
        var revenus = RevenusFiscaux();
        Estimation = CalculImpot.Calculer(_fichier.Parametres, Foyer, revenus, PlusValues);
        Pistes = CalculImpot.Pistes(_fichier.Parametres, Foyer, revenus, PlusValues);
        Detail = ConstruireDetail(Estimation);
        foreach (var nom in new[]
                 {
                     nameof(Situation), nameof(Enfants), nameof(TauxPrelevement), nameof(FraisReels), nameof(VersementsPer), nameof(Dons),
                     nameof(EmploiDomicile), nameof(OptionBareme), nameof(TexteImpot), nameof(TexteTmi), nameof(TexteTauxMoyen),
                     nameof(TexteSociaux), nameof(TexteTotal), nameof(TextePrelevement), nameof(SoldeAPayer), nameof(AvecPrelevement),
                     nameof(TexteParts), nameof(Remarques), nameof(AvecRemarques),
                 })
            OnPropertyChanged(nom);
    }

    private static IReadOnlyList<LigneDetailImpot> ConstruireDetail(EstimationImpot e)
    {
        var lignes = new List<LigneDetailImpot>();
        void Ajouter(string libelle, decimal montant, bool important = false) => lignes.Add(new LigneDetailImpot(libelle, montant, important));
        if (e.Salaires > 0)
        {
            Ajouter("Salaires nets imposables", e.Salaires);
            Ajouter(e.FraisReelsRetenus ? "Frais réels" : "Déduction de 10 %", -e.DeductionSalaires);
        }
        if (e.LoyersNus > 0)
        {
            Ajouter("Loyers de location vide", e.LoyersNus);
            Ajouter("Abattement micro-foncier (30 %)", e.FoncierImposable - e.LoyersNus);
        }
        if (e.LoyersMeubles > 0)
        {
            Ajouter("Loyers de location meublée", e.LoyersMeubles);
            Ajouter("Abattement micro-BIC (50 %)", e.MeubleImposable - e.LoyersMeubles);
        }
        if (e.DeductionPer > 0)
            Ajouter("Versements PER déduits", -e.DeductionPer);
        Ajouter("Revenu net imposable", e.RevenuImposable, true);
        Ajouter(e.QuotientPlafonne ? "Impôt du barème (quotient familial plafonné)" : "Impôt du barème", e.ImpotBrut);
        if (e.Decote > 0)
            Ajouter("Décote", -e.Decote);
        if (e.ReductionDons > 0)
            Ajouter("Réduction pour dons", -e.ReductionDons);
        if (e.CreditEmploiDomicile > 0)
            Ajouter("Crédit d'impôt emploi à domicile", -e.CreditEmploiDomicile);
        Ajouter("Impôt sur le revenu", e.ImpotRevenu, true);
        if (e.ImpotForfaitaire > 0)
            Ajouter("Flat tax sur les plus-values (12,8 %)", e.ImpotForfaitaire);
        if (e.PrelevementsSociaux > 0)
            Ajouter("Prélèvements sociaux (loyers, plus-values)", e.PrelevementsSociaux);
        Ajouter("Total estimé", e.Total, true);
        return lignes;
    }

    public string TexteImpot => Estimation.ImpotRevenu < 0
        ? $"{Montants.Formater(-Estimation.ImpotRevenu)} € remboursés"
        : $"{Montants.Formater(Estimation.ImpotRevenu)} €";

    public string TexteTmi => CalculImpot.Pourcent(Estimation.Tmi);

    public string TexteTauxMoyen => $"{Estimation.TauxMoyen.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} %";

    public string TexteSociaux => $"{Montants.Formater(Estimation.ImpotForfaitaire + Estimation.PrelevementsSociaux)} €";

    public string TexteTotal => $"{Montants.Formater(Estimation.Total)} €";

    public string TexteParts => $"{Estimation.Parts.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} part{(Estimation.Parts > 1 ? "s" : "")}";

    public bool AvecPrelevement => Foyer.TauxPrelevement > 0;

    public bool SoldeAPayer => Estimation.Solde > 0;

    public string TextePrelevement
    {
        get
        {
            var e = Estimation;
            if (Foyer.TauxPrelevement <= 0)
                return $"Saisissez votre taux de prélèvement à la source (fiche de paie) pour savoir s'il restera à payer. Taux qui correspondrait à " +
                       $"l'estimation : {e.TauxCalcule.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} %.";
            var taux = e.TauxCalcule.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
            var debut = $"Prélevé sur l'année à {Foyer.TauxPrelevement.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} % : " +
                        $"{Montants.Formater(e.Preleve)} €. ";
            return Math.Abs(e.Solde) < 50
                ? debut + "C'est à peu près l'impôt estimé : rien de notable à payer ni à recevoir."
                : e.Solde > 0
                    ? debut + $"Il resterait environ {Montants.Formater(e.Solde)} € à payer à l'été {_annee + 1}. Taux qui correspondrait : {taux} % " +
                      "(modifiable sur impots.gouv.fr, Gérer mon prélèvement à la source)."
                    : debut + $"Environ {Montants.Formater(-e.Solde)} € seraient remboursés à l'été {_annee + 1}. Taux qui correspondrait : {taux} %.";
        }
    }

    public IReadOnlyList<string> Remarques => Estimation.Remarques;

    public bool AvecRemarques => Estimation.Remarques.Count > 0;

    // Revenus ajoutés à la main
    [RelayCommand]
    private void AjouterRevenu()
    {
        RevenusManuels.Add(new RevenuManuelViewModel(this, "Autre revenu", TypeRevenuFiscal.LoyerNu, 0));
        LigneModifiee();
    }

    [RelayCommand]
    private void RetirerRevenu(RevenuManuelViewModel? revenu)
    {
        if (revenu is null || !RevenusManuels.Remove(revenu))
            return;
        LigneModifiee();
    }

    // Barème
    private void CreerParametres()
    {
        Parametres.Clear();
        var p = _fichier.Parametres;
        void Ajouter(string libelle, Func<decimal> lire, Action<decimal> ecrire, bool pourcent = false) =>
            Parametres.Add(new ParametreImpotViewModel(libelle, lire, ecrire, pourcent, ParametreModifie));
        var tranches = p.Tranches.OrderBy(t => t.Seuil).ToList();
        p.Tranches = tranches;
        for (var i = 1; i < tranches.Count; i++)
        {
            var index = i;
            Ajouter($"Tranche {i} : à partir de (€ par part)", () => p.Tranches[index].Seuil,
                v => p.Tranches[index] = p.Tranches[index] with { Seuil = v });
            Ajouter($"Tranche {i} : taux", () => p.Tranches[index].Taux, v => p.Tranches[index] = p.Tranches[index] with { Taux = v }, true);
        }
        Ajouter("Plafond du quotient familial par demi-part", () => p.PlafondDemiPart, v => p.PlafondDemiPart = v);
        Ajouter("Plafond pour le 1er enfant d'un parent seul", () => p.PlafondParentIsole, v => p.PlafondParentIsole = v);
        Ajouter("Décote : personne seule", () => p.DecoteSeul, v => p.DecoteSeul = v);
        Ajouter("Décote : couple", () => p.DecoteCouple, v => p.DecoteCouple = v);
        Ajouter("Décote : taux", () => p.TauxDecote, v => p.TauxDecote = v, true);
        Ajouter("Déduction des salaires", () => p.TauxAbattementSalaires, v => p.TauxAbattementSalaires = v, true);
        Ajouter("Déduction des salaires : minimum", () => p.AbattementMinimum, v => p.AbattementMinimum = v);
        Ajouter("Déduction des salaires : maximum", () => p.AbattementMaximum, v => p.AbattementMaximum = v);
        Ajouter("Micro-foncier : abattement", () => p.AbattementMicroFoncier, v => p.AbattementMicroFoncier = v, true);
        Ajouter("Micro-foncier : plafond des loyers", () => p.PlafondMicroFoncier, v => p.PlafondMicroFoncier = v);
        Ajouter("Micro-BIC meublé : abattement", () => p.AbattementMicroBic, v => p.AbattementMicroBic = v, true);
        Ajouter("Micro-BIC meublé : plafond des loyers", () => p.PlafondMicroBic, v => p.PlafondMicroBic = v);
        Ajouter("Prélèvements sociaux : location vide", () => p.PrelevementsSociauxFonciers, v => p.PrelevementsSociauxFonciers = v, true);
        Ajouter("Prélèvements sociaux : meublé et bourse", () => p.PrelevementsSociauxPlacements, v => p.PrelevementsSociauxPlacements = v, true);
        Ajouter("Flat tax : part impôt", () => p.TauxForfaitaire, v => p.TauxForfaitaire = v, true);
        Ajouter("PER : plafond minimum", () => p.PlafondPerMinimum, v => p.PlafondPerMinimum = v);
        Ajouter("PER : plafond maximum", () => p.PlafondPerMaximum, v => p.PlafondPerMaximum = v);
        Ajouter("Emploi à domicile : plafond des dépenses", () => p.PlafondEmploiDomicile, v => p.PlafondEmploiDomicile = v);
    }

    private void ParametreModifie()
    {
        Enregistrer();
        Recalculer();
    }

    [RelayCommand]
    private void BaremeParDefaut()
    {
        _fichier.Parametres = new ParametresImpot();
        CreerParametres();
        ParametreModifie();
    }

    private void Enregistrer()
    {
        if (_chargement)
            return;
        _fichier.Affectations = Lignes.Select(l => new AffectationRevenu(l.Compte, l.Revenu, l.Type, l.MontantImposable, l.Personne))
            // Les revenus des comptes non relus (autre année) gardent leur réglage.
            .Concat(_fichier.Affectations.Where(a => !Lignes.Any(l => l.Compte == a.Compte && l.Revenu == a.Revenu)))
            .ToList();
        _fichier.RevenusManuels = RevenusManuels.Select(r => new RevenuManuel(r.Libelle, r.Type, r.Montant, r.Personne)).ToList();
        try
        {
            _fichier.Enregistrer();
            Erreur = "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Erreur = $"Les réglages des impôts n'ont pas pu être enregistrés : {e.Message}";
        }
    }

    // Assistant
    /// <summary>« Analyser avec l'Assistant » : l'application ouvre l'Assistant et lui pose la question.</summary>
    public event EventHandler<string>? AnalyseDemandee;

    [RelayCommand]
    private void Analyser() => AnalyseDemandee?.Invoke(this, QuestionAnalyse());

    public string QuestionAnalyse()
    {
        var e = Estimation;
        var texte = new StringBuilder();
        texte.Append($"Voici l'estimation de l'impôt sur mes revenus {_annee} ({(Foyer.EnCouple ? "couple" : "personne seule")}, ");
        texte.Append($"{Foyer.Enfants} enfant{(Foyer.Enfants > 1 ? "s" : "")}, {TexteParts}) : ");
        texte.Append(string.Join(" ; ", Detail.Select(l => $"{l.Libelle} {Montants.Formater(l.Montant)} €")));
        texte.Append($". Tranche marginale {TexteTmi}, taux moyen {TexteTauxMoyen}.");
        if (Foyer.VersementsPer > 0 || Foyer.Dons > 0 || Foyer.EmploiDomicile > 0)
            texte.Append($" Déjà versé cette année : PER {Montants.Formater(Foyer.VersementsPer)} €, dons {Montants.Formater(Foyer.Dons)} €, " +
                         $"emploi à domicile {Montants.Formater(Foyer.EmploiDomicile)} €.");
        texte.Append(" Quelles solutions légales me conseilles-tu pour réduire mes impôts, avec l'économie estimée et les points de vigilance ?");
        return texte.ToString();
    }
}

/// <summary>Une ligne du détail du calcul.</summary>
public sealed record LigneDetailImpot(string Libelle, decimal Montant, bool Important);

/// <summary>Un revenu d'un compte, avec son type fiscal et son montant imposable.</summary>
public sealed partial class LigneRevenuImpot : ObservableObject
{
    private readonly ImpotsViewModel _module;

    public LigneRevenuImpot(ImpotsViewModel module, string compte, string revenu, decimal montantRecu, TypeRevenuFiscal type,
        decimal? montantImposable, string? personne = null)
    {
        _module = module;
        _personne = PersonnesFoyer.De(personne);
        Compte = compte;
        Revenu = revenu;
        MontantRecu = montantRecu;
        _type = type;
        _montantImposable = montantImposable;
    }

    public string Compte { get; }
    public string Revenu { get; }

    /// <summary>Total de l'année reçu sur le compte (mois saisis et montants par défaut pour les autres).</summary>
    public decimal MontantRecu { get; }

    [ObservableProperty] private TypeRevenuFiscal _type;

    /// <summary>Index dans la liste des types (pour la liste déroulante).</summary>
    public int IndexType
    {
        get => (int)Type;
        set => Type = (TypeRevenuFiscal)value;
    }

    /// <summary>Personne du foyer qui perçoit le revenu (utile pour les salaires).</summary>
    [ObservableProperty] private string _personne;

    /// <summary>Index dans la liste des personnes (pour la liste déroulante).</summary>
    public int IndexPersonne
    {
        get => Math.Max(0, PersonnesFoyer.Toutes.ToList().IndexOf(Personne));
        set => Personne = PersonnesFoyer.Toutes[Math.Clamp(value, 0, PersonnesFoyer.Toutes.Count - 1)];
    }

    /// <summary>La personne ne compte que pour les salaires.</summary>
    public bool AvecPersonne => Type == TypeRevenuFiscal.Salaire;

    partial void OnPersonneChanged(string value)
    {
        OnPropertyChanged(nameof(IndexPersonne));
        _module.LigneModifiee();
    }

    partial void OnTypeChanged(TypeRevenuFiscal value)
    {
        OnPropertyChanged(nameof(IndexType));
        OnPropertyChanged(nameof(AvecPersonne));
        _module.LigneModifiee();
    }

    /// <summary>Montant imposable saisi (cumul net imposable de la fiche de paie de décembre), ou null pour l'estimation.</summary>
    [ObservableProperty] private decimal? _montantImposable;

    partial void OnMontantImposableChanged(decimal? value) => _module.LigneModifiee();

    /// <summary>Montant pris dans le calcul.</summary>
    public decimal MontantRetenu => MontantImposable ?? (Type == TypeRevenuFiscal.Salaire ? _module.EstimerNetImposable(MontantRecu) : MontantRecu);

    public string Estimation => Type switch
    {
        TypeRevenuFiscal.NonImposable => "pas imposé",
        _ when MontantImposable is not null => "montant saisi",
        TypeRevenuFiscal.Salaire => $"≈ {Montants.Formater(MontantRetenu)} € net imposable",
        _ => "loyers perçus",
    };

    internal void ActualiserEstimation()
    {
        OnPropertyChanged(nameof(MontantRetenu));
        OnPropertyChanged(nameof(Estimation));
    }
}

/// <summary>Revenu perçu hors des comptes suivis, ajouté à la main.</summary>
public sealed partial class RevenuManuelViewModel : ObservableObject
{
    private readonly ImpotsViewModel _module;

    public RevenuManuelViewModel(ImpotsViewModel module, string libelle, TypeRevenuFiscal type, decimal montant, string? personne = null)
    {
        _module = module;
        _personne = PersonnesFoyer.De(personne);
        _libelle = libelle;
        _type = type;
        _montant = montant;
    }

    [ObservableProperty] private string _libelle;
    [ObservableProperty] private TypeRevenuFiscal _type;
    [ObservableProperty] private decimal _montant;

    public int IndexType
    {
        get => (int)Type;
        set => Type = (TypeRevenuFiscal)value;
    }

    partial void OnLibelleChanged(string value) => _module.LigneModifiee();

    /// <summary>Personne du foyer qui perçoit le revenu (utile pour les salaires).</summary>
    [ObservableProperty] private string _personne;

    /// <summary>Index dans la liste des personnes (pour la liste déroulante).</summary>
    public int IndexPersonne
    {
        get => Math.Max(0, PersonnesFoyer.Toutes.ToList().IndexOf(Personne));
        set => Personne = PersonnesFoyer.Toutes[Math.Clamp(value, 0, PersonnesFoyer.Toutes.Count - 1)];
    }

    /// <summary>La personne ne compte que pour les salaires.</summary>
    public bool AvecPersonne => Type == TypeRevenuFiscal.Salaire;

    partial void OnPersonneChanged(string value)
    {
        OnPropertyChanged(nameof(IndexPersonne));
        _module.LigneModifiee();
    }

    partial void OnTypeChanged(TypeRevenuFiscal value)
    {
        OnPropertyChanged(nameof(IndexType));
        OnPropertyChanged(nameof(AvecPersonne));
        _module.LigneModifiee();
    }

    partial void OnMontantChanged(decimal value) => _module.LigneModifiee();
}

/// <summary>Une valeur du barème, modifiable (les taux s'affichent en %).</summary>
public sealed class ParametreImpotViewModel : ObservableObject
{
    private readonly Func<decimal> _lire;
    private readonly Action<decimal> _ecrire;
    private readonly Action _modifie;

    public ParametreImpotViewModel(string libelle, Func<decimal> lire, Action<decimal> ecrire, bool pourcent, Action modifie)
    {
        Libelle = libelle;
        _lire = lire;
        _ecrire = ecrire;
        Pourcent = pourcent;
        _modifie = modifie;
    }

    public string Libelle { get; }
    public bool Pourcent { get; }
    public string Unite => Pourcent ? "%" : "€";

    public decimal Valeur
    {
        get => Pourcent ? _lire() * 100 : _lire();
        set
        {
            if (value < 0 || value == Valeur)
                return;
            _ecrire(Pourcent ? value / 100 : value);
            OnPropertyChanged();
            _modifie();
        }
    }
}
