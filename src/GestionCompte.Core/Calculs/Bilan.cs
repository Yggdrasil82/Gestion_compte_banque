using GestionCompte.Core.Modeles;

namespace GestionCompte.Core.Calculs;

public enum TypePoste
{
    Charge,
    Enveloppe,
    Autre,
    Epargne,
}

/// <summary>Un poste de dépense sur la période (une charge, une enveloppe ou une autre dépense).</summary>
/// <param name="Total">Montant de la période (enveloppe : budget, ou dépense réelle si elle l'a dépassé).</param>
/// <param name="Mois">Nombre de mois de la période où le poste apparaît.</param>
/// <param name="MoyenneMensuelle">Total divisé par le nombre de mois de la période.</param>
/// <param name="MoyennePrecedente">Moyenne par mois sur les mêmes mois un an plus tôt, ou null s'il n'y apparaît pas.</param>
public sealed record PosteBilan(string Nom, TypePoste Type, Categorie Categorie, decimal Total, int Mois, decimal MoyenneMensuelle,
    decimal PartRevenus, decimal? MoyennePrecedente)
{
    /// <summary>Évolution de la moyenne par mois (0,1 = +10 %), ou null sans période précédente.</summary>
    public decimal? Evolution => MoyennePrecedente is > 0 and var avant ? (MoyenneMensuelle - avant) / avant : null;
}

/// <summary>Chiffres d'un mois de la période.</summary>
/// <param name="Depenses">Sorties hors épargne.</param>
public sealed record MoisBilan(PeriodeMois Periode, decimal Revenus, decimal Depenses, decimal Epargne, decimal SoldeFin);

/// <param name="GainAnnuel">Économie possible sur un an, ou null quand elle ne peut pas être chiffrée.</param>
public sealed record PisteEconomie(string Titre, string Detail, decimal? GainAnnuel);

/// <summary>Bilan d'une période : totaux, mois par mois, postes de dépenses et pistes d'économie.</summary>
/// <param name="Revenus">Revenus et autres entrées (remboursements…).</param>
/// <param name="Depenses">Sorties hors épargne.</param>
/// <param name="Epargne">Sorties vers les comptes cumulés d'épargne.</param>
/// <param name="SoldeDebut">Solde au début du premier mois de la période.</param>
/// <param name="Precedent">Totaux des mêmes mois un an plus tôt, ou null si aucun mois n'y existe.</param>
public sealed record ResultatBilan(
    PeriodeMois Debut, PeriodeMois Fin, IReadOnlyList<MoisBilan> Mois,
    decimal Revenus, decimal Depenses, decimal Epargne, decimal SoldeDebut, decimal SoldeFin,
    IReadOnlyList<PosteBilan> Postes, IReadOnlyList<PisteEconomie> Pistes, TotauxBilan? Precedent)
{
    /// <summary>Part des revenus mise de côté (0,1 = 10 %), ou null sans revenus.</summary>
    public decimal? TauxEpargne => Revenus > 0 ? Epargne / Revenus : null;

    public decimal Variation => SoldeFin - SoldeDebut;

    public bool Vide => Mois.Count == 0;

    /// <summary>Ex. « 10 mois, de janvier 2026 à octobre 2026 » (mois réellement présents).</summary>
    public string Etendue => Mois.Count switch
    {
        0 => "Aucun mois créé sur cette période",
        1 => $"1 mois : {Mois[0].Periode.Libelle.ToLower(Montants.Francais)}",
        _ => $"{Mois.Count} mois, de {Mois[0].Periode.Libelle.ToLower(Montants.Francais)} à {Mois[^1].Periode.Libelle.ToLower(Montants.Francais)}",
    };
}

/// <param name="Mois">Nombre de mois de la période précédente présents dans le compte.</param>
public sealed record TotauxBilan(int Mois, decimal Revenus, decimal Depenses, decimal Epargne)
{
    public decimal DepensesMensuelles => Mois == 0 ? 0m : Depenses / Mois;
    public decimal RevenusMensuels => Mois == 0 ? 0m : Revenus / Mois;
    public decimal EpargneMensuelle => Mois == 0 ? 0m : Epargne / Mois;
}

