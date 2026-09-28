using System.Windows;
using GestionCompte.Presentation;

namespace GestionCompte.App;

/// <summary>Création d'un compte : son nom et la configuration de départ (vierge ou copiée d'un compte existant).</summary>
public partial class FenetreNouveauCompte : Window
{
    private const string Vierge = "Configuration vierge";

    private readonly IReadOnlyList<string> _comptes;

    public FenetreNouveauCompte(IReadOnlyList<string> comptes, string compteActif)
    {
        InitializeComponent();
        _comptes = comptes;
        Modele.ItemsSource = new[] { Vierge }.Concat(comptes.Select(c => $"Copier « {c} »")).ToList();
        Modele.SelectedIndex = Math.Max(0, comptes.ToList().IndexOf(compteActif) + 1);
        Loaded += (_, _) => Nom.Focus();
    }

    public DemandeNouveauCompte? Demande { get; private set; }

    private void Creer(object sender, RoutedEventArgs e)
    {
        var index = Modele.SelectedIndex;
        Demande = new DemandeNouveauCompte(Nom.Text, index >= 1 ? _comptes[index - 1] : null);
        DialogResult = true;
    }
}
