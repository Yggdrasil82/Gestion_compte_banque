using GestionCompte.Core;
using GestionCompte.Data;
using GestionCompte.Presentation;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Tests;

public sealed class PretsTests
{
    /// <summary>Prêt fictif lissé en 3 paliers (60, 180, 60 mois), assurance 0,30 % du capital restant dû.</summary>
    private static PretImmobilier PretPaliers() =>
        new("Prêt maison", 120000m, 1.6m, new PeriodeMois(2021, 3),
            new[] { new PalierPret(60, 540m), new PalierPret(180, 430m), new PalierPret(60, 0m) }, 0.30m)
        {
            Signature = new PeriodeMois(2019, 1),
            ExonerationAnnees = 7,
        };

    private static PretImmobilier PretAvecDernierPalierCalcule()
    {
        var pret = PretPaliers();
        pret.Paliers[^1] = new PalierPret(60, CalculPret.MensualiteDernierPalier(pret)!.Value);
        return pret;
    }

    [Fact]
    public void Tableau_PremiereEcheance_InteretsEtAssuranceCommeLaBanque()
    {
        var tableau = CalculPret.Tableau(PretAvecDernierPalierCalcule());

        var premiere = tableau[0];
        Assert.Equal(new PeriodeMois(2021, 3), premiere.Periode);
        Assert.Equal(160.00m, premiere.Interets); // 120 000 × 1,6 % ÷ 12
        Assert.Equal(380.00m, premiere.Capital); // 540 − 160
        Assert.Equal(119620.00m, premiere.CapitalRestant);
        Assert.Equal(29.91m, premiere.Assurance); // 119 620 × 0,30 % ÷ 12 (capital restant après l'échéance)
        Assert.Equal(569.91m, premiere.Mensualite);
    }

    [Fact]
    public void Tableau_PaliersPuisDerniereEcheanceQuiSolde()
    {
        var pret = PretAvecDernierPalierCalcule();
        var tableau = CalculPret.Tableau(pret);

        Assert.Equal(300, tableau.Count);
        Assert.Equal(430m, tableau[60].HorsAssurance);
        Assert.Equal(1, tableau[60].Palier);
        Assert.Equal(2, tableau[^1].Palier);
        Assert.Equal(0m, tableau[^1].CapitalRestant);
        Assert.Equal(0m, tableau[^1].Assurance);
        Assert.InRange(tableau[^1].HorsAssurance, pret.Paliers[2].Mensualite - 1m, pret.Paliers[2].Mensualite + 1m);
        Assert.Equal(pret.Montant, tableau.Sum(e => e.Capital));
    }

    [Fact]
    public void Tableau_PretATauxZeroAvecDiffere_SeuleLAssuranceAuDebut()
    {
        var ptz = new PretImmobilier("PTZ", 18000m, 0m, new PeriodeMois(2021, 3),
            new[] { new PalierPret(60, 0m), new PalierPret(180, 100m) }, 0.20m) { SansIndemnite = true };

        var tableau = CalculPret.Tableau(ptz);

        Assert.Equal(240, tableau.Count);
        Assert.All(tableau.Take(60), e => Assert.Equal(0m, e.HorsAssurance));
        Assert.Equal(3.00m, tableau[0].Assurance); // 18 000 × 0,20 % ÷ 12
        Assert.Equal(100m, tableau[60].Capital);
        Assert.Equal(0m, tableau[^1].CapitalRestant);
    }

    [Fact]
    public void MensualiteDernierPalier_SoldeLePretALaDatePrevue()
    {
        var pret = new PretImmobilier("Simple", 100000m, 2m, new PeriodeMois(2026, 1), new[] { new PalierPret(240, 0m) });

        var mensualite = CalculPret.MensualiteDernierPalier(pret);

        Assert.Equal(505.88m, mensualite); // formule classique t / (1 − (1 + t)^−n)
    }

    [Fact]
    public void Indemnite_SixMoisDInteretsPlafonnesATroisPourCent()
    {
        var pret = PretPaliers();
        pret.ExonerationAnnees = 0;

        var indemnite = CalculPret.CalculerIndemnite(pret, new PeriodeMois(2024, 6), 20000m, rachat: false);

        Assert.Equal(160.00m, indemnite.Montant); // 20 000 × 1,6 % × 6 ÷ 12 = 160 < 600 (3 %)
        Assert.Contains("plus petit", indemnite.Explication);
    }

    [Fact]
    public void Indemnite_AucuneApresLAnniversaire_SaufRachat()
    {
        var pret = PretPaliers();

        Assert.Equal(0m, CalculPret.CalculerIndemnite(pret, new PeriodeMois(2026, 6), 20000m, rachat: false).Montant);
        Assert.Equal(160.00m, CalculPret.CalculerIndemnite(pret, new PeriodeMois(2026, 6), 20000m, rachat: true).Montant);
        Assert.Equal(160.00m, CalculPret.CalculerIndemnite(pret, new PeriodeMois(2025, 12), 20000m, rachat: false).Montant);
    }

