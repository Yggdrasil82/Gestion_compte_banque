using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Core;
using GestionCompte.Core.Lettres;
using GestionCompte.Data.Achats;
using GestionCompte.Data.Lettres;
using GestionCompte.Data.Mail;

namespace GestionCompte.Presentation;

/// <summary>
/// Module « Lettres » : lettres types rédigées par Gemini et Mistral (une version chacune, au choix),
/// relues et modifiables, puis exportées en PDF ou Word ou envoyées par mail.
/// Les coordonnées de l'utilisateur sont ajoutées par l'application et ne partent jamais aux IA.
/// </summary>
public sealed partial class LettresViewModel : ObservableObject
{
    private readonly ServicesIA _ia;
    private readonly IDialogues _dialogues;
    private readonly FichierLettres _fichier;
    private readonly Func<DateTime> _aujourdhui;

    /// <param name="dossier">Dossier des données de l'application (« lettres.json »).</param>
    public LettresViewModel(string dossier, ServicesIA ia, IDialogues dialogues, Func<DateTime>? aujourdhui = null)
    {
        _ia = ia;
        _ia.Patiente += (_, message) => { if (Occupe) Statut = message; };
        _dialogues = dialogues;
        _aujourdhui = aujourdhui ?? (() => DateTime.Today);
        _fichier = new FichierLettres(dossier);
        try
        {
            _fichier.Charger();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Erreur = $"Les lettres enregistrées n'ont pas pu être lues : {e.Message}";
        }
        _ia.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IADisponibles));
        _modeleChoisi = Modeles[0];
        CreerChamps();
        MettreAJourCoordonnees();
        MettreAJourHistorique();
    }

    /// <summary>Demande d'envoi de la lettre par mail (pièce jointe PDF, objet et texte), gérée par l'écran principal.</summary>
    public event EventHandler<LettreAEnvoyer>? EnvoiParMailDemande;

    [ObservableProperty] private string _statut = "";
    [ObservableProperty] private string _erreur = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RedigerCommand))]
    private bool _occupe;

    [ObservableProperty] private bool _envoiParMailPossible = true;

    public bool IADisponibles => _ia.Disponibles;

    // ---- Coordonnées (jamais envoyées aux IA) ----

    [ObservableProperty] private string _resumeCoordonnees = "";
    [ObservableProperty] private bool _coordonneesVides;

    private void MettreAJourCoordonnees()
    {
        var c = _fichier.Coordonnees;
        CoordonneesVides = c.Vides;
        ResumeCoordonnees = c.Vides
            ? "Vos coordonnées ne sont pas encore saisies : elles seront ajoutées en haut de chaque lettre."
            : string.Join(" · ", c.Lignes());
        OnPropertyChanged(nameof(LieuDate));
    }

    [RelayCommand]
    private void ModifierCoordonnees()
    {
        if (_dialogues.DemanderCoordonnees(_fichier.Coordonnees) is not { } coordonnees)
            return;
        _fichier.Coordonnees.Nom = coordonnees.Nom;
        _fichier.Coordonnees.Adresse = coordonnees.Adresse;
        _fichier.Coordonnees.Telephone = coordonnees.Telephone;
        _fichier.Coordonnees.Email = coordonnees.Email;
        _fichier.Coordonnees.Ville = coordonnees.Ville;
        if (Enregistrer())
            Statut = "Coordonnées enregistrées sur ce PC.";
        MettreAJourCoordonnees();
    }

    // ---- Rédaction ----

    public IReadOnlyList<ModeleLettre> Modeles => ModeleLettre.Liste;

    [ObservableProperty] private ModeleLettre _modeleChoisi;

    partial void OnModeleChoisiChanged(ModeleLettre value) => CreerChamps();

    public ObservableCollection<ChampLettreViewModel> Champs { get; } = new();

    [ObservableProperty] private string _precisions = "";

    private void CreerChamps()
    {
        var anciens = Champs.ToDictionary(c => c.Nom, c => c.Valeur);
        Champs.Clear();
        foreach (var champ in ModeleChoisi.Champs)
            Champs.Add(new ChampLettreViewModel(champ, anciens.GetValueOrDefault(champ, ""),
                multiligne: ModeleChoisi.Titre == ModeleLettre.Libre, RedigerCommand.NotifyCanExecuteChanged));
        RedigerCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Versions proposées par les IA (une par IA).</summary>
    [ObservableProperty] private IReadOnlyList<VersionLettreViewModel> _versions = Array.Empty<VersionLettreViewModel>();

    public bool AvecVersions => Versions.Count > 0;

    partial void OnVersionsChanged(IReadOnlyList<VersionLettreViewModel> value) => OnPropertyChanged(nameof(AvecVersions));

    private bool PeutRediger() => !Occupe && Champs.Any(c => !string.IsNullOrWhiteSpace(c.Valeur));

    [RelayCommand(CanExecute = nameof(PeutRediger))]
    private async Task Rediger()
    {
        var assistants = _ia.Assistants();
        if (assistants.Count == 0)
        {
            Erreur = _ia.Actives
                ? "Aucune IA réglée : saisissez une clé Gemini ou Mistral (Configuration › Intelligence artificielle)."
                : "Les IA sont coupées (Configuration › Intelligence artificielle).";
            return;
        }

        var demande = RedactionLettre.Demande(ModeleChoisi, Champs.Select(c => (c.Nom, c.Valeur)), Precisions);
        Occupe = true;
        Erreur = "";
        Versions = Array.Empty<VersionLettreViewModel>();
        Statut = $"Rédaction par {string.Join(" et ", assistants.Select(a => a.Nom))}…";
        try
        {
            var versions = await Task.WhenAll(assistants.Select(async ia =>
            {
                try
                {
                    var (objet, corps) = RedactionLettre.Decouper(await ia.DemanderAsync(demande, avecRecherche: false));
                    return corps.Length == 0
                        ? new VersionLettreViewModel(ia.Nom, "", "", $"{ia.Nom} n'a pas proposé de texte.")
                        : new VersionLettreViewModel(ia.Nom, objet, corps, null);
                }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
                {
                    return new VersionLettreViewModel(ia.Nom, "", "",
                        e is OperationCanceledException ? $"{ia.Nom} n'a pas répondu à temps." : e.Message);
                }
            }));
            Versions = versions;
            Erreur = string.Join("\n", versions.Where(v => v.Erreur is not null).Select(v => v.Erreur));
            Statut = versions.Any(v => v.Erreur is null)
                ? "Choisissez la version à garder, puis relisez-la et complétez les passages entre crochets."
                : "";
        }
        finally
        {
            Occupe = false;
        }
    }

    [RelayCommand]
    private void ChoisirVersion(VersionLettreViewModel? version)
    {
        if (version is null || version.Erreur is not null)
            return;
        EnCours = new Lettre
        {
            Date = _aujourdhui().Date,
            Modele = ModeleChoisi.Titre,
            Destinataire = DestinataireProbable(),
            Objet = version.Objet,
            Corps = version.Corps,
        };
        Statut = $"Version de {version.Source} gardée : relisez-la, complétez le destinataire, puis exportez-la ou envoyez-la.";
    }

    /// <summary>Premier champ (organisme, banque, vendeur…) pour pré-remplir le destinataire.</summary>
    private string DestinataireProbable() =>
        ModeleChoisi.Titre == ModeleLettre.Libre ? "" : Champs.FirstOrDefault()?.Valeur.Trim() ?? "";

    // ---- Lettre en cours ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvecLettre))]
    [NotifyCanExecuteChangedFor(nameof(ExporterPdfCommand), nameof(ExporterWordCommand), nameof(EnvoyerParMailCommand),
        nameof(GarderCommand))]
    private Lettre? _enCours;

    public bool AvecLettre => EnCours is not null;

    partial void OnEnCoursChanged(Lettre? value)
    {
        OnPropertyChanged(nameof(Destinataire));
        OnPropertyChanged(nameof(Objet));
        OnPropertyChanged(nameof(Corps));
        OnPropertyChanged(nameof(LieuDate));
    }

    public string Destinataire
    {
        get => EnCours?.Destinataire ?? "";
        set
        {
            if (EnCours is null || EnCours.Destinataire == value)
                return;
            EnCours.Destinataire = value;
            OnPropertyChanged();
        }
    }

    public string Objet
    {
        get => EnCours?.Objet ?? "";
        set
        {
            if (EnCours is null || EnCours.Objet == value)
                return;
            EnCours.Objet = value;
            OnPropertyChanged();
        }
    }

    public string Corps
    {
        get => EnCours?.Corps ?? "";
        set
        {
            if (EnCours is null || EnCours.Corps == value)
                return;
            EnCours.Corps = value;
            OnPropertyChanged();
        }
    }

    public string LieuDate => EnCours?.LieuDate(_fichier.Coordonnees) ?? "";

    private string NomFichier => $"Lettre - {Nettoyer(EnCours?.Objet is { Length: > 0 } o ? o : EnCours?.Modele ?? "lettre")}";

    private static string Nettoyer(string nom)
    {
        foreach (var interdit in Path.GetInvalidFileNameChars())
            nom = nom.Replace(interdit, ' ');
        nom = nom.Trim();
        return nom.Length > 60 ? nom[..60].Trim() : nom;
    }

    [RelayCommand(CanExecute = nameof(AvecLettre))]
    private void ExporterPdf() => Exporter(_dialogues.ChoisirFichierPdf($"{NomFichier}.pdf"), ExportLettre.ExporterPdf, "PDF");

    [RelayCommand(CanExecute = nameof(AvecLettre))]
    private void ExporterWord() => Exporter(_dialogues.ChoisirFichierWord($"{NomFichier}.docx"), ExportLettre.ExporterWord, "Word");

    private void Exporter(string? destination, Action<Lettre, Coordonnees, string> exporter, string format)
    {
        if (EnCours is null || destination is null)
            return;
        try
        {
            exporter(EnCours, _fichier.Coordonnees, destination);
            Garder();
            Erreur = "";
            Statut = $"Lettre enregistrée en {format} : {destination}";
            _dialogues.OuvrirFichier(destination);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Erreur = $"La lettre n'a pas pu être enregistrée en {format} : {e.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(AvecLettre))]
    private void EnvoyerParMail()
    {
        if (EnCours is null)
            return;
        var chemin = Path.Combine(Path.GetTempPath(), $"gestioncompte-lettre-{Guid.NewGuid():N}.pdf");
        try
        {
            ExportLettre.ExporterPdf(EnCours, _fichier.Coordonnees, chemin);
            var piece = new PieceJointe($"{NomFichier}.pdf", File.ReadAllBytes(chemin));
            Garder();
            EnvoiParMailDemande?.Invoke(this, new LettreAEnvoyer(EnCours.Objet, EnCours.TexteComplet(_fichier.Coordonnees), piece));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Erreur = $"La lettre n'a pas pu être préparée pour le mail : {e.Message}";
        }
        finally
        {
            try
            {
                File.Delete(chemin);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Garde la lettre dans « Mes lettres » (remplace la version déjà gardée).</summary>
    [RelayCommand(CanExecute = nameof(AvecLettre))]
    private void Garder()
    {
        if (EnCours is null)
            return;
        _fichier.Lettres.RemoveAll(l => l.Id == EnCours.Id);
        _fichier.Lettres.Insert(0, EnCours);
        if (Enregistrer())
            Statut = "Lettre gardée dans « Mes lettres ».";
        MettreAJourHistorique();
    }

    [RelayCommand]
    private void NouvelleLettre()
    {
        EnCours = null;
        Versions = Array.Empty<VersionLettreViewModel>();
        Precisions = "";
        foreach (var champ in Champs)
            champ.Valeur = "";
        Erreur = "";
        Statut = "";
    }

    // ---- Mes lettres ----

    public ObservableCollection<LettreGardeeViewModel> Historique { get; } = new();

    public bool AvecHistorique => Historique.Count > 0;

    private void MettreAJourHistorique()
    {
        Historique.Clear();
        foreach (var lettre in _fichier.Lettres)
            Historique.Add(new LettreGardeeViewModel(lettre));
        OnPropertyChanged(nameof(AvecHistorique));
    }

    [RelayCommand]
    private void Reprendre(LettreGardeeViewModel? gardee)
    {
        if (gardee is null)
            return;
        Versions = Array.Empty<VersionLettreViewModel>();
        EnCours = gardee.Lettre;
        if (Modeles.FirstOrDefault(m => m.Titre == gardee.Lettre.Modele) is { } modele)
            ModeleChoisi = modele;
        Statut = $"Lettre du {gardee.Date} reprise.";
    }

    [RelayCommand]
    private void Supprimer(LettreGardeeViewModel? gardee)
    {
        if (gardee is null || !_dialogues.Confirmer("Supprimer la lettre", $"Supprimer la lettre « {gardee.Objet} » ?"))
            return;
        _fichier.Lettres.Remove(gardee.Lettre);
        if (EnCours == gardee.Lettre)
            EnCours = null;
        Enregistrer();
        MettreAJourHistorique();
    }

    private bool Enregistrer()
    {
        try
        {
            _fichier.Enregistrer();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Erreur = $"Les lettres n'ont pas pu être enregistrées : {e.Message}";
            return false;
        }
    }
}

/// <summary>Lettre à joindre à un mail.</summary>
public sealed record LettreAEnvoyer(string Objet, string Texte, PieceJointe Piece);

/// <summary>Information à donner pour la lettre (organisme, numéro de contrat…).</summary>
public sealed partial class ChampLettreViewModel : ObservableObject
{
    private readonly Action? _modifie;

    public ChampLettreViewModel(string nom, string valeur, bool multiligne, Action? modifie = null)
    {
        _modifie = modifie;
        Nom = nom;
        _valeur = valeur;
        Multiligne = multiligne;
    }

    public string Nom { get; }

    public bool Multiligne { get; }

    [ObservableProperty] private string _valeur;

    partial void OnValeurChanged(string value) => _modifie?.Invoke();
}

/// <summary>Version proposée par une IA.</summary>
public sealed class VersionLettreViewModel
{
    public VersionLettreViewModel(string source, string objet, string corps, string? erreur)
    {
        Source = source;
        Objet = objet;
        Corps = corps;
        Erreur = erreur;
    }

    public string Source { get; }
    public string Objet { get; }
    public string Corps { get; }
    public string? Erreur { get; }
    public bool Reussie => Erreur is null;
    public string Titre => $"Version {Source}";
}

public sealed class LettreGardeeViewModel
{
    public LettreGardeeViewModel(Lettre lettre) => Lettre = lettre;

    public Lettre Lettre { get; }
    public string Date => Lettre.Date.ToString("dd/MM/yyyy", Montants.Francais);
    public string Objet => string.IsNullOrWhiteSpace(Lettre.Objet) ? "(sans objet)" : Lettre.Objet;
    public string Detail => string.IsNullOrWhiteSpace(Lettre.Destinataire)
        ? $"{Date} · {Lettre.Modele}"
        : $"{Date} · {Lettre.Destinataire.Split('\n')[0].Trim()}";
}
