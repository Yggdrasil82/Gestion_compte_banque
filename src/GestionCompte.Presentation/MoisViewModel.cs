using CommunityToolkit.Mvvm.ComponentModel;
using GestionCompte.Core;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Import;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Presentation;

/// <summary>Écran d'un mois : revenus, enveloppes, opérations et résumé.</summary>
public sealed partial class MoisViewModel : ObservableObject
{
    private readonly CompteBancaire _compte;
    private readonly MoisBudget _mois;
    private readonly Action _donneesModifiees;

    private readonly ApparenceViewModel? _apparence;

    /// <param name="apparence">Réglage « ranger par catégorie » (propre au PC) ; null = rangé par catégorie.</param>
    /// <param name="comptes">Autres comptes vers lesquels un virement peut être lié ; par défaut, aucun.</param>
    public MoisViewModel(CompteBancaire compte, MoisBudget mois, Action donneesModifiees, ApparenceViewModel? apparence = null,
        ComptesLiables? comptes = null)
    {
        Comptes = comptes ?? ComptesLiables.Aucun;
        _compte = compte;
        _mois = mois;
        _donneesModifiees = donneesModifiees;
        _apparence = apparence;

        Revenus = new ListeEditable<RevenuMoisViewModel>(
            mois.Revenus.Select(r => new RevenuMoisViewModel(r, ValeurModifiee)),
            () => new RevenuMoisViewModel(new LigneRevenu("Nouveau revenu", 0m), ValeurModifiee),
            StructureModifiee);

        Enveloppes = mois.Enveloppes.Select(e => new EnveloppeMoisViewModel(e, ValeurModifiee)).ToList();

        Operations = new ListeEditable<OperationViewModel>(
            mois.Operations.Select(o => new OperationViewModel(o, ValeurModifiee, EnveloppeSuggeree, Comptes)),
            () => new OperationViewModel(new Operation("Nouvelle opération"), ValeurModifiee, EnveloppeSuggeree, Comptes),
            StructureModifiee);

        NomsEnveloppes = new[] { "" }.Concat(mois.Enveloppes.Select(e => e.Nom)).ToList();
        NomsComptesCumul = new[] { "" }.Concat(compte.Configuration.ComptesCumul.Select(c => c.Nom)).ToList();
        // Catégories de la configuration, plus celles encore utilisées dans le mois (catégorie renommée ou supprimée).
        NomsCategories = new[] { "" }
            .Concat(compte.Configuration.CategoriesOperations.Select(c => c.Nom))
            .Concat(mois.Operations.Select(o => o.CategorieOperation).OfType<string>())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _rangerParCategorie = apparence?.RangerParCategorie ?? true;

        Recalculer();
    }

    public PeriodeMois Periode => _mois.Periode;

    /// <summary>Autres comptes : choix de la colonne « Virement avec » (masquée avec un seul compte).</summary>
    public ComptesLiables Comptes { get; }

    /// <summary>Aide au-dessus du tableau des opérations.</summary>
    public string AideOperations =>
        "Cochez « Pointé » quand l'opération apparaît sur le relevé. Choisissez une enveloppe pour les dépenses de courses, carburant… : elles sont déduites de son budget."
        + (Comptes.Disponibles
            ? " Pour un virement vers un autre de vos comptes, choisissez-le dans « Virement avec » : l'opération inverse y est créée et suit vos modifications."
            : "");

    /// <summary>
    /// Enveloppe proposée pour un libellé saisi : l'enveloppe du même nom (« Courses »),
    /// sinon celle d'une règle de classement (« LECLERC » → Courses), sinon aucune.
    /// </summary>
    private string? EnveloppeSuggeree(string libelle)
    {
        var noms = _mois.Enveloppes.Select(e => e.Nom).ToList();
        return noms.FirstOrDefault(n => CalculateurMois.MemeNom(n, libelle))
            ?? ImportReleve.EnveloppePour(_compte.Configuration.Regles, libelle, noms);
    }

    public string Titre => _mois.Periode.Libelle;

    public ListeEditable<RevenuMoisViewModel> Revenus { get; }

    public IReadOnlyList<EnveloppeMoisViewModel> Enveloppes { get; }

    public ListeEditable<OperationViewModel> Operations { get; }

    /// <summary>Choix possibles dans la colonne « Enveloppe » (vide = aucune).</summary>
    public IReadOnlyList<string> NomsEnveloppes { get; }

    /// <summary>Choix possibles dans la colonne « Compte cumulé » (vide = aucun).</summary>
    public IReadOnlyList<string> NomsComptesCumul { get; }

    /// <summary>Choix possibles dans la colonne « Catégorie » (vide = aucune).</summary>
    public IReadOnlyList<string> NomsCategories { get; }

