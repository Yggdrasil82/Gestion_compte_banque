using GestionCompte.Data;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class ApparenceTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"));

    private string Chemin => Path.Combine(_dossier, "preferences.json");

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    [Fact]
    public void ParDefaut_OceanClair()
    {
        var apparence = new ApparenceViewModel(Chemin);

        Assert.Equal(Ambiance.Ocean, apparence.Ambiance);
        Assert.False(apparence.Sombre);
        Assert.True(apparence.ModeSombreModifiable);
    }

    [Fact]
    public void ChoixMemoriseEntreDeuxLancements()
    {
        var apparence = new ApparenceViewModel(Chemin);
        apparence.Ambiance = Ambiance.Pastel;
        apparence.ModeSombre = true;

        var relue = new ApparenceViewModel(Chemin);

        Assert.Equal(Ambiance.Pastel, relue.Ambiance);
        Assert.True(relue.ModeSombre);
        Assert.True(relue.Sombre);
    }

    [Fact]
    public void Nuit_EstToujoursSombre()
    {
        var apparence = new ApparenceViewModel(null) { Ambiance = Ambiance.Nuit };

        Assert.True(apparence.Sombre);
        Assert.False(apparence.ModeSombreModifiable);
        Assert.False(apparence.ModeSombre);
    }

    [Fact]
    public void Changement_PrevientLAffichage()
    {
        var apparence = new ApparenceViewModel(null);
        var notifications = 0;
        apparence.Changee += (_, _) => notifications++;

        apparence.Ambiance = Ambiance.Nuit;
        apparence.Ambiance = Ambiance.Nuit;
        apparence.ModeSombre = true;

        Assert.Equal(2, notifications);
    }

    [Fact]
    public void FichierIllisible_ValeursParDefaut()
    {
        Directory.CreateDirectory(_dossier);
        File.WriteAllText(Chemin, "{ pas du json");

        Assert.Equal(Ambiance.Ocean, new ApparenceViewModel(Chemin).Ambiance);
    }

    [Fact]
    public void SansChemin_RienNEstEnregistre()
    {
        var apparence = new ApparenceViewModel(null) { Ambiance = Ambiance.Pastel };

        Assert.Equal(Ambiance.Pastel, apparence.Ambiance);
        Assert.False(Directory.Exists(_dossier));
    }

    [Fact]
    public void ModuleCredits_ActifParDefautEtMemorise()
    {
        var apparence = Ouvrir("compte.db");
        Assert.True(apparence.ModuleCredits);

        apparence.ModuleCredits = false;
        Assert.False(Ouvrir("compte.db").ModuleCredits);

        apparence.Reinitialiser();
        Assert.True(Ouvrir("compte.db").ModuleCredits);
    }

    private ApparenceViewModel Ouvrir(string compte)
    {
        var apparence = new ApparenceViewModel(Chemin);
        apparence.ChoisirCompte(compte);
        return apparence;
    }

    [Fact]
    public void Modules_PropresAChaqueCompte()
    {
        var apparence = Ouvrir("compte.db");
        apparence.ChoisirCompte("compte-2.db");
        apparence.ModuleBourse = false;
        apparence.Deplacer("Bourse", -1);

        apparence.ChoisirCompte("compte.db");
        Assert.True(apparence.ModuleBourse);
        Assert.Equal("Impots", apparence.OrdreModules[^1]);

        var relu = Ouvrir("compte-2.db");
        Assert.False(relu.ModuleBourse);
        Assert.Equal(new[] { "Bourse", "Prets", "Impots" }, relu.OrdreModules.TakeLast(3));
    }

    [Fact]
    public void Modules_NouveauCompte_RepritDuCompteOuvert()
    {
        var apparence = Ouvrir("compte.db");
        apparence.ModuleMail = false;
        apparence.Deplacer("Assistant", -5);

        apparence.ChoisirCompte("compte-3.db");

        Assert.False(apparence.ModuleMail);
        Assert.Equal("Assistant", apparence.OrdreModules[0]);
        apparence.ModuleMail = true;
        apparence.ChoisirCompte("compte.db");
        Assert.False(apparence.ModuleMail);
    }

    [Fact]
    public void Modules_Deplacer_ListeDeLaConfigurationSuit()
    {
        var apparence = Ouvrir("compte.db");

        apparence.Modules[2].MonterCommand.Execute(null);
        apparence.Modules[1].MonterCommand.Execute(null);
        apparence.Modules[0].MonterCommand.Execute(null);
        apparence.Modules[^1].DescendreCommand.Execute(null);

        Assert.Equal(new[] { "Mail", "Bilan", "Documents" }, apparence.OrdreModules.Take(3));
        Assert.Equal(apparence.OrdreModules, apparence.Modules.Select(m => m.Id));
        Assert.Equal(10, apparence.OrdreModules.Count);
    }

    [Fact]
    public void Modules_ReglagesDAvant211_RepritsParLePremierCompte()
    {
        Directory.CreateDirectory(_dossier);
        File.WriteAllText(Chemin, """{ "ModuleBilan": false, "ModuleLettres": false }""");

        var apparence = Ouvrir("compte.db");

        Assert.False(apparence.ModuleBilan);
        Assert.False(apparence.ModuleLettres);
        Assert.True(apparence.ModuleMail);
        Assert.False(apparence.Modules.First(m => m.Id == "Bilan").Affiche);
    }

    [Fact]
    public void Modules_CompteSupprime_Oublie()
    {
        var apparence = Ouvrir("compte.db");
        apparence.ChoisirCompte("compte-2.db");
        apparence.ModuleAchats = false;
        apparence.ChoisirCompte("compte.db");
        apparence.OublierCompte("compte-2.db");

        apparence.ChoisirCompte("compte-2.db");

        Assert.True(apparence.ModuleAchats);
    }

    [Fact]
    public void ModuleCredits_MasqueQuandOnEstDessus_RetourALaConfiguration()
    {
        var depot = new DepotSqlite(Path.Combine(_dossier, "compte.db"));
        depot.Enregistrer(ConfigurationParDefaut.CreerDemo());
        var vm = new MainViewModel(depot, new SansDialogue(), new DateTime(2026, 11, 15), new ApparenceViewModel(null));
        Assert.Same(vm.Apparence, vm.Configuration.Apparence);
        vm.OngletSelectionne = MainViewModel.OngletCredits;

        vm.Apparence.ModuleCredits = false;

        Assert.Equal(MainViewModel.OngletConfiguration, vm.OngletSelectionne);
        Assert.Equal(3, vm.Credits.Liste.Elements.Count);
    }

    [Fact]
    public void Mois_TuilesDeDepensesEtDEpargne()
    {
        var depot = new DepotSqlite(Path.Combine(_dossier, "compte.db"));
        depot.Enregistrer(ConfigurationParDefaut.CreerDemo());
        var vm = new MainViewModel(depot, new SansDialogue(), new DateTime(2026, 11, 15));

        var mois = vm.MoisCourant!;

        Assert.Equal(mois.TotalReserveEnveloppes + mois.TotalDebits, mois.DepensesPrevues);
        Assert.Equal(mois.SoldeDepart - mois.DepensesPrevues + mois.TotalCredits, mois.SoldeFin);
        Assert.Equal(ConfigurationParDefaut.Epargne, mois.CompteCumulPrincipal!.Nom);
        Assert.Equal(150m, mois.CompteCumulPrincipal.Total);
        Assert.Equal(9, mois.NombrePointees);
    }

    private sealed class SansDialogue : IDialogues
    {
        public bool Confirmer(string titre, string message) => false;
        public void Erreur(string message) { }
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => null;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => Releve;
        public string? Releve { get; set; }
        public void OuvrirDossier(string dossier) { }
    }
}
