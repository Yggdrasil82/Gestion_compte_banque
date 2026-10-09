using System.Windows;
using GestionCompte.Presentation;

namespace GestionCompte.App;

/// <summary>Ouverture des données de Google Drive : compte Google, mot de passe, PC de confiance.</summary>
public partial class FenetreOuvertureDrive : Window
{
    private readonly OuvertureDriveViewModel _vm;

    public FenetreOuvertureDrive(OuvertureDriveViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += (_, _) => MotDePasse.Focus();
    }

    /// <summary>Données téléchargées et déchiffrées : la session est prête.</summary>
    public event EventHandler<SessionDrive>? Ouverte;

    private async void Connecter(object sender, RoutedEventArgs e) => await _vm.ConnecterAsync();

    private async void Ouvrir(object sender, RoutedEventArgs e)
    {
        if (await _vm.OuvrirAsync(MotDePasse.Password) && _vm.Session is { } session)
        {
            MotDePasse.Clear();
            Ouverte?.Invoke(this, session);
            Close();
        }
        else
        {
            MotDePasse.SelectAll();
            MotDePasse.Focus();
        }
    }

    private void Quitter(object sender, RoutedEventArgs e) => Close();
}
