using System.Globalization;
using System.Windows;
using System.Windows.Data;
using GestionCompte.Core;

namespace GestionCompte.App;

/// <summary>
/// Affiche un montant au format français (« 1 234,50 ») et relit une saisie (« 12,5 », « 12.5 », « 1 234 € »…).
/// Accepte aussi les montants facultatifs (decimal?) : une saisie vide donne alors null.
/// </summary>
public sealed class MontantConvertisseur : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal montant ? Montants.Formater(montant) : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var texte = value as string;
        if (targetType == typeof(decimal?) && string.IsNullOrWhiteSpace(texte))
            return null;

        // Saisie invalide : aucune valeur n'est enregistrée, la cellule reprend l'ancien montant.
        return Montants.TryLire(texte, out var montant) ? montant : DependencyProperty.UnsetValue;
    }
}

/// <summary>Montant avec le symbole euro, pour les zones en lecture seule (« 1 234,50 € »).</summary>
public sealed class EurosConvertisseur : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal montant ? $"{Montants.Formater(montant)} €" : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
