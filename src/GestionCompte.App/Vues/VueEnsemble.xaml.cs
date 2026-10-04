using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GestionCompte.Presentation;

namespace GestionCompte.App.Vues;

public partial class VueEnsemble : UserControl
{
    public VueEnsemble() => InitializeComponent();

    /// <summary>
    /// Lignes des comptes et colonne « Bourse » (seulement si un compte a des placements ; les colonnes ne sont pas
    /// dans l'arbre visuel). Les lignes sont données ici : l'écran est réutilisé d'une ouverture à l'autre de la vue d'ensemble.
    /// </summary>
    private void DonneesChangees(object sender, DependencyPropertyChangedEventArgs e)
    {
        var ensemble = e.NewValue as VueEnsembleViewModel;
        GrilleComptes.ItemsSource = ensemble?.Lignes;
        Dispatcher.InvokeAsync(() =>
        {
            ColonneBourse.Visibility = ensemble is { ABourse: true } ? Visibility.Visible : Visibility.Collapsed;
            GrilleComptes.Items.Refresh();
        }, DispatcherPriority.Loaded);
    }
}
