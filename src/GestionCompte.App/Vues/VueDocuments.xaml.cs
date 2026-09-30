using System.Windows.Controls;
using System.Windows.Input;
using GestionCompte.Presentation;

namespace GestionCompte.App.Vues;

public partial class VueDocuments : UserControl
{
    public VueDocuments() => InitializeComponent();

    /// <summary>Double-clic sur un document : il s'ouvre dans le programme habituel (lecteur PDF, visionneuse…).</summary>
    private void OuvrirDocument(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is DocumentsViewModel vm && vm.OuvrirCommand.CanExecute(null))
            vm.OuvrirCommand.Execute(null);
    }
}
