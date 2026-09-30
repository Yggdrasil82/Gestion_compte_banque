using System.IO.Compression;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Core;
using GestionCompte.Core.Documents;
using GestionCompte.Data.Documents;

namespace GestionCompte.Presentation;

/// <summary>
/// Module « Documents » : coffre des documents importants, commun à tous les comptes, rangé dans le dossier
/// de l'application, dans un dossier synchronisé (Google Drive pour ordinateur) ou directement dans Google Drive.
/// </summary>
public sealed partial class DocumentsViewModel : ObservableObject
{
    public const string Toutes = "Toutes";
    public const string Aucune = "(aucune)";
    private readonly ApparenceViewModel _reglages;
    private readonly string _dossierLocal;
    private readonly CompteGoogle _google;
    private readonly IDialogues _dialogues;
    private readonly DateOnly _aujourdHui;
    private readonly Func<IReadOnlyList<string>> _charges;
    private readonly int _iterations;
    private readonly SemaphoreSlim _enregistrement = new(1, 1);
    private CoffreDocuments? _coffre;
    private List<DocumentViewModel> _tous = new();

    /// <param name="dossierLocal">Dossier des données de l'application ; le coffre local est son sous-dossier « Documents ».</param>
    /// <param name="charges">Noms des charges du compte ouvert (pour lier un contrat à sa charge).</param>
    /// <param name="iterations">Coût du calcul de la clé depuis le mot de passe (réduit dans les tests).</param>
    /// <param name="google">Compte Google partagé (connexion directe à Google Drive).</param>
    public DocumentsViewModel(ApparenceViewModel reglages, string dossierLocal, CompteGoogle google, IDialogues dialogues,
        DateOnly aujourdHui, Func<IReadOnlyList<string>> charges, int iterations = ChiffrementDocuments.IterationsParDefaut)
    {
        _reglages = reglages;
        _dossierLocal = dossierLocal;
        _google = google;
        _google.Deconnecte += async (_, _) =>
        {
            if (_reglages.EmplacementDocuments != EmplacementDocuments.GoogleDrive)
                return;
            _reglages.ChoisirEmplacementDocuments(EmplacementDocuments.Local, null);
            await OuvrirCoffreAsync(new StockageDossier(DossierLocal));
        };
        _dialogues = dialogues;
        _aujourdHui = aujourdHui;
        _charges = charges;
        _iterations = iterations;
        NettoyerFichiersTemporaires();
        Chargement = OuvrirCoffreAsync(CreerStockage());
    }

    /// <summary>Premier chargement du coffre (lancé à la création).</summary>
    public Task Chargement { get; private set; }

    public static IReadOnlyList<string> NomsCategories { get; } =
        Enum.GetValues<CategorieDocument>().Select(DocumentImportant.NomCategorie).ToList();

    public IReadOnlyList<string> Categories => NomsCategories;

    public IReadOnlyList<string> Filtres { get; } = new[] { Toutes }.Concat(NomsCategories).ToList();

    /// <summary>Charges proposées pour lier un document (« (aucune) » en premier).</summary>
    public IReadOnlyList<string> Charges => new[] { Aucune }.Concat(_charges()).Distinct().ToList();

    /// <summary>Le compte ouvert (ou sa configuration) a changé : la liste des charges est relue.</summary>
    public void ChargesModifiees() => OnPropertyChanged(nameof(Charges));

