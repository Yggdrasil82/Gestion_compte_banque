using System.Windows;
using System.Windows.Controls;
using GestionCompte.Core.Mail;
using GestionCompte.Data.Mail;
using GestionCompte.Presentation;

namespace GestionCompte.App;

/// <summary>Réglages du serveur d'envoi (SMTP) : Orange, Free, SFR…</summary>
public partial class FenetreSmtp : Window
{
    private readonly ReglagesSmtp? _actuels;

    public FenetreSmtp(ReglagesSmtp? actuels)
    {
        InitializeComponent();
        _actuels = actuels;
        Connus.ItemsSource = ServeurConnu.Liste;
        Securite.ItemsSource = new[] { "SSL (port 465)", "STARTTLS (port 587)" };
        Serveur.Text = actuels?.Serveur ?? "";
        Port.Text = (actuels?.Port ?? 465).ToString();
        Securite.SelectedIndex = actuels?.Securite == SecuriteSmtp.StartTls ? 1 : 0;
        Identifiant.Text = actuels?.Identifiant ?? "";
        Expediteur.Text = actuels?.Expediteur ?? "";
        NomAffiche.Text = actuels?.NomAffiche ?? "";
        if (actuels is not null)
            LibelleMotDePasse.Text = "Mot de passe (laisser vide pour garder celui déjà enregistré)";
    }

    public SaisieSmtp? Saisie { get; private set; }

    private void ServeurChoisi(object sender, SelectionChangedEventArgs e)
    {
        if (Connus.SelectedItem is not ServeurConnu connu)
            return;
        Serveur.Text = connu.Serveur;
        Port.Text = connu.Port.ToString();
        Securite.SelectedIndex = connu.Securite == SecuriteSmtp.StartTls ? 1 : 0;
    }

    private void Valider(object sender, RoutedEventArgs e)
    {
        string? probleme = null;
        if (string.IsNullOrWhiteSpace(Serveur.Text))
            probleme = "Indiquez le serveur SMTP (exemple : smtp.orange.fr).";
        else if (!int.TryParse(Port.Text, out var port) || port is < 1 or > 65535)
            probleme = "Le port doit être un nombre (465 ou 587 le plus souvent).";
        else if (string.IsNullOrWhiteSpace(Identifiant.Text))
            probleme = "Indiquez l'identifiant du compte mail.";
        else if (!string.IsNullOrWhiteSpace(Expediteur.Text) && !AdressesMail.Valide(Expediteur.Text))
            probleme = "L'adresse d'expédition est incorrecte.";
        else if (string.IsNullOrWhiteSpace(Expediteur.Text) && !AdressesMail.Valide(Identifiant.Text))
            probleme = "L'identifiant n'est pas une adresse mail : indiquez l'adresse d'expédition.";
        else if (_actuels is null && MotDePasse.Password.Length == 0)
            probleme = "Indiquez le mot de passe.";

        if (probleme is not null)
        {
            Probleme.Text = probleme;
            Probleme.Visibility = Visibility.Visible;
            return;
        }

        Saisie = new SaisieSmtp(
            new ReglagesSmtp(Serveur.Text.Trim(), int.Parse(Port.Text), Securite.SelectedIndex == 1 ? SecuriteSmtp.StartTls : SecuriteSmtp.Ssl,
                Identifiant.Text.Trim(), Expediteur.Text.Trim(), NomAffiche.Text.Trim()),
            MotDePasse.Password.Length > 0 ? MotDePasse.Password : null);
        DialogResult = true;
    }
}
