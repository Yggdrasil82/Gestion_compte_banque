using GestionCompte.Core;
using GestionCompte.Core.Import;
using GestionCompte.Core.Modeles;
using GestionCompte.Data;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

/// <summary>Plusieurs comptes (une banque par compte), vue d'ensemble et réinitialisation.</summary>
public sealed class ComptesTests : IDisposable
{
    private static readonly DateTime Aujourdhui = new(2026, 11, 15);
    private static readonly PeriodeMois Novembre = new(2026, 11);

    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"));
    private readonly FauxDialogues _dialogues = new();

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    private string Chemin(string nom) => Path.Combine(_dossier, nom);

    /// <summary>Compte principal = données d'exemple (octobre et novembre 2026).</summary>
    private MainViewModel Ouvrir()
    {
        if (!File.Exists(Chemin(RegistreComptes.FichierPrincipal)))
            new DepotSqlite(Chemin(RegistreComptes.FichierPrincipal)).Enregistrer(ConfigurationParDefaut.CreerDemo());
        return new MainViewModel(RegistreComptes.Charger(_dossier), _dialogues, Aujourdhui);
    }

    private MainViewModel OuvrirAvecLivret()
    {
        var vm = Ouvrir();
        _dialogues.NouveauCompte = new DemandeNouveauCompte("Livret A", CopierDe: null);
        vm.NouveauCompteCommand.Execute(null);
        return vm;
    }

    // ---- Liste des comptes ----

    [Fact]
    public void Registre_SansFichier_UnSeulComptePrincipal()
    {
        var registre = RegistreComptes.Charger(_dossier);

        Assert.Equal(new EntreeCompte(RegistreComptes.NomPrincipal, "compte.db"), Assert.Single(registre.Comptes));
        Assert.Equal(Chemin("compte.db"), registre.Chemin(registre.Actif));
    }

    [Fact]
    public void Registre_AjouterRenommerSupprimer_EstMemorise()
    {
        var registre = RegistreComptes.Charger(_dossier);
        var livret = registre.Ajouter("Livret A");
        var joint = registre.Ajouter("Compte joint");
        registre.DefinirActif(joint);
        livret = registre.Renommer(livret, "Livret Bleu");

        Assert.Equal(("compte-2.db", "compte-3.db"), (livret.Fichier, joint.Fichier));
        Assert.False(registre.NomDisponible("livret bleu"));
        Assert.Throws<ArgumentException>(() => registre.Ajouter("Compte principal"));

        var relu = RegistreComptes.Charger(_dossier);
        Assert.Equal(new[] { "Compte principal", "Livret Bleu", "Compte joint" }, relu.Comptes.Select(c => c.Nom));
        Assert.Equal("Compte joint", relu.Actif.Nom);

        File.WriteAllText(Chemin("compte-3.db"), "données");
        relu.Supprimer(relu.Actif);
        Assert.False(File.Exists(Chemin("compte-3.db")));
        Assert.Equal(2, RegistreComptes.Charger(_dossier).Comptes.Count);
    }

    [Fact]
    public void Registre_DernierCompte_NePeutPasEtreSupprime()
    {
        var registre = RegistreComptes.Charger(_dossier);
        Assert.Throws<InvalidOperationException>(() => registre.Supprimer(registre.Actif));
    }

    [Fact]
    public void Registre_FichierIllisible_ComptePrincipal()
    {
        Directory.CreateDirectory(_dossier);
        File.WriteAllText(Chemin(RegistreComptes.NomFichier), "{ pas du json");

        Assert.Single(RegistreComptes.Charger(_dossier).Comptes);
    }

    [Fact]
    public void Registre_FichierHorsDuDossier_Ignore()
    {
        Directory.CreateDirectory(_dossier);
        File.WriteAllText(Chemin(RegistreComptes.NomFichier),
            """{ "Comptes": [ { "Nom": "Pirate", "Fichier": "..\\autre.db" } ] }""");

        Assert.Equal(RegistreComptes.NomPrincipal, Assert.Single(RegistreComptes.Charger(_dossier).Comptes).Nom);
    }

    // ---- Comptes dans l'application ----

    [Fact]
    public void UnSeulCompte_PasDeVueDEnsemble()
    {
        var vm = Ouvrir();

        Assert.True(vm.GestionComptes);
        Assert.False(vm.PlusieursComptes);
        Assert.Equal(new[] { RegistreComptes.NomPrincipal }, vm.NomsComptes);
        Assert.False(vm.SupprimerCompteCommand.CanExecute(null));
    }

