using GestionCompte.Core.Modeles;

namespace GestionCompte.Core.Calculs;

/// <summary>Une échéance du tableau d'amortissement d'un prêt.</summary>
/// <param name="Palier">Numéro du palier (0 pour le premier).</param>
/// <param name="CapitalAvant">Capital restant dû avant l'échéance (base des intérêts).</param>
/// <param name="CapitalRestant">Capital restant dû après l'échéance (et après un éventuel remboursement anticipé).</param>
/// <param name="Anticipe">Capital remboursé par anticipation juste après cette échéance.</param>
public sealed record EcheancePret(
    int Numero, PeriodeMois Periode, int Palier, decimal CapitalAvant, decimal Capital, decimal Interets, decimal Assurance,
    decimal CapitalRestant, decimal Anticipe = 0m)
{
    /// <summary>Échéance hors assurance (capital + intérêts).</summary>
    public decimal HorsAssurance => Capital + Interets;

    /// <summary>Montant prélevé (capital + intérêts + assurance), sans le remboursement anticipé.</summary>
    public decimal Mensualite => Capital + Interets + Assurance;
}

/// <summary>Indemnité de remboursement anticipé et son calcul, en clair.</summary>
public sealed record Indemnite(decimal Montant, string Explication);

/// <summary>Effet d'un remboursement anticipé selon une option (réduire la durée ou la mensualité).</summary>
/// <param name="NouvelleMensualite">Première échéance après le remboursement (assurance comprise), 0 si le prêt est soldé.</param>
public sealed record OptionAnticipe(
    IReadOnlyList<EcheancePret> Tableau, PeriodeMois? Fin, int MoisGagnes, decimal NouvelleMensualite, decimal AncienneMensualite,
    decimal InteretsEconomises, decimal AssuranceEconomisee, decimal GainNet);

/// <summary>Résultat d'une simulation de remboursement anticipé.</summary>
/// <param name="Mensualite">Option « réduire la mensualité », null si elle n'est pas possible (voir <paramref name="SansOptionMensualite"/>).</param>
public sealed record ResultatAnticipe(
    string? Erreur, decimal CapitalRestantAvant, decimal CapitalRembourse, bool Solde, Indemnite? Indemnite,
    OptionAnticipe? Duree, OptionAnticipe? Mensualite, string SansOptionMensualite)
{
    public static ResultatAnticipe Impossible(string erreur) => new(erreur, 0m, 0m, false, null, null, null, "");
}

/// <summary>
/// Tableau d'amortissement d'un prêt à paliers (taux fixe, différé possible) et simulation
/// de remboursement anticipé avec les règles de la banque. Calcul au centime, comme les banques :
/// intérêts = capital restant dû × taux ÷ 12, arrondis au centime ; la dernière échéance solde le prêt.
/// </summary>
public static class CalculPret
{
    /// <summary>Durée maximale acceptée (50 ans).</summary>
    public const int DureeMaximale = 600;

    /// <summary>Tableau d'amortissement prévu, éventuellement avec un remboursement anticipé juste après l'échéance de <paramref name="date"/>.</summary>
    /// <param name="reduireMensualite">Après le remboursement : vrai pour baisser les échéances (même fin), faux pour finir plus tôt.</param>
    public static IReadOnlyList<EcheancePret> Tableau(PretImmobilier pret, PeriodeMois? date = null, decimal montantAnticipe = 0m,
        bool reduireMensualite = false)
    {
        ArgumentNullException.ThrowIfNull(pret);
        var echeances = Echeancier(pret);
        if (pret.Montant <= 0 || echeances.Count == 0 || pret.TauxAnnuel < 0)
            return Array.Empty<EcheancePret>();

        var taux = pret.TauxAnnuel / 100m / 12m;
        var tableau = new List<EcheancePret>(echeances.Count);
        var restant = pret.Montant;
        var periode = pret.PremiereEcheance;
        for (var i = 0; i < echeances.Count && restant > 0; i++, periode = periode.Suivant())
        {
            var (palier, mensualite) = echeances[i];
            var avant = restant;
            var interets = Arrondi(avant * taux);
            // Échéance inférieure aux intérêts (différé) : les intérêts non payés s'ajoutent au capital.
            var capital = i == echeances.Count - 1 ? avant : Math.Min(avant, mensualite - interets);
            restant = avant - capital;
            var assurance = Assurance(pret, restant);

            var anticipe = 0m;
            if (date == periode && montantAnticipe > 0)
            {
                anticipe = Math.Min(montantAnticipe, restant);
                restant -= anticipe;
                if (reduireMensualite && restant > 0)
                    Reechelonner(echeances, i + 1, restant, taux);
            }
            tableau.Add(new EcheancePret(i + 1, periode, palier, avant, capital, interets, assurance, restant, anticipe));
        }
        return tableau;
    }

    /// <summary>Échéance du mois <paramref name="periode"/>, ou null si le prêt n'a pas d'échéance ce mois-là.</summary>
    public static EcheancePret? EcheanceDu(IReadOnlyList<EcheancePret> tableau, PeriodeMois periode) =>
        tableau.FirstOrDefault(e => e.Periode == periode);

