using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using GestionCompte.Data.Nuage;

namespace GestionCompte.Presentation;

/// <summary>
/// Réglage lu au lancement : les données sont-elles dans Google Drive, et ce PC est-il de confiance ?
/// Sur un PC de confiance, le réglage est dans le dossier de l'application de ce PC (avec la connexion Google et une copie
/// chiffrée des données) ; à côté de l'exe (clé USB), un simple fichier indique d'ouvrir les données de Google Drive.
/// </summary>
public sealed class DemarrageDrive
{
    public const string NomFichierPC = "demarrage.json";
    public const string NomFichierPortable = "MonBudget-drive.json";

    /// <param name="dossierPC">Dossier de l'application sur ce PC (%LOCALAPPDATA%\GestionCompte).</param>
    /// <param name="dossierExe">Dossier de l'exe (clé USB…) ; null s'il est inconnu.</param>
    public DemarrageDrive(string dossierPC, string? dossierExe)
    {
        DossierPC = dossierPC;
        DossierExe = dossierExe;
        var reglage = Lire(Path.Combine(dossierPC, NomFichierPC));
        Drive = reglage?.Drive == true || (DossierExe is not null && File.Exists(Path.Combine(DossierExe, NomFichierPortable)));
        Confiance = Drive && reglage?.Drive == true && reglage.Confiance;
    }

    public string DossierPC { get; }
    public string? DossierExe { get; }

    /// <summary>Les données sont dans Google Drive.</summary>
    public bool Drive { get; }

    /// <summary>PC de confiance : connexion Google gardée et copie chiffrée des données (lecture hors connexion).</summary>
    public bool Confiance { get; }

    /// <summary>Copie chiffrée des données sur un PC de confiance.</summary>
    public string DossierCopie => Path.Combine(DossierPC, "drive");

    /// <summary>Dossier de travail de cette session (déchiffré), supprimé à la fermeture.</summary>
    public static string DossierSession(string dossierTemporaire, int processus) =>
        Path.Combine(dossierTemporaire, $"MonBudget-{processus}");

    /// <summary>Enregistre le réglage de ce PC (données dans Drive, PC de confiance ou non).</summary>
    public void EnregistrerPC(bool drive, bool confiance)
    {
        var chemin = Path.Combine(DossierPC, NomFichierPC);
        if (drive && confiance)
        {
            Directory.CreateDirectory(DossierPC);
            File.WriteAllText(chemin, JsonSerializer.Serialize(new Reglage { Drive = true, Confiance = true }));
            return;
        }
        // PC qui n'est pas (ou plus) de confiance : ni réglage ni copie des données.
        if (File.Exists(chemin))
            File.Delete(chemin);
        if (Directory.Exists(DossierCopie))
            Directory.Delete(DossierCopie, recursive: true);
    }

    /// <summary>Fichier posé à côté de l'exe : ce lanceur (clé USB) ouvre les données de Google Drive sur n'importe quel PC.</summary>
    public static void PreparerPortable(string dossier) =>
        File.WriteAllText(Path.Combine(dossier, NomFichierPortable),
            JsonSerializer.Serialize(new Reglage { Drive = true }));

    /// <summary>Retire le fichier portable à côté de l'exe (retour aux données sur ce PC).</summary>
    public void RetirerPortable()
    {
        if (DossierExe is null)
            return;
        try
        {
            File.Delete(Path.Combine(DossierExe, NomFichierPortable));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Dossier en lecture seule : sans importance, le réglage de ce PC n'indique plus Google Drive.
        }
    }

