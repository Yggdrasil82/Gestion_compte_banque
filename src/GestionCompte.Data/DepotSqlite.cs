using System.Globalization;
using GestionCompte.Core;
using GestionCompte.Core.Modeles;
using Microsoft.Data.Sqlite;

namespace GestionCompte.Data;

/// <summary>
/// Enregistre et recharge le compte complet (configuration + mois) dans un fichier SQLite.
/// Chaque enregistrement remplace tout le contenu, dans une seule transaction :
/// en cas d'erreur, le fichier garde son état précédent.
/// </summary>
public sealed class DepotSqlite
{
    /// <summary>Version du format du fichier ; à incrémenter à chaque changement de schéma.</summary>
    /// <remarks>Version 2 : ajout des opérations prévues.</remarks>
    public const int VersionSchema = 2;

    public DepotSqlite(string cheminFichier)
    {
        if (string.IsNullOrWhiteSpace(cheminFichier))
            throw new ArgumentException("Chemin du fichier de données manquant.", nameof(cheminFichier));

        CheminFichier = Path.GetFullPath(cheminFichier);
    }

    public string CheminFichier { get; }

    /// <summary>Emplacement par défaut : Documents\GestionCompte\compte.db.</summary>
    public static string CheminParDefaut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GestionCompte", "compte.db");

    public bool Existe => File.Exists(CheminFichier);

    /// <summary>Recharge le compte, ou renvoie null si aucun compte n'a encore été enregistré.</summary>
    public CompteBancaire? Charger()
    {
        if (!Existe)
            return null;

        using var connexion = Ouvrir(SqliteOpenMode.ReadOnly);
        VerifierVersion(connexion);

        var parametres = LireParametres(connexion);
        if (parametres.Count == 0)
            return null;

        var configuration = new ConfigurationBudget
        {
            PremierMois = new PeriodeMois(int.Parse(parametres["premier_annee"], CultureInfo.InvariantCulture),
                                          int.Parse(parametres["premier_mois"], CultureInfo.InvariantCulture)),
            SoldeInitial = LireDecimal(parametres["solde_initial"]),
        };

        Lire(connexion, "SELECT nom, montant FROM modele_revenu ORDER BY ordre",
            l => configuration.Revenus.Add(new ModeleRevenu(l.GetString(0), LireDecimal(l.GetString(1)))));
        Lire(connexion, "SELECT nom, budget FROM modele_enveloppe ORDER BY ordre",
            l => configuration.Enveloppes.Add(new ModeleEnveloppe(l.GetString(0), LireDecimal(l.GetString(1)))));
        Lire(connexion, "SELECT nom, debit, credit, compte_cumul FROM modele_charge ORDER BY ordre",
            l => configuration.Charges.Add(new ModeleCharge(
                l.GetString(0), LireDecimal(l.GetString(1)), LireDecimal(l.GetString(2)), TexteOuNull(l, 3))));
        Lire(connexion, "SELECT nom, montant_initial, objectif FROM compte_cumul ORDER BY ordre",
            l => configuration.ComptesCumul.Add(new CompteCumul(
                l.GetString(0), LireDecimal(l.GetString(1)), l.IsDBNull(2) ? null : LireDecimal(l.GetString(2)))));

        var compte = new CompteBancaire(configuration);
        var moisParId = new Dictionary<long, MoisBudget>();

        Lire(connexion, "SELECT id, annee, mois FROM mois ORDER BY annee, mois", l =>
        {
            var mois = new MoisBudget(new PeriodeMois(l.GetInt32(1), l.GetInt32(2)));
            compte.AjouterMoisExistant(mois);
            moisParId.Add(l.GetInt64(0), mois);
        });

        Lire(connexion, "SELECT mois_id, nom, montant FROM mois_revenu ORDER BY mois_id, ordre",
            l => moisParId[l.GetInt64(0)].Revenus.Add(new LigneRevenu(l.GetString(1), LireDecimal(l.GetString(2)))));
        Lire(connexion, "SELECT mois_id, nom, budget FROM mois_enveloppe ORDER BY mois_id, ordre",
            l => moisParId[l.GetInt64(0)].Enveloppes.Add(new LigneEnveloppe(l.GetString(1), LireDecimal(l.GetString(2)))));
        Lire(connexion,
            "SELECT mois_id, libelle, debit, credit, pointee, enveloppe, compte_cumul FROM operation ORDER BY mois_id, ordre",
            l => moisParId[l.GetInt64(0)].Operations.Add(
                new Operation(l.GetString(1), LireDecimal(l.GetString(2)), LireDecimal(l.GetString(3)))
                {
                    Pointee = l.GetInt64(4) != 0,
                    Enveloppe = TexteOuNull(l, 5),
                    CompteCumul = TexteOuNull(l, 6),
                }));

        // Table absente des fichiers au format 1.
        if (TableExiste(connexion, "operation_prevue"))
        {
            Lire(connexion, "SELECT annee, mois, libelle, debit, credit, compte_cumul FROM operation_prevue ORDER BY ordre",
                l => compte.OperationsPrevues.Add(
                    new OperationPrevue(new PeriodeMois(l.GetInt32(0), l.GetInt32(1)), l.GetString(2),
                        LireDecimal(l.GetString(3)), LireDecimal(l.GetString(4)))
                    {
                        CompteCumul = TexteOuNull(l, 5),
                    }));
        }

        return compte;
    }

