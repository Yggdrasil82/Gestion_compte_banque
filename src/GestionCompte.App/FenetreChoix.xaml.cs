using System.Windows;

namespace GestionCompte.App;

/// <summary>Liste à cocher (ex. documents du coffre à joindre à un mail).</summary>
public partial class FenetreChoix : Window
{
    private readonly List<Element> _elements;

    public FenetreChoix(string titre, string message, IReadOnlyList<string> elements)
    {
        InitializeComponent();
        Title = titre;
        Message.Text = message;
        _elements = elements.Select(e => new Element { Texte = e }).ToList();
        Liste.ItemsSource = _elements;
    }

    public IReadOnlyList<int> Choix => _elements.Select((e, i) => (e, i)).Where(x => x.e.Coche).Select(x => x.i).ToList();

    private void Valider(object sender, RoutedEventArgs e) => DialogResult = true;

    public sealed class Element
    {
        public string Texte { get; init; } = "";
        public bool Coche { get; set; }
    }
}
