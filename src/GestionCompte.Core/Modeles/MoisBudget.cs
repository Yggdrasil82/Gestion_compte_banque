namespace GestionCompte.Core.Modeles;

/// <summary>
/// Les données saisies pour un mois (équivalent d'une feuille « Octobre 2026 »).
/// Les soldes ne sont pas stockés : ils sont recalculés par <see cref="Calculs.CalculateurMois"/>.
/// </summary>
public sealed class MoisBudget
{
    public MoisBudget(PeriodeMois periode) => Periode = periode;

    public PeriodeMois Periode { get; }

    /// <summary>Revenus de ce mois, modifiables (primes, NDF…).</summary>
    public List<LigneRevenu> Revenus { get; } = new();

    /// <summary>Budgets des enveloppes pour ce mois.</summary>
    public List<LigneEnveloppe> Enveloppes { get; } = new();

    /// <summary>Charges mensuelles et opérations saisies, dans l'ordre d'affichage.</summary>
    public List<Operation> Operations { get; } = new();
}

public sealed class LigneRevenu
{
    public LigneRevenu(string nom, decimal montant)
    {
        Nom = nom;
        Montant = montant;
    }

    public string Nom { get; set; }
    public decimal Montant { get; set; }

    /// <summary>Revenu vu sur le relevé bancaire (import ou coche manuelle).</summary>
    public bool Recu { get; set; }

    /// <summary>Identifiant de l'opération bancaire importée (FITID du fichier OFX), ou null.</summary>
    public string? IdentifiantBanque { get; set; }
}

public sealed class LigneEnveloppe
{
    public LigneEnveloppe(string nom, decimal budget)
    {
        Nom = nom;
        Budget = budget;
    }

    public string Nom { get; set; }
    public decimal Budget { get; set; }
}

public sealed class Operation
{
    public Operation(string libelle, decimal debit = 0m, decimal credit = 0m)
    {
        Libelle = libelle;
        Debit = debit;
        Credit = credit;
    }

    public string Libelle { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    /// <summary>Opération vue sur le relevé bancaire (colonne « Validation » d'Excel).</summary>
    public bool Pointee { get; set; }

    /// <summary>Enveloppe à laquelle cette dépense est imputée (ex. « Courses »), ou null.</summary>
    public string? Enveloppe { get; set; }

    /// <summary>Compte cumulé alimenté par le débit de cette opération (ex. « Épargne »), ou null.</summary>
    public string? CompteCumul { get; set; }

    /// <summary>Identifiant de l'opération bancaire importée (FITID du fichier OFX), ou null si saisie à la main.</summary>
    public string? IdentifiantBanque { get; set; }
}
