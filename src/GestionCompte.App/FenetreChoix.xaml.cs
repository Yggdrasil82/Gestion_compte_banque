using System.Windows;

namespace GestionCompte.App;

/// <summary>Liste à cocher (ex. documents du coffre à joindre à un mail, montants remplacés par la configuration).</summary>
public partial class FenetreChoix : Window
{
    private readonly List<Element> _elements;

    public FenetreChoix(string titre, string message, IReadOnlyList<string> elements, bool coches = false, string information = "",
        string bouton = "OK")
    {
        InitializeComponent();
        Title = titre;
        Message.Text = message;
        Information.Text = information;
        Information.Visibility = information.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        BoutonValider.Content = bouton;
        _elements = elements.Select(e => new Element { Texte = e, Coche = coches }).ToList();
        Liste.ItemsSource = _elements;
    }

    /// <summary>État de chaque case, dans l'ordre de la liste.</summary>
    public IReadOnlyList<bool> Cases => _elements.Select(e => e.Coche).ToList();

    public IReadOnlyList<int> Choix => _elements.Select((e, i) => (e, i)).Where(x => x.e.Coche).Select(x => x.i).ToList();

    private void Valider(object sender, RoutedEventArgs e) => DialogResult = true;

    public sealed class Element
    {
        public string Texte { get; init; } = "";
        public bool Coche { get; set; }
    }
}
