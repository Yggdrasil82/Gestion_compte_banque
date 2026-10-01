using System.Globalization;
using System.Text;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Core.Import;

public enum StatutImport
{
    /// <summary>Une opération prévue du mois a le même montant : elle sera pointée.</summary>
    Rapprochee,

    /// <summary>Une opération prévue ressemble (même libellé) mais le montant diffère : il sera corrigé.</summary>
    MontantAjuste,

    /// <summary>Un revenu du mois correspond : il sera marqué reçu, avec le montant réel.</summary>
    RevenuRecu,

    /// <summary>Aucune correspondance : l'opération sera ajoutée au mois.</summary>
    Nouvelle,

    /// <summary>Déjà importée lors d'un import précédent (même identifiant bancaire).</summary>
    DejaImportee,

    /// <summary>Antérieure au premier mois suivi dans l'application.</summary>
    AvantDebut,
}

/// <summary>Opération ou revenu du mois auquel une ligne du relevé peut être rattachée.</summary>
/// <param name="Index">Position dans la liste des opérations (ou des revenus) du mois.</param>
public sealed record Candidat(bool EstRevenu, int Index, string Libelle, decimal Montant)
{
    public override string ToString() => $"{Libelle} ({Montants.Formater(Montant)} €)";
}

/// <summary>Une ligne du relevé et ce que l'import en fera ; <see cref="Importer"/>, <see cref="Choix"/> et <see cref="Enveloppe"/> sont modifiables.</summary>
public sealed class LigneImport
{
    internal LigneImport(OperationBancaire source, PeriodeMois periode, StatutImport statut, IReadOnlyList<Candidat> candidats)
    {
        Source = source;
        Periode = periode;
        StatutInitial = statut;
        Candidats = candidats;
        Importer = statut is not (StatutImport.DejaImportee or StatutImport.AvantDebut);
    }

    public OperationBancaire Source { get; }

    /// <summary>Mois où la ligne sera rangée.</summary>
    public PeriodeMois Periode { get; }

    /// <summary>Mois de la date de l'opération.</summary>
    public PeriodeMois PeriodeDate => new(Source.Date.Year, Source.Date.Month);

    /// <summary>Date antérieure au mois en cours : l'utilisateur choisit de la ranger dans le mois en cours ou dans le sien.</summary>
    public bool DateAnterieure { get; internal init; }

    /// <summary>Statut proposé par le rapprochement automatique.</summary>
    public StatutImport StatutInitial { get; }

    /// <summary>Opérations et revenus du mois auxquels la ligne peut être rattachée.</summary>
    public IReadOnlyList<Candidat> Candidats { get; }

    public bool Importer { get; set; }

    /// <summary>Rattachement choisi ; null = nouvelle opération.</summary>
    public Candidat? Choix { get; set; }

    /// <summary>Enveloppe d'une nouvelle dépense (proposée par les règles de classement).</summary>
    public string? Enveloppe { get; set; }

    public string? EnveloppeProposee { get; internal set; }

    public bool Modifiable => StatutInitial is not (StatutImport.DejaImportee or StatutImport.AvantDebut);

    /// <summary>Statut selon le choix actuel.</summary>
    public StatutImport Statut => !Modifiable ? StatutInitial
        : Choix is null ? StatutImport.Nouvelle
        : Choix.EstRevenu ? StatutImport.RevenuRecu
        : Choix.Montant == Math.Abs(Source.Montant) ? StatutImport.Rapprochee
        : StatutImport.MontantAjuste;
}

/// <param name="MoisCourant">Mois en cours qui reçoit les opérations datées d'un mois antérieur (null = chacune dans son mois).</param>
public sealed record PlanImport(IReadOnlyList<LigneImport> Lignes, IReadOnlyList<PeriodeMois> MoisACreer, decimal? SoldeBanque, DateOnly? DateSolde,
    PeriodeMois? MoisCourant = null);

public sealed record ResultatImport(int Rapprochees, int Ajustees, int RevenusRecus, int Nouvelles, int Ignorees, IReadOnlyList<PeriodeMois> MoisCrees)
{
    public int Total => Rapprochees + Ajustees + RevenusRecus + Nouvelles;
}

/// <summary>
/// Import d'un relevé bancaire : rapprochement avec les opérations prévues, ajout des opérations manquantes,
/// classement dans les enveloppes et protection contre les doublons.
/// </summary>
public static class ImportReleve
{
    private static readonly HashSet<string> MotsIgnores = new(StringComparer.Ordinal)
    {
        "CB", "CARTE", "PRLV", "PRELEVEMENT", "SEPA", "VIR", "VIREMENT", "PAIEMENT", "PAR", "DE", "DU", "DES", "LA", "LE", "LES",
        "ET", "EN", "AU", "AUX", "ECH", "ECHEANCE", "FACTURE", "INST", "INSTANTANE", "RECU", "EMIS", "SCT", "FR", "EUR", "REF",
    };

