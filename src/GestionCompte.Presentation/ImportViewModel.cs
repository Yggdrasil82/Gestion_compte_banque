using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Core;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Import;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Presentation;

/// <summary>
/// Aperçu d'un import de relevé : l'utilisateur vérifie et corrige le rapprochement avant de valider.
/// Rien n'est modifié tant que l'import n'est pas validé.
/// </summary>
public sealed partial class ImportViewModel : ObservableObject
{
    private readonly CompteBancaire _compte;
    private readonly IDialogues _dialogues;
    private readonly Action<ResultatImport> _termine;
    private readonly Action _annule;
    private readonly Func<IReadOnlyDictionary<string, bool>, PlanImport>? _preparer;
    private readonly Dictionary<string, bool> _dansLeurMois = new(StringComparer.Ordinal);
    private PlanImport _plan;
    private decimal _soldePointeAvant;

    /// <param name="preparer">Prépare de nouveau le plan quand l'utilisateur choisit le mois d'opérations antérieures
    /// (identifiant bancaire → true = mois de la date, false = mois en cours) ; null = pas de choix du mois.</param>
    public ImportViewModel(CompteBancaire compte, PlanImport plan, string nomFichier, IDialogues dialogues,
        Action<ResultatImport> termine, Action annule, Func<IReadOnlyDictionary<string, bool>, PlanImport>? preparer = null)
    {
        _compte = compte;
        _plan = plan;
        _dialogues = dialogues;
        _termine = termine;
        _annule = annule;
        _preparer = preparer;
        NomFichier = nomFichier;

        NomsEnveloppes = new[] { "" }.Concat(compte.Configuration.Enveloppes.Select(e => e.Nom)).ToList();
        SoldeBanque = plan.SoldeBanque;
        DateSolde = plan.DateSolde?.ToString("dd/MM/yyyy") ?? "";

        var premierMois = compte.Configuration.PremierMois;
        SoldeAvantPremierMois = plan.DateSolde is { } date && new PeriodeMois(date.Year, date.Month) < premierMois;
        Afficher(plan, null);

        // Relevé daté d'avant le premier mois : comparé au solde de départ, sauf si des lignes (mois juste avant) sont importées.
        LibelleSoldePointe = SoldeAvantPremierMois && Lignes.All(l => !l.Modifiable)
            ? "Solde de départ (configuration)" : "Solde pointé après import";

        var nonImportables = Lignes.Where(l => !l.Modifiable).Select(l => l.Statut).ToList();
        RienAImporter = nonImportables.Count == Lignes.Count;
        MessageRienAImporter = !RienAImporter ? ""
            : Lignes.Count == 0 ? "Ce relevé ne contient aucune opération."
            : nonImportables.All(s => s == StatutImport.AvantDebut)
                ? $"Ce relevé est antérieur au premier mois géré ({premierMois.Libelle}) : aucune opération à importer."
            : nonImportables.All(s => s == StatutImport.DejaImportee)
                ? "Toutes les opérations de ce relevé ont déjà été importées."
                : $"Aucune opération à importer : elles sont déjà importées ou antérieures au premier mois géré ({premierMois.Libelle}).";
    }

