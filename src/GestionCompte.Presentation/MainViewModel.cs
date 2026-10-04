using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Core;
using GestionCompte.Core.Import;
using GestionCompte.Core.Lettres;
using GestionCompte.Core.Modeles;
using GestionCompte.Data;

namespace GestionCompte.Presentation;

/// <summary>Fenêtre principale : navigation entre les mois, configuration, sauvegardes. Enregistre après chaque modification.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    public const int OngletMois = 0;
    public const int OngletPrevisionnel = 1;
    public const int OngletAide = 2;
    public const int OngletConfiguration = 3;

    /// <summary>Aperçu d'un import de relevé (pas d'entrée dans la barre de gauche).</summary>
    public const int OngletImport = 4;

    /// <summary>Soldes de tous les comptes (affiché quand il y a plusieurs comptes).</summary>
    public const int OngletEnsemble = 5;

    /// <summary>Module « Crédits » (masquable dans la configuration).</summary>
    public const int OngletCredits = 6;

    /// <summary>Module « Bilan » (masquable dans la configuration).</summary>
    public const int OngletBilan = 7;

    /// <summary>Module « Documents » (masquable dans la configuration).</summary>
    public const int OngletDocuments = 8;

    /// <summary>Module « Mail » (masquable dans la configuration).</summary>
    public const int OngletMail = 9;

    /// <summary>Module « Achats » (masquable dans la configuration).</summary>
    public const int OngletAchats = 10;

    /// <summary>Module « Lettres » (masquable dans la configuration).</summary>
    public const int OngletLettres = 11;

    /// <summary>Module « Assistant » (masquable dans la configuration).</summary>
    public const int OngletAssistant = 12;

    /// <summary>Module « Prêts » (masquable dans la configuration).</summary>
    public const int OngletPrets = 13;

    private DepotSqlite _depot;
    private readonly RegistreComptes? _registre;
    private readonly IDialogues _dialogues;
    private readonly DateTime _aujourdhui;
    private CompteBancaire _compte = null!;
    private int _indexMois = -1;
    private bool _erreurEnregistrementSignalee;

    /// <summary>Numéro de compte bancaire du relevé en cours d'import.</summary>
    private string? _compteReleve;

    /// <summary>Application avec plusieurs comptes possibles (liste enregistrée dans le dossier des données).</summary>
    /// <param name="secrets">Identifiants gardés sur ce PC (connexion Google Drive) ; par défaut, en mémoire seulement.</param>
    public MainViewModel(RegistreComptes registre, IDialogues dialogues, DateTime aujourdHui, ApparenceViewModel? apparence = null,
        ISecretsLocaux? secrets = null)
        : this(new DepotSqlite(registre.Chemin(registre.Actif)), dialogues, aujourdHui, apparence, registre, secrets)
    {
    }

    /// <param name="aujourdHui">Date du jour : premier mois proposé et mois affiché au démarrage.</param>
    /// <param name="apparence">Choix des couleurs ; par défaut, non enregistré.</param>
    public MainViewModel(DepotSqlite depot, IDialogues dialogues, DateTime aujourdHui, ApparenceViewModel? apparence = null,
        ISecretsLocaux? secrets = null)
        : this(depot, dialogues, aujourdHui, apparence, null, secrets)
    {
    }

    private MainViewModel(DepotSqlite depot, IDialogues dialogues, DateTime aujourdHui, ApparenceViewModel? apparence,
        RegistreComptes? registre, ISecretsLocaux? secrets)
    {
        _depot = depot;
        _registre = registre;
        _dialogues = dialogues;
        _moisDuJour = new PeriodeMois(aujourdHui.Year, aujourdHui.Month);
        _aujourdhui = aujourdHui.Date;
        Apparence = apparence ?? new ApparenceViewModel(null);
        // Documents et mails sont communs à tous les comptes, à côté des données.
        var dossier = registre?.Dossier ?? Path.GetDirectoryName(Path.GetFullPath(depot.CheminFichier)) ?? ".";
        secrets ??= new SecretsEnMemoire();
        Google = new CompteGoogle(secrets, dialogues);
        Documents = new DocumentsViewModel(Apparence, dossier, Google, dialogues,
            DateOnly.FromDateTime(aujourdHui), () => _compte?.Configuration.Charges.Select(c => c.Nom).ToList() ?? new List<string>())
        {
            EnvoiParMailPossible = Apparence.ModuleMail,
        };
        Mail = new MailViewModel(Apparence, dossier, Google, secrets, dialogues, Documents, () => Bilan);
        IA = new ServicesIA(secrets, dialogues);
        Achats = new AchatsViewModel(dossier, IA, dialogues);
        Achats.AchatAPrevoir += (_, achat) => PrevoirAchat(achat);
        Lettres = new LettresViewModel(dossier, IA, dialogues, () => aujourdHui.Date) { EnvoiParMailPossible = Apparence.ModuleMail };
        Lettres.EnvoiParMailDemande += (_, lettre) =>
        {
            Mail.PreparerLettre(lettre.Objet, lettre.Texte, lettre.Piece);
            OngletSelectionne = OngletMail;
        };
        Assistant = new AssistantViewModel(IA, () => _compte is null ? "" : ResumeBudget.Construire(_compte, _moisDuJour));
        Documents.EnvoiParMailDemande += async (_, fiche) =>
        {
            if (await Mail.JoindreDocumentAsync(fiche))
                OngletSelectionne = OngletMail;
        };
        Apparence.ModulesChanges += (_, _) =>
        {
            if ((OngletSelectionne == OngletCredits && !Apparence.ModuleCredits)
                || (OngletSelectionne == OngletBilan && !Apparence.ModuleBilan)
                || (OngletSelectionne == OngletDocuments && !Apparence.ModuleDocuments)
                || (OngletSelectionne == OngletMail && !Apparence.ModuleMail)
                || (OngletSelectionne == OngletAchats && !Apparence.ModuleAchats)
                || (OngletSelectionne == OngletLettres && !Apparence.ModuleLettres)
                || (OngletSelectionne == OngletAssistant && !Apparence.ModuleAssistant)
                || (OngletSelectionne == OngletPrets && !Apparence.ModulePrets))
                OngletSelectionne = OngletConfiguration;
            Documents.EnvoiParMailPossible = Apparence.ModuleMail;
            Lettres.EnvoiParMailPossible = Apparence.ModuleMail;
            if (Bilan is not null)
                Bilan.EnvoiParMailPossible = Apparence.ModuleMail;
            Mail.ModulesModifies();
        };

        ChargerCompte(depot);
    }

    /// <summary>Ouvre les données d'un compte. Une erreur de lecture est remontée : on n'écrase jamais un fichier illisible.</summary>
    private void ChargerCompte(DepotSqlite depot)
    {
        var compte = depot.Charger(_moisDuJour);
        _depot = depot;
        Import = null;
        if (compte is null)
        {
            _compte = new CompteBancaire(ConfigurationParDefaut.Creer(_moisDuJour));
            _indexMois = -1;
            Enregistrer();
            OngletSelectionne = OngletConfiguration;
            Statut = "Bienvenue ! Vérifiez la configuration, puis créez le premier mois depuis l'onglet « Mois ».";
        }
        else
        {
            _compte = compte;
            _indexMois = IndexMoisParDefaut();
            if (OngletSelectionne == OngletImport)
                OngletSelectionne = OngletMois;
            Statut = $"Données chargées depuis {depot.CheminFichier}";
        }

        Reconstruire();
        NotifierComptes();
    }

    private readonly PeriodeMois _moisDuJour;

    // ---- Comptes ----

    /// <summary>Le choix du compte est proposé (application Windows ; pas en mode compte unique).</summary>
    public bool GestionComptes => _registre is not null;

    public bool PlusieursComptes => _registre is { Comptes.Count: > 1 };

    public IReadOnlyList<string> NomsComptes =>
        _registre?.Comptes.Select(c => c.Nom).ToList() ?? new List<string> { RegistreComptes.NomPrincipal };

    /// <summary>Nom du compte ouvert ; le changer ouvre l'autre compte.</summary>
    public string CompteActif
    {
        get => _registre?.Actif.Nom ?? RegistreComptes.NomPrincipal;
        set
        {
            var cible = _registre?.Comptes.FirstOrDefault(c => c.Nom == value);
            if (cible is null || cible == _registre!.Actif)
                return;
            ChangerDeCompte(cible);
        }
    }

    [ObservableProperty] private VueEnsembleViewModel? _ensemble;

    partial void OnOngletSelectionneChanged(int value)
    {
        if (value == OngletEnsemble)
            Ensemble = CalculerEnsemble();
        else if (value == OngletAssistant)
            Assistant.ActualiserResume();
    }

    private void NotifierComptes()
    {
        OnPropertyChanged(nameof(NomsComptes));
        OnPropertyChanged(nameof(CompteActif));
        OnPropertyChanged(nameof(PlusieursComptes));
        OnPropertyChanged(nameof(GestionComptes));
        RenommerCompteCommand.NotifyCanExecuteChanged();
        SupprimerCompteCommand.NotifyCanExecuteChanged();
        if (OngletSelectionne == OngletEnsemble)
        {
            if (PlusieursComptes)
                Ensemble = CalculerEnsemble();
            else
                OngletSelectionne = OngletMois;
        }
    }

    private void ChangerDeCompte(EntreeCompte cible)
    {
        Enregistrer();
        var depot = new DepotSqlite(_registre!.Chemin(cible));
        try
        {
            depot.Charger(_moisDuJour);
        }
        catch (Exception e)
        {
            _dialogues.Erreur($"Le compte « {cible.Nom} » n'a pas pu être ouvert.\n\n{e.Message}");
            OnPropertyChanged(nameof(CompteActif));
            return;
        }

        _registre.DefinirActif(cible);
        ChargerCompte(depot);
        Statut = $"Compte « {cible.Nom} » ouvert.";
    }

    /// <summary>Compte de la liste, lu depuis son fichier (le compte ouvert est pris en mémoire).</summary>
    private CompteBancaire? LireCompte(EntreeCompte entree) =>
        entree == _registre?.Actif ? _compte : new DepotSqlite(_registre!.Chemin(entree)).Charger(_moisDuJour);

    [RelayCommand(CanExecute = nameof(GestionComptes))]
    private void NouveauCompte()
    {
        var demande = _dialogues.DemanderNouveauCompte(NomsComptes, CompteActif);
        if (demande is null)
            return;
        if (!_registre!.NomDisponible(demande.Nom))
        {
            _dialogues.Erreur(string.IsNullOrWhiteSpace(demande.Nom)
                ? "Donnez un nom au compte."
                : $"Un compte s'appelle déjà « {demande.Nom.Trim()} ».");
            return;
        }

        ConfigurationBudget configuration;
        try
        {
            var modele = demande.CopierDe is null ? null : _registre.Comptes.FirstOrDefault(c => c.Nom == demande.CopierDe);
            configuration = modele is null
                ? ConfigurationParDefaut.Creer(_moisDuJour)
                : (LireCompte(modele)?.Configuration ?? ConfigurationParDefaut.Creer(_moisDuJour)).CopierPourNouveauCompte(_moisDuJour);
        }
        catch (Exception e)
        {
            _dialogues.Erreur($"La configuration à copier n'a pas pu être lue.\n\n{e.Message}");
            return;
        }

        Enregistrer();
        var entree = _registre.Ajouter(demande.Nom);
        new DepotSqlite(_registre.Chemin(entree)).Enregistrer(new CompteBancaire(configuration));
        ChangerDeCompte(entree);
        OngletSelectionne = OngletConfiguration;
        Statut = $"Compte « {entree.Nom} » créé : vérifiez le premier mois et le solde de départ, puis créez le premier mois.";
    }

    [RelayCommand(CanExecute = nameof(GestionComptes))]
    private void RenommerCompte()
    {
        var actif = _registre!.Actif;
        var nom = _dialogues.DemanderNom("Renommer le compte", "Nouveau nom du compte :", actif.Nom);
        if (nom is null || nom.Trim() == actif.Nom)
            return;
        if (!_registre.NomDisponible(nom, actif))
        {
            _dialogues.Erreur(string.IsNullOrWhiteSpace(nom) ? "Donnez un nom au compte." : $"Un compte s'appelle déjà « {nom.Trim()} ».");
            return;
        }

        _registre.Renommer(actif, nom);
        NotifierComptes();
        Statut = $"Compte renommé en « {_registre.Actif.Nom} ».";
    }

    [RelayCommand(CanExecute = nameof(PlusieursComptes))]
    private void SupprimerCompte()
    {
        var actif = _registre!.Actif;
        if (!_dialogues.Confirmer("Supprimer un compte",
                $"Supprimer définitivement le compte « {actif.Nom} » et toutes ses données " +
                "(mois, configuration, copies de sécurité) ?\n\n" +
                "Pour les garder, annulez et faites d'abord « Enregistrer une copie… »."))
            return;

        try
        {
            _registre.Supprimer(actif);
        }
        catch (Exception e)
        {
            _dialogues.Erreur($"Le compte n'a pas pu être supprimé.\n\n{e.Message}");
            return;
        }

        ChargerCompte(new DepotSqlite(_registre.Chemin(_registre.Actif)));
        Statut = $"Compte « {actif.Nom} » supprimé ; compte « {_registre.Actif.Nom} » ouvert.";
    }

    private VueEnsembleViewModel CalculerEnsemble()
    {
        var comptes = new List<CompteEnsemble>();
        var illisibles = new List<string>();
        foreach (var entree in _registre?.Comptes ?? (IReadOnlyList<EntreeCompte>)Array.Empty<EntreeCompte>())
        {
            try
            {
                if (LireCompte(entree) is { } compte)
                    comptes.Add(new CompteEnsemble(entree.Nom, compte, entree == _registre!.Actif));
            }
            catch (Exception)
            {
                illisibles.Add(entree.Nom);
            }
        }

        return new VueEnsembleViewModel(comptes, _moisDuJour, illisibles);
    }

    public string CheminDonnees => _depot.CheminFichier;

    public ApparenceViewModel Apparence { get; }

    [ObservableProperty] private ConfigurationViewModel _configuration = null!;

    [ObservableProperty] private PrevisionnelViewModel _previsionnel = null!;

    [ObservableProperty] private AideBudgetViewModel _aideBudget = null!;
    [ObservableProperty] private CreditsViewModel _credits = null!;
    [ObservableProperty] private PretsViewModel _prets = null!;
    [ObservableProperty] private BilanViewModel _bilan = null!;

    /// <summary>Documents importants, communs à tous les comptes.</summary>
    public DocumentsViewModel Documents { get; }

    /// <summary>Compte Google (Google Drive, Gmail, contacts), propre à ce PC.</summary>
    public CompteGoogle Google { get; }

    /// <summary>Envoi de mails, carnet d'adresses et historique, communs à tous les comptes.</summary>
    public MailViewModel Mail { get; }

    /// <summary>IA gratuites (Gemini, Groq) avec les clés de l'utilisateur, propres à ce PC.</summary>
    public ServicesIA IA { get; }

    /// <summary>Aide à l'achat : recherche du meilleur prix, sites, prix suivis.</summary>
    public AchatsViewModel Achats { get; }

    /// <summary>Lettres types rédigées par les IA, communes à tous les comptes.</summary>
    public LettresViewModel Lettres { get; }

    /// <summary>Assistant budgétaire (questions aux IA sur un résumé des chiffres du compte ouvert).</summary>
    public AssistantViewModel Assistant { get; }

    /// <summary>« Prévoir l'achat » : ajouté au prévisionnel du compte ouvert, au mois choisi.</summary>
    private void PrevoirAchat(DemandeAchat achat)
    {
        var saisie = _dialogues.DemanderAchatPrevu(achat.Libelle, achat.Prix, Previsionnel.Periodes);
        if (saisie is null)
            return;
        _compte.OperationsPrevues.Add(new OperationPrevue(saisie.Periode, saisie.Libelle, debit: saisie.Montant));
        Enregistrer();
        Previsionnel = new PrevisionnelViewModel(_compte, OperationsPrevuesModifiees) { Horizon = Previsionnel.Horizon };
        AideBudget.Recalculer();
        Credits.RafraichirTout();
        OngletSelectionne = OngletPrevisionnel;
        Statut = $"Achat « {saisie.Libelle} » prévu en {saisie.Periode.Libelle} ({Montants.Formater(saisie.Montant)} €) sur le compte « {CompteActif} ».";
    }

    /// <summary>Import de relevé en cours d'aperçu, ou null.</summary>
    [ObservableProperty] private ImportViewModel? _import;

    private int _ongletAvantImport = OngletMois;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AucunMois), nameof(TitreMois))]
    private MoisViewModel? _moisCourant;

    [ObservableProperty] private int _ongletSelectionne = OngletMois;

    [ObservableProperty] private string _statut = "";

    /// <summary>Numéro de version de l'application (celui du .exe), affiché dans la barre de gauche.</summary>
    public string Version { get; } = "Version " + (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "");

    public bool AucunMois => _compte.Mois.Count == 0;

    public string TitreMois => MoisCourant?.Titre ?? "Aucun mois";

    public string TexteCreerMois => AucunMois
        ? $"Créer le premier mois ({_compte.Configuration.PremierMois.Libelle})"
        : $"Créer {_compte.Mois[^1].Periode.Suivant().Libelle}";

    [RelayCommand(CanExecute = nameof(PeutAllerAuMoisPrecedent))]
    private void MoisPrecedent() => AllerAuMois(_indexMois - 1);

    [RelayCommand(CanExecute = nameof(PeutAllerAuMoisSuivant))]
    private void MoisSuivant() => AllerAuMois(_indexMois + 1);

    private bool PeutAllerAuMoisPrecedent() => _indexMois > 0;

    private bool PeutAllerAuMoisSuivant() => _indexMois < _compte.Mois.Count - 1;

    /// <summary>Mois créés, proposés dans la liste déroulante du bandeau.</summary>
    /// <remarks>
    /// La liste n'est recréée que si les mois changent : la remplacer pendant un choix dans la liste déroulante
    /// relançait la sélection en boucle et faisait planter l'application.
    /// </remarks>
    public IReadOnlyList<ChoixPeriode> ListeMois { get; private set; } = Array.Empty<ChoixPeriode>();

    /// <summary>Mois affiché, choisi dans la liste déroulante.</summary>
    public ChoixPeriode? MoisSelectionne
    {
        get => _indexMois >= 0 && _indexMois < ListeMois.Count ? ListeMois[_indexMois] : null;
        set
        {
            var index = value is null ? -1 : _compte.Mois.ToList().FindIndex(m => m.Periode == value.Periode);
            if (index >= 0 && index != _indexMois)
                AllerAuMois(index);
        }
    }

    private void MettreAJourListeMois()
    {
        if (ListeMois.Select(c => c.Periode).SequenceEqual(_compte.Mois.Select(m => m.Periode)))
            return;
        ListeMois = _compte.Mois.Select(m => new ChoixPeriode(m.Periode)).ToList();
        OnPropertyChanged(nameof(ListeMois));
    }

    /// <summary>Revient au mois du jour, s'il est créé.</summary>
    [RelayCommand(CanExecute = nameof(PeutAllerAuMoisEnCours))]
    private void AllerAuMoisEnCours() => AllerAuMois(IndexMoisEnCours);

    private bool PeutAllerAuMoisEnCours() => IndexMoisEnCours >= 0 && IndexMoisEnCours != _indexMois;

    private int IndexMoisEnCours => _compte.Mois.ToList().FindIndex(m => m.Periode == _moisDuJour);

    /// <summary>Mois affiché à l'ouverture : le mois en cours, sinon le dernier mois créé qui le précède (sinon le premier).</summary>
    private int IndexMoisParDefaut()
    {
        if (_compte.Mois.Count == 0)
            return -1;
        var avant = _compte.Mois.ToList().FindLastIndex(m => m.Periode <= _moisDuJour);
        return avant >= 0 ? avant : 0;
    }

    [RelayCommand]
    private void CreerMois()
    {
        var mois = _compte.CreerMoisSuivant();
        _indexMois = _compte.Mois.Count - 1;
        Enregistrer();
        Reconstruire();
        OngletSelectionne = OngletMois;
        Statut = $"{mois.Periode.Libelle} créé.";
    }

    [RelayCommand(CanExecute = nameof(PeutSupprimerDernierMois))]
    private void SupprimerDernierMois()
    {
        var dernier = _compte.Mois[^1].Periode;
        if (!_dialogues.Confirmer("Supprimer un mois",
                $"Supprimer {dernier.Libelle} et toutes ses opérations ?\nCette action est définitive."))
            return;

        _compte.SupprimerDernierMois();
        _indexMois = Math.Min(_indexMois, _compte.Mois.Count - 1);
        Enregistrer();
        Reconstruire();
        Statut = $"{dernier.Libelle} supprimé.";
    }

    private bool PeutSupprimerDernierMois() => !AucunMois;

    [RelayCommand(CanExecute = nameof(PeutAppliquerConfiguration))]
    private void AppliquerConfiguration()
    {
        var mois = _compte.Mois[_indexMois];
        if (!_dialogues.Confirmer("Appliquer la configuration",
                $"Mettre à jour {mois.Periode.Libelle} avec la configuration actuelle ?\n\n" +
                "• les montants des charges mensuelles et les budgets des enveloppes seront remplacés ;\n" +
                "• les charges, enveloppes et revenus manquants seront ajoutés ;\n" +
                "• vos autres opérations et les montants de vos revenus ne changent pas."))
            return;

        _compte.AppliquerConfiguration(mois);
        Enregistrer();
        Reconstruire();
        Statut = $"Configuration appliquée à {mois.Periode.Libelle}.";
    }

    private bool PeutAppliquerConfiguration() => _indexMois >= 0;

    [RelayCommand]
    private void SauvegarderCopie()
    {
        var destination = _dialogues.ChoisirFichierSauvegarde($"compte-sauvegarde-{DateTime.Now:yyyy-MM-dd}.db");
        if (destination is null)
            return;

        try
        {
            Enregistrer();
            _depot.Sauvegarder(destination);
            Statut = $"Copie enregistrée : {destination}";
        }
        catch (Exception e)
        {
            _dialogues.Erreur($"La copie n'a pas pu être enregistrée.\n\n{e.Message}");
        }
    }

    [RelayCommand]
    private void Restaurer()
    {
        var source = _dialogues.ChoisirFichierARestaurer();
        if (source is null)
            return;

        CompteBancaire? restaure;
        try
        {
            restaure = new DepotSqlite(source).Charger();
        }
        catch (Exception e)
        {
            _dialogues.Erreur($"Ce fichier n'a pas pu être lu.\n\n{e.Message}");
            return;
        }

        if (restaure is null)
        {
            _dialogues.Erreur("Ce fichier ne contient pas de données de l'application.");
            return;
        }

        if (!_dialogues.Confirmer("Restaurer une sauvegarde",
                $"Remplacer toutes les données actuelles par celles de :\n{source}\n\n" +
                "Une copie des données actuelles sera d'abord enregistrée à côté du fichier de données."))
            return;

        try
        {
            if (_depot.Existe && !MemeFichier(source, _depot.CheminFichier))
                _depot.Sauvegarder(_depot.CheminFichier + ".avant-restauration.db");
        }
        catch (Exception e)
        {
            _dialogues.Erreur($"La copie de sécurité n'a pas pu être faite ; restauration annulée.\n\n{e.Message}");
            return;
        }

        _compte = restaure;
        _indexMois = _compte.Mois.Count - 1;
        Enregistrer();
        Reconstruire();
        Statut = $"Sauvegarde restaurée depuis {source}";
    }

    [RelayCommand]
    private void Reinitialiser()
    {
        var demande = _dialogues.ChoisirReinitialisation();
        if (demande is null)
            return;

        if (demande.Choix == ChoixReinitialisation.EffacerMois)
            EffacerLesMois();
        else
            ToutEffacer(demande.CopieAvant);
    }

    /// <summary>Repart de zéro avec la même configuration (nouvelle année, nouveau départ…).</summary>
    private void EffacerLesMois()
    {
        if (!_dialogues.Confirmer("Effacer les mois",
                $"Effacer les {_compte.Mois.Count} mois et les opérations prévues ?\n\n" +
                "• la configuration (charges, revenus, enveloppes, règles) et les objectifs d'épargne sont gardés ;\n" +
                $"• le premier mois devient {_moisDuJour.Libelle} : vérifiez ensuite le solde de départ et les montants " +
                "déjà cumulés dans la Configuration.\n\n" +
                "Une copie de sécurité des données actuelles est d'abord enregistrée à côté du fichier de données."))
            return;

        try
        {
            Enregistrer();
            _depot.Sauvegarder(_depot.CheminFichier + ".avant-reinitialisation.db");
        }
        catch (Exception e)
        {
            _dialogues.Erreur($"La copie de sécurité n'a pas pu être faite ; rien n'a été effacé.\n\n{e.Message}");
            return;
        }

        var configuration = _compte.Configuration;
        configuration.PremierMois = _moisDuJour;
        var nouveau = new CompteBancaire(configuration);
        nouveau.ObjectifsEpargne.AddRange(_compte.ObjectifsEpargne);
        nouveau.SimulationsCredit.AddRange(_compte.SimulationsCredit);
        nouveau.Prets.AddRange(_compte.Prets);
        Repartir(nouveau);
        Statut = "Mois effacés : vérifiez le premier mois et le solde de départ, puis créez le premier mois.";
    }

    /// <summary>Efface toutes les données, pour passer l'application à une autre personne.</summary>
    private void ToutEffacer(bool copieAvant)
    {
        if (copieAvant)
        {
            var destination = _dialogues.ChoisirFichierSauvegarde($"compte-sauvegarde-{DateTime.Now:yyyy-MM-dd}.db");
            if (destination is null)
                return;
            try
            {
                Enregistrer();
                _depot.Sauvegarder(destination);
            }
            catch (Exception e)
            {
                _dialogues.Erreur($"La copie n'a pas pu être enregistrée ; rien n'a été effacé.\n\n{e.Message}");
                return;
            }
        }

        if (!_dialogues.Confirmer("Tout effacer",
                "Dernière confirmation : toutes les données vont être définitivement effacées de ce PC " +
                "(tous les comptes : mois, configuration, règles, opérations prévues, objectifs, copies de sécurité automatiques " +
                "et apparence).\n\nL'application repartira d'une configuration vierge. Continuer ?"))
            return;

        try
        {
            if (_registre is not null)
            {
                _registre.EffacerTout();
                _depot = new DepotSqlite(_registre.Chemin(_registre.Actif));
            }
            else
            {
                _depot.EffacerTout();
            }
        }
        catch (Exception e)
        {
            _dialogues.Erreur($"Les données n'ont pas pu être effacées (fichier ouvert ailleurs ?).\n\n{e.Message}");
            return;
        }

        Apparence.Reinitialiser();
        Repartir(new CompteBancaire(ConfigurationParDefaut.Creer(_moisDuJour)));
        NotifierComptes();
        Statut = "Toutes les données ont été effacées. Bienvenue ! Vérifiez la configuration, puis créez le premier mois.";
    }

    private void Repartir(CompteBancaire compte)
    {
        Import = null;
        _compte = compte;
        _indexMois = -1;
        Enregistrer();
        Reconstruire();
        OngletSelectionne = OngletConfiguration;
    }

    [RelayCommand(CanExecute = nameof(PeutExporterMois))]
    private void ExporterMois()
    {
        var periode = _compte.Mois[_indexMois].Periode;
        var destination = _dialogues.ChoisirFichierExport($"Budget {periode.Libelle}.xlsx");
        if (destination is null)
            return;

        try
        {
            ExportExcel.ExporterMois(_compte, periode, destination);
            Statut = $"{periode.Libelle} exporté : {destination}";
        }
        catch (Exception e)
        {
            _dialogues.Erreur($"Le fichier Excel n'a pas pu être créé (est-il ouvert dans Excel ?).\n\n{e.Message}");
        }
    }

    private bool PeutExporterMois() => _indexMois >= 0;

    [RelayCommand]
    private void ImporterReleve()
    {
        var chemin = _dialogues.ChoisirReleve();
        if (chemin is not null)
            OuvrirImport(chemin);
    }

    /// <summary>Lit un relevé OFX et affiche l'aperçu de l'import (rien n'est modifié avant validation).</summary>
    public void OuvrirImport(string chemin)
    {
        ReleveBancaire releve;
        try
        {
            releve = ReleveOfx.Lire(chemin);
        }
        catch (Exception e)
        {
            _dialogues.Erreur($"Ce relevé n'a pas pu être lu. Vérifiez qu'il s'agit bien d'un fichier OFX (« Money »).\n\n{e.Message}");
            return;
        }

        if (releve.Operations.Count == 0)
        {
            _dialogues.Erreur("Ce relevé ne contient aucune opération.");
            return;
        }

        if (!VerifierCompteDuReleve(releve.Compte))
            return;

        _compteReleve = releve.Compte;
        // Chevauchement de relevé : les opérations datées d'un mois antérieur vont par défaut dans le mois en cours.
        var plan = ImportReleve.Preparer(_compte, releve, _moisDuJour);
        if (OngletSelectionne != OngletImport)
            _ongletAvantImport = OngletSelectionne;
        Import = new ImportViewModel(_compte, plan, Path.GetFileName(chemin), _dialogues, ImportTermine, ImportAnnule,
            dansLeurMois => ImportReleve.Preparer(_compte, releve, _moisDuJour, dansLeurMois));
        OngletSelectionne = OngletImport;
    }

    /// <summary>
    /// Vérifie que le relevé est bien celui du compte ouvert (numéro ACCTID) ; propose d'ouvrir le bon compte sinon.
    /// Renvoie false si l'import est abandonné.
    /// </summary>
    private bool VerifierCompteDuReleve(string? numero)
    {
        if (numero is null)
            return true;

        EntreeCompte? proprietaire = null;
        if (_registre is not null)
        {
            foreach (var entree in _registre.Comptes.Where(c => c != _registre.Actif))
            {
                try
                {
                    if (LireCompte(entree)?.IdentifiantBanque == numero)
                    {
                        proprietaire = entree;
                        break;
                    }
                }
                catch (Exception)
                {
                    // Un compte illisible est ignoré ici ; l'erreur apparaîtra à son ouverture.
                }
            }
        }

        if (proprietaire is not null)
        {
            if (!_dialogues.Confirmer("Relevé d'un autre compte",
                    $"Ce relevé (compte bancaire n° {Masquer(numero)}) correspond au compte « {proprietaire.Nom} ».\n\n" +
                    $"Ouvrir « {proprietaire.Nom} » pour l'importer ?"))
                return false;
            ChangerDeCompte(proprietaire);
            return _registre!.Actif == proprietaire;
        }

        return _compte.IdentifiantBanque is not { } attendu || attendu == numero
               || _dialogues.Confirmer("Relevé d'un autre compte bancaire",
                   $"Ce relevé vient du compte bancaire n° {Masquer(numero)}, alors que « {CompteActif} » reçoit " +
                   $"d'habitude les relevés du n° {Masquer(attendu)}.\n\nL'importer quand même dans « {CompteActif} » ?");
    }

    /// <summary>Numéro de compte abrégé (4 derniers caractères) pour les messages.</summary>
    private static string Masquer(string numero) => numero.Length <= 4 ? numero : "…" + numero[^4..];

    private void ImportTermine(ResultatImport resultat)
    {
        // Le premier relevé importé fixe le numéro de compte bancaire attendu pour ce compte.
        _compte.IdentifiantBanque ??= _compteReleve;

        var soldeBanque = Import?.SoldeBanque;
        var soldePointe = Import?.SoldePointeApres ?? 0m;
        var concernes = Import?.Lignes.Where(l => l.Importer && l.Modifiable).Select(l => l.Ligne.Periode).ToList() ?? new List<PeriodeMois>();
        Import = null;

        // Affiche le mois en cours s'il a reçu des opérations, sinon le dernier mois concerné par l'import.
        var cible = concernes.Contains(_moisDuJour) ? _moisDuJour
            : concernes.Count > 0 ? concernes.Max()
            : resultat.MoisCrees.Count > 0 ? resultat.MoisCrees[^1] : (PeriodeMois?)null;
        var index = cible is { } p ? _compte.Mois.ToList().FindIndex(m => m.Periode == p) : -1;
        _indexMois = index >= 0 ? index : IndexMoisParDefaut();

        Enregistrer();
        Reconstruire();
        OngletSelectionne = OngletMois;

        var texte = $"Import terminé : {resultat.Rapprochees} rapprochée(s), {resultat.Ajustees} ajustée(s), " +
                    $"{resultat.RevenusRecus} revenu(s) reçu(s), {resultat.Nouvelles} nouvelle(s)";
        if (resultat.MoisCrees.Count > 0)
            texte += $", mois créé(s) : {string.Join(", ", resultat.MoisCrees.Select(m => m.Libelle))}";
        if (soldeBanque is { } solde)
            texte += $". Solde banque : {Montants.Formater(solde)} €, solde pointé : {Montants.Formater(soldePointe)} €";
        Statut = texte + ".";
    }

    private void ImportAnnule()
    {
        Import = null;
        OngletSelectionne = _ongletAvantImport;
        Statut = "Import annulé : aucune donnée modifiée.";
    }

    [RelayCommand]
    private void OuvrirDossierDonnees() => _dialogues.OuvrirDossier(Path.GetDirectoryName(_depot.CheminFichier)!);

    private void AllerAuMois(int index)
    {
        if (index < 0 || index >= _compte.Mois.Count)
            return;

        _indexMois = index;
        AfficherMoisCourant();
    }

    /// <summary>Recrée les écrans après un changement de structure (mois créé, supprimé, restauré…).</summary>
    private void Reconstruire()
    {
        Configuration = new ConfigurationViewModel(_compte.Configuration, premierMoisModifiable: AucunMois, ConfigurationModifiee, _moisDuJour)
            { Apparence = Apparence, Google = Google, IA = IA };
        ReconstruirePrevisionnel();
        AfficherMoisCourant();
    }

    private void ReconstruirePrevisionnel()
    {
        var horizon = Previsionnel?.Horizon ?? 12;
        Previsionnel = new PrevisionnelViewModel(_compte, OperationsPrevuesModifiees) { Horizon = horizon };
        AideBudget = new AideBudgetViewModel(_compte, _dialogues, Enregistrer, ConfigurationRemplacee, _moisDuJour.Suivant());
        Credits = new CreditsViewModel(_compte, AideBudget.Periodes, _dialogues, Enregistrer, CreditAjouteOuRetire, IA, () => _aujourdhui);
        // Même prêt sélectionné après un changement de la configuration (ex. « Ajouter aux charges »).
        var pretSelectionne = Prets is { } anciens && ReferenceEquals(anciens.Compte, _compte) ? anciens.IndexSelection : 0;
        Prets = new PretsViewModel(_compte, _moisDuJour, _dialogues, Enregistrer, PretAjouteAuxCharges, pretSelectionne);
        Bilan = new BilanViewModel(_compte, _moisDuJour, _dialogues) { EnvoiParMailPossible = Apparence.ModuleMail };
        Bilan.EnvoiParMailDemande += (_, _) =>
        {
            if (Mail.JoindreBilanPdf())
                OngletSelectionne = OngletMail;
        };
        Documents?.ChargesModifiees();
        Assistant?.ActualiserResume();
    }

    /// <summary>Échéances d'un crédit ajoutées ou retirées depuis le module Crédits.</summary>
    private void CreditAjouteOuRetire()
    {
        Enregistrer();
        Previsionnel = new PrevisionnelViewModel(_compte, OperationsPrevuesModifiees) { Horizon = Previsionnel.Horizon };
        AideBudget.Recalculer();
        Bilan.Recalculer();
        AfficherMoisCourant();
        Statut = "Prévisionnel mis à jour avec la simulation de crédit.";
    }

    /// <summary>Échéance d'un prêt ajoutée aux charges de la configuration depuis le module Prêts.</summary>
    private void PretAjouteAuxCharges()
    {
        Enregistrer();
        Reconstruire();
        Statut = "Charge du prêt enregistrée dans la configuration : elle sera reprise dans chaque nouveau mois.";
    }

    private void OperationsPrevuesModifiees()
    {
        Enregistrer();
        AideBudget.Recalculer();
        Credits.RafraichirTout();
    }

    /// <summary>La configuration a été modifiée depuis l'aide au budget : tous les écrans sont recréés.</summary>
    private void ConfigurationRemplacee()
    {
        Enregistrer();
        Reconstruire();
        Statut = "Configuration mise à jour depuis l'aide au budget.";
    }

    private void AfficherMoisCourant()
    {
        MoisCourant = _indexMois >= 0 ? new MoisViewModel(_compte, _compte.Mois[_indexMois], MoisModifie, Apparence) : null;

        OnPropertyChanged(nameof(TexteCreerMois));
        MettreAJourListeMois();
        OnPropertyChanged(nameof(MoisSelectionne));
        AllerAuMoisEnCoursCommand.NotifyCanExecuteChanged();
        MoisPrecedentCommand.NotifyCanExecuteChanged();
        MoisSuivantCommand.NotifyCanExecuteChanged();
        SupprimerDernierMoisCommand.NotifyCanExecuteChanged();
        AppliquerConfigurationCommand.NotifyCanExecuteChanged();
        ExporterMoisCommand.NotifyCanExecuteChanged();
    }

    private void MoisModifie()
    {
        Enregistrer();
        Previsionnel.Recalculer();
        AideBudget.Recalculer();
        Credits.RafraichirTout();
        Bilan.Recalculer();
    }

    private void ConfigurationModifiee()
    {
        Enregistrer();
        // Le solde initial, les charges et les comptes cumulés changent les chiffres du mois affiché et du prévisionnel.
        ReconstruirePrevisionnel();
        AfficherMoisCourant();
    }

    private void Enregistrer()
    {
        try
        {
            _depot.Enregistrer(_compte);
            _erreurEnregistrementSignalee = false;
            Statut = $"Enregistré à {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception e)
        {
            Statut = $"ERREUR : les modifications ne sont pas enregistrées ({e.Message})";
            if (!_erreurEnregistrementSignalee)
            {
                _erreurEnregistrementSignalee = true;
                _dialogues.Erreur($"Les données n'ont pas pu être enregistrées dans\n{_depot.CheminFichier}\n\n{e.Message}");
            }
        }
    }

    private static bool MemeFichier(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
