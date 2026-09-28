using GestionCompte.Core.Modeles;

namespace GestionCompte.Tests;

public class PeriodeMoisTests
{
    [Theory]
    [InlineData(2026, 10, "Octobre 2026")]
    [InlineData(2027, 2, "Février 2027")]
    [InlineData(2027, 8, "Août 2027")]
    public void Libelle_EnFrancaisAvecMajuscule(int annee, int mois, string attendu)
    {
        Assert.Equal(attendu, new PeriodeMois(annee, mois).Libelle);
    }

    [Fact]
    public void Precedent_DeJanvier_EstDecembreDeLAnneePrecedente()
    {
        Assert.Equal(new PeriodeMois(2026, 12), new PeriodeMois(2027, 1).Precedent());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void MoisInvalide_Refuse(int mois)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PeriodeMois(2026, mois));
    }

    [Fact]
    public void Comparaison_ChronologiqueSurPlusieursAnnees()
    {
        Assert.True(new PeriodeMois(2026, 12) < new PeriodeMois(2027, 1));
        Assert.True(new PeriodeMois(2027, 1) > new PeriodeMois(2026, 12));
    }
}
