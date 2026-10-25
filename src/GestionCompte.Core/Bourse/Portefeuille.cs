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

    /// <summary>Secteur de chaque titre (Pharmaceutique, Énergie…), par ISIN, choisi à la main.</summary>
    public Dictionary<string, string> Secteurs { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Idées d'investissement : titres à surveiller, pas encore achetés.</summary>
    public List<IdeeInvestissement> Idees { get; } = new();

    /// <summary>Carte des idées d'investissement repliée.</summary>
    public bool IdeesRepliees { get; set; }

    public bool Vide => Operations.Count == 0;

    /// <summary>Rien à enregistrer : ni opération, ni cours, ni secteur, ni idée.</summary>
    public bool SansDonnees => Vide && Cours.Count == 0 && Secteurs.Count == 0 && Idees.Count == 0 && !IdeesRepliees;

    /// <summary>Secteur du titre, « Non classé » s'il n'a pas été choisi.</summary>
    public string SecteurDe(string isin) => Secteurs.TryGetValue(isin, out var secteur) && !string.IsNullOrWhiteSpace(secteur)
        ? secteur
        : SecteursBourse.NonClasse;

    /// <summary>Recopie les cours, secteurs et idées dans un autre portefeuille (les opérations sont ajoutées à part).</summary>
    public void CopierReglagesDans(Portefeuille autre)
    {
        foreach (var (isin, cours) in Cours)
            autre.Cours[isin] = cours;
        foreach (var (isin, secteur) in Secteurs)
            autre.Secteurs[isin] = secteur;
        autre.Idees.AddRange(Idees.Select(i => i with { }));
        autre.IdeesRepliees = IdeesRepliees;
    }

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

/// <summary>Titre à surveiller avant de l'acheter.</summary>
/// <param name="Recherche">ISIN ou code Yahoo (ex. « AI.PA ») pour trouver le cours ; vide : le nom est cherché.</param>
public sealed record IdeeInvestissement(string Nom, string? Secteur = null, string? Recherche = null, string? Note = null,
    decimal? Cours = null, DateTime? DateCours = null, string? Symbole = null);

/// <summary>Secteurs proposés pour ranger les titres (la liste s'allonge des secteurs saisis à la main).</summary>
public static class SecteursBourse
{
    public const string NonClasse = "Non classé";

    public static readonly IReadOnlyList<string> Proposes = new[]
    {
        "ETF", "ETF monde", "Pharmaceutique", "Santé", "Semi-conducteurs", "Technologie", "Énergie", "Pétrole et gaz", "Matières premières",
        "Défense et aéronautique", "Industrie", "Banque et assurance", "Consommation", "Luxe et cosmétique", "Immobilier", "Télécoms et médias", "Autre",
    };
}
