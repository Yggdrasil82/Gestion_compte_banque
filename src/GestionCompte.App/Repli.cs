using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GestionCompte.Presentation;

namespace GestionCompte.App;

/// <summary>
/// Carte réductible : un clic sur son titre (première ligne de la carte) cache ou montre le reste.
/// L'état est mémorisé dans les préférences (Configuration › Apparence) sous le nom donné à <c>Repli.Carte</c>.
/// </summary>
public static class Repli
{
    public static readonly DependencyProperty CarteProperty = DependencyProperty.RegisterAttached(
        "Carte", typeof(string), typeof(Repli), new PropertyMetadata(null, CarteChangee));

    public static string? GetCarte(DependencyObject element) => (string?)element.GetValue(CarteProperty);

    public static void SetCarte(DependencyObject element, string? valeur) => element.SetValue(CarteProperty, valeur);

    // Chevron ajouté au titre, une seule fois par carte.
    private static readonly DependencyProperty ChevronProperty = DependencyProperty.RegisterAttached(
        "Chevron", typeof(TextBlock), typeof(Repli));

    private static readonly DependencyProperty ReduiteProperty = DependencyProperty.RegisterAttached(
        "Reduite", typeof(bool), typeof(Repli));

    private static void CarteChangee(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is Border carte)
            carte.Loaded += (_, _) => Preparer(carte);
    }

    /// <summary>Apparence de l'écran Configuration (certaines cartes ont leur propre DataContext : on remonte jusqu'à lui).</summary>
    private static ApparenceViewModel? Apparence(FrameworkElement carte)
    {
        for (DependencyObject? element = carte; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is FrameworkElement { DataContext: ConfigurationViewModel configuration })
                return configuration.Apparence;
        return null;
    }

    private static void Preparer(Border carte)
    {
        if (carte.Child is not Panel contenu || contenu.Children.Count < 2 || contenu.Children[0] is not FrameworkElement titre)
            return;

        if (carte.GetValue(ChevronProperty) is not TextBlock chevron)
        {
            chevron = new TextBlock
            {
                FontFamily = (FontFamily)carte.FindResource("Icones"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                Opacity = 0.6,
            };
            if (TitreDe(titre)?.Parent is Panel ligne)
                ligne.Children.Add(chevron);
            carte.SetValue(ChevronProperty, chevron);
            titre.Cursor = Cursors.Hand;
            if (titre is Panel { Background: null } fond)
                fond.Background = Brushes.Transparent; // clic possible sur tout le titre
            titre.MouseLeftButtonUp += (_, e) =>
            {
                // Les boutons du titre (+, −…) gardent leur rôle.
                for (var source = e.OriginalSource as DependencyObject; source is not null && source != titre;
                     source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
                    if (source is ButtonBase)
                        return;
                var apparence = Apparence(carte);
                var reduite = !(bool)carte.GetValue(ReduiteProperty);
                apparence?.ReduireCarte(GetCarte(carte)!, reduite);
                Afficher(carte, contenu, chevron, reduite);
            };
        }

        Afficher(carte, contenu, chevron, Apparence(carte)?.CarteReduite(GetCarte(carte)!) == true);
    }

    private static void Afficher(Border carte, Panel contenu, TextBlock chevron, bool reduite)
    {
        carte.SetValue(ReduiteProperty, reduite);
        for (var i = 1; i < contenu.Children.Count; i++)
        {
            var enfant = contenu.Children[i];
            // Une visibilité liée (ex. bouton affiché selon un réglage) retrouve sa valeur à la réouverture.
            var liaison = System.Windows.Data.BindingOperations.GetBindingExpressionBase(enfant, UIElement.VisibilityProperty);
            if (liaison is not null)
            {
                if (reduite)
                    enfant.SetCurrentValue(UIElement.VisibilityProperty, Visibility.Collapsed);
                else
                    liaison.UpdateTarget();
            }
            else if (reduite)
                enfant.Visibility = Visibility.Collapsed;
            else
                enfant.ClearValue(UIElement.VisibilityProperty);
        }
        chevron.Text = reduite ? "" : ""; // flèche bas : réduite ; flèche haut : ouverte
        chevron.ToolTip = reduite ? "Afficher" : "Réduire";
        ((FrameworkElement)contenu.Children[0]).ToolTip = reduite ? "Cliquer pour afficher la carte" : "Cliquer pour réduire la carte";
    }

    /// <summary>Texte du titre de la carte (style TitreCarte).</summary>
    private static TextBlock? TitreDe(DependencyObject element)
    {
        if (element is TextBlock { Style: { } style } texte && ReferenceEquals(texte.TryFindResource("TitreCarte"), style))
            return texte;
        foreach (var enfant in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
            if (TitreDe(enfant) is { } trouve)
                return trouve;
        return null;
    }
}
