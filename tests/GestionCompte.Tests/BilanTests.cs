using GestionCompte.Core;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;
using GestionCompte.Data;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class BilanTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    private static readonly PeriodeMois Janvier2026 = new(2026, 1);
    private static readonly PeriodeMois Decembre2026 = new(2026, 12);

    [Fact]
    public void Totaux_ExpliquentLaVariationDuSolde()
    {
        var compte = ConfigurationParDefaut.CreerDemoHistorique();

        var bilan = Bilan.Calculer(compte, Janvier2026, Decembre2026);

        Assert.Equal(10, bilan.Mois.Count);
        Assert.Equal("10 mois, de janvier 2026 à octobre 2026", bilan.Etendue);
        Assert.Equal(bilan.Variation, bilan.Revenus - bilan.Depenses - bilan.Epargne);
        Assert.Equal(compte.Calculer(new PeriodeMois(2025, 12)).SoldeFinPrevisionnel, bilan.SoldeDebut);
        Assert.Equal(compte.Calculer(new PeriodeMois(2026, 10)).SoldeFinPrevisionnel, bilan.SoldeFin);
        Assert.Equal(1500m, bilan.Epargne);
        Assert.Equal(0.044m, decimal.Round(bilan.TauxEpargne!.Value, 3));
    }

    [Fact]
    public void Postes_EnveloppesAuBudgetEtComparaisonAuxMemesMois()
    {
        var bilan = Bilan.Calculer(ConfigurationParDefaut.CreerDemoHistorique(), Janvier2026, Decembre2026);

        Assert.Equal("Loyer", bilan.Postes[0].Nom);
        var courses = bilan.Postes.Single(p => p.Nom == "Courses");
        Assert.Equal(TypePoste.Enveloppe, courses.Type);
        Assert.Equal(4000m, courses.Total); // budget de 400 € non dépassé : compté en entier, comme dans le solde

        var electricite = bilan.Postes.Single(p => p.Nom == "Électricité");
        Assert.Equal(172m, electricite.MoyenneMensuelle);
        Assert.Equal(145m, electricite.MoyennePrecedente);

        // Janvier à octobre 2026 comparés à janvier à octobre 2025.
        Assert.Equal(10, bilan.Precedent!.Mois);
        var vacances = bilan.Postes.Single(p => p.Nom == "Vacances");
        Assert.Equal(TypePoste.Autre, vacances.Type);
        Assert.Equal(135m, vacances.MoyenneMensuelle);
        Assert.Equal(110m, vacances.MoyennePrecedente);
        Assert.DoesNotContain(bilan.Postes, p => p.Nom is "Leclerc" or "Plein Total");
        Assert.Equal(TypePoste.Epargne, bilan.Postes.Single(p => p.Nom == ConfigurationParDefaut.Epargne).Type);
    }

    [Fact]
    public void Pistes_HaussesEnveloppesEtConfort()
    {
        var bilan = Bilan.Calculer(ConfigurationParDefaut.CreerDemoHistorique(), Janvier2026, Decembre2026);
        var titres = bilan.Pistes.Select(p => p.Titre).ToList();

        Assert.StartsWith("« Restaurant » en hausse", titres[0]);
        Assert.Equal(540m, bilan.Pistes[0].GainAnnuel);
        Assert.Contains(titres, t => t.StartsWith("« Électricité » en hausse de 19 %"));
        Assert.Contains(bilan.Pistes, p => p.Titre == "Enveloppe « Courses » plus large que nécessaire" && p.GainAnnuel == 1440m);
        Assert.Contains(titres, t => t == "Enveloppe « Carburant » dépassée 10 mois sur 10");
        Assert.Contains(titres, t => t.StartsWith("Charges « confort »"));
        Assert.Contains(titres, t => t.StartsWith("4 charges « Mobile »"));
        // L'épargne en hausse n'est pas une piste d'économie.
        Assert.DoesNotContain(titres, t => t.Contains(ConfigurationParDefaut.Epargne));
    }

    [Fact]
    public void DouzeDerniersMois_JusquAuMoisTermine()
    {
        var (debut, fin) = Bilan.DouzeDerniersMois(new PeriodeMois(2026, 11));
        Assert.Equal(new PeriodeMois(2025, 11), debut);
        Assert.Equal(new PeriodeMois(2026, 10), fin);

        var bilan = Bilan.Calculer(ConfigurationParDefaut.CreerDemoHistorique(), debut, fin);
        Assert.Equal(12, bilan.Mois.Count);
        Assert.Equal(bilan.Variation, bilan.Revenus - bilan.Depenses - bilan.Epargne);
    }

    [Fact]
    public void RemboursementCompteAvecLesRevenus_PeriodeVide()
    {
        var compte = ConfigurationParDefaut.CreerDemoHistorique();
        var mars = Bilan.Calculer(compte, new PeriodeMois(2025, 3), new PeriodeMois(2025, 3));
        Assert.Equal(3400m + 64.20m, mars.Revenus);

        var vide = Bilan.Calculer(compte, new PeriodeMois(2030, 1), new PeriodeMois(2030, 12));
        Assert.True(vide.Vide);
        Assert.Empty(vide.Postes);
        Assert.Null(vide.Precedent);
        Assert.Equal(new[] { 2026, 2025 }, Bilan.Annees(compte));
    }

    [Fact]
    public void Exports_ExcelEtPdf()
    {
        Directory.CreateDirectory(_dossier);
        var bilan = Bilan.Calculer(ConfigurationParDefaut.CreerDemoHistorique(), Janvier2026, Decembre2026);
        var excel = Path.Combine(_dossier, "bilan.xlsx");
        var pdf = Path.Combine(_dossier, "bilan.pdf");

        ExportExcel.ExporterBilan(bilan, "Bilan 2026", excel);
        ExportPdf.ExporterBilan(bilan, "Bilan 2026", pdf);

        using (var classeur = new ClosedXML.Excel.XLWorkbook(excel))
        {
            var feuille = classeur.Worksheet("Bilan");
            Assert.Equal("Bilan 2026", feuille.Cell(1, 1).GetString());
            Assert.Contains(feuille.CellsUsed(), c => c.GetString() == "Restaurant");
        }
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 4));
    }

    [Fact]
    public void Module_ChoixDesPeriodesEtExport()
    {
        var chemin = Path.Combine(_dossier, "compte.db");
        new DepotSqlite(chemin).Enregistrer(ConfigurationParDefaut.CreerDemoHistorique());
        var dialogues = new Dialogues { Destination = Path.Combine(_dossier, "export.pdf") };
        var vm = new MainViewModel(new DepotSqlite(chemin), dialogues, new DateTime(2026, 11, 15));
        var bilan = vm.Bilan;

        Assert.Equal(new[] { BilanViewModel.DouzeDerniersMois, "Année 2026", "Année 2025" }, bilan.Choix.Select(c => c.Libelle));
        Assert.Equal("Année 2026", bilan.Selection!.Libelle);
        Assert.Equal("Bilan 2026", bilan.Titre);
        Assert.Equal(10, bilan.Mois.Count);
        Assert.Contains("un an plus tôt", bilan.ComparaisonDepenses);
        var restaurant = bilan.Postes.Single(p => p.Nom == "Restaurant");
        Assert.Equal("+97 %", restaurant.Evolution);
        Assert.True(restaurant.EvolutionDefavorable);
        Assert.True(bilan.Postes.Single(p => p.Nom == ConfigurationParDefaut.Epargne).EvolutionFavorable);

        bilan.Selection = bilan.Choix.Single(c => c.Libelle == "Année 2025");
        Assert.Equal(12, bilan.Mois.Count);
        Assert.All(bilan.Postes, p => Assert.Equal("", p.Evolution));

        bilan.ExporterPdfCommand.Execute(null);
        Assert.True(File.Exists(dialogues.Destination));
        Assert.StartsWith("Bilan exporté", bilan.MessageExport);

        // Une opération ajoutée dans un mois est prise en compte, sur la période choisie.
        vm.AllerAuMoisEnCoursCommand.Execute(null);
        vm.MoisCourant!.Operations.AjouterCommand.Execute(null);
        vm.MoisCourant.Operations.Selection!.Libelle = "Garage";
        vm.MoisCourant.Operations.Selection.Debit = 300m;
        Assert.Equal("Année 2025", vm.Bilan.Selection!.Libelle);
        vm.Bilan.Selection = vm.Bilan.Choix.Single(c => c.Libelle == "Année 2026");
        Assert.Contains(vm.Bilan.Postes, p => p.Nom == "Garage" && p.Total == 300m);
    }

    [Fact]
    public void Module_MasqueQuandOnEstDessus()
    {
        var chemin = Path.Combine(_dossier, "compte.db");
        new DepotSqlite(chemin).Enregistrer(ConfigurationParDefaut.CreerDemo());
        var vm = new MainViewModel(new DepotSqlite(chemin), new Dialogues(), new DateTime(2026, 11, 15), new ApparenceViewModel(null));
        Assert.True(vm.Apparence.ModuleBilan);
        vm.OngletSelectionne = MainViewModel.OngletBilan;

        vm.Apparence.ModuleBilan = false;

        Assert.Equal(MainViewModel.OngletConfiguration, vm.OngletSelectionne);
    }

    private sealed class Dialogues : IDialogues
    {
        public string? Destination { get; set; }

        public bool Confirmer(string titre, string message) => true;
        public void Erreur(string message) => throw new InvalidOperationException(message);
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => Destination;
        public string? ChoisirFichierPdf(string nomParDefaut) => Destination;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => null;
        public void OuvrirDossier(string dossier) { }
    }
}
