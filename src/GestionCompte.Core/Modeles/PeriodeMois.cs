using System.Globalization;

namespace GestionCompte.Core.Modeles;

/// <summary>Un mois précis d'une année (ex. octobre 2026).</summary>
public readonly record struct PeriodeMois : IComparable<PeriodeMois>
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    public PeriodeMois(int annee, int mois)
    {
        if (mois is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(mois), mois, "Le mois doit être compris entre 1 et 12.");
        if (annee is < 1900 or > 9999)
            throw new ArgumentOutOfRangeException(nameof(annee), annee, "Année invalide.");

        Annee = annee;
        Mois = mois;
    }

    public int Annee { get; }
    public int Mois { get; }

    /// <summary>Mois suivant ; décembre passe à janvier de l'année suivante.</summary>
    public PeriodeMois Suivant() => Mois == 12 ? new PeriodeMois(Annee + 1, 1) : new PeriodeMois(Annee, Mois + 1);

    /// <summary>Nombre de mois entre ce mois et <paramref name="autre"/> (positif si <paramref name="autre"/> est après).</summary>
    public int MoisJusqua(PeriodeMois autre) => (autre.Annee * 12 + autre.Mois) - (Annee * 12 + Mois);

    public PeriodeMois Precedent() => Mois == 1 ? new PeriodeMois(Annee - 1, 12) : new PeriodeMois(Annee, Mois - 1);

    /// <summary>Libellé affiché, ex. « Octobre 2026 ».</summary>
    public string Libelle
    {
        get
        {
            var texte = new DateTime(Annee, Mois, 1).ToString("MMMM yyyy", Francais);
            return char.ToUpper(texte[0], Francais) + texte[1..];
        }
    }

    public int CompareTo(PeriodeMois autre) => (Annee, Mois).CompareTo((autre.Annee, autre.Mois));

    public static bool operator <(PeriodeMois a, PeriodeMois b) => a.CompareTo(b) < 0;
    public static bool operator >(PeriodeMois a, PeriodeMois b) => a.CompareTo(b) > 0;
    public static bool operator <=(PeriodeMois a, PeriodeMois b) => a.CompareTo(b) <= 0;
    public static bool operator >=(PeriodeMois a, PeriodeMois b) => a.CompareTo(b) >= 0;

    public override string ToString() => Libelle;
}
