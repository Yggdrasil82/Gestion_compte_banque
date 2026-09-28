using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GestionCompte.Presentation;

namespace GestionCompte.App;

/// <summary>
/// Couleurs des 5 apparences (Océan et Pastel en clair ou sombre, Nuit).
/// Les pinceaux sont placés dans les ressources de l'application ; les écrans les utilisent
/// en DynamicResource, donc un changement d'apparence s'applique immédiatement.
/// </summary>
internal static class Themes
{
    public static void Appliquer(Application application, Ambiance ambiance, bool sombre)
    {
        var palette = (ambiance, sombre || ambiance == Ambiance.Nuit) switch
        {
            (Ambiance.Ocean, false) => OceanClair,
            (Ambiance.Ocean, true) => OceanSombre,
            (Ambiance.Pastel, false) => PastelClair,
            (Ambiance.Pastel, true) => PastelSombre,
            _ => Nuit,
        };

        foreach (var (cle, pinceau) in palette.Pinceaux())
            application.Resources[cle] = pinceau;

        foreach (Window fenetre in application.Windows)
            BarreDeTitreSombre(fenetre, palette.Sombre);
    }

    /// <summary>Barre de titre Windows sombre ou claire (Windows 10 20H1 et plus récent ; ignoré ailleurs).</summary>
    public static void BarreDeTitreSombre(Window fenetre, bool sombre)
    {
        var handle = new WindowInteropHelper(fenetre).Handle;
        if (handle == IntPtr.Zero)
            return;

        var valeur = sombre ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref valeur, sizeof(int));
    }

    public static bool EstSombre(Ambiance ambiance, bool modeSombre) => ambiance == Ambiance.Nuit || modeSombre;

    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribut, ref int valeur, int taille);

    private sealed record Palette(
        bool Sombre,
        string Fond, string Carte, string Texte, string TexteDiscret, string Bordure, string LigneAlternee,
        string Saisie, string Selection,
        (string Haut, string Bas) Barre, string BarreTexte, string BarreActif,
        string[] Entete, string EnteteTexte, string EnteteBouton,
        string Accent1, string Accent2, string Accent3, string Accent4,
        string Accent1Clair, string Accent2Clair, string Accent3Clair, string Accent4Clair,
        string Negatif, string Avertissement)
    {
        public IEnumerable<(string Cle, Brush Pinceau)> Pinceaux()
        {
            yield return ("Fond", Uni(Fond));
            yield return ("Carte", Uni(Carte));
            yield return ("Texte", Uni(Texte));
            yield return ("TexteDiscret", Uni(TexteDiscret));
            yield return ("Bordure", Uni(Bordure));
            yield return ("LigneAlternee", Uni(LigneAlternee));
            yield return ("Saisie", Uni(Saisie));
            yield return ("Selection", Uni(Selection));
            yield return ("Barre", Degrade(new[] { Barre.Haut, Barre.Bas }, vertical: true));
            yield return ("BarreTexte", Uni(BarreTexte));
            yield return ("BarreActif", Uni(BarreActif));
            yield return ("Entete", Degrade(Entete, vertical: false));
            yield return ("EnteteTexte", Uni(EnteteTexte));
            yield return ("EnteteBouton", Uni(EnteteBouton));
            yield return ("Accent1", Uni(Accent1));
            yield return ("Accent2", Uni(Accent2));
            yield return ("Accent3", Uni(Accent3));
            yield return ("Accent4", Uni(Accent4));
            yield return ("Accent1Clair", Uni(Accent1Clair));
            yield return ("Accent2Clair", Uni(Accent2Clair));
            yield return ("Accent3Clair", Uni(Accent3Clair));
            yield return ("Accent4Clair", Uni(Accent4Clair));
            yield return ("Positif", Uni(Accent2));
            yield return ("Negatif", Uni(Negatif));
            yield return ("Avertissement", Uni(Avertissement));
            // Anciennes clés, gardées pour les écrans qui les utilisent encore.
            yield return ("Accent", Uni(Accent1));
            yield return ("AccentClair", Uni(Accent1Clair));
        }

        private static Brush Uni(string couleur)
        {
            var pinceau = new SolidColorBrush((Color)ColorConverter.ConvertFromString(couleur));
            pinceau.Freeze();
            return pinceau;
        }

        private static Brush Degrade(string[] couleurs, bool vertical)
        {
            var pinceau = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = vertical ? new Point(0, 1) : new Point(1, 0),
            };
            for (var i = 0; i < couleurs.Length; i++)
                pinceau.GradientStops.Add(new GradientStop(
                    (Color)ColorConverter.ConvertFromString(couleurs[i]), couleurs.Length == 1 ? 0 : (double)i / (couleurs.Length - 1)));
            pinceau.Freeze();
            return pinceau;
        }
    }

    private static readonly Palette OceanClair = new(
        Sombre: false,
        Fond: "#F3F6FB", Carte: "#FFFFFF", Texte: "#1D2433", TexteDiscret: "#6B7489", Bordure: "#E3E8F0", LigneAlternee: "#F8FAFD",
        Saisie: "#FFFFFF", Selection: "#DCEBFB",
        Barre: ("#123A6B", "#0E6E8C"), BarreTexte: "#DCE9F7", BarreActif: "#29FFFFFF",
        Entete: new[] { "#1565C0", "#00A3A3" }, EnteteTexte: "#FFFFFF", EnteteBouton: "#38FFFFFF",
        Accent1: "#1E88E5", Accent2: "#2EAD6B", Accent3: "#F2724B", Accent4: "#8E5CD9",
        Accent1Clair: "#E6F1FC", Accent2Clair: "#E5F6EC", Accent3Clair: "#FDEDE7", Accent4Clair: "#F1EAFB",
        Negatif: "#D93A3A", Avertissement: "#F5A524");

    private static readonly Palette OceanSombre = new(
        Sombre: true,
        Fond: "#0F1722", Carte: "#172233", Texte: "#E6EDF7", TexteDiscret: "#8FA0B8", Bordure: "#26344A", LigneAlternee: "#1B2839",
        Saisie: "#1F2D42", Selection: "#23405F",
        Barre: ("#0A1220", "#0B3A4A"), BarreTexte: "#BFD3EA", BarreActif: "#22FFFFFF",
        Entete: new[] { "#1565C0", "#00A3A3" }, EnteteTexte: "#FFFFFF", EnteteBouton: "#38FFFFFF",
        Accent1: "#42A5F5", Accent2: "#3CCB85", Accent3: "#FF8A65", Accent4: "#B388FF",
        Accent1Clair: "#2642A5F5", Accent2Clair: "#263CCB85", Accent3Clair: "#26FF8A65", Accent4Clair: "#26B388FF",
        Negatif: "#FF6B6B", Avertissement: "#FFB74D");

    private static readonly Palette PastelClair = new(
        Sombre: false,
        Fond: "#FBF7F2", Carte: "#FFFFFF", Texte: "#2D2A32", TexteDiscret: "#857E8C", Bordure: "#EFE7DE", LigneAlternee: "#FDFBF8",
        Saisie: "#FFFFFF", Selection: "#FFEBDD",
        Barre: ("#FFFFFF", "#FFFFFF"), BarreTexte: "#5A5363", BarreActif: "#FFE9D6",
        Entete: new[] { "#FFB88C", "#F59FB8", "#B69CF2" }, EnteteTexte: "#3A2640", EnteteBouton: "#55FFFFFF",
        Accent1: "#5B8DEF", Accent2: "#3DBE8B", Accent3: "#F28C6B", Accent4: "#B07CE8",
        Accent1Clair: "#EAF1FE", Accent2Clair: "#E4F7EE", Accent3Clair: "#FDEEE8", Accent4Clair: "#F4ECFD",
        Negatif: "#E0525E", Avertissement: "#F2B04B");

    private static readonly Palette PastelSombre = new(
        Sombre: true,
        Fond: "#1E1A22", Carte: "#2A2430", Texte: "#F3EAF2", TexteDiscret: "#A99BAE", Bordure: "#3B3342", LigneAlternee: "#302936",
        Saisie: "#342C3A", Selection: "#4A3A52",
        Barre: ("#241F29", "#241F29"), BarreTexte: "#D9CCE0", BarreActif: "#3D2F45",
        Entete: new[] { "#FFB88C", "#F59FB8", "#B69CF2" }, EnteteTexte: "#3A2640", EnteteBouton: "#55FFFFFF",
        Accent1: "#7FA7F5", Accent2: "#5ED3A2", Accent3: "#F7A487", Accent4: "#C69BF0",
        Accent1Clair: "#2E7FA7F5", Accent2Clair: "#2E5ED3A2", Accent3Clair: "#2EF7A487", Accent4Clair: "#2EC69BF0",
        Negatif: "#FF7A85", Avertissement: "#F7C46C");

    private static readonly Palette Nuit = new(
        Sombre: true,
        Fond: "#12151F", Carte: "#1B2030", Texte: "#E8ECF5", TexteDiscret: "#8F99B0", Bordure: "#2A3145", LigneAlternee: "#1F2536",
        Saisie: "#232A3D", Selection: "#2E3650",
        Barre: ("#0C0F17", "#0C0F17"), BarreTexte: "#AEB8CE", BarreActif: "#232A3D",
        Entete: new[] { "#6D28D9", "#DB2777" }, EnteteTexte: "#FFFFFF", EnteteBouton: "#38FFFFFF",
        Accent1: "#38BDF8", Accent2: "#34D399", Accent3: "#FB7185", Accent4: "#C084FC",
        Accent1Clair: "#2438BDF8", Accent2Clair: "#2434D399", Accent3Clair: "#24FB7185", Accent4Clair: "#24C084FC",
        Negatif: "#FB7185", Avertissement: "#FBBF24");
}