    /// <summary>Prépare l'import sans rien modifier : statut de chaque ligne et mois à créer.</summary>
    /// <param name="moisCourant">Mois en cours : une opération datée d'un mois antérieur (chevauchement de relevé) y est rangée,
    /// sauf si elle est dans <paramref name="dansLeurMois"/> ; null = chaque opération dans le mois de sa date.
    /// Une opération d'avant le premier mois suivi reste ignorée.</param>
    /// <param name="dansLeurMois">Identifiants bancaires des opérations antérieures à ranger dans le mois de leur date.</param>
    public static PlanImport Preparer(CompteBancaire compte, ReleveBancaire releve, PeriodeMois? moisCourant = null,
        IReadOnlySet<string>? dansLeurMois = null)
    {
        ArgumentNullException.ThrowIfNull(compte);
        ArgumentNullException.ThrowIfNull(releve);

        var dejaImportes = compte.Mois
            .SelectMany(m => m.Operations.Select(o => o.IdentifiantBanque).Concat(m.Revenus.Select(r => r.IdentifiantBanque)))
            .Where(id => id is not null)
            .ToHashSet();

        var premier = compte.Configuration.PremierMois;
        var lignes = new List<LigneImport>();
        var aRapprocher = new List<(OperationBancaire Operation, PeriodeMois Periode)>();
        var courant = moisCourant is { } m && m >= premier ? m : (PeriodeMois?)null;

        foreach (var operation in releve.Operations.OrderBy(o => o.Date))
        {
            var periode = new PeriodeMois(operation.Date.Year, operation.Date.Month);
            if (dejaImportes.Contains(operation.Identifiant))
                lignes.Add(new LigneImport(operation, periode, StatutImport.DejaImportee, Array.Empty<Candidat>()));
            else if (periode < premier)
                lignes.Add(new LigneImport(operation, periode, StatutImport.AvantDebut, Array.Empty<Candidat>()));
            else if (courant is { } mois && periode < mois)
                aRapprocher.Add((operation, dansLeurMois?.Contains(operation.Identifiant) == true ? periode : mois));
            else
                aRapprocher.Add((operation, periode));
        }

        var dernierPeriode = aRapprocher.Count == 0 ? (PeriodeMois?)null : aRapprocher.Max(o => o.Periode);
        var moisACreer = new List<PeriodeMois>();
        if (dernierPeriode is { } fin)
        {
            for (var p = compte.ProchainMois; p <= fin; p = p.Suivant())
                moisACreer.Add(p);
        }

        foreach (var groupe in aRapprocher.GroupBy(o => o.Periode))
        {
            // Un mois pas encore créé est rapproché avec le mois tel qu'il sera créé.
            var mois = compte.Trouver(groupe.Key) ?? compte.Generer(groupe.Key);
            lignes.AddRange(Rapprocher(compte.Configuration, mois, groupe.Select(o => o.Operation).ToList(), courant));
        }

        return new PlanImport(
            lignes.OrderBy(l => l.Source.Date).ThenBy(l => l.Source.Identifiant, StringComparer.Ordinal).ToList(),
            moisACreer, releve.Solde, releve.DateSolde, courant);
    }

