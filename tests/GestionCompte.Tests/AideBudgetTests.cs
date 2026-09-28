using GestionCompte.Core;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Tests;

public class AideBudgetTests
{
    // Configuration d'octobre 2026 : 3 200 € de revenus, 2 077,82 € de charges, 500 € d'enveloppes, 622,18 € d'excédent par mois.

    private static ConfigurationBudget ConfigurationClassee()
    {
        var configuration = DonneesExcel.ConfigurationOctobre2026();
        var confort = new[] { "Abo Xbox", "Spotify" };
        for (var i = 0; i < configuration.Charges.Count; i++)
        {
            var charge = configuration.Charges[i];
            var categorie = confort.Contains(charge.Nom) ? Categorie.Confort
                : charge.Nom == "Économie" ? Categorie.Epargne
                : Categorie.Essentiel;
            configuration.Charges[i] = charge with { Categorie = categorie };
        }
        return configuration;
    }

    // ---- 1. Analyse des charges ----

    [Fact]
    public void AnalyseCharges_TrieeDeLaPlusChereALaMoinsChere()
    {
        var analyse = AideBudget.AnalyserCharges(DonneesExcel.ConfigurationOctobre2026());

        Assert.Equal(3200m, analyse.RevenusMensuels);
        Assert.Equal("Loyer", analyse.Charges[0].Nom);
        Assert.Equal(801m * 12, analyse.Charges[0].Annuel);
        Assert.Equal(801m / 3200m, analyse.Charges[0].PartRevenus);
        Assert.Equal(analyse.Charges.OrderByDescending(c => c.Mensuel).Select(c => c.Nom), analyse.Charges.Select(c => c.Nom));
        Assert.DoesNotContain(analyse.Charges, c => c.Nom == "Économie");
        Assert.Equal(2077.82m, analyse.TotalMensuel);
    }

    [Fact]
    public void AnalyseCharges_RegroupeLesFamilles()
    {
        var groupes = AideBudget.AnalyserCharges(DonneesExcel.ConfigurationOctobre2026()).Groupes.ToDictionary(g => g.Famille);

        var mobiles = groupes["Mobile"];
        Assert.Equal(4, mobiles.Charges.Count);
        Assert.Equal(9.99m + 9.99m + 8.99m + 15.99m, mobiles.Mensuel);
        Assert.Equal(mobiles.Mensuel * 12, mobiles.Annuel);
        Assert.Equal(2, groupes["Assurance"].Charges.Count);
    }

    [Theory]
    [InlineData("Mobile enfant 2", "mobile")]
    [InlineData("Crédit Voiture :", "credit")]
    [InlineData("  Électricité : ", "electricite")]
    [InlineData("", "")]
    public void CleFamille_SansAccentsNiMajuscules(string nom, string attendu)
    {
        Assert.Equal(attendu, AideBudget.CleFamille(nom));
    }

    // ---- 2. Répartition 50/30/20 ----

    [Fact]
    public void Repartition_ParCategorieAvecEnveloppesEnEssentiel()
    {
        var repartition = AideBudget.Repartir(ConfigurationClassee());
        var parts = repartition.Parts.ToDictionary(p => p.Categorie);

        Assert.Equal(2077.82m - 18m - 18.99m + 500m, parts[Categorie.Essentiel].Montant);
        Assert.Equal(36.99m, parts[Categorie.Confort].Montant);
        Assert.Equal(0m, parts[Categorie.Epargne].Montant);
        Assert.Equal(0.5m, parts[Categorie.Essentiel].Cible);
        Assert.Equal(0m, repartition.NonClasse);
        Assert.Equal(622.18m, repartition.ResteDisponible);
    }

    [Fact]
    public void Repartition_ChargesNonClassees()
    {
        var repartition = AideBudget.Repartir(DonneesExcel.ConfigurationOctobre2026());

        Assert.Equal(2077.82m, repartition.NonClasse);
        Assert.Equal(500m, repartition.Parts.Single(p => p.Categorie == Categorie.Essentiel).Montant);
    }

    [Fact]
    public void Repartition_SansRevenus_PartsAZero()
    {
        var configuration = new ConfigurationBudget { PremierMois = new PeriodeMois(2026, 1) };
        configuration.Charges.Add(new ModeleCharge("Loyer", 500m, Categorie: Categorie.Essentiel));

        Assert.All(AideBudget.Repartir(configuration).Parts, p => Assert.Equal(0m, p.Part));
    }

    // ---- 3. Suivi des enveloppes ----

