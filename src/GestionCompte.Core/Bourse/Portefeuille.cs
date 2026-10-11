namespace GestionCompte.Core.Bourse;

/// <summary>Enveloppe fiscale du placement.</summary>
public enum EnveloppeBourse
{
    CompteTitres,
    Pea,
}

public enum TypeOperationBourse
{
    Achat,
    Vente,
    Versement,
    Retrait,
    Dividende,
    Interets,
    Frais,
    Impot,
    Autre,
}

/// <summary>
/// Une ligne du relevé du courtier (export CSV de Trade Republic).
/// Les montants suivent le compte espèces : négatifs pour ce qui sort (achat, frais, taxe), positifs pour ce qui entre.
/// </summary>
/// <param name="Identifiant">Identifiant de l'opération chez le courtier (évite les doublons à chaque import).</param>
/// <param name="Quantite">Nombre de titres (positif à l'achat, négatif à la vente), 0 pour les mouvements d'espèces.</param>
/// <param name="Montant">Montant de l'opération hors frais et taxes (achat : −quantité × prix).</param>
/// <param name="Frais">Frais de courtage (négatifs).</param>
/// <param name="Taxes">Taxes (taxe sur les transactions financières, prélèvements…), négatives.</param>
public sealed record OperationBourse(
    string Identifiant, DateTime Date, EnveloppeBourse Enveloppe, TypeOperationBourse Type,
    string? Nom, string? Isin, string? Classe, decimal Quantite, decimal Prix, decimal Montant, decimal Frais, decimal Taxes,
    string Description)
{
    /// <summary>Effet sur les espèces du compte : montant, frais et taxes.</summary>
    public decimal Net => Montant + Frais + Taxes;

    public bool EstTitre => Isin is not null && Type is TypeOperationBourse.Achat or TypeOperationBourse.Vente;
}

/// <summary>Cours d'un titre : récupéré sur internet ou saisi à la main.</summary>
/// <param name="Symbole">Code du titre chez Yahoo Finance (ex. « TTE.PA »), trouvé à partir de l'ISIN ou corrigé à la main.</param>
public sealed record CoursTitre(decimal? Valeur, DateTime? Date, bool Manuel, string? Symbole);

/// <summary>Les placements en bourse d'un compte : opérations importées et derniers cours connus.</summary>
public sealed class Portefeuille
{
    public List<OperationBourse> Operations { get; } = new();

    /// <summary>Derniers cours, par ISIN.</summary>
    public Dictionary<string, CoursTitre> Cours { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Vide => Operations.Count == 0;

    /// <summary>Ajoute les opérations pas encore connues (même identifiant) ; renvoie le nombre d'ajouts.</summary>
    public int Ajouter(IEnumerable<OperationBourse> operations) => Importer(operations).Ajoutees;

    /// <summary>
    /// Ajoute les opérations pas encore connues et remplace celles qui ont changé (même identifiant, lues autrement :
    /// correction de l'import), de sorte qu'importer à nouveau le même export corrige le portefeuille.
    /// </summary>
    public (int Ajoutees, int Corrigees) Importer(IEnumerable<OperationBourse> operations)
    {
        var connues = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < Operations.Count; i++)
            connues.TryAdd(Operations[i].Identifiant, i);
        var ajoutees = 0;
        var corrigees = 0;
        foreach (var operation in operations)
        {
            if (connues.TryGetValue(operation.Identifiant, out var index))
            {
                if (Operations[index] != operation)
                {
                    Operations[index] = operation;
                    corrigees++;
                }
                continue;
            }
            connues[operation.Identifiant] = Operations.Count;
            Operations.Add(operation);
            ajoutees++;
        }
        Operations.Sort((a, b) => a.Date.CompareTo(b.Date));
        return (ajoutees, corrigees);
    }
}
