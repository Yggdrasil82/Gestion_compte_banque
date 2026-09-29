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
        var apparence = new ApparenceViewModel(Chemin);
        Assert.True(apparence.ModuleCredits);

        apparence.ModuleCredits = false;
        Assert.False(new ApparenceViewModel(Chemin).ModuleCredits);

        apparence.Reinitialiser();
        Assert.True(new ApparenceViewModel(Chemin).ModuleCredits);
    }

    [Fact]
    public void ModuleCredits_MasqueQuandOnEstDessus_RetourALaConfiguration()
    {
        var depot = new DepotSqlite(Path.Combine(_dossier, "compte.db"));
        depot.Enregistrer(ConfigurationParDefaut.CreerDemo());
        var vm = new MainViewModel(depot, new SansDialogue(), new DateTime(2026, 11, 15), new ApparenceViewModel(null));
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
