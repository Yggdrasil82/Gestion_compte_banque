using System.Text;
using GestionCompte.Data.Documents;
using GestionCompte.Data.Nuage;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class DonneesDriveTests : IDisposable
{
    private const int Iterations = 1_000;
    private const string MotDePasse = "motdepasse-test";
    private readonly string _racine = Path.Combine(Path.GetTempPath(), $"gestioncompte-drive-{Guid.NewGuid():N}");

    public DonneesDriveTests() => Directory.CreateDirectory(_racine);

    public void Dispose()
    {
        if (Directory.Exists(_racine))
            Directory.Delete(_racine, true);
    }

    private string Dossier(string nom) => Path.Combine(_racine, nom);

    /// <summary>Faux Google Drive : un dossier.</summary>
    private StockageDossier Drive => new(Dossier("drive"));

    private string DonneesExemple()
    {
        var dossier = Dossier("donnees");
        Directory.CreateDirectory(Path.Combine(dossier, "Documents"));
        File.WriteAllText(Path.Combine(dossier, "comptes.json"), "{\"Comptes\":[]}");
        File.WriteAllText(Path.Combine(dossier, "compte.db"), "DONNEES-SECRETES-DU-COMPTE");
        File.WriteAllText(Path.Combine(dossier, "Documents", "documents.json"), "[]");
        return dossier;
    }

    private async Task<string> CreerAsync()
    {
        var coffre = new CoffreDonnees(Drive, Iterations);
        return await coffre.CreerAsync(DonneesExemple(), MotDePasse);
    }

    [Fact]
    public async Task Les_donnees_sont_chiffrees_puis_relues_avec_le_mot_de_passe()
    {
        await CreerAsync();

        var brut = File.ReadAllBytes(Path.Combine(Dossier("drive"), CoffreDonnees.NomDonnees));
        Assert.DoesNotContain("DONNEES-SECRETES", Encoding.UTF8.GetString(brut));

        var coffre = new CoffreDonnees(Drive, Iterations);
        Assert.True(await coffre.ExisteAsync());
        Assert.True(await coffre.DeverrouillerAsync(MotDePasse));
        var session = Dossier("session");
        await coffre.TelechargerAsync(session);
        Assert.Equal("DONNEES-SECRETES-DU-COMPTE", File.ReadAllText(Path.Combine(session, "compte.db")));
        Assert.Equal("[]", File.ReadAllText(Path.Combine(session, "Documents", "documents.json")));
    }

    [Fact]
    public async Task Un_mauvais_mot_de_passe_est_refuse()
    {
        await CreerAsync();
        var coffre = new CoffreDonnees(Drive, Iterations);
        Assert.False(await coffre.DeverrouillerAsync("pas-le-bon"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coffre.TelechargerAsync(Dossier("session")));
    }

    [Fact]
    public async Task La_cle_de_secours_permet_de_choisir_un_nouveau_mot_de_passe()
    {
        var secours = await CreerAsync();

        Assert.True(await new CoffreDonnees(Drive, Iterations).RecupererAsync(secours.ToLowerInvariant(), "nouveau-mot"));
        Assert.False(await new CoffreDonnees(Drive, Iterations).DeverrouillerAsync(MotDePasse));
        Assert.True(await new CoffreDonnees(Drive, Iterations).DeverrouillerAsync("nouveau-mot"));
        Assert.False(await new CoffreDonnees(Drive, Iterations).RecupererAsync("AAAAA-BBBBB", "autre-mot-de-passe"));
    }

    [Fact]
    public async Task Le_mot_de_passe_peut_etre_change()
    {
        await CreerAsync();
        var coffre = new CoffreDonnees(Drive, Iterations);
        await coffre.DeverrouillerAsync(MotDePasse);
        await coffre.ChangerMotDePasseAsync("change-123");
        Assert.True(await new CoffreDonnees(Drive, Iterations).DeverrouillerAsync("change-123"));
        Assert.False(await new CoffreDonnees(Drive, Iterations).DeverrouillerAsync(MotDePasse));
    }

    [Fact]
    public void Une_archive_ne_peut_pas_ecrire_hors_du_dossier()
    {
        using var memoire = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(memoire, System.IO.Compression.ZipArchiveMode.Create, true))
        using (var ecriture = new StreamWriter(archive.CreateEntry("../dehors.txt").Open()))
            ecriture.Write("x");

        Assert.Throws<InvalidDataException>(() => CoffreDonnees.Paquet.Deballer(memoire.ToArray(), Dossier("session")));
        Assert.False(File.Exists(Path.Combine(_racine, "dehors.txt")));
    }

    [Fact]
    public async Task La_copie_du_pc_de_confiance_s_ouvre_sans_google_drive()
    {
        await CreerAsync();
        var coffre = new CoffreDonnees(Drive, Iterations) { Copie = new StockageDossier(Dossier("copie")) };
        await coffre.DeverrouillerAsync(MotDePasse);
        await coffre.TelechargerAsync(Dossier("session"));

        var horsLigne = new CoffreDonnees(new StockageDossier(Dossier("copie")), Iterations);
        Assert.True(await horsLigne.DeverrouillerAsync(MotDePasse));
        await horsLigne.TelechargerAsync(Dossier("session2"));
        Assert.Equal("DONNEES-SECRETES-DU-COMPTE", File.ReadAllText(Path.Combine(Dossier("session2"), "compte.db")));
    }

    private async Task<(CoffreDonnees Coffre, SessionDrive Session)> OuvrirSessionAsync(string nom, Func<DateTime> horloge)
    {
        var coffre = new CoffreDonnees(Drive, Iterations);
        await coffre.DeverrouillerAsync(MotDePasse);
        await coffre.TelechargerAsync(Dossier(nom));
        var session = new SessionDrive(Dossier(nom), coffre, false, nom, "PC-" + nom, horloge);
        await session.SignalerAsync();
        return (coffre, session);
    }

    [Fact]
    public async Task Une_modification_est_envoyee_dans_google_drive()
    {
        await CreerAsync();
        var maintenant = new DateTime(2026, 11, 15, 10, 0, 0);
        var (_, session) = await OuvrirSessionAsync("pc1", () => maintenant);

        File.WriteAllText(Path.Combine(session.Dossier, "compte.db"), "MODIFIE");
        Assert.True(session.EnvoiEnAttente);
        Assert.True(await session.SynchroniserAsync());
        Assert.False(session.EnvoiEnAttente);

        var autre = new CoffreDonnees(Drive, Iterations);
        await autre.DeverrouillerAsync(MotDePasse);
        await autre.TelechargerAsync(Dossier("verif"));
        Assert.Equal("MODIFIE", File.ReadAllText(Path.Combine(Dossier("verif"), "compte.db")));
    }

    [Fact]
    public async Task Ouverte_sur_un_autre_pc_la_session_passe_en_lecture_seule()
    {
        await CreerAsync();
        var maintenant = new DateTime(2026, 11, 15, 10, 0, 0);
        var (_, premiere) = await OuvrirSessionAsync("pc1", () => maintenant);
        // Le second PC voit le verrou du premier.
        var verrou = await new CoffreDonnees(Drive, Iterations).LireVerrouAsync();
        Assert.True(verrou!.BloqueAutreSession("pc2", maintenant));
        Assert.Equal("PC-pc1", verrou.Poste);

        // « Ouvrir quand même » sur le second PC : le premier ne doit plus rien envoyer.
        var (_, seconde) = await OuvrirSessionAsync("pc2", () => maintenant);
        VerrouDonnees? signale = null;
        premiere.OuverteAilleurs += (_, v) => signale = v;
        File.WriteAllText(Path.Combine(premiere.Dossier, "compte.db"), "ECRASERAIT");
        Assert.False(await premiere.SynchroniserAsync());
        Assert.True(premiere.LectureSeule);
        Assert.Equal("PC-pc2", signale!.Poste);

        await seconde.TerminerAsync();
        var autre = new CoffreDonnees(Drive, Iterations);
        await autre.DeverrouillerAsync(MotDePasse);
        await autre.TelechargerAsync(Dossier("verif"));
        Assert.Equal("DONNEES-SECRETES-DU-COMPTE", File.ReadAllText(Path.Combine(Dossier("verif"), "compte.db")));
    }

    [Fact]
    public async Task La_fermeture_libere_le_verrou_et_un_verrou_ancien_est_ignore()
    {
        await CreerAsync();
        var maintenant = new DateTime(2026, 11, 15, 10, 0, 0);
        var (_, session) = await OuvrirSessionAsync("pc1", () => maintenant);
        Assert.True(await session.TerminerAsync());
        Assert.False((await new CoffreDonnees(Drive, Iterations).LireVerrouAsync())!.BloqueAutreSession("pc2", maintenant));

        var ancien = new VerrouDonnees("PC", "x", maintenant, maintenant, false);
        Assert.True(ancien.BloqueAutreSession("y", maintenant.AddMinutes(10)));
        Assert.False(ancien.BloqueAutreSession("y", maintenant.AddMinutes(20)));
        Assert.False(ancien.BloqueAutreSession("x", maintenant));
    }

    [Fact]
    public void Le_reglage_de_demarrage_distingue_pc_de_confiance_et_cle_usb()
    {
        var pc = Dossier("pc");
        var usb = Dossier("usb");
        Directory.CreateDirectory(usb);
        Assert.False(new DemarrageDrive(pc, usb).Drive);

        DemarrageDrive.PreparerPortable(usb);
        var portable = new DemarrageDrive(pc, usb);
        Assert.True(portable.Drive);
        Assert.False(portable.Confiance);

        portable.EnregistrerPC(drive: true, confiance: true);
        Directory.CreateDirectory(portable.DossierCopie);
        Assert.True(new DemarrageDrive(pc, null).Confiance);

        new DemarrageDrive(pc, usb).EnregistrerPC(drive: true, confiance: false);
        Assert.False(Directory.Exists(portable.DossierCopie));
        Assert.False(new DemarrageDrive(pc, null).Drive);
    }

    [Fact]
    public void Les_sessions_abandonnees_sont_effacees()
    {
        var temp = Dossier("temp");
        Directory.CreateDirectory(DemarrageDrive.DossierSession(temp, 111));
        Directory.CreateDirectory(DemarrageDrive.DossierSession(temp, 222));
        DemarrageDrive.NettoyerSessions(temp, numero => numero == 222);
        Assert.False(Directory.Exists(DemarrageDrive.DossierSession(temp, 111)));
        Assert.True(Directory.Exists(DemarrageDrive.DossierSession(temp, 222)));
    }

    [Fact]
    public void Les_secrets_voyagent_avec_les_donnees_sauf_la_connexion_google()
    {
        var connexion = new SecretsEnMemoire();
        var dossier = Dossier("secrets");
        Directory.CreateDirectory(dossier);
        var secrets = new SecretsCombines(connexion, new SecretsFichier(dossier));
        secrets.Ecrire(CompteGoogle.SecretJeton, "jeton");
        secrets.Ecrire("ia-gemini", "cle-ia");

        Assert.Equal("jeton", connexion.Lire(CompteGoogle.SecretJeton));
        var relu = new SecretsFichier(dossier);
        Assert.Equal("cle-ia", relu.Lire("ia-gemini"));
        Assert.Null(relu.Lire(CompteGoogle.SecretJeton));
    }

    private static CompteGoogle GoogleConnecte(ISecretsLocaux secrets, IDialogues dialogues)
    {
        secrets.Ecrire(CompteGoogle.SecretJeton, "jeton-test");
        secrets.Ecrire(CompteGoogle.SecretAdresse, "prenom.nom@exemple.fr");
        return new CompteGoogle(secrets, dialogues, integres: new IdentifiantsGoogle("id", "secret"));
    }

    [Fact]
    public async Task Activer_puis_ouvrir_depuis_un_autre_pc()
    {
        var dialogues = new Dialogues { MotDePasse = MotDePasse };
        var secretsPC = new SecretsEnMemoire();
        var google = GoogleConnecte(secretsPC, dialogues);
        secretsPC.Ecrire("ia-gemini", "cle-ia");
        var demarrage = new DemarrageDrive(Dossier("pc"), null);
        var carte = new DonneesDriveViewModel(google, dialogues, demarrage, null, DonneesExemple(), Dossier("local"),
            secretsPC, secretsPC, null, () => Drive, Iterations);
        var redemarrage = false;
        carte.RedemarrageDemande += (_, _) => redemarrage = true;

        await carte.ActiverCommand.ExecuteAsync(null);

        Assert.True(redemarrage);
        Assert.NotNull(dialogues.CleSecours);
        Assert.True(new DemarrageDrive(Dossier("pc"), null).Confiance);

        // Autre PC (clé USB) : connexion en mémoire, mot de passe, données déchiffrées avec les clés des IA.
        var autrePC = new DemarrageDrive(Dossier("autre-pc"), null);
        var ouverture = new OuvertureDriveViewModel(GoogleConnecte(new SecretsEnMemoire(), dialogues), dialogues, autrePC,
            Dossier("session"), "PC-AMI", () => Drive, Iterations);
        Assert.False(await ouverture.OuvrirAsync("mauvais-mot"));
        Assert.Equal("Mot de passe incorrect.", ouverture.Etat);
        Assert.True(await ouverture.OuvrirAsync(MotDePasse));
        var session = ouverture.Session!;
        Assert.False(session.LectureSeule);
        Assert.Equal("DONNEES-SECRETES-DU-COMPTE", File.ReadAllText(Path.Combine(session.Dossier, "compte.db")));
        var secrets = new SecretsFichier(session.Dossier);
        Assert.Equal("cle-ia", secrets.Lire("ia-gemini"));
        Assert.Null(secrets.Lire(CompteGoogle.SecretJeton));
        // PC qui n'est pas de confiance : aucun réglage ni copie n'y reste.
        Assert.False(Directory.Exists(autrePC.DossierCopie));
        Assert.False(File.Exists(Path.Combine(Dossier("autre-pc"), DemarrageDrive.NomFichierPC)));

        // Troisième ouverture pendant que le PC-AMI est ouvert : choix « Lecture seule ».
        dialogues.Option = 0;
        var troisieme = new OuvertureDriveViewModel(GoogleConnecte(new SecretsEnMemoire(), dialogues), dialogues, autrePC,
            Dossier("session3"), "PC-BUREAU", () => Drive, Iterations);
        Assert.True(await troisieme.OuvrirAsync(MotDePasse));
        Assert.True(troisieme.Session!.LectureSeule);
        Assert.Contains("PC-AMI", dialogues.MessageOption);
    }

    [Fact]
    public async Task Remettre_les_donnees_sur_ce_pc()
    {
        await CreerAsync();
        var dialogues = new Dialogues();
        var connexion = new SecretsEnMemoire();
        var google = GoogleConnecte(connexion, dialogues);
        var coffre = new CoffreDonnees(Drive, Iterations);
        await coffre.DeverrouillerAsync(MotDePasse);
        await coffre.TelechargerAsync(Dossier("session"));
        new SecretsFichier(Dossier("session")).Ecrire("ia-groq", "cle-groq");
        var session = new SessionDrive(Dossier("session"), coffre, false, "s", "PC");
        var local = Dossier("local");
        Directory.CreateDirectory(local);
        File.WriteAllText(Path.Combine(local, "ancien.txt"), "ancien");
        var secretsPC = new SecretsEnMemoire();
        var demarrage = new DemarrageDrive(Dossier("pc"), null);
        demarrage.EnregistrerPC(true, true);
        var carte = new DonneesDriveViewModel(google, dialogues, demarrage, session, session.Dossier, local, secretsPC, connexion, null,
            () => Drive, Iterations, () => new DateTime(2026, 11, 15, 10, 30, 0));

        carte.ArreterCommand.Execute(null);

        Assert.Equal("DONNEES-SECRETES-DU-COMPTE", File.ReadAllText(Path.Combine(local, "compte.db")));
        Assert.False(File.Exists(Path.Combine(local, SecretsFichier.NomFichier)));
        Assert.True(File.Exists(Path.Combine(local + " (avant le 2026-11-15 10-30)", "ancien.txt")));
        Assert.Equal("cle-groq", secretsPC.Lire("ia-groq"));
        Assert.Equal("jeton-test", secretsPC.Lire(CompteGoogle.SecretJeton));
        Assert.False(new DemarrageDrive(Dossier("pc"), null).Drive);
    }

    private sealed class Dialogues : IDialogues
    {
        public string? MotDePasse { get; set; }
        public string? CleSecours { get; private set; }
        public int? Option { get; set; }
        public string MessageOption { get; private set; } = "";

        public bool Confirmer(string titre, string message) => true;
        public void Erreur(string message) => throw new InvalidOperationException(message);
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => null;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => null;
        public void OuvrirDossier(string dossier) { }
        public string? DemanderMotDePasse(string titre, string message, bool confirmer) => MotDePasse;
        public void AfficherCleSecours(string cle) => CleSecours = cle;

        public int? ChoisirOption(string titre, string message, IReadOnlyList<string> options)
        {
            MessageOption = message;
            return Option;
        }
    }
}
