using GestionCompte.Core.Impots;

namespace GestionCompte.Tests;

/// <summary>Module « Impôts » : barème, quotient familial, décote, loyers, bourse et pistes (montants inventés).</summary>
public sealed class ImpotsTests
{
    private static readonly ParametresImpot Bareme = new();

    [Fact]
    public void Celibataire_AvecDecote()
    {
        var e = CalculImpot.Calculer(Bareme, new FoyerFiscal(), new[] { new RevenuFiscal("Salaire", TypeRevenuFiscal.Salaire, 30_000m) }, 0);

        Assert.Equal(3_000m, e.DeductionSalaires);
        Assert.Equal(27_000m, e.RevenuImposable);
        Assert.Equal(1_694m, e.ImpotBrut);   // (27 000 − 11 600) × 11 %
        Assert.Equal(130m, e.Decote);        // 897 − 45,25 % × 1 694
        Assert.Equal(1_564m, e.ImpotRevenu);
        Assert.Equal(0.11m, e.Tmi);
    }

    [Fact]
    public void Couple_DeuxEnfants_QuotientPlafonne()
    {
        var foyer = new FoyerFiscal { EnCouple = true, Enfants = 2 };
        var e = CalculImpot.Calculer(Bareme, foyer, new[] { new RevenuFiscal("Salaires", TypeRevenuFiscal.Salaire, 80_000m) }, 0);

        Assert.Equal(3m, e.Parts);
        Assert.True(e.QuotientPlafonne);
        Assert.Equal(4_194m, e.ImpotRevenu); // 7 808 sans enfants − 2 × 1 807
    }

    [Fact]
    public void ParentIsole_UnePartPourLePremierEnfant()
    {
        Assert.Equal(2m, CalculImpot.Parts(new FoyerFiscal { Enfants = 1 }));
        Assert.Equal(4m, CalculImpot.Parts(new FoyerFiscal { EnCouple = true, Enfants = 3 }));
    }

    [Fact]
    public void Loyers_EtBourse_PrelevementsSociaux()
    {
        var revenus = new[]
        {
            new RevenuFiscal("Loyer studio", TypeRevenuFiscal.LoyerNu, 10_000m),
            new RevenuFiscal("Loyer meublé", TypeRevenuFiscal.LoyerMeuble, 6_000m),
        };
        var e = CalculImpot.Calculer(Bareme, new FoyerFiscal(), revenus, 1_000m);

        Assert.Equal(7_000m, e.FoncierImposable);
        Assert.Equal(3_000m, e.MeubleImposable);
        Assert.Equal(128m, e.ImpotForfaitaire);
        Assert.Equal(1_204m + 558m + 186m, e.PrelevementsSociaux); // 17,2 % × 7 000 + 18,6 % × 3 000 + 18,6 % × 1 000
    }

    [Fact]
    public void PrelevementALaSource_Solde()
    {
        var foyer = new FoyerFiscal { TauxPrelevement = 5m };
        var e = CalculImpot.Calculer(Bareme, foyer, new[] { new RevenuFiscal("Salaire", TypeRevenuFiscal.Salaire, 30_000m) }, 0);

        Assert.Equal(1_500m, e.Preleve);
        Assert.Equal(64m, e.Solde);
        Assert.Equal(5.2m, e.TauxCalcule);
    }

    [Fact]
    public void Pistes_PerDonsEmploi()
    {
        var revenus = new[] { new RevenuFiscal("Salaire", TypeRevenuFiscal.Salaire, 50_000m) };
        var pistes = CalculImpot.Pistes(Bareme, new FoyerFiscal(), revenus, 0);

        Assert.Equal(300m, pistes.Single(p => p.Titre.StartsWith("Épargne retraite")).Economie); // 1 000 € × 30 %
        Assert.Equal(66m, pistes.Single(p => p.Titre == "Dons").Economie);
        Assert.Equal(500m, pistes.Single(p => p.Titre == "Emploi à domicile").Economie);
        Assert.Contains(pistes, p => p.Titre == "Frais réels");
    }

    [Fact]
    public void Deduction10Pourcent_ParPersonne()
    {
        var revenus = new[]
        {
            new RevenuFiscal("Salaire", TypeRevenuFiscal.Salaire, 3_000m),
            new RevenuFiscal("Job d'été", TypeRevenuFiscal.Salaire, 3_000m, "Enfant 1"),
        };
        var e = CalculImpot.Calculer(Bareme, new FoyerFiscal { Enfants = 1 }, revenus, 0);

        Assert.Equal(1_018m, e.DeductionSalaires); // minimum de 509 € pour chacun (et non 600 € pour le foyer)
    }

