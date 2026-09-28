using GestionCompte.Core.Modeles;

namespace GestionCompte.Core.Calculs;

public enum TypeLigne
{
    Revenu,
    Enveloppe,
    Operation,
}

/// <summary>Une ligne du tableau d'un mois, avec son solde courant.</summary>
public sealed record LigneCalculee(TypeLigne Type, string Libelle, decimal Debit, decimal Credit, decimal Solde);

/// <param name="Reste">Partie du budget pas encore dépensée, jamais négative.</param>
public sealed record EtatEnveloppe(string Nom, decimal Budget, decimal Depense, decimal Reste)
{
    public bool Depassee => Depense > Budget;
}

public sealed record ResultatMois(
    PeriodeMois Periode,
    decimal AncienSolde,
    decimal TotalRevenus,
    IReadOnlyList<LigneCalculee> Lignes,
    IReadOnlyList<EtatEnveloppe> Enveloppes)
{
    /// <summary>Ancien solde + revenus (« Solde : » dans Excel).</summary>
    public decimal SoldeDepart => AncienSolde + TotalRevenus;

    /// <summary>Solde prévisionnel de fin de mois (cellule K5 d'Excel).</summary>
    public decimal SoldeFinPrevisionnel => Lignes.Count == 0 ? SoldeDepart : Lignes[^1].Solde;
}

/// <summary>Calcule les soldes d'un mois, comme le faisaient les formules du fichier Excel.</summary>
public static class CalculateurMois
{
    public static ResultatMois Calculer(MoisBudget mois, decimal ancienSolde)
    {
        ArgumentNullException.ThrowIfNull(mois);

        var lignes = new List<LigneCalculee>();
        var solde = ancienSolde;

        foreach (var revenu in mois.Revenus)
        {
            solde += revenu.Montant;
            lignes.Add(new LigneCalculee(TypeLigne.Revenu, revenu.Nom, 0m, revenu.Montant, solde));
        }

        // Une enveloppe réserve la partie de son budget pas encore dépensée ;
        // les dépenses déjà saisies sont débitées plus bas, dans les opérations.
        var enveloppes = mois.Enveloppes.Select(e => EtatDe(e, mois.Operations)).ToList();
        foreach (var enveloppe in enveloppes)
        {
            solde -= enveloppe.Reste;
            lignes.Add(new LigneCalculee(TypeLigne.Enveloppe, enveloppe.Nom, enveloppe.Reste, 0m, solde));
        }

        foreach (var operation in mois.Operations)
        {
            solde += operation.Credit - operation.Debit;
            lignes.Add(new LigneCalculee(TypeLigne.Operation, operation.Libelle, operation.Debit, operation.Credit, solde));
        }

        return new ResultatMois(mois.Periode, ancienSolde, mois.Revenus.Sum(r => r.Montant), lignes, enveloppes);
    }

    private static EtatEnveloppe EtatDe(LigneEnveloppe enveloppe, IEnumerable<Operation> operations)
    {
        var depense = operations
            .Where(o => MemeNom(o.Enveloppe, enveloppe.Nom))
            .Sum(o => o.Debit - o.Credit);

        return new EtatEnveloppe(enveloppe.Nom, enveloppe.Budget, depense, Math.Max(0m, enveloppe.Budget - depense));
    }

    /// <summary>Noms identiques, sans tenir compte des majuscules ni des espaces autour.</summary>
    public static bool MemeNom(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.CurrentCultureIgnoreCase);
}