    /// <summary>Capital restant dû à la fin du mois <paramref name="periode"/> (montant emprunté avant la 1re échéance, 0 après la dernière).</summary>
    public static decimal CapitalRestantAu(PretImmobilier pret, IReadOnlyList<EcheancePret> tableau, PeriodeMois periode)
    {
        if (tableau.Count == 0 || periode < tableau[0].Periode)
            return Math.Max(0m, pret.Montant);
        return tableau.LastOrDefault(e => e.Periode <= periode)?.CapitalRestant ?? 0m;
    }

    /// <summary>
    /// Mensualité hors assurance du dernier palier qui solde exactement le prêt à la date prévue,
    /// d'après les paliers précédents ; null s'il n'y a pas de palier.
    /// </summary>
    public static decimal? MensualiteDernierPalier(PretImmobilier pret)
    {
        ArgumentNullException.ThrowIfNull(pret);
        if (pret.Paliers.Count == 0 || pret.Paliers[^1].NombreMois <= 0 || pret.Montant <= 0)
            return null;

        var taux = pret.TauxAnnuel / 100m / 12m;
        var restant = pret.Montant;
        foreach (var palier in pret.Paliers.Take(pret.Paliers.Count - 1))
            for (var i = 0; i < palier.NombreMois && restant > 0; i++)
                restant -= Math.Min(restant, palier.Mensualite - Arrondi(restant * taux));
        return restant <= 0 ? 0m : Arrondi(restant * Facteur(taux, pret.Paliers[^1].NombreMois));
    }

    /// <summary>Indemnité due pour <paramref name="capitalRembourse"/> remboursé en <paramref name="date"/>, d'après les conditions du prêt.</summary>
    /// <param name="rachat">Remboursement par un prêt d'une autre banque (l'exonération après N ans ne s'applique pas).</param>
    public static Indemnite CalculerIndemnite(PretImmobilier pret, PeriodeMois date, decimal capitalRembourse, bool rachat)
    {
        ArgumentNullException.ThrowIfNull(pret);
        if (pret.SansIndemnite)
            return new Indemnite(0m, "Ce prêt ne prévoit aucune indemnité de remboursement anticipé (par exemple un prêt à taux zéro).");
        if (capitalRembourse <= 0)
            return new Indemnite(0m, "Aucun capital remboursé.");
        if (!rachat && pret.ExonerationAnnees > 0 && pret.Signature is { } signature && signature.MoisJusqua(date) >= pret.ExonerationAnnees * 12)
            return new Indemnite(0m,
                $"Aucune indemnité : le {pret.ExonerationAnnees}e anniversaire de la signature ({signature.Libelle}) est passé. " +
                "Elle resterait due en cas de rachat du prêt par une autre banque.");

        var interets = Arrondi(capitalRembourse * pret.TauxAnnuel / 100m * pret.IndemniteMoisInterets / 12m);
        var plafond = Arrondi(capitalRembourse * pret.IndemnitePlafond / 100m);
        var montant = Math.Min(interets, plafond);
        return new Indemnite(montant,
            $"{Nombre(pret.IndemniteMoisInterets)} mois d'intérêts : {Montants.Formater(capitalRembourse)} € × {Nombre(pret.TauxAnnuel)} % × " +
            $"{Nombre(pret.IndemniteMoisInterets)} ÷ 12 = {Montants.Formater(interets)} €. " +
            $"Plafond de {Nombre(pret.IndemnitePlafond)} % du capital remboursé : {Montants.Formater(plafond)} €. " +
            $"La banque prend le plus petit des deux : {Montants.Formater(montant)} €.");
    }