    [Fact]
    public void Deduction10Pourcent_PlafondParPersonne()
    {
        var revenus = new[]
        {
            new RevenuFiscal("Salaire", TypeRevenuFiscal.Salaire, 200_000m),
            new RevenuFiscal("Salaire conjoint", TypeRevenuFiscal.Salaire, 200_000m, PersonnesFoyer.Conjoint),
        };
        var e = CalculImpot.Calculer(Bareme, new FoyerFiscal { EnCouple = true }, revenus, 0);

        Assert.Equal(2 * 14_555m, e.DeductionSalaires);
    }

    [Fact]
    public void PlafondPer_ParDeclarant()
    {
        var revenus = new[]
        {
            new RevenuFiscal("Salaire", TypeRevenuFiscal.Salaire, 60_000m),
            new RevenuFiscal("Salaire conjoint", TypeRevenuFiscal.Salaire, 20_000m, PersonnesFoyer.Conjoint),
        };

        Assert.Equal(5_400m + 4_710m, CalculImpot.Calculer(Bareme, new FoyerFiscal { EnCouple = true }, revenus, 0).PlafondPer);
        Assert.Equal(5_400m, CalculImpot.Calculer(Bareme, new FoyerFiscal(), revenus.Take(1), 0).PlafondPer);
    }

    [Fact]
    public void Pistes_OptionBareme_SiPetiteTranche()
    {
        var revenus = new[] { new RevenuFiscal("Salaire", TypeRevenuFiscal.Salaire, 15_000m) };
        var pistes = CalculImpot.Pistes(Bareme, new FoyerFiscal(), revenus, 1_000m);

        Assert.Contains(pistes, p => p.Titre == "Bourse : option pour le barème" && p.Economie > 0);
    }
}

/// <summary>Écran Impôts : revenus de tous les comptes, réglages enregistrés dans « impots.json ».</summary>
public sealed class ImpotsViewModelTests : IDisposable
{
    private readonly string _dossier = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_dossier, recursive: true);

    private static GestionCompte.Core.CompteBancaire Compte()
    {
        var configuration = new GestionCompte.Core.Modeles.ConfigurationBudget { PremierMois = new GestionCompte.Core.Modeles.PeriodeMois(2026, 1) };
        configuration.Revenus.Add(new GestionCompte.Core.Modeles.ModeleRevenu("Salaire", 2_000m));
        configuration.Revenus.Add(new GestionCompte.Core.Modeles.ModeleRevenu("Loyer T2", 500m));
        configuration.Revenus.Add(new GestionCompte.Core.Modeles.ModeleRevenu("CAF", 100m));
        var compte = new GestionCompte.Core.CompteBancaire(configuration);
        compte.CreerMoisSuivant().Revenus.Single(r => r.Nom == "Salaire").Montant = 3_000m; // prime en janvier
        return compte;
    }

    [Fact]
    public void Actualiser_LitLesRevenusDeLAnnee_EtMemoriseLesChoix()
    {
        var compte = Compte();
        var module = new GestionCompte.Presentation.ImpotsViewModel(_dossier, () => new[] { ("Compte", compte) }, new DateTime(2026, 6, 1));
        module.Actualiser();

        var salaire = module.Lignes.Single(l => l.Revenu == "Salaire");
        Assert.Equal(25_000m, salaire.MontantRecu); // 3 000 + 11 × 2 000
        Assert.Equal(TypeRevenuFiscal.Salaire, salaire.Type);
        Assert.Equal(TypeRevenuFiscal.LoyerNu, module.Lignes.Single(l => l.Revenu == "Loyer T2").Type);
        Assert.Equal(TypeRevenuFiscal.NonImposable, module.Lignes.Single(l => l.Revenu == "CAF").Type);
        Assert.Equal(25_750m, salaire.MontantRetenu); // × 1,03 sans prélèvement

        salaire.MontantImposable = 26_000m;
        module.Lignes.Single(l => l.Revenu == "Loyer T2").IndexType = (int)TypeRevenuFiscal.LoyerMeuble;
        module.Enfants = 2;
        salaire.IndexPersonne = 1;

        var relu = new GestionCompte.Presentation.ImpotsViewModel(_dossier, () => new[] { ("Compte", compte) }, new DateTime(2026, 6, 1));
        relu.Actualiser();
        Assert.Equal(26_000m, relu.Lignes.Single(l => l.Revenu == "Salaire").MontantRetenu);
        Assert.Equal(TypeRevenuFiscal.LoyerMeuble, relu.Lignes.Single(l => l.Revenu == "Loyer T2").Type);
        Assert.Equal(2, relu.Enfants);
        Assert.Equal(PersonnesFoyer.Conjoint, relu.Lignes.Single(l => l.Revenu == "Salaire").Personne);
        Assert.Equal(3_000m, relu.Estimation.MeubleImposable);
        Assert.Contains("Voici l'estimation", relu.QuestionAnalyse());
    }
}
