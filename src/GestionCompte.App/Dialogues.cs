using System.Diagnostics;
using System.Windows;
using GestionCompte.Presentation;
using Microsoft.Win32;

namespace GestionCompte.App;

/// <summary>Fenêtres de dialogue Windows standard.</summary>
public sealed class Dialogues : IDialogues
{
    private const string Titre = "Gestion Compte";
    private const string FiltreFichiers = "Données Gestion Compte (*.db)|*.db|Tous les fichiers (*.*)|*.*";

    private static Window? Fenetre => Application.Current?.MainWindow;

    public bool Confirmer(string titre, string message) =>
        Afficher(message, titre, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Erreur(string message) => Afficher(message, Titre, MessageBoxButton.OK, MessageBoxImage.Error);

    private static MessageBoxResult Afficher(string message, string titre, MessageBoxButton boutons, MessageBoxImage icone) =>
        Fenetre is { } fenetre
            ? MessageBox.Show(fenetre, message, titre, boutons, icone)
            : MessageBox.Show(message, titre, boutons, icone);

    public string? ChoisirFichierSauvegarde(string nomParDefaut)
    {
        var dialogue = new SaveFileDialog
        {
            Title = "Enregistrer une copie des données",
            FileName = nomParDefaut,
            Filter = FiltreFichiers,
            DefaultExt = ".db",
            OverwritePrompt = true,
        };
        return dialogue.ShowDialog(Fenetre) == true ? dialogue.FileName : null;
    }

    public string? ChoisirFichierExport(string nomParDefaut)
    {
        var dialogue = new SaveFileDialog
        {
            Title = "Exporter le mois vers Excel",
            FileName = nomParDefaut,
            Filter = "Classeur Excel (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            OverwritePrompt = true,
        };
        return dialogue.ShowDialog(Fenetre) == true ? dialogue.FileName : null;
    }

    public string? ChoisirFichierARestaurer()
    {
        var dialogue = new OpenFileDialog
        {
            Title = "Restaurer une sauvegarde",
            Filter = FiltreFichiers,
            CheckFileExists = true,
        };
        return dialogue.ShowDialog(Fenetre) == true ? dialogue.FileName : null;
    }

    public void OuvrirDossier(string dossier) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dossier}\"") { UseShellExecute = true });
}