    [ObservableProperty] private IReadOnlyList<DocumentViewModel> _documents = Array.Empty<DocumentViewModel>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ASelection))]
    [NotifyCanExecuteChangedFor(nameof(OuvrirCommand), nameof(RemplacerCommand), nameof(SupprimerCommand),
        nameof(EnregistrerSousCommand), nameof(ProtegerCommand), nameof(EnvoyerParMailCommand))]
    private DocumentViewModel? _selection;

    public bool ASelection => Selection is not null;

    [ObservableProperty] private string _recherche = "";
    [ObservableProperty] private string _filtre = Toutes;

    partial void OnRechercheChanged(string value) => Filtrer();
    partial void OnFiltreChanged(string value) => Filtrer();

    [ObservableProperty] private IReadOnlyList<RappelDocument> _rappels = Array.Empty<RappelDocument>();
    [ObservableProperty] private string _titreNavigation = "Documents";
    [ObservableProperty] private string _statut = "";
    [ObservableProperty] private string _erreur = "";
    [ObservableProperty] private string _emplacement = "";
    [ObservableProperty] private bool _occupe;
    [ObservableProperty] private bool _protectionConfiguree;
    [ObservableProperty] private bool _deverrouille;

    /// <summary>Les documents sont rangés dans Google Drive.</summary>
    public bool SurGoogleDrive => _coffre?.Stockage is StockageGoogleDrive;

    public bool AvecRappels => Rappels.Count > 0;

    public bool Vide => _tous.Count == 0;

    // ---- Chargement et emplacement ----

    private IStockageDocuments CreerStockage()
    {
        switch (_reglages.EmplacementDocuments)
        {
            case EmplacementDocuments.Dossier when !string.IsNullOrWhiteSpace(_reglages.DossierDocuments):
                return new StockageDossier(_reglages.DossierDocuments);
            case EmplacementDocuments.GoogleDrive when _google.Connecte:
                return new StockageGoogleDrive(_google.Http, _google.JetonAsync);
        }
        return new StockageDossier(DossierLocal);
    }

    public string DossierLocal => Path.Combine(_dossierLocal, "Documents");

    private async Task OuvrirCoffreAsync(IStockageDocuments stockage)
    {
        var coffre = new CoffreDocuments(stockage, _iterations);
        await Executer("Ouverture du coffre…", async () =>
        {
            await coffre.ChargerAsync();
            _coffre = coffre;
            Emplacement = stockage.Description;
            Reconstruire();
        });
    }

    /// <summary>Relit le coffre (après une synchronisation du drive, par exemple).</summary>
    [RelayCommand]
    private Task Actualiser() => OuvrirCoffreAsync(_coffre?.Stockage ?? CreerStockage());

    [RelayCommand]
    private Task UtiliserDossierLocal() => ChangerEmplacementAsync(new StockageDossier(DossierLocal), EmplacementDocuments.Local, null);

    [RelayCommand]
    private Task ChoisirDossierSynchronise()
    {
        var dossier = _dialogues.ChoisirDossier(
            "Dossier des documents (par exemple dans « Mon Drive », synchronisé par Google Drive pour ordinateur)");
        return dossier is null
            ? Task.CompletedTask
            : ChangerEmplacementAsync(new StockageDossier(dossier), EmplacementDocuments.Dossier, dossier);
    }

    /// <summary>Range les documents directement dans Google Drive (connexion au compte Google si besoin).</summary>
    [RelayCommand]
    private async Task UtiliserGoogleDrive()
    {
        if (!_google.Connecte)
        {
            var connecte = false;
            await Executer("Connexion au compte Google : terminez la connexion dans votre navigateur…",
                async () => connecte = await _google.ConnecterAsync());
            if (!connecte)
                return;
        }
        if (!_google.Autorise(ConnexionGoogle.PorteeDrive))
        {
            Erreur = "Le compte Google n'autorise pas Google Drive : reconnectez-le (Configuration › Compte Google) en cochant l'accès aux fichiers.";
            return;
        }
        await ChangerEmplacementAsync(new StockageGoogleDrive(_google.Http, _google.JetonAsync), EmplacementDocuments.GoogleDrive, null);
    }

    /// <summary>
    /// Passe à un autre emplacement : si le nouvel emplacement n'a pas encore de coffre, les documents actuels y sont copiés
    /// (après accord) ; s'il en a déjà un (autre PC), c'est lui qui est ouvert.
    /// </summary>
    private async Task ChangerEmplacementAsync(IStockageDocuments destination, EmplacementDocuments emplacement, string? dossier)
    {
        await Executer("Changement d'emplacement…", async () =>
        {
            var existant = await destination.LireAsync(CoffreDocuments.NomIndex);
            if (existant is null && _coffre is { Documents.Count: > 0 } actuel
                && _dialogues.Confirmer("Copier les documents",
                    $"Copier les {actuel.Documents.Count} documents actuels vers le nouvel emplacement ?\n\n{destination.Description}\n\n" +
                    "Ils restent aussi à l'ancien emplacement."))
            {
                Statut = "Copie des documents…";
                var copies = await actuel.CopierVersAsync(destination);
                Statut = $"{copies} documents copiés.";
            }

            _reglages.ChoisirEmplacementDocuments(emplacement, dossier);
            var coffre = new CoffreDocuments(destination, _iterations);
            await coffre.ChargerAsync();
            _coffre = coffre;
            Emplacement = destination.Description;
            Reconstruire();
        });
    }

    // ---- Documents ----

    [RelayCommand]
    private async Task Ajouter()
    {
        if (_coffre is null || _dialogues.ChoisirDocument() is not { } chemin)
            return;
        await Executer("Ajout du document…", async () =>
        {
            var contenu = await File.ReadAllBytesAsync(chemin);
            var document = new DocumentImportant
            {
                Nom = Path.GetFileNameWithoutExtension(chemin),
                NomFichier = Path.GetFileName(chemin),
                Categorie = CategorieProbable(Path.GetFileNameWithoutExtension(chemin)),
            };
            await _coffre.AjouterAsync(document, contenu);
            Reconstruire();
            Selection = _tous.FirstOrDefault(d => d.Modele == document);
            Statut = $"« {document.Nom} » ajouté au coffre.";
        });
    }

    [RelayCommand(CanExecute = nameof(ASelection))]
    private async Task Ouvrir()
    {
        var document = Selection!.Modele;
        if (!await DeverrouillerSiBesoinAsync(document))
            return;
        await Executer("Ouverture…", async () =>
        {
            var contenu = await _coffre!.LireAsync(document);
            var dossier = Directory.CreateDirectory(DossierTemporaire).FullName;
            var chemin = Path.Combine(dossier, NomSur(document));
            await File.WriteAllBytesAsync(chemin, contenu);
            _dialogues.OuvrirFichier(chemin);
        });
    }

    [RelayCommand(CanExecute = nameof(ASelection))]
    private async Task EnregistrerSous()
    {
        var document = Selection!.Modele;
        if (!await DeverrouillerSiBesoinAsync(document) || _dialogues.ChoisirEmplacementFichier(NomSur(document)) is not { } chemin)
            return;
        await Executer("Enregistrement…", async () =>
        {
            await File.WriteAllBytesAsync(chemin, await _coffre!.LireAsync(document));
            Statut = $"Copie enregistrée : {chemin}";
        });
    }

    /// <summary>Tous les documents du coffre (sans filtre), pour les joindre à un mail.</summary>
    public IReadOnlyList<DocumentViewModel> Tous => _tous;

    /// <summary>Demande d'envoi du document sélectionné par mail (le module Mail s'ouvre avec la pièce jointe).</summary>
    public event EventHandler<DocumentViewModel>? EnvoiParMailDemande;

    /// <summary>Le module Mail est affiché : le bouton « Envoyer par mail » est proposé.</summary>
    [ObservableProperty] private bool _envoiParMailPossible;

    [RelayCommand(CanExecute = nameof(ASelection))]
    private void EnvoyerParMail() => EnvoiParMailDemande?.Invoke(this, Selection!);

    /// <summary>Contenu d'un document pour une pièce jointe (mot de passe demandé s'il est protégé) ; null si annulé ou en erreur.</summary>
    public async Task<Data.Mail.PieceJointe?> PieceJointeAsync(DocumentViewModel fiche)
    {
        var document = fiche.Modele;
        if (!await DeverrouillerSiBesoinAsync(document))
            return null;
        Data.Mail.PieceJointe? piece = null;
        await Executer("Lecture du document…", async () => piece = new Data.Mail.PieceJointe(NomSur(document), await _coffre!.LireAsync(document)));
        return piece;
    }

    [RelayCommand(CanExecute = nameof(ASelection))]
    private async Task Remplacer()
    {
        var document = Selection!.Modele;
        if (!await DeverrouillerSiBesoinAsync(document) || _dialogues.ChoisirDocument() is not { } chemin)
            return;
        await Executer("Remplacement du fichier…", async () =>
        {
            await _coffre!.RemplacerAsync(document, await File.ReadAllBytesAsync(chemin), Path.GetFileName(chemin));
            Reconstruire();
            Statut = $"Fichier de « {document.Nom} » remplacé.";
        });
    }

    [RelayCommand(CanExecute = nameof(ASelection))]
    private async Task Supprimer()
    {
        var document = Selection!.Modele;
        if (!_dialogues.Confirmer("Supprimer le document", $"Supprimer « {document.Nom} » du coffre ? Le fichier sera effacé."))
            return;
        await Executer("Suppression…", async () =>
        {
            await _coffre!.SupprimerAsync(document);
            Reconstruire();
            Statut = $"« {document.Nom} » supprimé.";
        });
    }

    /// <summary>Protège le document sélectionné par le mot de passe du coffre, ou retire sa protection.</summary>
    [RelayCommand(CanExecute = nameof(ASelection))]
    private async Task Proteger()
    {
        var document = Selection!.Modele;
        if (_coffre is null)
            return;

        if (!document.Protege && !_coffre.ProtectionConfiguree)
        {
            var motDePasse = _dialogues.DemanderMotDePasse("Mot de passe du coffre",
                "Choisissez le mot de passe qui protégera vos documents sensibles (8 caractères au moins).\n" +
                "Une clé de secours vous sera ensuite donnée : gardez-la en lieu sûr, elle seule permet de récupérer " +
                "les documents protégés si vous oubliez le mot de passe.", confirmer: true);
            if (motDePasse is null)
                return;
            string? secours = null;
            await Executer("Création de la protection…", async () => secours = await _coffre.ConfigurerProtectionAsync(motDePasse));
            if (secours is null)
                return;
            _dialogues.AfficherCleSecours(secours);
        }
        else if (!await DeverrouillerSiBesoinAsync(null))
            return;

        var proteger = !document.Protege;
        await Executer(proteger ? "Protection du document…" : "Retrait de la protection…", async () =>
        {
            await _coffre.ProtegerAsync(document, proteger);
            Reconstruire();
            Statut = proteger ? $"« {document.Nom} » est protégé par le mot de passe." : $"« {document.Nom} » n'est plus protégé.";
        });
    }

    [RelayCommand]
    private async Task ChangerMotDePasse()
    {
        if (_coffre is not { ProtectionConfiguree: true } || !await DeverrouillerSiBesoinAsync(null))
            return;
        var nouveau = _dialogues.DemanderMotDePasse("Nouveau mot de passe", "Nouveau mot de passe du coffre (la clé de secours ne change pas).", confirmer: true);
        if (nouveau is null)
            return;
        await Executer("Changement du mot de passe…", async () =>
        {
            await _coffre.ChangerMotDePasseAsync(nouveau);
            Statut = "Mot de passe changé.";
        });
    }

    [RelayCommand]
    private async Task MotDePasseOublie()
    {
        if (_coffre is not { ProtectionConfiguree: true })
            return;
        var cle = _dialogues.DemanderNom("Mot de passe oublié", "Saisissez la clé de secours du coffre (avec ou sans tirets) :", "");
        if (string.IsNullOrWhiteSpace(cle))
            return;
        var nouveau = _dialogues.DemanderMotDePasse("Nouveau mot de passe", "Choisissez un nouveau mot de passe pour le coffre.", confirmer: true);
        if (nouveau is null)
            return;
        await Executer("Récupération…", async () =>
        {
            if (await _coffre.RecupererAsync(cle, nouveau))
                Statut = "Nouveau mot de passe enregistré : le coffre est déverrouillé.";
            else
                Erreur = "Cette clé de secours ne correspond pas au coffre.";
        });
    }

    [RelayCommand]
    private void Verrouiller()
    {
        _coffre?.Verrouiller();
        MettreAJourEtat();
        Statut = "Coffre verrouillé : le mot de passe sera redemandé.";
    }

    /// <summary>Sauvegarde du coffre dans un fichier .zip (fiches et fichiers tels quels, protégés compris).</summary>
    [RelayCommand]
    private async Task Sauvegarder()
    {
        if (_coffre is null || _dialogues.ChoisirEmplacementFichier($"Coffre documents {_aujourdHui:yyyy-MM-dd}.zip") is not { } chemin)
            return;
        await Executer("Sauvegarde du coffre…", async () =>
        {
            var dossier = Path.Combine(Path.GetTempPath(), $"gestioncompte-coffre-{Guid.NewGuid():N}");
            try
            {
                await _coffre.CopierVersAsync(new StockageDossier(dossier));
                File.Delete(chemin);
                ZipFile.CreateFromDirectory(dossier, chemin);
            }
            finally
            {
                if (Directory.Exists(dossier))
                    Directory.Delete(dossier, true);
            }
            Statut = $"Coffre sauvegardé : {chemin}";
        });
    }

    /// <summary>Demande le mot de passe si le document (ou l'action, quand <paramref name="document"/> est null) l'exige.</summary>
    private async Task<bool> DeverrouillerSiBesoinAsync(DocumentImportant? document)
    {
        if (_coffre is null)
            return false;
        if ((document is not null && !document.Protege) || _coffre.Deverrouille)
            return true;

        var motDePasse = _dialogues.DemanderMotDePasse("Coffre protégé", "Mot de passe du coffre :", confirmer: false);
        if (motDePasse is null)
            return false;
        var ouvert = false;
        await Task.Run(() => ouvert = _coffre.Deverrouiller(motDePasse)); // calcul volontairement long
        MettreAJourEtat();
        if (!ouvert)
            Erreur = "Mot de passe incorrect. En cas d'oubli : « Mot de passe oublié » avec la clé de secours.";
        return ouvert;
    }

    // ---- Fiches ----

    /// <summary>Une fiche a changé : enregistrement des fiches (l'un après l'autre).</summary>
    internal async void FicheModifiee()
    {
        Rappels = RappelsDocuments.Calculer(_tous.Select(d => d.Modele), _aujourdHui);
        MettreAJourRappels();
        if (_coffre is null)
            return;
        await _enregistrement.WaitAsync();
        try
        {
            await _coffre.EnregistrerAsync();
            Erreur = "";
        }
        catch (Exception e)
        {
            Erreur = $"Les modifications n'ont pas pu être enregistrées : {e.Message}";
        }
        finally
        {
            _enregistrement.Release();
        }
    }

    /// <summary>Attend la fin des enregistrements en cours (tests).</summary>
    public async Task AttendreEnregistrementAsync()
    {
        await _enregistrement.WaitAsync();
        _enregistrement.Release();
    }

    private void Reconstruire()
    {
        var selection = Selection?.Modele;
        _tous = (_coffre?.Documents ?? Array.Empty<DocumentImportant>())
            .OrderBy(d => d.Categorie).ThenBy(d => d.Nom, StringComparer.CurrentCultureIgnoreCase)
            .Select(d => new DocumentViewModel(d, this))
            .ToList();
        Filtrer();
        Selection = Documents.FirstOrDefault(d => d.Modele == selection) ?? Documents.FirstOrDefault();
        Rappels = RappelsDocuments.Calculer(_tous.Select(d => d.Modele), _aujourdHui);
        MettreAJourRappels();
        MettreAJourEtat();
        OnPropertyChanged(nameof(Vide));
        OnPropertyChanged(nameof(Charges));
        OnPropertyChanged(nameof(SurGoogleDrive));
    }

    private void Filtrer()
    {
        var mots = Recherche.Trim();
        Documents = _tous
            .Where(d => Filtre == Toutes || d.Categorie == Filtre)
            .Where(d => mots.Length == 0
                        || d.Nom.Contains(mots, StringComparison.CurrentCultureIgnoreCase)
                        || d.Notes.Contains(mots, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    private void MettreAJourRappels()
    {
        TitreNavigation = Rappels.Count > 0 ? $"Documents ({Rappels.Count})" : "Documents";
        OnPropertyChanged(nameof(AvecRappels));
    }

    private void MettreAJourEtat()
    {
        ProtectionConfiguree = _coffre?.ProtectionConfiguree ?? false;
        Deverrouille = _coffre?.Deverrouille ?? false;
    }

    private async Task Executer(string message, Func<Task> action)
    {
        Occupe = true;
        Erreur = "";
        Statut = message;
        try
        {
            await action();
            if (Statut == message)
                Statut = "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException
                                      or InvalidDataException or ArgumentException or CryptographicException or OperationCanceledException
                                      or System.Text.Json.JsonException)
        {
            Statut = "";
            Erreur = e is OperationCanceledException ? "Opération annulée (délai dépassé)." : e.Message;
        }
        finally
        {
            Occupe = false;
        }
    }

    /// <summary>Catégorie devinée d'après le nom du fichier (« RIB », « carte identité », « assurance »…).</summary>
    public static CategorieDocument CategorieProbable(string nom)
    {
        var n = nom.ToLowerInvariant();
        if (n.Contains("identit") || n.Contains("passeport") || n.Contains("permis") || n.Contains("cni"))
            return CategorieDocument.Identite;
        if (n.Contains("rib") || n.Contains("banque") || n.Contains("releve") || n.Contains("relevé"))
            return CategorieDocument.Banque;
        if (n.Contains("impot") || n.Contains("impôt") || n.Contains("fiscal"))
            return CategorieDocument.Impots;
        if (n.Contains("bail") || n.Contains("loyer") || n.Contains("logement") || n.Contains("habitation"))
            return CategorieDocument.Logement;
        if (n.Contains("carte grise") || n.Contains("voiture") || n.Contains("auto") || n.Contains("moto"))
            return CategorieDocument.Vehicule;
        if (n.Contains("mutuelle") || n.Contains("sante") || n.Contains("santé") || n.Contains("vitale"))
            return CategorieDocument.Sante;
        if (n.Contains("garantie") || n.Contains("facture"))
            return CategorieDocument.Garanties;
        if (n.Contains("contrat") || n.Contains("assurance"))
            return CategorieDocument.Contrats;
        return CategorieDocument.Autres;
    }

    private static string DossierTemporaire => Path.Combine(Path.GetTempPath(), "GestionCompte-documents");

    /// <summary>Les copies ouvertes lors d'une session précédente (y compris déchiffrées) sont effacées.</summary>
    private static void NettoyerFichiersTemporaires()
    {
        try
        {
            if (Directory.Exists(DossierTemporaire))
                Directory.Delete(DossierTemporaire, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Un fichier encore ouvert dans un autre programme : il sera effacé au prochain lancement.
        }
    }

    private static string NomSur(DocumentImportant document)
    {
        var nom = string.IsNullOrWhiteSpace(document.NomFichier) ? document.Nom : document.NomFichier;
        foreach (var c in Path.GetInvalidFileNameChars())
            nom = nom.Replace(c, '_');
        return nom.Length == 0 ? document.Id : nom;
    }
}

/// <summary>Fiche d'un document, modifiable dans le détail de l'écran Documents.</summary>
public sealed class DocumentViewModel : ObservableObject
{
    private readonly DocumentsViewModel _parent;

    public DocumentViewModel(DocumentImportant modele, DocumentsViewModel parent)
    {
        Modele = modele;
        _parent = parent;
    }

    public DocumentImportant Modele { get; }

    public string Nom
    {
        get => Modele.Nom;
        set => Modifier(Modele.Nom, (value ?? "").Trim(), v => Modele.Nom = v.Length == 0 ? Modele.Nom : v);
    }

    public string Categorie
    {
        get => DocumentImportant.NomCategorie(Modele.Categorie);
        set
        {
            // Une valeur inconnue (ou null, envoyée par la liste quand la fiche change) est ignorée.
            var categories = Enum.GetValues<CategorieDocument>().Where(c => DocumentImportant.NomCategorie(c) == value).ToList();
            if (categories.Count == 1)
                Modifier(Modele.Categorie, categories[0], v => Modele.Categorie = v);
        }
    }

    public DateTime? Date
    {
        get => Modele.Date?.ToDateTime(TimeOnly.MinValue);
        set => Modifier(Modele.Date, value is { } d ? DateOnly.FromDateTime(d) : null, v => Modele.Date = v);
    }

    public DateTime? Echeance
    {
        get => Modele.Echeance?.ToDateTime(TimeOnly.MinValue);
        set
        {
            Modifier(Modele.Echeance, value is { } d ? DateOnly.FromDateTime(d) : null, v => Modele.Echeance = v);
            OnPropertyChanged(nameof(EcheanceTexte));
            OnPropertyChanged(nameof(EcheanceListe));
        }
    }

    public int RappelJours
    {
        get => Modele.RappelJours;
        set => Modifier(Modele.RappelJours, Math.Clamp(value, 0, 365), v => Modele.RappelJours = v);
    }

    public string Notes
    {
        get => Modele.Notes;
        set => Modifier(Modele.Notes, value ?? "", v => Modele.Notes = v);
    }

    public string Charge
    {
        get => Modele.Charge ?? DocumentsViewModel.Aucune;
        set
        {
            if (value is null)
                return; // envoyé par la liste quand la fiche change
            Modifier(Modele.Charge, value == DocumentsViewModel.Aucune || string.IsNullOrWhiteSpace(value) ? null : value, v => Modele.Charge = v);
        }
    }

    public bool Protege => Modele.Protege;

    public string Fichier => $"{Modele.NomFichier} · {Taille(Modele.Taille)}";

    public string EcheanceTexte => Modele.Echeance?.ToString("dd/MM/yyyy", Montants.Francais) ?? "";

    /// <summary>Échéance affichée dans la liste (« échéance 01/12/2026 »), vide sans échéance.</summary>
    public string EcheanceListe => Modele.Echeance is null ? "" : $"échéance {EcheanceTexte}";

    public string TexteProtection => Modele.Protege ? "Retirer la protection" : "Protéger ce document";

    private void Modifier<T>(T actuel, T nouveau, Action<T> appliquer, [System.Runtime.CompilerServices.CallerMemberName] string? propriete = null)
    {
        if (EqualityComparer<T>.Default.Equals(actuel, nouveau))
            return;
        appliquer(nouveau);
        OnPropertyChanged(propriete);
        _parent.FicheModifiee();
    }

    private static string Taille(long octets) => octets switch
    {
        < 1024 => $"{octets} o",
        < 1024 * 1024 => string.Format(Montants.Francais, "{0:0} Ko", octets / 1024.0),
        _ => string.Format(Montants.Francais, "{0:0.0} Mo", octets / 1024.0 / 1024.0),
    };
}