    /// <summary>Le mois a au moins une catégorie d'opérations à proposer (sinon la colonne et le bouton sont masqués).</summary>
    public bool ACategories => NomsCategories.Count > 1;

    /// <summary>
    /// Opérations rangées par catégorie (dans l'ordre de la configuration, sans catégorie à la fin),
    /// sinon dans l'ordre de saisie. Le solde ligne par ligne suit l'ordre affiché.
    /// </summary>
    [ObservableProperty] private bool _rangerParCategorie;

    partial void OnRangerParCategorieChanged(bool value)
    {
        if (_apparence is not null)
            _apparence.RangerParCategorie = value;
        Recalculer();
    }

    /// <summary>Opérations dans l'ordre affiché.</summary>
    public IReadOnlyList<OperationViewModel> OperationsAffichees =>
        RangerParCategorie
            ? Operations.Elements.OrderBy(o => o.OrdreCategorie).ThenBy(o => o.Position).ToList()
            : Operations.Elements.OrderBy(o => o.Position).ToList();

    [ObservableProperty] private decimal _ancienSolde;
    [ObservableProperty] private decimal _totalRevenus;
    [ObservableProperty] private decimal _soldeDepart;
    [ObservableProperty] private decimal _totalReserveEnveloppes;
    [ObservableProperty] private decimal _totalDebits;
    [ObservableProperty] private decimal _totalCredits;
    [ObservableProperty] private decimal _soldeFin;

    /// <summary>Réservé pour les enveloppes + débits des opérations.</summary>
    [ObservableProperty] private decimal _depensesPrevues;

    [ObservableProperty] private int _nombrePointees;

    [ObservableProperty] private string _resumePointage = "";
    [ObservableProperty] private IReadOnlyList<CompteCumulViewModel> _comptesCumul = Array.Empty<CompteCumulViewModel>();

    public bool SoldeFinNegatif => SoldeFin < 0;

    partial void OnSoldeFinChanged(decimal value) => OnPropertyChanged(nameof(SoldeFinNegatif));

    /// <summary>Recalcule tous les soldes et totaux affichés.</summary>
    public void Recalculer()
    {
        var resultat = _compte.Calculer(_mois.Periode);

        AncienSolde = resultat.AncienSolde;
        TotalRevenus = resultat.TotalRevenus;
        SoldeDepart = resultat.SoldeDepart;
        TotalReserveEnveloppes = resultat.Enveloppes.Sum(e => e.Reste);
        TotalDebits = _mois.Operations.Sum(o => o.Debit);
        TotalCredits = _mois.Operations.Sum(o => o.Credit);
        SoldeFin = resultat.SoldeFinPrevisionnel;
        DepensesPrevues = TotalReserveEnveloppes + TotalDebits;

        foreach (var (vm, etat) in Enveloppes.Zip(resultat.Enveloppes))
            vm.MettreAJour(etat);

        // Solde ligne par ligne dans l'ordre affiché, à partir du solde avant les opérations.
        var categories = _compte.Configuration.CategoriesOperations;
        for (var i = 0; i < Operations.Elements.Count; i++)
        {
            var vm = Operations.Elements[i];
            vm.Position = i;
            var index = categories.FindIndex(c => string.Equals(c.Nom, vm.Modele.CategorieOperation, StringComparison.CurrentCultureIgnoreCase));
            vm.OrdreCategorie = vm.Modele.CategorieOperation is null ? int.MaxValue : index >= 0 ? index : categories.Count;
            vm.CouleurCategorie = index >= 0 ? categories[index].Couleur : vm.Modele.CategorieOperation is null ? null : "#7D8A93";
        }

        var soldeAvant = resultat.Lignes.LastOrDefault(l => l.Type != Core.Calculs.TypeLigne.Operation)?.Solde ?? resultat.AncienSolde;
        var solde = soldeAvant;
        foreach (var vm in OperationsAffichees)
        {
            solde += vm.Credit - vm.Debit;
            vm.Solde = solde;
        }

        foreach (var groupe in Operations.Elements.GroupBy(o => o.Modele.CategorieOperation ?? ""))
        {
            var total = groupe.Sum(o => o.Credit - o.Debit);
            foreach (var vm in groupe)
                vm.TotalCategorie = total;
        }
        OnPropertyChanged(nameof(OperationsAffichees));

        NombrePointees = _mois.Operations.Count(o => o.Pointee);
        ResumePointage = $"{NombrePointees} / {_mois.Operations.Count} opérations pointées";

        ComptesCumul = _compte.ComptesCumulJusqua(_mois.Periode).Select(c => new CompteCumulViewModel(c)).ToList();
        OnPropertyChanged(nameof(CompteCumulPrincipal));
    }

