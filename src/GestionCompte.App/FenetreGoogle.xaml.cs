using System.Diagnostics;
using System.Windows;
using GestionCompte.Data.Documents;

namespace GestionCompte.App;

/// <summary>Saisie de l'identifiant d'application Google (avec le guide pour le créer).</summary>
public partial class FenetreGoogle : Window
{
    private readonly IdentifiantsGoogle? _actuels;

    public FenetreGoogle(IdentifiantsGoogle? actuels)
    {
        InitializeComponent();
        _actuels = actuels;
        ClientId.Text = actuels?.ClientId ?? "";
        if (actuels is not null)
            LibelleSecret.Text = "Code secret du client (laisser vide pour garder celui déjà saisi)";
        Loaded += (_, _) => ClientId.Focus();
    }

    public IdentifiantsGoogle? Identifiants { get; private set; }

    private void OuvrirConsole(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://console.cloud.google.com/") { UseShellExecute = true });

    private void Valider(object sender, RoutedEventArgs e)
    {
        var id = ClientId.Text.Trim();
        var secret = Secret.Password.Trim();
        if (secret.Length == 0 && _actuels is not null && id == _actuels.ClientId)
            secret = _actuels.ClientSecret;

        if (!id.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase) || secret.Length == 0)
        {
            Probleme.Text = "Saisissez l'ID client (…apps.googleusercontent.com) et le code secret du client.";
            Probleme.Visibility = Visibility.Visible;
            return;
        }
        Identifiants = new IdentifiantsGoogle(id, secret);
        DialogResult = true;
    }
}
