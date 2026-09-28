using System.Globalization;
using System.Text;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Core.Calculs;

// ---- 1. Analyse des charges ----

/// <param name="Mensuel">Débit − crédit de la charge.</param>
/// <param name="PartRevenus">Part des revenus mensuels habituels (0,25 = 25 %).</param>
public sealed record ChargeAnalysee(string Nom, decimal Mensuel, decimal Annuel, decimal PartRevenus, Categorie Categorie);

/// <summary>Charges de la même famille (ex. plusieurs forfaits « Mobile »).</summary>
public sealed record GroupeCharges(string Famille, IReadOnlyList<string> Charges, decimal Mensuel, decimal Annuel);

public sealed record AnalyseCharges(decimal RevenusMensuels, decimal TotalMensuel, IReadOnlyList<ChargeAnalysee> Charges, IReadOnlyList<GroupeCharges> Groupes);

// ---- 2. Répartition 50/30/20 ----

/// <param name="Part">Part des revenus (0,5 = 50 %).</param>
/// <param name="Cible">Part conseillée par la règle 50/30/20.</param>
public sealed record PartCategorie(Categorie Categorie, decimal Montant, decimal Part, decimal Cible);

/// <param name="NonClasse">Charges sans catégorie.</param>
/// <param name="ResteDisponible">Revenus non utilisés par les charges et enveloppes.</param>
public sealed record Repartition(decimal RevenusMensuels, IReadOnlyList<PartCategorie> Parts, decimal NonClasse, decimal ResteDisponible);

// ---- 3. Suivi des enveloppes ----

public enum Tendance
{
    SansDonnees,
    Adapte,
    AReduire,
    AAugmenter,
}

/// <param name="Moyenne">Dépense moyenne sur les mois observés.</param>
/// <param name="BudgetConseille">Nouveau budget suggéré, ou null si le budget actuel convient.</param>
public sealed record SuiviEnveloppe(
    string Nom, decimal Budget, decimal Moyenne, decimal Maximum, int MoisObserves, int Depassements, Tendance Tendance, decimal? BudgetConseille);

// ---- 4. Simulateur « Et si… ? » ----

/// <param name="GainMensuel">Économie par mois par rapport au budget actuel (négatif = dépense en plus).</param>
public sealed record ResultatSimulation(decimal GainMensuel, int Horizon, decimal SoldeAvant, decimal SoldeApres)
{
    public decimal GainAnnuel => GainMensuel * 12;
}

// ---- 5. Objectifs d'épargne ----

public enum Faisabilite
{
    Atteint,
    Tenable,
    Juste,
    Difficile,
    EcheancePassee,
}

/// <param name="Mensualite">Montant à mettre de côté chaque mois jusqu'à l'échéance.</param>
public sealed record AnalyseObjectif(ObjectifEpargne Objectif, int MoisRestants, decimal ResteAEpargner, decimal Mensualite, Faisabilite Faisabilite);

/// <param name="CapaciteMensuelle">Excédent moyen prévu par mois (entrées − sorties) sur les 12 prochains mois.</param>
public sealed record ResultatObjectifs(decimal CapaciteMensuelle, IReadOnlyList<AnalyseObjectif> Objectifs);

// ---- 6. Alertes ----

public enum NiveauAlerte
{
    Info,
    Attention,
    Danger,
}

public sealed record Alerte(NiveauAlerte Niveau, string Titre, string Detail);

/// <summary>
/// Aide à la gestion du budget : tous les calculs sont faits à partir de la configuration et des mois saisis,
/// sur le PC, sans service extérieur.
/// </summary>
public static class AideBudget
{
    public const decimal CibleEssentiel = 0.50m;
    public const decimal CibleConfort = 0.30m;
    public const decimal CibleEpargne = 0.20m;

    /// <summary>En dessous de cette part du budget dépensée en moyenne, l'enveloppe peut être réduite.</summary>
    public const decimal SeuilReduction = 0.85m;

    // ---- 1. Analyse des charges ----

    public static AnalyseCharges AnalyserCharges(ConfigurationBudget configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var revenus = RevenusMensuels(configuration);

        var charges = configuration.Charges
            .Select(c => (Charge: c, Net: c.NetMensuel))
            .Where(c => c.Net > 0)
            .OrderByDescending(c => c.Net)
            .Select(c => new ChargeAnalysee(c.Charge.Nom, c.Net, c.Net * 12, Part(c.Net, revenus), c.Charge.Categorie))
            .ToList();

        var groupes = charges
            .GroupBy(c => CleFamille(c.Nom))
            .Where(g => g.Key.Length > 0 && g.Count() >= 2)
            .Select(g => new GroupeCharges(
                NomFamille(g.First().Nom), g.Select(c => c.Nom).ToList(), g.Sum(c => c.Mensuel), g.Sum(c => c.Annuel)))
            .OrderByDescending(g => g.Mensuel)
            .ToList();

        return new AnalyseCharges(revenus, charges.Sum(c => c.Mensuel), charges, groupes);
    }

