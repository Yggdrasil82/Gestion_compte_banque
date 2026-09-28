using GestionCompte.Core.Modeles;
using GestionCompte.Data;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private static readonly DateTime Aujourdhui = new(2026, 10, 15);

    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"));
    private readonly FauxDialogues _dialogues = new();

    private string Chemin(string nom = "compte.db") => Path.Combine(_dossier, nom);

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    private MainViewModel Ouvrir(DateTime? aujourdhui = null) =>
        new(new DepotSqlite(Chemin()), _dialogues, aujourdhui ?? Aujourdhui);

    private MainViewModel OuvrirAvecUnMois()
    {
        var vm = Ouvrir();
        vm.CreerMoisCommand.Execute(null);
        return vm;
    }

    [Fact]
    public void PremierLancement_CreeLaConfigurationParDefautEtAfficheLaConfiguration()
    {
        var vm = Ouvrir();

        Assert.True(vm.AucunMois);
        Assert.Null(vm.MoisCourant);
        Assert.Equal(MainViewModel.OngletConfiguration, vm.OngletSelectionne);
        Assert.True(vm.Configuration.PremierMoisModifiable);
        Assert.Equal(9, vm.Configuration.IndexMoisDebut);
        Assert.Equal(2026, vm.Configuration.AnneeDebut);
        Assert.True(File.Exists(Chemin()));
        Assert.Equal("Créer le premier mois (Octobre 2026)", vm.TexteCreerMois);
    }

    [Fact]
    public void CreerMois_AfficheLeMoisEtBloqueLePremierMois()
    {
        var vm = OuvrirAvecUnMois();

        Assert.Equal("Octobre 2026", vm.TitreMois);
        Assert.Equal(MainViewModel.OngletMois, vm.OngletSelectionne);
        Assert.False(vm.Configuration.PremierMoisModifiable);
        Assert.Equal("Créer Novembre 2026", vm.TexteCreerMois);
    }

    [Fact]
    public void MoisDeDemarrage_ChoisiDansLaConfiguration()
    {
        var vm = Ouvrir();
        vm.Configuration.IndexMoisDebut = 0;
        vm.Configuration.AnneeDebut = 2027;

        vm.CreerMoisCommand.Execute(null);

        Assert.Equal("Janvier 2027", vm.TitreMois);
    }

    [Fact]
    public void ModifierUneOperation_RecalculeEtEnregistre()
    {
        var vm = OuvrirAvecUnMois();
        var soldeAvant = vm.MoisCourant!.SoldeFin;

        vm.MoisCourant.Operations.Elements.Single(o => o.Libelle == "Loyer").Debit = 851m;

        Assert.Equal(soldeAvant - 50m, vm.MoisCourant.SoldeFin);
        var recharge = new DepotSqlite(Chemin()).Charger()!;
        Assert.Equal(851m, recharge.Mois[0].Operations.Single(o => o.Libelle == "Loyer").Debit);
    }

    [Fact]
    public void AjouterUneDepenseCourses_MetAJourLEnveloppeSansChangerLeSolde()
    {
        var vm = OuvrirAvecUnMois();
        var mois = vm.MoisCourant!;
        var soldeAvant = mois.SoldeFin;

        mois.Operations.AjouterCommand.Execute(null);
        var nouvelle = mois.Operations.Selection!;
        nouvelle.Libelle = "Leclerc";
        nouvelle.Enveloppe = "Courses";
        nouvelle.Debit = 120m;

        var courses = mois.Enveloppes.Single(e => e.Nom == "Courses");
        Assert.Equal(120m, courses.Depense);
        Assert.Equal(280m, courses.Reste);
        Assert.Equal(30d, courses.Progression);
        Assert.Equal(soldeAvant, mois.SoldeFin);
        Assert.Equal(mois.SoldeFin, mois.Operations.Elements[^1].Solde);
    }

    [Fact]
    public void Operations_SupprimerEtDeplacer_SontEnregistres()
    {
        var vm = OuvrirAvecUnMois();
        var operations = vm.MoisCourant!.Operations;
        var soldeAvant = vm.MoisCourant.SoldeFin;

        operations.Selection = operations.Elements.Single(o => o.Libelle == "Loyer");
        operations.SupprimerCommand.Execute(null);
        operations.Selection = operations.Elements.Single(o => o.Libelle == "Eau");
        operations.MonterCommand.Execute(null);

        Assert.Equal(soldeAvant + 801m, vm.MoisCourant.SoldeFin);
        var libelles = new DepotSqlite(Chemin()).Charger()!.Mois[0].Operations.Select(o => o.Libelle).ToList();
        Assert.DoesNotContain("Loyer", libelles);
        Assert.True(libelles.IndexOf("Eau") < libelles.IndexOf("Électricité"));
    }

    [Fact]
    public void ListeEditable_BoutonsActifsSelonLaSelection()
    {
        var operations = OuvrirAvecUnMois().MoisCourant!.Operations;

        Assert.False(operations.SupprimerCommand.CanExecute(null));

        operations.Selection = operations.Elements[0];
        Assert.True(operations.SupprimerCommand.CanExecute(null));
        Assert.False(operations.MonterCommand.CanExecute(null));
        Assert.True(operations.DescendreCommand.CanExecute(null));

        operations.Selection = operations.Elements[^1];
        Assert.False(operations.DescendreCommand.CanExecute(null));
    }

    [Fact]
    public void Pointage_MetAJourLeResume()
    {
        var vm = OuvrirAvecUnMois();
        var mois = vm.MoisCourant!;
        var total = mois.Operations.Elements.Count;

        mois.Operations.Elements[0].Pointee = true;
        mois.Operations.Elements[1].Pointee = true;

        Assert.Equal($"2 / {total} opérations pointées", mois.ResumePointage);
    }

    [Fact]
    public void RevenuModifie_ChangeLeSoldeDuMoisEtDuMoisSuivant()
    {
        var vm = OuvrirAvecUnMois();
        vm.MoisCourant!.Revenus.Elements.Single(r => r.Nom == "NDF").Montant = 100m;
        var finOctobre = vm.MoisCourant.SoldeFin;

        vm.CreerMoisCommand.Execute(null);

        Assert.Equal(finOctobre, vm.MoisCourant!.AncienSolde);
        Assert.Equal(0m, vm.MoisCourant.Revenus.Elements.Single(r => r.Nom == "NDF").Montant);
    }

    [Fact]
    public void Navigation_EntreLesMois()
    {
        var vm = OuvrirAvecUnMois();
        vm.CreerMoisCommand.Execute(null);

        Assert.Equal("Novembre 2026", vm.TitreMois);
        Assert.False(vm.MoisSuivantCommand.CanExecute(null));

        vm.MoisPrecedentCommand.Execute(null);

        Assert.Equal("Octobre 2026", vm.TitreMois);
        Assert.False(vm.MoisPrecedentCommand.CanExecute(null));
        Assert.True(vm.MoisSuivantCommand.CanExecute(null));
    }

    [Fact]
    public void Redemarrage_AfficheLeMoisDuJourSinonLeDernier()
    {
        var vm = OuvrirAvecUnMois();
        vm.CreerMoisCommand.Execute(null);
        vm.CreerMoisCommand.Execute(null);

        Assert.Equal("Novembre 2026", Ouvrir(new DateTime(2026, 11, 3)).TitreMois);
        Assert.Equal("Décembre 2026", Ouvrir(new DateTime(2027, 6, 1)).TitreMois);
        Assert.Equal(MainViewModel.OngletMois, Ouvrir().OngletSelectionne);
    }

    [Fact]
    public void SupprimerDernierMois_DemandeConfirmation()
    {
        var vm = OuvrirAvecUnMois();
        vm.CreerMoisCommand.Execute(null);

        _dialogues.ReponseConfirmation = false;
        vm.SupprimerDernierMoisCommand.Execute(null);
        Assert.Equal(2, new DepotSqlite(Chemin()).Charger()!.Mois.Count);

        _dialogues.ReponseConfirmation = true;
        vm.SupprimerDernierMoisCommand.Execute(null);
        Assert.Single(new DepotSqlite(Chemin()).Charger()!.Mois);
        Assert.Equal("Octobre 2026", vm.TitreMois);
        Assert.Contains("Novembre 2026", _dialogues.DerniereConfirmation);
    }

    [Fact]
    public void ModifierLaConfiguration_SAppliqueAuxNouveauxMoisSeulement()
    {
        var vm = OuvrirAvecUnMois();

        vm.Configuration.Charges.Elements.Single(c => c.Nom == "Loyer").Debit = 900m;
        vm.CreerMoisCommand.Execute(null);

        var compte = new DepotSqlite(Chemin()).Charger()!;
        Assert.Equal(801m, compte.Mois[0].Operations.Single(o => o.Libelle == "Loyer").Debit);
        Assert.Equal(900m, compte.Mois[1].Operations.Single(o => o.Libelle == "Loyer").Debit);
    }

    [Fact]
    public void AppliquerConfiguration_MetAJourLeMoisAffiche()
    {
        var vm = OuvrirAvecUnMois();
        vm.Configuration.Charges.Elements.Single(c => c.Nom == "Loyer").Debit = 900m;
        vm.Configuration.Enveloppes.AjouterCommand.Execute(null);
        vm.Configuration.Enveloppes.Selection!.Nom = "Loisirs";
        vm.Configuration.Enveloppes.Selection!.Montant = 60m;

        vm.AppliquerConfigurationCommand.Execute(null);

        Assert.Equal(900m, vm.MoisCourant!.Operations.Elements.Single(o => o.Libelle == "Loyer").Debit);
        Assert.Contains(vm.MoisCourant.Enveloppes, e => e.Nom == "Loisirs" && e.Budget == 60m);
        Assert.Contains("Loisirs", vm.MoisCourant.NomsEnveloppes);
    }

    [Fact]
    public void SoldeInitial_ModifieDansLaConfiguration_ChangeLeMoisAffiche()
    {
        var vm = OuvrirAvecUnMois();
        var avant = vm.MoisCourant!.SoldeFin;

        vm.Configuration.SoldeInitial = 250m;

        Assert.Equal(250m, vm.MoisCourant!.AncienSolde);
        Assert.Equal(avant + 250m, vm.MoisCourant.SoldeFin);
    }

    [Fact]
    public void CompteCumul_ObjectifAfficheLeResteARembourser()
    {
        var vm = OuvrirAvecUnMois();

        vm.Configuration.ComptesCumul.Elements.Single(c => c.Nom == ConfigurationParDefaut.RembPascale).Objectif = 1000m;

        var remb = vm.MoisCourant!.ComptesCumul.Single(c => c.Nom == ConfigurationParDefaut.RembPascale);
        Assert.Equal(100m, remb.Total);
        Assert.Equal(10d, remb.Progression);
        Assert.Contains("reste 900,00", remb.Detail);
    }

    [Fact]
    public void SauvegarderPuisRestaurer_RetrouveLesDonnees()
    {
        var vm = OuvrirAvecUnMois();
        _dialogues.FichierSauvegarde = Chemin("sauvegarde.db");
        vm.SauvegarderCopieCommand.Execute(null);

        vm.CreerMoisCommand.Execute(null);
        _dialogues.FichierARestaurer = Chemin("sauvegarde.db");
        vm.RestaurerCommand.Execute(null);

        Assert.Equal("Octobre 2026", vm.TitreMois);
        Assert.Single(new DepotSqlite(Chemin()).Charger()!.Mois);
        Assert.Equal(2, new DepotSqlite(Chemin("compte.db.avant-restauration.db")).Charger()!.Mois.Count);
        Assert.Empty(_dialogues.Erreurs);
    }

    [Fact]
    public void Restaurer_FichierInvalide_AfficheUneErreurSansRienChanger()
    {
        var vm = OuvrirAvecUnMois();
        Directory.CreateDirectory(_dossier);
        File.WriteAllText(Chemin("pas-une-base.db"), "ceci n'est pas une base SQLite");
        _dialogues.FichierARestaurer = Chemin("pas-une-base.db");

        vm.RestaurerCommand.Execute(null);

        Assert.Single(_dialogues.Erreurs);
        Assert.Single(new DepotSqlite(Chemin()).Charger()!.Mois);
    }

    [Fact]
    public void Demo_DeuxMoisCoherents()
    {
        var compte = ConfigurationParDefaut.CreerDemo();

        Assert.Equal(2, compte.Mois.Count);
        Assert.Equal(compte.Calculer(compte.Mois[0].Periode).SoldeFinPrevisionnel,
                     compte.Calculer(compte.Mois[1].Periode).AncienSolde);
    }

    private sealed class FauxDialogues : IDialogues
    {
        public bool ReponseConfirmation { get; set; } = true;
        public string DerniereConfirmation { get; private set; } = "";
        public List<string> Erreurs { get; } = new();
        public string? FichierSauvegarde { get; set; }
        public string? FichierARestaurer { get; set; }

        public bool Confirmer(string titre, string message)
        {
            DerniereConfirmation = message;
            return ReponseConfirmation;
        }

        public void Erreur(string message) => Erreurs.Add(message);

        public string? ChoisirFichierSauvegarde(string nomParDefaut) => FichierSauvegarde;

        public string? ChoisirFichierARestaurer() => FichierARestaurer;

        public void OuvrirDossier(string dossier) { }
    }
}
