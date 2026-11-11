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

    /// <summary>Import qui a validé ce revenu (depuis la 2.20.2), ou null.</summary>
    public TraceImport? Trace { get; set; }
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

    /// <summary>Catégorie d'opérations choisie à la main (ex. « Agen »), ou null.</summary>
    public string? CategorieOperation { get; set; }

    /// <summary>Identifiant de l'opération bancaire importée (FITID du fichier OFX), ou null si saisie à la main.</summary>
    public string? IdentifiantBanque { get; set; }

    /// <summary>Import qui a validé ou ajouté cette opération (depuis la 2.20.2), ou null.</summary>
    public TraceImport? Trace { get; set; }

    /// <summary>Fichier de l'autre compte (ex. « compte-2.db ») pour un virement entre comptes, ou null.</summary>
    /// <remarks>L'opération inverse (crédit pour un débit) est créée et tenue à jour dans l'autre compte.</remarks>
    public string? CompteLie { get; set; }

    /// <summary>Identifiant commun à ce virement et à son double dans l'autre compte, ou null.</summary>
    public string? IdLien { get; set; }
}

/// <summary>Import d'un relevé qui a validé une opération ou un revenu : quand, et la ligne du relevé telle que la banque l'a écrite.</summary>
/// <param name="Montant">Montant de la ligne du relevé (négatif pour un débit).</param>
public sealed record TraceImport(DateTime Import, DateOnly DateReleve, string LibelleBanque, decimal Montant)
{
    private static readonly System.Globalization.CultureInfo Francais = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Ex. « Validée par l'import du 11/11/2026 à 23:10. Relevé : 08/11/2026 « CB CARREFOUR » −45,50 € ».</summary>
    public string Texte(string validee = "Validée") =>
        $"{validee} par l'import du {Import.ToString("dd/MM/yyyy 'à' HH:mm", Francais)}.\n" +
        $"Relevé : {DateReleve.ToString("dd/MM/yyyy", Francais)} « {LibelleBanque} » " +
        $"{(Montant < 0 ? "\u2212" : "+")}{Math.Abs(Montant).ToString("#,##0.00", Francais)} €";
}
