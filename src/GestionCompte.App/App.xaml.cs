using System.Windows.Controls;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using GestionCompte.Core;
using GestionCompte.Data;
using GestionCompte.Presentation;

namespace GestionCompte.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!e.Args.Contains("--captures"))
            JournalDemarrage.Commencer();

        // Infobulles affichées tant que la souris reste dessus (les explications des calculs sont longues à lire).
        ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(int.MaxValue));

        // Toute l'application en français (dates, nombres), quelle que soit la langue de Windows.
        CultureInfo.DefaultThreadCurrentCulture = Montants.Francais;
        CultureInfo.DefaultThreadCurrentUICulture = Montants.Francais;
        Thread.CurrentThread.CurrentCulture = Montants.Francais;
        Thread.CurrentThread.CurrentUICulture = Montants.Francais;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(Montants.Francais.IetfLanguageTag)));

        // Mode utilisé par GitHub Actions : ouvre des données d'exemple, enregistre des captures d'écran puis quitte.
        var indexCaptures = Array.IndexOf(e.Args, "--captures");
        if (indexCaptures >= 0 && indexCaptures + 1 < e.Args.Length)
        {
            Captures.Lancer(this, e.Args[indexCaptures + 1]);
            return;
        }

        DispatcherUnhandledException += ErreurNonPrevue;
        try
        {
            Demarrer();
        }
        catch (Exception ex)
        {
            // Démarrage interrompu : l'erreur est montrée au lieu d'un processus qui tourne sans fenêtre.
            JournalDemarrage.Noter($"Échec du démarrage : {ex}");
            MessageBox.Show($"Mon Budget n'a pas pu démarrer :\n\n{ex.Message}\n\nDétails : {JournalDemarrage.Chemin}",
                "Gestion Compte", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void Demarrer()
    {
        // Données dans Google Drive (réglage de ce PC, ou fichier posé à côté de l'exe sur une clé USB).
        JournalDemarrage.Noter("Lecture du réglage Google Drive");
        var demarrage = new DemarrageDrive(DossierPC, Path.GetDirectoryName(Environment.ProcessPath));
        if (demarrage.Drive)
        {
            JournalDemarrage.Noter("Données dans Google Drive : fenêtre d'ouverture");
            OuvrirDepuisDrive(demarrage);
            return;
        }

        // Apparence mémorisée à côté des données (Documents\GestionCompte\preferences.json).
        JournalDemarrage.Noter($"Lecture des préférences dans {DossierLocal}");
        var apparence = new ApparenceViewModel(Path.Combine(DossierLocal, "preferences.json"));
        Themes.Appliquer(this, apparence.Ambiance, apparence.Sombre);
        apparence.Changee += (_, _) => Themes.Appliquer(this, apparence.Ambiance, apparence.Sombre);

        // Identifiants (connexion Google, clés des IA…) : chiffrés par Windows, propres à ce PC.
        JournalDemarrage.Noter("Lecture des identifiants de ce PC");
        var secretsPC = new SecretsWindows(Path.Combine(DossierPC, "secrets"));
        void Lancer() => Ouvrir(apparence, RegistreComptes.Charger(DossierLocal), secretsPC, secretsPC, secretsPC, demarrage, null);

        // Animation du canard (réglable dans Configuration › Modules), puis ouverture de l'application.
        if (apparence.AnimationDemarrage)
        {
            JournalDemarrage.Noter("Animation du canard");
            var accueil = new FenetreAccueil();
            accueil.Terminee += (_, _) => Lancer();
            accueil.Show();
        }
        else
            Lancer();
    }

    /// <summary>Dossier habituel des données sur ce PC : Documents\GestionCompte.</summary>
    private static string DossierLocal => Path.GetDirectoryName(DepotSqlite.CheminParDefaut)!;

    /// <summary>Dossier de l'application sur ce PC (%LOCALAPPDATA%\GestionCompte) : secrets, réglage Google Drive.</summary>
    private static string DossierPC =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GestionCompte");

    private SessionDrive? _session;
    private bool _sessionTerminee;

    /// <summary>Connexion à Google et mot de passe, puis données téléchargées et déchiffrées dans un dossier de travail.</summary>
    private void OuvrirDepuisDrive(DemarrageDrive demarrage)
    {
        DemarrageDrive.NettoyerSessions(Path.GetTempPath(), ProcessusActif);
        var defaut = new ApparenceViewModel(null);
        Themes.Appliquer(this, defaut.Ambiance, defaut.Sombre);

        var secretsPC = new SecretsWindows(Path.Combine(DossierPC, "secrets"));
        ISecretsLocaux connexion = demarrage.Confiance ? secretsPC : new SecretsEnMemoire();
        var dialogues = new Dialogues();
        var google = new CompteGoogle(connexion, dialogues);
        var ouverture = new OuvertureDriveViewModel(google, dialogues, demarrage,
            DemarrageDrive.DossierSession(Path.GetTempPath(), Environment.ProcessId), Environment.MachineName);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var fenetre = new FenetreOuvertureDrive(ouverture);
        SessionDrive? ouverte = null;
        fenetre.Ouverte += (_, session) => ouverte = session;
        fenetre.Closed += (_, _) =>
        {
            if (ouverte is null)
            {
                Shutdown();
                return;
            }

            // Connexion Google : gardée sur un PC de confiance, oubliée à la fermeture sinon.
            var noms = new[] { CompteGoogle.SecretJeton, CompteGoogle.SecretPortees, CompteGoogle.SecretAdresse };
            ISecretsLocaux connexionSession = connexion;
            if (ouverture.Confiance && connexion != secretsPC)
            {
                foreach (var nom in noms)
                    secretsPC.Ecrire(nom, connexion.Lire(nom));
                connexionSession = secretsPC;
            }
            else if (!ouverture.Confiance && connexion == secretsPC)
            {
                connexionSession = new SecretsEnMemoire();
                foreach (var nom in noms)
                {
                    connexionSession.Ecrire(nom, secretsPC.Lire(nom));
                    secretsPC.Ecrire(nom, null);
                }
            }

            _session = ouverte;
            var apparence = new ApparenceViewModel(Path.Combine(ouverte.Dossier, "preferences.json"));
            Themes.Appliquer(this, apparence.Ambiance, apparence.Sombre);
            apparence.Changee += (_, _) => Themes.Appliquer(this, apparence.Ambiance, apparence.Sombre);
            var secrets = new SecretsCombines(connexionSession, new SecretsFichier(ouverte.Dossier));
            var demarrageSession = new DemarrageDrive(DossierPC, Path.GetDirectoryName(Environment.ProcessPath));
            Ouvrir(apparence, RegistreComptes.Charger(ouverte.Dossier), secrets, secretsPC, connexionSession, demarrageSession, ouverte);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        };
        fenetre.Show();
        JournalDemarrage.Noter("Fenêtre d'ouverture Google Drive affichée");
        fenetre.ContentRendered += (_, _) => JournalDemarrage.Terminer("Fenêtre d'ouverture Google Drive dessinée");
    }

    private static bool ProcessusActif(int numero)
    {
        try
        {
            using var processus = System.Diagnostics.Process.GetProcessById(numero);
            return !processus.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <param name="secrets">Secrets de l'application (avec les données dans Google Drive : connexion du PC + fichier des données).</param>
    /// <param name="secretsPC">Secrets chiffrés par Windows sur ce PC.</param>
    /// <param name="connexion">Secrets de la connexion Google de cette session.</param>
    /// <param name="session">Session Google Drive ; null quand les données sont sur ce PC.</param>
    private void Ouvrir(ApparenceViewModel apparence, RegistreComptes registre, ISecretsLocaux secrets, ISecretsLocaux secretsPC,
        ISecretsLocaux connexion, DemarrageDrive demarrage, SessionDrive? session)
    {
        MainViewModel vm;
        JournalDemarrage.Noter($"Chargement des données depuis {registre.Chemin(registre.Actif)}");
        try
        {
            vm = new MainViewModel(registre, new Dialogues(), DateTime.Today, apparence, secrets);
        }
        catch (Exception ex)
        {
            JournalDemarrage.Noter($"Données illisibles : {ex}");
            MessageBox.Show(
                $"Les données n'ont pas pu être chargées depuis :\n{registre.Chemin(registre.Actif)}\n\n{ex.Message}\n\n" +
                "Le fichier n'a pas été modifié.",
                "Gestion Compte", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        vm.DonneesDrive = new DonneesDriveViewModel(vm.Google, new Dialogues(), demarrage, session, registre.Dossier, DossierLocal,
            secretsPC, connexion, Environment.ProcessPath);

        JournalDemarrage.Noter("Création de la fenêtre principale");
        var fenetre = new MainWindow { DataContext = vm };
        fenetre.SourceInitialized += (_, _) => Themes.BarreDeTitreSombre(fenetre, apparence.Sombre);
        MainWindow = fenetre;

        if (session is not null)
        {
            void Titre() => fenetre.Title = session.LectureSeule ? "Gestion Compte Banque — lecture seule" : "Gestion Compte Banque";
            Titre();
            session.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SessionDrive.LectureSeule))
                    Titre();
            };
            session.OuverteAilleurs += (_, verrou) => new Dialogues().Information("Ouverte sur un autre PC",
                $"Mon Budget vient d'être ouvert sur « {verrou.Poste} ». Pour ne rien écraser, cette fenêtre passe en lecture seule : " +
                "ses prochaines modifications ne seront pas envoyées dans Google Drive.");
            // Fermeture : dernier envoi dans Google Drive avant de quitter.
            fenetre.Closing += async (_, e) =>
            {
                if (_sessionTerminee)
                    return;
                e.Cancel = true;
                if (await TerminerSessionAsync(fenetre))
                    fenetre.Close();
            };
            fenetre.ContentRendered += (_, _) =>
            {
                if (session.LectureSeule)
                    new Dialogues().Information("Lecture seule", session.Etat);
                session.Demarrer(TimeSpan.FromSeconds(10));
            };
        }
        fenetre.ContentRendered += (_, _) => JournalDemarrage.Terminer("Fenêtre principale dessinée");
        fenetre.Show();
        JournalDemarrage.Noter("Fenêtre principale affichée");

        vm.DonneesDrive.RedemarrageDemande += async (_, _) => await RedemarrerAsync(fenetre, Environment.ProcessPath);

        // Mises à jour : l'exe de la version précédente est supprimé, puis une nouvelle version est cherchée sur GitHub
        // une fois la fenêtre affichée (sans rien dire si l'application est à jour ou hors ligne).
        if (Environment.ProcessPath is { } exe)
            ServiceMisesAJour.NettoyerAncienneVersion(exe);
        vm.MisesAJour.RedemarrageDemande += async (_, chemin) => await RedemarrerAsync(fenetre, chemin);
        fenetre.ContentRendered += async (_, _) => await vm.MisesAJour.VerifierAuDemarrageAsync();
    }

    /// <summary>Envoie les dernières modifications dans Google Drive et lève le verrou ; false si l'utilisateur annule la fermeture.</summary>
    private async Task<bool> TerminerSessionAsync(Window fenetre)
    {
        if (_session is null || _sessionTerminee)
            return true;
        var titre = fenetre.Title;
        fenetre.IsEnabled = false;
        fenetre.Title = "Envoi des données dans Google Drive…";
        try
        {
            while (!await _session.TerminerAsync())
            {
                var choix = new Dialogues().ChoisirOption("Envoi impossible",
                    $"Les dernières modifications n'ont pas pu être envoyées dans Google Drive.\n\n{_session.Etat}",
                    new[] { "Réessayer", "Quitter sans envoyer", "Annuler" });
                if (choix == 1)
                    break;
                if (choix != 0)
                    return false;
            }
            _sessionTerminee = true;
            return true;
        }
        finally
        {
            fenetre.IsEnabled = true;
            fenetre.Title = titre;
        }
    }

    private async Task RedemarrerAsync(Window fenetre, string? exe)
    {
        if (!await TerminerSessionAsync(fenetre))
            return;
        if (exe is not null)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Données déchiffrées de la session : effacées de ce PC (la copie chiffrée d'un PC de confiance reste).
        _session?.Effacer();
        base.OnExit(e);
    }

    private void ErreurNonPrevue(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        JournalDemarrage.Noter($"Erreur inattendue : {e.Exception}");
        MessageBox.Show($"Une erreur inattendue s'est produite :\n\n{e.Exception.Message}",
            "Gestion Compte", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
