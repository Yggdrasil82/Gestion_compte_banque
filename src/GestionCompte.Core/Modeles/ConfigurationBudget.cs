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

    /// <summary>Règles de classement des opérations importées : mot-clé du libellé → enveloppe.</summary>
    public List<RegleClassement> Regles { get; } = new();

    /// <summary>Catégories d'opérations choisies à la main (Agen, Maison…), avec leur couleur, pour ranger les opérations du mois.</summary>
    public List<CategorieOperation> CategoriesOperations { get; } = new();

    /// <summary>
    /// Copie pour un nouveau compte : mêmes revenus, enveloppes, charges, comptes cumulés et règles,
    /// mais nouveau premier mois, solde de départ et montants déjà cumulés à zéro.
    /// </summary>
    public ConfigurationBudget CopierPourNouveauCompte(PeriodeMois premierMois)
    {
        var copie = new ConfigurationBudget { PremierMois = premierMois, SoldeInitial = 0m };
        copie.Revenus.AddRange(Revenus);
        copie.Enveloppes.AddRange(Enveloppes);
        copie.Charges.AddRange(Charges);
        copie.ComptesCumul.AddRange(ComptesCumul.Select(c => c with { MontantInitial = 0m }));
        copie.Regles.AddRange(Regles);
        copie.CategoriesOperations.AddRange(CategoriesOperations);
        return copie;
    }
}

/// <param name="MotCle">Texte cherché dans le libellé bancaire (sans tenir compte des majuscules ni des accents).</param>
public sealed record RegleClassement(string MotCle, string Enveloppe)
{
    /// <summary>Règles proposées au départ : grandes surfaces et stations-service.</summary>
    public static IReadOnlyList<RegleClassement> ParDefaut { get; } =
        new[] { "LECLERC", "CARREFOUR", "LIDL", "AUCHAN", "INTERMARCHE", "SUPER U", "ALDI" }
            .Select(m => new RegleClassement(m, "Courses"))
            .Concat(new[] { "TOTAL", "ESSO", "BP", "SHELL", "AVIA" }.Select(m => new RegleClassement(m, "Carburant")))
            .ToList();
}

/// <summary>Catégorie d'opérations (ex. « Agen ») ; à ne pas confondre avec la catégorie 50/30/20.</summary>
/// <param name="Couleur">Couleur au format « #RRGGBB ».</param>
public sealed record CategorieOperation(string Nom, string Couleur)
{
    /// <summary>Couleurs proposées (nom affiché, code).</summary>
    public static IReadOnlyList<(string Nom, string Code)> Couleurs { get; } = new[]
    {
        ("Vert", "#2E9E5B"), ("Bleu", "#2F7FD8"), ("Orange", "#E8892B"), ("Violet", "#8A5CD1"), ("Rouge", "#D64545"),
        ("Rose", "#D9539B"), ("Turquoise", "#1AA5A5"), ("Jaune", "#D8B21F"), ("Marron", "#9A6B44"), ("Gris", "#7D8A93"),
    };
}

public sealed record ModeleRevenu(string Nom, decimal MontantParDefaut);

/// <param name="Categorie">Catégorie pour la répartition 50/30/20 (courses, carburant : essentiel).</param>
public sealed record ModeleEnveloppe(string Nom, decimal BudgetParDefaut, Categorie Categorie = Categorie.Essentiel);

