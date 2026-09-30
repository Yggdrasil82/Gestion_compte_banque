using System.Windows;
using GestionCompte.Core.Achats;

namespace GestionCompte.App;

/// <summary>Ajout ou modification d'un site de la liste de recherche.</summary>
public partial class FenetreSite : Window
{
    public FenetreSite(SiteRecherche? actuel)
    {
        InitializeComponent();
        Title = actuel is null ? "Ajouter un site" : "Modifier le site";
        Nom.Text = actuel?.Nom ?? "";
        Adresse.Text = actuel?.Adresse ?? "https://";
        Loaded += (_, _) => Nom.Focus();
    }

    public SiteRecherche? Site { get; private set; }

    private void Valider(object sender, RoutedEventArgs e)
    {
        var adresse = Adresse.Text.Trim();
        string? probleme = null;
        if (Nom.Text.Trim().Length == 0)
            probleme = "Indiquez le nom du site.";
        else if (!adresse.Contains(SiteRecherche.Marqueur))
            probleme = $"L'adresse doit contenir {SiteRecherche.Marqueur} à la place du mot cherché.";
        else if (!Uri.TryCreate(adresse.Replace(SiteRecherche.Marqueur, "test"), UriKind.Absolute, out var uri)
                 || uri.Scheme is not ("http" or "https"))
            probleme = "L'adresse doit commencer par https://.";

        if (probleme is not null)
        {
            Probleme.Text = probleme;
            Probleme.Visibility = Visibility.Visible;
            return;
        }
        Site = new SiteRecherche { Nom = Nom.Text.Trim(), Adresse = adresse };
        DialogResult = true;
    }
}
