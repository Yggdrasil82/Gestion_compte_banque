using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using GestionCompte.Presentation;

namespace GestionCompte.App.Vues;

public partial class VueMois : UserControl
{
    private MoisViewModel? _mois;

    public VueMois()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Lier(DataContext as MoisViewModel);
    }

    private void Lier(MoisViewModel? mois)
    {
        if (_mois is not null)
            _mois.PropertyChanged -= MoisModifie;
        _mois = mois;
        if (mois is not null)
            mois.PropertyChanged += MoisModifie;
        Organiser();
    }

    private void MoisModifie(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MoisViewModel.RangerParCategorie))
            Organiser();
    }

    /// <summary>
    /// Opérations regroupées par catégorie (ordre de la configuration, sans catégorie à la fin) ou dans l'ordre de saisie.
    /// Le classement suit en direct les changements de catégorie et les déplacements (Monter / Descendre).
    /// </summary>
    private void Organiser()
    {
        GrilleOperations.CommitEdit(DataGridEditingUnit.Row, true);
        if (_mois is null)
        {
            GrilleOperations.ItemsSource = null;
            ColonneVirement.Visibility = Visibility.Collapsed;
            return;
        }

        var vue = new ListCollectionView(_mois.Operations.Elements) { IsLiveSorting = true, IsLiveGrouping = true };
        vue.LiveSortingProperties.Add(nameof(OperationViewModel.OrdreCategorie));
        vue.LiveSortingProperties.Add(nameof(OperationViewModel.Position));
        vue.LiveGroupingProperties.Add(nameof(OperationViewModel.GroupeCategorie));
        if (_mois.RangerParCategorie && _mois.ACategories)
        {
            vue.SortDescriptions.Add(new SortDescription(nameof(OperationViewModel.OrdreCategorie), ListSortDirection.Ascending));
            vue.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OperationViewModel.GroupeCategorie)));
        }
        vue.SortDescriptions.Add(new SortDescription(nameof(OperationViewModel.Position), ListSortDirection.Ascending));
        GrilleOperations.ItemsSource = vue;
        ColonneCategorie.Visibility = _mois.ACategories ? Visibility.Visible : Visibility.Collapsed;
        ColonneVirement.Visibility = _mois.Comptes.Disponibles ? Visibility.Visible : Visibility.Collapsed;
    }
}