    /// <summary>Supprime les dossiers de session laissés par une application arrêtée brutalement.</summary>
    public static void NettoyerSessions(string dossierTemporaire, Func<int, bool> processusActif)
    {
        if (!Directory.Exists(dossierTemporaire))
            return;
        foreach (var dossier in Directory.EnumerateDirectories(dossierTemporaire, "MonBudget-*"))
        {
            if (int.TryParse(Path.GetFileName(dossier)["MonBudget-".Length..], out var processus) && processusActif(processus))
                continue;
            try
            {
                Directory.Delete(dossier, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Fichier encore ouvert : nouvel essai au prochain lancement.
            }
        }
    }

    private static Reglage? Lire(string chemin)
    {
        try
        {
            return File.Exists(chemin) ? JsonSerializer.Deserialize<Reglage>(File.ReadAllText(chemin)) : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed class Reglage
    {
        public bool Drive { get; set; }
        public bool Confiance { get; set; }
    }
}

/// <summary>
/// Secrets rangés dans un fichier du dossier des données (clés des IA, mot de passe d'envoi des mails…) : avec les données
/// dans Google Drive, ils voyagent dans le fichier chiffré et suivent l'utilisateur d'un PC à l'autre.
/// </summary>
public sealed class SecretsFichier : ISecretsLocaux
{
    public const string NomFichier = "secrets.json";
    private readonly string _chemin;
    private readonly Dictionary<string, string> _secrets;

    public SecretsFichier(string dossier)
    {
        _chemin = Path.Combine(dossier, NomFichier);
        try
        {
            _secrets = File.Exists(_chemin)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_chemin)) ?? new()
                : new();
        }
        catch (JsonException)
        {
            _secrets = new();
        }
    }

    public string? Lire(string nom) => _secrets.GetValueOrDefault(nom);

    public void Ecrire(string nom, string? valeur)
    {
        if (valeur is null)
            _secrets.Remove(nom);
        else
            _secrets[nom] = valeur;
        File.WriteAllText(_chemin, JsonSerializer.Serialize(_secrets, new JsonSerializerOptions { WriteIndented = true }));
    }

    public IEnumerable<string> Noms() => _secrets.Keys.ToList();
}

/// <summary>
/// La connexion Google reste sur le PC (elle sert à télécharger les données, avant de pouvoir les lire) ; les autres secrets
/// sont rangés avec les données.
/// </summary>
public sealed class SecretsCombines : ISecretsLocaux
{
    private readonly ISecretsLocaux _connexion;
    private readonly ISecretsLocaux _donnees;

    public SecretsCombines(ISecretsLocaux connexion, ISecretsLocaux donnees)
    {
        _connexion = connexion;
        _donnees = donnees;
    }

    public static bool EstConnexionGoogle(string nom) => nom.StartsWith("google-", StringComparison.Ordinal);

    private ISecretsLocaux Pour(string nom) => EstConnexionGoogle(nom) ? _connexion : _donnees;

    public string? Lire(string nom) => Pour(nom).Lire(nom);

    public void Ecrire(string nom, string? valeur) => Pour(nom).Ecrire(nom, valeur);
}

/// <summary>
/// Session ouverte sur les données de Google Drive : les fichiers déchiffrés sont dans un dossier de travail ; dès qu'un fichier
/// change, tout est rechiffré et renvoyé. Un verrou prévient les autres PC ; si l'application est ouverte ailleurs entre-temps,
/// cette session passe en lecture seule pour ne rien écraser.
/// </summary>
public sealed partial class SessionDrive : ObservableObject
{
    /// <summary>Signe de vie renouvelé à cet intervalle (le verrou expire après <see cref="CoffreDonnees.DureeVerrou"/>).</summary>
    public static readonly TimeSpan IntervalleSigne = TimeSpan.FromMinutes(5);

    private readonly CoffreDonnees? _distant;
    private readonly string _session;
    private readonly string _poste;
    private readonly DateTime _depuis;
    private readonly Func<DateTime> _horloge;
    private string _empreinte;
    private DateTime _dernierSigne;
    private bool _enCours;
    private CancellationTokenSource? _boucle;

    /// <param name="distant">Données dans Google Drive ; null hors connexion (copie de ce PC, en lecture seule).</param>
    public SessionDrive(string dossier, CoffreDonnees? distant, bool lectureSeule, string session, string poste,
        Func<DateTime>? horloge = null)
    {
        Dossier = dossier;
        _distant = distant;
        _session = session;
        _poste = poste;
        _horloge = horloge ?? (() => DateTime.Now);
        _depuis = _horloge();
        _dernierSigne = _depuis;
        _empreinte = CoffreDonnees.Paquet.Empreinte(dossier);
        LectureSeule = lectureSeule || distant is null;
        Etat = distant is null
            ? "Hors connexion : copie de ce PC, en lecture seule (les modifications ne seront pas gardées)."
            : lectureSeule
                ? "Lecture seule : l'application est ouverte sur un autre PC (les modifications ne seront pas gardées)."
                : $"Données dans Google Drive, à jour ({_depuis:HH:mm}).";
    }

    /// <summary>Dossier des données déchiffrées, le temps de la session.</summary>
    public string Dossier { get; }

    [ObservableProperty] private bool _lectureSeule;
    [ObservableProperty] private string _etat;

