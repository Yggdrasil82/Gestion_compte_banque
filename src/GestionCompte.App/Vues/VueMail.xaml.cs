using System.Windows.Controls;
using System.Windows.Input;
using GestionCompte.Presentation;

namespace GestionCompte.App.Vues;

public partial class VueMail : UserControl
{
    public VueMail() => InitializeComponent();

    /// <summary>Double-clic sur un contact : il est ajouté aux destinataires.</summary>
    private void EcrireAuContact(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MailViewModel vm && vm.EcrireAuContactCommand.CanExecute(null))
            vm.EcrireAuContactCommand.Execute(null);
    }
}