    [Fact]
    public void NouveauCompte_Vierge_EstOuvertSurLaConfiguration()
    {
        var vm = OuvrirAvecLivret();

        Assert.Equal("Livret A", vm.CompteActif);
        Assert.True(vm.PlusieursComptes);
        Assert.True(vm.AucunMois);
        Assert.Equal(MainViewModel.OngletConfiguration, vm.OngletSelectionne);
        var livret = new DepotSqlite(Chemin("compte-2.db")).Charger()!;
        Assert.All(livret.Configuration.Charges, c => Assert.Equal(0m, c.Debit));
        Assert.Equal(Novembre, livret.Configuration.PremierMois);
    }

    [Fact]
    public void NouveauCompte_CopieLaConfiguration_SansLesMois()
    {
        var vm = Ouvrir();
        _dialogues.NouveauCompte = new DemandeNouveauCompte("Compte joint", CopierDe: RegistreComptes.NomPrincipal);

        vm.NouveauCompteCommand.Execute(null);

        var principal = new DepotSqlite(Chemin("compte.db")).Charger()!.Configuration;
        var joint = new DepotSqlite(Chemin("compte-2.db")).Charger()!;
        Assert.Empty(joint.Mois);
        Assert.Equal(principal.Charges, joint.Configuration.Charges);
        Assert.Equal(principal.Revenus, joint.Configuration.Revenus);
        Assert.Equal(principal.Regles, joint.Configuration.Regles);
        Assert.Equal(0m, joint.Configuration.SoldeInitial);
        Assert.Equal(Novembre, joint.Configuration.PremierMois);
        Assert.All(joint.Configuration.ComptesCumul, c => Assert.Equal(0m, c.MontantInitial));
    }

    [Fact]
    public void NouveauCompte_NomDejaPris_Refuse()
    {
        var vm = Ouvrir();
        _dialogues.NouveauCompte = new DemandeNouveauCompte("compte principal", CopierDe: null);

        vm.NouveauCompteCommand.Execute(null);

        Assert.Single(vm.NomsComptes);
        Assert.Contains("déjà", Assert.Single(_dialogues.Erreurs));
    }

    [Fact]
    public void ChangerDeCompte_OuvreSesDonnees_EtEstMemorise()
    {
        var vm = OuvrirAvecLivret();

        vm.CompteActif = RegistreComptes.NomPrincipal;

        Assert.False(vm.AucunMois);
        Assert.Equal("Novembre 2026", vm.TitreMois);
        Assert.Equal(RegistreComptes.NomPrincipal, RegistreComptes.Charger(_dossier).Actif.Nom);

        var rouvert = new MainViewModel(RegistreComptes.Charger(_dossier), _dialogues, Aujourdhui);
        Assert.Equal(RegistreComptes.NomPrincipal, rouvert.CompteActif);
    }

    [Fact]
    public void RenommerCompte()
    {
        var vm = OuvrirAvecLivret();
        _dialogues.Nom = "Livret Bleu";

        vm.RenommerCompteCommand.Execute(null);

        Assert.Equal("Livret Bleu", vm.CompteActif);
        Assert.Contains("Livret Bleu", RegistreComptes.Charger(_dossier).Comptes.Select(c => c.Nom));
    }

    [Fact]
    public void SupprimerCompte_OuvreUnAutreCompte()
    {
        var vm = OuvrirAvecLivret();

        vm.SupprimerCompteCommand.Execute(null);

        Assert.Equal(RegistreComptes.NomPrincipal, vm.CompteActif);
        Assert.False(vm.PlusieursComptes);
        Assert.False(File.Exists(Chemin("compte-2.db")));
    }

    [Fact]
    public void ToutEffacer_SupprimeTousLesComptes()
    {
        var vm = OuvrirAvecLivret();
        _dialogues.Reinitialisation = new DemandeReinitialisation(ChoixReinitialisation.ToutEffacer, CopieAvant: false);

        vm.ReinitialiserCommand.Execute(null);

        Assert.False(File.Exists(Chemin("compte-2.db")));
        Assert.False(File.Exists(Chemin(RegistreComptes.NomFichier)));
        Assert.Equal(RegistreComptes.NomPrincipal, vm.CompteActif);
        Assert.False(vm.PlusieursComptes);
        Assert.Empty(new DepotSqlite(Chemin("compte.db")).Charger()!.Mois);
    }

    // ---- Vue d'ensemble ----

    [Fact]
    public void VueDEnsemble_AdditionneLesComptes()
    {
        var vm = OuvrirAvecLivret();
        vm.Configuration.SoldeInitial = 2500m;
        vm.OngletSelectionne = MainViewModel.OngletEnsemble;

        var ensemble = vm.Ensemble!;
        var principal = ensemble.Lignes.Single(l => l.Nom == RegistreComptes.NomPrincipal);
        var livret = ensemble.Lignes.Single(l => l.Nom == "Livret A");
        Assert.True(livret.Actif);
        Assert.Equal(2500m, livret.SoldeActuel);
        Assert.Equal(1952.96m, principal.SoldeActuel);
        Assert.Equal(principal.SoldeActuel + livret.SoldeActuel, ensemble.TotalActuel);
        Assert.Equal(principal.SoldeFin + livret.SoldeFin, ensemble.TotalFin);
        Assert.Equal(13, ensemble.Courbe.Count);
        Assert.Equal("Novembre 2026", ensemble.MoisActuel);
        Assert.Equal("Novembre 2027", ensemble.MoisFin);
    }

