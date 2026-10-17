using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GestionCompte.Presentation;

/// <summary>Où le coffre des documents range ses fichiers.</summary>
public enum EmplacementDocuments
{
    /// <summary>Dossier « Documents » à côté des données de l'application.</summary>
    Local,

    /// <summary>Dossier choisi, par exemple synchronisé par Google Drive pour ordinateur.</summary>
    Dossier,

    /// <summary>Google Drive, connexion directe depuis l'application.</summary>
    GoogleDrive,
}

/// <summary>Compte utilisé pour envoyer les mails.</summary>
public enum ModeEnvoiMail
{
    /// <summary>Gmail, avec la connexion au compte Google.</summary>
    Gmail,

    /// <summary>Serveur d'envoi (SMTP) réglé à la main : Orange, Free…</summary>
    Smtp,
}

public enum Ambiance
{
    Ocean,
    Pastel,
    Nuit,
}

/// <summary>Préférences d'affichage, enregistrées à part des données du compte.</summary>
public sealed class PreferencesAffichage
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Ambiance Ambiance { get; set; } = Ambiance.Ocean;

    public bool ModeSombre { get; set; }

    /// <summary>Onglet « Crédits » affiché dans la barre de gauche.</summary>
    public bool ModuleCredits { get; set; } = true;

    /// <summary>Onglet « Bilan » affiché dans la barre de gauche.</summary>
    public bool ModuleBilan { get; set; } = true;

    /// <summary>Onglet « Documents » affiché dans la barre de gauche.</summary>
    public bool ModuleDocuments { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EmplacementDocuments EmplacementDocuments { get; set; } = EmplacementDocuments.Local;

    /// <summary>Dossier du coffre quand <see cref="EmplacementDocuments"/> vaut Dossier.</summary>
    public string? DossierDocuments { get; set; }

    /// <summary>Onglet « Mail » affiché dans la barre de gauche.</summary>
    public bool ModuleMail { get; set; } = true;

    /// <summary>Onglet « Achats » affiché dans la barre de gauche.</summary>
    public bool ModuleAchats { get; set; } = true;

    /// <summary>Onglet « Lettres » affiché dans la barre de gauche.</summary>
    public bool ModuleLettres { get; set; } = true;

    /// <summary>Animation du logo à l'ouverture de l'application.</summary>
    public bool AnimationDemarrage { get; set; } = true;

    /// <summary>Opérations du mois rangées par catégorie (sinon dans l'ordre de saisie).</summary>
    public bool RangerParCategorie { get; set; } = true;

    /// <summary>Onglet « Assistant » affiché dans la barre de gauche.</summary>
    public bool ModuleAssistant { get; set; } = true;

    /// <summary>Onglet « Prêts » affiché dans la barre de gauche.</summary>
    public bool ModulePrets { get; set; } = true;

    /// <summary>Onglet « Bourse » affiché dans la barre de gauche.</summary>
    public bool ModuleBourse { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ModeEnvoiMail ModeEnvoiMail { get; set; } = ModeEnvoiMail.Gmail;

    /// <summary>
    /// Modules affichés et leur ordre, pour chaque compte (clé : fichier du compte). Les réglages « Module… » ci-dessus,
    /// d'avant la version 2.11, servent de point de départ au premier compte ouvert.
    /// </summary>
    public Dictionary<string, ModulesCompte> ModulesParCompte { get; set; } = new();

    /// <summary>Lien CSV de la feuille des réponses au formulaire de suggestions (seulement chez l'auteur de l'application).</summary>
    public string? LienReponsesSuggestions { get; set; }

    /// <summary>Date de la dernière suggestion déjà vue (les suivantes sont signalées comme nouvelles).</summary>
    public DateTime? SuggestionsVuesJusquA { get; set; }

    /// <summary>Cartes de la Configuration réduites (titre seul), par nom de carte.</summary>
    public List<string> CartesReduites { get; set; } = new();

    /// <summary>Lit les préférences ; valeurs par défaut si le fichier est absent ou illisible.</summary>
    public static PreferencesAffichage Charger(string? chemin)
    {
        if (chemin is null || !File.Exists(chemin))
            return new PreferencesAffichage();

        try
        {
            return JsonSerializer.Deserialize<PreferencesAffichage>(File.ReadAllText(chemin)) ?? new PreferencesAffichage();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new PreferencesAffichage();
        }
    }

    public void Enregistrer(string chemin)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(chemin))!);
        File.WriteAllText(chemin, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>Modules masqués et ordre des modules dans la barre de gauche, pour un compte.</summary>
public sealed class ModulesCompte
{
    public List<string> Masques { get; set; } = new();

    public List<string> Ordre { get; set; } = new();

    public ModulesCompte Copier() => new() { Masques = new List<string>(Masques), Ordre = new List<string>(Ordre) };
}

/// <summary>Un module dans la liste de la Configuration (case à cocher et flèches d'ordre).</summary>
public sealed partial class ModuleAffiche : ObservableObject
{
    private readonly ApparenceViewModel _apparence;

    internal ModuleAffiche(ApparenceViewModel apparence, string id, string nom, string description)
    {
        _apparence = apparence;
        Id = id;
        Nom = nom;
        Description = description;
    }

    public string Id { get; }

    public string Nom { get; }

    public string Description { get; }

    public bool Affiche
    {
        get => _apparence.Affiche(Id);
        set => _apparence.Afficher(Id, value);
    }

    internal void Rafraichir() => OnPropertyChanged(nameof(Affiche));

    [RelayCommand]
    private void Monter() => _apparence.Deplacer(Id, -1);

    [RelayCommand]
    private void Descendre() => _apparence.Deplacer(Id, 1);
}

/// <summary>Choix de l'ambiance de couleurs, du mode sombre et des modules affichés (réglages propres au PC).</summary>
public sealed class ApparenceViewModel : ObservableObject
{
    private readonly string? _chemin;
    private readonly PreferencesAffichage _preferences;

    /// <param name="chemin">Fichier des préférences ; null = rien n'est enregistré (captures, tests).</param>
    public ApparenceViewModel(string? chemin)
    {
        _chemin = chemin;
        _preferences = PreferencesAffichage.Charger(chemin);
        _modules = ModulesAnciens();
        Modules = new ObservableCollection<ModuleAffiche>(OrdreModules.Select(id => Liste.First(m => m.Id == id)));
    }

    /// <summary>Les modules de la barre de gauche, dans leur ordre par défaut.</summary>
    private static readonly (string Id, string Nom, string Description)[] ListeModules =
    {
        ("Bilan", "Bilan", "bilan annuel, pistes d'économie, exports Excel et PDF"),
        ("Documents", "Documents", "documents importants, échéances, Google Drive"),
        ("Mail", "Mail", "envoi de mails (Gmail ou autre messagerie), carnet d'adresses"),
        ("Achats", "Achats", "meilleur prix par les IA, sites marchands, prix suivis"),
        ("Lettres", "Lettres", "lettres types rédigées par les IA, PDF, Word ou mail"),
        ("Assistant", "Assistant", "questions sur votre budget aux IA (ne modifie rien)"),
        ("Credits", "Crédits", "simulation et comparaison de crédits"),
        ("Prets", "Prêts", "prêts en cours, tableau d'amortissement, remboursement anticipé"),
        ("Bourse", "Bourse", "placements Trade Republic, positions, plus-values, cours"),
        ("Impots", "Impôts", "estimation de l'impôt sur le revenu et pistes pour le réduire"),
    };

    private ModulesCompte _modules;
    private string? _compte;
    private IReadOnlyList<ModuleAffiche>? _liste;

    private IReadOnlyList<ModuleAffiche> Liste =>
        _liste ??= ListeModules.Select(m => new ModuleAffiche(this, m.Id, m.Nom, m.Description)).ToList();

    /// <summary>Modules du compte ouvert, dans l'ordre de la barre de gauche (liste de la Configuration).</summary>
    public ObservableCollection<ModuleAffiche> Modules { get; }

    /// <summary>Identifiants des modules du compte ouvert, dans l'ordre de la barre de gauche.</summary>
    public IReadOnlyList<string> OrdreModules =>
        _modules.Ordre.Where(id => ListeModules.Any(m => m.Id == id)).Distinct()
            .Concat(ListeModules.Select(m => m.Id).Where(id => !_modules.Ordre.Contains(id)))
            .ToList();

    /// <summary>Réglages d'avant la version 2.11 (communs à tous les comptes) : point de départ du premier compte.</summary>
    private ModulesCompte ModulesAnciens()
    {
        var p = _preferences;
        var affiches = new Dictionary<string, bool>
        {
            ["Bilan"] = p.ModuleBilan, ["Documents"] = p.ModuleDocuments, ["Mail"] = p.ModuleMail, ["Achats"] = p.ModuleAchats,
            ["Lettres"] = p.ModuleLettres, ["Assistant"] = p.ModuleAssistant, ["Credits"] = p.ModuleCredits,
            ["Prets"] = p.ModulePrets, ["Bourse"] = p.ModuleBourse,
        };
        return new ModulesCompte
        {
            Masques = affiches.Where(a => !a.Value).Select(a => a.Key).ToList(),
            Ordre = ListeModules.Select(m => m.Id).ToList(),
        };
    }

    /// <summary>
    /// Modules du compte ouvert (clé : son fichier). Un compte qui n'a pas encore de réglage reprend les modules du compte
    /// ouvert jusque-là.
    /// </summary>
    public void ChoisirCompte(string compte)
    {
        if (compte == _compte)
            return;
        _compte = compte;
        if (!_preferences.ModulesParCompte.TryGetValue(compte, out var modules))
        {
            modules = _modules.Copier();
            _preferences.ModulesParCompte[compte] = modules;
            Enregistrer();
        }
        _modules = modules;
        ModulesModifies(ordre: true);
    }

    /// <summary>Compte supprimé : ses modules sont oubliés (un futur compte du même fichier repart du compte ouvert).</summary>
    public void OublierCompte(string compte)
    {
        if (_preferences.ModulesParCompte.Remove(compte))
            Enregistrer();
    }

    public bool Affiche(string id) => !_modules.Masques.Contains(id);

    public void Afficher(string id, bool affiche)
    {
        if (Affiche(id) == affiche)
            return;
        if (affiche)
            _modules.Masques.Remove(id);
        else
            _modules.Masques.Add(id);
        Enregistrer();
        ModulesModifies(ordre: false);
    }

    /// <summary>Monte (sens -1) ou descend (sens +1) un module dans la barre de gauche.</summary>
    public void Deplacer(string id, int sens)
    {
        var ordre = OrdreModules.ToList();
        var index = ordre.IndexOf(id);
        var cible = index + sens;
        if (index < 0 || cible < 0 || cible >= ordre.Count)
            return;
        (ordre[index], ordre[cible]) = (ordre[cible], ordre[index]);
        _modules.Ordre = ordre;
        Enregistrer();
        ModulesModifies(ordre: true);
    }

    private void ModulesModifies(bool ordre)
    {
        foreach (var m in ListeModules)
            OnPropertyChanged("Module" + m.Id);
        if (ordre)
        {
            var ids = OrdreModules;
            for (var i = 0; i < ids.Count; i++)
            {
                var module = Liste.First(m => m.Id == ids[i]);
                var actuel = Modules.IndexOf(module);
                if (actuel != i)
                    Modules.Move(actuel, i);
            }
            OnPropertyChanged(nameof(OrdreModules));
        }
        foreach (var module in Liste)
            module.Rafraichir();
        ModulesChanges?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Levé quand l'apparence à afficher change.</summary>
    public event EventHandler? Changee;

    public static IReadOnlyList<ChoixAmbiance> Ambiances { get; } = new[]
    {
        new ChoixAmbiance(Ambiance.Ocean, "Océan"),
        new ChoixAmbiance(Ambiance.Pastel, "Pastel"),
        new ChoixAmbiance(Ambiance.Nuit, "Nuit"),
    };

    public Ambiance Ambiance
    {
        get => _preferences.Ambiance;
        set
        {
            if (!SetProperty(_preferences.Ambiance, value, _preferences, (p, v) => p.Ambiance = v))
                return;
            OnPropertyChanged(nameof(ModeSombreModifiable));
            OnPropertyChanged(nameof(Sombre));
            Appliquer();
        }
    }

    /// <summary>Mode sombre demandé (sans effet pour Nuit, toujours sombre).</summary>
    public bool ModeSombre
    {
        get => _preferences.ModeSombre;
        set
        {
            if (!SetProperty(_preferences.ModeSombre, value, _preferences, (p, v) => p.ModeSombre = v))
                return;
            OnPropertyChanged(nameof(Sombre));
            Appliquer();
        }
    }

    /// <summary>Module « Crédits » affiché (simulations conservées même s'il est masqué).</summary>
    public bool ModuleCredits
    {
        get => Affiche("Credits");
        set => Afficher("Credits", value);
    }

    /// <summary>Module « Bilan » affiché.</summary>
    public bool ModuleBilan
    {
        get => Affiche("Bilan");
        set => Afficher("Bilan", value);
    }

    /// <summary>Module « Documents » affiché.</summary>
    public bool ModuleDocuments
    {
        get => Affiche("Documents");
        set => Afficher("Documents", value);
    }

    /// <summary>Module « Mail » affiché.</summary>
    public bool ModuleMail
    {
        get => Affiche("Mail");
        set => Afficher("Mail", value);
    }

    /// <summary>Module « Achats » affiché.</summary>
    public bool ModuleAchats
    {
        get => Affiche("Achats");
        set => Afficher("Achats", value);
    }

    /// <summary>Module « Lettres » affiché.</summary>
    public bool ModuleLettres
    {
        get => Affiche("Lettres");
        set => Afficher("Lettres", value);
    }

    /// <summary>Module « Prêts » affiché (prêts conservés même s'il est masqué).</summary>
    public bool ModulePrets
    {
        get => Affiche("Prets");
        set => Afficher("Prets", value);
    }

    /// <summary>Module « Bourse » affiché (portefeuille conservé même s'il est masqué).</summary>
    public bool ModuleBourse
    {
        get => Affiche("Bourse");
        set => Afficher("Bourse", value);
    }

    /// <summary>Module « Impôts » affiché (réglages conservés même s'il est masqué).</summary>
    public bool ModuleImpots
    {
        get => Affiche("Impots");
        set => Afficher("Impots", value);
    }

    /// <summary>Module « Assistant » affiché.</summary>
    public bool ModuleAssistant
    {
        get => Affiche("Assistant");
        set => Afficher("Assistant", value);
    }

    /// <summary>La carte de la Configuration est réduite à son titre.</summary>
    public bool CarteReduite(string carte) => _preferences.CartesReduites.Contains(carte);

    public void ReduireCarte(string carte, bool reduite)
    {
        if (reduite == CarteReduite(carte))
            return;
        if (reduite)
            _preferences.CartesReduites.Add(carte);
        else
            _preferences.CartesReduites.Remove(carte);
        Enregistrer();
    }

    /// <summary>Animation du canard à l'ouverture de l'application (réglage propre au PC).</summary>
    public bool AnimationDemarrage
    {
        get => _preferences.AnimationDemarrage;
        set
        {
            if (SetProperty(_preferences.AnimationDemarrage, value, _preferences, (p, v) => p.AnimationDemarrage = v))
                Enregistrer();
        }
    }

    /// <summary>Opérations du mois rangées par catégorie, ou dans l'ordre de saisie (réglage propre au PC).</summary>
    public bool RangerParCategorie
    {
        get => _preferences.RangerParCategorie;
        set
        {
            if (SetProperty(_preferences.RangerParCategorie, value, _preferences, (p, v) => p.RangerParCategorie = v))
                Enregistrer();
        }
    }

    /// <summary>Compte d'envoi des mails (réglage propre au PC).</summary>
    public ModeEnvoiMail ModeEnvoiMail
    {
        get => _preferences.ModeEnvoiMail;
        set
        {
            if (SetProperty(_preferences.ModeEnvoiMail, value, _preferences, (p, v) => p.ModeEnvoiMail = v))
                Enregistrer();
        }
    }

    /// <summary>Lien CSV des réponses au formulaire de suggestions ; vide = pas d'onglet Suggestions.</summary>
    public string LienReponsesSuggestions
    {
        get => _preferences.LienReponsesSuggestions ?? "";
        set
        {
            var lien = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (SetProperty(_preferences.LienReponsesSuggestions, lien, _preferences, (p, v) => p.LienReponsesSuggestions = v))
                Enregistrer();
        }
    }

    public DateTime? SuggestionsVuesJusquA
    {
        get => _preferences.SuggestionsVuesJusquA;
        set
        {
            if (SetProperty(_preferences.SuggestionsVuesJusquA, value, _preferences, (p, v) => p.SuggestionsVuesJusquA = v))
                Enregistrer();
        }
    }

    /// <summary>Emplacement du coffre des documents (réglage propre au PC).</summary>
    public EmplacementDocuments EmplacementDocuments => _preferences.EmplacementDocuments;

    public string? DossierDocuments => _preferences.DossierDocuments;

    public void ChoisirEmplacementDocuments(EmplacementDocuments emplacement, string? dossier)
    {
        _preferences.EmplacementDocuments = emplacement;
        _preferences.DossierDocuments = dossier;
        Enregistrer();
        OnPropertyChanged(nameof(EmplacementDocuments));
        OnPropertyChanged(nameof(DossierDocuments));
    }

    /// <summary>Levé quand un module est affiché ou masqué.</summary>
    public event EventHandler? ModulesChanges;

    /// <summary>Revient à l'apparence par défaut (Océan, clair, tous les modules affichés dans leur ordre d'origine).</summary>
    public void Reinitialiser()
    {
        _modules.Masques.Clear();
        _modules.Ordre = ListeModules.Select(m => m.Id).ToList();
        _preferences.ModulesParCompte.Clear();
        if (_compte is not null)
            _preferences.ModulesParCompte[_compte] = _modules;
        Enregistrer();
        ModulesModifies(ordre: true);
        AnimationDemarrage = true;
        ModeSombre = false;
        Ambiance = Ambiance.Ocean;
    }

    /// <summary>L'ambiance Nuit est toujours sombre : l'option n'a alors pas d'effet.</summary>
    public bool ModeSombreModifiable => Ambiance != Ambiance.Nuit;

    /// <summary>Apparence réellement affichée en sombre.</summary>
    public bool Sombre => Ambiance == Ambiance.Nuit || ModeSombre;

    private void Appliquer()
    {
        Enregistrer();
        Changee?.Invoke(this, EventArgs.Empty);
    }

    private void Enregistrer()
    {
        if (_chemin is not null)
        {
            try
            {
                _preferences.Enregistrer(_chemin);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Une préférence d'affichage non enregistrée n'est pas grave : elle reste active jusqu'à la fermeture.
            }
        }
    }
}

public sealed record ChoixAmbiance(Ambiance Valeur, string Nom)
{
    public override string ToString() => Nom;
}
