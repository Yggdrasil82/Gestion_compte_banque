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

    /// <summary>Numéro du compte bancaire (ACCTID) des relevés importés dans ce compte, ou null.</summary>
    public string? IdentifiantBanque { get; set; }

    public IReadOnlyList<MoisBudget> Mois => _mois;

    public MoisBudget? Trouver(PeriodeMois periode) => _mois.Find(m => m.Periode == periode);

    /// <summary>Opérations ponctuelles prévues pour des mois pas encore créés.</summary>
    public List<OperationPrevue> OperationsPrevues { get; } = new();

    /// <summary>Objectifs d'épargne (montant à réunir pour une date).</summary>
    public List<ObjectifEpargne> ObjectifsEpargne { get; } = new();

    /// <summary>Simulations de crédit de l'aide au budget.</summary>
    public List<SimulationCredit> SimulationsCredit { get; } = new();

    /// <summary>Prêts en cours du module « Prêts » (tableau d'amortissement, remboursement anticipé).</summary>
    public List<PretImmobilier> Prets { get; } = new();

    /// <summary>Mois qui sera créé ensuite : le suivant du dernier mois, ou le premier mois de la configuration.</summary>
    public PeriodeMois ProchainMois => _mois.Count == 0 ? Configuration.PremierMois : _mois[^1].Periode.Suivant();

    /// <summary>
    /// Crée le mois suivant le dernier mois existant (ou le premier mois de la configuration),
    /// prérempli avec les revenus, enveloppes et charges de la configuration,
    /// et avec les opérations prévues pour ce mois (qui quittent alors la liste des opérations prévues).
    /// </summary>
    public MoisBudget CreerMoisSuivant()
    {
        var mois = Generer(ProchainMois);
        OperationsPrevues.RemoveAll(o => o.Periode == mois.Periode);
        _mois.Add(mois);
        return mois;
    }

    /// <summary>Mois tel qu'il serait créé (configuration + opérations prévues), sans l'ajouter au compte.</summary>
    internal MoisBudget Generer(PeriodeMois periode)
    {
        var mois = new MoisBudget(periode);

        foreach (var revenu in Configuration.Revenus)
            mois.Revenus.Add(new LigneRevenu(revenu.Nom, revenu.MontantParDefaut));

        foreach (var enveloppe in Configuration.Enveloppes)
            mois.Enveloppes.Add(new LigneEnveloppe(enveloppe.Nom, enveloppe.BudgetParDefaut));

        foreach (var charge in Configuration.Charges.Where(c => c.TombeEn(periode)))
            mois.Operations.Add(new Operation(charge.Nom, charge.Debit, charge.Credit) { CompteCumul = charge.CompteCumul, CategorieOperation = charge.CategorieOperation });

        foreach (var prevue in OperationsPrevues.Where(o => o.Periode == periode))
            mois.Operations.Add(prevue.VersOperation());

        return mois;
    }

    /// <summary>Supprime le dernier mois (pour annuler une création par erreur). Renvoie false s'il n'y a aucun mois.</summary>
    public bool SupprimerDernierMois()
    {
        if (_mois.Count == 0)
            return false;

        _mois.RemoveAt(_mois.Count - 1);
        return true;
    }

    /// <summary>
    /// Applique la configuration actuelle à un mois existant :
    /// les montants des charges et les budgets des enveloppes sont remplacés par ceux de la configuration,
    /// les charges, enveloppes et revenus absents du mois sont ajoutés (une charge qui n'est pas prélevée
    /// tous les mois n'est ajoutée que les mois où elle tombe).
    /// Les revenus déjà présents et les autres opérations ne sont pas modifiés ; rien n'est supprimé.
    /// </summary>
    public void AppliquerConfiguration(MoisBudget mois)
    {
        ArgumentNullException.ThrowIfNull(mois);
        if (!_mois.Contains(mois))
            throw new ArgumentException("Ce mois n'appartient pas au compte.", nameof(mois));

        foreach (var revenu in Configuration.Revenus)
        {
            if (!mois.Revenus.Any(r => CalculateurMois.MemeNom(r.Nom, revenu.Nom)))
                mois.Revenus.Add(new LigneRevenu(revenu.Nom, revenu.MontantParDefaut));
        }

        foreach (var enveloppe in Configuration.Enveloppes)
        {
            var existante = mois.Enveloppes.Find(e => CalculateurMois.MemeNom(e.Nom, enveloppe.Nom));
            if (existante is null)
                mois.Enveloppes.Add(new LigneEnveloppe(enveloppe.Nom, enveloppe.BudgetParDefaut));
            else
                existante.Budget = enveloppe.BudgetParDefaut;
        }

        foreach (var charge in Configuration.Charges)
        {
            var existante = mois.Operations.Find(o => o.Enveloppe is null && CalculateurMois.MemeNom(o.Libelle, charge.Nom));
            if (existante is null)
            {
                if (charge.TombeEn(mois.Periode))
                    mois.Operations.Add(new Operation(charge.Nom, charge.Debit, charge.Credit) { CompteCumul = charge.CompteCumul, CategorieOperation = charge.CategorieOperation });
            }
            else
            {
                existante.Debit = charge.Debit;
                existante.Credit = charge.Credit;
                existante.CompteCumul = charge.CompteCumul;
                // Une catégorie choisie à la main dans le mois est gardée si la charge n'en a pas.
                if (charge.CategorieOperation is not null)
                    existante.CategorieOperation = charge.CategorieOperation;
            }
        }
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
