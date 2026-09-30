using System.Windows.Controls;
using System.Windows.Input;
using GestionCompte.Core.Mail;
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

    /// <summary>Sélection multiple du carnet (non liable en XAML) : transmise au modèle de vue.</summary>
    private void ContactsChoisis(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MailViewModel vm && sender is ListBox liste)
            vm.ContactsSelectionnes = liste.SelectedItems.OfType<Contact>().ToList();
    }
}
