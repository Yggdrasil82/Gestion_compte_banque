using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GestionCompte.Data;
using GestionCompte.Presentation;

namespace GestionCompte.App;

/// <summary>
/// Ouvre l'application sur des données d'exemple, enregistre une image PNG de chaque écran puis quitte.
/// Utilisé par GitHub Actions (option « --captures dossier ») pour montrer l'interface sans PC Windows.
/// </summary>
internal static class Captures
{
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
        var delaiMaximum = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        delaiMaximum.Tick += (_, _) =>
        {
            File.WriteAllText(fichierErreur, "Délai dépassé pendant les captures.");
            app.Shutdown(2);
        };
        delaiMaximum.Start();

        var depot = new DepotSqlite(Path.Combine(Path.GetTempPath(), $"gestioncompte-demo-{Guid.NewGuid():N}.db"));
        depot.Enregistrer(ConfigurationParDefaut.CreerDemo());
        var vm = new MainViewModel(depot, new Dialogues(), new DateTime(2026, 11, 15));

        var fenetre = new MainWindow
        {
            DataContext = vm,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Width = 1400,
            Height = 860,
            ShowActivated = false,
        };
        app.MainWindow = fenetre;

        fenetre.ContentRendered += async (_, _) =>
        {
            var etapes = new (string Nom, Action Preparer)[]
            {
                ("1-mois-novembre-2026", () => vm.OngletSelectionne = MainViewModel.OngletMois),
                ("2-mois-octobre-2026", () => vm.MoisPrecedentCommand.Execute(null)),
                ("3-configuration", () => vm.OngletSelectionne = MainViewModel.OngletConfiguration),
            };

            foreach (var (nom, preparer) in etapes)
            {
                preparer();
                await Task.Delay(700);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Enregistrer(fenetre, Path.Combine(dossier, nom + ".png"));
            }

            File.Delete(depot.CheminFichier);
            app.Shutdown(0);
        };

        fenetre.Show();
    }

    private static void Enregistrer(Window fenetre, string chemin)
    {
        var contenu = (FrameworkElement)fenetre.Content;
        contenu.UpdateLayout();
        var largeur = (int)Math.Ceiling(contenu.ActualWidth);
        var hauteur = (int)Math.Ceiling(contenu.ActualHeight);

        // Le fond de la fenêtre n'appartient pas au contenu : on le dessine d'abord.
        var dessin = new DrawingVisual();
        using (var contexte = dessin.RenderOpen())
        {
            contexte.DrawRectangle(fenetre.Background, null, new Rect(0, 0, largeur, hauteur));
            contexte.DrawRectangle(new VisualBrush(contenu), null, new Rect(0, 0, largeur, hauteur));
        }

        var image = new RenderTargetBitmap(largeur, hauteur, 96, 96, PixelFormats.Pbgra32);
        image.Render(dessin);

        var encodeur = new PngBitmapEncoder();
        encodeur.Frames.Add(BitmapFrame.Create(image));
        using var flux = File.Create(chemin);
        encodeur.Save(flux);
    }
}