    /// <summary>Premier compte cumulé (en général l'épargne), affiché dans une tuile ; null s'il n'y en a pas.</summary>
    public CompteCumulViewModel? CompteCumulPrincipal => ComptesCumul.Count > 0 ? ComptesCumul[0] : null;

    private void ValeurModifiee()
    {
        Recalculer();
        _donneesModifiees();
    }

    private void StructureModifiee()
    {
        _mois.Revenus.Clear();
        _mois.Revenus.AddRange(Revenus.Elements.Select(r => r.Modele));
        _mois.Operations.Clear();
        _mois.Operations.AddRange(Operations.Elements.Select(o => o.Modele));
        ValeurModifiee();
    }
}

public sealed class RevenuMoisViewModel : ObservableObject
{
    private readonly Action _modifie;

    public RevenuMoisViewModel(LigneRevenu modele, Action modifie)
    {
        Modele = modele;
        _modifie = modifie;
    }

    public LigneRevenu Modele { get; }

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

    /// <summary>Revenu vu sur le relevé (coché automatiquement à l'import).</summary>
    public bool Recu
    {
        get => Modele.Recu;
        set { if (SetProperty(Modele.Recu, value, Modele, (m, v) => m.Recu = v)) _modifie(); }
    }
}

public sealed partial class EnveloppeMoisViewModel : ObservableObject
{
    private readonly LigneEnveloppe _modele;
    private readonly Action _modifie;

    public EnveloppeMoisViewModel(LigneEnveloppe modele, Action modifie)
    {
        _modele = modele;
        _modifie = modifie;
    }

    public string Nom => _modele.Nom;

    public decimal Budget
    {
        get => _modele.Budget;
        set { if (SetProperty(_modele.Budget, value, _modele, (m, v) => m.Budget = v)) _modifie(); }
    }

    [ObservableProperty] private decimal _depense;
    [ObservableProperty] private decimal _reste;
    [ObservableProperty] private bool _depassee;

    /// <summary>Part du budget dépensée, de 0 à 100.</summary>
    [ObservableProperty] private double _progression;

    /// <summary>Budget presque entièrement dépensé (80 % ou plus), sans être dépassé.</summary>
    public bool PresqueAtteint => !Depassee && Progression >= 80;

    internal void MettreAJour(Core.Calculs.EtatEnveloppe etat)
    {
        Depense = etat.Depense;
        Reste = etat.Reste;
        Depassee = etat.Depassee;
        Progression = etat.Budget <= 0 ? (etat.Depense > 0 ? 100 : 0) : (double)Math.Clamp(etat.Depense / etat.Budget * 100, 0, 100);
        OnPropertyChanged(nameof(PresqueAtteint));
    }
}

public sealed partial class OperationViewModel : ObservableObject
{
    private readonly Action _modifie;

    private readonly Func<string, string?>? _enveloppeSuggeree;

    private readonly ComptesLiables _comptes;

    /// <param name="enveloppeSuggeree">Enveloppe proposée pour un nouveau libellé (remplie si aucune n'est choisie).</param>
    /// <param name="comptes">Autres comptes vers lesquels l'opération peut être un virement lié.</param>
    public OperationViewModel(Operation modele, Action modifie, Func<string, string?>? enveloppeSuggeree = null, ComptesLiables? comptes = null)
    {
        Modele = modele;
        _modifie = modifie;
        _enveloppeSuggeree = enveloppeSuggeree;
        _comptes = comptes ?? ComptesLiables.Aucun;
    }

    /// <summary>
    /// Compte de l'autre côté du virement (« Livret A ») ; chaîne vide = opération simple.
    /// L'opération inverse est créée, modifiée et supprimée avec celle-ci dans l'autre compte.
    /// </summary>
    public string CompteLie
    {
        get => _comptes.Nom(Modele.CompteLie);
        set
        {
            var id = _comptes.Id(value);
            if (string.Equals(id, Modele.CompteLie, StringComparison.OrdinalIgnoreCase))
                return;
            Modele.CompteLie = id;
            Modele.IdLien = id is null ? null : Modele.IdLien ?? Core.VirementsLies.NouveauLien();
            OnPropertyChanged();
            OnPropertyChanged(nameof(EstLie));
            OnPropertyChanged(nameof(InfoLien));
            _modifie();
        }
    }

    public bool EstLie => Modele.IdLien is not null && Modele.CompteLie is not null;

    /// <summary>Infobulle de la colonne « Virement avec ».</summary>
    public string InfoLien => EstLie
        ? $"Virement lié avec « {CompteLie} » : l'opération inverse ({(Debit > 0 ? "crédit" : "débit")}) y est tenue à jour automatiquement. " +
          "Le pointage reste propre à chaque compte."
        : "Choisissez un autre compte pour en faire un virement : l'opération inverse y sera créée automatiquement, dans le même mois.";

