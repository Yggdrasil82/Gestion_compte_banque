using GestionCompte.Core;
using GestionCompte.Core.Bourse;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Presentation;

/// <summary>Un compte de la vue d'ensemble ; <see cref="Actif"/> = compte ouvert.</summary>
public sealed record CompteEnsemble(string Nom, CompteBancaire Compte, bool Actif);

/// <summary>
/// Vue d'ensemble : solde de chaque compte à la fin du mois en cours et dans 12 mois, et total de tous les comptes.
/// Les mois non créés sont simulés comme dans le prévisionnel.
/// </summary>
public sealed class VueEnsembleViewModel
{
    public const int Horizon = 12;

    public VueEnsembleViewModel(IReadOnlyList<CompteEnsemble> comptes, PeriodeMois moisDuJour, IReadOnlyList<string>? illisibles = null)
    {
        var periodes = new List<PeriodeMois> { moisDuJour };
        while (periodes.Count <= Horizon)
            periodes.Add(periodes[^1].Suivant());

        MoisActuel = moisDuJour.Libelle;
        MoisFin = periodes[^1].Libelle;

        var soldes = comptes.Select(c => (c, Soldes: Soldes(c.Compte, periodes))).ToList();
        Lignes = soldes.Select(x => new LigneEnsemble(x.c.Nom, x.c.Actif, periodes, x.Soldes,
            x.c.Compte.Portefeuille.Vide ? null : CalculBourse.Calculer(x.c.Compte.Portefeuille))).ToList();
        ABourse = Lignes.Any(l => l.Bourse is not null);
        TotalBourse = Lignes.Sum(l => l.Bourse ?? 0);
        PlusValueBourse = Lignes.Sum(l => l.PlusValueBourse ?? 0);

        Courbe = periodes.Select((p, i) => new LignePrevisionViewModel(new MoisPrevision(
                p, soldes.Count > 0 && soldes.All(x => x.Soldes[i].Reel), 0m, 0m, 0m,
                soldes.Sum(x => x.Soldes[i].Solde), new Dictionary<string, decimal>())))
            .ToList();

        TotalActuel = Courbe[0].SoldeFin;
        TotalFin = Courbe[^1].SoldeFin;
        var pointBas = Courbe.MinBy(l => l.SoldeFin)!;
        PointBasTotal = pointBas.SoldeFin;
        MoisPointBas = pointBas.Libelle;
        Illisibles = illisibles is { Count: > 0 } ? $"Comptes illisibles, non comptés : {string.Join(", ", illisibles)}." : "";
    }

    public string MoisActuel { get; }

    public string MoisFin { get; }

    public IReadOnlyList<LigneEnsemble> Lignes { get; }

    /// <summary>Total de tous les comptes, mois par mois (pour le graphique).</summary>
    public IReadOnlyList<LignePrevisionViewModel> Courbe { get; }

    public decimal TotalActuel { get; }

    public decimal TotalFin { get; }

    public decimal PointBasTotal { get; }

    public string MoisPointBas { get; }

    public bool TotalNegatif => PointBasTotal < 0;

    public string Illisibles { get; }

    /// <summary>Au moins un compte a des placements en bourse (module « Bourse »).</summary>
    public bool ABourse { get; }

    /// <summary>Valeur de tous les portefeuilles (titres au dernier cours connu et espèces).</summary>
    public decimal TotalBourse { get; }

    /// <summary>Gain de tous les portefeuilles depuis le début (valeur moins argent versé).</summary>
    public decimal PlusValueBourse { get; }

    /// <summary>Comptes à la fin du mois en cours et placements en bourse.</summary>
    public decimal Patrimoine => TotalActuel + TotalBourse;

    public bool AIllisibles => Illisibles.Length > 0;

    /// <summary>
    /// Solde de fin de chaque mois demandé : réel pour les mois créés, simulé ensuite ;
    /// avant le premier mois du compte, son solde de départ.
    /// </summary>
    public static IReadOnlyList<(decimal Solde, bool Reel)> Soldes(CompteBancaire compte, IReadOnlyList<PeriodeMois> periodes)
    {
        var dernierCree = compte.Mois.Count > 0 ? compte.Mois[^1].Periode : compte.Configuration.PremierMois.Precedent();
        var aSimuler = Math.Max(0, dernierCree.MoisJusqua(periodes[^1]));
        var prevision = Previsionnel.Calculer(compte, compte.Mois.Count, aSimuler).Mois.ToDictionary(m => m.Periode);

        return periodes.Select(p =>
                prevision.TryGetValue(p, out var mois) ? (mois.SoldeFin, mois.Reel)
                : p < compte.Configuration.PremierMois ? (compte.Configuration.SoldeInitial, false)
                : (prevision.Values.LastOrDefault()?.SoldeFin ?? compte.Configuration.SoldeInitial, false))
            .ToList();
    }
}

public sealed class LigneEnsemble
{
    internal LigneEnsemble(string nom, bool actif, IReadOnlyList<PeriodeMois> periodes, IReadOnlyList<(decimal Solde, bool Reel)> soldes,
        BilanBourse? bourse = null)
    {
        Bourse = bourse?.ValeurTotale;
        PlusValueBourse = bourse?.Gain;
        Nom = nom;
        Actif = actif;
        SoldeActuel = soldes[0].Solde;
        SoldeFin = soldes[^1].Solde;
        var bas = soldes.Select((s, i) => (s.Solde, i)).MinBy(x => x.Solde);
        PointBas = bas.Solde;
        MoisPointBas = periodes[bas.i].Libelle;
    }

    public string Nom { get; }

    /// <summary>Compte ouvert en ce moment.</summary>
    public bool Actif { get; }

    public decimal SoldeActuel { get; }

    public decimal SoldeFin { get; }

    public decimal Variation => SoldeFin - SoldeActuel;

    public decimal PointBas { get; }

    public string MoisPointBas { get; }

    public bool PointBasNegatif => PointBas < 0;

    /// <summary>Valeur des placements en bourse du compte, null s'il n'en a pas.</summary>
    public decimal? Bourse { get; }

    public decimal? PlusValueBourse { get; }
}
