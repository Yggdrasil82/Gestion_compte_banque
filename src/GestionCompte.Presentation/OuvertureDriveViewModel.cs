using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Data.Documents;
using GestionCompte.Data.Nuage;

namespace GestionCompte.Presentation;

/// <summary>
/// Fenêtre affichée au lancement quand les données sont dans Google Drive : connexion au compte Google, mot de passe des
/// données, PC de confiance. Sans connexion, un PC de confiance ouvre sa copie chiffrée en lecture seule.
/// </summary>
public sealed partial class OuvertureDriveViewModel : ObservableObject
{
    private readonly IDialogues _dialogues;
    private readonly DemarrageDrive _demarrage;
    private readonly string _dossierSession;
    private readonly string _poste;
    private readonly Func<IStockageDocuments> _distant;
    private readonly int _iterations;
    private readonly Func<DateTime> _horloge;

    /// <param name="dossierSession">Dossier de travail où les données sont déchiffrées le temps de la session.</param>
    /// <param name="poste">Nom de ce PC (affiché aux autres PC par le verrou).</param>
    /// <param name="distant">Stockage des données (par défaut le dossier « Mon Budget - Données » du Google Drive).</param>
    public OuvertureDriveViewModel(CompteGoogle google, IDialogues dialogues, DemarrageDrive demarrage, string dossierSession,
        string poste, Func<IStockageDocuments>? distant = null, int iterations = ChiffrementDocuments.IterationsParDefaut,
        Func<DateTime>? horloge = null)
    {
        Google = google;
        _dialogues = dialogues;
        _demarrage = demarrage;
        _dossierSession = dossierSession;
        _poste = poste;
        _distant = distant ?? (() => new StockageGoogleDrive(google.Http, google.JetonAsync, CoffreDonnees.NomDossierDrive));
        _iterations = iterations;
        _horloge = horloge ?? (() => DateTime.Now);
        _confiance = demarrage.Confiance;
    }

    public CompteGoogle Google { get; }

    /// <summary>Garder la connexion Google et une copie chiffrée des données sur ce PC.</summary>
    [ObservableProperty] private bool _confiance;

