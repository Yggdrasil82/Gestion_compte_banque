using CommunityToolkit.Mvvm.ComponentModel;
using GestionCompte.Core;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Presentation;

/// <summary>Écran Prévisionnel : simulation des mois à venir, graphique, échéances et opérations ponctuelles prévues.</summary>
public sealed partial class PrevisionnelViewModel : ObservableObject
{
    /// <summary>Nombre de mois réels affichés avant la simulation.</summary>
    public const int MoisReelsAffiches = 6;

    /// <summary>Nombre de mois futurs proposés pour une opération prévue.</summary>
    public const int MoisProposes = 36;

    private readonly CompteBancaire _compte;
    private readonly Action _donneesModifiees;

    public PrevisionnelViewModel(CompteBancaire compte, Action donneesModifiees)
    {
        _compte = compte;
        _donneesModifiees = donneesModifiees;

        var premier = compte.ProchainMois;
        Periodes = Enumerable.Range(0, MoisProposes)
            .Select(i => Enumerable.Range(0, i).Aggregate(premier, (p, _) => p.Suivant()))
            .Select(p => new ChoixPeriode(p))
            .ToList();

        OperationsPrevues = new ListeEditable<OperationPrevueViewModel>(
            compte.OperationsPrevues.Select(o => new OperationPrevueViewModel(o, Periodes, ValeurModifiee)),
            () => new OperationPrevueViewModel(new OperationPrevue(premier, "Nouvelle opération prévue"), Periodes, ValeurModifiee),
            StructureModifiee);

        NomsComptesCumul = new[] { "" }.Concat(compte.Configuration.ComptesCumul.Select(c => c.Nom)).ToList();

        Recalculer();
    }

    public static IReadOnlyList<int> Horizons { get; } = new[] { 6, 12, 24 };

    /// <summary>Nombre de mois simulés (12 par défaut).</summary>
    [ObservableProperty] private int _horizon = 12;

    partial void OnHorizonChanged(int value) => Recalculer();

    /// <summary>Mois futurs possibles pour une opération prévue.</summary>
    public IReadOnlyList<ChoixPeriode> Periodes { get; }

    public ListeEditable<OperationPrevueViewModel> OperationsPrevues { get; }

    public IReadOnlyList<string> NomsComptesCumul { get; }

    [ObservableProperty] private IReadOnlyList<LignePrevisionViewModel> _lignes = Array.Empty<LignePrevisionViewModel>();
    [ObservableProperty] private IReadOnlyList<EcheanceViewModel> _echeances = Array.Empty<EcheanceViewModel>();

    [ObservableProperty] private string _titreSoldeFinal = "";
    [ObservableProperty] private decimal _soldeFinal;
    [ObservableProperty] private string _titrePointBas = "";
    [ObservableProperty] private decimal _soldePointBas;
    [ObservableProperty] private string _titreEpargne = "";
    [ObservableProperty] private decimal _epargneFinale;
    [ObservableProperty] private bool _decouvertPrevu;
    [ObservableProperty] private string _alerte = "";

    public void Recalculer()
    {
        var prevision = Previsionnel.Calculer(_compte, MoisReelsAffiches, Horizon);
        Lignes = prevision.Mois.Select(m => new LignePrevisionViewModel(m)).ToList();
        Echeances = prevision.Echeances.Select(e => new EcheanceViewModel(e)).ToList();

        var dernier = prevision.MoisPrevus.LastOrDefault();
        TitreSoldeFinal = dernier is null ? "Solde prévu" : $"Solde fin {dernier.Periode.Libelle}";
        SoldeFinal = dernier?.SoldeFin ?? 0m;

        var bas = prevision.PointBas;
        TitrePointBas = bas is null ? "Point le plus bas" : $"Point le plus bas ({bas.Periode.Libelle})";
        SoldePointBas = bas?.SoldeFin ?? 0m;

        var epargne = _compte.Configuration.ComptesCumul.FirstOrDefault();
        TitreEpargne = epargne is null ? "Épargne" : $"{epargne.Nom} dans {Horizon} mois";
        EpargneFinale = epargne is not null && dernier is not null ? dernier.Cumuls.GetValueOrDefault(epargne.Nom) : 0m;

        var negatif = prevision.PremierMoisNegatif;
        DecouvertPrevu = negatif is not null;
        Alerte = negatif is null
            ? $"Aucun découvert prévu sur {Horizon} mois"
            : $"Découvert prévu en {negatif.Periode.Libelle} : {Montants.Formater(negatif.SoldeFin)} €";
    }