    [Fact]
    public void Indemnite_PretSansIndemnite()
    {
        var pret = PretPaliers();
        pret.SansIndemnite = true;

        Assert.Equal(0m, CalculPret.CalculerIndemnite(pret, new PeriodeMois(2022, 1), 20000m, rachat: true).Montant);
    }

    [Fact]
    public void Anticipe_AvantLeDernierPalier_SeuleLaDureeBaisse()
    {
        var pret = PretAvecDernierPalierCalcule();

        var resultat = CalculPret.SimulerAnticipe(pret, new PeriodeMois(2026, 10), 20000m);

        Assert.Null(resultat.Erreur);
        Assert.NotNull(resultat.Duree);
        Assert.Null(resultat.Mensualite);
        Assert.Contains("dernier palier", resultat.SansOptionMensualite);
        Assert.True(resultat.Duree!.MoisGagnes > 0);
        Assert.True(resultat.Duree.InteretsEconomises > 0);
        Assert.Equal(0m, resultat.Indemnite!.Montant); // après le 7e anniversaire de la signature
        Assert.Equal(0m, resultat.Duree.Tableau[^1].CapitalRestant);
        Assert.Equal(20000m, resultat.Duree.Tableau.Single(e => e.Anticipe > 0).Anticipe);
    }

    [Fact]
    public void Anticipe_DansLeDernierPalier_LesDeuxOptionsSontComparees()
    {
        var pret = PretAvecDernierPalierCalcule();
        var prevu = CalculPret.Tableau(pret);

        var resultat = CalculPret.SimulerAnticipe(pret, new PeriodeMois(2042, 6), 15000m);

        Assert.NotNull(resultat.Mensualite);
        var mensualite = resultat.Mensualite!;
        Assert.Equal(prevu[^1].Periode, mensualite.Fin);
        Assert.Equal(0, mensualite.MoisGagnes);
        Assert.True(mensualite.NouvelleMensualite < mensualite.AncienneMensualite);
        Assert.Equal(0m, mensualite.Tableau[^1].CapitalRestant);
        Assert.True(resultat.Duree!.InteretsEconomises > mensualite.InteretsEconomises);
    }

    [Fact]
    public void Anticipe_SousLeMinimum_Refuse()
    {
        var resultat = CalculPret.SimulerAnticipe(PretAvecDernierPalierCalcule(), new PeriodeMois(2026, 10), 5000m);

        Assert.NotNull(resultat.Erreur);
        Assert.Contains("10 % du montant emprunté", resultat.Erreur);
    }

    [Fact]
    public void Anticipe_MontantSuperieurAuRestant_SoldeLePret()
    {
        var pret = PretAvecDernierPalierCalcule();

        var resultat = CalculPret.SimulerAnticipe(pret, new PeriodeMois(2040, 1), 500000m);

        Assert.True(resultat.Solde);
        Assert.Equal(resultat.CapitalRestantAvant, resultat.CapitalRembourse);
        Assert.Equal(new PeriodeMois(2040, 1), resultat.Duree!.Tableau[^1].Periode);
        Assert.Null(resultat.Duree.Fin);
    }

    [Fact]
    public void Anticipe_MoisHorsDuPret_Refuse()
    {
        var resultat = CalculPret.SimulerAnticipe(PretAvecDernierPalierCalcule(), new PeriodeMois(2020, 1), 20000m);

        Assert.NotNull(resultat.Erreur);
    }

    [Fact]
    public void CapitalRestantAu_AvantPendantApres()
    {
        var pret = PretAvecDernierPalierCalcule();
        var tableau = CalculPret.Tableau(pret);

        Assert.Equal(120000m, CalculPret.CapitalRestantAu(pret, tableau, new PeriodeMois(2020, 12)));
        Assert.Equal(119620m, CalculPret.CapitalRestantAu(pret, tableau, new PeriodeMois(2021, 3)));
        Assert.Equal(0m, CalculPret.CapitalRestantAu(pret, tableau, new PeriodeMois(2060, 1)));
    }
}