/// <summary>
/// Bilan annuel (ou de 12 mois glissants) calculé à partir des mois créés : les enveloppes comptent pour leur budget
/// (ou la dépense réelle si elle l'a dépassé), comme dans le solde. Revenus − dépenses − épargne = variation du solde.
/// </summary>
public static class Bilan
{
    /// <summary>Hausse retenue comme piste d'économie : au moins 10 % et 10 € par mois.</summary>
    public const decimal SeuilHausse = 0.10m;
    public const decimal HausseMinimale = 10m;

    /// <summary>Nombre maximal de pistes « poste en hausse ».</summary>
    private const int NombreHausses = 3;

    /// <summary>Années ayant au moins un mois créé, de la plus récente à la plus ancienne.</summary>
    public static IReadOnlyList<int> Annees(CompteBancaire compte) =>
        compte.Mois.Select(m => m.Periode.Annee).Distinct().OrderDescending().ToList();

    /// <summary>12 derniers mois terminés : du même mois un an plus tôt au mois précédant <paramref name="moisDuJour"/>.</summary>
    public static (PeriodeMois Debut, PeriodeMois Fin) DouzeDerniersMois(PeriodeMois moisDuJour)
    {
        var fin = moisDuJour.Precedent();
        return (new PeriodeMois(moisDuJour.Annee - 1, moisDuJour.Mois), fin);
    }

    public static ResultatBilan Calculer(CompteBancaire compte, PeriodeMois debut, PeriodeMois fin)
    {
        ArgumentNullException.ThrowIfNull(compte);
        var configuration = compte.Configuration;
        var comptesEpargne = ComptesEpargne(configuration);

        var detail = Detailler(compte, debut, fin, comptesEpargne);
        // Comparaison avec les mêmes mois un an plus tôt (année en cours : janvier à octobre contre janvier à octobre).
        var precedent = detail.Mois.Count == 0
            ? detail
            : Detailler(compte, UnAnAvant(detail.Mois[0].Periode), UnAnAvant(detail.Mois[^1].Periode), comptesEpargne);

        var revenus = detail.Mois.Sum(m => m.Revenus);
        // Moyennes sur tous les mois de la période (une dépense ponctuelle est étalée sur la période).
        var moyennesPrecedentes = precedent.Postes.ToDictionary(p => Cle(p.Nom), p => p.Total / precedent.Mois.Count);
        var postes = detail.Postes
            .Select(p =>
            {
                var moyenne = decimal.Round(p.Total / detail.Mois.Count, 2);
                decimal? avant = moyennesPrecedentes.TryGetValue(Cle(p.Nom), out var m) ? decimal.Round(m, 2) : null;
                return new PosteBilan(p.Nom, p.Type, p.Categorie, p.Total, p.Mois, moyenne, revenus > 0 ? p.Total / revenus : 0m, avant);
            })
            .OrderByDescending(p => p.Total)
            .ToList();

        TotauxBilan? totauxPrecedents = precedent.Mois.Count == 0
            ? null
            : new TotauxBilan(precedent.Mois.Count, precedent.Mois.Sum(m => m.Revenus), precedent.Mois.Sum(m => m.Depenses),
                precedent.Mois.Sum(m => m.Epargne));

        var soldeDebut = detail.Mois.Count == 0 ? 0m : detail.SoldeDebut;
        var soldeFin = detail.Mois.Count == 0 ? 0m : detail.Mois[^1].SoldeFin;
        return new ResultatBilan(debut, fin, detail.Mois, revenus, detail.Mois.Sum(m => m.Depenses), detail.Mois.Sum(m => m.Epargne),
            soldeDebut, soldeFin, postes, Pistes(compte, detail, postes), totauxPrecedents);
    }

    // ---- Calcul des mois et des postes ----

    private sealed class PosteBrut
    {
        public PosteBrut(string nom, TypePoste type, Categorie categorie)
        {
            Nom = nom;
            Type = type;
            Categorie = categorie;
        }

        public string Nom { get; }
        public TypePoste Type { get; }
        public Categorie Categorie { get; }
        public decimal Total { get; set; }
        public int Mois { get; set; }
        public PeriodeMois? DernierMois { get; set; }

        // Enveloppes : dépense réelle et budget, pour les pistes d'économie.
        public decimal Depense { get; set; }
        public decimal Budget { get; set; }
        public int Depassements { get; set; }

