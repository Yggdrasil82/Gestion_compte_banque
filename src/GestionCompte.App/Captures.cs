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

        var depot = new DepotSqlite(Path.Combine(Path.GetTempPath(), $"gestioncompte-demo-{Guid.NewGuid():N}.db"));
        depot.Enregistrer(ConfigurationParDefaut.CreerDemo());
        var vm = new MainViewModel(depot, new Dialogues(), new DateTime(2026, 11, 15));
        vm.Apparence.Changee += (_, _) => Themes.Appliquer(app, vm.Apparence.Ambiance, vm.Apparence.Sombre);
        Themes.Appliquer(app, vm.Apparence.Ambiance, vm.Apparence.Sombre);

        // Le contenu de la fenêtre est rendu hors écran, à taille fixe :
        // l'écran du serveur de compilation est trop petit pour la fenêtre réelle.
        var fenetre = new MainWindow();
        var contenu = (UIElement)fenetre.Content;
        fenetre.Content = null;
        var hote = new Border { Child = contenu, DataContext = vm, Width = Largeur, Height = Hauteur };
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
            ("6-configuration-ocean", Ambiance.Ocean, false, MainViewModel.OngletConfiguration, false),
            ("7-configuration-nuit", Ambiance.Nuit, false, MainViewModel.OngletConfiguration, false),
        };

        app.Dispatcher.InvokeAsync(async () =>
        {
            foreach (var etape in etapes)
            {
                vm.Apparence.Ambiance = etape.Ambiance;
                vm.Apparence.ModeSombre = etape.Sombre;
                vm.OngletSelectionne = etape.Onglet;
                if (etape.MoisPrecedent)
                    vm.MoisPrecedentCommand.Execute(null);

                await MettreEnPage(hote);
                Enregistrer(hote, Path.Combine(dossier, etape.Nom + ".png"));

                if (etape.MoisPrecedent)
                    vm.MoisSuivantCommand.Execute(null);
            }

            File.Delete(depot.CheminFichier);
            app.Shutdown(0);
        });
    }

    private static async Task MettreEnPage(FrameworkElement element)
    {
        for (var passe = 0; passe < 3; passe++)
        {
            element.Measure(new Size(Largeur, Hauteur));
            element.Arrange(new Rect(0, 0, Largeur, Hauteur));
            element.UpdateLayout();
            await Task.Delay(250);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
    }

    private static void Enregistrer(FrameworkElement element, string chemin)
    {
        var image = new RenderTargetBitmap((int)Largeur, (int)Hauteur, 96, 96, PixelFormats.Pbgra32);
        image.Render(element);

        var encodeur = new PngBitmapEncoder();
        encodeur.Frames.Add(BitmapFrame.Create(image));
        using var flux = File.Create(chemin);
        encodeur.Save(flux);
    }
}
