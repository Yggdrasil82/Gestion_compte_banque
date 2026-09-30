using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GestionCompte.Core;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Presentation;

/// <summary>Écran Configuration. Chaque modification est reportée aussitôt dans la configuration du compte.</summary>
public sealed partial class ConfigurationViewModel : ObservableObject
{
    private readonly ConfigurationBudget _configuration;
    private readonly Action _modifiee;

    /// <summary>Réglages propres au PC (modules affichés), montrés dans la carte « Modules ».</summary>
    public ApparenceViewModel? Apparence { get; init; }

    /// <summary>Compte Google (réglage propre à ce PC, partagé par les modules Documents et Mail).</summary>
    public CompteGoogle? Google { get; init; }

    /// <param name="moisDuJour">Premier mois proposé dans la colonne « À partir de » des charges (par défaut, le premier mois).</param>
    public ConfigurationViewModel(ConfigurationBudget configuration, bool premierMoisModifiable, Action modifiee,
        PeriodeMois? moisDuJour = null)
    {
        _configuration = configuration;
        _modifiee = modifiee;
        PremierMoisModifiable = premierMoisModifiable;

        // 24 mois à partir d'aujourd'hui, plus les mois de départ déjà choisis.
        var mois = new List<PeriodeMois> { moisDuJour ?? configuration.PremierMois };
        while (mois.Count < 24)
            mois.Add(mois[^1].Suivant());
        MoisDepart = mois
            .Concat(configuration.Charges.Where(c => c.Depart is not null).Select(c => c.Depart!.Value))
            .Distinct()
            .Order()
            .ToList();

        Revenus = new ListeEditable<ElementConfigViewModel>(
            configuration.Revenus.Select(r => new ElementConfigViewModel(r.Nom, r.MontantParDefaut, Synchroniser)),
            () => new ElementConfigViewModel("Nouveau revenu", 0m, Synchroniser),
            Synchroniser);

        Enveloppes = new ListeEditable<ElementConfigViewModel>(
            configuration.Enveloppes.Select(e => new ElementConfigViewModel(e.Nom, e.BudgetParDefaut, Synchroniser, e.Categorie)),
            () => new ElementConfigViewModel("Nouvelle enveloppe", 0m, Synchroniser, Categorie.Essentiel),
            Synchroniser);

        Charges = new ListeEditable<ChargeConfigViewModel>(
            configuration.Charges.Select(c => new ChargeConfigViewModel(c, Synchroniser, MoisDepart[0])),
            () => new ChargeConfigViewModel(new ModeleCharge("Nouvelle charge", 0m), Synchroniser, MoisDepart[0]),
            Synchroniser);

        ComptesCumul = new ListeEditable<CompteCumulConfigViewModel>(
            configuration.ComptesCumul.Select(c => new CompteCumulConfigViewModel(c, Synchroniser)),
            () => new CompteCumulConfigViewModel(new CompteCumul("Nouveau compte"), Synchroniser),
            Synchroniser);

        Regles = new ListeEditable<RegleConfigViewModel>(
            configuration.Regles.Select(r => new RegleConfigViewModel(r, Synchroniser)),
            () => new RegleConfigViewModel(new RegleClassement("MOT-CLÉ", configuration.Enveloppes.FirstOrDefault()?.Nom ?? ""), Synchroniser),
            Synchroniser);
    }

    public static IReadOnlyList<string> NomsMois { get; } =
        Enumerable.Range(1, 12).Select(m => CultureInfo.GetCultureInfo("fr-FR").TextInfo.ToTitleCase(
            new DateTime(2000, m, 1).ToString("MMMM", CultureInfo.GetCultureInfo("fr-FR")))).ToList();

    /// <summary>Le premier mois ne peut plus changer une fois des mois créés.</summary>
    public bool PremierMoisModifiable { get; }

    /// <summary>Mois de début, de 0 (janvier) à 11 (décembre).</summary>
    public int IndexMoisDebut
    {
        get => _configuration.PremierMois.Mois - 1;
        set
        {
            if (value is < 0 or > 11 || value == IndexMoisDebut)
                return;
            _configuration.PremierMois = new PeriodeMois(_configuration.PremierMois.Annee, value + 1);
            OnPropertyChanged();
            _modifiee();
        }
    }

    public int AnneeDebut
    {
        get => _configuration.PremierMois.Annee;
        set
        {
            if (value is < 1900 or > 9999 || value == AnneeDebut)
                return;
            _configuration.PremierMois = new PeriodeMois(value, _configuration.PremierMois.Mois);
            OnPropertyChanged();
            _modifiee();
        }
    }

    public decimal SoldeInitial
    {
        get => _configuration.SoldeInitial;
        set { if (SetProperty(_configuration.SoldeInitial, value, _configuration, (c, v) => c.SoldeInitial = v)) _modifiee(); }
    }

