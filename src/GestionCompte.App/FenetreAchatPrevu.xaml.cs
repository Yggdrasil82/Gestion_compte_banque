using System.Windows;
using GestionCompte.Core;
using GestionCompte.Presentation;

namespace GestionCompte.App;

/// <summary>Mois, libellé et montant d'un achat ajouté au prévisionnel.</summary>
public partial class FenetreAchatPrevu : Window
{
    public FenetreAchatPrevu(string libelle, decimal montant, IReadOnlyList<ChoixPeriode> periodes)
    {
        InitializeComponent();
        Libelle.Text = libelle;
        Montant.Text = Montants.Formater(montant);
        Mois.ItemsSource = periodes;
        Mois.SelectedIndex = periodes.Count > 0 ? 0 : -1;
        Loaded += (_, _) => Mois.Focus();
    }

    public AchatPrevu? Achat { get; private set; }

    private void Valider(object sender, RoutedEventArgs e)
    {
        string? probleme = null;
        if (Libelle.Text.Trim().Length == 0)
            probleme = "Indiquez un libellé.";
        else if (Mois.SelectedItem is not ChoixPeriode)
            probleme = "Choisissez le mois.";
        else if (!Montants.TryLire(Montant.Text, out var m) || m <= 0)
            probleme = "Montant incorrect.";

        if (probleme is not null)
        {
            Probleme.Text = probleme;
            Probleme.Visibility = Visibility.Visible;
            return;
        }
        Montants.TryLire(Montant.Text, out var montant);
        Achat = new AchatPrevu(((ChoixPeriode)Mois.SelectedItem!).Periode, Libelle.Text.Trim(), montant);
        DialogResult = true;
    }
}
