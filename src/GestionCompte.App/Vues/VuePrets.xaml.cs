using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using GestionCompte.Presentation;

namespace GestionCompte.App.Vues;

public partial class VuePrets : UserControl
{
    public VuePrets() => InitializeComponent();

    /// <summary>Fait défiler le tableau d'amortissement jusqu'au mois en cours (placé en haut, avec deux mois avant).</summary>
    private void TableauMisAJour(object? sender, DataTransferEventArgs e)
    {
        if (sender is not DataGrid grille || grille.ItemsSource is not IReadOnlyList<LignePretViewModel> lignes)
            return;
        var index = lignes.ToList().FindIndex(l => l.MoisEnCours);
        if (index < 0 || lignes.Count == 0)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            grille.ScrollIntoView(lignes[^1]);
            grille.ScrollIntoView(lignes[Math.Max(0, index - 2)]);
        });
    }
}
