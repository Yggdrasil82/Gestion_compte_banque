namespace GestionCompte.Core.Bourse;

/// <summary>Titres détenus dans une enveloppe (compte-titres ou PEA).</summary>
/// <param name="PrixRevient">Coût total des titres encore détenus (prix moyen pondéré, frais et taxes d'achat compris).</param>
/// <param name="Cours">Dernier cours connu, null s'il n'y en a pas (la ligne est alors estimée à son prix de revient).</param>
public sealed record PositionBourse(
    EnveloppeBourse Enveloppe, string Isin, string Nom, string? Classe, decimal Quantite, decimal PrixRevient,
    decimal? Cours, DateTime? DateCours, bool CoursManuel)
{
    /// <summary>Prix de revient unitaire (PRU).</summary>
    public decimal Pru => Quantite == 0 ? 0 : PrixRevient / Quantite;

    public decimal Valeur => Cours is { } cours ? Math.Round(Quantite * cours, 2) : PrixRevient;

    public decimal PlusValue => Valeur - PrixRevient;

    /// <summary>Plus-value latente en % du prix de revient, null sans cours.</summary>
    public decimal? PlusValuePourcent => Cours is null || PrixRevient == 0 ? null : Math.Round(PlusValue / PrixRevient * 100, 2);
}

/// <summary>Bilan d'un portefeuille (ou d'une seule enveloppe).</summary>
/// <param name="Versements">Argent versé sur le compte du courtier.</param>
/// <param name="Retraits">Argent retiré vers la banque (positif).</param>
/// <param name="Especes">Espèces non investies : somme de tous les mouvements.</param>
/// <param name="PlusValuesRealisees">Gains (ou pertes) des ventes, par rapport au prix de revient moyen.</param>
/// <param name="Dividendes">Dividendes et intérêts reçus.</param>
/// <param name="FraisEtTaxes">Frais de courtage et taxes payés (positif).</param>
/// <param name="SansCours">Nombre de lignes sans cours (estimées à leur prix de revient).</param>
public sealed record BilanBourse(
    IReadOnlyList<PositionBourse> Positions, decimal Versements, decimal Retraits, decimal Especes,
    decimal PlusValuesRealisees, decimal Dividendes, decimal FraisEtTaxes, int SansCours)
{
    public decimal ValeurTitres => Positions.Sum(p => p.Valeur);

    public decimal PrixRevient => Positions.Sum(p => p.PrixRevient);

    public decimal PlusValueLatente => ValeurTitres - PrixRevient;

    public decimal? PlusValueLatentePourcent => PrixRevient == 0 ? null : Math.Round(PlusValueLatente / PrixRevient * 100, 2);

    /// <summary>Valeur du portefeuille : titres et espèces.</summary>
    public decimal ValeurTotale => ValeurTitres + Especes;

    /// <summary>Argent versé moins argent retiré.</summary>
    public decimal VerseNet => Versements - Retraits;

    /// <summary>Gain total depuis le début : ce que vaut le portefeuille moins ce qui y a été mis.</summary>
    public decimal Gain => ValeurTotale - VerseNet;
}

/// <summary>Calcul des positions par la méthode du prix moyen pondéré (celle de l'administration fiscale).</summary>
public static class CalculBourse
{
    /// <summary>Quantités plus petites que ceci : la ligne est soldée (arrondis des fractions de titres).</summary>
    private const decimal Epsilon = 0.000001m;

    /// <param name="enveloppe">Une seule enveloppe, ou null pour tout le portefeuille.</param>
    public static BilanBourse Calculer(Portefeuille portefeuille, EnveloppeBourse? enveloppe = null)
    {
        ArgumentNullException.ThrowIfNull(portefeuille);

        var lignes = new Dictionary<(EnveloppeBourse, string), Ligne>();
        decimal versements = 0, retraits = 0, especes = 0, realisees = 0, dividendes = 0, frais = 0;

        foreach (var o in portefeuille.Operations.Where(o => enveloppe is null || o.Enveloppe == enveloppe).OrderBy(o => o.Date))
        {
            especes += o.Net;
            frais -= o.Frais + o.Taxes;
            switch (o.Type)
            {
                case TypeOperationBourse.Versement:
                    versements += o.Montant;
                    break;
                case TypeOperationBourse.Retrait:
                    retraits -= o.Montant;
                    break;
                case TypeOperationBourse.Dividende:
                case TypeOperationBourse.Interets:
                    dividendes += o.Montant;
                    break;
                case TypeOperationBourse.Frais:
                case TypeOperationBourse.Impot:
                    frais -= o.Montant;
                    break;
                case TypeOperationBourse.Achat when o.Isin is not null:
                {
                    var l = Trouver(lignes, o);
                    l.Quantite += Math.Abs(o.Quantite);
                    l.Cout -= o.Net;
                    break;
                }
                case TypeOperationBourse.Vente when o.Isin is not null:
                {
                    var l = Trouver(lignes, o);
                    // Un export partiel peut contenir des ventes de titres achetés avant : on ne vend que ce qui est connu.
                    var vendue = Math.Min(Math.Abs(o.Quantite), l.Quantite);
                    var coutVendu = l.Quantite == 0 ? 0 : Math.Round(l.Cout * vendue / l.Quantite, 2);
                    realisees += o.Net - coutVendu;
                    l.Quantite -= vendue;
                    l.Cout -= coutVendu;
                    if (l.Quantite < Epsilon)
                        (l.Quantite, l.Cout) = (0, 0);
                    break;
                }
            }
        }

        var positions = lignes
            .Where(x => x.Value.Quantite >= Epsilon)
            .Select(x =>
            {
                var cours = portefeuille.Cours.GetValueOrDefault(x.Key.Item2);
                return new PositionBourse(x.Key.Item1, x.Key.Item2, x.Value.Nom, x.Value.Classe, x.Value.Quantite,
                    Math.Round(x.Value.Cout, 2), cours?.Valeur, cours?.Date, cours?.Manuel ?? false);
            })
            .OrderByDescending(p => p.Valeur)
            .ToList();

        return new BilanBourse(positions, versements, retraits, especes, realisees, dividendes, frais,
            positions.Count(p => p.Cours is null));
    }

    private sealed class Ligne
    {
        public string Nom = "";
        public string? Classe;
        public decimal Quantite;
        public decimal Cout;
    }

    private static Ligne Trouver(Dictionary<(EnveloppeBourse, string), Ligne> lignes, OperationBourse o)
    {
        if (!lignes.TryGetValue((o.Enveloppe, o.Isin!), out var ligne))
            lignes[(o.Enveloppe, o.Isin!)] = ligne = new Ligne { Nom = o.Nom ?? o.Isin! };
        if (o.Nom is not null)
            ligne.Nom = o.Nom;
        ligne.Classe = o.Classe ?? ligne.Classe;
        return ligne;
    }

    /// <summary>ISIN des titres encore détenus (ceux dont il faut chercher le cours).</summary>
    public static IReadOnlyList<string> TitresDetenus(Portefeuille portefeuille) =>
        Calculer(portefeuille).Positions.Select(p => p.Isin).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
