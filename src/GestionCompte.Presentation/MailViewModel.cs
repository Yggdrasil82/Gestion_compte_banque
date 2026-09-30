using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Core;
using GestionCompte.Core.Mail;
using GestionCompte.Data;
using GestionCompte.Data.Documents;
using GestionCompte.Data.Mail;

namespace GestionCompte.Presentation;

/// <summary>
/// Module « Mail » : écrire et envoyer un mail (Gmail ou serveur d'envoi réglé à la main), avec des pièces jointes
/// tirées du coffre des documents, du bilan ou d'un fichier ; carnet d'adresses (manuel et Google) et historique.
/// </summary>
public sealed partial class MailViewModel : ObservableObject
{
    public const string SecretSmtp = "smtp-reglages";
    public const string SecretSmtpMotDePasse = "smtp-mot-de-passe";

    private readonly ApparenceViewModel _reglages;
    private readonly CompteGoogle _google;
    private readonly ISecretsLocaux _secrets;
    private readonly IDialogues _dialogues;
    private readonly DocumentsViewModel _documents;
    private readonly Func<BilanViewModel?> _bilan;
    private readonly Func<IEnvoiMail?>? _envoiTest;
    private readonly CarnetMail _carnet;

    /// <param name="dossier">Dossier des données de l'application (carnet « mail.json »).</param>
    /// <param name="bilan">Bilan affiché (pour le joindre en PDF).</param>
    /// <param name="envoiTest">Remplace l'envoi réel (tests et captures).</param>
    public MailViewModel(ApparenceViewModel reglages, string dossier, CompteGoogle google, ISecretsLocaux secrets, IDialogues dialogues,
        DocumentsViewModel documents, Func<BilanViewModel?> bilan, Func<IEnvoiMail?>? envoiTest = null)
    {
        _reglages = reglages;
        _google = google;
        _secrets = secrets;
        _dialogues = dialogues;
        _documents = documents;
        _bilan = bilan;
        _envoiTest = envoiTest;
        _carnet = new CarnetMail(dossier);
        try
        {
            _carnet.Charger();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Erreur = $"Le carnet d'adresses n'a pas pu être lu : {e.Message}";
        }
        _google.PropertyChanged += (_, _) => MettreAJourCompte();
        PiecesJointes.CollectionChanged += (_, _) => MettreAJourPieces();
        MettreAJourCompte();
        MettreAJourContacts();
        MettreAJourHistorique();
    }

    // ---- Compte d'envoi ----

    public bool ModeGmail
    {
        get => _reglages.ModeEnvoiMail == ModeEnvoiMail.Gmail;
        set
        {
            if (value)
                ChangerMode(ModeEnvoiMail.Gmail);
        }
    }

    public bool ModeSmtp
    {
        get => _reglages.ModeEnvoiMail == ModeEnvoiMail.Smtp;
        set
        {
            if (value)
                ChangerMode(ModeEnvoiMail.Smtp);
        }
    }

    private void ChangerMode(ModeEnvoiMail mode)
    {
        _reglages.ModeEnvoiMail = mode;
        MettreAJourCompte();
    }

    /// <summary>Ex. « Gmail : romain@gmail.com » ou « Serveur smtp.orange.fr (romain@orange.fr) ».</summary>
    [ObservableProperty] private string _compteEnvoi = "";

    /// <summary>Le compte choisi est prêt : Google connecté, ou serveur d'envoi réglé.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnvoyerCommand))]
    private bool _comptePret;

    public bool GoogleConnecte => _google.Connecte;

    /// <summary>Gmail est choisi mais le compte Google n'est pas connecté : le bouton « Connecter » est proposé.</summary>
    public bool ConnexionGoogleNecessaire => ModeGmail && !_google.Connecte;

    public ReglagesSmtp? Smtp => LireSmtp();

