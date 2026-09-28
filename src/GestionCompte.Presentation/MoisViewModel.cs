using CommunityToolkit.Mvvm.ComponentModel;
using GestionCompte.Core;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Presentation;

/// <summary>Écran d'un mois : revenus, enveloppes, opérations et résumé.</summary>
public sealed partial class MoisViewModel : ObservableObject
{
    private readonly CompteBancaire _compte;
    private readonly MoisBudget _mois;
    private readonly Action _donneesModifiees;

    public MoisViewModel(CompteBancaire compte, MoisBudget mois, Action donneesModifiees)
    {
        _compte = compte;
        _mois = mois;
        _donneesModifiees = donneesModifiees;

        Revenus = new ListeEditable<RevenuMoisViewModel>(
            mois.Revenus.Select(r => new RevenuMoisViewModel(r, ValeurModifiee)),
            () => new RevenuMoisViewModel(new LigneRevenu("Nouveau revenu", 0m), ValeurModifiee),
            StructureModifiee);

        Enveloppes = mois.Enveloppes.Select(e => new EnveloppeMoisViewModel(e, ValeurModifiee)).ToList();

        Operations = new ListeEditable<OperationViewModel>(
            mois.Operations.Select(o => new OperationViewModel(o, ValeurModifiee)),
            () => new OperationViewModel(new Operation("Nouvelle opération"), ValeurModifiee),
            StructureModifiee);

        NomsEnveloppes = new[] { "" }.Concat(mois.Enveloppes.Select(e => e.Nom)).ToList();
        NomsComptesCumul = new[] { "" }.Concat(compte.Configuration.ComptesCumul.Select(c => c.Nom)).ToList();

        Recalculer();
    }

    public PeriodeMois Periode => _mois.Periode;

    public string Titre => _mois.Periode.Libelle;

    public ListeEditable<RevenuMoisViewModel> Revenus { get; }

    public IReadOnlyList<EnveloppeMoisViewModel> Enveloppes { get; }

    public ListeEditable<OperationViewModel> Operations { get; }

    /// <summary>Choix possibles dans la colonne « Enveloppe » (vide = aucune).</summary>
    public IReadOnlyList<string> NomsEnveloppes { get; }

    /// <summary>Choix possibles dans la colonne « Compte cumulé » (vide = aucun).</summary>
    public IReadOnlyList<string> NomsComptesCumul { get; }

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

        var soldesOperations = resultat.Lignes.Where(l => l.Type == Core.Calculs.TypeLigne.Operation).Select(l => l.Solde);
        foreach (var (vm, solde) in Operations.Elements.Zip(soldesOperations))
            vm.Solde = solde;

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

    public OperationViewModel(Operation modele, Action modifie)
    {
        Modele = modele;
        _modifie = modifie;
    }

    public Operation Modele { get; }

    public string Libelle
    {
        get => Modele.Libelle;
        set { if (SetProperty(Modele.Libelle, value ?? "", Modele, (m, v) => m.Libelle = v)) _modifie(); }
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
