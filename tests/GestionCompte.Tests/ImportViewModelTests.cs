using GestionCompte.Core.Import;
using GestionCompte.Core.Modeles;
using GestionCompte.Data;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class ImportViewModelTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"));
    private readonly Dialogues _dialogues = new();

    public ImportViewModelTests() => Directory.CreateDirectory(_dossier);

    public void Dispose() => Directory.Delete(_dossier, recursive: true);

    private string Chemin(string nom) => Path.Combine(_dossier, nom);

    private MainViewModel OuvrirDemo()
    {
        var depot = new DepotSqlite(Chemin("compte.db"));
        depot.Enregistrer(ConfigurationParDefaut.CreerDemo());
        File.WriteAllText(Chemin("releve.ofx"), ConfigurationParDefaut.ReleveDemo());
        _dialogues.Releve = Chemin("releve.ofx");
        return new MainViewModel(depot, _dialogues, new DateTime(2026, 11, 15));
    }

    [Fact]
    public void ImporterReleve_AfficheLApercuSansRienModifier()
    {
        var vm = OuvrirDemo();

        vm.ImporterReleveCommand.Execute(null);

        var import = vm.Import!;
        Assert.Equal(MainViewModel.OngletImport, vm.OngletSelectionne);
        Assert.Equal("releve.ofx", import.NomFichier);
        Assert.Equal(10, import.Lignes.Count);
        Assert.Equal("Décembre 2026", import.MoisACreer);
        Assert.True(import.ASoldeBanque);
        Assert.Equal(2, new DepotSqlite(Chemin("compte.db")).Charger()!.Mois.Count);

        var parLibelle = import.Lignes.ToDictionary(l => l.Libelle);
        Assert.Equal(StatutImport.Rapprochee, parLibelle["PRLV SEPA LOYER SCI LES TILLEULS"].Statut);
        Assert.Equal(StatutImport.MontantAjuste, parLibelle["PRLV SEPA EDF ELECTRICITE"].Statut);
        Assert.Equal(StatutImport.RevenuRecu, parLibelle["VIR SALAIRE"].Statut);
        Assert.Equal("Courses", parLibelle["CB LECLERC DRIVE"].Enveloppe);
        Assert.Equal("Carburant", parLibelle["CB ESSO EXPRESS"].Enveloppe);
        Assert.Equal(StatutImport.Nouvelle, parLibelle["CB COFIDIS AMAZON"].Statut);
    }

    [Fact]
    public void ReleveAvantLePremierMois_RienAImporter_ComparaisonAuSoldeDeDepart()
    {
        var vm = OuvrirDemo();
        var ofx = ImportReleveTests.Ofx(
                ImportReleveTests.Ligne("DEBIT", "20260925", "-9.99", "S1", "PRLV SEPA BOUYGUES TELECOM"),
                ImportReleveTests.Ligne("POS", "20260925", "-3.80", "S2", "CB HUGO"))
            .Replace("20261027000000", "20260927000000");
        File.WriteAllText(Chemin("septembre.ofx"), ofx);

        vm.OuvrirImport(Chemin("septembre.ofx"));

        var import = vm.Import!;
        Assert.True(import.RienAImporter);
        Assert.Contains("antérieur au premier mois", import.MessageRienAImporter);
        Assert.False(import.ValiderCommand.CanExecute(null));
        Assert.True(import.SoldeAvantPremierMois);
        Assert.Equal(ConfigurationParDefaut.CreerDemo().Configuration.SoldeInitial, import.SoldePointeApres);
        Assert.Equal(-483.45m - import.SoldePointeApres, import.Ecart);
    }

    [Fact]
    public void ToutesLesLignesDecochees_ValiderImpossible()
    {
        var vm = OuvrirDemo();
        vm.ImporterReleveCommand.Execute(null);
        var import = vm.Import!;
        Assert.False(import.RienAImporter);
        Assert.True(import.ValiderCommand.CanExecute(null));

        foreach (var ligne in import.Lignes)
            ligne.Importer = false;

        Assert.False(import.ValiderCommand.CanExecute(null));
    }

    [Fact]
    public void Annuler_RevientALEcranPrecedent()
    {
        var vm = OuvrirDemo();
        vm.OngletSelectionne = MainViewModel.OngletPrevisionnel;
        vm.ImporterReleveCommand.Execute(null);

        vm.Import!.AnnulerCommand.Execute(null);

        Assert.Null(vm.Import);
        Assert.Equal(MainViewModel.OngletPrevisionnel, vm.OngletSelectionne);
        Assert.Equal(2, new DepotSqlite(Chemin("compte.db")).Charger()!.Mois.Count);
    }

    [Fact]
    public void Valider_CreeLeMoisImporteEtEnregistre()
    {
        var vm = OuvrirDemo();
        vm.ImporterReleveCommand.Execute(null);

        vm.Import!.ValiderCommand.Execute(null);

        Assert.Null(vm.Import);
        Assert.Equal("Décembre 2026", vm.TitreMois);
        Assert.Contains("Décembre 2026", _dialogues.DerniereConfirmation);
        var decembre = new DepotSqlite(Chemin("compte.db")).Charger()!.Mois[2];
        Assert.True(decembre.Operations.Single(o => o.Libelle == "Loyer").Pointee);
        Assert.Equal(152.30m, decembre.Operations.Single(o => o.Libelle == "Électricité").Debit);
        Assert.True(decembre.Revenus.Single(r => r.Nom == "Salaire").Recu);
        Assert.Equal("Courses", decembre.Operations.Single(o => o.Libelle == "CB LECLERC DRIVE").Enveloppe);
        Assert.StartsWith("Import terminé", vm.Statut);
    }

    [Fact]
    public void Valider_RefusDeCreerLeMois_RienNEstFait()
    {
        var vm = OuvrirDemo();
        vm.ImporterReleveCommand.Execute(null);
        _dialogues.Reponse = false;

        vm.Import!.ValiderCommand.Execute(null);

        Assert.NotNull(vm.Import);
        Assert.Equal(2, new DepotSqlite(Chemin("compte.db")).Charger()!.Mois.Count);
    }

    [Fact]
    public void ChoixModifies_ResumeEtRegleRetenue()
    {
        var vm = OuvrirDemo();
        vm.ImporterReleveCommand.Execute(null);
        var import = vm.Import!;
        var nouvelles = import.NombreNouvelles;

        var boulangerie = import.Lignes.Single(l => l.Libelle == "CB BOULANGERIE DU MARCHE");
        Assert.False(boulangerie.PeutRetenirRegle);
        boulangerie.Enveloppe = "Courses";
        Assert.True(boulangerie.PeutRetenirRegle);
        Assert.Equal("BOULANGERIE", boulangerie.MotCleSuggere);

        var amazon = import.Lignes.Single(l => l.Libelle == "CB COFIDIS AMAZON");
        amazon.Importer = false;
        Assert.Equal(nouvelles - 1, import.NombreNouvelles);
        Assert.Equal(1, import.NombreIgnorees);

        var loyer = import.Lignes.Single(l => l.Libelle == "PRLV SEPA LOYER SCI LES TILLEULS");
        loyer.OptionChoisie = loyer.Options[0];
        Assert.Equal(StatutImport.Nouvelle, loyer.Statut);
        loyer.OptionChoisie = loyer.Options.Single(o => o.Candidat?.Libelle == "Loyer");

        import.ValiderCommand.Execute(null);

        var compte = new DepotSqlite(Chemin("compte.db")).Charger()!;
        Assert.Contains(new RegleClassement("BOULANGERIE", "Courses"), compte.Configuration.Regles);
        Assert.DoesNotContain(compte.Mois[2].Operations, o => o.Libelle == "CB COFIDIS AMAZON");
        Assert.Equal("Courses", compte.Mois[2].Operations.Single(o => o.Libelle == "CB BOULANGERIE DU MARCHE").Enveloppe);
    }

    [Fact]
    public void ReimporterLeMemeReleve_ToutEstDejaImporte()
    {
        var vm = OuvrirDemo();
        vm.ImporterReleveCommand.Execute(null);
        vm.Import!.ValiderCommand.Execute(null);

        vm.ImporterReleveCommand.Execute(null);

        Assert.All(vm.Import!.Lignes, l => Assert.Equal(StatutImport.DejaImportee, l.Statut));
        Assert.Equal(10, vm.Import.NombreIgnorees);
        Assert.Empty(vm.Import.MoisACreer);
    }

    [Fact]
    public void FichierInvalide_ErreurSansAperçu()
    {
        var vm = OuvrirDemo();
        File.WriteAllText(Chemin("faux.ofx"), "Date;Libellé;Montant");
        _dialogues.Releve = Chemin("faux.ofx");

        vm.ImporterReleveCommand.Execute(null);

        Assert.Null(vm.Import);
        Assert.Single(_dialogues.Erreurs);
    }

    [Fact]
    public void Configuration_ReglesModifiables()
    {
        var vm = OuvrirDemo();

        vm.Configuration.Regles.AjouterCommand.Execute(null);
        vm.Configuration.Regles.Selection!.MotCle = "INTERMARCHÉ SUPER";
        vm.Configuration.Regles.Selection!.Enveloppe = "Courses";

        Assert.Contains(new RegleClassement("INTERMARCHÉ SUPER", "Courses"),
            new DepotSqlite(Chemin("compte.db")).Charger()!.Configuration.Regles);
        Assert.Contains("Carburant", vm.Configuration.NomsEnveloppes);
    }

    [Fact]
    public void RevenuRecu_CocheManuellementEnregistree()
    {
        var vm = OuvrirDemo();

        vm.MoisCourant!.Revenus.Elements[0].Recu = true;

        Assert.True(new DepotSqlite(Chemin("compte.db")).Charger()!.Mois[1].Revenus[0].Recu);
    }

    private sealed class Dialogues : IDialogues
    {
        public bool Reponse { get; set; } = true;
        public string DerniereConfirmation { get; private set; } = "";
        public List<string> Erreurs { get; } = new();
        public string? Releve { get; set; }

        public bool Confirmer(string titre, string message)
        {
            DerniereConfirmation = message;
            return Reponse;
        }

        public void Erreur(string message) => Erreurs.Add(message);
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => null;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => Releve;
        public void OuvrirDossier(string dossier) { }
    }
}