    [Fact]
    public void Soldes_AvantLePremierMois_SoldeDeDepart()
    {
        var compte = new CompteBancaire(ConfigurationParDefaut.Creer(new PeriodeMois(2027, 1)));
        compte.Configuration.SoldeInitial = 100m;

        var soldes = VueEnsembleViewModel.Soldes(compte, new[] { new PeriodeMois(2026, 11), new PeriodeMois(2027, 1) });

        Assert.Equal((100m, false), soldes[0]);
        Assert.Equal(100m, soldes[1].Solde);
    }

    // ---- Import : bon compte bancaire ----

    [Fact]
    public void ReleveOfx_LitLeNumeroDeCompte()
    {
        var releve = ReleveOfx.LireTexte(ImportReleveTests.Ofx(ImportReleveTests.Ligne("DEBIT", "20261005", "-10.00", "X1", "CB TEST")));
        Assert.Equal("33333A", releve.Compte);
    }

    private string EcrireReleve()
    {
        var chemin = Chemin("releve.ofx");
        File.WriteAllText(chemin, ImportReleveTests.Ofx(ImportReleveTests.Ligne("DEBIT", "20261105", "-10.00", "X1", "CB TEST")));
        return chemin;
    }

    [Fact]
    public void Import_PremierReleve_RetientLeNumeroDuCompte()
    {
        var vm = Ouvrir();

        vm.OuvrirImport(EcrireReleve());
        vm.Import!.ValiderCommand.Execute(null);

        Assert.Equal("33333A", new DepotSqlite(Chemin("compte.db")).Charger()!.IdentifiantBanque);
    }

    [Fact]
    public void Import_ReleveDUnAutreCompte_ProposeDeLOuvrir()
    {
        var principal = ConfigurationParDefaut.CreerDemo();
        principal.IdentifiantBanque = "33333A";
        new DepotSqlite(Chemin("compte.db")).Enregistrer(principal);
        var vm = OuvrirAvecLivret();

        vm.OuvrirImport(EcrireReleve());

        Assert.Contains("correspond au compte « Compte principal »", _dialogues.DerniereConfirmation);
        Assert.Equal(RegistreComptes.NomPrincipal, vm.CompteActif);
        Assert.NotNull(vm.Import);
    }

    [Fact]
    public void Import_ReleveDUnAutreCompte_Refuse_RienNEstImporte()
    {
        var principal = ConfigurationParDefaut.CreerDemo();
        principal.IdentifiantBanque = "33333A";
        new DepotSqlite(Chemin("compte.db")).Enregistrer(principal);
        var vm = OuvrirAvecLivret();
        _dialogues.Reponse = false;

        vm.OuvrirImport(EcrireReleve());

        Assert.Equal("Livret A", vm.CompteActif);
        Assert.Null(vm.Import);
    }

    [Fact]
    public void Import_NumeroDifferent_DemandeConfirmation()
    {
        var vm = Ouvrir();
        vm.OuvrirImport(EcrireReleve());
        vm.Import!.ValiderCommand.Execute(null);
        _dialogues.Reponse = false;

        File.WriteAllText(Chemin("autre.ofx"), File.ReadAllText(Chemin("releve.ofx")).Replace("33333A", "99999Z"));
        vm.OuvrirImport(Chemin("autre.ofx"));

        Assert.Contains("n° …999Z", _dialogues.DerniereConfirmation);
        Assert.Null(vm.Import);
    }

    private sealed class FauxDialogues : IDialogues
    {
        public bool Reponse { get; set; } = true;
        public string DerniereConfirmation { get; private set; } = "";
        public List<string> Erreurs { get; } = new();
        public DemandeNouveauCompte? NouveauCompte { get; set; }
        public DemandeReinitialisation? Reinitialisation { get; set; }
        public string? Nom { get; set; }

        public bool Confirmer(string titre, string message)
        {
            DerniereConfirmation = message;
            return Reponse;
        }

        public void Erreur(string message) => Erreurs.Add(message);
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => null;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => null;
        public void OuvrirDossier(string dossier) { }
        public DemandeReinitialisation? ChoisirReinitialisation() => Reinitialisation;
        public DemandeNouveauCompte? DemanderNouveauCompte(IReadOnlyList<string> comptes, string compteActif) => NouveauCompte;
        public string? DemanderNom(string titre, string message, string valeur) => Nom;
    }
}
