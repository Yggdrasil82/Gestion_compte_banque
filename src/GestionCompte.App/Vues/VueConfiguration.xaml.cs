using System.Windows;
using System.Windows.Controls;
using GestionCompte.Presentation;

namespace GestionCompte.App.Vues;

public partial class VueConfiguration : UserControl
{
    public VueConfiguration()
    {
        InitializeComponent();
        // Une colonne de tableau n'est pas dans l'arbre visuel : sa visibilité est réglée ici.
        DataContextChanged += (_, _) => ColonneVirement.Visibility =
            DataContext is ConfigurationViewModel { Comptes.Disponibles: true } ? Visibility.Visible : Visibility.Collapsed;
    }
}
