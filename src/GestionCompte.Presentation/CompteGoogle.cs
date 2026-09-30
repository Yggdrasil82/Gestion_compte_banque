using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Data.Documents;

namespace GestionCompte.Presentation;

/// <summary>
/// Compte Google de l'utilisateur, partagé par les modules (Documents : Google Drive ; Mail : Gmail et contacts).
/// L'identifiant d'application et la connexion sont gardés sur ce PC, chiffrés par Windows.
/// </summary>
public sealed partial class CompteGoogle : ObservableObject
{
    public const string SecretClientId = "google-client-id";
    public const string SecretClientSecret = "google-client-secret";
    public const string SecretJeton = "google-jeton";
    public const string SecretPortees = "google-portees";
    public const string SecretAdresse = "google-adresse";

    private readonly ISecretsLocaux _secrets;
    private readonly IDialogues _dialogues;

    /// <param name="http">Client HTTP (remplacé dans les tests).</param>
    public CompteGoogle(ISecretsLocaux secrets, IDialogues dialogues, HttpClient? http = null)
    {
        _secrets = secrets;
        _dialogues = dialogues;
        Http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        if (Identifiants() is { } identifiants && _secrets.Lire(SecretJeton) is { Length: > 0 } jeton)
            Connexion = new ConnexionGoogle(Http, identifiants, jeton);
        MettreAJour();
    }

    public HttpClient Http { get; }

    public ConnexionGoogle? Connexion { get; private set; }

    [ObservableProperty] private bool _connecte;
    [ObservableProperty] private string _adresse = "";
    [ObservableProperty] private string _etat = "";

    /// <summary>Le compte a été déconnecté (Documents revient alors au dossier de ce PC).</summary>
    public event EventHandler? Deconnecte;

    /// <summary>Jeton d'accès (renouvelé automatiquement).</summary>
    public Task<string> JetonAsync(CancellationToken annulation = default) =>
        Connexion is { Connecte: true } connexion
            ? connexion.JetonAsync(annulation)
            : throw new InvalidOperationException("Le compte Google n'est pas connecté (Configuration › Compte Google).");

    /// <summary>Droit accordé lors de la connexion (l'utilisateur peut en décocher sur la page de Google).</summary>
    public bool Autorise(string portee) =>
        Connecte && (_secrets.Lire(SecretPortees) is not { Length: > 0 } portees || portees.Split(' ').Contains(portee));

    public IdentifiantsGoogle? Identifiants() =>
        _secrets.Lire(SecretClientId) is { Length: > 0 } id && _secrets.Lire(SecretClientSecret) is { Length: > 0 } secret
            ? new IdentifiantsGoogle(id, secret)
            : null;

    /// <summary>
    /// Demande l'identifiant d'application puis ouvre la connexion Google dans le navigateur.
    /// false si l'utilisateur annule ; une erreur de connexion est remontée.
    /// </summary>
    public async Task<bool> ConnecterAsync(CancellationToken annulation = default)
    {
        var saisie = _dialogues.DemanderIdentifiantsGoogle(Identifiants());
        if (saisie is null)
            return false;
        var identifiants = new IdentifiantsGoogle(saisie.ClientId.Trim(), saisie.ClientSecret.Trim());
        _secrets.Ecrire(SecretClientId, identifiants.ClientId);
        _secrets.Ecrire(SecretClientSecret, identifiants.ClientSecret);

        var connexion = new ConnexionGoogle(Http, identifiants, null);
        await connexion.ConnecterAsync(_dialogues.OuvrirLien, annulation);
        _secrets.Ecrire(SecretJeton, connexion.JetonRenouvellement);
        _secrets.Ecrire(SecretPortees, connexion.PorteesAccordees);
        Connexion = connexion;
        _secrets.Ecrire(SecretAdresse, await connexion.AdresseAsync(annulation));
        MettreAJour();
        return true;
    }

    /// <summary>Bouton « Connecter » de la configuration.</summary>
    [RelayCommand]
    private async Task Connecter()
    {
        Etat = "Terminez la connexion dans votre navigateur…";
        try
        {
            await ConnecterAsync();
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or OperationCanceledException
                                      or IOException or System.Net.Sockets.SocketException or System.Text.Json.JsonException)
        {
            _dialogues.Erreur(e is OperationCanceledException
                ? "La connexion à Google n'a pas été terminée à temps (5 minutes)."
                : $"La connexion à Google a échoué.\n\n{e.Message}");
        }
        finally
        {
            MettreAJour();
        }
    }

    [RelayCommand]
    private void DemanderDeconnexion()
    {
        if (_dialogues.Confirmer("Déconnecter le compte Google",
                "L'application n'aura plus accès à Google Drive, à Gmail ni aux contacts. Vos documents restent dans votre Google Drive ; " +
                "l'onglet Documents reviendra au dossier de ce PC.\n\nContinuer ?"))
            Deconnecter();
    }

    public void Deconnecter()
    {
        Connexion?.Deconnecter();
        Connexion = null;
        _secrets.Ecrire(SecretJeton, null);
        _secrets.Ecrire(SecretPortees, null);
        _secrets.Ecrire(SecretAdresse, null);
        MettreAJour();
        Deconnecte?.Invoke(this, EventArgs.Empty);
    }

    private void MettreAJour()
    {
        Connecte = Connexion is { Connecte: true };
        Adresse = Connecte ? _secrets.Lire(SecretAdresse) ?? "" : "";
        Etat = Connecte
            ? $"Connecté{(Adresse.Length > 0 ? $" : {Adresse}" : "")}"
            : "Non connecté";
    }
}