    [ObservableProperty] private string _etat = "";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Libre))] private bool _occupe;

    public bool Libre => !Occupe;

    /// <summary>Session ouverte (après <see cref="OuvrirAsync"/> réussi).</summary>
    public SessionDrive? Session { get; private set; }

    /// <summary>Connecte le compte Google ; false si annulé ou en échec (message affiché).</summary>
    public async Task<bool> ConnecterAsync()
    {
        Etat = "Terminez la connexion à Google dans votre navigateur…";
        var chrono = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var connecte = await Google.ConnecterAsync();
            Etat = connecte ? "" : "Connexion annulée.";
            return connecte;
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or OperationCanceledException or TimeoutException
                                      or IOException or System.Net.Sockets.SocketException or System.Text.Json.JsonException)
        {
            Etat = "";
            _dialogues.Erreur(CompteGoogle.MessageEchec(Google.Etat, e, chrono.Elapsed));
            return false;
        }
    }

    /// <summary>Télécharge et déchiffre les données ; false si l'ouverture n'a pas abouti (raison dans <see cref="Etat"/>).</summary>
    public async Task<bool> OuvrirAsync(string motDePasse)
    {
        if (string.IsNullOrEmpty(motDePasse))
        {
            Etat = "Saisissez le mot de passe de vos données.";
            return false;
        }
        if (!Google.Connecte && !await ConnecterAsync())
            return await OuvrirCopieSiPossibleAsync(motDePasse, "Le compte Google n'est pas connecté.");

        Occupe = true;
        try
        {
            Etat = "Recherche de vos données dans Google Drive…";
            var coffre = new CoffreDonnees(_distant(), _iterations);
            if (!await coffre.ExisteAsync())
            {
                Etat = "";
                _dialogues.Erreur($"Aucune donnée Mon Budget n'a été trouvée dans le Google Drive de {Google.Adresse}.\n\n" +
                                  "Sur votre PC habituel, ouvrez Configuration › Données › « Mettre mes données dans Google Drive ».");
                return false;
            }

            Etat = "Vérification du mot de passe…";
            if (!await coffre.DeverrouillerAsync(motDePasse))
            {
                Etat = "Mot de passe incorrect.";
                return false;
            }

            var session = Guid.NewGuid().ToString("N");
            var lectureSeule = false;
            if (await coffre.LireVerrouAsync() is { } verrou && verrou.BloqueAutreSession(session, _horloge()))
            {
                var choix = _dialogues.ChoisirOption("Déjà ouverte ailleurs",
                    $"Mon Budget est déjà ouvert sur « {verrou.Poste} » depuis le {verrou.Depuis:dd/MM à HH:mm}.\n\n" +
                    "En lecture seule, vous consultez vos données sans rien changer. « Ouvrir quand même » fait passer l'autre PC en " +
                    "lecture seule : ses modifications non envoyées seront perdues.",
                    new[] { "Lecture seule", "Ouvrir quand même", "Annuler" });
                if (choix is null or 2)
                {
                    Etat = "";
                    return false;
                }
                lectureSeule = choix == 0;
            }

            if (Confiance)
                coffre.Copie = new StockageDossier(_demarrage.DossierCopie);
            Etat = "Téléchargement et déchiffrement des données…";
            await coffre.TelechargerAsync(_dossierSession);
            Session = new SessionDrive(_dossierSession, coffre, lectureSeule, session, _poste, _horloge);
            await Session.SignalerAsync();
            _demarrage.EnregistrerPC(drive: true, Confiance);
            Etat = "";
            return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or TimeoutException
                                      or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            return await OuvrirCopieSiPossibleAsync(motDePasse, e.Message);
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or InvalidDataException
                                      or System.Text.Json.JsonException or IOException)
        {
            Etat = "";
            _dialogues.Erreur($"Les données de Google Drive n'ont pas pu être ouvertes :\n\n{e.Message}");
            return false;
        }
        finally
        {
            Occupe = false;
        }
    }

    /// <summary>Sans Google Drive : un PC de confiance peut ouvrir sa copie chiffrée, en lecture seule.</summary>
    private async Task<bool> OuvrirCopieSiPossibleAsync(string motDePasse, string raison)
    {
        Etat = "";
        var copie = new StockageDossier(_demarrage.DossierCopie);
        if (!_demarrage.Confiance || !File.Exists(Path.Combine(copie.Dossier, CoffreDonnees.NomDonnees)))
        {
            _dialogues.Erreur($"Google Drive est injoignable : vos données ne peuvent pas être ouvertes.\n\n{raison}");
            return false;
        }
        if (!_dialogues.Confirmer("Hors connexion",
                $"Google Drive est injoignable ({raison}).\n\nOuvrir la dernière copie de ce PC en lecture seule ? " +
                "Les modifications ne seront pas gardées."))
            return false;

        try
        {
            var coffre = new CoffreDonnees(copie, _iterations);
            if (!await coffre.DeverrouillerAsync(motDePasse))
            {
                Etat = "Mot de passe incorrect.";
                return false;
            }
            await coffre.TelechargerAsync(_dossierSession);
            Session = new SessionDrive(_dossierSession, null, true, Guid.NewGuid().ToString("N"), _poste, _horloge);
            return true;
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or InvalidDataException
                                      or System.Text.Json.JsonException or IOException)
        {
            _dialogues.Erreur($"La copie de ce PC n'a pas pu être ouverte :\n\n{e.Message}");
            return false;
        }
    }

    /// <summary>Mot de passe oublié : la clé de secours permet d'en choisir un nouveau.</summary>
    [RelayCommand]
    private async Task MotDePasseOublie()
    {
        if (!Google.Connecte && !await ConnecterAsync())
            return;
        var secours = _dialogues.DemanderNom("Mot de passe oublié",
            "Saisissez la clé de secours notée lors de la mise des données dans Google Drive :", "");
        if (string.IsNullOrWhiteSpace(secours))
            return;
        var nouveau = _dialogues.DemanderMotDePasse("Nouveau mot de passe",
            "Choisissez un nouveau mot de passe pour vos données (la clé de secours reste la même).", confirmer: true);
        if (nouveau is null)
            return;

        Occupe = true;
        try
        {
            var coffre = new CoffreDonnees(_distant(), _iterations);
            if (await coffre.RecupererAsync(secours, nouveau))
            {
                _dialogues.Information("Mot de passe changé", "Le nouveau mot de passe est enregistré. Saisissez-le pour ouvrir vos données.");
            }
            else
                _dialogues.Erreur("Cette clé de secours ne correspond pas à vos données.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or ArgumentException
                                      or InvalidOperationException or System.Text.Json.JsonException)
        {
            _dialogues.Erreur($"Le mot de passe n'a pas pu être changé :\n\n{e.Message}");
        }
        finally
        {
            Occupe = false;
        }
    }
}