    /// <summary>
    /// Affiche un plan. Refait pour le même relevé, les lignes déjà affichées sont gardées (mêmes opérations, même ordre)
    /// avec les choix de l'utilisateur : la liste n'est pas recréée pendant qu'il choisit un mois.
    /// </summary>
    private void Afficher(PlanImport plan, IReadOnlyList<LigneImportViewModel>? precedentes)
    {
        _plan = plan;
        _soldePointeAvant = ImportReleve.SoldePointe(_compte, plan.DateSolde);
        var parIdentifiant = plan.Lignes.ToDictionary(l => l.Source.Identifiant, StringComparer.Ordinal);
        if (precedentes is not null && precedentes.Count == plan.Lignes.Count
            && precedentes.All(l => parIdentifiant.ContainsKey(l.Ligne.Source.Identifiant)))
        {
            foreach (var ligne in precedentes)
                ligne.Remplacer(parIdentifiant[ligne.Ligne.Source.Identifiant]);
        }
        else
        {
            Lignes = plan.Lignes.Select(l => new LigneImportViewModel(l, Recalculer, plan.MoisCourant, PlacerDansSonMois)).ToList();
            OnPropertyChanged(nameof(Lignes));
        }

        MoisACreer = string.Join(", ", plan.MoisACreer.Select(p => p.Libelle));
        OnPropertyChanged(nameof(MoisACreer));
        OnPropertyChanged(nameof(AMoisACreer));
        OnPropertyChanged(nameof(ADatesAnterieures));
        OnPropertyChanged(nameof(TexteDatesAnterieures));
        OnPropertyChanged(nameof(TexteRangement));
        OnPropertyChanged(nameof(RangementPossible));
        RangerDatesAnterieuresCommand.NotifyCanExecuteChanged();
        Recalculer();
    }

    private void PlacerDansSonMois(IEnumerable<LigneImportViewModel> lignes, bool dansSonMois)
    {
        if (_preparer is null)
            return;
        var modifie = false;
        foreach (var ligne in lignes.Where(l => l.Ligne.PeutAllerDansSonMois && l.DansSonMois != dansSonMois))
        {
            _dansLeurMois[ligne.Ligne.Source.Identifiant] = dansSonMois;
            modifie = true;
        }
        if (modifie)
            Afficher(_preparer(_dansLeurMois), Lignes);
    }

    public string NomFichier { get; }

    public IReadOnlyList<LigneImportViewModel> Lignes { get; private set; } = Array.Empty<LigneImportViewModel>();

    /// <summary>Choix de la colonne « Enveloppe » (vide = aucune).</summary>
    public IReadOnlyList<string> NomsEnveloppes { get; }

    /// <summary>Mois qui seront créés (vide s'il n'y en a pas).</summary>
    public string MoisACreer { get; private set; } = "";

    public bool AMoisACreer => MoisACreer.Length > 0;

    public decimal? SoldeBanque { get; }

    public bool ASoldeBanque => SoldeBanque is not null;

    public string DateSolde { get; }

    /// <summary>Le solde de la banque date d'avant le premier mois : il est comparé au solde de départ de la configuration.</summary>
    public bool SoldeAvantPremierMois { get; }

    public string LibelleSoldePointe { get; }

    /// <summary>Aucune ligne ne peut être importée (toutes déjà importées ou antérieures au premier mois).</summary>
    public bool RienAImporter { get; }

    public string MessageRienAImporter { get; }

    /// <summary>Opérations à importer datées d'un mois antérieur au mois en cours (chevauchement de relevé).</summary>
    private List<LigneImportViewModel> DatesAnterieures => Lignes.Where(l => l.DateAnterieure && l.Modifiable).ToList();

    public bool ADatesAnterieures => DatesAnterieures.Count > 0;

    public string TexteDatesAnterieures
    {
        get
        {
            var nombre = DatesAnterieures.Count;
            if (nombre == 0 || _plan.MoisCourant is not { } courant)
                return "";
            return (nombre == 1 ? "1 opération est datée" : $"{nombre} opérations sont datées") +
                   $" d'avant {courant.Libelle} : vérifiez leur mois (colonne « Mois ») et les lignes « À vérifier ».";
        }
    }

    /// <summary>Le bouton range toutes les dates antérieures dans leur mois, ou les remet toutes dans le mois en cours.</summary>
    private bool ToutesDansLeurMois => DatesAnterieures.Where(l => l.Ligne.PeutAllerDansSonMois).All(l => l.DansSonMois);

    public string TexteRangement => ToutesDansLeurMois ? "Tout remettre dans le mois en cours" : "Ranger les dates antérieures dans leur mois";

