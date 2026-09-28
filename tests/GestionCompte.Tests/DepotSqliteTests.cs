using GestionCompte.Core;
using GestionCompte.Core.Modeles;
using GestionCompte.Data;
using Microsoft.Data.Sqlite;

namespace GestionCompte.Tests;

public sealed class DepotSqliteTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"));

    private string Chemin(string nom = "compte.db") => Path.Combine(_dossier, nom);

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    private static CompteBancaire CompteAvecTroisMois()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026(objectifRembPascale: 1500m));
        compte.Configuration.SoldeInitial = -45.67m;

        var octobre = compte.CreerMoisSuivant();
        octobre.Operations.Add(new Operation("Leclerc", debit: 87.35m) { Enveloppe = "Courses", Pointee = true });
        octobre.Operations[0].Pointee = true;

        var novembre = compte.CreerMoisSuivant();
        novembre.Revenus.Single(r => r.Nom == "NDF").Montant = 123.45m;
        novembre.Enveloppes.Single(e => e.Nom == "Carburant").Budget = 150m;
        novembre.Operations.Single(o => o.CompteCumul == DonneesExcel.Epargne).Debit = 75m;
        novembre.Operations.Add(new Operation("Remboursement mutuelle", credit: 32.10m));

        compte.CreerMoisSuivant();
        compte.OperationsPrevues.Add(new OperationPrevue(new PeriodeMois(2027, 7), "Vacances", debit: 1200.50m));
        compte.OperationsPrevues.Add(new OperationPrevue(new PeriodeMois(2027, 3), "Prime", credit: 500m) { CompteCumul = DonneesExcel.Epargne });
        compte.Configuration.Charges[0] = compte.Configuration.Charges[0] with { Categorie = Categorie.Essentiel };
        compte.Configuration.Charges[1] = compte.Configuration.Charges[1] with { Categorie = Categorie.Confort };
        compte.Configuration.Enveloppes[1] = compte.Configuration.Enveloppes[1] with { Categorie = Categorie.Confort };
        compte.ObjectifsEpargne.Add(new ObjectifEpargne("Vacances", 1500.50m, new PeriodeMois(2027, 6), dejaEpargne: 200m));
        compte.Configuration.Regles.Add(new RegleClassement("BOULANGERIE", "Courses"));
        octobre.Operations[0].IdentifiantBanque = "FITID-123";
        octobre.Revenus[0].Recu = true;
        octobre.Revenus[0].IdentifiantBanque = "FITID-456";
        return compte;
    }

    [Fact]
    public void Charger_SansFichier_RenvoieNull()
    {
        var depot = new DepotSqlite(Chemin());

        Assert.False(depot.Existe);
        Assert.Null(depot.Charger());
    }

    [Fact]
    public void Enregistrer_CreeLeDossierEtLeFichier()
    {
        var depot = new DepotSqlite(Path.Combine(_dossier, "sous", "dossier", "compte.db"));

        depot.Enregistrer(CompteAvecTroisMois());

        Assert.True(depot.Existe);
    }

    [Fact]
    public void EnregistrerPuisCharger_RestitueToutesLesDonnees()
    {
        var original = CompteAvecTroisMois();
        var depot = new DepotSqlite(Chemin());

        depot.Enregistrer(original);
        var recharge = depot.Charger()!;

        VerifierIdentiques(original, recharge);
    }

    [Fact]
    public void EnregistrerPuisCharger_DonneLesMemesCalculs()
    {
        var original = CompteAvecTroisMois();
        var depot = new DepotSqlite(Chemin());

        depot.Enregistrer(original);
        var recharge = depot.Charger()!;

        foreach (var mois in original.Mois)
        {
            Assert.Equal(original.Calculer(mois.Periode), recharge.Calculer(mois.Periode), new ResultatComparateur());
            Assert.Equal(original.ComptesCumulJusqua(mois.Periode), recharge.ComptesCumulJusqua(mois.Periode));
        }
    }

    [Fact]
    public void Enregistrer_UneDeuxiemeFois_RemplaceLesDonnees()
    {
        var depot = new DepotSqlite(Chemin());
        depot.Enregistrer(CompteAvecTroisMois());

        var nouveau = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        nouveau.Configuration.Charges.RemoveAt(0);
        nouveau.CreerMoisSuivant();
        depot.Enregistrer(nouveau);
        var recharge = depot.Charger()!;

        VerifierIdentiques(nouveau, recharge);
        Assert.Single(recharge.Mois);
    }

    [Fact]
    public void Enregistrer_CompteSansMois_RechargeLaConfiguration()
    {
        var original = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        var depot = new DepotSqlite(Chemin());

        depot.Enregistrer(original);
        var recharge = depot.Charger()!;

        Assert.Empty(recharge.Mois);
        VerifierIdentiques(original, recharge);
    }

    [Fact]
    public void Montants_ConserventTousLesChiffresApresLaVirgule()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        var mois = compte.CreerMoisSuivant();
        mois.Operations.Add(new Operation("Précis", debit: 0.1m + 0.2m, credit: 1234567.891m));
        var depot = new DepotSqlite(Chemin());

        depot.Enregistrer(compte);
        var operation = depot.Charger()!.Mois[0].Operations[^1];

        Assert.Equal(0.3m, operation.Debit);
        Assert.Equal(1234567.891m, operation.Credit);
    }

    [Fact]
    public void Charger_FichierDUneVersionPlusRecente_EstRefuse()
    {
        var depot = new DepotSqlite(Chemin());
        depot.Enregistrer(CompteAvecTroisMois());
        using (var connexion = new SqliteConnection($"Data Source={depot.CheminFichier};Pooling=False"))
        {
            connexion.Open();
            using var commande = connexion.CreateCommand();
            commande.CommandText = $"PRAGMA user_version = {DepotSqlite.VersionSchema + 1}";
            commande.ExecuteNonQuery();
        }

        Assert.Throws<InvalidDataException>(() => depot.Charger());
        Assert.Throws<InvalidDataException>(() => depot.Enregistrer(CompteAvecTroisMois()));
    }

    [Fact]
    public void FichierAuFormat1_SansOperationsPrevues_EstLuPuisMisAJour()
    {
        var depot = new DepotSqlite(Chemin());
        depot.Enregistrer(CompteAvecTroisMois());
        using (var connexion = new SqliteConnection($"Data Source={depot.CheminFichier};Pooling=False"))
        {
            connexion.Open();
            using var commande = connexion.CreateCommand();
            commande.CommandText = "DROP TABLE operation_prevue; PRAGMA user_version = 1;";
            commande.ExecuteNonQuery();
        }

        var compte = depot.Charger()!;
        Assert.Equal(3, compte.Mois.Count);
        Assert.Empty(compte.OperationsPrevues);

        compte.OperationsPrevues.Add(new OperationPrevue(new PeriodeMois(2027, 1), "Soldes", debit: 80m));
        depot.Enregistrer(compte);
        Assert.Single(depot.Charger()!.OperationsPrevues);
    }

    [Fact]
    public void FichierAuFormat2_SansCategoriesNiObjectifs_EstLuPuisMisAJour()
    {
        var depot = new DepotSqlite(Chemin());
        depot.Enregistrer(CompteAvecTroisMois());
        using (var connexion = new SqliteConnection($"Data Source={depot.CheminFichier};Pooling=False"))
        {
            connexion.Open();
            using var commande = connexion.CreateCommand();
            commande.CommandText = "ALTER TABLE modele_charge DROP COLUMN categorie; ALTER TABLE modele_enveloppe DROP COLUMN categorie; " +
                                   "DROP TABLE objectif_epargne; PRAGMA user_version = 2;";
            commande.ExecuteNonQuery();
        }

        var compte = depot.Charger()!;
        Assert.All(compte.Configuration.Charges, c => Assert.Equal(Categorie.NonClassee, c.Categorie));
        Assert.All(compte.Configuration.Enveloppes, e => Assert.Equal(Categorie.Essentiel, e.Categorie));
        Assert.Empty(compte.ObjectifsEpargne);
        Assert.Equal(2, compte.OperationsPrevues.Count);

        compte.Configuration.Charges[0] = compte.Configuration.Charges[0] with { Categorie = Categorie.Essentiel };
        compte.ObjectifsEpargne.Add(new ObjectifEpargne("Voiture", 3000m, new PeriodeMois(2028, 1)));
        depot.Enregistrer(compte);
        var relu = depot.Charger()!;
        Assert.Equal(Categorie.Essentiel, relu.Configuration.Charges[0].Categorie);
        Assert.Single(relu.ObjectifsEpargne);
    }

    [Fact]
    public void FichierAuFormat3_SansImport_EstLuAvecLesReglesDeBase()
    {
        var depot = new DepotSqlite(Chemin());
        depot.Enregistrer(CompteAvecTroisMois());
        using (var connexion = new SqliteConnection($"Data Source={depot.CheminFichier};Pooling=False"))
        {
            connexion.Open();
            using var commande = connexion.CreateCommand();
            commande.CommandText = "ALTER TABLE operation DROP COLUMN identifiant_banque; ALTER TABLE mois_revenu DROP COLUMN recu; " +
                                   "ALTER TABLE mois_revenu DROP COLUMN identifiant_banque; DROP TABLE regle_classement; PRAGMA user_version = 3;";
            commande.ExecuteNonQuery();
        }

        var compte = depot.Charger()!;
        Assert.Equal(RegleClassement.ParDefaut, compte.Configuration.Regles);
        Assert.All(compte.Mois.SelectMany(m => m.Operations), o => Assert.Null(o.IdentifiantBanque));
        Assert.All(compte.Mois.SelectMany(m => m.Revenus), r => Assert.False(r.Recu));

        compte.Mois[0].Operations[0].IdentifiantBanque = "F1";
        depot.Enregistrer(compte);
        Assert.Equal("F1", depot.Charger()!.Mois[0].Operations[0].IdentifiantBanque);
    }

    [Fact]
    public void Sauvegarder_CreeUneCopieRechargeable()
    {
        var original = CompteAvecTroisMois();
        var depot = new DepotSqlite(Chemin());
        depot.Enregistrer(original);

        depot.Sauvegarder(Chemin("sauvegardes/compte-2026-10.db"));
        var copie = new DepotSqlite(Chemin("sauvegardes/compte-2026-10.db")).Charger()!;

        VerifierIdentiques(original, copie);
    }

    [Fact]
    public void Sauvegarder_SurLeFichierDeDonnees_EstRefuse()
    {
        var depot = new DepotSqlite(Chemin());
        depot.Enregistrer(CompteAvecTroisMois());

        Assert.Throws<ArgumentException>(() => depot.Sauvegarder(Chemin()));
        Assert.NotNull(depot.Charger());
    }

    [Fact]
    public void Sauvegarder_SansDonnees_EstRefuse()
    {
        Assert.Throws<FileNotFoundException>(() => new DepotSqlite(Chemin()).Sauvegarder(Chemin("copie.db")));
    }

    [Fact]
    public void CheminParDefaut_EstDansLesDocuments()
    {
        Assert.EndsWith(Path.Combine("GestionCompte", "compte.db"), DepotSqlite.CheminParDefaut);
    }

    private static void VerifierIdentiques(CompteBancaire attendu, CompteBancaire obtenu)
    {
        var a = attendu.Configuration;
        var o = obtenu.Configuration;

        Assert.Equal(a.PremierMois, o.PremierMois);
        Assert.Equal(a.SoldeInitial, o.SoldeInitial);
        Assert.Equal(a.Revenus, o.Revenus);
        Assert.Equal(a.Enveloppes, o.Enveloppes);
        Assert.Equal(a.Charges, o.Charges);
        Assert.Equal(a.ComptesCumul, o.ComptesCumul);
        Assert.Equal(a.Regles, o.Regles);

        Assert.Equal(
            attendu.OperationsPrevues.Select(x => (x.Periode, x.Libelle, x.Debit, x.Credit, x.CompteCumul)),
            obtenu.OperationsPrevues.Select(x => (x.Periode, x.Libelle, x.Debit, x.Credit, x.CompteCumul)));

        Assert.Equal(
            attendu.ObjectifsEpargne.Select(x => (x.Nom, x.Montant, x.Echeance, x.DejaEpargne)),
            obtenu.ObjectifsEpargne.Select(x => (x.Nom, x.Montant, x.Echeance, x.DejaEpargne)));

        Assert.Equal(attendu.Mois.Count, obtenu.Mois.Count);
        foreach (var (moisA, moisO) in attendu.Mois.Zip(obtenu.Mois))
        {
            Assert.Equal(moisA.Periode, moisO.Periode);
            Assert.Equal(moisA.Revenus.Select(r => (r.Nom, r.Montant, r.Recu, r.IdentifiantBanque)),
                         moisO.Revenus.Select(r => (r.Nom, r.Montant, r.Recu, r.IdentifiantBanque)));
            Assert.Equal(moisA.Enveloppes.Select(e => (e.Nom, e.Budget)), moisO.Enveloppes.Select(e => (e.Nom, e.Budget)));
            Assert.Equal(
                moisA.Operations.Select(x => (x.Libelle, x.Debit, x.Credit, x.Pointee, x.Enveloppe, x.CompteCumul, x.IdentifiantBanque)),
                moisO.Operations.Select(x => (x.Libelle, x.Debit, x.Credit, x.Pointee, x.Enveloppe, x.CompteCumul, x.IdentifiantBanque)));
        }
    }

    private sealed class ResultatComparateur : IEqualityComparer<Core.Calculs.ResultatMois>
    {
        public bool Equals(Core.Calculs.ResultatMois? x, Core.Calculs.ResultatMois? y) =>
            x is not null && y is not null
            && x.Periode == y.Periode
            && x.AncienSolde == y.AncienSolde
            && x.TotalRevenus == y.TotalRevenus
            && x.Lignes.SequenceEqual(y.Lignes)
            && x.Enveloppes.SequenceEqual(y.Enveloppes);

        public int GetHashCode(Core.Calculs.ResultatMois obj) => obj.Periode.GetHashCode();
    }
}