    private ReglagesSmtp? LireSmtp()
    {
        try
        {
            return _secrets.Lire(SecretSmtp) is { Length: > 0 } json ? JsonSerializer.Deserialize<ReglagesSmtp>(json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void MettreAJourCompte()
    {
        var smtp = LireSmtp();
        if (ModeGmail)
        {
            ComptePret = _google.Connecte;
            CompteEnvoi = _google.Connecte
                ? $"Gmail : {(_google.Adresse.Length > 0 ? _google.Adresse : "compte Google connecté")}"
                : "Gmail : connectez le compte Google (bouton ci-dessous ou Configuration › Compte Google).";
        }
        else
        {
            ComptePret = smtp is not null && _secrets.Lire(SecretSmtpMotDePasse) is { Length: > 0 };
            CompteEnvoi = smtp is null
                ? "Serveur d'envoi : à régler (bouton « Régler le serveur… »)."
                : $"Serveur {smtp.Serveur} : {smtp.AdresseExpediteur}";
        }
        OnPropertyChanged(nameof(ModeGmail));
        OnPropertyChanged(nameof(ModeSmtp));
        OnPropertyChanged(nameof(GoogleConnecte));
        OnPropertyChanged(nameof(ConnexionGoogleNecessaire));
        OnPropertyChanged(nameof(Smtp));
    }

    [RelayCommand]
    private Task ConnecterGoogle() => _google.ConnecterCommand.ExecuteAsync(null);

    [RelayCommand]
    private void ReglerSmtp()
    {
        var saisie = _dialogues.DemanderReglagesSmtp(LireSmtp());
        if (saisie is null)
            return;
        _secrets.Ecrire(SecretSmtp, JsonSerializer.Serialize(saisie.Reglages));
        if (saisie.MotDePasse is not null)
            _secrets.Ecrire(SecretSmtpMotDePasse, saisie.MotDePasse);
        _reglages.ModeEnvoiMail = ModeEnvoiMail.Smtp;
        MettreAJourCompte();
        Statut = "Serveur d'envoi enregistré sur ce PC.";
    }

    // ---- Composition ----

    [ObservableProperty] private string _destinataires = "";
    [ObservableProperty] private string _copies = "";
    [ObservableProperty] private string _objet = "";
    [ObservableProperty] private string _corps = "";

    public ObservableCollection<PieceJointeViewModel> PiecesJointes { get; } = new();

    [ObservableProperty] private string _taillePieces = "";

    public bool AvecPieces => PiecesJointes.Count > 0;

    private void MettreAJourPieces()
    {
        var total = PiecesJointes.Sum(p => (long)p.Piece.Contenu.Length);
        TaillePieces = PiecesJointes.Count == 0
            ? ""
            : $"{PiecesJointes.Count} pièce{(PiecesJointes.Count > 1 ? "s" : "")} jointe{(PiecesJointes.Count > 1 ? "s" : "")} · {PieceJointeViewModel.Taille(total)}";
        OnPropertyChanged(nameof(AvecPieces));
    }

    public void AjouterPiece(PieceJointe piece)
    {
        // Deux pièces du même nom : la seconde est renommée « nom (2).pdf ».
        var nom = piece.Nom;
        for (var n = 2; PiecesJointes.Any(p => p.Nom.Equals(nom, StringComparison.OrdinalIgnoreCase)); n++)
            nom = $"{Path.GetFileNameWithoutExtension(piece.Nom)} ({n}){Path.GetExtension(piece.Nom)}";
        PiecesJointes.Add(new PieceJointeViewModel(piece with { Nom = nom }));
    }

    [RelayCommand]
    private void RetirerPiece(PieceJointeViewModel? piece)
    {
        if (piece is not null)
            PiecesJointes.Remove(piece);
    }

    public bool DocumentsDisponibles => _reglages.ModuleDocuments;

    public bool BilanDisponible => _reglages.ModuleBilan;

    /// <summary>Le module Documents ou Bilan a été affiché ou masqué.</summary>
    public void ModulesModifies()
    {
        OnPropertyChanged(nameof(DocumentsDisponibles));
        OnPropertyChanged(nameof(BilanDisponible));
    }

    [RelayCommand]
    private async Task JoindreDocuments()
    {
        await _documents.Chargement;
        var documents = _documents.Tous;
        if (documents.Count == 0)
        {
            _dialogues.Information("Joindre des documents", "Le coffre ne contient aucun document (onglet Documents).");
            return;
        }
        var choix = _dialogues.ChoisirParmi("Joindre des documents", "Documents du coffre à joindre au mail :",
            documents.Select(d => $"{d.Nom} ({d.Categorie}){(d.Protege ? " · protégé" : "")}").ToList());
        foreach (var index in choix ?? Array.Empty<int>())
            await JoindreDocumentAsync(documents[index]);
    }

    /// <summary>Joint un document du coffre (mot de passe demandé s'il est protégé) ; false si annulé.</summary>
    public async Task<bool> JoindreDocumentAsync(DocumentViewModel document)
    {
        var piece = await _documents.PieceJointeAsync(document);
        if (piece is null)
        {
            if (_documents.Erreur.Length > 0)
                Erreur = _documents.Erreur;
            return false;
        }
        AjouterPiece(piece);
        if (string.IsNullOrWhiteSpace(Objet))
            Objet = document.Nom;
        return true;
    }

    /// <summary>Prépare un mail avec une lettre : objet et texte repris s'ils sont vides, lettre jointe en PDF.</summary>
    public void PreparerLettre(string objet, string texte, PieceJointe piece)
    {
        AjouterPiece(piece);
        if (string.IsNullOrWhiteSpace(Objet))
            Objet = objet;
        if (string.IsNullOrWhiteSpace(Corps))
            Corps = texte;
        Statut = "Lettre jointe en PDF : ajoutez le destinataire puis envoyez.";
    }

    [RelayCommand]
    private void JoindreBilan() => JoindreBilanPdf();

    /// <summary>Joint le bilan affiché dans l'onglet Bilan, en PDF ; false s'il n'y en a pas.</summary>
    public bool JoindreBilanPdf()
    {
        if (_bilan() is not { Resultat: { Vide: false } resultat } bilan)
        {
            Erreur = "Aucun bilan à joindre : créez d'abord des mois (onglet Bilan).";
            return false;
        }
        var chemin = Path.Combine(Path.GetTempPath(), $"gestioncompte-bilan-{Guid.NewGuid():N}.pdf");
        try
        {
            ExportPdf.ExporterBilan(resultat, bilan.Titre, chemin);
            AjouterPiece(new PieceJointe($"{bilan.Titre}.pdf", File.ReadAllBytes(chemin)));
            if (string.IsNullOrWhiteSpace(Objet))
                Objet = bilan.Titre;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Erreur = $"Le bilan n'a pas pu être joint : {e.Message}";
            return false;
        }
        finally
        {
            File.Delete(chemin);
        }
    }

    [RelayCommand]
    private void JoindreFichier()
    {
        if (_dialogues.ChoisirFichierAJoindre() is not { } chemin)
            return;
        try
        {
            AjouterPiece(new PieceJointe(Path.GetFileName(chemin), File.ReadAllBytes(chemin)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Erreur = $"Le fichier n'a pas pu être lu (est-il ouvert dans un autre programme ?) : {e.Message}";
        }
    }

    [RelayCommand]
    private void Nouveau()
    {
        Destinataires = "";
        Copies = "";
        Objet = "";
        Corps = "";
        PiecesJointes.Clear();
        Erreur = "";
        Statut = "";
    }

    // ---- Envoi ----

    [ObservableProperty] private string _statut = "";
    [ObservableProperty] private string _erreur = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnvoyerCommand))]
    private bool _occupe;

    private bool PeutEnvoyer() => ComptePret && !Occupe;

    [RelayCommand(CanExecute = nameof(PeutEnvoyer))]
    private async Task Envoyer()
    {
        Erreur = "";
        var (a, invalidesA) = AdressesMail.Decouper(Destinataires);
        var (cc, invalidesCc) = AdressesMail.Decouper(Copies);
        var invalides = invalidesA.Concat(invalidesCc).ToList();
        if (invalides.Count > 0)
        {
            Erreur = $"Adresse incorrecte : {string.Join(", ", invalides)}";
            return;
        }
        if (a.Count == 0)
        {
            Erreur = "Indiquez au moins un destinataire.";
            return;
        }
        var total = PiecesJointes.Sum(p => (long)p.Piece.Contenu.Length);
        if (total > Messages.TailleMaximale)
        {
            Erreur = $"Les pièces jointes dépassent {Messages.TailleMaximale / 1024 / 1024} Mo : retirez-en une partie.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Objet)
            && !_dialogues.Confirmer("Mail sans objet", "Le mail n'a pas d'objet. L'envoyer quand même ?"))
            return;

        var envoi = CreerEnvoi();
        if (envoi is null)
        {
            Erreur = "Le compte d'envoi n'est pas prêt.";
            return;
        }

        var message = new MessageMail(a, cc, Objet.Trim(), Corps, PiecesJointes.Select(p => p.Piece).ToList());
        var trace = new EnvoiMail
        {
            Date = DateTime.Now,
            Compte = envoi.Description,
            Destinataires = a.Concat(cc).ToList(),
            Objet = message.Objet,
            Extrait = Corps.Length > 200 ? Corps[..200] + "…" : Corps,
            PiecesJointes = PiecesJointes.Select(p => p.Nom).ToList(),
        };

        Occupe = true;
        Statut = "Envoi en cours…";
        try
        {
            await envoi.EnvoyerAsync(message);
            trace.Reussi = true;
            Statut = $"Mail envoyé à {string.Join(", ", a)}.";
            Destinataires = "";
            Copies = "";
            Objet = "";
            Corps = "";
            PiecesJointes.Clear();
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or OperationCanceledException
                                      or IOException or System.Net.Sockets.SocketException or FormatException)
        {
            trace.Erreur = e is OperationCanceledException ? "Délai dépassé." : e.Message;
            Statut = "";
            Erreur = $"Le mail n'est pas parti : {trace.Erreur}";
        }
        finally
        {
            Occupe = false;
        }

        _carnet.NoterEnvoi(trace);
        EnregistrerCarnet();
        MettreAJourContacts();
        MettreAJourHistorique();
    }

    private IEnvoiMail? CreerEnvoi()
    {
        if (_envoiTest is not null)
            return _envoiTest();
        if (ModeGmail)
            return _google.Connecte ? new EnvoiGmail(_google.Http, _google.JetonAsync, null, null) : null;
        return LireSmtp() is { } smtp && _secrets.Lire(SecretSmtpMotDePasse) is { Length: > 0 } motDePasse
            ? new EnvoiSmtp(smtp, motDePasse)
            : null;
    }

    // ---- Contacts ----

    [ObservableProperty] private IReadOnlyList<Contact> _contacts = Array.Empty<Contact>();
    [ObservableProperty] private string _rechercheContact = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ModifierContactCommand), nameof(SupprimerContactCommand), nameof(EcrireAuContactCommand))]
    private Contact? _contactSelectionne;

    partial void OnRechercheContactChanged(string value) => MettreAJourContacts();

    public int NombreContacts => _carnet.Contacts.Count;

    /// <summary>Contacts écrits récemment d'abord, puis par nom.</summary>
    private void MettreAJourContacts()
    {
        var mots = RechercheContact.Trim();
        var selection = ContactSelectionne;
        Contacts = _carnet.Contacts
            .Where(c => mots.Length == 0
                        || c.Nom.Contains(mots, StringComparison.CurrentCultureIgnoreCase)
                        || c.Email.Contains(mots, StringComparison.CurrentCultureIgnoreCase)
                        || c.Notes.Contains(mots, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(c => c.DernierEnvoi ?? DateTime.MinValue)
            .ThenBy(c => string.IsNullOrWhiteSpace(c.Nom) ? c.Email : c.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        ContactSelectionne = selection is not null && Contacts.Contains(selection) ? selection : null;
        OnPropertyChanged(nameof(NombreContacts));
    }

    /// <summary>Ajoute le contact aux destinataires (double-clic ou bouton « Écrire »).</summary>
    [RelayCommand(CanExecute = nameof(AContact))]
    private void EcrireAuContact()
    {
        if (ContactSelectionne is not { } contact)
            return;
        var (adresses, _) = AdressesMail.Decouper(Destinataires);
        if (adresses.Contains(contact.Email, StringComparer.OrdinalIgnoreCase))
            return;
        Destinataires = string.IsNullOrWhiteSpace(Destinataires) ? contact.Email : $"{Destinataires.TrimEnd().TrimEnd(';')}; {contact.Email}";
    }

    private bool AContact() => ContactSelectionne is not null;

    [RelayCommand]
    private void NouveauContact()
    {
        if (_dialogues.DemanderContact(null) is not { } contact)
            return;
        if (_carnet.Contacts.Any(c => string.Equals(c.Email, contact.Email, StringComparison.OrdinalIgnoreCase)))
        {
            Erreur = $"L'adresse {contact.Email} est déjà dans le carnet.";
            return;
        }
        _carnet.Contacts.Add(contact);
        EnregistrerCarnet();
        MettreAJourContacts();
        ContactSelectionne = contact;
    }

    [RelayCommand(CanExecute = nameof(AContact))]
    private void ModifierContact()
    {
        if (ContactSelectionne is not { } contact || _dialogues.DemanderContact(contact) is not { } modifie)
            return;
        contact.Nom = modifie.Nom;
        contact.Email = modifie.Email;
        contact.Notes = modifie.Notes;
        EnregistrerCarnet();
        MettreAJourContacts();
        ContactSelectionne = contact;
    }

    [RelayCommand(CanExecute = nameof(AContact))]
    private void SupprimerContact()
    {
        if (ContactSelectionne is not { } contact
            || !_dialogues.Confirmer("Supprimer le contact", $"Retirer {contact.Affichage} du carnet d'adresses ?"))
            return;
        _carnet.Contacts.Remove(contact);
        EnregistrerCarnet();
        MettreAJourContacts();
    }

    [RelayCommand]
    private async Task ImporterContactsGoogle()
    {
        Erreur = "";
        if (!_google.Connecte)
        {
            await _google.ConnecterCommand.ExecuteAsync(null);
            if (!_google.Connecte)
                return;
        }
        if (!_google.Autorise(ConnexionGoogle.PorteeContacts))
        {
            Erreur = "Le compte Google n'autorise pas la lecture des contacts : reconnectez-le (Configuration › Compte Google) en cochant les contacts.";
            return;
        }

        Occupe = true;
        Statut = "Lecture des contacts Google…";
        try
        {
            var contacts = await ContactsGoogle.LireAsync(_google.Http, _google.JetonAsync,
                _google.Autorise(ConnexionGoogle.PorteeAutresContacts));
            var ajoutes = _carnet.Fusionner(contacts);
            EnregistrerCarnet();
            MettreAJourContacts();
            Statut = ajoutes == 0 ? "Carnet déjà à jour avec vos contacts Google." : $"{ajoutes} contacts Google ajoutés au carnet.";
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or OperationCanceledException or JsonException)
        {
            Statut = "";
            Erreur = e is OperationCanceledException ? "Google n'a pas répondu à temps." : e.Message;
        }
        finally
        {
            Occupe = false;
        }
    }

    // ---- Historique ----

    [ObservableProperty] private IReadOnlyList<EnvoiMailViewModel> _historique = Array.Empty<EnvoiMailViewModel>();

    public bool AvecHistorique => Historique.Count > 0;

    private void MettreAJourHistorique()
    {
        Historique = _carnet.Historique.Select(e => new EnvoiMailViewModel(e)).ToList();
        OnPropertyChanged(nameof(AvecHistorique));
    }

    /// <summary>Reprend les destinataires et l'objet d'un envoi (pièces jointes à rajouter).</summary>
    [RelayCommand]
    private void Reprendre(EnvoiMailViewModel? envoi)
    {
        if (envoi is null)
            return;
        Destinataires = string.Join("; ", envoi.Envoi.Destinataires);
        Objet = envoi.Envoi.Objet;
        Statut = envoi.Envoi.PiecesJointes.Count > 0 ? "Destinataires et objet repris : ajoutez les pièces jointes à renvoyer." : "Destinataires et objet repris.";
    }

    [RelayCommand]
    private void ViderHistorique()
    {
        if (!_dialogues.Confirmer("Vider l'historique", "Effacer l'historique des mails envoyés ? Le carnet d'adresses est conservé."))
            return;
        _carnet.Historique.Clear();
        EnregistrerCarnet();
        MettreAJourHistorique();
    }

    private void EnregistrerCarnet()
    {
        try
        {
            _carnet.Enregistrer();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Erreur = $"Le carnet d'adresses n'a pas pu être enregistré : {e.Message}";
        }
    }
}

public sealed class PieceJointeViewModel
{
    public PieceJointeViewModel(PieceJointe piece) => Piece = piece;

    public PieceJointe Piece { get; }

    public string Nom => Piece.Nom;

    public string Detail => Taille(Piece.Contenu.Length);

    internal static string Taille(long octets) => octets switch
    {
        < 1024 => $"{octets} o",
        < 1024 * 1024 => string.Format(Montants.Francais, "{0:0} Ko", octets / 1024.0),
        _ => string.Format(Montants.Francais, "{0:0.0} Mo", octets / 1024.0 / 1024.0),
    };
}

public sealed class EnvoiMailViewModel
{
    public EnvoiMailViewModel(EnvoiMail envoi) => Envoi = envoi;

    public EnvoiMail Envoi { get; }

    public string Date => Envoi.Date.ToString("dd/MM/yyyy HH:mm", Montants.Francais);
    public string Destinataires => string.Join(", ", Envoi.Destinataires);
    public string Objet => string.IsNullOrWhiteSpace(Envoi.Objet) ? "(sans objet)" : Envoi.Objet;
    public bool Reussi => Envoi.Reussi;

    public string Detail
    {
        get
        {
            var pieces = Envoi.PiecesJointes.Count == 0 ? "" : $" · {string.Join(", ", Envoi.PiecesJointes)}";
            return Envoi.Reussi ? $"Envoyé{pieces}" : $"Échec : {Envoi.Erreur}{pieces}";
        }
    }
}
