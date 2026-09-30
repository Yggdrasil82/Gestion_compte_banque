using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
using GestionCompte.Presentation;

namespace GestionCompte.App.Vues;

public partial class VueAssistant : UserControl
{
    public VueAssistant()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is AssistantViewModel ancien)
                ancien.Conversation.CollectionChanged -= NouveauMessage;
            if (e.NewValue is AssistantViewModel vm)
                vm.Conversation.CollectionChanged += NouveauMessage;
        };
    }

    /// <summary>Descend en bas de la conversation à chaque nouveau message.</summary>
    private void NouveauMessage(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.InvokeAsync(() => Defilement.ScrollToEnd());

    /// <summary>Entrée : envoie la question ; Maj+Entrée : nouvelle ligne.</summary>
    private void QuestionTouche(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || DataContext is not AssistantViewModel vm)
            return;
        if (vm.EnvoyerCommand.CanExecute(null))
            vm.EnvoyerCommand.Execute(null);
        e.Handled = true;
    }
}
