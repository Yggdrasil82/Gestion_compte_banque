using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GestionCompte.Presentation;

/// <summary>Liste affichée dans un tableau, avec les boutons Ajouter / Supprimer / Monter / Descendre.</summary>
public sealed partial class ListeEditable<T> : ObservableObject where T : class
{
    private readonly Func<T> _creer;
    private readonly Action _structureModifiee;

    public ListeEditable(IEnumerable<T> elements, Func<T> creer, Action structureModifiee)
    {
        Elements = new ObservableCollection<T>(elements);
        _creer = creer;
        _structureModifiee = structureModifiee;
    }

    public ObservableCollection<T> Elements { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SupprimerCommand), nameof(MonterCommand), nameof(DescendreCommand))]
    private T? _selection;

    [RelayCommand]
    private void Ajouter()
    {
        var element = _creer();
        var position = Selection is null ? Elements.Count : Elements.IndexOf(Selection) + 1;
        Elements.Insert(position, element);
        Selection = element;
        _structureModifiee();
    }

    [RelayCommand(CanExecute = nameof(ASelection))]
    private void Supprimer()
    {
        var position = Elements.IndexOf(Selection!);
        Elements.RemoveAt(position);
        Selection = Elements.Count == 0 ? null : Elements[Math.Min(position, Elements.Count - 1)];
        _structureModifiee();
    }

    [RelayCommand(CanExecute = nameof(PeutMonter))]
    private void Monter() => Deplacer(-1);

    [RelayCommand(CanExecute = nameof(PeutDescendre))]
    private void Descendre() => Deplacer(+1);

    private bool ASelection() => Selection is not null;

    private bool PeutMonter() => Selection is not null && Elements.IndexOf(Selection) > 0;

    private bool PeutDescendre() => Selection is not null && Elements.IndexOf(Selection) < Elements.Count - 1;

    private void Deplacer(int decalage)
    {
        var position = Elements.IndexOf(Selection!);
        Elements.Move(position, position + decalage);
        MonterCommand.NotifyCanExecuteChanged();
        DescendreCommand.NotifyCanExecuteChanged();
        _structureModifiee();
    }
}
