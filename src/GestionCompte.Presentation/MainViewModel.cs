using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Core;
using GestionCompte.Core.Import;
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
        _moisDuJour = new PeriodeMois(aujourdHui.Year, aujourdHui.Month);
        Apparence = apparence ?? new ApparenceViewModel(null);

        // Une erreur de lecture est remontée à l'appelant : on n'écrase jamais un fichier illisible.
        var compte = depot.Charger(new PeriodeMois(aujourdHui.Year, aujourdHui.Month));
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

    private readonly PeriodeMois _moisDuJour;

    public string CheminDonnees => _depot.CheminFichier;

    public ApparenceViewModel Apparence { get; }

    [ObservableProperty] private ConfigurationViewModel _configuration = null!;

    [ObservableProperty] private PrevisionnelViewModel _previsionnel = null!;

    [ObservableProperty] private AideBudgetViewModel _aideBudget = null!;

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
                "(mois, configuration, règles, opérations prévues, objectifs, copies de sécurité automatiques " +
                "et apparence).\n\nL'application repartira d'une configuration vierge. Continuer ?"))
            return;

        try
        {
            _depot.EffacerTout();
        }
        catch (Exception e)
        {
            _dialogues.Erreur($"Les données n'ont pas pu être effacées (fichier ouvert ailleurs ?).\n\n{e.Message}");
            return;
        }

        Apparence.Reinitialiser();
        Repartir(new CompteBancaire(ConfigurationParDefaut.Creer(_moisDuJour)));
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

        var plan = ImportReleve.Preparer(_compte, releve);
        if (OngletSelectionne != OngletImport)
            _ongletAvantImport = OngletSelectionne;
        Import = new ImportViewModel(_compte, plan, Path.GetFileName(chemin), _dialogues, ImportTermine, ImportAnnule);
        OngletSelectionne = OngletImport;
    }

    private void ImportTermine(ResultatImport resultat)
    {
        var soldeBanque = Import?.SoldeBanque;
        var soldePointe = Import?.SoldePointeApres ?? 0m;
        Import = null;

        // Affiche le dernier mois concerné par l'import.
        var derniere = resultat.MoisCrees.Count > 0 ? resultat.MoisCrees[^1] : (PeriodeMois?)null;
        _indexMois = derniere is { } p ? _compte.Mois.ToList().FindIndex(m => m.Periode == p) : Math.Max(_indexMois, _compte.Mois.Count - 1);

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
        Configuration = new ConfigurationViewModel(_compte.Configuration, premierMoisModifiable: AucunMois, ConfigurationModifiee, _moisDuJour);
        ReconstruirePrevisionnel();
        AfficherMoisCourant();
    }

    private void ReconstruirePrevisionnel()
    {
        var horizon = Previsionnel?.Horizon ?? 12;
        Previsionnel = new PrevisionnelViewModel(_compte, OperationsPrevuesModifiees) { Horizon = horizon };
        AideBudget = new AideBudgetViewModel(_compte, _dialogues, Enregistrer, ConfigurationRemplacee);
    }

    private void OperationsPrevuesModifiees()
    {
        Enregistrer();
        AideBudget.Recalculer();
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
        AideBudget.Recalculer();
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