    /// <summary>Choix de la colonne « Catégorie » (règle 50/30/20).</summary>
    public static IReadOnlyList<ChoixCategorie> Categories => ChoixCategorie.Tous;

    /// <summary>Choix de la colonne « Fréquence » des charges.</summary>
    public static IReadOnlyList<ChoixFrequence> Frequences => ChoixFrequence.Toutes;

    /// <summary>Choix de la colonne « À partir de » des charges qui ne sont pas prélevées tous les mois.</summary>
    public IReadOnlyList<PeriodeMois> MoisDepart { get; }

    public ListeEditable<ElementConfigViewModel> Revenus { get; }

    public ListeEditable<ElementConfigViewModel> Enveloppes { get; }

    public ListeEditable<ChargeConfigViewModel> Charges { get; }

    public ListeEditable<CompteCumulConfigViewModel> ComptesCumul { get; }

    /// <summary>Règles de classement des opérations importées (mot-clé du libellé → enveloppe).</summary>
    public ListeEditable<RegleConfigViewModel> Regles { get; }

    /// <summary>Choix de la colonne « Enveloppe » des règles.</summary>
    public IReadOnlyList<string> NomsEnveloppes => Enveloppes.Elements.Select(e => e.Nom).ToList();

    /// <summary>Choix possibles dans la colonne « Compte cumulé » des charges (vide = aucun).</summary>
    public IReadOnlyList<string> NomsComptesCumul =>
        new[] { "" }.Concat(ComptesCumul.Elements.Select(c => c.Nom)).ToList();

    private void Synchroniser()
    {
        _configuration.Revenus.Clear();
        _configuration.Revenus.AddRange(Revenus.Elements.Select(r => new ModeleRevenu(r.Nom, r.Montant)));
        _configuration.Enveloppes.Clear();
        _configuration.Enveloppes.AddRange(Enveloppes.Elements.Select(e => new ModeleEnveloppe(e.Nom, e.Montant, e.Categorie.Valeur)));
        _configuration.Charges.Clear();
        _configuration.Charges.AddRange(Charges.Elements.Select(c => c.VersModele()));
        _configuration.ComptesCumul.Clear();
        _configuration.ComptesCumul.AddRange(ComptesCumul.Elements.Select(c => c.VersModele()));
        _configuration.Regles.Clear();
        _configuration.Regles.AddRange(Regles.Elements
            .Where(r => !string.IsNullOrWhiteSpace(r.MotCle) && !string.IsNullOrWhiteSpace(r.Enveloppe))
            .Select(r => new RegleClassement(r.MotCle.Trim(), r.Enveloppe)));

        OnPropertyChanged(nameof(NomsComptesCumul));
        OnPropertyChanged(nameof(NomsEnveloppes));
        _modifiee();
    }
}

/// <summary>Ligne « nom + montant » (revenu, ou enveloppe avec sa catégorie).</summary>
public sealed class ElementConfigViewModel : ObservableObject
{
    private readonly Action _modifie;
    private string _nom;
    private decimal _montant;
    private ChoixCategorie _categorie;

    public ElementConfigViewModel(string nom, decimal montant, Action modifie, Core.Modeles.Categorie categorie = Core.Modeles.Categorie.NonClassee)
    {
        _nom = nom;
        _montant = montant;
        _modifie = modifie;
        _categorie = ChoixCategorie.De(categorie);
    }

    public ChoixCategorie Categorie
    {
        get => _categorie;
        set { if (value is not null && SetProperty(ref _categorie, value)) _modifie(); }
    }

    public string Nom
    {
        get => _nom;
        set { if (SetProperty(ref _nom, value ?? "")) _modifie(); }
    }

    public decimal Montant
    {
        get => _montant;
        set { if (SetProperty(ref _montant, value)) _modifie(); }
    }
}

public sealed class ChargeConfigViewModel : ObservableObject
{
    private readonly Action _modifie;
    private string _nom;
    private decimal _debit;
    private decimal _credit;
    private string? _compteCumul;
    private ChoixCategorie _categorie;
    private ChoixFrequence _frequence;
    private PeriodeMois? _depart;
    private readonly PeriodeMois _departParDefaut;

    public ChargeConfigViewModel(ModeleCharge charge, Action modifie, PeriodeMois departParDefaut)
    {
        _nom = charge.Nom;
        _debit = charge.Debit;
        _credit = charge.Credit;
        _compteCumul = charge.CompteCumul;
        _categorie = ChoixCategorie.De(charge.Categorie);
        _frequence = ChoixFrequence.De(charge.Frequence);
        _depart = charge.Depart;
        _departParDefaut = departParDefaut;
        _modifie = modifie;
    }

    public ChoixFrequence Frequence
    {
        get => _frequence;
        set
        {
            if (value is null || !SetProperty(ref _frequence, value))
                return;
            // Une charge non mensuelle a besoin d'un mois de repère.
            if (value.Mois > 1 && _depart is null)
            {
                _depart = _departParDefaut;
                OnPropertyChanged(nameof(Depart));
            }
            OnPropertyChanged(nameof(DepartModifiable));
            _modifie();
        }
    }

