namespace GestionCompte.Core.Modeles;

/// <summary>
/// Paramètres généraux (équivalent de la feuille « Configuration » du fichier Excel).
/// Ils servent de valeurs par défaut à chaque nouveau mois.
/// </summary>
public sealed class ConfigurationBudget
{
    /// <summary>Premier mois géré par l'application.</summary>
    public PeriodeMois PremierMois { get; set; }

    /// <summary>Solde du compte au démarrage (« Solde actuel »).</summary>
    public decimal SoldeInitial { get; set; }

    /// <summary>Revenus du mois : salaire, NDF, CAF, autres…</summary>
    public List<ModeleRevenu> Revenus { get; } = new();

    /// <summary>Enveloppes à budget : courses, carburant…</summary>
    public List<ModeleEnveloppe> Enveloppes { get; } = new();

    /// <summary>Charges mensuelles préremplies à chaque nouveau mois : loyer, crédits, abonnements…</summary>
    public List<ModeleCharge> Charges { get; } = new();

    /// <summary>Comptes cumulés d'un mois sur l'autre : épargne, remboursement…</summary>
    public List<CompteCumul> ComptesCumul { get; } = new();
}

public sealed record ModeleRevenu(string Nom, decimal MontantParDefaut);

public sealed record ModeleEnveloppe(string Nom, decimal BudgetParDefaut);

/// <param name="CompteCumul">Nom du compte cumulé alimenté par cette charge (ex. « Épargne »), ou null.</param>
public sealed record ModeleCharge(string Nom, decimal Debit, decimal Credit = 0m, string? CompteCumul = null);

/// <param name="MontantInitial">Montant déjà cumulé avant le premier mois.</param>
/// <param name="Objectif">Montant total à atteindre (ex. somme à rembourser), ou null s'il n'y en a pas.</param>
public sealed record CompteCumul(string Nom, decimal MontantInitial = 0m, decimal? Objectif = null);
