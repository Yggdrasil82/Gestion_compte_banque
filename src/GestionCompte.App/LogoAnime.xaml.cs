using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace GestionCompte.App;

/// <summary>Animation d'accueil (environ 2 secondes) : logo en fondu avec rebond, pièces qui tombent dans le coffre, titre.</summary>
public partial class LogoAnime : UserControl
{
    public static readonly TimeSpan Duree = TimeSpan.FromSeconds(2.1);

    /// <summary>Position horizontale des pièces (fente du coffre, dans le logo de 220 px).</summary>
    private static readonly double[] Colonnes = { 94, 108, 98, 112 };

    public LogoAnime()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        TexteVersion.Text = version is null ? "" : $"Version {version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }

    public void Demarrer()
    {
        Logo.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.45)));
        var rebond = new DoubleAnimationUsingKeyFrames();
        rebond.KeyFrames.Add(new EasingDoubleKeyFrame(0.6, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        rebond.KeyFrames.Add(new EasingDoubleKeyFrame(1.08, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.45)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        rebond.KeyFrames.Add(new EasingDoubleKeyFrame(0.97, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.6))));
        rebond.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.72))));
        Echelle.BeginAnimation(ScaleTransform.ScaleXProperty, rebond);
        Echelle.BeginAnimation(ScaleTransform.ScaleYProperty, rebond);

        for (var i = 0; i < Colonnes.Length; i++)
            FaireTomber(Colonnes[i], TimeSpan.FromSeconds(0.65 + 0.22 * i));

        Titre.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4))
        {
            BeginTime = TimeSpan.FromSeconds(0.9),
        });
    }

    /// <summary>État final, sans animation (captures d'écran).</summary>
    public void AfficherFin()
    {
        Logo.Opacity = 1;
        Echelle.ScaleX = Echelle.ScaleY = 1;
        Titre.Opacity = 1;
    }

    private void FaireTomber(double x, TimeSpan debut)
    {
        var piece = new Grid { Width = 22, Height = 22, Opacity = 0 };
        piece.Children.Add(new Ellipse
        {
            Fill = new LinearGradientBrush(Color.FromRgb(0xFF, 0xE3, 0x8A), Color.FromRgb(0xD6, 0x8A, 0x0C), 90),
            Stroke = new SolidColorBrush(Color.FromRgb(0xA8, 0x6A, 0x05)),
            StrokeThickness = 1.5,
        });
        piece.Children.Add(new TextBlock
        {
            Text = "€",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x55, 0x00)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Canvas.SetLeft(piece, x);
        Canvas.SetTop(piece, -60);
        Pieces.Children.Add(piece);

        var chute = new DoubleAnimation(-60, 128, TimeSpan.FromSeconds(0.42))
        {
            BeginTime = debut,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        piece.BeginAnimation(Canvas.TopProperty, chute);

        var visibilite = new DoubleAnimationUsingKeyFrames { BeginTime = debut };
        visibilite.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        visibilite.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.08))));
        visibilite.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.36))));
        visibilite.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.44))));
        piece.BeginAnimation(OpacityProperty, visibilite);
    }
}