    /// <summary>Le bouton de rangement n'est affiché que si une date antérieure peut aller dans son mois.</summary>
    public bool RangementPossible => PeutRanger();

    private bool PeutRanger() => _preparer is not null && DatesAnterieures.Any(l => l.Ligne.PeutAllerDansSonMois);

    [RelayCommand(CanExecute = nameof(PeutRanger))]
    private void RangerDatesAnterieures() => PlacerDansSonMois(DatesAnterieures, !ToutesDansLeurMois);

    /// <summary>Lignes importées telles que proposées alors qu'une question reste posée (même montant, date antérieure).</summary>
    [ObservableProperty] private int _nombreAVerifier;

    [ObservableProperty] private int _nombreRapprochees;
    [ObservableProperty] private int _nombreAjustees;
    [ObservableProperty] private int _nombreRevenus;
    [ObservableProperty] private int _nombreNouvelles;
    [ObservableProperty] private int _nombreIgnorees;

    /// <summary>Solde pointé de l'application une fois l'import fait.</summary>
    [ObservableProperty] private decimal _soldePointeApres;

    /// <summary>Solde banque − solde pointé après import (0 = tout correspond).</summary>
    [ObservableProperty] private decimal _ecart;

    public bool SoldeConcorde => SoldeBanque is not null && Ecart == 0;

    public bool SoldeDiffere => SoldeBanque is not null && Ecart != 0;

    partial void OnEcartChanged(decimal value)
    {
        OnPropertyChanged(nameof(SoldeConcorde));
        OnPropertyChanged(nameof(SoldeDiffere));
    }

    private void Recalculer()
    {
        var importees = Lignes.Where(l => l.Ligne.Importer && l.Ligne.Modifiable).ToList();
        NombreRapprochees = importees.Count(l => l.Ligne.Statut == StatutImport.Rapprochee);
        NombreAjustees = importees.Count(l => l.Ligne.Statut == StatutImport.MontantAjuste);
        NombreRevenus = importees.Count(l => l.Ligne.Statut == StatutImport.RevenuRecu);
        NombreNouvelles = importees.Count(l => l.Ligne.Statut == StatutImport.Nouvelle);
        NombreIgnorees = Lignes.Count - importees.Count;
        NombreAVerifier = Lignes.Count(l => l.AVerifier);

        // Chaque ligne importée devient pointée avec le montant de la banque (seules celles jusqu'à la date du solde comptent).
        SoldePointeApres = _soldePointeAvant + importees
            .Where(l => _plan.DateSolde is not { } date || l.Ligne.Source.Date <= date)
            .Sum(l => l.Ligne.Source.Montant);
        Ecart = (SoldeBanque ?? 0m) - SoldePointeApres;
        ValiderCommand.NotifyCanExecuteChanged();
    }

    private bool PeutValider() => Lignes.Any(l => l.Ligne.Importer && l.Ligne.Modifiable);

    [RelayCommand(CanExecute = nameof(PeutValider))]
    private void Valider()
    {
        var aVerifier = Lignes.Where(l => l.AVerifier).ToList();
        if (aVerifier.Count > 0 && !_dialogues.Confirmer("Opérations à vérifier",
                "Ces opérations du relevé sont datées d'un mois antérieur et ont le même montant qu'une opération de l'application :\n\n" +
                string.Join("\n", aVerifier.Select(l => $"• {l.Date} {l.Libelle} ({Montants.Formater(l.Montant)} €) : {l.Verification}")) +
                "\n\nImporter ainsi ?\nOui : valider l'import. Non : revenir à l'écran pour corriger."))
            return;

        if (AMoisACreer && !_dialogues.Confirmer("Créer des mois",
                $"Le relevé contient des opérations de mois pas encore créés.\n\nCréer : {MoisACreer} ?\n\n" +
                "Ils seront préremplis avec la configuration, comme avec « Créer le mois suivant »."))
            return;

        var resultat = ImportReleve.Appliquer(_compte, _plan);

        // Règles retenues : mot-clé du libellé → enveloppe choisie.
        foreach (var ligne in Lignes.Where(l => l.RetenirRegle && l.PeutRetenirRegle))
        {
            var motCle = ligne.MotCleSuggere!;
            if (!_compte.Configuration.Regles.Any(r => CalculateurMois.MemeNom(r.MotCle, motCle)))
                _compte.Configuration.Regles.Add(new RegleClassement(motCle, ligne.Enveloppe));
        }

        _termine(resultat);
    }

