using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GestionCompte.Presentation;

namespace GestionCompte.App.Vues;

public partial class VueEnsemble : UserControl
{
    public VueEnsemble() => InitializeComponent();

    /// <summary>Colonne « Bourse » seulement si un compte a des placements (les colonnes ne sont pas dans l'arbre visuel).</summary>
    /// <remarks>Appliqué après la mise à jour des lignes : changer une colonne pendant le changement de données gêne leur affichage.</remarks>
    private void DonneesChangees(object sender, DependencyPropertyChangedEventArgs e)
    {
        var bourse = e.NewValue is VueEnsembleViewModel { ABourse: true };
        Dispatcher.InvokeAsync(() => ColonneBourse.Visibility = bourse ? Visibility.Visible : Visibility.Collapsed,
            DispatcherPriority.Loaded);
    }
}
