using GestionCompte.Core;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Tests;

public sealed class CreditTests
{
    private static SimulationCredit Maison(decimal assurance = 0m) =>
        new("Maison", 200000m, 3.5m, 240, new PeriodeMois(2027, 1), assurance);

    [Fact]
    public void Mensualite_CoutEtTableau()
    {
        var resultat = Credit.Calculer(Maison(assurance: 0.30m));

        // 200 000 € à 3,5 % sur 20 ans : 1 159,92 € hors assurance ; assurance 0,30 % par an = 50 € par mois.
        Assert.Equal(1159.92m, resultat.MensualiteHorsAssurance);
        Assert.Equal(50m, resultat.AssuranceMensuelle);
        Assert.Equal(1209.92m, resultat.Mensualite);
        Assert.Equal(240, resultat.Tableau.Count);
        Assert.Equal(583.33m, resultat.Tableau[0].Interets);
        Assert.Equal(new PeriodeMois(2046, 12), resultat.Tableau[^1].Periode);
        Assert.Equal(0m, resultat.Tableau[^1].CapitalRestant);
        Assert.Equal(200000m, resultat.Tableau.Sum(l => l.Capital));
        Assert.Equal(12000m, resultat.CoutAssurance);
        Assert.InRange(resultat.CoutInterets, 78370m, 78390m);
    }

    [Fact]
    public void SansInterets_EtAssuranceParMois()
    {
        var resultat = Credit.Calculer(new SimulationCredit("Moto", 1200m, 0m, 12, new PeriodeMois(2027, 1), 5m, TypeAssurance.ParMois));

        Assert.Equal(100m, resultat.MensualiteHorsAssurance);
        Assert.Equal(105m, resultat.Mensualite);
        Assert.Equal(0m, resultat.CoutInterets);
        Assert.Equal(60m, resultat.CoutTotal);
    }

    [Fact]
    public void ConditionsInvalides_TableauVide()
    {
        Assert.Empty(Credit.Calculer(new SimulationCredit("x", 0m, 3m, 240, new PeriodeMois(2027, 1))).Tableau);
        Assert.Empty(Credit.Calculer(new SimulationCredit("x", 1000m, 3m, 0, new PeriodeMois(2027, 1))).Tableau);
    }

    [Fact]
    public void MontantEmpruntable_InverseDeLaMensualite()
    {
        var conditions = Maison(assurance: 0.30m);
        var montant = Credit.MontantEmpruntable(1209.92m, conditions);

        Assert.InRange(montant, 199990m, 200000m);
        Assert.Equal(0m, Credit.MontantEmpruntable(0m, conditions));
    }

    [Theory]
    [InlineData("Crédit Voiture", true)]
    [InlineData("Pret immo", true)]
    [InlineData("Emprunt moto", true)]
    [InlineData("Crédits", true)]
    [InlineData("Loyer", false)]
    [InlineData("Créditeur", false)]
    public void EstUnCredit(string nom, bool attendu) => Assert.Equal(attendu, Credit.EstUnCredit(nom));

    [Fact]
    public void Endettement_AvecLesCreditsExistants()
    {
        var configuration = new ConfigurationBudget { PremierMois = new PeriodeMois(2026, 10) };
        configuration.Revenus.Add(new ModeleRevenu("Salaire", 3000m));
        configuration.Charges.Add(new ModeleCharge("Crédit voiture", 300m));
        configuration.Charges.Add(new ModeleCharge("Loyer", 800m));

        var endettement = Credit.CalculerEndettement(configuration, 750m);

        Assert.Equal(300m, endettement.CreditsExistants);
        Assert.Equal(new[] { "Crédit voiture" }, endettement.NomsCredits);
        Assert.Equal(0.35m, endettement.Taux);
        Assert.False(endettement.Excessif);
        Assert.True(Credit.CalculerEndettement(configuration, 751m).Excessif);
    }

    [Fact]
    public void AjouterEtRetirerDuPrevisionnel()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.CreerMoisSuivant(); // octobre 2026
        compte.CreerMoisSuivant(); // novembre 2026
        var simulation = new SimulationCredit("Moto", 1200m, 0m, 4, new PeriodeMois(2026, 11));

        Assert.Equal(4, Credit.AjouterAuPrevisionnel(compte, simulation));
        Assert.True(Credit.EstAuPrevisionnel(compte, simulation));
        var novembre = compte.Trouver(new PeriodeMois(2026, 11))!;
        var echeance = Assert.Single(novembre.Operations, o => o.Libelle == "Crédit Moto");
        Assert.Equal(300m, echeance.Debit);
        Assert.Equal(3, compte.OperationsPrevues.Count(o => o.Libelle == "Crédit Moto"));

        // Ajouter de nouveau remplace les échéances au lieu de les doubler.
        Credit.AjouterAuPrevisionnel(compte, simulation);
        Assert.Equal(3, compte.OperationsPrevues.Count(o => o.Libelle == "Crédit Moto"));

        // Une échéance pointée reste dans le mois.
        novembre.Operations.Single(o => o.Libelle == "Crédit Moto").Pointee = true;
        Assert.Equal(3, Credit.RetirerDuPrevisionnel(compte, simulation));
        Assert.Single(novembre.Operations, o => o.Libelle == "Crédit Moto");
        Assert.Empty(compte.OperationsPrevues.Where(o => o.Libelle == "Crédit Moto"));
    }

    [Fact]
    public void ResteApres_NeCompteQuUneFoisLeCredit()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.CreerMoisSuivant();
        var simulation = new SimulationCredit("Moto", 2400m, 0m, 24, compte.ProchainMois);
        var avant = Credit.CapaciteSansOperations(compte, Credit.Libelle(simulation));

        Credit.AjouterAuPrevisionnel(compte, simulation);

        Assert.Equal(avant, Credit.CapaciteSansOperations(compte, Credit.Libelle(simulation)));
        Assert.True(AideBudget.CapaciteMensuelle(compte) < avant);
    }
}
