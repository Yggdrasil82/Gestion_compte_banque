using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace GestionCompte.App;

/// <summary>Affiche la clé de secours du coffre, à copier ou enregistrer avant de continuer.</summary>
public partial class FenetreCleSecours : Window
{
    public FenetreCleSecours(string cle)
    {
        InitializeComponent();
        Cle.Text = cle;
        // La fenêtre ne se ferme qu'une fois la clé notée.
        Closing += (_, e) => e.Cancel = Notee.IsChecked != true;
    }

    private void Copier(object sender, RoutedEventArgs e) => Clipboard.SetText(Cle.Text);

    private void Enregistrer(object sender, RoutedEventArgs e)
    {
        var dialogue = new SaveFileDialog
        {
            Title = "Enregistrer la clé de secours",
            FileName = "Clé de secours - coffre Gestion Compte.txt",
            Filter = "Fichier texte (*.txt)|*.txt",
            DefaultExt = ".txt",
        };
        if (dialogue.ShowDialog(this) == true)
            File.WriteAllText(dialogue.FileName,
                $"Clé de secours du coffre de documents (Gestion Compte) :\r\n\r\n{Cle.Text}\r\n\r\n" +
                "À garder en lieu sûr : elle permet de choisir un nouveau mot de passe en cas d'oubli.\r\n");
    }

    private void CaseCochee(object sender, RoutedEventArgs e) => Fermer.IsEnabled = Notee.IsChecked == true;

    private void Terminer(object sender, RoutedEventArgs e) => Close();
}