    [RelayCommand]
    private void Annuler() => _annule();
}

/// <summary>Rattachement proposé dans la liste déroulante d'une ligne ; <see cref="Candidat"/> null = nouvelle opération.</summary>
public sealed record OptionRapprochement(Candidat? Candidat, string Libelle)
{
    public override string ToString() => Libelle;
}

public sealed class LigneImportViewModel : ObservableObject
{
    private readonly Action _modifie;
    private readonly Action<IEnumerable<LigneImportViewModel>, bool>? _placer;

    /// <param name="moisCourant">Mois en cours du plan (choix entre lui et le mois de la date).</param>
    /// <param name="placer">Range la ligne dans le mois de sa date (true) ou dans le mois en cours (false).</param>
    public LigneImportViewModel(LigneImport ligne, Action modifie, PeriodeMois? moisCourant = null,
        Action<IEnumerable<LigneImportViewModel>, bool>? placer = null)
    {
        Ligne = ligne;
        _modifie = modifie;
        _placer = placer;
        ChoixMois = ligne.PeutAllerDansSonMois && moisCourant is { } courant && placer is not null
            ? new[] { courant.Libelle, ligne.PeriodeDate.Libelle }
            : new[] { ligne.Periode.Libelle };
        Options = CreerOptions(ligne);
        MotCleSuggere = ImportReleve.MotCleSuggere(ligne.Source.Libelle);
    }

    private static IReadOnlyList<OptionRapprochement> CreerOptions(LigneImport ligne) =>
        new[] { new OptionRapprochement(null, "➕ Nouvelle opération") }
            .Concat(ligne.Candidats.Select(c => new OptionRapprochement(c, (c.EstRevenu ? "Revenu : " : "") + c)))
            .ToList();

    public LigneImport Ligne { get; private set; }

    /// <summary>
    /// Remplace la ligne par celle d'un plan refait (même opération, éventuellement rangée dans un autre mois).
    /// La case « Importer » est gardée ; rattachement et enveloppe aussi si le mois ne change pas.
    /// </summary>
    internal void Remplacer(LigneImport nouvelle)
    {
        var ancienne = Ligne;
        if (nouvelle.Modifiable)
            nouvelle.Importer = ancienne.Importer;
        if (ancienne.Periode == nouvelle.Periode)
        {
            if (ancienne.Choix is null || nouvelle.Candidats.Contains(ancienne.Choix))
                nouvelle.Choix = ancienne.Choix;
            nouvelle.Enveloppe = ancienne.Enveloppe;
        }

        Ligne = nouvelle;
        Options = CreerOptions(nouvelle);
        OnPropertyChanged(string.Empty);
    }

    public string Date => Ligne.Source.Date.ToString("dd/MM/yyyy");

    /// <summary>Opération datée d'un mois antérieur au mois en cours (chevauchement de relevé) : alerte orange.</summary>
    public bool DateAnterieure => Ligne.DateAnterieure;

    /// <summary>Question sur le même montant (voir <see cref="LigneImport.Verification"/>), affichée sous la ligne.</summary>
    public string? Verification => Ligne.Verification;

    public bool AVerification => Verification is not null;

    public bool AVerifier => Ligne.AVerifier;

