using System.Globalization;
using System.Text;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Core.Calculs;

/// <summary>Une échéance du tableau d'amortissement.</summary>
/// <param name="CapitalRestant">Capital restant dû après cette échéance.</param>
public sealed record LigneAmortissement(int Numero, PeriodeMois Periode, decimal Capital, decimal Interets, decimal Assurance, decimal CapitalRestant)
{
    public decimal Mensualite => Capital + Interets + Assurance;
}

/// <param name="MensualiteHorsAssurance">Échéance constante (capital + intérêts), sauf la dernière qui solde l'arrondi.</param>
public sealed record ResultatCredit(
    decimal MensualiteHorsAssurance,
    decimal AssuranceMensuelle,
    decimal CoutInterets,
    decimal CoutAssurance,
    IReadOnlyList<LigneAmortissement> Tableau)
{
    public decimal Mensualite => MensualiteHorsAssurance + AssuranceMensuelle;
    public decimal CoutTotal => CoutInterets + CoutAssurance;
}

/// <summary>Part des revenus consacrée aux crédits (règle bancaire : 35 % au plus).</summary>
/// <param name="CreditsExistants">Mensualités des charges de la configuration qui sont des crédits (ex. « Crédit Voiture »).</param>
/// <param name="Taux">Crédits existants + nouveau crédit, divisés par les revenus (0,35 = 35 %) ; null sans revenus.</param>
public sealed record Endettement(decimal Revenus, decimal CreditsExistants, IReadOnlyList<string> NomsCredits, decimal NouveauCredit, decimal? Taux)
{
    public bool Excessif => Taux is null ? NouveauCredit > 0 : Taux > Credit.SeuilEndettement;
}

/// <summary>Simulation de crédit : mensualité, coût, tableau d'amortissement, montant empruntable et endettement.</summary>
public static class Credit
{
    /// <summary>Taux d'endettement maximal habituel des banques.</summary>
    public const decimal SeuilEndettement = 0.35m;

    /// <summary>Durée maximale acceptée (40 ans).</summary>
    public const int DureeMaximale = 480;

    public static ResultatCredit Calculer(SimulationCredit simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var montant = simulation.Montant;
        var duree = simulation.DureeMois;
        if (montant <= 0 || duree <= 0 || duree > DureeMaximale || simulation.TauxAnnuel < 0)
            return new ResultatCredit(0m, 0m, 0m, 0m, Array.Empty<LigneAmortissement>());

        var taux = TauxMensuel(simulation.TauxAnnuel);
        var mensualite = decimal.Round(montant * Facteur(taux, duree), 2);
        var assurance = AssuranceMensuelle(simulation);

        var tableau = new List<LigneAmortissement>(duree);
        var restant = montant;
        var periode = simulation.PremiereEcheance;
        for (var numero = 1; numero <= duree; numero++, periode = periode.Suivant())
        {
            var interets = decimal.Round(restant * taux, 2);
            var capital = numero == duree ? restant : Math.Min(restant, mensualite - interets);
            restant -= capital;
            tableau.Add(new LigneAmortissement(numero, periode, capital, interets, assurance, restant));
        }

        return new ResultatCredit(mensualite, assurance, tableau.Sum(l => l.Interets), assurance * duree, tableau);
    }

    /// <summary>
    /// Montant qu'on peut emprunter pour une mensualité (assurance comprise) avec le taux, la durée
    /// et l'assurance de la simulation ; arrondi à l'euro inférieur.
    /// </summary>
    public static decimal MontantEmpruntable(decimal mensualiteMaximale, SimulationCredit conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        var duree = conditions.DureeMois;
        if (mensualiteMaximale <= 0 || duree <= 0 || duree > DureeMaximale || conditions.TauxAnnuel < 0)
            return 0m;

        var facteur = Facteur(TauxMensuel(conditions.TauxAnnuel), duree);
        // Assurance en % : proportionnelle au montant emprunté ; en € : retirée de la mensualité.
        var montant = conditions.TypeAssurance == TypeAssurance.Pourcentage
            ? mensualiteMaximale / (facteur + conditions.Assurance / 100m / 12m)
            : (mensualiteMaximale - conditions.Assurance) / facteur;
        return Math.Max(0m, decimal.Floor(montant));
    }

