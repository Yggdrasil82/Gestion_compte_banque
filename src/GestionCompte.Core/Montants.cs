using System.Globalization;

namespace GestionCompte.Core;

/// <summary>Affichage et saisie des montants en euros, au format français.</summary>
public static class Montants
{
    public static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Ex. 1234.5 → « 1 234,50 ».</summary>
    public static string Formater(decimal montant) => montant.ToString("N2", Francais);

    /// <summary>
    /// Lit un montant saisi : « 12,50 », « 12.50 », « 1 234,56 € », « -3 »…
    /// Le dernier séparateur (virgule ou point) est pris comme séparateur décimal.
    /// Une saisie vide vaut 0.
    /// </summary>
    public static bool TryLire(string? texte, out decimal montant)
    {
        montant = 0m;
        if (string.IsNullOrWhiteSpace(texte))
            return true;

        var nettoye = new string(texte.Where(c => !char.IsWhiteSpace(c) && c != '€').ToArray());
        if (nettoye.Length == 0)
            return false;

        var dernierSeparateur = nettoye.LastIndexOfAny(new[] { ',', '.' });
        if (dernierSeparateur >= 0)
        {
            var entier = nettoye[..dernierSeparateur].Replace(",", "").Replace(".", "");
            nettoye = entier + "." + nettoye[(dernierSeparateur + 1)..];
        }

        return decimal.TryParse(nettoye, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out montant);
    }
}
