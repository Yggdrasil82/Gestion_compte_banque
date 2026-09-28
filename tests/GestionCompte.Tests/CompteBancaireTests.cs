using GestionCompte.Core;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Tests;

public class CompteBancaireTests
{
    [Fact]
    public void PremierMois_UtiliseLeSoldeInitialCommeAncienSolde()
    {
        var configuration = DonneesExcel.ConfigurationOctobre2026();
        configuration.SoldeInitial = 1000m;
        var compte = new CompteBancaire(configuration);
        var octobre = compte.CreerMoisSuivant();

        var resultat = compte.Calculer(octobre.Periode);

        Assert.Equal(1000m, resultat.AncienSolde);
        Assert.Equal(1622.18m, resultat.SoldeFinPrevisionnel);
    }

    [Fact]
    public void MoisSuivant_ReprendLeSoldeDeFinDuMoisPrecedent()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.CreerMoisSuivant();
        var novembre = compte.CreerMoisSuivant();

        var resultat = compte.Calculer(novembre.Periode);

        Assert.Equal(new PeriodeMois(2026, 11), novembre.Periode);
        Assert.Equal(622.18m, resultat.AncienSolde);
        Assert.Equal(1244.36m, resultat.SoldeFinPrevisionnel);
    }

    [Fact]
    public void ModifierUnMoisPasse_RecalculeLesMoisSuivants()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        var octobre = compte.CreerMoisSuivant();
        var novembre = compte.CreerMoisSuivant();

        octobre.Operations.Add(new Operation("Réparation", debit: 200m));

        Assert.Equal(422.18m, compte.Calculer(novembre.Periode).AncienSolde);
    }

    [Fact]
    public void MoisSuivant_ApresDecembre_PasseAJanvierDeLAnneeSuivante()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());

        var periodes = Enumerable.Range(0, 4).Select(_ => compte.CreerMoisSuivant().Periode).ToList();

        Assert.Equal(
            new[] { new PeriodeMois(2026, 10), new PeriodeMois(2026, 11), new PeriodeMois(2026, 12), new PeriodeMois(2027, 1) },
            periodes);
    }

    [Fact]
    public void NouveauMois_EstPrerempliAvecLaConfiguration()
    {
        var configuration = DonneesExcel.ConfigurationOctobre2026();
        var compte = new CompteBancaire(configuration);

        var mois = compte.CreerMoisSuivant();

        Assert.Equal(configuration.Revenus.Count, mois.Revenus.Count);
        Assert.Equal(configuration.Enveloppes.Count, mois.Enveloppes.Count);
        Assert.Equal(configuration.Charges.Select(c => c.Nom), mois.Operations.Select(o => o.Libelle));
        Assert.All(mois.Operations, o => Assert.False(o.Pointee));
    }

    [Fact]
    public void ModifierUnMois_NeModifiePasLaConfigurationNiLesAutresMois()
    {
        var configuration = DonneesExcel.ConfigurationOctobre2026();
        var compte = new CompteBancaire(configuration);
        var octobre = compte.CreerMoisSuivant();

        octobre.Revenus[0].Montant = 3000m;
        octobre.Operations[0].Debit = 900m;
        var novembre = compte.CreerMoisSuivant();

        Assert.Equal(2600m, configuration.Revenus[0].MontantParDefaut);
        Assert.Equal(2600m, novembre.Revenus[0].Montant);
        Assert.Equal(801m, novembre.Operations[0].Debit);
    }

    [Fact]
    public void AjouterMoisExistant_RefuseUnDoublonOuUnTrou()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.AjouterMoisExistant(new MoisBudget(new PeriodeMois(2026, 10)));

        Assert.Throws<InvalidOperationException>(() => compte.AjouterMoisExistant(new MoisBudget(new PeriodeMois(2026, 10))));
        Assert.Throws<InvalidOperationException>(() => compte.AjouterMoisExistant(new MoisBudget(new PeriodeMois(2026, 12))));
    }

    [Fact]
    public void Calculer_MoisInexistant_LeveUneErreur()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.CreerMoisSuivant();

        Assert.Throws<KeyNotFoundException>(() => compte.Calculer(new PeriodeMois(2027, 5)));
    }

    [Fact]
    public void ComptesCumul_Octobre2026_IdentiquesAExcel()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        var octobre = compte.CreerMoisSuivant();

        var cumuls = compte.ComptesCumulJusqua(octobre.Periode).ToDictionary(c => c.Nom);

        Assert.Equal(0m, cumuls[DonneesExcel.Epargne].Total);
        Assert.Equal(100m, cumuls[DonneesExcel.RembPascale].Total);
    }

    [Fact]
    public void ComptesCumul_SAdditionnentDeMoisEnMoisJusquAuMoisDemande()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        var octobre = compte.CreerMoisSuivant();
        var novembre = compte.CreerMoisSuivant();
        compte.CreerMoisSuivant();
        novembre.Operations.Single(o => o.CompteCumul == DonneesExcel.Epargne).Debit = 50m;

        var enOctobre = compte.ComptesCumulJusqua(octobre.Periode).ToDictionary(c => c.Nom);
        var enNovembre = compte.ComptesCumulJusqua(novembre.Periode).ToDictionary(c => c.Nom);

        Assert.Equal(0m, enOctobre[DonneesExcel.Epargne].Total);
        Assert.Equal(50m, enNovembre[DonneesExcel.Epargne].Total);
        Assert.Equal(200m, enNovembre[DonneesExcel.RembPascale].Total);
    }

    [Fact]
    public void CompteCumulAvecObjectif_CalculeLeResteARembourser()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026(objectifRembPascale: 250m));
        compte.CreerMoisSuivant();
        compte.CreerMoisSuivant();
        var decembre = compte.CreerMoisSuivant();

        var remb = compte.ComptesCumulJusqua(decembre.Periode).Single(c => c.Nom == DonneesExcel.RembPascale);

        Assert.Equal(300m, remb.Total);
        Assert.Equal(0m, remb.Reste);
    }

    [Fact]
    public void CompteCumulSansObjectif_NaPasDeReste()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        var octobre = compte.CreerMoisSuivant();

        var epargne = compte.ComptesCumulJusqua(octobre.Periode).Single(c => c.Nom == DonneesExcel.Epargne);

        Assert.Null(epargne.Reste);
    }
}
