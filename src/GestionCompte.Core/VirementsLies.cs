using System.Globalization;
using System.Text;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Core;

/// <param name="Modifie">L'autre compte a changé et doit être enregistré.</param>
/// <param name="MoisCrees">Mois créés dans l'autre compte pour y mettre un virement.</param>
/// <param name="Ignores">Virements datés d'avant le premier mois de l'autre compte : ils n'y sont pas reportés.</param>
public sealed record ResultatSynchronisation(bool Modifie, IReadOnlyList<PeriodeMois> MoisCrees, int Ignores);

/// <summary>
/// Virements entre deux comptes de l'application : un débit « vers le livret » dans le compte courant
/// a son double, un crédit, dans le livret. Les deux opérations portent le même identifiant de lien ;
/// le compte modifié en dernier recopie ses virements dans l'autre (création, modification, suppression).
/// Il en va de même pour les charges de la configuration (virements de chaque mois).
/// </summary>
public static class VirementsLies
{
    public static string NouveauLien() => Guid.NewGuid().ToString("N");

    /// <summary>Lien du virement d'un mois créé par une charge liée : le même dans les deux comptes, quel que soit celui qui crée le mois.</summary>
    public static string LienDuMois(string lienCharge, PeriodeMois periode) =>
        string.Create(CultureInfo.InvariantCulture, $"{lienCharge}:{periode.Annee:D4}-{periode.Mois:D2}");

    /// <summary>Opération d'un nouveau mois pour une charge de la configuration (liée à l'autre compte si c'est un virement).</summary>
    public static Operation OperationDe(ModeleCharge charge, PeriodeMois periode) =>
        new(charge.Nom, charge.Debit, charge.Credit)
        {
            CompteCumul = charge.CompteCumul,
            CategorieOperation = charge.CategorieOperation,
            CompteLie = charge.Lien is null ? null : charge.CompteLie,
            IdLien = charge.Lien is null || charge.CompteLie is null ? null : LienDuMois(charge.Lien, periode),
        };

    /// <summary>Comptes (fichiers) vers lesquels ce compte a des virements liés.</summary>
    public static IReadOnlySet<string> ComptesLies(CompteBancaire compte) =>
        compte.Configuration.Charges.Where(c => c.Lien is not null).Select(c => c.CompteLie)
            .Concat(compte.Mois.SelectMany(m => m.Operations).Where(o => o.IdLien is not null).Select(o => o.CompteLie))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Empreinte des virements de <paramref name="compte"/> liés à <paramref name="cible"/> :
    /// quand elle ne change pas, l'autre compte est déjà à jour.
    /// </summary>
    public static string Signature(CompteBancaire compte, string cible)
    {
        var texte = new StringBuilder();
        texte.Append(compte.Configuration.PremierMois.Annee).Append('-').Append(compte.Configuration.PremierMois.Mois).Append('\n');
        foreach (var charge in ChargesVers(compte, cible))
            texte.Append(string.Create(CultureInfo.InvariantCulture,
                $"C|{charge.Lien}|{charge.Nom}|{charge.Debit}|{charge.Credit}|{charge.Frequence}|{charge.Depart?.Annee}-{charge.Depart?.Mois}\n"));
        foreach (var (periode, operation) in OperationsVers(compte, cible))
            texte.Append(string.Create(CultureInfo.InvariantCulture,
                $"O|{operation.IdLien}|{periode.Annee}-{periode.Mois}|{operation.Libelle}|{operation.Debit}|{operation.Credit}\n"));
        return texte.ToString();
    }

    /// <summary>
    /// Recopie dans <paramref name="cible"/> les virements de <paramref name="source"/> liés à elle :
    /// les doubles sont créés, mis à jour (libellé, montants inversés, mois) ou supprimés ;
    /// les mois manquants de la cible sont créés. Le compte source n'est jamais modifié.
    /// </summary>
    /// <param name="idSource">Fichier du compte source (« compte.db »).</param>
    /// <param name="idCible">Fichier du compte cible (« compte-2.db »).</param>
    public static ResultatSynchronisation Synchroniser(CompteBancaire source, string idSource, CompteBancaire cible, string idCible)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(cible);

        var modifie = SynchroniserCharges(source, idSource, cible, idCible);

        var operations = OperationsVers(source, idCible).ToList();
        var premierCible = cible.Configuration.PremierMois;
        var aReporter = operations.Where(x => x.Periode >= premierCible).ToList();
        var ignores = operations.Count - aReporter.Count;

