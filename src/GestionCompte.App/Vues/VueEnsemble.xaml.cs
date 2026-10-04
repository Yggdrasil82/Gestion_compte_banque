using System.Windows;
using System.Windows.Controls;
using GestionCompte.Presentation;

namespace GestionCompte.App.Vues;

public partial class VueEnsemble : UserControl
{
    public VueEnsemble() => InitializeComponent();

    /// <summary>Colonne « Bourse » seulement si un compte a des placements (les colonnes ne sont pas dans l'arbre visuel).</summary>
    private void DonneesChangees(object sender, DependencyPropertyChangedEventArgs e) =>
        ColonneBourse.Visibility = e.NewValue is VueEnsembleViewModel { ABourse: true } ? Visibility.Visible : Visibility.Collapsed;
}
