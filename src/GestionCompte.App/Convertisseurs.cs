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
    /// <param name="parameter">« vide » : un montant nul s'affiche comme une case vide.</param>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal montant && !(montant == 0 && parameter as string == "vide") ? Montants.Formater(montant) : "";

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

/// <summary>Vrai si la valeur (entier) vaut le paramètre ; sert aux boutons de navigation liés à l'onglet affiché.</summary>
public sealed class EgalConvertisseur : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int valeur && int.TryParse(parameter as string, out var attendu) && valeur == attendu;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && int.TryParse(parameter as string, out var attendu) ? attendu : Binding.DoNothing;
}

/// <summary>Visible si la valeur (entier) vaut le paramètre ; sert à afficher l'écran de l'onglet choisi.</summary>
public sealed class VisibleSiEgalConvertisseur : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int valeur && int.TryParse(parameter as string, out var attendu) && valeur == attendu
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}

/// <summary>Largeur d'une barre : proportion (0 à 1) × largeur disponible.</summary>
public sealed class ProportionConvertisseur : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [double proportion, double largeur] ? Math.Max(0, proportion * largeur) : 0.0;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