        // 1. Mois manquants de l'autre compte (créés avec sa configuration, dont les virements de chaque mois).
        var crees = new List<PeriodeMois>();
        if (aReporter.Count > 0)
        {
            var fin = aReporter.Max(x => x.Periode);
            while (cible.ProchainMois <= fin)
                crees.Add(cible.CreerMoisSuivant().Periode);
        }

        // 2. Doubles dont le virement n'existe plus (supprimé, délié ou lié à un autre compte).
        //    Les mois d'avant le premier mois de la source ne peuvent pas avoir de virement : ils ne sont pas touchés.
        var liens = aReporter.Select(x => x.Operation.IdLien!).ToHashSet(StringComparer.Ordinal);
        foreach (var mois in cible.Mois.Where(m => m.Periode >= source.Configuration.PremierMois))
            if (mois.Operations.RemoveAll(o => o.IdLien is not null && Meme(o.CompteLie, idSource) && !liens.Contains(o.IdLien)) > 0)
                modifie = true;

        // 3. Doubles créés ou mis à jour.
        foreach (var (periode, operation) in aReporter)
        {
            var mois = cible.Trouver(periode)!;
            var trouve = cible.Mois
                .SelectMany(m => m.Operations.Select(o => (Mois: m, Operation: o)))
                .FirstOrDefault(x => x.Operation.IdLien == operation.IdLien);
            var double_ = trouve.Operation;
            if (double_ is not null && trouve.Mois != mois)
            {
                trouve.Mois.Operations.Remove(double_);
                mois.Operations.Add(double_);
                modifie = true;
            }
            if (double_ is null)
            {
                double_ = new Operation(operation.Libelle) { IdLien = operation.IdLien };
                mois.Operations.Add(double_);
                modifie = true;
            }

            if (double_.Libelle != operation.Libelle || double_.Debit != operation.Credit || double_.Credit != operation.Debit
                || !Meme(double_.CompteLie, idSource))
            {
                double_.Libelle = operation.Libelle;
                double_.Debit = operation.Credit;
                double_.Credit = operation.Debit;
                double_.CompteLie = idSource;
                modifie = true;
            }
        }

        return new ResultatSynchronisation(modifie || crees.Count > 0, crees, ignores);
    }

    /// <summary>Charges liées de la source recopiées dans la configuration de la cible (montants inversés).</summary>
    private static bool SynchroniserCharges(CompteBancaire source, string idSource, CompteBancaire cible, string idCible)
    {
        var modifie = false;
        var charges = ChargesVers(source, idCible).ToList();
        var liens = charges.Select(c => c.Lien!).ToHashSet(StringComparer.Ordinal);
        var configuration = cible.Configuration;

        if (configuration.Charges.RemoveAll(c => c.Lien is not null && Meme(c.CompteLie, idSource) && !liens.Contains(c.Lien)) > 0)
            modifie = true;

        foreach (var charge in charges)
        {
            var index = configuration.Charges.FindIndex(c => c.Lien == charge.Lien);
            var existante = index >= 0 ? configuration.Charges[index] : null;
            // Le classement (compte cumulé, catégories) reste propre à chaque compte.
            var double_ = new ModeleCharge(charge.Nom, charge.Credit, charge.Debit, existante?.CompteCumul,
                existante?.Categorie ?? Categorie.NonClassee,
                charge.Frequence, charge.Depart, existante?.CategorieOperation)
            {
                CompteLie = idSource,
                Lien = charge.Lien,
            };
            if (existante is null)
            {
                configuration.Charges.Add(double_);
                modifie = true;
            }
            else if (existante != double_)
            {
                configuration.Charges[index] = double_;
                modifie = true;
            }
        }

        return modifie;
    }

    private static IEnumerable<ModeleCharge> ChargesVers(CompteBancaire compte, string cible) =>
        compte.Configuration.Charges.Where(c => c.Lien is not null && Meme(c.CompteLie, cible));

    private static IEnumerable<(PeriodeMois Periode, Operation Operation)> OperationsVers(CompteBancaire compte, string cible) =>
        compte.Mois.SelectMany(m => m.Operations
            .Where(o => o.IdLien is not null && Meme(o.CompteLie, cible))
            .Select(o => (m.Periode, o)));

    private static bool Meme(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