    /// <summary>
    /// Simule un remboursement anticipé de <paramref name="montant"/> juste après l'échéance de <paramref name="date"/>
    /// et compare les deux options (réduire la durée, réduire la mensualité) au tableau prévu.
    /// </summary>
    public static ResultatAnticipe SimulerAnticipe(PretImmobilier pret, PeriodeMois date, decimal montant, bool rachat = false)
    {
        ArgumentNullException.ThrowIfNull(pret);
        var prevu = Tableau(pret);
        if (prevu.Count == 0)
            return ResultatAnticipe.Impossible("Complétez le prêt (montant et paliers) pour simuler un remboursement anticipé.");
        if (EcheanceDu(prevu, date) is not { } echeance || echeance.CapitalRestant <= 0)
            return ResultatAnticipe.Impossible(
                $"Choisissez un mois entre {prevu[0].Periode.Libelle} et {prevu[^1].Periode.Precedent().Libelle} : le remboursement se fait juste après l'échéance de ce mois.");
        if (montant <= 0)
            return ResultatAnticipe.Impossible("Saisissez le montant à rembourser.");

        var restant = echeance.CapitalRestant;
        var solde = montant >= restant;
        var rembourse = Math.Min(montant, restant);
        var minimum = Arrondi(pret.Montant * pret.MinimumPourcent / 100m);
        if (!solde && rembourse < minimum)
            return ResultatAnticipe.Impossible(
                $"La banque demande au moins {Montants.Formater(minimum)} € ({Nombre(pret.MinimumPourcent)} % du montant emprunté), sauf pour solder le prêt " +
                $"({Montants.Formater(restant)} € restant dus après l'échéance de {date.Libelle}).");

        var indemnite = CalculerIndemnite(pret, date, rembourse, rachat);
        var duree = Option(prevu, Tableau(pret, date, rembourse), date, indemnite.Montant);
        if (solde)
            return new ResultatAnticipe(null, restant, rembourse, true, indemnite, duree, null, "Remboursement total : le prêt est soldé.");

        var suivante = prevu.First(e => e.Periode > date);
        var dernierPalier = prevu[^1].Palier;
        if (pret.DureeSeuleAvantDernierPalier && suivante.Palier < dernierPalier)
        {
            var debut = prevu.First(e => e.Palier == dernierPalier).Periode;
            return new ResultatAnticipe(null, restant, rembourse, false, indemnite, duree, null,
                $"Avant le dernier palier (à partir de {debut.Libelle}), la banque ne permet que la réduction de la durée.");
        }

        var mensualite = Option(prevu, Tableau(pret, date, rembourse, reduireMensualite: true), date, indemnite.Montant);
        return new ResultatAnticipe(null, restant, rembourse, false, indemnite, duree, mensualite, "");
    }

    private static OptionAnticipe Option(IReadOnlyList<EcheancePret> prevu, IReadOnlyList<EcheancePret> apres, PeriodeMois date, decimal indemnite)
    {
        var interets = prevu.Where(e => e.Periode > date).Sum(e => e.Interets) - apres.Where(e => e.Periode > date).Sum(e => e.Interets);
        var assurance = prevu.Where(e => e.Periode > date).Sum(e => e.Assurance) - apres.Where(e => e.Periode > date).Sum(e => e.Assurance);
        var fin = apres.Count > 0 && apres[^1].Periode > date ? apres[^1].Periode : (PeriodeMois?)null;
        var gagnes = prevu.Count - apres.Count;
        var nouvelle = apres.FirstOrDefault(e => e.Periode > date)?.Mensualite ?? 0m;
        var ancienne = prevu.FirstOrDefault(e => e.Periode > date)?.Mensualite ?? 0m;
        return new OptionAnticipe(apres, fin, gagnes, nouvelle, ancienne, interets, assurance, interets + assurance - indemnite);
    }

    /// <summary>Palier et échéance hors assurance de chaque mois prévu.</summary>
    private static List<(int Palier, decimal Mensualite)> Echeancier(PretImmobilier pret)
    {
        var echeances = new List<(int, decimal)>();
        foreach (var (palier, index) in pret.Paliers.Select((p, i) => (p, i)))
            for (var i = 0; i < palier.NombreMois && echeances.Count < DureeMaximale; i++)
                echeances.Add((index, Math.Max(0m, palier.Mensualite)));
        return echeances;
    }

    /// <summary>
    /// Baisse les échéances restantes dans la même proportion pour solder <paramref name="restant"/> à la même date :
    /// facteur = restant × (1 + t)^n ÷ Σ échéance(k) × (1 + t)^(n − k).
    /// </summary>
    private static void Reechelonner(List<(int Palier, decimal Mensualite)> echeances, int depuis, decimal restant, decimal taux)
    {
        var n = echeances.Count - depuis;
        if (n <= 0)
            return;
        var somme = 0m;
        var puissance = 1m;
        for (var k = echeances.Count - 1; k >= depuis; k--)
        {
            somme += echeances[k].Mensualite * puissance;
            puissance *= 1 + taux;
        }
        if (somme <= 0)
            return;
        var facteur = restant * puissance / somme;
        for (var k = depuis; k < echeances.Count; k++)
            echeances[k] = (echeances[k].Palier, Arrondi(echeances[k].Mensualite * facteur));
    }

    private static decimal Assurance(PretImmobilier pret, decimal restant) => pret.TypeAssurance switch
    {
        AssurancePret.CapitalRestant => Arrondi(restant * pret.Assurance / 100m / 12m),
        AssurancePret.CapitalInitial => Arrondi(pret.Montant * pret.Assurance / 100m / 12m),
        _ => Arrondi(pret.Assurance),
    };

    /// <summary>Échéance pour 1 € emprunté : t / (1 − (1 + t)^−n), ou 1 / n sans intérêts.</summary>
    private static decimal Facteur(decimal taux, int duree)
    {
        if (taux == 0)
            return 1m / duree;
        var puissance = 1m;
        for (var i = 0; i < duree; i++)
            puissance *= 1 + taux;
        return taux * puissance / (puissance - 1);
    }

    /// <summary>Arrondi au centime, les demis vers le haut (comme les banques).</summary>
    private static decimal Arrondi(decimal montant) => decimal.Round(montant, 2, MidpointRounding.AwayFromZero);

    private static string Nombre(decimal valeur) => valeur.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
}
