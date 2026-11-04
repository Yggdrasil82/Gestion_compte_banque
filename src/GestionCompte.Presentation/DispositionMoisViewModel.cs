using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GestionCompte.Presentation;

/// <summary>Opérations montrées dans la vue épurée du mois.</summary>
public enum FiltreOperations
{
    APointer,
    Pointees,
    Toutes,
}

/// <summary>Disposition enregistrée de la vue épurée du mois (réglage propre au PC).</summary>
public sealed class DispositionMois
{
    public List<string> Gauche { get; set; } = new();

    public List<string> Droite { get; set; } = new();

    public List<string> Masques { get; set; } = new();

    /// <summary>
    /// Éléments changés par rapport à l'origine (voir <see cref="DispositionMoisViewModel"/>, constantes « Element… ») :
    /// un élément affiché au départ y figure quand il est masqué, un élément masqué au départ y figure précédé de « + » quand il est affiché.
    /// </summary>
    public List<string> ElementsMasques { get; set; } = new();

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FiltreOperations Filtre { get; set; } = FiltreOperations.APointer;
}

/// <summary>Un module de la vue épurée du mois (carte déplaçable).</summary>
public sealed record ModuleMois(string Id, string Nom);

/// <summary>
/// Vue épurée du mois : place des modules (colonne de gauche, de droite ou masqués), éléments affichés et filtre des opérations.
/// Chaque changement est enregistré aussitôt.
/// </summary>
public sealed partial class DispositionMoisViewModel : ObservableObject
{
    public const string Operations = "operations";
    public const string Revenus = "revenus";
    public const string Enveloppes = "enveloppes";
    public const string Detail = "detail";
    public const string Cumuls = "cumuls";

    public const string ElementRevenus = "revenus";
    public const string ElementDepenses = "depenses";
    public const string ElementEpargne = "epargne";
    public const string ElementBarres = "barres";
    public const string ElementCategorie = "categorie";
    public const string ElementEnveloppe = "enveloppe";
    public const string ElementCompteCumul = "cumul";
    public const string ElementVirement = "virement";
    public const string ElementSolde = "solde";

    /// <summary>Débit et crédit dans deux colonnes ; sinon une seule colonne « Montant ».</summary>
    public const string ElementDebitCreditSepares = "separes";

    private static readonly ModuleMois[] Tous =
    {
        new(Operations, "Opérations"),
        new(Revenus, "Revenus du mois"),
        new(Enveloppes, "Enveloppes"),
        new(Detail, "Détail du solde"),
        new(Cumuls, "Comptes cumulés"),
    };

    /// <summary>Éléments affichés tant que l'utilisateur ne les a pas masqués (les autres sont masqués au départ).</summary>
    private static readonly string[] AffichesParDefaut =
    {
        ElementRevenus, ElementDepenses, ElementEpargne, ElementBarres, ElementCategorie, ElementEnveloppe,
        ElementCompteCumul, ElementVirement, ElementSolde,
    };

    private readonly DispositionMois _reglage;
    private readonly Action _enregistrer;

    /// <param name="reglage">Disposition enregistrée (modifiée en place).</param>
    /// <param name="enregistrer">Enregistre les préférences après un changement.</param>
    public DispositionMoisViewModel(DispositionMois reglage, Action enregistrer)
    {
        _reglage = reglage;
        _enregistrer = enregistrer;
        Charger();
    }

    /// <summary>Modules de la colonne de gauche (large), de haut en bas.</summary>
    public ObservableCollection<ModuleMois> Gauche { get; } = new();

    /// <summary>Modules de la colonne de droite (étroite), de haut en bas.</summary>
    public ObservableCollection<ModuleMois> Droite { get; } = new();

    /// <summary>Modules masqués, à remettre depuis le panneau « Personnaliser ».</summary>
    public ObservableCollection<ModuleMois> Masques { get; } = new();

    public bool AMasques => Masques.Count > 0;

    /// <summary>Mode « Personnaliser » : poignées de déplacement, boutons pour masquer, panneau des éléments.</summary>
    [ObservableProperty] private bool _enEdition;

    [RelayCommand]
    private void BasculerEdition() => EnEdition = !EnEdition;

    private void Charger()
    {
        Gauche.Clear();
        Droite.Clear();
        Masques.Clear();
        var places = new HashSet<string>();
        void Remplir(IEnumerable<string> ids, ObservableCollection<ModuleMois> cible)
        {
            foreach (var id in ids)
            {
                var module = Tous.FirstOrDefault(m => m.Id == id);
                if (module is not null && places.Add(id))
                    cible.Add(module);
            }
        }

        Remplir(_reglage.Gauche, Gauche);
        Remplir(_reglage.Droite, Droite);
        Remplir(_reglage.Masques, Masques);
        // Disposition d'origine pour les modules jamais placés (premier lancement ou nouveau module).
        foreach (var module in Tous.Where(m => !places.Contains(m.Id)))
            (module.Id == Operations ? Gauche : Droite).Add(module);
        OnPropertyChanged(nameof(AMasques));
    }

    private void Enregistrer()
    {
        _reglage.Gauche = Gauche.Select(m => m.Id).ToList();
        _reglage.Droite = Droite.Select(m => m.Id).ToList();
        _reglage.Masques = Masques.Select(m => m.Id).ToList();
        OnPropertyChanged(nameof(AMasques));
        _enregistrer();
    }