        public void Ajouter(PeriodeMois periode, decimal montant)
        {
            Total += montant;
            if (DernierMois != periode)
            {
                Mois++;
                DernierMois = periode;
            }
        }
    }

    private sealed record Detail(IReadOnlyList<MoisBilan> Mois, decimal SoldeDebut, IReadOnlyList<PosteBrut> Postes);

    private static Detail Detailler(CompteBancaire compte, PeriodeMois debut, PeriodeMois fin, IReadOnlySet<string> comptesEpargne)
    {
        var configuration = compte.Configuration;
        var postes = new Dictionary<string, PosteBrut>();
        PosteBrut Poste(string nom, TypePoste type, Categorie categorie)
        {
            var cle = Cle(nom);
            if (!postes.TryGetValue(cle, out var poste))
                postes[cle] = poste = new PosteBrut(nom.Trim(), type, categorie);
            return poste;
        }

        var mois = new List<MoisBilan>();
        var solde = configuration.SoldeInitial;
        decimal? soldeDebut = null;
        foreach (var m in compte.Mois)
        {
            var resultat = CalculateurMois.Calculer(m, solde);
            solde = resultat.SoldeFinPrevisionnel;
            if (m.Periode < debut || m.Periode > fin)
                continue;
            soldeDebut ??= resultat.AncienSolde;

            var revenus = resultat.TotalRevenus;
            var depenses = 0m;
            var epargne = 0m;

            foreach (var enveloppe in resultat.Enveloppes)
            {
                var montant = Math.Max(enveloppe.Budget, enveloppe.Depense);
                var poste = Poste(enveloppe.Nom, TypePoste.Enveloppe, CategorieEnveloppe(configuration, enveloppe.Nom));
                poste.Ajouter(m.Periode, montant);
                poste.Depense += enveloppe.Depense;
                poste.Budget += enveloppe.Budget;
                if (enveloppe.Depassee)
                    poste.Depassements++;
                depenses += montant;
            }

            foreach (var operation in m.Operations)
            {
                var net = operation.Debit - operation.Credit;
                // Dépense imputée à une enveloppe du mois : déjà comptée avec l'enveloppe.
                if (m.Enveloppes.Any(e => CalculateurMois.MemeNom(e.Nom, operation.Enveloppe)))
                    continue;

                if (operation.CompteCumul is { } cumul && comptesEpargne.Contains(Cle(cumul)))
                {
                    Poste(operation.Libelle, TypePoste.Epargne, Categorie.Epargne).Ajouter(m.Periode, net);
                    epargne += net;
                    continue;
                }

                var charge = configuration.Charges.FirstOrDefault(c => CalculateurMois.MemeNom(c.Nom, operation.Libelle));
                if (charge is not null)
                {
                    Poste(charge.Nom, TypePoste.Charge, charge.Categorie).Ajouter(m.Periode, net);
                    depenses += net;
                }
                else if (net >= 0)
                {
                    Poste(operation.Libelle, TypePoste.Autre, Categorie.NonClassee).Ajouter(m.Periode, net);
                    depenses += net;
                }
                else
                {
                    // Remboursement, virement reçu… : compté avec les revenus.
                    revenus -= net;
                }
            }

            mois.Add(new MoisBilan(m.Periode, revenus, depenses, epargne, resultat.SoldeFinPrevisionnel));
        }

        return new Detail(mois, soldeDebut ?? 0m, postes.Values.Where(p => p.Mois > 0 && p.Total != 0).ToList());
    }

    /// <summary>Comptes cumulés alimentés par une charge de la catégorie Épargne.</summary>
    private static IReadOnlySet<string> ComptesEpargne(ConfigurationBudget configuration) =>
        configuration.Charges
            .Where(c => c.Categorie == Categorie.Epargne && !string.IsNullOrWhiteSpace(c.CompteCumul))
            .Select(c => Cle(c.CompteCumul!))
            .ToHashSet();

    private static Categorie CategorieEnveloppe(ConfigurationBudget configuration, string nom) =>
        configuration.Enveloppes.FirstOrDefault(e => CalculateurMois.MemeNom(e.Nom, nom))?.Categorie ?? Categorie.Essentiel;

    // ---- Pistes d'économie ----