    public string AlerteDate => DateAnterieure
        ? $"Date antérieure au mois : opération de {Ligne.PeriodeDate.Libelle}, rangée dans {Ligne.Periode.Libelle}."
        : "";

    /// <summary>La ligne est rangée dans le mois de sa date plutôt que dans le mois en cours.</summary>
    public bool DansSonMois => DateAnterieure && Ligne.Periode == Ligne.PeriodeDate;

    /// <summary>Mois possibles : le mois en cours puis celui de la date (un seul choix sinon).</summary>
    public IReadOnlyList<string> ChoixMois { get; }

    public bool MoisModifiable => Modifiable && ChoixMois.Count > 1;

    public string MoisChoisi
    {
        get => Ligne.Periode.Libelle;
        set
        {
            if (value is null || value == MoisChoisi || !MoisModifiable)
                return;
            _placer?.Invoke(new[] { this }, value == Ligne.PeriodeDate.Libelle);
        }
    }

    public string Libelle => Ligne.Source.Libelle;

    public string LibelleBanque => Ligne.Source.LibelleBanque;

    public decimal Montant => Ligne.Source.Montant;

    public bool EstCredit => Montant > 0;

    public bool Modifiable => Ligne.Modifiable;

    public bool Importer
    {
        get => Ligne.Importer;
        set
        {
            if (!Modifiable || Ligne.Importer == value)
                return;
            Ligne.Importer = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AVerifier));
            OnPropertyChanged(nameof(StatutTexte));
            _modifie();
        }
    }

    public IReadOnlyList<OptionRapprochement> Options { get; private set; }

    public OptionRapprochement OptionChoisie
    {
        get => Options.First(o => o.Candidat == Ligne.Choix);
        set
        {
            if (value is null || value.Candidat == Ligne.Choix)
                return;
            Ligne.Choix = value.Candidat;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Statut));
            OnPropertyChanged(nameof(StatutTexte));
            OnPropertyChanged(nameof(EnveloppeModifiable));
            OnPropertyChanged(nameof(PeutRetenirRegle));
            OnPropertyChanged(nameof(AVerifier));
            _modifie();
        }
    }

    /// <summary>Enveloppe d'une nouvelle dépense ; chaîne vide = aucune.</summary>
    public string Enveloppe
    {
        get => Ligne.Enveloppe ?? "";
        set
        {
            var nouvelle = string.IsNullOrWhiteSpace(value) ? null : value;
            if (nouvelle == Ligne.Enveloppe)
                return;
            Ligne.Enveloppe = nouvelle;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PeutRetenirRegle));
            _modifie();
        }
    }

    public bool EnveloppeModifiable => Modifiable && Statut == StatutImport.Nouvelle && !EstCredit;

    public StatutImport Statut => Ligne.Statut;

    public string StatutTexte => AVerifier ? "À vérifier" : Statut switch
    {
        StatutImport.Rapprochee => "Rapprochée",
        StatutImport.MontantAjuste => "Montant ajusté",
        StatutImport.RevenuRecu => "Revenu reçu",
        StatutImport.Nouvelle => "Nouvelle",
        StatutImport.DejaImportee => "Déjà importée",
        StatutImport.AvantDebut => "Avant le début",
        _ => "",
    };

    /// <summary>Mot-clé proposé pour une règle de classement (ex. « CARREFOUR »).</summary>
    public string? MotCleSuggere { get; }

    /// <summary>Une règle peut être retenue si l'utilisateur a choisi une enveloppe différente de la proposition.</summary>
    public bool PeutRetenirRegle => EnveloppeModifiable && MotCleSuggere is not null
                                    && Ligne.Enveloppe is not null && Ligne.Enveloppe != Ligne.EnveloppeProposee;

    private bool _retenirRegle = true;

    public bool RetenirRegle
    {
        get => _retenirRegle;
        set => SetProperty(ref _retenirRegle, value);
    }

    public string TexteRegle => $"Retenir : « {MotCleSuggere} » → cette enveloppe";
}