    // ---- 2. Répartition 50/30/20 ----

    public static Repartition Repartir(ConfigurationBudget configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var revenus = RevenusMensuels(configuration);

        var montants = new Dictionary<Categorie, decimal>();
        foreach (var charge in configuration.Charges)
            montants[charge.Categorie] = montants.GetValueOrDefault(charge.Categorie) + charge.NetMensuel;
        foreach (var enveloppe in configuration.Enveloppes)
            montants[enveloppe.Categorie] = montants.GetValueOrDefault(enveloppe.Categorie) + enveloppe.BudgetParDefaut;

        var parts = new[]
        {
            (Categorie.Essentiel, CibleEssentiel),
            (Categorie.Confort, CibleConfort),
            (Categorie.Epargne, CibleEpargne),
        }
        .Select(p => new PartCategorie(p.Item1, montants.GetValueOrDefault(p.Item1), Part(montants.GetValueOrDefault(p.Item1), revenus), p.Item2))
        .ToList();

        return new Repartition(revenus, parts, montants.GetValueOrDefault(Categorie.NonClassee), revenus - montants.Values.Sum());
    }

    // ---- 3. Suivi des enveloppes ----

    /// <summary>
    /// Compare les dépenses réelles de chaque enveloppe à son budget, sur les derniers mois terminés.
    /// Le dernier mois créé est considéré comme en cours et n'est utilisé que s'il est le seul.
    /// </summary>
    public static IReadOnlyList<SuiviEnveloppe> SuivreEnveloppes(CompteBancaire compte, int nombreMois = 3)
    {
        ArgumentNullException.ThrowIfNull(compte);
        var termines = compte.Mois.Count > 1 ? compte.Mois.Take(compte.Mois.Count - 1) : compte.Mois;
        var observes = termines.TakeLast(nombreMois).ToList();

        return compte.Configuration.Enveloppes.Select(enveloppe =>
        {
            var mesures = observes
                .Select(m => (
                    Budget: m.Enveloppes.Find(e => CalculateurMois.MemeNom(e.Nom, enveloppe.Nom))?.Budget,
                    Depense: m.Operations.Where(o => CalculateurMois.MemeNom(o.Enveloppe, enveloppe.Nom)).Sum(o => o.Debit - o.Credit)))
                .Where(m => m.Budget is not null)
                .ToList();

            var budget = enveloppe.BudgetParDefaut;
            if (mesures.Count == 0)
                return new SuiviEnveloppe(enveloppe.Nom, budget, 0m, 0m, 0, 0, Tendance.SansDonnees, null);

            var moyenne = decimal.Round(mesures.Average(m => m.Depense), 2);
            var maximum = mesures.Max(m => m.Depense);
            var depassements = mesures.Count(m => m.Depense > m.Budget);

            var (tendance, conseil) = moyenne > budget
                ? (Tendance.AAugmenter, ArrondiDizaineSuperieure(moyenne))
                : moyenne < budget * SeuilReduction
                    ? (Tendance.AReduire, ArrondiDizaineSuperieure(moyenne * 1.05m))
                    : (Tendance.Adapte, (decimal?)null);

            if (conseil == budget)
                (tendance, conseil) = (Tendance.Adapte, null);

            return new SuiviEnveloppe(enveloppe.Nom, budget, moyenne, maximum, mesures.Count, depassements, tendance, conseil);
        }).ToList();
    }

    // ---- 4. Simulateur « Et si… ? » ----

