using System.Windows;

namespace GestionCompte.App;

/// <summary>Saisie d'un texte court (ex. nouveau nom d'un compte).</summary>
public partial class FenetreNom : Window
{
    public FenetreNom(string titre, string message, string valeur)
    {
        InitializeComponent();
        Title = titre;
        Message.Text = message;
        Saisie.Text = valeur;
        Loaded += (_, _) =>
        {
            Saisie.Focus();
            Saisie.SelectAll();
        };
    }

    public string Valeur => Saisie.Text;

    private void Valider(object sender, RoutedEventArgs e) => DialogResult = true;
}
