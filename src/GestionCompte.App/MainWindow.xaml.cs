using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GestionCompte.Presentation;

namespace GestionCompte.App;

public partial class MainWindow : Window
{
    /// <summary>Largeur de la barre de gauche, entière ou réduite aux icônes.</summary>
    private const double LargeurBarre = 220;
    private const double LargeurBarreReduite = 76;

    private ApparenceViewModel? _apparence;

    public MainWindow()
    {
        InitializeComponent();
        // Suivi sur la barre elle-même : elle reçoit aussi le contexte quand son contenu est rendu ailleurs (captures).
        BarreBudget.DataContextChanged += (_, _) => Suivre((BarreBudget.DataContext as MainViewModel)?.Apparence);
    }

    /// <summary>Range les modules de la barre de gauche dans l'ordre choisi pour le compte ouvert.</summary>
    private void Suivre(ApparenceViewModel? apparence)
    {
        if (_apparence is not null)
            _apparence.PropertyChanged -= ApparenceChangee;
        _apparence = apparence;
        if (apparence is null)
            return;
        apparence.PropertyChanged += ApparenceChangee;
        RangerModules();
        AjusterLargeur();
    }

    private void ApparenceChangee(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApparenceViewModel.OrdreModules))
            RangerModules();
        else if (e.PropertyName == nameof(ApparenceViewModel.BarreReduite))
            AjusterLargeur();
    }

    private void AjusterLargeur() =>
        ColonneBarre.Width = new GridLength(_apparence?.BarreReduite == true ? LargeurBarreReduite : LargeurBarre);

    /// <summary>Dans chaque thème : les écrans fixes d'abord (Mois…), puis les modules dans l'ordre choisi.</summary>
    private void RangerModules()
    {
        if (_apparence is null)
            return;
        var positions = _apparence.OrdreModules.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i);
        foreach (var theme in new Panel[] { BarreBudget, BarreFinances, BarreOutils })
        {
            var boutons = theme.Children.OfType<UIElement>().ToList();
            var ranges = boutons
                .OrderBy(b => string.IsNullOrEmpty(b.Uid) ? -1 : positions.GetValueOrDefault(b.Uid, int.MaxValue))
                .ToList();
            if (ranges.SequenceEqual(boutons))
                continue;
            theme.Children.Clear();
            foreach (var bouton in ranges)
                theme.Children.Add(bouton);
        }
    }
}