public sealed class PretsViewModelTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"));

    public PretsViewModelTests() => Directory.CreateDirectory(_dossier);

    public void Dispose() => Directory.Delete(_dossier, recursive: true);

    private string Chemin => Path.Combine(_dossier, "compte.db");

    private static CompteBancaire CompteAvecPret()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        var pret = new PretImmobilier("Maison", 120000m, 1.6m, new PeriodeMois(2021, 3),
            new[] { new PalierPret(60, 540m), new PalierPret(180, 430m), new PalierPret(60, 0m) }, 0.30m)
        {
            Signature = new PeriodeMois(2019, 1),
            ExonerationAnnees = 7,
        };
        pret.Paliers[2] = new PalierPret(60, CalculPret.MensualiteDernierPalier(pret)!.Value);
        compte.Prets.Add(pret);
        return compte;
    }

    [Fact]
    public void Depot_EnregistreEtRelitLesPrets()
    {
        var compte = CompteAvecPret();
        compte.Prets.Add(new PretImmobilier("PTZ", 18000m, 0m, new PeriodeMois(2021, 3),
            new[] { new PalierPret(60, 0m), new PalierPret(180, 100m) }, 4m, AssurancePret.ParMois) { SansIndemnite = true });
        new DepotSqlite(Chemin).Enregistrer(compte);

        var relu = new DepotSqlite(Chemin).Charger()!;

        Assert.Equal(2, relu.Prets.Count);
        var maison = relu.Prets[0];
        Assert.Equal(120000m, maison.Montant);
        Assert.Equal(1.6m, maison.TauxAnnuel);
        Assert.Equal(3, maison.Paliers.Count);
        Assert.Equal(compte.Prets[0].Paliers[2], maison.Paliers[2]);
        Assert.Equal(new PeriodeMois(2019, 1), maison.Signature);
        Assert.Equal(7, maison.ExonerationAnnees);
        Assert.True(relu.Prets[1].SansIndemnite);
        Assert.Equal(AssurancePret.ParMois, relu.Prets[1].TypeAssurance);
        Assert.Null(relu.Prets[1].Signature);
    }

    [Fact]
    public void ViewModel_ChiffresDuMoisEtSimulationParDefaut()
    {
        var vm = new PretsViewModel(CompteAvecPret(), new PeriodeMois(2026, 11), new Dialogues(), () => { }, () => { });

        var pret = vm.Selection!;
        Assert.True(pret.EcheanceDuMois > 0);
        Assert.Contains(pret.Tableau, l => l.MoisEnCours && l.Mois == "Novembre 2026");
        Assert.Equal(new PeriodeMois(2026, 11), vm.DateAnticipe!.Periode);
        Assert.Equal(12000m, vm.MontantAnticipe); // 10 % du montant emprunté
        Assert.True(vm.AnticipeValide);
        Assert.NotNull(vm.OptionDuree);
        Assert.False(vm.AvecOptionMensualite);
        Assert.Contains("dernier palier", vm.SansOptionMensualite);
        Assert.Contains("Intérêts :", pret.Tableau[0].Explication);
    }

    [Fact]
    public void ViewModel_ModifierUnPalierRecalculeEtEnregistre()
    {
        var enregistrements = 0;
        var vm = new PretsViewModel(CompteAvecPret(), new PeriodeMois(2026, 11), new Dialogues(), () => enregistrements++, () => { });
        var avant = vm.Selection!.CoutInterets;

        vm.Selection.Paliers[1].Mensualite = 500m;

        Assert.Equal(1, enregistrements);
        Assert.True(vm.Selection.CoutInterets < avant);
        Assert.NotEqual("", vm.Selection.Avertissement); // le dernier palier ne correspond plus
        vm.CalculerDernierPalierCommand.Execute(null);
        Assert.Equal("", vm.Selection.Avertissement);
    }

    [Fact]
    public void ViewModel_AjouterAuxCharges_CreePuisMetAJourLaCharge()
    {
        var compte = CompteAvecPret();
        var modifiees = 0;
        var vm = new PretsViewModel(compte, new PeriodeMois(2026, 11), new Dialogues(), () => { }, () => modifiees++);

        vm.AjouterAuxChargesCommand.Execute(null);

        var charge = compte.Configuration.Charges.Single(c => c.Nom == "Prêt Maison");
        Assert.Equal(vm.Selection!.EcheanceDuMois, charge.Debit);
        Assert.Equal(1, modifiees);
        Assert.Equal("Mettre à jour la charge", vm.Selection.TexteBoutonCharges);

        vm.Selection.Paliers[1].Mensualite = 450m;
        vm.AjouterAuxChargesCommand.Execute(null);
        Assert.Single(compte.Configuration.Charges, c => c.Nom == "Prêt Maison");
        Assert.Equal(vm.Selection.EcheanceDuMois, compte.Configuration.Charges.Single(c => c.Nom == "Prêt Maison").Debit);
    }

    [Fact]
    public void ExportExcel_PretEtSimulation()
    {
        var compte = CompteAvecPret();
        var chemin = Path.Combine(_dossier, "pret.xlsx");
        var simulation = CalculPret.SimulerAnticipe(compte.Prets[0], new PeriodeMois(2026, 11), 20000m);

        ExportExcel.ExporterPret(compte.Prets[0], chemin, simulation, new PeriodeMois(2026, 11));

        using var classeur = new ClosedXML.Excel.XLWorkbook(chemin);
        Assert.Equal(2, classeur.Worksheets.Count);
        Assert.Contains("Remboursement anticipé", classeur.Worksheets.Select(f => f.Name));
    }

    private sealed class Dialogues : IDialogues
    {
        public bool Confirmer(string titre, string message) => true;
        public void Erreur(string message) => throw new InvalidOperationException(message);
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => null;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => null;
        public void OuvrirDossier(string dossier) { }
    }
}
