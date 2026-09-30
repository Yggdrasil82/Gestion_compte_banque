using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace GestionCompte.App;

/// <summary>Fenêtre d'accueil animée ; un clic la passe.</summary>
public partial class FenetreAccueil : Window
{
    private readonly DispatcherTimer _minuterie = new() { Interval = LogoAnime.Duree };
    private bool _terminee;

    public FenetreAccueil()
    {
        InitializeComponent();
        _minuterie.Tick += (_, _) => Terminer();
        Loaded += (_, _) =>
        {
            Animation.Demarrer();
            _minuterie.Start();
        };
        MouseLeftButtonUp += (_, _) => Terminer();
        KeyUp += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Enter or Key.Space)
                Terminer();
        };
    }

    /// <summary>Levé une seule fois, à la fin de l'animation ou au clic.</summary>
    public event EventHandler? Terminee;

    private void Terminer()
    {
        if (_terminee)
            return;
        _terminee = true;
        _minuterie.Stop();
        Terminee?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