    /// <summary>Un mois où la charge est prélevée ; les suivants s'en déduisent avec la fréquence.</summary>
    public PeriodeMois? Depart
    {
        get => _depart;
        set { if (value is not null && SetProperty(ref _depart, value)) _modifie(); }
    }

    public bool DepartModifiable => _frequence.Mois > 1;

    public ChoixCategorie Categorie
    {
        get => _categorie;
        set { if (value is not null && SetProperty(ref _categorie, value)) _modifie(); }
    }

    public string Nom
    {
        get => _nom;
        set { if (SetProperty(ref _nom, value ?? "")) _modifie(); }
    }

    public decimal Debit
    {
        get => _debit;
        set { if (SetProperty(ref _debit, value)) _modifie(); }
    }

    public decimal Credit
    {
        get => _credit;
        set { if (SetProperty(ref _credit, value)) _modifie(); }
    }

    /// <summary>Chaîne vide = aucun compte cumulé.</summary>
    public string CompteCumul
    {
        get => _compteCumul ?? "";
        set { if (SetProperty(ref _compteCumul, OperationViewModel.VideVersNull(value))) _modifie(); }
    }

    internal ModeleCharge VersModele() => new(Nom, Debit, Credit, _compteCumul, _categorie.Valeur, _frequence.Mois, _depart);
}

public sealed class CompteCumulConfigViewModel : ObservableObject
{
    private readonly Action _modifie;
    private string _nom;
    private decimal _montantInitial;
    private decimal? _objectif;

    public CompteCumulConfigViewModel(CompteCumul compte, Action modifie)
    {
        _nom = compte.Nom;
        _montantInitial = compte.MontantInitial;
        _objectif = compte.Objectif;
        _modifie = modifie;
    }

    public string Nom
    {
        get => _nom;
        set { if (SetProperty(ref _nom, value ?? "")) _modifie(); }
    }

    public decimal MontantInitial
    {
        get => _montantInitial;
        set { if (SetProperty(ref _montantInitial, value)) _modifie(); }
    }

    /// <summary>Montant total à atteindre ; null = pas d'objectif.</summary>
    public decimal? Objectif
    {
        get => _objectif;
        set { if (SetProperty(ref _objectif, value)) _modifie(); }
    }

    internal CompteCumul VersModele() => new(Nom, MontantInitial, Objectif);
}

/// <summary>Catégorie de la règle 50/30/20, avec son libellé.</summary>
public sealed record ChoixCategorie(Categorie Valeur, string Nom)
{
    public static IReadOnlyList<ChoixCategorie> Tous { get; } = new[]
    {
        new ChoixCategorie(Core.Modeles.Categorie.NonClassee, "—"),
        new ChoixCategorie(Core.Modeles.Categorie.Essentiel, "Essentiel"),
        new ChoixCategorie(Core.Modeles.Categorie.Confort, "Confort"),
        new ChoixCategorie(Core.Modeles.Categorie.Epargne, "Épargne"),
    };

    public static ChoixCategorie De(Categorie categorie) => Tous.First(c => c.Valeur == categorie);

    public override string ToString() => Nom;
}

public sealed class RegleConfigViewModel : ObservableObject
{
    private readonly Action _modifie;
    private string _motCle;
    private string _enveloppe;

    public RegleConfigViewModel(RegleClassement regle, Action modifie)
    {
        _motCle = regle.MotCle;
        _enveloppe = regle.Enveloppe;
        _modifie = modifie;
    }

    /// <summary>Texte cherché dans le libellé bancaire, ex. « CARREFOUR ».</summary>
    public string MotCle
    {
        get => _motCle;
        set { if (SetProperty(ref _motCle, value ?? "")) _modifie(); }
    }

    public string Enveloppe
    {
        get => _enveloppe;
        set { if (SetProperty(ref _enveloppe, value ?? "")) _modifie(); }
    }
}

/// <summary>Fréquence de prélèvement d'une charge, affichée en français.</summary>
public sealed record ChoixFrequence(int Mois, string Nom)
{
    public static IReadOnlyList<ChoixFrequence> Toutes { get; } = new[]
    {
        new ChoixFrequence(1, "Tous les mois"),
        new ChoixFrequence(2, "Tous les 2 mois"),
        new ChoixFrequence(3, "Tous les 3 mois"),
        new ChoixFrequence(6, "Tous les 6 mois"),
        new ChoixFrequence(12, "Une fois par an"),
    };

    /// <summary>Fréquence correspondant à un nombre de mois (une valeur inconnue est gardée telle quelle).</summary>
    public static ChoixFrequence De(int mois) =>
        Toutes.FirstOrDefault(f => f.Mois == mois) ?? new ChoixFrequence(mois, $"Tous les {mois} mois");

    public override string ToString() => Nom;
}
