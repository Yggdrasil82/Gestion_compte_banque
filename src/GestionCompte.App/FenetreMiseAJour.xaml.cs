using System.Windows;
using System.Windows.Documents;

namespace GestionCompte.App;

/// <summary>Annonce d'une nouvelle version, avec ses nouveautés.</summary>
public partial class FenetreMiseAJour : Window
{
    public FenetreMiseAJour(string nouvelle, string actuelle, string nouveautes)
    {
        InitializeComponent();
        Titre.Text = $"Version {nouvelle} disponible";
        Actuelle.Text = $"Vous avez la version {actuelle}.";
        // Titres « Version x.y.z » en gras, le reste tel quel.
        foreach (var ligne in nouveautes.Replace("\r", "").Split('\n'))
        {
            var texte = ligne.Replace("**", "").TrimStart('-', '*', ' ');
            if (Nouveautes.Inlines.Count > 0)
                Nouveautes.Inlines.Add(new LineBreak());
            if (ligne.StartsWith("Version ", StringComparison.Ordinal))
                Nouveautes.Inlines.Add(new Bold(new Run(ligne)));
            else if (texte.Length > 0)
                Nouveautes.Inlines.Add(new Run((ligne.TrimStart().StartsWith('-') || ligne.TrimStart().StartsWith('*') ? "• " : "") + texte));
        }
    }

    private void Installer(object sender, RoutedEventArgs e) => DialogResult = true;
}
