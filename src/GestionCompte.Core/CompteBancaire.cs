using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Core;

/// <param name="Total">Montant cumulé depuis le début, mois demandé inclus.</param>
/// <param name="Reste">Montant restant pour atteindre l'objectif (jamais négatif), ou null sans objectif.</param>
public sealed record EtatCompteCumul(string Nom, decimal Total, decimal? Objectif, decimal? Reste);

/// <summary>
/// Le compte complet : la configuration et tous les mois, dans l'ordre chronologique.
/// Remplace le classeur Excel et ses macros de création de mois.
/// </summary>
public sealed class CompteBancaire
{
    private readonly List<MoisBudget> _mois = new();

    public CompteBancaire(ConfigurationBudget configuration)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public ConfigurationBudget Configuration { get; }

    public IReadOnlyList<MoisBudget> Mois => _mois;

    public MoisBudget? Trouver(PeriodeMois periode) => _mois.Find(m => m.Periode == periode);

    /// <summary>
    /// Crée le mois suivant le dernier mois existant (ou le premier mois de la configuration),
    /// prérempli avec les revenus, enveloppes et charges de la configuration.
    /// </summary>
    public MoisBudget CreerMoisSuivant()
    {
        var periode = _mois.Count == 0 ? Configuration.PremierMois : _mois[^1].Periode.Suivant();
        var mois = new MoisBudget(periode);

        foreach (var revenu in Configuration.Revenus)
            mois.Revenus.Add(new LigneRevenu(revenu.Nom, revenu.MontantParDefaut));

        foreach (var enveloppe in Configuration.Enveloppes)
            mois.Enveloppes.Add(new LigneEnveloppe(enveloppe.Nom, enveloppe.BudgetParDefaut));

        foreach (var charge in Configuration.Charges)
            mois.Operations.Add(new Operation(charge.Nom, charge.Debit, charge.Credit) { CompteCumul = charge.CompteCumul });

        _mois.Add(mois);
        return mois;
    }

    /// <summary>
    /// Ajoute un mois déjà existant (chargé depuis le stockage).
    /// Les mois doivent être ajoutés dans l'ordre et sans trou.
    /// </summary>
    public void AjouterMoisExistant(MoisBudget mois)
    {
        ArgumentNullException.ThrowIfNull(mois);

        var attendu = _mois.Count == 0 ? Configuration.PremierMois : _mois[^1].Periode.Suivant();
        if (mois.Periode != attendu)
            throw new InvalidOperationException($"Mois attendu : {attendu}, reçu : {mois.Periode}.");

        _mois.Add(mois);
    }

    /// <summary>Calcule tous les mois jusqu'à celui demandé, en reportant le solde de fin de chaque mois.</summary>
    public ResultatMois Calculer(PeriodeMois periode)
    {
        var ancienSolde = Configuration.SoldeInitial;

        foreach (var mois in _mois)
        {
            var resultat = CalculateurMois.Calculer(mois, ancienSolde);
            if (mois.Periode == periode)
                return resultat;

            ancienSolde = resultat.SoldeFinPrevisionnel;
        }

        throw new KeyNotFoundException($"Le mois {periode} n'existe pas.");
    }

    /// <summary>État des comptes cumulés (épargne, remboursements…) à la fin du mois demandé.</summary>
    public IReadOnlyList<EtatCompteCumul> ComptesCumulJusqua(PeriodeMois periode)
    {
        if (Trouver(periode) is null)
            throw new KeyNotFoundException($"Le mois {periode} n'existe pas.");

        var operations = _mois
            .Where(m => m.Periode <= periode)
            .SelectMany(m => m.Operations)
            .ToList();

        return Configuration.ComptesCumul
            .Select(compte =>
            {
                var total = compte.MontantInitial + operations
                    .Where(o => CalculateurMois.MemeNom(o.CompteCumul, compte.Nom))
                    .Sum(o => o.Debit - o.Credit);

                decimal? reste = compte.Objectif is { } objectif ? Math.Max(0m, objectif - total) : null;
                return new EtatCompteCumul(compte.Nom, total, compte.Objectif, reste);
            })
            .ToList();
    }
}
