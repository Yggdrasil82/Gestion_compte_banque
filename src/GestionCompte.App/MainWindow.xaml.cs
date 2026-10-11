using System.ComponentModel;
using System.Windows;
using GestionCompte.Presentation;

namespace GestionCompte.App;

public partial class MainWindow : Window
{
    private ApparenceViewModel? _apparence;

    public MainWindow()
    {
        InitializeComponent();
        // Suivi sur la barre elle-même : elle reçoit aussi le contexte quand son contenu est rendu ailleurs (captures).
        BarreModules.DataContextChanged += (_, _) => Suivre((BarreModules.DataContext as MainViewModel)?.Apparence);
    }

    /// <summary>Range les modules de la barre de gauche dans l'ordre choisi pour le compte ouvert.</summary>
    private void Suivre(ApparenceViewModel? apparence)
    {
        if (_apparence is not null)
            _apparence.PropertyChanged -= OrdreChange;
        _apparence = apparence;
        if (apparence is null)
            return;
        apparence.PropertyChanged += OrdreChange;
        RangerModules();
    }

    private void OrdreChange(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApparenceViewModel.OrdreModules))
            RangerModules();
    }

    private void RangerModules()
    {
        if (_apparence is null)
            return;
        var boutons = BarreModules.Children.OfType<UIElement>().ToList();
        var positions = _apparence.OrdreModules.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i);
        var ranges = boutons.OrderBy(b => positions.GetValueOrDefault(b.Uid, int.MaxValue)).ToList();
        if (ranges.SequenceEqual(boutons))
            return;
        BarreModules.Children.Clear();
        foreach (var bouton in ranges)
            BarreModules.Children.Add(bouton);
    }
}
