using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Core;
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

    private readonly DepotSqlite _depot;
    private readonly IDialogues _dialogues;
    private CompteBancaire _compte;
    private int _indexMois = -1;
    private bool _erreurEnregistrementSignalee;

    /// <param name="aujourdHui">Date du jour : premier mois proposé et mois affiché au démarrage.</param>
    /// <param name="apparence">Choix des couleurs ; par défaut, non enregistré.</param>
    public MainViewModel(DepotSqlite depot, IDialogues dialogues, DateTime aujourdHui, ApparenceViewModel? apparence = null)
    {
        _depot = depot;
        _dialogues = dialogues;
        Apparence = apparence ?? new ApparenceViewModel(null);

        // Une erreur de lecture est remontée à l'appelant : on n'écrase jamais un fichier illisible.
        var compte = depot.Charger();
        if (compte is null)
        {
            _compte = new CompteBancaire(ConfigurationParDefaut.Creer(new PeriodeMois(aujourdHui.Year, aujourdHui.Month)));
            Enregistrer();
            OngletSelectionne = OngletConfiguration;
            Statut = "Bienvenue ! Vérifiez la configuration, puis créez le premier mois depuis l'onglet « Mois ».";
        }
        else
        {
            _compte = compte;
            var moisDuJour = new PeriodeMois(aujourdHui.Year, aujourdHui.Month);
            var index = _compte.Mois.ToList().FindIndex(m => m.Periode == moisDuJour);
            _indexMois = index >= 0 ? index : _compte.Mois.Count - 1;
            Statut = $"Données chargées depuis {depot.CheminFichier}";
        }

        Reconstruire();
    }

    public string CheminDonnees => _depot.CheminFichier;

    public ApparenceViewModel Apparence { get; }

    [ObservableProperty] private ConfigurationViewModel _configuration = null!;

    [ObservableProperty] private PrevisionnelViewModel _previsionnel = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AucunMois), nameof(TitreMois))]
    private MoisViewModel? _moisCourant;

    [ObservableProperty] private int _ongletSelectionne = OngletMois;

    [ObservableProperty] private string _statut = "";

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
        Configuration = new ConfigurationViewModel(_compte.Configuration, premierMoisModifiable: AucunMois, ConfigurationModifiee);
        ReconstruirePrevisionnel();
        AfficherMoisCourant();
    }

    private void ReconstruirePrevisionnel()
    {
        var horizon = Previsionnel?.Horizon ?? 12;
        Previsionnel = new PrevisionnelViewModel(_compte, Enregistrer) { Horizon = horizon };
    }

    private void AfficherMoisCourant()
    {
        MoisCourant = _indexMois >= 0 ? new MoisViewModel(_compte, _compte.Mois[_indexMois], MoisModifie) : null;

        OnPropertyChanged(nameof(TexteCreerMois));
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
