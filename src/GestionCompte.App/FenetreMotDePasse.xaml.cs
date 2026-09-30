using System.Windows;
using GestionCompte.Data.Documents;

namespace GestionCompte.App;

/// <summary>Saisie du mot de passe du coffre des documents (deux fois quand on le choisit).</summary>
public partial class FenetreMotDePasse : Window
{
    private readonly bool _confirmer;

    public FenetreMotDePasse(string titre, string message, bool confirmer)
    {
        InitializeComponent();
        Title = titre;
        Message.Text = message;
        _confirmer = confirmer;
        ZoneConfirmation.Visibility = confirmer ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => Saisie.Focus();
    }

    public string Valeur => Saisie.Password;

    private void Valider(object sender, RoutedEventArgs e)
    {
        string? probleme = null;
        if (_confirmer && Saisie.Password.Length < CoffreDocuments.LongueurMinimale)
            probleme = $"Le mot de passe doit faire au moins {CoffreDocuments.LongueurMinimale} caractères.";
        else if (_confirmer && Saisie.Password != Confirmation.Password)
            probleme = "Les deux saisies sont différentes.";
        else if (Saisie.Password.Length == 0)
            probleme = "Saisissez le mot de passe.";

        if (probleme is null)
        {
            DialogResult = true;
            return;
        }
        Probleme.Text = probleme;
        Probleme.Visibility = Visibility.Visible;
    }
}