    /// <summary>Enregistre tout le compte (le dossier est créé si besoin).</summary>
    public void Enregistrer(CompteBancaire compte)
    {
        ArgumentNullException.ThrowIfNull(compte);

        Directory.CreateDirectory(Path.GetDirectoryName(CheminFichier)!);

        using var connexion = Ouvrir(SqliteOpenMode.ReadWriteCreate);
        VerifierVersion(connexion);
        CreerSchema(connexion);

        using var transaction = connexion.BeginTransaction();

        foreach (var table in new[] { "operation_prevue", "operation", "mois_enveloppe", "mois_revenu", "mois", "compte_cumul",
                                      "modele_charge", "modele_enveloppe", "modele_revenu", "parametres" })
            Executer(connexion, $"DELETE FROM {table}");

        var configuration = compte.Configuration;

        Executer(connexion, "INSERT INTO parametres (cle, valeur) VALUES ('premier_annee', $a), ('premier_mois', $m), ('solde_initial', $s)",
            ("$a", configuration.PremierMois.Annee.ToString(CultureInfo.InvariantCulture)),
            ("$m", configuration.PremierMois.Mois.ToString(CultureInfo.InvariantCulture)),
            ("$s", EcrireDecimal(configuration.SoldeInitial)));

        foreach (var (revenu, ordre) in configuration.Revenus.Select((r, i) => (r, i)))
            Executer(connexion, "INSERT INTO modele_revenu (ordre, nom, montant) VALUES ($o, $n, $m)",
                ("$o", ordre), ("$n", revenu.Nom), ("$m", EcrireDecimal(revenu.MontantParDefaut)));

        foreach (var (enveloppe, ordre) in configuration.Enveloppes.Select((e, i) => (e, i)))
            Executer(connexion, "INSERT INTO modele_enveloppe (ordre, nom, budget) VALUES ($o, $n, $b)",
                ("$o", ordre), ("$n", enveloppe.Nom), ("$b", EcrireDecimal(enveloppe.BudgetParDefaut)));

        foreach (var (charge, ordre) in configuration.Charges.Select((c, i) => (c, i)))
            Executer(connexion, "INSERT INTO modele_charge (ordre, nom, debit, credit, compte_cumul) VALUES ($o, $n, $d, $c, $cc)",
                ("$o", ordre), ("$n", charge.Nom), ("$d", EcrireDecimal(charge.Debit)), ("$c", EcrireDecimal(charge.Credit)),
                ("$cc", charge.CompteCumul));

        foreach (var (cumul, ordre) in configuration.ComptesCumul.Select((c, i) => (c, i)))
            Executer(connexion, "INSERT INTO compte_cumul (ordre, nom, montant_initial, objectif) VALUES ($o, $n, $m, $obj)",
                ("$o", ordre), ("$n", cumul.Nom), ("$m", EcrireDecimal(cumul.MontantInitial)),
                ("$obj", cumul.Objectif is { } objectif ? EcrireDecimal(objectif) : null));

        foreach (var mois in compte.Mois)
        {
            var moisId = (long)ExecuterScalaire(connexion,
                "INSERT INTO mois (annee, mois) VALUES ($a, $m) RETURNING id",
                ("$a", mois.Periode.Annee), ("$m", mois.Periode.Mois))!;

            foreach (var (revenu, ordre) in mois.Revenus.Select((r, i) => (r, i)))
                Executer(connexion, "INSERT INTO mois_revenu (mois_id, ordre, nom, montant) VALUES ($id, $o, $n, $m)",
                    ("$id", moisId), ("$o", ordre), ("$n", revenu.Nom), ("$m", EcrireDecimal(revenu.Montant)));

            foreach (var (enveloppe, ordre) in mois.Enveloppes.Select((e, i) => (e, i)))
                Executer(connexion, "INSERT INTO mois_enveloppe (mois_id, ordre, nom, budget) VALUES ($id, $o, $n, $b)",
                    ("$id", moisId), ("$o", ordre), ("$n", enveloppe.Nom), ("$b", EcrireDecimal(enveloppe.Budget)));

            foreach (var (operation, ordre) in mois.Operations.Select((o, i) => (o, i)))
                Executer(connexion,
                    "INSERT INTO operation (mois_id, ordre, libelle, debit, credit, pointee, enveloppe, compte_cumul) " +
                    "VALUES ($id, $o, $l, $d, $c, $p, $e, $cc)",
                    ("$id", moisId), ("$o", ordre), ("$l", operation.Libelle), ("$d", EcrireDecimal(operation.Debit)),
                    ("$c", EcrireDecimal(operation.Credit)), ("$p", operation.Pointee ? 1 : 0),
                    ("$e", operation.Enveloppe), ("$cc", operation.CompteCumul));
        }

        foreach (var (prevue, ordre) in compte.OperationsPrevues.Select((o, i) => (o, i)))
            Executer(connexion,
                "INSERT INTO operation_prevue (ordre, annee, mois, libelle, debit, credit, compte_cumul) VALUES ($o, $a, $m, $l, $d, $c, $cc)",
                ("$o", ordre), ("$a", prevue.Periode.Annee), ("$m", prevue.Periode.Mois), ("$l", prevue.Libelle),
                ("$d", EcrireDecimal(prevue.Debit)), ("$c", EcrireDecimal(prevue.Credit)), ("$cc", prevue.CompteCumul));

        transaction.Commit();
    }

