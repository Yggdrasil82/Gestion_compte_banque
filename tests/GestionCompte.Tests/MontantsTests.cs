using GestionCompte.Core;

namespace GestionCompte.Tests;

public class MontantsTests
{
    [Theory]
    [InlineData("12,50", 12.50)]
    [InlineData("12.50", 12.50)]
    [InlineData("1 234,56", 1234.56)]
    [InlineData("1 234,56 €", 1234.56)]
    [InlineData("1 234,56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("-3", -3)]
    [InlineData("  801  ", 801)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void TryLire_AccepteLesSaisiesCourantes(string? texte, double attendu)
    {
        Assert.True(Montants.TryLire(texte, out var montant));
        Assert.Equal((decimal)attendu, montant);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12,5x")]
    [InlineData("€")]
    [InlineData("--3")]
    public void TryLire_RefuseLesSaisiesInvalides(string texte)
    {
        Assert.False(Montants.TryLire(texte, out _));
    }

    [Fact]
    public void Formater_AuFormatFrancais()
    {
        Assert.True(Montants.TryLire(Montants.Formater(1234.5m), out var relu));
        Assert.Equal(1234.5m, relu);
        Assert.EndsWith("234,50", Montants.Formater(1234.5m));
        Assert.Equal("622,18", Montants.Formater(622.18m));
    }
}
