namespace GestionCompte.Core.Modeles;

/// <summary>
/// Dépense ou recette ponctuelle prévue pour un mois futur (vacances, prime, taxe foncière…).
/// Elle compte dans le prévisionnel et est ajoutée automatiquement au mois quand il est créé.
/// </summary>
public sealed class OperationPrevue
{
    public OperationPrevue(PeriodeMois periode, string libelle, decimal debit = 0m, decimal credit = 0m)
    {
        Periode = periode;
        Libelle = libelle;
        Debit = debit;
        Credit = credit;
    }

    public PeriodeMois Periode { get; set; }
    public string Libelle { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    /// <summary>Compte cumulé alimenté par le débit (ex. « Épargne »), ou null.</summary>
    public string? CompteCumul { get; set; }

    internal Operation VersOperation() => new(Libelle, Debit, Credit) { CompteCumul = CompteCumul };
}
