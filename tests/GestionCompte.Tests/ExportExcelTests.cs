using ClosedXML.Excel;
using GestionCompte.Core;
using GestionCompte.Core.Modeles;
using GestionCompte.Data;

namespace GestionCompte.Tests;

public sealed class ExportExcelTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"));

    public ExportExcelTests() => Directory.CreateDirectory(_dossier);

    public void Dispose() => Directory.Delete(_dossier, recursive: true);

    [Fact]
    public void ExporterMois_ContientLesOperationsEtLeSoldeFinal()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        var octobre = compte.CreerMoisSuivant();
        octobre.Operations[0].Pointee = true;
        octobre.Operations.Add(new Operation("Leclerc", debit: 120m) { Enveloppe = "Courses" });
        var chemin = Path.Combine(_dossier, "octobre.xlsx");

        ExportExcel.ExporterMois(compte, octobre.Periode, chemin);

        using var classeur = new XLWorkbook(chemin);
        var feuille = classeur.Worksheet("Octobre 2026");
        Assert.Equal("Octobre 2026", feuille.Cell(1, 1).GetString());

        var cellules = feuille.CellsUsed().ToList();
        var fin = cellules.Single(c => c.GetString() == "Solde prévisionnel de fin de mois");
        Assert.Equal(622.18m, feuille.Cell(fin.Address.RowNumber, 2).GetValue<decimal>());

        var loyer = cellules.Single(c => c.GetString() == "Loyer");
        Assert.Equal(801m, feuille.Cell(loyer.Address.RowNumber, 2).GetValue<decimal>());
        Assert.Equal("✓", feuille.Cell(loyer.Address.RowNumber, 5).GetString());

        var leclerc = cellules.Single(c => c.GetString() == "Leclerc");
        Assert.Equal("Courses", feuille.Cell(leclerc.Address.RowNumber, 6).GetString());
        Assert.Equal(622.18m, feuille.Cell(leclerc.Address.RowNumber, 4).GetValue<decimal>());
    }

    [Fact]
    public void ExporterMois_Inexistant_EstRefuse()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());

        Assert.Throws<KeyNotFoundException>(() =>
            ExportExcel.ExporterMois(compte, new PeriodeMois(2026, 10), Path.Combine(_dossier, "x.xlsx")));
    }
}
