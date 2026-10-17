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
    public void Pistes_OptionBareme_SiPetiteTranche()
    {
        var revenus = new[] { new RevenuFiscal("Salaire", TypeRevenuFiscal.Salaire, 15_000m) };
        var pistes = CalculImpot.Pistes(Bareme, new FoyerFiscal(), revenus, 1_000m);

        Assert.Contains(pistes, p => p.Titre == "Bourse : option pour le barème" && p.Economie > 0);
    }
}
