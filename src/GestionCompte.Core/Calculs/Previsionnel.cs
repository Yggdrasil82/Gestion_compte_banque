using GestionCompte.Core.Modeles;

namespace GestionCompte.Core.Calculs;

/// <summary>Un mois du prévisionnel : réel (mois déjà créé) ou simulé.</summary>
/// <param name="Entrees">Revenus + crédits.</param>
/// <param name="Sorties">Réservé pour les enveloppes + débits.</param>
/// <param name="Cumuls">Total de chaque compte cumulé à la fin du mois.</param>
public sealed record MoisPrevision(
    PeriodeMois Periode,
    bool Reel,
    decimal AncienSolde,
    decimal Entrees,
    decimal Sorties,
    decimal SoldeFin,
    IReadOnlyDictionary<string, decimal> Cumuls)
{
    public bool Negatif => SoldeFin < 0;
}

/// <summary>Date à laquelle un compte cumulé atteint son objectif (ex. remboursement soldé).</summary>
/// <param name="AtteintEn">Premier mois où l'objectif est atteint ; null s'il ne l'est pas dans les 10 ans simulés.</param>
/// <param name="DejaAtteint">Objectif déjà atteint dans les mois réels.</param>
public sealed record EcheanceCompteCumul(string Nom, decimal Objectif, PeriodeMois? AtteintEn, bool DejaAtteint);

public sealed record ResultatPrevision(IReadOnlyList<MoisPrevision> Mois, IReadOnlyList<EcheanceCompteCumul> Echeances)
{
    public IEnumerable<MoisPrevision> MoisPrevus => Mois.Where(m => !m.Reel);

    /// <summary>Premier mois simulé qui finit en négatif, ou null.</summary>
    public MoisPrevision? PremierMoisNegatif => MoisPrevus.FirstOrDefault(m => m.Negatif);

    /// <summary>Mois simulé au solde de fin le plus bas, ou null s'il n'y a pas de mois simulé.</summary>
    public MoisPrevision? PointBas => MoisPrevus.MinBy(m => m.SoldeFin);
}

/// <summary>
/// Simule les mois à venir avec la configuration (revenus habituels, charges, budgets complets des enveloppes)
/// et les opérations ponctuelles prévues, à la suite des mois déjà créés.
/// </summary>
public static class Previsionnel
{
    /// <summary>Durée de simulation maximale pour trouver la date d'atteinte des objectifs.</summary>
    public const int MoisMaximumPourEcheances = 120;

    /// <param name="moisReels">Nombre de derniers mois réels à inclure (pour comparer avec le passé).</param>
    /// <param name="moisPrevus">Nombre de mois à simuler après le dernier mois créé.</param>
    public static ResultatPrevision Calculer(CompteBancaire compte, int moisReels, int moisPrevus)
    {
        ArgumentNullException.ThrowIfNull(compte);
        ArgumentOutOfRangeException.ThrowIfNegative(moisReels);
        ArgumentOutOfRangeException.ThrowIfNegative(moisPrevus);

        var configuration = compte.Configuration;
        // Deux comptes du même nom (ex. deux « Nouveau compte ») sont comptés une seule fois.
        var comptes = configuration.ComptesCumul.DistinctBy(c => c.Nom.Trim().ToUpperInvariant()).ToList();
        var cumuls = comptes.ToDictionary(c => c.Nom, c => c.MontantInitial);
        var echeances = comptes
            .Where(c => c.Objectif is not null)
            .ToDictionary(c => c.Nom, c => (Objectif: c.Objectif!.Value, AtteintEn: (PeriodeMois?)null, Deja: c.MontantInitial >= c.Objectif));

        var resultat = new List<MoisPrevision>();
        var solde = configuration.SoldeInitial;

        void Ajouter(MoisBudget mois, bool reel, bool garder)
        {
            var calcul = CalculateurMois.Calculer(mois, solde);
            AjouterAuxCumuls(cumuls, mois.Operations);

            foreach (var nom in echeances.Keys.ToList())
            {
                var e = echeances[nom];
                if (e.Deja || e.AtteintEn is not null || cumuls.GetValueOrDefault(nom) < e.Objectif)
                    continue;
                echeances[nom] = reel ? e with { Deja = true } : e with { AtteintEn = mois.Periode };
            }

            if (garder)
            {
                resultat.Add(new MoisPrevision(
                    mois.Periode,
                    reel,
                    solde,
                    calcul.TotalRevenus + mois.Operations.Sum(o => o.Credit),
                    calcul.Enveloppes.Sum(e => e.Reste) + mois.Operations.Sum(o => o.Debit),
                    calcul.SoldeFinPrevisionnel,
                    new Dictionary<string, decimal>(cumuls)));
            }

            solde = calcul.SoldeFinPrevisionnel;
        }

        for (var i = 0; i < compte.Mois.Count; i++)
            Ajouter(compte.Mois[i], reel: true, garder: i >= compte.Mois.Count - moisReels);

        var periode = compte.ProchainMois;
        var aSimuler = Math.Max(moisPrevus, echeances.Values.Any(e => !e.Deja) ? MoisMaximumPourEcheances : 0);
        for (var i = 0; i < aSimuler; i++, periode = periode.Suivant())
            Ajouter(compte.Generer(periode), reel: false, garder: i < moisPrevus);

        return new ResultatPrevision(
            resultat,
            echeances.Select(e => new EcheanceCompteCumul(e.Key, e.Value.Objectif, e.Value.AtteintEn, e.Value.Deja)).ToList());
    }

    /// <summary>Ajoute aux comptes cumulés les débits (moins les crédits) des opérations qui leur sont liées.</summary>
    internal static void AjouterAuxCumuls(Dictionary<string, decimal> cumuls, IEnumerable<Operation> operations)
    {
        foreach (var operation in operations)
        {
            var nom = cumuls.Keys.FirstOrDefault(n => CalculateurMois.MemeNom(n, operation.CompteCumul));
            if (nom is not null)
                cumuls[nom] += operation.Debit - operation.Credit;
        }
    }
}
