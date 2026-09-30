using System.Windows;
using GestionCompte.Core.Mail;

namespace GestionCompte.App;

/// <summary>Ajout ou modification d'un contact du carnet d'adresses.</summary>
public partial class FenetreContact : Window
{
    public FenetreContact(Contact? actuel)
    {
        InitializeComponent();
        Title = actuel is null ? "Nouveau contact" : "Modifier le contact";
        Nom.Text = actuel?.Nom ?? "";
        Email.Text = actuel?.Email ?? "";
        Notes.Text = actuel?.Notes ?? "";
        Loaded += (_, _) => Nom.Focus();
    }

    public Contact? Contact { get; private set; }

    private void Valider(object sender, RoutedEventArgs e)
    {
        if (!AdressesMail.Valide(Email.Text))
        {
            Probleme.Text = "Adresse mail incorrecte (exemple : prenom.nom@exemple.fr).";
            Probleme.Visibility = Visibility.Visible;
            return;
        }
        Contact = new Contact { Nom = Nom.Text.Trim(), Email = Email.Text.Trim(), Notes = Notes.Text.Trim() };
        DialogResult = true;
    }
}