/// <param name="CompteCumul">Nom du compte cumulé alimenté par cette charge (ex. « Épargne »), ou null.</param>
/// <param name="Categorie">Catégorie pour la répartition 50/30/20.</param>
/// <param name="Frequence">Nombre de mois entre deux prélèvements : 1 = tous les mois, 2 = un mois sur deux…</param>
/// <param name="Depart">Un mois où la charge est prélevée (sert de repère quand <paramref name="Frequence"/> &gt; 1).</param>
/// <param name="CategorieOperation">Catégorie d'opérations donnée à l'opération du mois (ex. « Agen »), ou null.</param>
public sealed record ModeleCharge(
    string Nom, decimal Debit, decimal Credit = 0m, string? CompteCumul = null, Categorie Categorie = Categorie.NonClassee,
    int Frequence = 1, PeriodeMois? Depart = null, string? CategorieOperation = null)
{
    /// <summary>Vrai si la charge est prélevée ce mois-ci.</summary>
    public bool TombeEn(PeriodeMois periode)
    {
        if (Frequence <= 1 || Depart is not { } depart)
            return true;
        return depart.MoisJusqua(periode) % Frequence == 0;
    }

    /// <summary>Coût net ramené à un mois (ex. 61,18 € tous les 2 mois = 30,59 € par mois).</summary>
    public decimal NetMensuel => Frequence <= 1 ? Debit - Credit : decimal.Round((Debit - Credit) / Frequence, 2);
}

/// <summary>Catégories de la règle 50/30/20 : 50 % essentiel, 30 % confort, 20 % épargne.</summary>
public enum Categorie
{
    NonClassee,
    Essentiel,
    Confort,
    Epargne,
}

/// <summary>Objectif d'épargne : un montant à réunir pour une date (vacances, voiture, fonds d'urgence…).</summary>
public sealed class ObjectifEpargne
{
    public ObjectifEpargne(string nom, decimal montant, PeriodeMois echeance, decimal dejaEpargne = 0m)
    {
        Nom = nom;
        Montant = montant;
        Echeance = echeance;
        DejaEpargne = dejaEpargne;
    }

    public string Nom { get; set; }
    public decimal Montant { get; set; }

    /// <summary>Mois pour lequel le montant doit être réuni (inclus).</summary>
    public PeriodeMois Echeance { get; set; }

    /// <summary>Saisi à la main ; ignoré quand l'objectif est alimenté par un compte cumulé.</summary>
    public decimal DejaEpargne { get; set; }

    /// <summary>Compte cumulé (ex. « Économie ») qui alimente l'objectif, ou null pour une saisie manuelle.</summary>
    public string? CompteCumul { get; set; }
}

/// <param name="MontantInitial">Montant déjà cumulé avant le premier mois.</param>
/// <param name="Objectif">Montant total à atteindre (ex. somme à rembourser), ou null s'il n'y en a pas.</param>
public sealed record CompteCumul(string Nom, decimal MontantInitial = 0m, decimal? Objectif = null);

public enum TypeAssurance
{
    /// <summary>Taux annuel appliqué au montant emprunté.</summary>
    Pourcentage,

    /// <summary>Montant fixe par mois.</summary>
    ParMois,
}

public enum TypeCredit
{
    Immobilier,
    AutoMoto,
    Consommation,
}

/// <summary>Simulation de crédit enregistrée, pour comparer plusieurs offres (montant, taux, durée, assurance).</summary>
public sealed class SimulationCredit
{
    public SimulationCredit(string nom, decimal montant, decimal tauxAnnuel, int dureeMois, PeriodeMois premiereEcheance,
        decimal assurance = 0m, TypeAssurance typeAssurance = TypeAssurance.Pourcentage)
    {
        Nom = nom;
        Montant = montant;
        TauxAnnuel = tauxAnnuel;
        DureeMois = dureeMois;
        PremiereEcheance = premiereEcheance;
        Assurance = assurance;
        TypeAssurance = typeAssurance;
    }

    public string Nom { get; set; }
    public decimal Montant { get; set; }

    /// <summary>Taux nominal annuel en % (ex. 3,5).</summary>
    public decimal TauxAnnuel { get; set; }

    public int DureeMois { get; set; }
    public PeriodeMois PremiereEcheance { get; set; }

    /// <summary>Taux annuel en % du montant emprunté, ou montant par mois, selon <see cref="TypeAssurance"/>.</summary>
    public decimal Assurance { get; set; }

    public TypeAssurance TypeAssurance { get; set; }

    public TypeCredit Type { get; set; } = TypeCredit.Immobilier;
}
