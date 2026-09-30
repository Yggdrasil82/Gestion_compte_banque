using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Data.Achats;

namespace GestionCompte.Presentation;

/// <summary>Clé API saisie dans l'application (null : garder la clé déjà enregistrée) et modèle choisi (vide : modèle par défaut).</summary>
public sealed record SaisieIA(string? Cle, string Modele);

/// <summary>
/// IA gratuites (Gemini, Mistral) avec les clés personnelles de l'utilisateur, gardées sur ce PC et chiffrées par Windows.
/// Une case permet de couper tout envoi aux IA.
/// </summary>
public sealed partial class ServicesIA : ObservableObject
{
    public const string SecretGemini = "ia-gemini-cle";
    public const string SecretMistral = "ia-mistral-cle";
    public const string SecretModeleGemini = "ia-gemini-modele";
    public const string SecretModeleMistral = "ia-mistral-modele";
    public const string SecretCoupee = "ia-coupee";

    public const string AdresseCleGemini = "https://aistudio.google.com/apikey";
    public const string AdresseCleMistral = "https://console.mistral.ai/api-keys";

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
        Http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        MettreAJour();
    }

    public HttpClient Http { get; }

    [ObservableProperty] private bool _geminiConfigure;
    [ObservableProperty] private bool _mistralConfigure;
    [ObservableProperty] private string _etatGemini = "";
    [ObservableProperty] private string _etatMistral = "";

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

    /// <summary>IA utilisables maintenant (clé saisie, envoi autorisé).</summary>
    public IReadOnlyList<IAssistantIA> Assistants()
    {
        if (!Actives)
            return Array.Empty<IAssistantIA>();
        if (_assistantsTest is not null)
            return _assistantsTest();
        var assistants = new List<IAssistantIA>();
        if (_secrets.Lire(SecretGemini) is { Length: > 0 } gemini)
            assistants.Add(new Gemini(Http, gemini, _secrets.Lire(SecretModeleGemini)));
        if (_secrets.Lire(SecretMistral) is { Length: > 0 } mistral)
            assistants.Add(new Mistral(Http, mistral, _secrets.Lire(SecretModeleMistral)));
        return assistants;
    }

    [RelayCommand]
    private void ReglerGemini() => Regler("Gemini", SecretGemini, SecretModeleGemini, Gemini.ModeleParDefaut, AdresseCleGemini);

    [RelayCommand]
    private void ReglerMistral() => Regler("Mistral", SecretMistral, SecretModeleMistral, Mistral.ModeleParDefaut, AdresseCleMistral);

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
    private void RetirerMistral() => Retirer("Mistral", SecretMistral);

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
        MistralConfigure = _secrets.Lire(SecretMistral) is { Length: > 0 };
        EtatGemini = GeminiConfigure ? $"Gemini : clé enregistrée ({_secrets.Lire(SecretModeleGemini) ?? Gemini.ModeleParDefaut})" : "Gemini : pas de clé";
        EtatMistral = MistralConfigure ? $"Mistral : clé enregistrée ({_secrets.Lire(SecretModeleMistral) ?? Mistral.ModeleParDefaut})" : "Mistral : pas de clé";
        OnPropertyChanged(nameof(Disponibles));
    }
}
