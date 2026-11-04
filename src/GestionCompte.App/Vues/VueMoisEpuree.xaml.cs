using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using GestionCompte.Presentation;

namespace GestionCompte.App.Vues;

/// <summary>
/// Vue épurée du mois : modules dans deux colonnes, déplacés par glisser-déposer en mode « Personnaliser »,
/// opérations filtrées (à pointer, pointées, toutes) et colonnes au choix.
/// </summary>
public partial class VueMoisEpuree : UserControl
{
    private MoisViewModel? _mois;
    private DispositionMoisViewModel? _disposition;
    private readonly List<DataGrid> _grilles = new();

    public VueMoisEpuree()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Lier();
        Loaded += (_, _) => Lier();
        Unloaded += (_, _) => Delier();
    }

    private void Lier()
    {
        Delier();
        if (DataContext is not MoisViewModel mois)
            return;
        _mois = mois;
        _disposition = mois.Disposition;
        mois.PropertyChanged += MoisModifie;
        _disposition.PropertyChanged += DispositionModifiee;
        foreach (var grille in _grilles)
            Organiser(grille);
    }

    private void Delier()
    {
        if (_mois is not null)
            _mois.PropertyChanged -= MoisModifie;
        if (_disposition is not null)
            _disposition.PropertyChanged -= DispositionModifiee;
        _mois = null;
        _disposition = null;
    }

    private void MoisModifie(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MoisViewModel.RangerParCategorie))
            foreach (var grille in _grilles)
                Organiser(grille);
    }

    private void DispositionModifiee(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DispositionMoisViewModel.Filtre))
        {
            foreach (var grille in _grilles)
                (grille.ItemsSource as ICollectionView)?.Refresh();
        }
        else
        {
            foreach (var grille in _grilles)
                AfficherColonnes(grille);
        }
    }

    private void Grille_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid grille)
            return;
        if (!_grilles.Contains(grille))
            _grilles.Add(grille);
        if (_mois is null)
            Lier();
        Organiser(grille);
    }

    private void Grille_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGrid grille)
            _grilles.Remove(grille);
    }

    /// <summary>
    /// Opérations filtrées (à pointer, pointées ou toutes), regroupées par catégorie ou dans l'ordre de saisie.
    /// Le filtre et le classement suivent en direct le pointage, les changements de catégorie et les déplacements.
    /// </summary>
    private void Organiser(DataGrid grille)
    {
        grille.CommitEdit(DataGridEditingUnit.Row, true);
        if (_mois is null || _disposition is null)
        {
            grille.ItemsSource = null;
            return;
        }

        var disposition = _disposition;
        var vue = new ListCollectionView(_mois.Operations.Elements)
        {
            IsLiveSorting = true,
            IsLiveGrouping = true,
            IsLiveFiltering = true,
            Filter = o => o is not OperationViewModel op || disposition.Montre(op.Pointee),
        };
        vue.LiveFilteringProperties.Add(nameof(OperationViewModel.Pointee));
        vue.LiveSortingProperties.Add(nameof(OperationViewModel.OrdreCategorie));
        vue.LiveSortingProperties.Add(nameof(OperationViewModel.Position));
        vue.LiveGroupingProperties.Add(nameof(OperationViewModel.GroupeCategorie));
        if (_mois.RangerParCategorie && _mois.ACategories)
        {
            vue.SortDescriptions.Add(new SortDescription(nameof(OperationViewModel.OrdreCategorie), ListSortDirection.Ascending));
            vue.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OperationViewModel.GroupeCategorie)));
        }
        vue.SortDescriptions.Add(new SortDescription(nameof(OperationViewModel.Position), ListSortDirection.Ascending));
        grille.ItemsSource = vue;
        AfficherColonnes(grille);
    }

    /// <summary>Colonnes affichées selon les choix du panneau « Personnaliser ».</summary>
    private void AfficherColonnes(DataGrid grille)
    {
        if (_mois is null || _disposition is null)
            return;
        var d = _disposition;
        foreach (var colonne in grille.Columns)
        {
            var affichee = (colonne.Header as string) switch
            {
                "Catégorie" => d.AfficherCategorie && _mois.ACategories,
                "Enveloppe" => d.AfficherEnveloppe,
                "Compte cumulé" => d.AfficherCompteCumul,
                "Virement avec" => d.AfficherVirement && _mois.Comptes.Disponibles,
                "Montant" => !d.DebitCreditSepares,
                "Débit" or "Crédit" => d.DebitCreditSepares,
                "Solde" => d.AfficherSolde,
                _ => true,
            };
            colonne.Visibility = affichee ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>La molette fait défiler tout l'écran, même au-dessus d'un tableau.</summary>
    private void Grille_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        Defilement.ScrollToVerticalOffset(Defilement.VerticalOffset - e.Delta / 3.0);
    }

    // ----- Glisser-déposer des modules -----

    private void Poignee_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement poignee && poignee.DataContext is ModuleMois module)
        {
            e.Handled = true;
            DragDrop.DoDragDrop(poignee, new DataObject(typeof(ModuleMois), module), DragDropEffects.Move);
        }
    }

    private void Module_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ModuleMois)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Lâché sur un module : le module déplacé se place au-dessus ou en dessous, selon la moitié visée.</summary>
    private void Module_Drop(object sender, DragEventArgs e)
    {
        if (_disposition is not { } d || sender is not FrameworkElement cible || cible.DataContext is not ModuleMois dessous
            || e.Data.GetData(typeof(ModuleMois)) is not ModuleMois deplace)
            return;
        e.Handled = true;
        if (deplace == dessous)
            return;
        var aGauche = d.Gauche.Contains(dessous);
        var position = (aGauche ? d.Gauche : d.Droite).IndexOf(dessous);
        if (e.GetPosition(cible).Y > cible.ActualHeight / 2)
            position++;
        d.Deplacer(deplace.Id, aGauche, position);
    }

    /// <summary>Lâché dans une colonne, hors d'un module : le module va en bas de cette colonne.</summary>
    private void Colonne_Drop(object sender, DragEventArgs e)
    {
        if (_disposition is not { } d || sender is not FrameworkElement colonne || e.Data.GetData(typeof(ModuleMois)) is not ModuleMois deplace)
            return;
        e.Handled = true;
        d.Deplacer(deplace.Id, Equals(colonne.Tag, "gauche"), int.MaxValue);
    }
}
