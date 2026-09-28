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
        Assert.Equal(100m, cumuls[DonneesExcel.RembFamille].Total);
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
        Assert.Equal(200m, enNovembre[DonneesExcel.RembFamille].Total);
    }

    [Fact]
    public void CompteCumulAvecObjectif_CalculeLeResteARembourser()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026(objectifRembFamille: 250m));
        compte.CreerMoisSuivant();
        compte.CreerMoisSuivant();
        var decembre = compte.CreerMoisSuivant();

        var remb = compte.ComptesCumulJusqua(decembre.Periode).Single(c => c.Nom == DonneesExcel.RembFamille);

        Assert.Equal(300m, remb.Total);
        Assert.Equal(0m, remb.Reste);
    }

    [Fact]
    public void SupprimerDernierMois_PermetDeLeRecreer()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.CreerMoisSuivant();
        compte.CreerMoisSuivant();

        Assert.True(compte.SupprimerDernierMois());
        Assert.Equal(new PeriodeMois(2026, 11), compte.CreerMoisSuivant().Periode);
    }

    [Fact]
    public void SupprimerDernierMois_SansMois_RenvoieFaux()
    {
        Assert.False(new CompteBancaire(DonneesExcel.ConfigurationOctobre2026()).SupprimerDernierMois());
    }

    [Fact]
    public void AppliquerConfiguration_MetAJourChargesEtBudgetsSansToucherAuReste()
    {
        var configuration = DonneesExcel.ConfigurationOctobre2026();
        var compte = new CompteBancaire(configuration);
        var octobre = compte.CreerMoisSuivant();
        octobre.Revenus[0].Montant = 3100m;
        octobre.Operations.Add(new Operation("Loyer", debit: 20m) { Enveloppe = "Courses" });
        octobre.Operations.Add(new Operation("Cadeau", debit: 35m));

        configuration.Charges[0] = new ModeleCharge("Loyer", 850m);
        configuration.Charges.Add(new ModeleCharge("Netflix", 13.49m));
        configuration.Enveloppes[0] = new ModeleEnveloppe("Courses", 450m);
        configuration.Enveloppes.Add(new ModeleEnveloppe("Loisirs", 80m));
        configuration.Revenus.Add(new ModeleRevenu("Prime", 150m));

        compte.AppliquerConfiguration(octobre);

        Assert.Equal(850m, octobre.Operations[0].Debit);
        Assert.Equal(20m, octobre.Operations.Single(o => o.Enveloppe == "Courses").Debit);
        Assert.Equal(35m, octobre.Operations.Single(o => o.Libelle == "Cadeau").Debit);
        Assert.Equal(13.49m, octobre.Operations.Single(o => o.Libelle == "Netflix").Debit);
        Assert.Equal(450m, octobre.Enveloppes.Single(e => e.Nom == "Courses").Budget);
        Assert.Equal(80m, octobre.Enveloppes.Single(e => e.Nom == "Loisirs").Budget);
        Assert.Equal(3100m, octobre.Revenus[0].Montant);
        Assert.Equal(150m, octobre.Revenus.Single(r => r.Nom == "Prime").Montant);
    }

    [Fact]
    public void AppliquerConfiguration_DeuxFois_NeDupliqueRien()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        var octobre = compte.CreerMoisSuivant();

        compte.AppliquerConfiguration(octobre);
        compte.AppliquerConfiguration(octobre);

        Assert.Equal(compte.Configuration.Charges.Count, octobre.Operations.Count);
        Assert.Equal(622.18m, compte.Calculer(octobre.Periode).SoldeFinPrevisionnel);
    }

    [Fact]
    public void AppliquerConfiguration_MoisDUnAutreCompte_EstRefuse()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());

        Assert.Throws<ArgumentException>(() => compte.AppliquerConfiguration(new MoisBudget(new PeriodeMois(2026, 10))));
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