    private static IEnumerable<LigneImport> Rapprocher(ConfigurationBudget configuration, MoisBudget mois, List<OperationBancaire> operations,
        PeriodeMois? courant = null)
    {
        var candidatsRevenus = mois.Revenus
            .Select((r, i) => (Ligne: r, Candidat: new Candidat(true, i, r.Nom, r.Montant)))
            .Where(x => !x.Ligne.Recu && x.Ligne.IdentifiantBanque is null)
            .ToList();
        var candidatsOperations = mois.Operations
            .Select((o, i) => (Ligne: o, Candidat: new Candidat(false, i, o.Libelle, o.Debit > 0 ? o.Debit : o.Credit)))
            .Where(x => !x.Ligne.Pointee && x.Ligne.IdentifiantBanque is null)
            .ToList();

        var choix = new Dictionary<OperationBancaire, Candidat>();
        var pris = new HashSet<Candidat>();

        bool Libre(Candidat c) => !pris.Contains(c);
        void Prendre(OperationBancaire o, Candidat c) { choix[o] = c; pris.Add(c); }

        // 1. Crédits → revenus (même montant ou libellé proche).
        foreach (var operation in operations.Where(o => o.Montant > 0))
        {
            var meilleur = candidatsRevenus
                .Where(x => Libre(x.Candidat))
                .Select(x => (x.Candidat, Score: (x.Ligne.Montant == operation.Montant ? 2 : 0) + Ressemblance(operation.Libelle, x.Ligne.Nom)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();
            if (meilleur.Candidat is not null)
                Prendre(operation, meilleur.Candidat);
        }

        // 2. Même montant exact (et libellé proche, ou montant unique dans le mois).
        foreach (var operation in operations.Where(o => !choix.ContainsKey(o)))
        {
            var memeMontant = candidatsOperations
                .Where(x => Libre(x.Candidat) && MemeMontant(x.Ligne, operation.Montant))
                .Select(x => (x.Candidat, Score: Ressemblance(operation.Libelle, x.Ligne.Libelle)))
                .OrderByDescending(x => x.Score)
                .ToList();
            // Sans libellé proche, un montant unique suffit pour un prélèvement (« PRLV SEPA VEOLIA » pour « Eau »),
            // pas pour un paiement carte, qui correspond rarement à une charge prévue.
            if (memeMontant.Count > 0 && (memeMontant[0].Score > 0 || (memeMontant.Count == 1 && !EstPaiementCarte(operation))))
                Prendre(operation, memeMontant[0].Candidat);
        }

        // 3. Libellé très proche et montant voisin (facture qui varie) : montant ajusté.
        foreach (var operation in operations.Where(o => !choix.ContainsKey(o) && o.Montant < 0))
        {
            var montant = -operation.Montant;
            var proche = candidatsOperations
                .Where(x => Libre(x.Candidat) && x.Ligne.Enveloppe is null && x.Ligne.Debit > 0
                            && montant / x.Ligne.Debit is >= 0.67m and <= 1.5m)
                .Select(x => (x.Candidat, Score: Ressemblance(operation.Libelle, x.Ligne.Libelle)))
                .Where(x => x.Score >= 0.5)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();
            if (proche.Candidat is not null)
                Prendre(operation, proche.Candidat);
        }

        var enveloppes = mois.Enveloppes.Select(e => e.Nom).ToList();
        foreach (var operation in operations)
        {
            var candidats = (operation.Montant > 0
                    ? candidatsRevenus.Select(x => x.Candidat).Concat(candidatsOperations.Where(x => x.Ligne.Credit > 0).Select(x => x.Candidat))
                    : candidatsOperations.Where(x => x.Ligne.Debit > 0).Select(x => x.Candidat))
                .ToList();

            var trouve = choix.GetValueOrDefault(operation);
            var periodeDate = new PeriodeMois(operation.Date.Year, operation.Date.Month);
            var ligne = new LigneImport(operation, mois.Periode, trouve is null ? StatutImport.Nouvelle : StatutImport.Rapprochee, candidats)
            {
                Choix = trouve,
                DateAnterieure = courant is { } c && periodeDate < c,
            };

            if (operation.Montant < 0)
            {
                ligne.EnveloppeProposee = EnveloppePour(configuration.Regles, operation.Libelle, enveloppes);
                ligne.Enveloppe = ligne.EnveloppeProposee;
            }

            yield return ligne;
        }
    }

    /// <summary>Applique l'import : crée les mois nécessaires, pointe, ajuste ou ajoute les opérations.</summary>
    public static ResultatImport Appliquer(CompteBancaire compte, PlanImport plan)
    {
        ArgumentNullException.ThrowIfNull(compte);
        ArgumentNullException.ThrowIfNull(plan);

        var aImporter = plan.Lignes.Where(l => l.Importer && l.Modifiable).ToList();
        var crees = new List<PeriodeMois>();
        if (aImporter.Count > 0)
        {
            var fin = aImporter.Max(l => l.Periode);
            while (compte.ProchainMois <= fin)
                crees.Add(compte.CreerMoisSuivant().Periode);
        }

        int rapprochees = 0, ajustees = 0, revenus = 0, nouvelles = 0;
        foreach (var ligne in aImporter)
        {
            var mois = compte.Trouver(ligne.Periode)!;
            var source = ligne.Source;

            if (ligne.Choix is { EstRevenu: true } choixRevenu)
            {
                var revenu = mois.Revenus[choixRevenu.Index];
                revenu.Montant = source.Montant;
                revenu.Recu = true;
                revenu.IdentifiantBanque = source.Identifiant;
                revenus++;
                continue;
            }

            if (ligne.Choix is { } choixOperation)
            {
                var operation = mois.Operations[choixOperation.Index];
                if (ligne.Statut == StatutImport.MontantAjuste)
                    ajustees++;
                else
                    rapprochees++;
                AppliquerMontant(operation, source.Montant);
                operation.Pointee = true;
                operation.IdentifiantBanque = source.Identifiant;
                continue;
            }

            var nouvelle = new Operation(source.Libelle)
            {
                Pointee = true,
                IdentifiantBanque = source.Identifiant,
                Enveloppe = source.Montant < 0 && !string.IsNullOrWhiteSpace(ligne.Enveloppe) ? ligne.Enveloppe : null,
            };
            AppliquerMontant(nouvelle, source.Montant);
            mois.Operations.Add(nouvelle);
            nouvelles++;
        }

        return new ResultatImport(rapprochees, ajustees, revenus, nouvelles, plan.Lignes.Count - aImporter.Count, crees);
    }

    /// <summary>
    /// Solde réel d'après l'application : solde de départ + revenus reçus + opérations pointées.
    /// Comparable au solde donné par la banque quand tout est pointé.
    /// </summary>
    /// <param name="jusquAu">Date du solde de la banque : seuls les mois jusqu'à celui-ci sont comptés
    /// (avant le premier mois, il reste le solde de départ de la configuration).</param>
    public static decimal SoldePointe(CompteBancaire compte, DateOnly? jusquAu = null)
    {
        var dernier = jusquAu is { } date ? new PeriodeMois(date.Year, date.Month) : (PeriodeMois?)null;
        return compte.Configuration.SoldeInitial + compte.Mois
            .Where(m => dernier is null || m.Periode <= dernier.Value)
            .Sum(m => m.Revenus.Where(r => r.Recu).Sum(r => r.Montant)
                      + m.Operations.Where(o => o.Pointee).Sum(o => o.Credit - o.Debit));
    }

    /// <summary>Enveloppe donnée par la première règle dont le mot-clé figure dans le libellé, si l'enveloppe existe.</summary>
    public static string? EnveloppePour(IEnumerable<RegleClassement> regles, string libelle, IReadOnlyCollection<string> enveloppes)
    {
        var mots = Mots(libelle, filtrer: false);
        foreach (var regle in regles)
        {
            var cle = Mots(regle.MotCle, filtrer: false);
            if (cle.Count == 0 || !ContientSuite(mots, cle))
                continue;
            var enveloppe = enveloppes.FirstOrDefault(e => CalculateurMois.MemeNom(e, regle.Enveloppe));
            if (enveloppe is not null)
                return enveloppe;
        }
        return null;
    }

    /// <summary>Mot le plus significatif d'un libellé, proposé comme mot-clé de règle (ex. « CB CARREFOUR MARKET » → « CARREFOUR »).</summary>
    public static string? MotCleSuggere(string libelle) => Mots(libelle, filtrer: true).FirstOrDefault();

    /// <summary>Ressemblance entre deux libellés, de 0 (aucun mot commun) à 1.</summary>
    public static double Ressemblance(string a, string b)
    {
        var motsA = Mots(a, filtrer: true);
        var motsB = Mots(b, filtrer: true);
        if (motsA.Count == 0 || motsB.Count == 0)
            return 0;

        var communs = motsA.Count(m => motsB.Any(n => n == m || (m.Length >= 4 && n.Length >= 4 && (n.StartsWith(m) || m.StartsWith(n)))));
        return (double)communs / Math.Min(motsA.Count, motsB.Count);
    }

    private static bool EstPaiementCarte(OperationBancaire operation)
    {
        var libelle = operation.LibelleBanque.TrimStart().ToUpperInvariant();
        return libelle.StartsWith("CB ", StringComparison.Ordinal) || libelle.StartsWith("CARTE ", StringComparison.Ordinal)
            || operation.Type.Equals("POS", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MemeMontant(Operation operation, decimal montantBanque) =>
        montantBanque < 0
            ? operation.Debit == -montantBanque && operation.Credit == 0
            : operation.Credit == montantBanque && operation.Debit == 0;

    private static void AppliquerMontant(Operation operation, decimal montantBanque)
    {
        operation.Debit = montantBanque < 0 ? -montantBanque : 0m;
        operation.Credit = montantBanque > 0 ? montantBanque : 0m;
    }

    private static bool ContientSuite(IReadOnlyList<string> mots, IReadOnlyList<string> suite)
    {
        for (var i = 0; i + suite.Count <= mots.Count; i++)
        {
            if (suite.Select((m, j) => mots[i + j] == m).All(x => x))
                return true;
        }
        return false;
    }

    /// <summary>Mots en majuscules sans accents ; avec <paramref name="filtrer"/>, sans les mots bancaires courants, nombres et mots de moins de 3 lettres.</summary>
    private static List<string> Mots(string texte, bool filtrer)
    {
        var decompose = texte.ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sansAccents = new string(decompose.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var mots = sansAccents.Split(sansAccents.Where(c => !char.IsLetterOrDigit(c)).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries);
        return mots
            .Where(m => !filtrer || (m.Length >= 3 && !m.All(char.IsDigit) && !MotsIgnores.Contains(m)))
            .ToList();
    }
}
