using GestionCompte.Core;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;
using GestionCompte.Data;
using GestionCompte.Presentation;
using Microsoft.Data.Sqlite;

namespace GestionCompte.Tests;

/// <summary>Charges qui ne sont pas prélevées tous les mois (poubelles tous les 2 mois…).</summary>
public sealed class FrequenceChargesTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    private static readonly PeriodeMois Novembre = new(2026, 11);

    /// <summary>Configuration d'octobre 2026, poubelles tous les 2 mois à partir de novembre.</summary>
    private static CompteBancaire CompteAvecPoubelleBimestrielle()
    {
        var configuration = DonneesExcel.ConfigurationOctobre2026();
        var index = configuration.Charges.FindIndex(c => c.Nom == "Poubelle");
        configuration.Charges[index] = configuration.Charges[index] with { Frequence = 2, Depart = Novembre };
        return new CompteBancaire(configuration);
    }

    private static bool APoubelle(MoisBudget mois) => mois.Operations.Any(o => o.Libelle == "Poubelle");

    [Theory]
    [InlineData(2026, 9, true)]
    [InlineData(2026, 10, false)]
    [InlineData(2026, 11, true)]
    [InlineData(2026, 12, false)]
    [InlineData(2027, 1, true)]
    [InlineData(2027, 11, true)]
    public void TombeEn_TousLesDeuxMois(int annee, int mois, bool attendu)
    {
        var charge = new ModeleCharge("Poubelle", 61.18m, Frequence: 2, Depart: Novembre);
        Assert.Equal(attendu, charge.TombeEn(new PeriodeMois(annee, mois)));
    }

    [Fact]
    public void TombeEn_ChargeMensuelleOuSansRepere_TousLesMois()
    {
        Assert.True(new ModeleCharge("Loyer", 801m).TombeEn(new PeriodeMois(2026, 10)));
        Assert.True(new ModeleCharge("X", 10m, Frequence: 3).TombeEn(new PeriodeMois(2026, 10)));
    }

    [Fact]
    public void TombeEn_UneFoisParAn()
    {
        var charge = new ModeleCharge("Assurance", 300m, Frequence: 12, Depart: new PeriodeMois(2027, 3));
        Assert.True(charge.TombeEn(new PeriodeMois(2026, 3)));
        Assert.True(charge.TombeEn(new PeriodeMois(2028, 3)));
        Assert.False(charge.TombeEn(new PeriodeMois(2027, 4)));
    }

    [Fact]
    public void NetMensuel_EquivalentParMois()
    {
        Assert.Equal(30.59m, new ModeleCharge("Poubelle", 61.18m, Frequence: 2, Depart: Novembre).NetMensuel);
        Assert.Equal(61.18m, new ModeleCharge("Poubelle", 61.18m).NetMensuel);
    }

    [Fact]
    public void CreerMoisSuivant_AjouteLaChargeUnMoisSurDeux()
    {
        var compte = CompteAvecPoubelleBimestrielle();

        var octobre = compte.CreerMoisSuivant();
        var novembre = compte.CreerMoisSuivant();
        var decembre = compte.CreerMoisSuivant();

        Assert.False(APoubelle(octobre));
        Assert.True(APoubelle(novembre));
        Assert.False(APoubelle(decembre));
    }

    [Fact]
    public void AppliquerConfiguration_NAjoutePasLaChargeHorsDeSesMois_EtNeSupprimeRien()
    {
        var compte = CompteAvecPoubelleBimestrielle();
        var octobre = compte.CreerMoisSuivant();
        var novembre = compte.CreerMoisSuivant();

        compte.AppliquerConfiguration(octobre);
        Assert.False(APoubelle(octobre));

        novembre.Operations.RemoveAll(o => o.Libelle == "Poubelle");
        compte.AppliquerConfiguration(novembre);
        Assert.True(APoubelle(novembre));

        // Ligne ajoutée à la main un mois « sans » : conservée, montant mis à jour.
        octobre.Operations.Add(new Operation("Poubelle", 50m));
        compte.AppliquerConfiguration(octobre);
        Assert.Equal(61.18m, octobre.Operations.Single(o => o.Libelle == "Poubelle").Debit);
    }

    [Fact]
    public void Previsionnel_ChargeUnMoisSurDeux()
    {
        var compte = CompteAvecPoubelleBimestrielle();
        var mensuel = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());

        var bimestriel = Previsionnel.Calculer(compte, 0, 4).Mois;
        var reference = Previsionnel.Calculer(mensuel, 0, 4).Mois;

        // Octobre et décembre : 61,18 € de moins à payer chaque fois.
        Assert.Equal(reference[0].SoldeFin + 61.18m, bimestriel[0].SoldeFin);
        Assert.Equal(reference[2].SoldeFin + 2 * 61.18m, bimestriel[2].SoldeFin);
    }

    [Fact]
    public void AideBudget_CoutAnnuelEtRepartitionSurLEquivalentMensuel()
    {
        var compte = CompteAvecPoubelleBimestrielle();

        var poubelle = AideBudget.AnalyserCharges(compte.Configuration).Charges.Single(c => c.Nom == "Poubelle");
        Assert.Equal(30.59m, poubelle.Mensuel);
        Assert.Equal(367.08m, poubelle.Annuel);

        var mensuel = AideBudget.Repartir(DonneesExcel.ConfigurationOctobre2026());
        var bimestriel = AideBudget.Repartir(compte.Configuration);
        Assert.Equal(mensuel.ResteDisponible + 30.59m, bimestriel.ResteDisponible);
    }

    [Fact]
    public void Depot_EnregistreEtRelitLaFrequence()
    {
        var depot = new DepotSqlite(Path.Combine(_dossier, "compte.db"));
        depot.Enregistrer(CompteAvecPoubelleBimestrielle());

        var charges = depot.Charger()!.Configuration.Charges;

        var poubelle = charges.Single(c => c.Nom == "Poubelle");
        Assert.Equal((2, (PeriodeMois?)Novembre), (poubelle.Frequence, poubelle.Depart));
        Assert.All(charges.Where(c => c.Nom != "Poubelle"), c => Assert.Equal((1, (PeriodeMois?)null), (c.Frequence, c.Depart)));
    }

    [Fact]
    public void Depot_FichierAuFormat5_ChargesMensuelles()
    {
        var depot = new DepotSqlite(Path.Combine(_dossier, "compte.db"));
        depot.Enregistrer(CompteAvecPoubelleBimestrielle());
        using (var connexion = new SqliteConnection($"Data Source={depot.CheminFichier};Pooling=False"))
        {
            connexion.Open();
            using var commande = connexion.CreateCommand();
            commande.CommandText = "ALTER TABLE modele_charge DROP COLUMN frequence; ALTER TABLE modele_charge DROP COLUMN depart_annee; " +
                                   "ALTER TABLE modele_charge DROP COLUMN depart_mois; PRAGMA user_version = 5;";
            commande.ExecuteNonQuery();
        }

        var compte = depot.Charger()!;
        Assert.All(compte.Configuration.Charges, c => Assert.Equal(1, c.Frequence));

        depot.Enregistrer(compte);
        Assert.Equal(DepotSqlite.VersionSchema, LireVersion(depot.CheminFichier));
    }

    private static long LireVersion(string chemin)
    {
        using var connexion = new SqliteConnection($"Data Source={chemin};Pooling=False");
        connexion.Open();
        using var commande = connexion.CreateCommand();
        commande.CommandText = "PRAGMA user_version";
        return (long)commande.ExecuteScalar()!;
    }

    [Fact]
    public void Configuration_ChoisirUneFrequence_ProposeLeMoisDuJour()
    {
        var configuration = DonneesExcel.ConfigurationOctobre2026();
        var modifications = 0;
        var vm = new ConfigurationViewModel(configuration, premierMoisModifiable: false, () => modifications++, new PeriodeMois(2026, 9));
        var poubelle = vm.Charges.Elements.Single(c => c.Nom == "Poubelle");

        Assert.False(poubelle.DepartModifiable);
        Assert.Equal(24, vm.MoisDepart.Count);
        Assert.Equal(new PeriodeMois(2026, 9), vm.MoisDepart[0]);

        poubelle.Frequence = ConfigurationViewModel.Frequences.Single(f => f.Mois == 2);
        Assert.True(poubelle.DepartModifiable);
        Assert.Equal(new PeriodeMois(2026, 9), poubelle.Depart);

        poubelle.Depart = Novembre;
        var modele = configuration.Charges.Single(c => c.Nom == "Poubelle");
        Assert.Equal((2, (PeriodeMois?)Novembre), (modele.Frequence, modele.Depart));
        Assert.True(modifications >= 2);
    }
}
