using System.Windows;
using GestionCompte.Core.Lettres;

namespace GestionCompte.App;

/// <summary>Coordonnées de l'expéditeur des lettres.</summary>
public partial class FenetreCoordonnees : Window
{
    public FenetreCoordonnees(Coordonnees actuelles)
    {
        InitializeComponent();
        Nom.Text = actuelles.Nom;
        Adresse.Text = actuelles.Adresse;
        Telephone.Text = actuelles.Telephone;
        Email.Text = actuelles.Email;
        Ville.Text = actuelles.Ville;
        Loaded += (_, _) => Nom.Focus();
    }

    public Coordonnees? Coordonnees { get; private set; }

    private void Valider(object sender, RoutedEventArgs e)
    {
        Coordonnees = new Coordonnees
        {
            Nom = Nom.Text.Trim(),
            Adresse = Adresse.Text.Replace("\r", "").Trim(),
            Telephone = Telephone.Text.Trim(),
            Email = Email.Text.Trim(),
            Ville = Ville.Text.Trim(),
        };
        DialogResult = true;
    }
}