    private void ValeurModifiee()
    {
        Recalculer();
        _donneesModifiees();
    }

    private void StructureModifiee()
    {
        _compte.OperationsPrevues.Clear();
        _compte.OperationsPrevues.AddRange(OperationsPrevues.Elements.Select(o => o.Modele));
        ValeurModifiee();
    }
}

public sealed record ChoixPeriode(PeriodeMois Periode)
{
    public string Libelle => Periode.Libelle;

    public override string ToString() => Libelle;
}

public sealed class LignePrevisionViewModel
{
    public LignePrevisionViewModel(MoisPrevision mois) => Mois = mois;

    public MoisPrevision Mois { get; }

    public string Libelle => Mois.Periode.Libelle;

    /// <summary>Libellé court pour le graphique, ex. « oct. 26 ».</summary>
    public string LibelleCourt => new DateTime(Mois.Periode.Annee, Mois.Periode.Mois, 1).ToString("MMM yy", Montants.Francais);

    public bool Reel => Mois.Reel;

    public string Type => Mois.Reel ? "Réel" : "Prévu";

    public decimal Entrees => Mois.Entrees;

    public decimal Sorties => Mois.Sorties;

    /// <summary>Entrées − sorties du mois.</summary>
    public decimal Variation => Mois.Entrees - Mois.Sorties;

    public bool VariationNegative => Variation < 0;

    public decimal SoldeFin => Mois.SoldeFin;

    public bool Negatif => Mois.Negatif;
}

public sealed class EcheanceViewModel
{
    public EcheanceViewModel(EcheanceCompteCumul echeance) => Echeance = echeance;

    public EcheanceCompteCumul Echeance { get; }

    public string Nom => Echeance.Nom;

    public bool Atteinte => Echeance.DejaAtteint || Echeance.AtteintEn is not null;

    public string Texte => Echeance switch
    {
        { DejaAtteint: true } => $"Objectif de {Montants.Formater(Echeance.Objectif)} € déjà atteint",
        { AtteintEn: { } periode } => $"Objectif de {Montants.Formater(Echeance.Objectif)} € atteint en {periode.Libelle}",
        _ => $"Objectif de {Montants.Formater(Echeance.Objectif)} € non atteint d'ici {Previsionnel.MoisMaximumPourEcheances / 12} ans",
    };
}

public sealed class OperationPrevueViewModel : ObservableObject
{
    private readonly IReadOnlyList<ChoixPeriode> _periodes;
    private readonly Action _modifie;

    public OperationPrevueViewModel(OperationPrevue modele, IReadOnlyList<ChoixPeriode> periodes, Action modifie)
    {
        Modele = modele;
        _periodes = periodes;
        _modifie = modifie;
    }

    public OperationPrevue Modele { get; }

    /// <summary>Mois choisi parmi les mois futurs proposés.</summary>
    public ChoixPeriode? Periode
    {
        get => _periodes.FirstOrDefault(p => p.Periode == Modele.Periode);
        set
        {
            if (value is null || value.Periode == Modele.Periode)
                return;
            Modele.Periode = value.Periode;
            OnPropertyChanged();
            _modifie();
        }
    }

    public string Libelle
    {
        get => Modele.Libelle;
        set { if (SetProperty(Modele.Libelle, value ?? "", Modele, (m, v) => m.Libelle = v)) _modifie(); }
    }

    public decimal Debit
    {
        get => Modele.Debit;
        set { if (SetProperty(Modele.Debit, value, Modele, (m, v) => m.Debit = v)) _modifie(); }
    }

    public decimal Credit
    {
        get => Modele.Credit;
        set { if (SetProperty(Modele.Credit, value, Modele, (m, v) => m.Credit = v)) _modifie(); }
    }

    /// <summary>Compte cumulé alimenté ; chaîne vide = aucun.</summary>
    public string CompteCumul
    {
        get => Modele.CompteCumul ?? "";
        set { if (SetProperty(Modele.CompteCumul, OperationViewModel.VideVersNull(value), Modele, (m, v) => m.CompteCumul = v)) _modifie(); }
    }
}