    private static CompteBancaire CompteAvecDepenses(params (decimal Courses, decimal Carburant)[] mois)
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        foreach (var (courses, carburant) in mois)
        {
            var m = compte.CreerMoisSuivant();
            m.Operations.Add(new Operation("Courses", debit: courses) { Enveloppe = "Courses" });
            m.Operations.Add(new Operation("Plein", debit: carburant) { Enveloppe = "Carburant" });
        }
        // Mois en cours, non pris en compte.
        compte.CreerMoisSuivant().Operations.Add(new Operation("Courses", debit: 5000m) { Enveloppe = "Courses" });
        return compte;
    }

    [Fact]
    public void SuiviEnveloppes_BudgetTropLarge_SuggereDeReduire()
    {
        var suivi = AideBudget.SuivreEnveloppes(CompteAvecDepenses((300m, 95m), (320m, 100m), (310m, 105m)))
            .ToDictionary(s => s.Nom);

        var courses = suivi["Courses"];
        Assert.Equal(3, courses.MoisObserves);
        Assert.Equal(310m, courses.Moyenne);
        Assert.Equal(320m, courses.Maximum);
        Assert.Equal(Tendance.AReduire, courses.Tendance);
        Assert.Equal(330m, courses.BudgetConseille);

        var carburant = suivi["Carburant"];
        Assert.Equal(Tendance.Adapte, carburant.Tendance);
        Assert.Equal(1, carburant.Depassements);
        Assert.Null(carburant.BudgetConseille);
    }

    [Fact]
    public void SuiviEnveloppes_BudgetTropJuste_SuggereDAugmenter()
    {
        var carburant = AideBudget.SuivreEnveloppes(CompteAvecDepenses((400m, 130m), (400m, 118m)))
            .Single(s => s.Nom == "Carburant");

        Assert.Equal(Tendance.AAugmenter, carburant.Tendance);
        Assert.Equal(124m, carburant.Moyenne);
        Assert.Equal(130m, carburant.BudgetConseille);
        Assert.Equal(2, carburant.Depassements);
    }

    [Fact]
    public void SuiviEnveloppes_SeulementLesDerniersMois()
    {
        var courses = AideBudget.SuivreEnveloppes(CompteAvecDepenses((100m, 0m), (400m, 100m), (400m, 100m), (400m, 100m)), 3)
            .Single(s => s.Nom == "Courses");

        Assert.Equal(3, courses.MoisObserves);
        Assert.Equal(400m, courses.Moyenne);
        Assert.Equal(Tendance.Adapte, courses.Tendance);
    }

    [Fact]
    public void SuiviEnveloppes_SansMois_SansDonnees()
    {
        var suivi = AideBudget.SuivreEnveloppes(new CompteBancaire(DonneesExcel.ConfigurationOctobre2026()));

        Assert.All(suivi, s => Assert.Equal(Tendance.SansDonnees, s.Tendance));
    }

    [Fact]
    public void SuiviEnveloppes_UnSeulMois_EstUtilise()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.CreerMoisSuivant().Operations.Add(new Operation("Plein", debit: 50m) { Enveloppe = "Carburant" });

        var carburant = AideBudget.SuivreEnveloppes(compte).Single(s => s.Nom == "Carburant");

        Assert.Equal(1, carburant.MoisObserves);
        Assert.Equal(Tendance.AReduire, carburant.Tendance);
        Assert.Equal(60m, carburant.BudgetConseille);
    }

    // ---- 4. Simulateur ----

    [Fact]
    public void Simuler_SupprimerUneCharge_GainSurDouzeMois()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        var charges = compte.Configuration.Charges.Where(c => c.Nom != "Abo Xbox").ToList();

        var resultat = AideBudget.Simuler(compte, charges, compte.Configuration.Enveloppes);

        Assert.Equal(18m, resultat.GainMensuel);
        Assert.Equal(216m, resultat.GainAnnuel);
        Assert.Equal(12 * 622.18m, resultat.SoldeAvant);
        Assert.Equal(12 * (622.18m + 18m), resultat.SoldeApres);
        Assert.Contains(compte.Configuration.Charges, c => c.Nom == "Abo Xbox");
    }

    [Fact]
    public void Simuler_ChangerUnMontantEtUneEnveloppe()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.CreerMoisSuivant();
        var charges = compte.Configuration.Charges.Select(c => c.Nom == "Loyer" ? c with { Debit = 850m } : c).ToList();
        var enveloppes = compte.Configuration.Enveloppes.Select(e => e.Nom == "Courses" ? e with { BudgetParDefaut = 350m } : e).ToList();

        var resultat = AideBudget.Simuler(compte, charges, enveloppes, horizon: 6);

        Assert.Equal(-49m + 50m, resultat.GainMensuel);
        Assert.Equal(resultat.SoldeAvant + 6 * resultat.GainMensuel, resultat.SoldeApres);
        Assert.Single(compte.Mois);
    }

    // ---- 5. Objectifs d'épargne ----

    [Fact]
    public void Objectifs_MensualiteEtFaisabilite()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.ObjectifsEpargne.Add(new ObjectifEpargne("Vacances", 1500m, new PeriodeMois(2027, 6), dejaEpargne: 300m));
        compte.ObjectifsEpargne.Add(new ObjectifEpargne("Voiture", 4000m, new PeriodeMois(2027, 3)));

        var resultat = AideBudget.AnalyserObjectifs(compte);

        Assert.Equal(622.18m, resultat.CapaciteMensuelle);
        var vacances = resultat.Objectifs[0];
        Assert.Equal(9, vacances.MoisRestants);
        Assert.Equal(1200m, vacances.ResteAEpargner);
        Assert.Equal(133.34m, vacances.Mensualite);
        Assert.Equal(Faisabilite.Tenable, vacances.Faisabilite);

        var voiture = resultat.Objectifs[1];
        Assert.Equal(6, voiture.MoisRestants);
        Assert.Equal(666.67m, voiture.Mensualite);
        Assert.Equal(Faisabilite.Difficile, voiture.Faisabilite);
    }

    [Fact]
    public void Objectifs_JusteAtteintEtEcheancePassee()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.CreerMoisSuivant();
        compte.ObjectifsEpargne.Add(new ObjectifEpargne("Juste", 5500m, new PeriodeMois(2027, 8)));
        compte.ObjectifsEpargne.Add(new ObjectifEpargne("Fait", 500m, new PeriodeMois(2027, 1), dejaEpargne: 500m));
        compte.ObjectifsEpargne.Add(new ObjectifEpargne("Trop tard", 500m, new PeriodeMois(2026, 10)));

        var objectifs = AideBudget.AnalyserObjectifs(compte).Objectifs;

        Assert.Equal(550m, objectifs[0].Mensualite);
        Assert.Equal(Faisabilite.Juste, objectifs[0].Faisabilite);
        Assert.Equal(Faisabilite.Atteint, objectifs[1].Faisabilite);
        Assert.Equal(Faisabilite.EcheancePassee, objectifs[2].Faisabilite);
    }

    // ---- 6. Alertes ----

    [Fact]
    public void Alertes_DecouvertEnveloppeHausseEtObjectif()
    {
        var compte = new CompteBancaire(ConfigurationClassee());
        compte.CreerMoisSuivant();
        var novembre = compte.CreerMoisSuivant();
        novembre.Operations.Single(o => o.Libelle == "Électricité").Debit = 160m;
        novembre.Operations.Add(new Operation("Plein", debit: 130m) { Enveloppe = "Carburant" });
        novembre.Operations.Add(new Operation("Courses", debit: 350m) { Enveloppe = "Courses" });
        compte.OperationsPrevues.Add(new OperationPrevue(new PeriodeMois(2027, 1), "Voiture", debit: 10000m));
        compte.ObjectifsEpargne.Add(new ObjectifEpargne("Maison", 50000m, new PeriodeMois(2027, 12)));

        var alertes = AideBudget.Alertes(compte);
        var titres = alertes.Select(a => a.Titre).ToList();

        Assert.Equal(NiveauAlerte.Danger, alertes[0].Niveau);
        Assert.Equal("Découvert prévu en Janvier 2027", alertes[0].Titre);
        Assert.Contains("Enveloppe Carburant dépassée", titres);
        Assert.Contains("Enveloppe Courses presque utilisée", titres);
        Assert.Contains("Hausse : Électricité", titres);
        Assert.Contains(alertes, a => a.Titre == "Hausse : Électricité" && a.Detail.Contains("+10 %"));
        Assert.Contains("Objectif « Maison » difficile à tenir", titres);
        Assert.Equal(alertes.OrderByDescending(a => a.Niveau).Select(a => a.Titre), titres);
    }

    [Fact]
    public void Alertes_EpargneFaibleEtEssentielEleve()
    {
        var titres = AideBudget.Alertes(new CompteBancaire(ConfigurationClassee())).Select(a => a.Titre).ToList();

        Assert.Contains("Dépenses essentielles élevées", titres);
        Assert.Contains("Épargne programmée faible", titres);
    }

    [Fact]
    public void Alertes_BudgetEquilibre_AucuneAlerte()
    {
        var configuration = new ConfigurationBudget { PremierMois = new PeriodeMois(2026, 10) };
        configuration.Revenus.Add(new ModeleRevenu("Salaire", 2000m));
        configuration.Charges.Add(new ModeleCharge("Loyer", 900m, Categorie: Categorie.Essentiel));
        configuration.Charges.Add(new ModeleCharge("Sorties", 500m, Categorie: Categorie.Confort));
        configuration.Charges.Add(new ModeleCharge("Livret A", 400m, Categorie: Categorie.Epargne));

        var alerte = Assert.Single(AideBudget.Alertes(new CompteBancaire(configuration)));

        Assert.Equal("Aucune alerte", alerte.Titre);
    }
}
