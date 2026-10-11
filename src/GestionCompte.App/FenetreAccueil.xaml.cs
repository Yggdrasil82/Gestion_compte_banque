using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace GestionCompte.App;

/// <summary>Fenêtre d'accueil : vidéo du canard (ou l'animation de secours) ; un clic la passe.</summary>
public partial class FenetreAccueil : Window
{
    /// <summary>Durée maximale de l'accueil, même si la vidéo ne signale pas sa fin.</summary>
    private static readonly TimeSpan DureeVideo = TimeSpan.FromSeconds(6.5);

    private readonly DispatcherTimer _minuterie = new() { Interval = LogoAnime.Duree };

    // Si la vidéo ne s'ouvre pas à temps, l'animation de secours prend le relais.
    private readonly DispatcherTimer _attenteVideo = new() { Interval = TimeSpan.FromSeconds(3) };

    // Si la fenêtre n'est jamais chargée (affichage bloqué), l'application s'ouvre quand même.
    private readonly DispatcherTimer _secours = new() { Interval = DureeVideo + TimeSpan.FromSeconds(5) };
    private bool _terminee;
    private bool _secoursAffiche;

    public FenetreAccueil()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        TexteVersion.Text = version is null ? "Mon Budget" : $"Mon Budget · version {version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        _minuterie.Tick += (_, _) => Terminer();
        _attenteVideo.Tick += (_, _) =>
        {
            JournalDemarrage.Noter("Vidéo d'accueil pas ouverte à temps : animation de secours");
            AfficherSecours();
        };
        _secours.Tick += (_, _) =>
        {
            JournalDemarrage.Noter("Accueil jamais terminé : ouverture sans attendre");
            Terminer();
        };
        _secours.Start();
        Lecteur.MediaOpened += (_, _) =>
        {
            _attenteVideo.Stop();
            JournalDemarrage.Noter("Vidéo d'accueil affichée");
            _minuterie.Interval = DureeVideo;
            _minuterie.Start();
        };
        Lecteur.MediaEnded += (_, _) => Terminer();
        Lecteur.MediaFailed += (_, e) =>
        {
            JournalDemarrage.Noter($"Vidéo d'accueil illisible ({e.ErrorException?.Message}) : animation de secours");
            AfficherSecours();
        };
        Loaded += (_, _) =>
        {
            if (CheminVideo() is { } chemin)
            {
                Lecteur.Source = new Uri(chemin);
                Lecteur.Play();
                _attenteVideo.Start();
            }
            else
                AfficherSecours();
        };
        MouseLeftButtonUp += (_, _) => Terminer();
        KeyUp += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Enter or Key.Space)
                Terminer();
        };
    }

    /// <summary>Levé une seule fois, à la fin de la vidéo ou de l'animation, ou au clic.</summary>
    public event EventHandler? Terminee;

    private void AfficherSecours()
    {
        if (_secoursAffiche || _terminee)
            return;
        _secoursAffiche = true;
        _attenteVideo.Stop();
        _minuterie.Stop();
        Lecteur.Close();
        Video.Visibility = Visibility.Collapsed;
        Animation.Visibility = Visibility.Visible;
        JournalDemarrage.Noter("Animation affichée");
        Animation.Demarrer();
        _minuterie.Interval = LogoAnime.Duree;
        _minuterie.Start();
    }

    /// <summary>
    /// Vidéo copiée une fois par version dans %LOCALAPPDATA%\GestionCompte (le lecteur de Windows lit un fichier, pas une
    /// ressource) ; null si la copie est impossible.
    /// </summary>
    private static string? CheminVideo()
    {
        try
        {
            var assemblage = Assembly.GetExecutingAssembly();
            var dossier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GestionCompte");
            var chemin = Path.Combine(dossier, $"ouverture-{assemblage.GetName().Version}.mp4");
            if (File.Exists(chemin))
                return chemin;
            using var flux = assemblage.GetManifestResourceStream("ouverture.mp4");
            if (flux is null)
                return null;
            Directory.CreateDirectory(dossier);
            foreach (var ancienne in Directory.EnumerateFiles(dossier, "ouverture-*.mp4"))
            {
                try
                {
                    File.Delete(ancienne);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Vidéo d'une autre version encore ouverte : effacée une prochaine fois.
                }
            }
            var temporaire = chemin + ".tmp";
            using (var fichier = File.Create(temporaire))
                flux.CopyTo(fichier);
            File.Move(temporaire, chemin, overwrite: true);
            return chemin;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            JournalDemarrage.Noter($"Vidéo d'accueil non copiée : {e.Message}");
            return null;
        }
    }

    private void Terminer()
    {
        if (_terminee)
            return;
        _terminee = true;
        _minuterie.Stop();
        _attenteVideo.Stop();
        _secours.Stop();
        Lecteur.Stop();
        Lecteur.Close();
        Terminee?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