    /// <summary>Des modifications n'ont pas encore pu être envoyées.</summary>
    public bool EnvoiEnAttente => !LectureSeule && CoffreDonnees.Paquet.Empreinte(Dossier) != _empreinte;

    /// <summary>L'application a été ouverte sur un autre PC pendant cette session : plus rien n'est envoyé.</summary>
    public event EventHandler<VerrouDonnees>? OuverteAilleurs;

    public string Session => _session;

    /// <summary>Données dans Google Drive ; null hors connexion.</summary>
    public CoffreDonnees? Distant => _distant;

    /// <summary>Pose le verrou à l'ouverture.</summary>
    public Task SignalerAsync(CancellationToken annulation = default) =>
        LectureSeule || _distant is null
            ? Task.CompletedTask
            : _distant.EcrireVerrouAsync(new VerrouDonnees(_poste, _session, _depuis, _horloge(), false), annulation);

    /// <summary>
    /// Envoie les données si un fichier a changé, et renouvelle le verrou. Une erreur réseau est indiquée dans <see cref="Etat"/>
    /// (nouvel essai au prochain passage) ; true si tout est à jour dans Google Drive.
    /// </summary>
    public async Task<bool> SynchroniserAsync(bool forcer = false, CancellationToken annulation = default)
    {
        if (LectureSeule || _distant is null)
            return false;
        if (_enCours)
            return false;
        _enCours = true;
        try
        {
            var maintenant = _horloge();
            var empreinte = CoffreDonnees.Paquet.Empreinte(Dossier);
            var modifie = empreinte != _empreinte;
            if (!modifie && !forcer && maintenant - _dernierSigne < IntervalleSigne)
                return true;

            var verrou = await _distant.LireVerrouAsync(annulation);
            if (verrou is not null && verrou.BloqueAutreSession(_session, maintenant))
            {
                LectureSeule = true;
                Etat = $"Ouverte sur « {verrou.Poste} » depuis {verrou.Depuis:HH:mm} : cette fenêtre est passée en lecture seule.";
                OuverteAilleurs?.Invoke(this, verrou);
                return false;
            }

            if (modifie || forcer)
            {
                await _distant.EnvoyerAsync(Dossier, annulation);
                _empreinte = empreinte;
                Etat = $"Enregistré dans Google Drive à {maintenant:HH:mm}.";
            }
            await _distant.EcrireVerrouAsync(new VerrouDonnees(_poste, _session, _depuis, maintenant, false), annulation);
            _dernierSigne = maintenant;
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or TimeoutException
                                      or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            Etat = $"Pas de connexion à Google Drive : les modifications seront envoyées dès son retour. ({e.Message})";
            return false;
        }
        finally
        {
            _enCours = false;
        }
    }

    /// <summary>Vérifie les modifications à intervalle régulier (sur le fil de l'interface, entre deux enregistrements).</summary>
    public async void Demarrer(TimeSpan intervalle)
    {
        _boucle = new CancellationTokenSource();
        using var minuterie = new PeriodicTimer(intervalle);
        try
        {
            while (await minuterie.WaitForNextTickAsync(_boucle.Token))
                await SynchroniserAsync();
        }
        catch (OperationCanceledException)
        {
            // Session fermée.
        }
    }

    /// <summary>
    /// Fin de session : dernier envoi et verrou levé. false si des modifications n'ont pas pu être envoyées
    /// (le dossier de travail est alors gardé pour un nouvel essai).
    /// </summary>
    public async Task<bool> TerminerAsync(CancellationToken annulation = default)
    {
        _boucle?.Cancel();
        while (_enCours)
            await Task.Delay(100, annulation);
        if (!LectureSeule && _distant is not null)
        {
            if (!await SynchroniserAsync(annulation: annulation) || EnvoiEnAttente)
                return false;
            try
            {
                await _distant.EcrireVerrouAsync(new VerrouDonnees(_poste, _session, _depuis, _horloge(), true), annulation);
            }
            catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
            {
                // Les données sont envoyées ; le verrou expirera de lui-même.
            }
        }
        return true;
    }

    /// <summary>Efface les données déchiffrées de ce PC.</summary>
    public void Effacer()
    {
        try
        {
            if (Directory.Exists(Dossier))
                Directory.Delete(Dossier, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Fichier encore ouvert : supprimé au prochain lancement (DemarrageDrive.NettoyerSessions).
        }
    }
}
