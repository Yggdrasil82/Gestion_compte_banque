using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GestionCompte.Presentation;

/// <summary>
/// Module « Assistant » : questions sur le budget posées à Gemini et/ou Mistral.
/// Seul un résumé des chiffres, affiché avant l'envoi, part aux IA (s'il est coché) ; l'assistant ne modifie rien.
/// La conversation n'est pas enregistrée.
/// </summary>
public sealed partial class AssistantViewModel : ObservableObject
{
    public const string LesDeux = "Gemini et Mistral";

    /// <summary>Nombre d'échanges précédents renvoyés avec la question.</summary>
    private const int ToursGardes = 6;

    private readonly ServicesIA _ia;
    private readonly Func<string> _calculerResume;

    /// <param name="resume">Résumé des chiffres du compte ouvert (sans libellés d'opérations ni numéro de compte).</param>
    public AssistantViewModel(ServicesIA ia, Func<string> resume)
    {
        _ia = ia;
        _calculerResume = resume;
        _ia.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IADisponibles));
        Conversation.CollectionChanged += (_, _) => OnPropertyChanged(nameof(AvecConversation));
        ActualiserResume();
    }

    public bool IADisponibles => _ia.Disponibles;

    [ObservableProperty] private string _statut = "";
    [ObservableProperty] private string _erreur = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnvoyerCommand), nameof(PoserCommand))]
    private bool _occupe;

    // ---- Résumé envoyé ----

    [ObservableProperty] private string _resume = "";

    /// <summary>Joindre le résumé des chiffres aux questions.</summary>
    [ObservableProperty] private bool _inclureResume = true;

    /// <summary>Recalcule le résumé (compte ouvert, mois du jour) ; appelé à l'ouverture de l'onglet.</summary>
    [RelayCommand]
    public void ActualiserResume() => Resume = _calculerResume();

    // ---- IA utilisées ----

    public IReadOnlyList<string> ChoixIA { get; } = new[] { LesDeux, "Gemini", "Mistral" };

    [ObservableProperty] private string _iAChoisie = LesDeux;

    // ---- Conversation ----

    public IReadOnlyList<string> Suggestions { get; } = new[]
    {
        "Où puis-je faire des économies ?",
        "Mon budget du mois est-il équilibré ?",
        "Puis-je atteindre mes objectifs d'épargne ?",
        "Que penses-tu de mon prévisionnel ?",
    };

    public ObservableCollection<MessageAssistantViewModel> Conversation { get; } = new();

    public bool AvecConversation => Conversation.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnvoyerCommand))]
    private string _question = "";

    private bool PeutEnvoyer() => !Occupe && Question.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(PeutEnvoyer))]
    private Task Envoyer() => Demander(Question.Trim());

    private bool PeutPoser() => !Occupe;

    /// <summary>Pose une question suggérée.</summary>
    [RelayCommand(CanExecute = nameof(PeutPoser))]
    private Task Poser(string? suggestion) => string.IsNullOrWhiteSpace(suggestion) ? Task.CompletedTask : Demander(suggestion);

    private async Task Demander(string question)
    {
        var assistants = _ia.Assistants()
            .Where(a => IAChoisie == LesDeux || a.Nom.Equals(IAChoisie, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (assistants.Count == 0)
        {
            Erreur = !_ia.Actives
                ? "Les IA sont coupées (Configuration › Intelligence artificielle)."
                : IAChoisie == LesDeux
                    ? "Aucune IA réglée : saisissez une clé Gemini ou Mistral (Configuration › Intelligence artificielle)."
                    : $"{IAChoisie} n'est pas réglée : saisissez sa clé (Configuration › Intelligence artificielle) ou choisissez une autre IA.";
            return;
        }

        if (InclureResume)
            ActualiserResume();
        var demande = Construire(question);
        Conversation.Add(new MessageAssistantViewModel("Vous", question, deVous: true));
        Question = "";
        Occupe = true;
        Erreur = "";
        Statut = $"{string.Join(" et ", assistants.Select(a => a.Nom))} réfléchi{(assistants.Count > 1 ? "ssent" : "t")}…";
        try
        {
            var reponses = await Task.WhenAll(assistants.Select(async ia =>
            {
                try
                {
                    return (ia.Nom, Texte: (await ia.DemanderAsync(demande, avecRecherche: false)).Trim(), Erreur: (string?)null);
                }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
                {
                    return (ia.Nom, Texte: "", Erreur: e is OperationCanceledException ? $"{ia.Nom} n'a pas répondu à temps." : e.Message);
                }
            }));
            foreach (var reponse in reponses.Where(r => r.Erreur is null))
                Conversation.Add(new MessageAssistantViewModel(reponse.Nom, reponse.Texte, deVous: false));
                Erreur = string.Join("\n", reponses.Where(r => r.Erreur is not null).Select(r => r.Erreur));
            Statut = "";
        }
        finally
        {
            Occupe = false;
        }
    }

    /// <summary>Texte envoyé : consignes, résumé (si coché), derniers échanges, question.</summary>
    internal string Construire(string question)
    {
        var texte = new StringBuilder();
        texte.AppendLine("Tu es l'assistant budgétaire d'une application de gestion de budget personnel. Réponds en français, " +
                         "de façon concrète et concise (quelques paragraphes ou une courte liste au plus), sans tableau.");
        texte.AppendLine("Tu ne peux rien modifier dans l'application : propose des pistes que l'utilisateur appliquera lui-même. " +
                         "Ne donne pas de conseil d'investissement personnalisé et ne prétends pas connaître d'autres chiffres que ceux fournis.");
        if (InclureResume && Resume.Length > 0)
        {
            texte.AppendLine();
            texte.AppendLine("Résumé des chiffres du budget (montants en euros) :");
            texte.AppendLine(Resume);
        }
        var precedents = Conversation.TakeLast(ToursGardes * 2).ToList();
        if (precedents.Count > 0)
        {
            texte.AppendLine();
            texte.AppendLine("Échanges précédents :");
            foreach (var message in precedents)
                texte.AppendLine($"{(message.DeVous ? "Utilisateur" : $"Assistant ({message.Auteur})")} : {message.Texte}");
        }
        texte.AppendLine();
        texte.AppendLine($"Question : {question}");
        return texte.ToString();
    }

    [RelayCommand]
    private void Effacer()
    {
        Conversation.Clear();
        Erreur = "";
        Statut = "";
    }
}

public sealed class MessageAssistantViewModel
{
    public MessageAssistantViewModel(string auteur, string texte, bool deVous)
    {
        Auteur = auteur;
        Texte = texte;
        DeVous = deVous;
    }

    public string Auteur { get; }
    public string Texte { get; }
    public bool DeVous { get; }
}
