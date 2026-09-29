using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;
using GestionCompte.Data;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class AideBudgetViewModelTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"));
    private readonly Dialogues _dialogues = new();

    private string Chemin => Path.Combine(_dossier, "compte.db");

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    private MainViewModel OuvrirDemo()
    {
        var depot = new DepotSqlite(Chemin);
        depot.Enregistrer(ConfigurationParDefaut.CreerDemo());
        return new MainViewModel(depot, _dialogues, new DateTime(2026, 11, 15));
    }

    [Fact]
    public void Demo_RepartitionEtAnalyse()
    {
        var aide = OuvrirDemo().AideBudget;

        Assert.Equal(3400m, aide.RevenusMensuels);
        Assert.Equal(new[] { "Essentiel", "Confort", "Épargne" }, aide.Parts.Select(p => p.Nom));
        Assert.Equal(0m, aide.NonClasse);
        Assert.False(aide.AChargesNonClassees);
        Assert.Equal("Loyer", aide.Charges[0].Nom);
        Assert.Contains(aide.Groupes, g => g.Famille == "Mobile" && g.Charges.Count == 4);
    }

    [Fact]
    public void Demo_SuiviDesEnveloppesSurOctobre()
    {
        var suivi = OuvrirDemo().AideBudget.SuiviEnveloppes.ToDictionary(s => s.Nom);

        Assert.True(suivi["Courses"].AReduire);
        Assert.Equal(220m, suivi["Courses"].Suivi.BudgetConseille);
        Assert.True(suivi["Carburant"].AAugmenter);
        Assert.Equal(130m, suivi["Carburant"].Suivi.BudgetConseille);
    }

    [Fact]
    public void AppliquerUnBudgetConseille_ModifieLaConfigurationApresConfirmation()
    {
        var vm = OuvrirDemo();
        var carburant = vm.AideBudget.SuiviEnveloppes.Single(s => s.Nom == "Carburant");

        _dialogues.Reponse = false;
        carburant.AppliquerCommand.Execute(null);
        Assert.Equal(100m, new DepotSqlite(Chemin).Charger()!.Configuration.Enveloppes.Single(e => e.Nom == "Carburant").BudgetParDefaut);

        _dialogues.Reponse = true;
        carburant.AppliquerCommand.Execute(null);
        Assert.Equal(130m, new DepotSqlite(Chemin).Charger()!.Configuration.Enveloppes.Single(e => e.Nom == "Carburant").BudgetParDefaut);
        Assert.Equal(130m, vm.Configuration.Enveloppes.Elements.Single(e => e.Nom == "Carburant").Montant);
    }

    [Fact]
    public void Simulateur_RetirerUneChargeEtBaisserUneEnveloppe()
    {
        var aide = OuvrirDemo().AideBudget;
        Assert.False(aide.AppliquerSimulationCommand.CanExecute(null));

        aide.Simulation.Single(l => l.Nom == "Abo Xbox").Garder = false;
        aide.Simulation.Single(l => l.Nom == "Courses").NouveauMontant = 350m;

        Assert.Equal(68m, aide.GainMensuel);
        Assert.Equal(816m, aide.GainAnnuel);
        Assert.Equal(aide.SoldeAvant + 12 * 68m, aide.SoldeApres);
        Assert.True(aide.GainPositif);
        Assert.True(aide.AppliquerSimulationCommand.CanExecute(null));

        aide.ReinitialiserSimulationCommand.Execute(null);

        Assert.Equal(0m, aide.GainMensuel);
        Assert.False(aide.SimulationChangee);
    }

    [Fact]
    public void Simulateur_Appliquer_ModifieLaConfiguration()
    {
        var vm = OuvrirDemo();
        vm.AideBudget.Simulation.Single(l => l.Nom == "Spotify").Garder = false;
        vm.AideBudget.Simulation.Single(l => l.Nom == "Loyer").NouveauMontant = 780m;

        vm.AideBudget.AppliquerSimulationCommand.Execute(null);

        var configuration = new DepotSqlite(Chemin).Charger()!.Configuration;
        Assert.DoesNotContain(configuration.Charges, c => c.Nom == "Spotify");
        Assert.Equal(780m, configuration.Charges.Single(c => c.Nom == "Loyer").Debit);
        Assert.Equal(Categorie.Essentiel, configuration.Charges.Single(c => c.Nom == "Loyer").Categorie);
        Assert.Contains("Spotify", _dialogues.DernierMessage);
        Assert.False(vm.AideBudget.SimulationChangee);
    }

    [Fact]
    public void Objectifs_DeplacerChangeLaPriorite()
    {
        var vm = OuvrirDemo();
        var aide = vm.AideBudget;
        var compteEpargne = aide.ComptesCumul[1];
        foreach (var objectif in aide.Objectifs.Elements)
            objectif.CompteCumul = compteEpargne;
        var vacances = aide.Objectifs.Elements[0];
        var urgence = aide.Objectifs.Elements[1];
        Assert.True(vacances.DejaEpargne > 0);
        Assert.Equal(0m, urgence.DejaEpargne);

        aide.Objectifs.Selection = urgence;
        aide.Objectifs.MonterCommand.Execute(null);

        Assert.Same(urgence, aide.Objectifs.Elements[0]);
        Assert.True(urgence.DejaEpargne > 0);
        Assert.Equal(0m, vacances.DejaEpargne);
        Assert.Equal(new[] { urgence.Nom, vacances.Nom },
            new DepotSqlite(Chemin).Charger()!.ObjectifsEpargne.Select(o => o.Nom));
    }

    [Fact]
    public void Credits_ComparaisonInverseEtPrevisionnel()
    {
        var vm = OuvrirDemo();
        var credits = vm.Credits;
        Assert.Equal(3, credits.Liste.Elements.Count);
        var maison20 = credits.Selection!;
        Assert.Equal("Maison 20 ans", maison20.Nom);
        Assert.Equal("ans", maison20.Unite);
        Assert.Equal(20, maison20.Duree);
        Assert.True(maison20.Mensualite > 1000m);
        Assert.True(credits.Liste.Elements[1].CoutTotal > maison20.CoutTotal);

        // Passer en mois garde le nombre saisi : 20 ans devient 20 mois.
        maison20.Unite = "mois";
        Assert.Equal(20, maison20.Modele.DureeMois);
        maison20.Unite = "ans";
        Assert.Equal(240, maison20.Modele.DureeMois);

        credits.MensualiteMaximale = maison20.Mensualite;
        Assert.InRange(credits.MontantEmpruntable, 179990m, 180000m);
        credits.CreerDepuisMensualiteCommand.Execute(null);
        Assert.Equal(4, credits.Liste.Elements.Count);

        credits.Liste.Selection = credits.Liste.Elements[2];
        credits.AjouterAuPrevisionnelCommand.Execute(null);
        Assert.True(credits.Selection!.EstAuPrevisionnel);

        var relu = new DepotSqlite(Chemin).Charger()!;
        Assert.Equal(4, relu.SimulationsCredit.Count);
        Assert.Contains(relu.OperationsPrevues, o => o.Libelle == "Crédit Moto");

        credits.RetirerDuPrevisionnelCommand.Execute(null);
        Assert.DoesNotContain(new DepotSqlite(Chemin).Charger()!.OperationsPrevues, o => o.Libelle == "Crédit Moto");
    }

    [Fact]
    public void Objectifs_MensualitesEtEnregistrement()
    {
        var vm = OuvrirDemo();
        var aide = vm.AideBudget;

        var vacances = aide.Objectifs.Elements[0];
        Assert.Equal("Vacances d'été", vacances.Nom);
        Assert.Equal(214.29m, vacances.Mensualite);
        Assert.Equal("Tenable", vacances.Statut);

        aide.Objectifs.AjouterCommand.Execute(null);
        var voiture = aide.Objectifs.Selection!;
        voiture.Nom = "Voiture";
        voiture.Montant = 20000m;
        voiture.Echeance = aide.Periodes.Single(p => p.Periode == new PeriodeMois(2027, 6));

        Assert.Equal("Difficile", voiture.Statut);
        Assert.True(voiture.EstDifficile);
        Assert.Contains(aide.Alertes, a => a.Titre == "Objectif « Voiture » difficile à tenir");
        Assert.Contains(new DepotSqlite(Chemin).Charger()!.ObjectifsEpargne, o => o.Nom == "Voiture" && o.Montant == 20000m);
    }

    [Fact]
    public void Alertes_SuiventLesModificationsDuMois()
    {
        var vm = OuvrirDemo();
        Assert.DoesNotContain(vm.AideBudget.Alertes, a => a.Niveau == NiveauAlerte.Danger);

        vm.MoisCourant!.Operations.AjouterCommand.Execute(null);
        vm.MoisCourant.Operations.Selection!.Debit = 10000m;

        Assert.Equal(NiveauAlerte.Danger, vm.AideBudget.Alertes[0].Niveau);
    }

    [Fact]
    public void Configuration_CategorieModifiable()
    {
        var vm = OuvrirDemo();

        vm.Configuration.Charges.Elements.Single(c => c.Nom == "Alarme").Categorie = ChoixCategorie.De(Categorie.Essentiel);

        Assert.Equal(Categorie.Essentiel,
            new DepotSqlite(Chemin).Charger()!.Configuration.Charges.Single(c => c.Nom == "Alarme").Categorie);
        Assert.Equal(36.99m, vm.AideBudget.Parts.Single(p => p.Categorie == Categorie.Confort).Montant);
    }

    private sealed class Dialogues : IDialogues
    {
        public bool Reponse { get; set; } = true;
        public string DernierMessage { get; private set; } = "";

        public bool Confirmer(string titre, string message)
        {
            DernierMessage = message;
            return Reponse;
        }

        public void Erreur(string message) { }
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => null;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => Releve;
        public string? Releve { get; set; }
        public void OuvrirDossier(string dossier) { }
    }
}
