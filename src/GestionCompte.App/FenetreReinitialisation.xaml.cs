using System.Windows;
using GestionCompte.Presentation;

namespace GestionCompte.App;

/// <summary>Choix de la réinitialisation : effacer les mois, ou tout effacer pour une nouvelle personne.</summary>
public partial class FenetreReinitialisation : Window
{
    public FenetreReinitialisation() => InitializeComponent();

    public DemandeReinitialisation? Demande { get; private set; }

    private void Continuer(object sender, RoutedEventArgs e)
    {
        Demande = OptionTout.IsChecked == true
            ? new DemandeReinitialisation(ChoixReinitialisation.ToutEffacer, CopieAvant.IsChecked == true)
            : new DemandeReinitialisation(ChoixReinitialisation.EffacerMois, CopieAvant: false);
        DialogResult = true;
    }
}