    /// <summary>
    /// Simule le budget avec d'autres charges et enveloppes (charges supprimées ou montants changés),
    /// sans modifier le compte.
    /// </summary>
    public static ResultatSimulation Simuler(
        CompteBancaire compte, IEnumerable<ModeleCharge> charges, IEnumerable<ModeleEnveloppe> enveloppes, int horizon = 12)
    {
        ArgumentNullException.ThrowIfNull(compte);
        var actuelle = compte.Configuration;

        var configuration = new ConfigurationBudget { PremierMois = actuelle.PremierMois, SoldeInitial = actuelle.SoldeInitial };
        configuration.Revenus.AddRange(actuelle.Revenus);
        configuration.Charges.AddRange(charges);
        configuration.Enveloppes.AddRange(enveloppes);
        configuration.ComptesCumul.AddRange(actuelle.ComptesCumul);

        // Les mois réels et les opérations prévues sont partagés en lecture seule.
        var simule = new CompteBancaire(configuration);
        foreach (var mois in compte.Mois)
            simule.AjouterMoisExistant(mois);
        simule.OperationsPrevues.AddRange(compte.OperationsPrevues);

        decimal Mensuel(ConfigurationBudget c) => c.Charges.Sum(x => x.NetMensuel) + c.Enveloppes.Sum(x => x.BudgetParDefaut);

        return new ResultatSimulation(
            Mensuel(actuelle) - Mensuel(configuration),
            horizon,
            SoldeFinal(compte, horizon),
            SoldeFinal(simule, horizon));
    }

    // ---- 5. Objectifs d'épargne ----

    public static ResultatObjectifs AnalyserObjectifs(CompteBancaire compte)
    {
        ArgumentNullException.ThrowIfNull(compte);
        var capacite = CapaciteMensuelle(compte);
        var engage = 0m;

        var objectifs = compte.ObjectifsEpargne.Select(objectif =>
        {
            var reste = Math.Max(0m, objectif.Montant - objectif.DejaEpargne);
            var moisRestants = compte.ProchainMois.MoisJusqua(objectif.Echeance) + 1;

            if (reste == 0)
                return new AnalyseObjectif(objectif, Math.Max(0, moisRestants), 0m, 0m, Faisabilite.Atteint);
            if (moisRestants <= 0)
                return new AnalyseObjectif(objectif, 0, reste, reste, Faisabilite.EcheancePassee);

            var mensualite = Math.Ceiling(reste / moisRestants * 100) / 100;
            engage += mensualite;
            var faisabilite = engage <= capacite * 0.8m ? Faisabilite.Tenable
                : engage <= capacite ? Faisabilite.Juste
                : Faisabilite.Difficile;

            return new AnalyseObjectif(objectif, moisRestants, reste, mensualite, faisabilite);
        }).ToList();

        return new ResultatObjectifs(capacite, objectifs);
    }

    /// <summary>Excédent moyen prévu par mois (entrées − sorties) sur les 12 prochains mois ; jamais négatif.</summary>
    public static decimal CapaciteMensuelle(CompteBancaire compte)
    {
        var prevus = Previsionnel.Calculer(compte, 0, 12).MoisPrevus.ToList();
        return prevus.Count == 0 ? 0m : Math.Max(0m, decimal.Round(prevus.Average(m => m.Entrees - m.Sorties), 2));
    }

    // ---- 6. Alertes ----

