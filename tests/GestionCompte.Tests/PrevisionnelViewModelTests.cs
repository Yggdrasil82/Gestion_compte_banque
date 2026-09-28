using GestionCompte.Core.Modeles;
using GestionCompte.Data;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class PrevisionnelViewModelTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"));
    private readonly Dialogues _dialogues = new();

    private string Chemin(string nom = "compte.db") => Path.Combine(_dossier, nom);

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    private MainViewModel OuvrirDemo()
    {
        var depot = new DepotSqlite(Chemin());
        depot.Enregistrer(ConfigurationParDefaut.CreerDemo());
        return new MainViewModel(depot, _dialogues, new DateTime(2026, 11, 15));
    }

    [Fact]
    public void Demo_DeuxMoisReelsPuisDouzeMoisPrevus()
    {
        var prevision = OuvrirDemo().Previsionnel;

        Assert.Equal(14, prevision.Lignes.Count);
        Assert.Equal(new[] { "Réel", "Réel", "Prévu" }, prevision.Lignes.Take(3).Select(l => l.Type));
        Assert.Equal("Décembre 2026", prevision.Lignes[2].Libelle);
        Assert.Equal("Solde fin Novembre 2027", prevision.TitreSoldeFinal);
        Assert.Equal(prevision.Lignes[^1].SoldeFin, prevision.SoldeFinal);
    }

    [Fact]
    public void Horizon_ChangeLeNombreDeMoisPrevus()
    {
        var prevision = OuvrirDemo().Previsionnel;

        prevision.Horizon = 24;

        Assert.Equal(26, prevision.Lignes.Count);
        Assert.Equal("Épargne dans 24 mois", prevision.TitreEpargne);
    }

    [Fact]
    public void Echeance_RembFamilleAvecDate()
    {
        // 200 € déjà remboursés fin novembre 2026, puis 100 € par mois : 1 500 € en décembre 2027.
        var echeance = OuvrirDemo().Previsionnel.Echeances.Single();

        Assert.Equal(ConfigurationParDefaut.RembFamille, echeance.Nom);
        Assert.Contains("atteint en Décembre 2027", echeance.Texte);
    }

    [Fact]
    public void AjouterUneOperationPrevue_ChangeLaPrevisionEtEstEnregistree()
    {
        var vm = OuvrirDemo();
        var prevision = vm.Previsionnel;
        var avant = prevision.SoldeFinal;

        prevision.OperationsPrevues.AjouterCommand.Execute(null);
        var nouvelle = prevision.OperationsPrevues.Selection!;
        nouvelle.Libelle = "Nouveau canapé";
        nouvelle.Periode = prevision.Periodes.Single(p => p.Periode == new PeriodeMois(2027, 3));
        nouvelle.Debit = 700m;

        Assert.Equal(avant - 700m, prevision.SoldeFinal);
        var enregistre = new DepotSqlite(Chemin()).Charger()!;
        Assert.Contains(enregistre.OperationsPrevues, o => o.Libelle == "Nouveau canapé" && o.Periode == new PeriodeMois(2027, 3) && o.Debit == 700m);
    }

    [Fact]
    public void GrosseDepensePrevue_AlerteDecouvert()
    {
        var prevision = OuvrirDemo().Previsionnel;
        Assert.False(prevision.DecouvertPrevu);

        prevision.OperationsPrevues.AjouterCommand.Execute(null);
        prevision.OperationsPrevues.Selection!.Debit = 20000m;

        Assert.True(prevision.DecouvertPrevu);
        Assert.StartsWith("Découvert prévu en Décembre 2026", prevision.Alerte);
    }

    [Fact]
    public void CreerLeMoisSuivant_IntegreLesOperationsPrevues()
    {
        var vm = OuvrirDemo();

        vm.CreerMoisCommand.Execute(null);

        Assert.Contains(vm.MoisCourant!.Operations.Elements, o => o.Libelle == "Cadeaux de Noël");
        Assert.DoesNotContain(vm.Previsionnel.OperationsPrevues.Elements, o => o.Libelle == "Cadeaux de Noël");
        Assert.Equal("Janvier 2027", vm.Previsionnel.Periodes[0].Libelle);
    }

    [Fact]
    public void ModifierUnMois_MetAJourLePrevisionnel()
    {
        var vm = OuvrirDemo();
        var avant = vm.Previsionnel.SoldeFinal;

        vm.MoisCourant!.Operations.Elements.Single(o => o.Libelle == "Loyer").Debit = 901m;

        Assert.Equal(avant - 100m, vm.Previsionnel.SoldeFinal);
    }

    [Fact]
    public void ModifierLaConfiguration_MetAJourLesMoisPrevus()
    {
        var vm = OuvrirDemo();
        var avant = vm.Previsionnel.SoldeFinal;

        vm.Configuration.Charges.Elements.Single(c => c.Nom == "Loyer").Debit = 851m;

        Assert.Equal(avant - 12 * 50m, vm.Previsionnel.SoldeFinal);
        Assert.Equal(12, vm.Previsionnel.Horizon);
    }

    [Fact]
    public void ExporterMois_CreeLeFichierExcel()
    {
        var vm = OuvrirDemo();
        _dialogues.FichierExport = Chemin("novembre.xlsx");

        vm.ExporterMoisCommand.Execute(null);

        Assert.True(File.Exists(Chemin("novembre.xlsx")));
        Assert.Empty(_dialogues.Erreurs);
    }

    [Fact]
    public void ExporterMois_FichierImpossible_AfficheUneErreur()
    {
        var vm = OuvrirDemo();
        // Un dossier porte déjà ce nom : le fichier ne peut pas être écrit.
        Directory.CreateDirectory(Chemin("novembre.xlsx"));
        _dialogues.FichierExport = Chemin("novembre.xlsx");

        vm.ExporterMoisCommand.Execute(null);

        Assert.Single(_dialogues.Erreurs);
    }

    private sealed class Dialogues : IDialogues
    {
        public List<string> Erreurs { get; } = new();
        public string? FichierExport { get; set; }

        public bool Confirmer(string titre, string message) => true;
        public void Erreur(string message) => Erreurs.Add(message);
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => FichierExport;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => Releve;
        public string? Releve { get; set; }
        public void OuvrirDossier(string dossier) { }
    }
}
