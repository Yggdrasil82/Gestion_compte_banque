using System.Windows.Controls;
using System.Windows.Input;
using GestionCompte.Presentation;

namespace GestionCompte.App.Vues;

public partial class VueAchats : UserControl
{
    public VueAchats() => InitializeComponent();

    /// <summary>Entrée dans la zone de recherche : lance la recherche.</summary>
    private void RechercheEntree(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not AchatsViewModel vm)
            return;
        // La saisie est déjà transmise (mise à jour à chaque frappe).
        if (vm.RechercherCommand.CanExecute(null))
            vm.RechercherCommand.Execute(null);
        e.Handled = true;
    }
}