    /// <summary>Copie cohérente du fichier de données vers <paramref name="destination"/> (écrasée si elle existe).</summary>
    public void Sauvegarder(string destination)
    {
        if (!Existe)
            throw new FileNotFoundException("Aucune donnée à sauvegarder.", CheminFichier);

        var cheminDestination = Path.GetFullPath(destination);
        if (string.Equals(cheminDestination, CheminFichier, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("La sauvegarde doit être un autre fichier que les données.", nameof(destination));

        Directory.CreateDirectory(Path.GetDirectoryName(cheminDestination)!);
        File.Delete(cheminDestination);

        using var source = Ouvrir(SqliteOpenMode.ReadOnly);
        using var cible = new SqliteConnection(ChaineConnexion(cheminDestination, SqliteOpenMode.ReadWriteCreate));
        cible.Open();
        source.BackupDatabase(cible);
    }

    private SqliteConnection Ouvrir(SqliteOpenMode mode)
    {
        var connexion = new SqliteConnection(ChaineConnexion(CheminFichier, mode));
        connexion.Open();
        return connexion;
    }

    // Pooling désactivé : le fichier est libéré dès la fermeture (copie, sauvegarde, suppression).
    private static string ChaineConnexion(string chemin, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder { DataSource = chemin, Mode = mode, Pooling = false }.ToString();

    private void VerifierVersion(SqliteConnection connexion)
    {
        var version = Convert.ToInt32(ExecuterScalaire(connexion, "PRAGMA user_version"), CultureInfo.InvariantCulture);
        if (version > VersionSchema)
            throw new InvalidDataException(
                $"Le fichier {CheminFichier} a été créé par une version plus récente de l'application (format {version}).");
    }

    private static void CreerSchema(SqliteConnection connexion)
    {
        // Les montants sont stockés en texte pour garder la valeur décimale exacte.
        Executer(connexion, """
            CREATE TABLE IF NOT EXISTS parametres (cle TEXT PRIMARY KEY, valeur TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS modele_revenu (ordre INTEGER NOT NULL, nom TEXT NOT NULL, montant TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS modele_enveloppe (ordre INTEGER NOT NULL, nom TEXT NOT NULL, budget TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS modele_charge (
                ordre INTEGER NOT NULL, nom TEXT NOT NULL, debit TEXT NOT NULL, credit TEXT NOT NULL, compte_cumul TEXT);
            CREATE TABLE IF NOT EXISTS compte_cumul (
                ordre INTEGER NOT NULL, nom TEXT NOT NULL, montant_initial TEXT NOT NULL, objectif TEXT);
            CREATE TABLE IF NOT EXISTS mois (
                id INTEGER PRIMARY KEY, annee INTEGER NOT NULL, mois INTEGER NOT NULL, UNIQUE (annee, mois));
            CREATE TABLE IF NOT EXISTS mois_revenu (
                mois_id INTEGER NOT NULL REFERENCES mois(id), ordre INTEGER NOT NULL, nom TEXT NOT NULL, montant TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS mois_enveloppe (
                mois_id INTEGER NOT NULL REFERENCES mois(id), ordre INTEGER NOT NULL, nom TEXT NOT NULL, budget TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS operation (
                mois_id INTEGER NOT NULL REFERENCES mois(id), ordre INTEGER NOT NULL, libelle TEXT NOT NULL,
                debit TEXT NOT NULL, credit TEXT NOT NULL, pointee INTEGER NOT NULL, enveloppe TEXT, compte_cumul TEXT);
            CREATE TABLE IF NOT EXISTS operation_prevue (
                ordre INTEGER NOT NULL, annee INTEGER NOT NULL, mois INTEGER NOT NULL, libelle TEXT NOT NULL,
                debit TEXT NOT NULL, credit TEXT NOT NULL, compte_cumul TEXT);
            """);
        Executer(connexion, $"PRAGMA user_version = {VersionSchema}");
    }

    private static Dictionary<string, string> LireParametres(SqliteConnection connexion)
    {
        var parametres = new Dictionary<string, string>();
        if (!TableExiste(connexion, "parametres"))
            return parametres;

        Lire(connexion, "SELECT cle, valeur FROM parametres", l => parametres[l.GetString(0)] = l.GetString(1));
        return parametres;
    }

    private static bool TableExiste(SqliteConnection connexion, string table) =>
        ExecuterScalaire(connexion, "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $t", ("$t", table)) is not null;

    private static void Lire(SqliteConnection connexion, string sql, Action<SqliteDataReader> action)
    {
        using var commande = connexion.CreateCommand();
        commande.CommandText = sql;
        using var lecteur = commande.ExecuteReader();
        while (lecteur.Read())
            action(lecteur);
    }

    private static void Executer(SqliteConnection connexion, string sql, params (string Nom, object? Valeur)[] parametres)
    {
        using var commande = Commande(connexion, sql, parametres);
        commande.ExecuteNonQuery();
    }

    private static object? ExecuterScalaire(SqliteConnection connexion, string sql, params (string Nom, object? Valeur)[] parametres)
    {
        using var commande = Commande(connexion, sql, parametres);
        return commande.ExecuteScalar();
    }

    private static SqliteCommand Commande(SqliteConnection connexion, string sql, (string Nom, object? Valeur)[] parametres)
    {
        var commande = connexion.CreateCommand();
        commande.CommandText = sql;
        foreach (var (nom, valeur) in parametres)
            commande.Parameters.AddWithValue(nom, valeur ?? DBNull.Value);
        return commande;
    }

    private static string EcrireDecimal(decimal valeur) => valeur.ToString(CultureInfo.InvariantCulture);

    private static decimal LireDecimal(string texte) => decimal.Parse(texte, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static string? TexteOuNull(SqliteDataReader lecteur, int colonne) =>
        lecteur.IsDBNull(colonne) ? null : lecteur.GetString(colonne);
}
