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
    private readonly PlanImport _plan;
    private readonly IDialogues _dialogues;
    private readonly Action<ResultatImport> _termine;
    private readonly Action _annule;
    private readonly decimal _soldePointeAvant;

    public ImportViewModel(CompteBancaire compte, PlanImport plan, string nomFichier, IDialogues dialogues,
        Action<ResultatImport> termine, Action annule)
    {
        _compte = compte;
        _plan = plan;
        _dialogues = dialogues;
        _termine = termine;
        _annule = annule;
        _soldePointeAvant = ImportReleve.SoldePointe(compte, plan.DateSolde);
        NomFichier = nomFichier;

        NomsEnveloppes = new[] { "" }.Concat(compte.Configuration.Enveloppes.Select(e => e.Nom)).ToList();
        Lignes = plan.Lignes.Select(l => new LigneImportViewModel(l, Recalculer)).ToList();
        MoisACreer = string.Join(", ", plan.MoisACreer.Select(p => p.Libelle));
        SoldeBanque = plan.SoldeBanque;
        DateSolde = plan.DateSolde?.ToString("dd/MM/yyyy") ?? "";

        var premierMois = compte.Configuration.PremierMois;
        SoldeAvantPremierMois = plan.DateSolde is { } date && new PeriodeMois(date.Year, date.Month) < premierMois;
        LibelleSoldePointe = SoldeAvantPremierMois ? "Solde de départ (configuration)" : "Solde pointé après import";

        var nonImportables = Lignes.Where(l => !l.Modifiable).Select(l => l.Statut).ToList();
        RienAImporter = nonImportables.Count == Lignes.Count;
        MessageRienAImporter = !RienAImporter ? ""
            : Lignes.Count == 0 ? "Ce relevé ne contient aucune opération."
            : nonImportables.All(s => s == StatutImport.AvantDebut)
                ? $"Ce relevé est antérieur au premier mois géré ({premierMois.Libelle}) : aucune opération à importer."
            : nonImportables.All(s => s == StatutImport.DejaImportee)
                ? "Toutes les opérations de ce relevé ont déjà été importées."
                : $"Aucune opération à importer : elles sont déjà importées ou antérieures au premier mois géré ({premierMois.Libelle}).";

        Recalculer();
    }

    public string NomFichier { get; }

    public IReadOnlyList<LigneImportViewModel> Lignes { get; }

    /// <summary>Choix de la colonne « Enveloppe » (vide = aucune).</summary>
    public IReadOnlyList<string> NomsEnveloppes { get; }

    /// <summary>Mois qui seront créés (vide s'il n'y en a pas).</summary>
    public string MoisACreer { get; }

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

    public LigneImportViewModel(LigneImport ligne, Action modifie)
    {
        Ligne = ligne;
        _modifie = modifie;
        Options = new[] { new OptionRapprochement(null, "➕ Nouvelle opération") }
            .Concat(ligne.Candidats.Select(c => new OptionRapprochement(c, (c.EstRevenu ? "Revenu : " : "") + c)))
            .ToList();
        MotCleSuggere = ImportReleve.MotCleSuggere(ligne.Source.Libelle);
    }

    public LigneImport Ligne { get; }

    public string Date => Ligne.Source.Date.ToString("dd/MM/yyyy");

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
            _modifie();
        }
    }

    public IReadOnlyList<OptionRapprochement> Options { get; }

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

    public string StatutTexte => Statut switch
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
