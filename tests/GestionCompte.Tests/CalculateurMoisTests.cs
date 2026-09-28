using GestionCompte.Core;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Tests;

public class CalculateurMoisTests
{
    private static (CompteBancaire Compte, MoisBudget Octobre) Octobre2026()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        return (compte, compte.CreerMoisSuivant());
    }

    [Fact]
    public void Octobre2026_DonneLeMemeSoldePrevisionnelQueLeFichierExcel()
    {
        var (compte, octobre) = Octobre2026();

        var resultat = compte.Calculer(octobre.Periode);

        Assert.Equal(0m, resultat.AncienSolde);
        Assert.Equal(3200m, resultat.SoldeDepart);
        Assert.Equal(622.18m, resultat.SoldeFinPrevisionnel);
    }

    [Fact]
    public void Octobre2026_SoldesIntermediairesIdentiquesAExcel()
    {
        var (compte, octobre) = Octobre2026();

        var soldes = compte.Calculer(octobre.Periode).Lignes.ToDictionary(l => l.Libelle, l => l.Solde);

        Assert.Equal(3200m, soldes["CAF"]);
        Assert.Equal(2800m, soldes["Courses"]);
        Assert.Equal(2700m, soldes["Carburant"]);
        Assert.Equal(1899m, soldes["Loyer"]);
        Assert.Equal(1789.01m, soldes["Remb Pascale"]);
        Assert.Equal(938.36m, soldes["Cantine"]);
    }

    [Fact]
    public void Enveloppe_ReserveSeulementLeResteDuBudget()
    {
        var (compte, octobre) = Octobre2026();
        octobre.Operations.Add(new Operation("Leclerc", debit: 150m) { Enveloppe = "Courses" });
        octobre.Operations.Add(new Operation("Lidl", debit: 50m) { Enveloppe = "Courses" });

        var resultat = compte.Calculer(octobre.Periode);
        var courses = resultat.Enveloppes.Single(e => e.Nom == "Courses");

        Assert.Equal(200m, courses.Depense);
        Assert.Equal(200m, courses.Reste);
        Assert.False(courses.Depassee);
        // Dépenser dans le budget prévu ne change pas le solde prévisionnel.
        Assert.Equal(622.18m, resultat.SoldeFinPrevisionnel);
    }

    [Fact]
    public void Enveloppe_Depassee_ResteAZeroEtLeDepassementBaisseLeSolde()
    {
        var (compte, octobre) = Octobre2026();
        octobre.Operations.Add(new Operation("Plein", debit: 130m) { Enveloppe = "Carburant" });

        var resultat = compte.Calculer(octobre.Periode);
        var carburant = resultat.Enveloppes.Single(e => e.Nom == "Carburant");

        Assert.Equal(0m, carburant.Reste);
        Assert.True(carburant.Depassee);
        Assert.Equal(622.18m - 30m, resultat.SoldeFinPrevisionnel);
    }

    [Fact]
    public void Enveloppe_BudgetModifiePourLeMois_RemplaceLeBudgetParDefaut()
    {
        var (compte, octobre) = Octobre2026();
        octobre.Enveloppes.Single(e => e.Nom == "Courses").Budget = 500m;

        Assert.Equal(522.18m, compte.Calculer(octobre.Periode).SoldeFinPrevisionnel);
    }

    [Fact]
    public void Enveloppe_NomComparéSansTenirCompteDesMajusculesNiDesEspaces()
    {
        var (compte, octobre) = Octobre2026();
        octobre.Operations.Add(new Operation("Marché", debit: 40m) { Enveloppe = " courses " });

        var courses = compte.Calculer(octobre.Periode).Enveloppes.Single(e => e.Nom == "Courses");

        Assert.Equal(40m, courses.Depense);
    }

    [Fact]
    public void Revenu_ModifiePourLeMois_ChangeLeSolde()
    {
        var (compte, octobre) = Octobre2026();
        octobre.Revenus.Single(r => r.Nom == "NDF").Montant = 120.50m;

        Assert.Equal(742.68m, compte.Calculer(octobre.Periode).SoldeFinPrevisionnel);
    }

    [Fact]
    public void OperationPointee_NeChangePasLeCalcul()
    {
        var (compte, octobre) = Octobre2026();
        octobre.Operations.ForEach(o => o.Pointee = true);

        Assert.Equal(622.18m, compte.Calculer(octobre.Periode).SoldeFinPrevisionnel);
    }

    [Fact]
    public void MoisVide_SoldeFinEgalSoldeDepart()
    {
        var mois = new MoisBudget(new PeriodeMois(2026, 1));

        var resultat = CalculateurMois.Calculer(mois, 250m);

        Assert.Empty(resultat.Lignes);
        Assert.Equal(250m, resultat.SoldeFinPrevisionnel);
    }
}
