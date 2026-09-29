using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GestionCompte.Data;
using GestionCompte.Presentation;

namespace GestionCompte.App;

/// <summary>
/// Ouvre l'application sur des données d'exemple, enregistre une image PNG de chaque écran et de chaque apparence, puis quitte.
/// Utilisé par GitHub Actions (option « --captures dossier ») pour montrer l'interface sans PC Windows.
/// </summary>
internal static class Captures
{
    private const double Largeur = 1400;
    private const double Hauteur = 880;

    /// <summary>L'aide au budget est longue : elle est capturée en entier sur une image plus haute.</summary>
    private const double HauteurAide = 1260;

    /// <summary>Le module Crédits : comparaison, tableau d'amortissement et calcul inverse.</summary>
    private const double HauteurCredits = 960;

    /// <summary>La configuration aussi : toutes ses cartes sur une seule image.</summary>
    private const double HauteurConfiguration = 1700;

    public static void Lancer(App app, string dossier)
    {
        dossier = Path.GetFullPath(dossier);
        Directory.CreateDirectory(dossier);
        var fichierErreur = Path.Combine(dossier, "erreur.txt");

        app.DispatcherUnhandledException += (_, e) =>
        {
            File.WriteAllText(fichierErreur, e.Exception.ToString());
            e.Handled = true;
            app.Shutdown(3);
        };

        // Sécurité : ne jamais bloquer le serveur de compilation.
        var delaiMaximum = new DispatcherTimer { Interval = TimeSpan.FromSeconds(120) };
        delaiMaximum.Tick += (_, _) =>
        {
            File.WriteAllText(fichierErreur, "Délai dépassé pendant les captures.");
            app.Shutdown(2);
        };
        delaiMaximum.Start();

        // Deux comptes d'exemple : le compte courant et un livret.
        var dossierDemo = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"gestioncompte-demo-{Guid.NewGuid():N}")).FullName;
        var registre = RegistreComptes.Charger(dossierDemo);
        new DepotSqlite(registre.Chemin(registre.Actif)).Enregistrer(ConfigurationParDefaut.CreerDemo());
        new DepotSqlite(registre.Chemin(registre.Ajouter("Livret A"))).Enregistrer(ConfigurationParDefaut.CreerDemoLivret());
        var vm = new MainViewModel(registre, new Dialogues(), new DateTime(2026, 11, 15));
        vm.Apparence.Changee += (_, _) => Themes.Appliquer(app, vm.Apparence.Ambiance, vm.Apparence.Sombre);
        Themes.Appliquer(app, vm.Apparence.Ambiance, vm.Apparence.Sombre);

        // Le contenu de la fenêtre est rendu hors écran, à taille fixe :
        // l'écran du serveur de compilation est trop petit pour la fenêtre réelle.
        var fenetre = new MainWindow();
        var contenu = (UIElement)fenetre.Content;
        fenetre.Content = null;
        var hote = new Border { Child = contenu, DataContext = vm, Width = Largeur, Height = Hauteur };
        // Le contenu reste rattaché à la fenêtre (jamais affichée) pour recevoir les changements de couleurs.
        fenetre.Content = hote;
        hote.SetResourceReference(Border.BackgroundProperty, "Fond");
        hote.SetResourceReference(TextElement.ForegroundProperty, "Texte");
        TextElement.SetFontFamily(hote, new FontFamily("Segoe UI"));
        TextElement.SetFontSize(hote, 13);

        var etapes = new (string Nom, Ambiance Ambiance, bool Sombre, int Onglet, bool MoisPrecedent)[]
        {
            ("1-mois-ocean", Ambiance.Ocean, false, MainViewModel.OngletMois, false),
            ("2-mois-ocean-sombre", Ambiance.Ocean, true, MainViewModel.OngletMois, false),
            ("3-mois-pastel", Ambiance.Pastel, false, MainViewModel.OngletMois, false),
            ("4-mois-pastel-sombre", Ambiance.Pastel, true, MainViewModel.OngletMois, false),
            ("5-mois-nuit-octobre", Ambiance.Nuit, false, MainViewModel.OngletMois, true),
            ("6-previsionnel-ocean", Ambiance.Ocean, false, MainViewModel.OngletPrevisionnel, false),
            ("7-previsionnel-nuit", Ambiance.Nuit, false, MainViewModel.OngletPrevisionnel, false),
            ("8-previsionnel-pastel", Ambiance.Pastel, false, MainViewModel.OngletPrevisionnel, false),
            ("9-configuration-ocean", Ambiance.Ocean, false, MainViewModel.OngletConfiguration, false),
            ("10-aide-ocean", Ambiance.Ocean, false, MainViewModel.OngletAide, false),
            ("11-aide-nuit", Ambiance.Nuit, false, MainViewModel.OngletAide, false),
            ("12-import-ocean", Ambiance.Ocean, false, MainViewModel.OngletImport, false),
            ("13-import-pastel-sombre", Ambiance.Pastel, true, MainViewModel.OngletImport, false),
            ("14-ensemble-ocean", Ambiance.Ocean, false, MainViewModel.OngletEnsemble, false),
            ("15-credits-ocean", Ambiance.Ocean, false, MainViewModel.OngletCredits, false),
            ("16-credits-pastel-sombre", Ambiance.Pastel, true, MainViewModel.OngletCredits, false),
        };

        // Aperçu d'import sur un relevé d'exemple (rien n'est validé).
        var dossierReleve = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"gestioncompte-{Guid.NewGuid():N}")).FullName;
        var releve = Path.Combine(dossierReleve, "releve-decembre-2026.ofx");
        File.WriteAllText(releve, ConfigurationParDefaut.ReleveDemo());
        vm.OuvrirImport(releve);

        app.Dispatcher.InvokeAsync(async () =>
        {
            foreach (var etape in etapes)
            {
                vm.Apparence.Ambiance = etape.Ambiance;
                vm.Apparence.ModeSombre = etape.Sombre;
                vm.OngletSelectionne = etape.Onglet;
                if (etape.MoisPrecedent)
                    vm.MoisPrecedentCommand.Execute(null);

                var hauteur = etape.Onglet switch
                {
                    MainViewModel.OngletAide => HauteurAide,
                    MainViewModel.OngletCredits => HauteurCredits,
                    MainViewModel.OngletConfiguration => HauteurConfiguration,
                    _ => Hauteur,
                };
                hote.Height = hauteur;
                await MettreEnPage(hote, hauteur);
                Enregistrer(hote, Path.Combine(dossier, etape.Nom + ".png"), hauteur);

                if (etape.MoisPrecedent)
                    vm.MoisSuivantCommand.Execute(null);
            }

            Directory.Delete(dossierDemo, true);
            Directory.Delete(dossierReleve, true);
            app.Shutdown(0);
        });
    }

    private static async Task MettreEnPage(FrameworkElement element, double hauteur)
    {
        for (var passe = 0; passe < 3; passe++)
        {
            element.Measure(new Size(Largeur, hauteur));
            element.Arrange(new Rect(0, 0, Largeur, hauteur));
            element.UpdateLayout();
            await Task.Delay(250);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
    }

    private static void Enregistrer(FrameworkElement element, string chemin, double hauteur)
    {
        var image = new RenderTargetBitmap((int)Largeur, (int)hauteur, 96, 96, PixelFormats.Pbgra32);
        image.Render(element);

        var encodeur = new PngBitmapEncoder();
        encodeur.Frames.Add(BitmapFrame.Create(image));
        using var flux = File.Create(chemin);
        encodeur.Save(flux);
    }
}