    private static List<PisteEconomie> Pistes(CompteBancaire compte, Detail detail, IReadOnlyList<PosteBilan> postes)
    {
        var pistes = new List<PisteEconomie>();

        // Postes en hausse par rapport à l'année d'avant.
        foreach (var poste in postes
                     .Where(p => p.Type != TypePoste.Epargne && p.MoyennePrecedente is > 0)
                     .Select(p => (Poste: p, Hausse: p.MoyenneMensuelle - p.MoyennePrecedente!.Value))
                     .Where(h => h.Hausse >= HausseMinimale && h.Poste.Evolution >= SeuilHausse)
                     .OrderByDescending(h => h.Hausse)
                     .Take(NombreHausses))
        {
            pistes.Add(new PisteEconomie(
                $"« {poste.Poste.Nom} » en hausse de {Pourcentage(poste.Poste.Evolution!.Value)}",
                $"{Euros(poste.Poste.MoyenneMensuelle)} par mois contre {Euros(poste.Poste.MoyennePrecedente!.Value)} l'année d'avant.",
                decimal.Round(poste.Hausse * 12, 2)));
        }

        // Enveloppes : budget trop large ou souvent dépassé.
        foreach (var enveloppe in detail.Postes.Where(p => p.Type == TypePoste.Enveloppe && p.Mois > 0))
        {
            var budget = enveloppe.Budget / enveloppe.Mois;
            var depense = enveloppe.Depense / enveloppe.Mois;
            if (enveloppe.Depassements * 3 >= enveloppe.Mois && enveloppe.Depassements > 0)
            {
                pistes.Add(new PisteEconomie(
                    $"Enveloppe « {enveloppe.Nom} » dépassée {enveloppe.Depassements} mois sur {enveloppe.Mois}",
                    $"Dépense moyenne {Euros(depense)} pour un budget de {Euros(budget)} : surveillez ce poste ou ajustez son budget.",
                    null));
            }
            else if (budget > 0 && depense > 0 && depense < budget * AideBudget.SeuilReduction)
            {
                var ecart = decimal.Round(budget - depense, 2);
                pistes.Add(new PisteEconomie(
                    $"Enveloppe « {enveloppe.Nom} » plus large que nécessaire",
                    $"Vous dépensez en moyenne {Euros(depense)} pour un budget de {Euros(budget)} : le budget peut baisser d'environ {Euros(ecart)} par mois.",
                    ecart * 12));
            }
        }

        // Charges « confort » (abonnements, loisirs…).
        var confort = postes.Where(p => p.Type == TypePoste.Charge && p.Categorie == Categorie.Confort && p.Total > 0).ToList();
        if (confort.Count > 0)
        {
            pistes.Add(new PisteEconomie(
                $"Charges « confort » : {Euros(confort.Sum(p => p.Total))} sur la période",
                $"{string.Join(", ", confort.Select(p => p.Nom))} : vérifiez que chacune sert encore (un abonnement arrêté, c'est 12 mois d'économie).",
                null));
        }

        // Plusieurs charges de la même famille (forfaits mobiles, assurances…).
        foreach (var groupe in postes
                     .Where(p => p.Type == TypePoste.Charge && p.Total > 0)
                     .GroupBy(p => AideBudget.CleFamille(p.Nom))
                     .Where(g => g.Key.Length > 0 && g.Count() >= 2))
        {
            pistes.Add(new PisteEconomie(
                $"{groupe.Count()} charges « {groupe.First().Nom.Trim().Split(' ')[0]} » : {Euros(groupe.Sum(p => p.Total))} sur la période",
                $"{string.Join(", ", groupe.Select(p => p.Nom))} : une offre groupée ou une renégociation peut coûter moins cher.",
                null));
        }

        return pistes;
    }

    /// <summary>Nom affiché du type d'un poste du bilan.</summary>
    public static string NomType(TypePoste type) => type switch
    {
        TypePoste.Charge => "Charge",
        TypePoste.Enveloppe => "Enveloppe",
        TypePoste.Epargne => "Épargne",
        _ => "Autre dépense",
    };

    private static PeriodeMois UnAnAvant(PeriodeMois periode) => new(periode.Annee - 1, periode.Mois);

    private static string Euros(decimal montant) => $"{Montants.Formater(montant)} €";

    private static string Pourcentage(decimal part) => $"{decimal.Round(part * 100, 0)} %";

    private static string Cle(string nom) => nom.Trim().ToUpperInvariant();
}
