using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Data.Achats;

namespace GestionCompte.Presentation;

/// <summary>Clé API saisie dans l'application (null : garder la clé déjà enregistrée) et modèle choisi (vide : modèle par défaut).</summary>
public sealed record SaisieIA(string? Cle, string Modele);

/// <summary>
/// IA gratuites (Gemini, Groq) avec les clés personnelles de l'utilisateur, gardées sur ce PC et chiffrées par Windows.
/// Une case permet de couper tout envoi aux IA.
/// </summary>
public sealed partial class ServicesIA : ObservableObject
{
    public const string SecretGemini = "ia-gemini-cle";
    public const string SecretGroq = "ia-groq-cle";
    public const string SecretModeleGemini = "ia-gemini-modele";
    public const string SecretModeleGroq = "ia-groq-modele";

    /// <summary>Anciennes clés Mistral (retiré en 2.2.0 : clés API réservées aux offres payantes), effacées au démarrage.</summary>
    private static readonly string[] AnciensSecrets = { "ia-mistral-cle", "ia-mistral-modele" };
    public const string SecretCoupee = "ia-coupee";

    public const string AdresseCleGemini = "https://aistudio.google.com/apikey";
    public const string AdresseCleGroq = "https://console.groq.com/keys";

    private readonly ISecretsLocaux _secrets;
    private readonly IDialogues _dialogues;
    private readonly Func<IReadOnlyList<IAssistantIA>>? _assistantsTest;

    /// <param name="assistantsTest">Remplace les vraies IA (tests et captures).</param>
    public ServicesIA(ISecretsLocaux secrets, IDialogues dialogues, HttpClient? http = null,
        Func<IReadOnlyList<IAssistantIA>>? assistantsTest = null)
    {
        _secrets = secrets;
        _dialogues = dialogues;
        _assistantsTest = assistantsTest;
        Http = http ?? Data.Reseau.Client(TimeSpan.FromMinutes(2));
        foreach (var ancien in AnciensSecrets)
            if (_secrets.Lire(ancien) is not null)
                _secrets.Ecrire(ancien, null);
        MettreAJour();
    }

    public HttpClient Http { get; }

    [ObservableProperty] private bool _geminiConfigure;
    [ObservableProperty] private bool _groqConfigure;
    [ObservableProperty] private string _etatGemini = "";
    [ObservableProperty] private string _etatGroq = "";

    /// <summary>Envoi aux IA autorisé (case « Utiliser les IA » de la configuration).</summary>
    public bool Actives
    {
        get => _secrets.Lire(SecretCoupee) != "1";
        set
        {
            if (value == Actives)
                return;
            _secrets.Ecrire(SecretCoupee, value ? null : "1");
            MettreAJour();
            OnPropertyChanged();
        }
    }

    /// <summary>Au moins une IA est réglée et l'envoi est autorisé.</summary>
    public bool Disponibles => Actives && Assistants().Count > 0;

    /// <summary>Une IA attend la fin d'une limite par minute avant de réessayer (texte à afficher).</summary>
    public event EventHandler<string>? Patiente;

    /// <summary>Signale une attente (utilisé aussi par les tests).</summary>
    public void Patienter(string message) => Patiente?.Invoke(this, message);

    /// <summary>IA utilisables maintenant (clé saisie, envoi autorisé).</summary>
    public IReadOnlyList<IAssistantIA> Assistants()
    {
        if (!Actives)
            return Array.Empty<IAssistantIA>();
        if (_assistantsTest is not null)
            return _assistantsTest();
        var assistants = new List<IAssistantIA>();
        if (_secrets.Lire(SecretGemini) is { Length: > 0 } gemini)
            assistants.Add(new Gemini(Http, gemini, _secrets.Lire(SecretModeleGemini), Patienter));
        if (_secrets.Lire(SecretGroq) is { Length: > 0 } groq)
            assistants.Add(new Groq(Http, groq, _secrets.Lire(SecretModeleGroq), Patienter));
        return assistants;
    }

    [RelayCommand]
    private void ReglerGemini() => Regler("Gemini", SecretGemini, SecretModeleGemini, Gemini.ModeleParDefaut, AdresseCleGemini);

    [RelayCommand]
    private void ReglerGroq() => Regler("Groq", SecretGroq, SecretModeleGroq, Groq.ModeleParDefaut, AdresseCleGroq);

    private void Regler(string nom, string secretCle, string secretModele, string modeleParDefaut, string adresseCle)
    {
        var saisie = _dialogues.DemanderCleIA(nom, adresseCle, _secrets.Lire(secretCle) is { Length: > 0 },
            _secrets.Lire(secretModele) ?? "", modeleParDefaut);
        if (saisie is null)
            return;
        if (saisie.Cle is not null)
            _secrets.Ecrire(secretCle, saisie.Cle.Trim().Length == 0 ? null : saisie.Cle.Trim());
        _secrets.Ecrire(secretModele, string.IsNullOrWhiteSpace(saisie.Modele) ? null : saisie.Modele.Trim());
        MettreAJour();
    }

    [RelayCommand]
    private void RetirerGemini() => Retirer("Gemini", SecretGemini);

    [RelayCommand]
    private void RetirerGroq() => Retirer("Groq", SecretGroq);

    private void Retirer(string nom, string secret)
    {
        if (!_dialogues.Confirmer($"Retirer la clé {nom}", $"Effacer la clé {nom} de ce PC ? L'application n'utilisera plus {nom}."))
            return;
        _secrets.Ecrire(secret, null);
        MettreAJour();
    }

    private void MettreAJour()
    {
        GeminiConfigure = _secrets.Lire(SecretGemini) is { Length: > 0 };
        GroqConfigure = _secrets.Lire(SecretGroq) is { Length: > 0 };
        EtatGemini = GeminiConfigure ? $"Gemini : clé enregistrée ({_secrets.Lire(SecretModeleGemini) ?? Gemini.ModeleParDefaut})" : "Gemini : pas de clé";
        EtatGroq = GroqConfigure ? $"Groq : clé enregistrée ({_secrets.Lire(SecretModeleGroq) ?? Groq.ModeleParDefaut})" : "Groq : pas de clé";
        OnPropertyChanged(nameof(Disponibles));
    }
}