    /// <summary>Place un module dans une colonne (« gauche » ou « droite »), à la position donnée (fin de colonne si hors limites).</summary>
    public void Deplacer(string id, bool aGauche, int position)
    {
        var module = Tous.FirstOrDefault(m => m.Id == id);
        if (module is null)
            return;
        var cible = aGauche ? Gauche : Droite;
        var ancienne = cible.IndexOf(module);
        if (ancienne >= 0 && ancienne < position)
            position--;
        Gauche.Remove(module);
        Droite.Remove(module);
        Masques.Remove(module);
        cible.Insert(Math.Clamp(position, 0, cible.Count), module);
        Enregistrer();
    }

    [RelayCommand]
    private void Masquer(ModuleMois? module)
    {
        if (module is null || Masques.Contains(module))
            return;
        Gauche.Remove(module);
        Droite.Remove(module);
        Masques.Add(module);
        Enregistrer();
    }

    /// <summary>Remet un module masqué, en bas de sa colonne d'origine.</summary>
    [RelayCommand]
    private void Remettre(ModuleMois? module)
    {
        if (module is null || !Masques.Remove(module))
            return;
        (module.Id == Operations ? Gauche : Droite).Add(module);
        Enregistrer();
    }

    /// <summary>Revient à la disposition et aux éléments d'origine.</summary>
    [RelayCommand]
    private void RevenirOrigine()
    {
        _reglage.Gauche.Clear();
        _reglage.Droite.Clear();
        _reglage.Masques.Clear();
        _reglage.ElementsMasques.Clear();
        Charger();
        Enregistrer();
        OnPropertyChanged(string.Empty);
    }

    private bool Affiche(string element) =>
        AffichesParDefaut.Contains(element) ? !_reglage.ElementsMasques.Contains(element) : _reglage.ElementsMasques.Contains("+" + element);

    private void Afficher(string element, bool affiche, string propriete)
    {
        if (Affiche(element) == affiche)
            return;
        var cle = AffichesParDefaut.Contains(element) ? element : "+" + element;
        var masqueDansListe = AffichesParDefaut.Contains(element) ? !affiche : affiche;
        if (masqueDansListe)
            _reglage.ElementsMasques.Add(cle);
        else
            _reglage.ElementsMasques.Remove(cle);
        _enregistrer();
        OnPropertyChanged(propriete);
    }

    /// <summary>Barre du haut : total des revenus.</summary>
    public bool AfficherRevenus { get => Affiche(ElementRevenus); set => Afficher(ElementRevenus, value, nameof(AfficherRevenus)); }

    /// <summary>Barre du haut : dépenses prévues.</summary>
    public bool AfficherDepenses { get => Affiche(ElementDepenses); set => Afficher(ElementDepenses, value, nameof(AfficherDepenses)); }

    /// <summary>Barre du haut : épargne cumulée.</summary>
    public bool AfficherEpargne { get => Affiche(ElementEpargne); set => Afficher(ElementEpargne, value, nameof(AfficherEpargne)); }

    /// <summary>Enveloppes : barres de progression (sinon seulement le budget et le reste).</summary>
    public bool AfficherBarres { get => Affiche(ElementBarres); set => Afficher(ElementBarres, value, nameof(AfficherBarres)); }

    public bool AfficherCategorie { get => Affiche(ElementCategorie); set => Afficher(ElementCategorie, value, nameof(AfficherCategorie)); }

    public bool AfficherEnveloppe { get => Affiche(ElementEnveloppe); set => Afficher(ElementEnveloppe, value, nameof(AfficherEnveloppe)); }

    public bool AfficherCompteCumul { get => Affiche(ElementCompteCumul); set => Afficher(ElementCompteCumul, value, nameof(AfficherCompteCumul)); }

    public bool AfficherVirement { get => Affiche(ElementVirement); set => Afficher(ElementVirement, value, nameof(AfficherVirement)); }

    public bool AfficherSolde { get => Affiche(ElementSolde); set => Afficher(ElementSolde, value, nameof(AfficherSolde)); }

    /// <summary>Débit et crédit dans deux colonnes (sinon une seule colonne « Montant », débits en rouge et crédits en vert).</summary>
    public bool DebitCreditSepares
    {
        get => Affiche(ElementDebitCreditSepares);
        set => Afficher(ElementDebitCreditSepares, value, nameof(DebitCreditSepares));
    }

    public FiltreOperations Filtre
    {
        get => _reglage.Filtre;
        set
        {
            if (_reglage.Filtre == value)
                return;
            _reglage.Filtre = value;
            _enregistrer();
            OnPropertyChanged();
            OnPropertyChanged(nameof(FiltreAPointer));
            OnPropertyChanged(nameof(FiltrePointees));
            OnPropertyChanged(nameof(FiltreToutes));
        }
    }

    public bool FiltreAPointer { get => Filtre == FiltreOperations.APointer; set { if (value) Filtre = FiltreOperations.APointer; } }

    public bool FiltrePointees { get => Filtre == FiltreOperations.Pointees; set { if (value) Filtre = FiltreOperations.Pointees; } }

    public bool FiltreToutes { get => Filtre == FiltreOperations.Toutes; set { if (value) Filtre = FiltreOperations.Toutes; } }

    /// <summary>Une opération est montrée par le filtre choisi.</summary>
    public bool Montre(bool pointee) => Filtre switch
    {
        FiltreOperations.APointer => !pointee,
        FiltreOperations.Pointees => pointee,
        _ => true,
    };
}
