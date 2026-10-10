using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Data.Documents;
using GestionCompte.Data.Nuage;

namespace GestionCompte.Presentation;

/// <summary>
/// Carte « Stocker dans Google Drive » de Configuration › Données : mise des données dans Google Drive (chiffrées), PC de
/// confiance, mot de passe, clé USB, et retour des données sur ce PC.
/// </summary>
public sealed partial class DonneesDriveViewModel : ObservableObject
{
    private readonly CompteGoogle _google;
    private readonly IDialogues _dialogues;
    private readonly DemarrageDrive _demarrage;
    private readonly string _dossierDonnees;
    private readonly string _dossierLocal;
    private readonly ISecretsLocaux _secretsPC;
    private readonly ISecretsLocaux _connexion;
    private readonly Func<IStockageDocuments> _distant;
    private readonly int _iterations;
    private readonly string? _exe;
    private readonly Func<DateTime> _horloge;
    private bool _chargement = true;

    /// <param name="dossierDonnees">Dossier des données ouvert (celui de ce PC, ou le dossier de travail de la session Drive).</param>
    /// <param name="dossierLocal">Dossier habituel des données sur ce PC (Documents\GestionCompte).</param>
    /// <param name="secretsPC">Secrets gardés sur ce PC (clés des IA… quand les données sont sur ce PC).</param>
    /// <param name="connexion">Secrets de la connexion Google utilisés par cette session.</param>
    /// <param name="exe">Chemin de l'application (copiée sur une clé USB) ; null s'il est inconnu.</param>
    public DonneesDriveViewModel(CompteGoogle google, IDialogues dialogues, DemarrageDrive demarrage, SessionDrive? session,
        string dossierDonnees, string dossierLocal, ISecretsLocaux secretsPC, ISecretsLocaux connexion, string? exe,
        Func<IStockageDocuments>? distant = null, int iterations = ChiffrementDocuments.IterationsParDefaut,
        Func<DateTime>? horloge = null)
    {
        _google = google;
        _dialogues = dialogues;
        _demarrage = demarrage;
        Session = session;
        _dossierDonnees = dossierDonnees;
        _dossierLocal = dossierLocal;
        _secretsPC = secretsPC;
        _connexion = connexion;
        _exe = exe;
        _distant = distant ?? (() => new StockageGoogleDrive(google.Http, google.JetonAsync, CoffreDonnees.NomDossierDrive));
        _iterations = iterations;
        _horloge = horloge ?? (() => DateTime.Now);
        Confiance = demarrage.Confiance;
        _chargement = false;
        if (session is not null)
            session.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(SessionDrive.Etat) or nameof(SessionDrive.LectureSeule))
                    OnPropertyChanged(nameof(Etat));
            };
    }

    public SessionDrive? Session { get; }

    /// <summary>Les données de cette session viennent de Google Drive.</summary>
    public bool Actif => Session is not null;

    public bool Inactif => !Actif;

    public string Etat => _etape ?? Session?.Etat ?? $"Les données sont sur ce PC, dans {_dossierLocal}.";

    /// <summary>Étape en cours (connexion, envoi…), affichée à la place de l'état.</summary>
    private string? _etape;

    private void Etape(string? etape)
    {
        _etape = etape;
        OnPropertyChanged(nameof(Etat));
    }

    /// <summary>PC de confiance : connexion Google gardée et copie chiffrée pour une ouverture hors connexion.</summary>
    [ObservableProperty] private bool _confiance;

    /// <summary>L'application doit redémarrer pour ouvrir les données à leur nouvel emplacement.</summary>
    public event EventHandler? RedemarrageDemande;

    partial void OnConfianceChanged(bool value)
    {
        if (_chargement || !Actif)
            return;
        _demarrage.EnregistrerPC(drive: true, value);
        foreach (var nom in new[] { CompteGoogle.SecretJeton, CompteGoogle.SecretPortees, CompteGoogle.SecretAdresse })
            _secretsPC.Ecrire(nom, value ? _connexion.Lire(nom) : null);
        if (Session?.Distant is { } distant)
            distant.Copie = value ? new StockageDossier(_demarrage.DossierCopie) : null;
    }

    /// <summary>Met les données de ce PC dans Google Drive (ou ouvre celles qui y sont déjà).</summary>
    [RelayCommand]
    private async Task Activer()
    {
        try
        {
            await ActiverAsync();
        }
        finally
        {
            Etape(null);
        }
    }

    private async Task ActiverAsync()
    {
        if (!_google.Connecte)
        {
            Etape("Terminez la connexion à Google dans votre navigateur (la page vient de s'ouvrir)…");
            try
            {
                if (!await _google.ConnecterAsync())
                    return;
            }
            catch (Exception e) when (e is HttpRequestException or InvalidOperationException or OperationCanceledException
                                          or TimeoutException or IOException or System.Text.Json.JsonException)
            {
                _dialogues.Erreur($"La connexion à Google a échoué :\n\n{e.Message}");
                return;
            }
        }

        try
        {
            Etape("Recherche de données dans Google Drive…");
            var coffre = new CoffreDonnees(_distant(), _iterations);
            if (await coffre.ExisteAsync())
            {
                if (!_dialogues.Confirmer("Données déjà dans Google Drive",
                        $"Des données Mon Budget sont déjà dans le Google Drive de {_google.Adresse}.\n\n" +
                        $"Les ouvrir sur ce PC ? Les données actuelles de ce PC ne sont pas envoyées : elles restent dans {_dossierLocal}."))
                    return;
                _demarrage.EnregistrerPC(drive: true, confiance: true);
                RedemarrageDemande?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!_dialogues.Confirmer("Stocker dans Google Drive",
                    $"Toutes vos données (comptes, préférences, documents, clés des IA) seront chiffrées puis rangées dans le dossier " +
                    $"« {CoffreDonnees.NomDossierDrive} » du Google Drive de {_google.Adresse}.\n\n" +
                    "À chaque lancement, l'application les téléchargera après la saisie de votre mot de passe, sur ce PC ou sur un autre.\n\nContinuer ?"))
                return;
            var motDePasse = _dialogues.DemanderMotDePasse("Mot de passe des données",
                $"Choisissez le mot de passe qui protège vos données (au moins {CoffreDonnees.LongueurMinimale} caractères). " +
                "Il vous sera demandé à chaque ouverture.", confirmer: true);
            if (motDePasse is null)
                return;

            // Copie de travail : les données de ce PC et les secrets qui doivent voyager avec elles.
            var preparation = Path.Combine(Path.GetTempPath(), $"MonBudget-envoi-{Guid.NewGuid():N}");
            string secours;
            try
            {
                CopierDossier(_dossierDonnees, preparation);
                var secrets = new SecretsFichier(preparation);
                foreach (var nom in _secretsPC.Noms().Where(n => !SecretsCombines.EstConnexionGoogle(n)))
                    secrets.Ecrire(nom, _secretsPC.Lire(nom));
                Etape("Chiffrement et envoi des données dans Google Drive…");
                secours = await coffre.CreerAsync(preparation, motDePasse);
            }
            finally
            {
                if (Directory.Exists(preparation))
                    Directory.Delete(preparation, recursive: true);
            }

            _dialogues.AfficherCleSecours(secours);
            _demarrage.EnregistrerPC(drive: true, confiance: true);
            _dialogues.Information("Données dans Google Drive",
                "C'est fait. L'application va redémarrer et ouvrir vos données depuis Google Drive.\n\n" +
                $"Le dossier {_dossierLocal} reste sur ce PC, non chiffré : vous pourrez le supprimer quand tout fonctionnera.");
            RedemarrageDemande?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
                                      or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {
            _dialogues.Erreur($"Les données n'ont pas pu être mises dans Google Drive :\n\n{e.Message}");
        }
    }

    [RelayCommand]
    private async Task ChangerMotDePasse()
    {
        if (Session?.Distant is not { } distant)
        {
            _dialogues.Erreur("Le mot de passe se change quand Google Drive est joignable.");
            return;
        }
        var nouveau = _dialogues.DemanderMotDePasse("Nouveau mot de passe",
            "Nouveau mot de passe de vos données (la clé de secours ne change pas).", confirmer: true);
        if (nouveau is null)
            return;
        try
        {
            await distant.ChangerMotDePasseAsync(nouveau);
            _dialogues.Information("Mot de passe changé", "Le nouveau mot de passe sera demandé au prochain lancement.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or ArgumentException
                                      or InvalidOperationException)
        {
            _dialogues.Erreur($"Le mot de passe n'a pas pu être changé :\n\n{e.Message}");
        }
    }

    /// <summary>Copie l'application sur une clé USB (ou un autre dossier) : lancée sur n'importe quel PC, elle ouvre les données de Google Drive.</summary>
    [RelayCommand]
    private void PreparerCleUsb()
    {
        if (_exe is null || !File.Exists(_exe))
        {
            _dialogues.Erreur("L'emplacement de l'application est inconnu.");
            return;
        }
        var dossier = _dialogues.ChoisirDossier("Choisissez la clé USB (ou le dossier) où copier l'application");
        if (dossier is null)
            return;
        try
        {
            var destination = Path.Combine(dossier, Path.GetFileName(_exe));
            if (!string.Equals(Path.GetFullPath(destination), Path.GetFullPath(_exe), StringComparison.OrdinalIgnoreCase))
                File.Copy(_exe, destination, overwrite: true);
            DemarrageDrive.PreparerPortable(dossier);
            _dialogues.Information("Clé USB prête",
                $"L'application est copiée dans {dossier}.\n\nSur n'importe quel PC, lancez-la : elle demande votre compte Google et votre " +
                "mot de passe, puis ouvre vos données. Sur un PC qui n'est pas le vôtre, rien n'y reste à la fermeture " +
                "(pensez seulement à vous déconnecter de Google dans le navigateur).");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _dialogues.Erreur($"L'application n'a pas pu être copiée :\n\n{e.Message}");
        }
    }

    /// <summary>Remet les données sur ce PC (non chiffrées) ; celles de Google Drive ne sont pas supprimées.</summary>
    [RelayCommand]
    private void Arreter()
    {
        if (Session is null)
            return;
        if (!_dialogues.Confirmer("Remettre les données sur ce PC",
                $"Vos données seront copiées dans {_dossierLocal} et l'application ne les ouvrira plus depuis Google Drive.\n\n" +
                $"La copie de Google Drive n'est pas supprimée (dossier « {CoffreDonnees.NomDossierDrive} »).\n\nContinuer ?"))
            return;
        try
        {
            if (Directory.Exists(_dossierLocal) && Directory.EnumerateFileSystemEntries(_dossierLocal).Any())
                Directory.Move(_dossierLocal, $"{_dossierLocal} (avant le {_horloge():yyyy-MM-dd HH-mm})");
            CopierDossier(Session.Dossier, _dossierLocal);

            // Les secrets voyageaient dans les données : ils reviennent dans le coffre de Windows.
            var cheminSecrets = Path.Combine(_dossierLocal, SecretsFichier.NomFichier);
            var secrets = new SecretsFichier(_dossierLocal);
            foreach (var nom in secrets.Noms())
                _secretsPC.Ecrire(nom, secrets.Lire(nom));
            File.Delete(cheminSecrets);
            foreach (var nom in new[] { CompteGoogle.SecretJeton, CompteGoogle.SecretPortees, CompteGoogle.SecretAdresse })
                _secretsPC.Ecrire(nom, _connexion.Lire(nom));

            _demarrage.EnregistrerPC(drive: false, confiance: false);
            _demarrage.RetirerPortable();
            _dialogues.Information("Données sur ce PC", "C'est fait. L'application va redémarrer et ouvrir les données de ce PC.");
            RedemarrageDemande?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _dialogues.Erreur($"Les données n'ont pas pu être copiées sur ce PC :\n\n{e.Message}");
        }
    }

    private static void CopierDossier(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        if (!Directory.Exists(source))
            return;
        foreach (var fichier in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var cible = Path.Combine(destination, Path.GetRelativePath(source, fichier));
            Directory.CreateDirectory(Path.GetDirectoryName(cible)!);
            using var lecture = new FileStream(fichier, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ecriture = File.Create(cible);
            lecture.CopyTo(ecriture);
        }
    }
}
