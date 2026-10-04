using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using GestionCompte.Presentation;

namespace GestionCompte.App.Vues;

public partial class VuePrets : UserControl
{
    public VuePrets() => InitializeComponent();

    private void TableauMisAJour(object? sender, DataTransferEventArgs e) => AllerAuMoisEnCours(sender);

    private void TableauCharge(object sender, RoutedEventArgs e) => AllerAuMoisEnCours(sender);

    private void TableauVisible(object sender, DependencyPropertyChangedEventArgs e) => AllerAuMoisEnCours(sender);

    /// <summary>Fait défiler le tableau d'amortissement jusqu'au mois en cours (placé en haut, avec deux mois avant).</summary>
    private void AllerAuMoisEnCours(object? sender)
    {
        if (sender is not DataGrid grille)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (grille.ItemsSource is not IReadOnlyList<LignePretViewModel> lignes)
                return;
            var index = lignes.ToList().FindIndex(l => l.MoisEnCours);
            if (index < 0)
                return;
            grille.UpdateLayout();
            // Défilement par ligne (tableau virtualisé) : la position est un numéro de ligne.
            if (Defilement(grille) is { } defilement)
                defilement.ScrollToVerticalOffset(Math.Max(0, index - 2));
        });
    }

    private static ScrollViewer? Defilement(DependencyObject element)
    {
        if (element is ScrollViewer defilement)
            return defilement;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (Defilement(VisualTreeHelper.GetChild(element, i)) is { } trouve)
                return trouve;
        return null;
    }
}