    public static IReadOnlyList<Alerte> Alertes(CompteBancaire compte)
    {
        ArgumentNullException.ThrowIfNull(compte);
        var alertes = new List<Alerte>();

        var prevision = Previsionnel.Calculer(compte, 0, 12);
        if (prevision.PremierMoisNegatif is { } negatif)
        {
            alertes.Add(new Alerte(NiveauAlerte.Danger, $"Découvert prévu en {negatif.Periode.Libelle}",
                $"Solde prévu : {Euros(negatif.SoldeFin)}. Réduisez une dépense ou décalez une opération prévue."));
        }

        if (compte.Mois.Count > 0)
        {
            var dernier = compte.Mois[^1];
            var calcul = compte.Calculer(dernier.Periode);

            if (calcul.SoldeFinPrevisionnel < 0)
            {
                alertes.Add(new Alerte(NiveauAlerte.Danger, $"Solde de fin de {dernier.Periode.Libelle} négatif",
                    $"Fin de mois prévue à {Euros(calcul.SoldeFinPrevisionnel)}."));
            }

            foreach (var enveloppe in calcul.Enveloppes)
            {
                if (enveloppe.Depassee)
                {
                    alertes.Add(new Alerte(NiveauAlerte.Attention, $"Enveloppe {enveloppe.Nom} dépassée",
                        $"{Euros(enveloppe.Depense)} dépensés pour {Euros(enveloppe.Budget)} prévus en {dernier.Periode.Libelle} " +
                        $"(+{Euros(enveloppe.Depense - enveloppe.Budget)})."));
                }
                else if (enveloppe.Budget > 0 && enveloppe.Depense >= enveloppe.Budget * 0.8m)
                {
                    alertes.Add(new Alerte(NiveauAlerte.Info, $"Enveloppe {enveloppe.Nom} presque utilisée",
                        $"Il reste {Euros(enveloppe.Reste)} sur {Euros(enveloppe.Budget)} en {dernier.Periode.Libelle}."));
                }
            }

            if (compte.Mois.Count > 1)
                alertes.AddRange(HaussesDeCharges(compte.Mois[^2], dernier));
        }

        foreach (var objectif in AnalyserObjectifs(compte).Objectifs.Where(o => o.Faisabilite == Faisabilite.Difficile))
        {
            alertes.Add(new Alerte(NiveauAlerte.Attention, $"Objectif « {objectif.Objectif.Nom} » difficile à tenir",
                $"Il faudrait mettre {Euros(objectif.Mensualite)} de côté par mois ; " +
                $"votre excédent prévu est d'environ {Euros(CapaciteMensuelle(compte))} par mois."));
        }

        var repartition = Repartir(compte.Configuration);
        if (repartition.RevenusMensuels > 0)
        {
            var essentiel = repartition.Parts.Single(p => p.Categorie == Categorie.Essentiel);
            if (essentiel.Part > 0.60m)
            {
                alertes.Add(new Alerte(NiveauAlerte.Info, "Dépenses essentielles élevées",
                    $"Elles représentent {Pourcentage(essentiel.Part)} des revenus (conseillé : {Pourcentage(CibleEssentiel)})."));
            }

            var epargne = repartition.Parts.Single(p => p.Categorie == Categorie.Epargne);
            if (epargne.Part < 0.10m)
            {
                alertes.Add(new Alerte(NiveauAlerte.Info, "Épargne programmée faible",
                    $"{Pourcentage(epargne.Part)} des revenus (conseillé : {Pourcentage(CibleEpargne)}). " +
                    $"Reste non affecté chaque mois : {Euros(repartition.ResteDisponible)}."));
            }
        }

        if (alertes.Count == 0)
            alertes.Add(new Alerte(NiveauAlerte.Info, "Aucune alerte", "Votre budget est équilibré."));

        return alertes.OrderByDescending(a => a.Niveau).ToList();
    }

    /// <summary>Charges (hors enveloppes) plus chères que le mois précédent.</summary>
    internal static IEnumerable<Alerte> HaussesDeCharges(MoisBudget precedent, MoisBudget dernier)
    {
        foreach (var operation in dernier.Operations.Where(o => o.Enveloppe is null && o.Debit > 0))
        {
            var avant = precedent.Operations.Find(o => o.Enveloppe is null && CalculateurMois.MemeNom(o.Libelle, operation.Libelle));
            if (avant is null || avant.Debit <= 0 || operation.Debit <= avant.Debit)
                continue;

            var hausse = (operation.Debit - avant.Debit) / avant.Debit;
            yield return new Alerte(NiveauAlerte.Attention, $"Hausse : {operation.Libelle}",
                $"{Euros(avant.Debit)} en {precedent.Periode.Libelle} → {Euros(operation.Debit)} en {dernier.Periode.Libelle} " +
                $"(+{Pourcentage(hausse)}).");
        }
    }

    // ---- Outils ----

    public static decimal RevenusMensuels(ConfigurationBudget configuration) => configuration.Revenus.Sum(r => r.MontantParDefaut);

    private static decimal SoldeFinal(CompteBancaire compte, int horizon)
    {
        var prevus = Previsionnel.Calculer(compte, 0, horizon).MoisPrevus.ToList();
        return prevus.Count == 0 ? 0m : prevus[^1].SoldeFin;
    }

    private static decimal Part(decimal montant, decimal revenus) => revenus <= 0 ? 0m : montant / revenus;

    private static decimal ArrondiDizaineSuperieure(decimal montant) => Math.Ceiling(montant / 10) * 10;

    private static string Euros(decimal montant) => $"{Montants.Formater(montant)} €";

    private static string Pourcentage(decimal part) => $"{decimal.Round(part * 100, 0)} %";

    /// <summary>Premier mot du nom, sans accents ni majuscules (« Mobile enfant 2 » → « mobile »).</summary>
    public static string CleFamille(string nom)
    {
        var mot = PremierMot(nom);
        var decompose = mot.Normalize(NormalizationForm.FormD);
        var sansAccents = new string(decompose.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return sansAccents.ToLowerInvariant();
    }

    private static string NomFamille(string nom) => PremierMot(nom);

    private static string PremierMot(string nom) =>
        nom.Trim().TrimEnd(':').Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.TrimEnd(':', '.') ?? "";
}
