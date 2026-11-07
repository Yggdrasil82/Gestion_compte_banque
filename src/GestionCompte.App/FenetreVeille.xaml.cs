using System.Diagnostics;
using System.Windows;
using GestionCompte.Presentation;

namespace GestionCompte.App;

/// <summary>Dépôt GitHub privé de la veille des marchés et clé en lecture seule.</summary>
public partial class FenetreVeille : Window
{
    private readonly bool _cleExistante;

    public FenetreVeille(string depot, bool cleExistante)
    {
        InitializeComponent();
        _cleExistante = cleExistante;
        Depot.Text = depot;
        if (cleExistante)
            LibelleCle.Text = "Clé GitHub (laisser vide pour garder celle déjà enregistrée)";
        Loaded += (_, _) => (depot.Length == 0 ? (UIElement)Depot : Cle).Focus();
    }

    public SaisieVeille? Saisie { get; private set; }

    private void OuvrirLien(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(VeilleMarchesViewModel.AdresseCle) { UseShellExecute = true });

    private void Valider(object sender, RoutedEventArgs e)
    {
        if (Depot.Text.Trim().Length == 0)
        {
            Depot.Focus();
            return;
        }
        if (!_cleExistante && Cle.Password.Trim().Length == 0)
        {
            Cle.Focus();
            return;
        }
        Saisie = new SaisieVeille(Depot.Text.Trim(), Cle.Password.Trim().Length > 0 ? Cle.Password.Trim() : null);
        DialogResult = true;
    }
}