    public Operation Modele { get; }

    public string Libelle
    {
        get => Modele.Libelle;
        set
        {
            if (!SetProperty(Modele.Libelle, value ?? "", Modele, (m, v) => m.Libelle = v))
                return;
            // Une dépense sans enveloppe ni compte cumulé reçoit l'enveloppe correspondant à son libellé.
            if (Modele.Enveloppe is null && Modele.CompteCumul is null && _enveloppeSuggeree?.Invoke(Modele.Libelle) is { } enveloppe)
            {
                Modele.Enveloppe = enveloppe;
                OnPropertyChanged(nameof(Enveloppe));
            }
            _modifie();
        }
    }

    public decimal Debit
    {
        get => Modele.Debit;
        set { if (SetProperty(Modele.Debit, value, Modele, (m, v) => m.Debit = v)) _modifie(); }
    }

    public decimal Credit
    {
        get => Modele.Credit;
        set { if (SetProperty(Modele.Credit, value, Modele, (m, v) => m.Credit = v)) _modifie(); }
    }

    public bool Pointee
    {
        get => Modele.Pointee;
        set { if (SetProperty(Modele.Pointee, value, Modele, (m, v) => m.Pointee = v)) _modifie(); }
    }

    /// <summary>Enveloppe de la dépense ; chaîne vide = aucune.</summary>
    public string Enveloppe
    {
        get => Modele.Enveloppe ?? "";
        set { if (SetProperty(Modele.Enveloppe, VideVersNull(value), Modele, (m, v) => m.Enveloppe = v)) _modifie(); }
    }

    /// <summary>Compte cumulé alimenté ; chaîne vide = aucun.</summary>
    public string CompteCumul
    {
        get => Modele.CompteCumul ?? "";
        set { if (SetProperty(Modele.CompteCumul, VideVersNull(value), Modele, (m, v) => m.CompteCumul = v)) _modifie(); }
    }

    /// <summary>Catégorie d'opérations choisie à la main (ex. « Agen ») ; chaîne vide = aucune.</summary>
    public string Categorie
    {
        get => Modele.CategorieOperation ?? "";
        set
        {
            if (SetProperty(Modele.CategorieOperation, VideVersNull(value), Modele, (m, v) => m.CategorieOperation = v))
            {
                OnPropertyChanged(nameof(GroupeCategorie));
                _modifie();
            }
        }
    }

    /// <summary>Titre du groupe affiché quand les opérations sont rangées par catégorie.</summary>
    public string GroupeCategorie => Modele.CategorieOperation ?? "Sans catégorie";

    /// <summary>Rang de la catégorie dans la configuration (sans catégorie : à la fin).</summary>
    [ObservableProperty] private int _ordreCategorie;

    /// <summary>Position dans l'ordre de saisie.</summary>
    [ObservableProperty] private int _position;

    /// <summary>Couleur de la catégorie (« #RRGGBB »), null sans catégorie.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ACategorie), nameof(CouleurBande), nameof(CouleurGroupe))]
    private string? _couleurCategorie;

    /// <summary>Liseré de couleur à gauche de la ligne (transparent sans catégorie).</summary>
    public string CouleurBande => CouleurCategorie ?? "Transparent";

    /// <summary>Pastille du bandeau de groupe (gris sans catégorie).</summary>
    public string CouleurGroupe => CouleurCategorie ?? "#7D8A93";

    public bool ACategorie => CouleurCategorie is not null;

    /// <summary>Total (crédits − débits) des opérations de la même catégorie, affiché dans le bandeau du groupe.</summary>
    [ObservableProperty] private decimal _totalCategorie;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SoldeNegatif))]
    private decimal _solde;

    public bool SoldeNegatif => Solde < 0;

    /// <summary>Opération venue d'un relevé importé.</summary>
    public bool Importee => Modele.IdentifiantBanque is not null;

    internal static string? VideVersNull(string? texte) => string.IsNullOrWhiteSpace(texte) ? null : texte;
}

public sealed class CompteCumulViewModel
{
    public CompteCumulViewModel(EtatCompteCumul etat) => Etat = etat;

    public EtatCompteCumul Etat { get; }

    public string Nom => Etat.Nom;

    public decimal Total => Etat.Total;

    public bool AObjectif => Etat.Objectif is not null;

    public string Detail => Etat.Objectif is { } objectif
        ? $"Objectif {Montants.Formater(objectif)} € — reste {Montants.Formater(Etat.Reste ?? 0m)} €"
        : "";

    /// <summary>Part de l'objectif atteinte, de 0 à 100.</summary>
    public double Progression => Etat.Objectif is { } objectif && objectif > 0
        ? (double)Math.Clamp(Etat.Total / objectif * 100, 0, 100)
        : 0;
}