    /// <summary>Endettement avec ce nouveau crédit, d'après les revenus et les charges de la configuration.</summary>
    public static Endettement CalculerEndettement(ConfigurationBudget configuration, decimal nouvelleMensualite)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var revenus = configuration.Revenus.Sum(r => r.MontantParDefaut);
        var credits = configuration.Charges.Where(c => EstUnCredit(c.Nom) && c.NetMensuel > 0).ToList();
        var existants = credits.Sum(c => c.NetMensuel);
        decimal? taux = revenus > 0 ? (existants + nouvelleMensualite) / revenus : null;
        return new Endettement(revenus, existants, credits.Select(c => c.Nom).ToList(), nouvelleMensualite, taux);
    }

    /// <summary>Charge considérée comme un crédit : son nom contient « crédit », « prêt » ou « emprunt ».</summary>
    public static bool EstUnCredit(string nom)
    {
        var mots = SansAccents(nom).ToUpperInvariant()
            .Split(new[] { ' ', '-', '_', '.', '/', '\'' }, StringSplitOptions.RemoveEmptyEntries);
        return mots.Any(m => m is "CREDIT" or "PRET" or "EMPRUNT" or "CREDITS" or "PRETS" or "EMPRUNTS");
    }

    /// <summary>
    /// Excédent moyen par mois (comme <see cref="AideBudget.CapaciteMensuelle"/>) sans les opérations prévues
    /// portant ce libellé : sert à montrer ce qui reste après le crédit, qu'il soit déjà au prévisionnel ou non.
    /// </summary>
    public static decimal CapaciteSansOperations(CompteBancaire compte, string libelle)
    {
        ArgumentNullException.ThrowIfNull(compte);
        var copie = new CompteBancaire(compte.Configuration);
        foreach (var mois in compte.Mois)
            copie.AjouterMoisExistant(mois);
        copie.OperationsPrevues.AddRange(compte.OperationsPrevues.Where(o => !CalculateurMois.MemeNom(o.Libelle, libelle)));
        return AideBudget.CapaciteMensuelle(copie);
    }

    /// <summary>Libellé des échéances ajoutées au prévisionnel (ex. « Crédit Maison »).</summary>
    public static string Libelle(SimulationCredit simulation) =>
        EstUnCredit(simulation.Nom) ? simulation.Nom.Trim() : $"Crédit {simulation.Nom.Trim()}";

    /// <summary>Les échéances de ce crédit sont-elles au prévisionnel ou dans les mois ?</summary>
    public static bool EstAuPrevisionnel(CompteBancaire compte, SimulationCredit simulation)
    {
        var libelle = Libelle(simulation);
        return compte.OperationsPrevues.Any(o => CalculateurMois.MemeNom(o.Libelle, libelle))
            || compte.Mois.Any(m => m.Operations.Any(o => CalculateurMois.MemeNom(o.Libelle, libelle)));
    }

    /// <summary>
    /// Ajoute une échéance par mois (assurance comprise) : dans les mois déjà créés, comme opération non pointée ;
    /// ensuite, comme opération prévue. Les échéances déjà ajoutées pour ce crédit sont d'abord retirées.
    /// </summary>
    /// <returns>Nombre d'échéances ajoutées.</returns>
    public static int AjouterAuPrevisionnel(CompteBancaire compte, SimulationCredit simulation)
    {
        ArgumentNullException.ThrowIfNull(compte);
        RetirerDuPrevisionnel(compte, simulation);
        var libelle = Libelle(simulation);
        var tableau = Calculer(simulation).Tableau;
        foreach (var ligne in tableau)
        {
            if (compte.Trouver(ligne.Periode) is { } mois)
                mois.Operations.Add(new Operation(libelle, debit: ligne.Mensualite));
            else
                compte.OperationsPrevues.Add(new OperationPrevue(ligne.Periode, libelle, debit: ligne.Mensualite));
        }
        return tableau.Count;
    }

    /// <summary>Retire les échéances de ce crédit : opérations prévues et opérations non pointées des mois.</summary>
    /// <returns>Nombre d'échéances retirées.</returns>
    public static int RetirerDuPrevisionnel(CompteBancaire compte, SimulationCredit simulation)
    {
        ArgumentNullException.ThrowIfNull(compte);
        ArgumentNullException.ThrowIfNull(simulation);
        var libelle = Libelle(simulation);
        var retirees = compte.OperationsPrevues.RemoveAll(o => CalculateurMois.MemeNom(o.Libelle, libelle));
        foreach (var mois in compte.Mois)
            retirees += mois.Operations.RemoveAll(o => !o.Pointee && CalculateurMois.MemeNom(o.Libelle, libelle));
        return retirees;
    }

    private static decimal TauxMensuel(decimal tauxAnnuel) => tauxAnnuel / 100m / 12m;

    /// <summary>Mensualité pour 1 € emprunté : t / (1 − (1 + t)^−n), ou 1 / n sans intérêts.</summary>
    private static decimal Facteur(decimal tauxMensuel, int duree)
    {
        if (tauxMensuel == 0)
            return 1m / duree;
        var puissance = 1m;
        for (var i = 0; i < duree; i++)
            puissance *= 1 + tauxMensuel;
        return tauxMensuel * puissance / (puissance - 1);
    }

    private static decimal AssuranceMensuelle(SimulationCredit simulation) => simulation.TypeAssurance == TypeAssurance.Pourcentage
        ? decimal.Round(simulation.Montant * simulation.Assurance / 100m / 12m, 2)
        : decimal.Round(simulation.Assurance, 2);

    private static string SansAccents(string texte)
    {
        var decompose = texte.Normalize(NormalizationForm.FormD);
        var resultat = new StringBuilder(decompose.Length);
        foreach (var c in decompose)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                resultat.Append(c);
        return resultat.ToString();
    }
}
