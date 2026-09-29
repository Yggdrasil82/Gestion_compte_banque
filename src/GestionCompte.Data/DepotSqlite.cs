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
    /// <remarks>
    /// Version 2 : opérations prévues. Version 3 : catégories 50/30/20 et objectifs d'épargne.
    /// Version 4 : import des relevés (identifiant bancaire, revenus reçus, règles de classement).
    /// Version 5 : même schéma ; marque la correction des revenus « reçus » des mois pas encore commencés.
    /// Version 6 : fréquence des charges (tous les 2 mois, trimestrielle…).
    /// Version 7 : objectifs d'épargne alimentés par un compte cumulé.
    /// Version 8 : simulations de crédit.
    /// </remarks>
    public const int VersionSchema = 8;

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
    /// <param name="moisEnCours">Mois du jour (par défaut, celui de l'horloge) : sert à mettre à jour les anciens fichiers.</param>
    public CompteBancaire? Charger(PeriodeMois? moisEnCours = null)
    {
        if (!Existe)
            return null;

        using var connexion = Ouvrir(SqliteOpenMode.ReadOnly);
        var version = VerifierVersion(connexion);
        var aujourdHui = moisEnCours ?? new PeriodeMois(DateTime.Today.Year, DateTime.Today.Month);

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
        // Colonne « categorie » absente des fichiers aux formats 1 et 2.
        var categorieEnveloppe = ColonneExiste(connexion, "modele_enveloppe", "categorie") ? "categorie" : "NULL";
        Lire(connexion, $"SELECT nom, budget, {categorieEnveloppe} FROM modele_enveloppe ORDER BY ordre",
            l => configuration.Enveloppes.Add(new ModeleEnveloppe(
                l.GetString(0), LireDecimal(l.GetString(1)), LireCategorie(l, 2, Categorie.Essentiel))));
        var categorieCharge = ColonneExiste(connexion, "modele_charge", "categorie") ? "categorie" : "NULL";
        // Colonnes absentes des fichiers aux formats 1 à 5 : charge mensuelle.
        var frequence = ColonneExiste(connexion, "modele_charge", "frequence")
            ? "frequence, depart_annee, depart_mois" : "1, NULL, NULL";
        Lire(connexion, $"SELECT nom, debit, credit, compte_cumul, {categorieCharge}, {frequence} FROM modele_charge ORDER BY ordre",
            l => configuration.Charges.Add(new ModeleCharge(
                l.GetString(0), LireDecimal(l.GetString(1)), LireDecimal(l.GetString(2)), TexteOuNull(l, 3),
                LireCategorie(l, 4, Categorie.NonClassee),
                Math.Max(1, l.GetInt32(5)),
                l.IsDBNull(6) || l.IsDBNull(7) ? null : new PeriodeMois(l.GetInt32(6), l.GetInt32(7)))));
        Lire(connexion, "SELECT nom, montant_initial, objectif FROM compte_cumul ORDER BY ordre",
            l => configuration.ComptesCumul.Add(new CompteCumul(
                l.GetString(0), LireDecimal(l.GetString(1)), l.IsDBNull(2) ? null : LireDecimal(l.GetString(2)))));

        var compte = new CompteBancaire(configuration) { IdentifiantBanque = parametres.GetValueOrDefault("identifiant_banque") };
        var moisParId = new Dictionary<long, MoisBudget>();

        Lire(connexion, "SELECT id, annee, mois FROM mois ORDER BY annee, mois", l =>
        {
            var mois = new MoisBudget(new PeriodeMois(l.GetInt32(1), l.GetInt32(2)));
            compte.AjouterMoisExistant(mois);
            moisParId.Add(l.GetInt64(0), mois);
        });

        // Colonnes absentes des fichiers aux formats 1 à 3.
        var format4 = ColonneExiste(connexion, "mois_revenu", "recu");
        Lire(connexion,
            format4
                ? "SELECT mois_id, nom, montant, recu, identifiant_banque FROM mois_revenu ORDER BY mois_id, ordre"
                : "SELECT mois_id, nom, montant, 0, NULL FROM mois_revenu ORDER BY mois_id, ordre",
            l => moisParId[l.GetInt64(0)].Revenus.Add(new LigneRevenu(l.GetString(1), LireDecimal(l.GetString(2)))
            {
                Recu = l.GetInt64(3) != 0,
                IdentifiantBanque = TexteOuNull(l, 4),
            }));
        // Avant le format 4, les revenus n'étaient pas cochés « reçu » : ceux des mois terminés sont considérés reçus
        // (sinon le solde pointé serait faux au premier import) ; ceux du mois en cours le seront à l'import.
        if (!format4)
            foreach (var revenu in compte.Mois.Where(m => m.Periode < aujourdHui).SelectMany(m => m.Revenus))
                revenu.Recu = true;
        // Le format 4 cochait aussi les revenus des mois pas encore commencés : ils sont décochés
        // (sauf s'ils viennent d'un relevé importé).
        else if (version < 5)
            foreach (var revenu in compte.Mois.Where(m => m.Periode > aujourdHui).SelectMany(m => m.Revenus))
                if (revenu.IdentifiantBanque is null)
                    revenu.Recu = false;
        Lire(connexion, "SELECT mois_id, nom, budget FROM mois_enveloppe ORDER BY mois_id, ordre",
            l => moisParId[l.GetInt64(0)].Enveloppes.Add(new LigneEnveloppe(l.GetString(1), LireDecimal(l.GetString(2)))));
        Lire(connexion,
            "SELECT mois_id, libelle, debit, credit, pointee, enveloppe, compte_cumul, " +
            (format4 ? "identifiant_banque" : "NULL") + " FROM operation ORDER BY mois_id, ordre",
            l => moisParId[l.GetInt64(0)].Operations.Add(
                new Operation(l.GetString(1), LireDecimal(l.GetString(2)), LireDecimal(l.GetString(3)))
                {
                    Pointee = l.GetInt64(4) != 0,
                    Enveloppe = TexteOuNull(l, 5),
                    CompteCumul = TexteOuNull(l, 6),
                    IdentifiantBanque = TexteOuNull(l, 7),
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

        // Règles de classement : absentes avant le format 4, les règles de base sont alors proposées.
        if (TableExiste(connexion, "regle_classement"))
            Lire(connexion, "SELECT mot_cle, enveloppe FROM regle_classement ORDER BY ordre",
                l => configuration.Regles.Add(new RegleClassement(l.GetString(0), l.GetString(1))));
        else
            configuration.Regles.AddRange(RegleClassement.ParDefaut);

        // Table absente des fichiers aux formats 1 et 2.
        if (TableExiste(connexion, "objectif_epargne"))
        {
            var compteCumul = ColonneExiste(connexion, "objectif_epargne", "compte_cumul") ? "compte_cumul" : "NULL";
            Lire(connexion, $"SELECT nom, montant, annee, mois, deja_epargne, {compteCumul} FROM objectif_epargne ORDER BY ordre",
                l => compte.ObjectifsEpargne.Add(new ObjectifEpargne(
                    l.GetString(0), LireDecimal(l.GetString(1)), new PeriodeMois(l.GetInt32(2), l.GetInt32(3)), LireDecimal(l.GetString(4)))
                {
                    CompteCumul = l.IsDBNull(5) ? null : l.GetString(5),
                }));
        }

        // Table absente des fichiers avant le format 8.
        if (TableExiste(connexion, "simulation_credit"))
        {
            Lire(connexion,
                "SELECT nom, montant, taux, duree_mois, annee, mois, assurance, type_assurance, type_credit FROM simulation_credit ORDER BY ordre",
                l => compte.SimulationsCredit.Add(new SimulationCredit(
                    l.GetString(0), LireDecimal(l.GetString(1)), LireDecimal(l.GetString(2)), l.GetInt32(3),
                    new PeriodeMois(l.GetInt32(4), l.GetInt32(5)), LireDecimal(l.GetString(6)), (TypeAssurance)l.GetInt32(7))
                {
                    Type = (TypeCredit)l.GetInt32(8),
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

        foreach (var table in new[] { "regle_classement", "objectif_epargne", "simulation_credit", "operation_prevue", "operation", "mois_enveloppe", "mois_revenu", "mois", "compte_cumul",
                                      "modele_charge", "modele_enveloppe", "modele_revenu", "parametres" })
            Executer(connexion, $"DELETE FROM {table}");

        var configuration = compte.Configuration;

        Executer(connexion, "INSERT INTO parametres (cle, valeur) VALUES ('premier_annee', $a), ('premier_mois', $m), ('solde_initial', $s)",
            ("$a", configuration.PremierMois.Annee.ToString(CultureInfo.InvariantCulture)),
            ("$m", configuration.PremierMois.Mois.ToString(CultureInfo.InvariantCulture)),
            ("$s", EcrireDecimal(configuration.SoldeInitial)));
        if (compte.IdentifiantBanque is not null)
            Executer(connexion, "INSERT INTO parametres (cle, valeur) VALUES ('identifiant_banque', $id)", ("$id", compte.IdentifiantBanque));

        foreach (var (revenu, ordre) in configuration.Revenus.Select((r, i) => (r, i)))
            Executer(connexion, "INSERT INTO modele_revenu (ordre, nom, montant) VALUES ($o, $n, $m)",
                ("$o", ordre), ("$n", revenu.Nom), ("$m", EcrireDecimal(revenu.MontantParDefaut)));

        foreach (var (enveloppe, ordre) in configuration.Enveloppes.Select((e, i) => (e, i)))
            Executer(connexion, "INSERT INTO modele_enveloppe (ordre, nom, budget, categorie) VALUES ($o, $n, $b, $cat)",
                ("$o", ordre), ("$n", enveloppe.Nom), ("$b", EcrireDecimal(enveloppe.BudgetParDefaut)), ("$cat", enveloppe.Categorie.ToString()));

        foreach (var (charge, ordre) in configuration.Charges.Select((c, i) => (c, i)))
            Executer(connexion,
                "INSERT INTO modele_charge (ordre, nom, debit, credit, compte_cumul, categorie, frequence, depart_annee, depart_mois) " +
                "VALUES ($o, $n, $d, $c, $cc, $cat, $f, $da, $dm)",
                ("$o", ordre), ("$n", charge.Nom), ("$d", EcrireDecimal(charge.Debit)), ("$c", EcrireDecimal(charge.Credit)),
                ("$cc", charge.CompteCumul), ("$cat", charge.Categorie.ToString()), ("$f", charge.Frequence),
                ("$da", charge.Depart?.Annee), ("$dm", charge.Depart?.Mois));

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
                Executer(connexion,
                    "INSERT INTO mois_revenu (mois_id, ordre, nom, montant, recu, identifiant_banque) VALUES ($id, $o, $n, $m, $r, $ib)",
                    ("$id", moisId), ("$o", ordre), ("$n", revenu.Nom), ("$m", EcrireDecimal(revenu.Montant)),
                    ("$r", revenu.Recu ? 1 : 0), ("$ib", revenu.IdentifiantBanque));

            foreach (var (enveloppe, ordre) in mois.Enveloppes.Select((e, i) => (e, i)))
                Executer(connexion, "INSERT INTO mois_enveloppe (mois_id, ordre, nom, budget) VALUES ($id, $o, $n, $b)",
                    ("$id", moisId), ("$o", ordre), ("$n", enveloppe.Nom), ("$b", EcrireDecimal(enveloppe.Budget)));

            foreach (var (operation, ordre) in mois.Operations.Select((o, i) => (o, i)))
                Executer(connexion,
                    "INSERT INTO operation (mois_id, ordre, libelle, debit, credit, pointee, enveloppe, compte_cumul, identifiant_banque) " +
                    "VALUES ($id, $o, $l, $d, $c, $p, $e, $cc, $ib)",
                    ("$id", moisId), ("$o", ordre), ("$l", operation.Libelle), ("$d", EcrireDecimal(operation.Debit)),
                    ("$c", EcrireDecimal(operation.Credit)), ("$p", operation.Pointee ? 1 : 0),
                    ("$e", operation.Enveloppe), ("$cc", operation.CompteCumul), ("$ib", operation.IdentifiantBanque));
        }

        foreach (var (prevue, ordre) in compte.OperationsPrevues.Select((o, i) => (o, i)))
            Executer(connexion,
                "INSERT INTO operation_prevue (ordre, annee, mois, libelle, debit, credit, compte_cumul) VALUES ($o, $a, $m, $l, $d, $c, $cc)",
                ("$o", ordre), ("$a", prevue.Periode.Annee), ("$m", prevue.Periode.Mois), ("$l", prevue.Libelle),
                ("$d", EcrireDecimal(prevue.Debit)), ("$c", EcrireDecimal(prevue.Credit)), ("$cc", prevue.CompteCumul));

        foreach (var (regle, ordre) in configuration.Regles.Select((r, i) => (r, i)))
            Executer(connexion, "INSERT INTO regle_classement (ordre, mot_cle, enveloppe) VALUES ($o, $m, $e)",
                ("$o", ordre), ("$m", regle.MotCle), ("$e", regle.Enveloppe));

        foreach (var (objectif, ordre) in compte.ObjectifsEpargne.Select((o, i) => (o, i)))
            Executer(connexion,
                "INSERT INTO objectif_epargne (ordre, nom, montant, annee, mois, deja_epargne, compte_cumul) VALUES ($o, $n, $m, $a, $mo, $d, $c)",
                ("$o", ordre), ("$n", objectif.Nom), ("$m", EcrireDecimal(objectif.Montant)),
                ("$a", objectif.Echeance.Annee), ("$mo", objectif.Echeance.Mois), ("$d", EcrireDecimal(objectif.DejaEpargne)),
                ("$c", objectif.CompteCumul));

        foreach (var (simulation, ordre) in compte.SimulationsCredit.Select((s, i) => (s, i)))
            Executer(connexion,
                "INSERT INTO simulation_credit (ordre, nom, montant, taux, duree_mois, annee, mois, assurance, type_assurance, type_credit) " +
                "VALUES ($o, $n, $m, $t, $d, $a, $mo, $as, $ta, $tc)",
                ("$o", ordre), ("$n", simulation.Nom), ("$m", EcrireDecimal(simulation.Montant)), ("$t", EcrireDecimal(simulation.TauxAnnuel)),
                ("$d", simulation.DureeMois), ("$a", simulation.PremiereEcheance.Annee), ("$mo", simulation.PremiereEcheance.Mois),
                ("$as", EcrireDecimal(simulation.Assurance)), ("$ta", (int)simulation.TypeAssurance),
                ("$tc", (int)simulation.Type));

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

    /// <summary>Copies de sécurité faites automatiquement à côté du fichier (avant une restauration, une réinitialisation…).</summary>
    public IReadOnlyList<string> CopiesDeSecurite()
    {
        var dossier = Path.GetDirectoryName(CheminFichier)!;
        return Directory.Exists(dossier)
            ? Directory.GetFiles(dossier, Path.GetFileName(CheminFichier) + ".avant-*.db")
            : Array.Empty<string>();
    }

    /// <summary>
    /// Supprime définitivement le fichier de données et ses copies de sécurité automatiques
    /// (un simple enregistrement pourrait laisser d'anciennes données dans l'espace libre du fichier).
    /// </summary>
    public void EffacerTout()
    {
        foreach (var copie in CopiesDeSecurite())
            File.Delete(copie);
        foreach (var suffixe in new[] { "", "-journal", "-wal", "-shm" })
            File.Delete(CheminFichier + suffixe);
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

    private int VerifierVersion(SqliteConnection connexion)
    {
        var version = Convert.ToInt32(ExecuterScalaire(connexion, "PRAGMA user_version"), CultureInfo.InvariantCulture);
        if (version > VersionSchema)
            throw new InvalidDataException(
                $"Le fichier {CheminFichier} a été créé par une version plus récente de l'application (format {version}).");
        return version;
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
            CREATE TABLE IF NOT EXISTS regle_classement (ordre INTEGER NOT NULL, mot_cle TEXT NOT NULL, enveloppe TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS objectif_epargne (
                ordre INTEGER NOT NULL, nom TEXT NOT NULL, montant TEXT NOT NULL,
                annee INTEGER NOT NULL, mois INTEGER NOT NULL, deja_epargne TEXT NOT NULL, compte_cumul TEXT);
            CREATE TABLE IF NOT EXISTS simulation_credit (
                ordre INTEGER NOT NULL, nom TEXT NOT NULL, montant TEXT NOT NULL, taux TEXT NOT NULL, duree_mois INTEGER NOT NULL,
                annee INTEGER NOT NULL, mois INTEGER NOT NULL, assurance TEXT NOT NULL, type_assurance INTEGER NOT NULL,
                type_credit INTEGER NOT NULL);
            """);

        // Mise à jour des fichiers aux formats 1 et 2.
        if (!ColonneExiste(connexion, "modele_charge", "categorie"))
            Executer(connexion, "ALTER TABLE modele_charge ADD COLUMN categorie TEXT");
        if (!ColonneExiste(connexion, "modele_charge", "frequence"))
        {
            Executer(connexion, "ALTER TABLE modele_charge ADD COLUMN frequence INTEGER NOT NULL DEFAULT 1");
            Executer(connexion, "ALTER TABLE modele_charge ADD COLUMN depart_annee INTEGER");
            Executer(connexion, "ALTER TABLE modele_charge ADD COLUMN depart_mois INTEGER");
        }
        if (!ColonneExiste(connexion, "modele_enveloppe", "categorie"))
            Executer(connexion, "ALTER TABLE modele_enveloppe ADD COLUMN categorie TEXT");
        if (!ColonneExiste(connexion, "operation", "identifiant_banque"))
            Executer(connexion, "ALTER TABLE operation ADD COLUMN identifiant_banque TEXT");
        if (!ColonneExiste(connexion, "mois_revenu", "recu"))
        {
            Executer(connexion, "ALTER TABLE mois_revenu ADD COLUMN recu INTEGER NOT NULL DEFAULT 0");
            Executer(connexion, "ALTER TABLE mois_revenu ADD COLUMN identifiant_banque TEXT");
        }
        if (!ColonneExiste(connexion, "objectif_epargne", "compte_cumul"))
            Executer(connexion, "ALTER TABLE objectif_epargne ADD COLUMN compte_cumul TEXT");
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

    private static bool ColonneExiste(SqliteConnection connexion, string table, string colonne) =>
        ExecuterScalaire(connexion, $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = $c", ("$c", colonne)) is not null;

    private static Categorie LireCategorie(SqliteDataReader lecteur, int colonne, Categorie parDefaut) =>
        !lecteur.IsDBNull(colonne) && Enum.TryParse<Categorie>(lecteur.GetString(colonne), out var categorie) ? categorie : parDefaut;

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
