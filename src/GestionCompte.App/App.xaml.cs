using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using GestionCompte.Core;
using GestionCompte.Data;
using GestionCompte.Presentation;

namespace GestionCompte.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Toute l'application en français (dates, nombres), quelle que soit la langue de Windows.
        CultureInfo.DefaultThreadCurrentCulture = Montants.Francais;
        CultureInfo.DefaultThreadCurrentUICulture = Montants.Francais;
        Thread.CurrentThread.CurrentCulture = Montants.Francais;
        Thread.CurrentThread.CurrentUICulture = Montants.Francais;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(Montants.Francais.IetfLanguageTag)));

        // Mode utilisé par GitHub Actions : ouvre des données d'exemple, enregistre des captures d'écran puis quitte.
        var indexCaptures = Array.IndexOf(e.Args, "--captures");
        if (indexCaptures >= 0 && indexCaptures + 1 < e.Args.Length)
        {
            Captures.Lancer(this, e.Args[indexCaptures + 1]);
            return;
        }

        DispatcherUnhandledException += ErreurNonPrevue;

        // Apparence mémorisée à côté des données (Documents\GestionCompte\preferences.json).
        var apparence = new ApparenceViewModel(
            Path.Combine(Path.GetDirectoryName(DepotSqlite.CheminParDefaut)!, "preferences.json"));
        Themes.Appliquer(this, apparence.Ambiance, apparence.Sombre);
        apparence.Changee += (_, _) => Themes.Appliquer(this, apparence.Ambiance, apparence.Sombre);

        // Un fichier de données par compte, listés dans Documents\GestionCompte\comptes.json.
        var registre = RegistreComptes.Charger(Path.GetDirectoryName(DepotSqlite.CheminParDefaut)!);
        MainViewModel vm;
        try
        {
            // Identifiants Google Drive : chiffrés par Windows, propres à ce PC (hors du dossier des données, qui peut être synchronisé).
            var secrets = new SecretsWindows(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GestionCompte", "secrets"));
            vm = new MainViewModel(registre, new Dialogues(), DateTime.Today, apparence, secrets);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Les données n'ont pas pu être chargées depuis :\n{registre.Chemin(registre.Actif)}\n\n{ex.Message}\n\n" +
                "Le fichier n'a pas été modifié.",
                "Gestion Compte", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var fenetre = new MainWindow { DataContext = vm };
        fenetre.SourceInitialized += (_, _) => Themes.BarreDeTitreSombre(fenetre, apparence.Sombre);
        MainWindow = fenetre;
        fenetre.Show();
    }

    private void ErreurNonPrevue(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Une erreur inattendue s'est produite :\n\n{e.Exception.Message}",
            "Gestion Compte", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
