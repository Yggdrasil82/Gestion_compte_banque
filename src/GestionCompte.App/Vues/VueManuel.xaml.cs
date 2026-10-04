using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GestionCompte.Presentation;

namespace GestionCompte.App.Vues;

/// <summary>Rubrique « Aide » : affiche le chapitre choisi du manuel, avec ses captures, aux couleurs du thème.</summary>
public partial class VueManuel : UserControl
{
    private ManuelViewModel? _manuel;

    public VueManuel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_manuel is not null)
                _manuel.PropertyChanged -= ManuelModifie;
            _manuel = DataContext as ManuelViewModel;
            if (_manuel is not null)
                _manuel.PropertyChanged += ManuelModifie;
            Afficher();
        };
    }

    private void ManuelModifie(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ManuelViewModel.Selection) or nameof(ManuelViewModel.Recherche))
            Afficher();
    }

    private void Afficher()
    {
        if (_manuel?.Selection is not { } chapitre)
        {
            Lecteur.Document = null;
            return;
        }

        var document = new FlowDocument
        {
            PagePadding = new Thickness(18, 10, 18, 18),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            LineHeight = 21,
            TextAlignment = TextAlignment.Left,
        };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "Texte");
        document.SetResourceReference(FlowDocument.BackgroundProperty, "FondCarte");

        var titre = new Paragraph(new Run(chapitre.Titre)) { FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) };
        titre.SetResourceReference(TextElement.ForegroundProperty, "Accent1");
        document.Blocks.Add(titre);

        var mots = _manuel.MotsCherches;
        foreach (var bloc in chapitre.Blocs)
            if (Bloc(bloc, mots) is { } element)
                document.Blocks.Add(element);

        Lecteur.Document = document;
        Lecteur.Defilement()?.ScrollToTop();
    }

    private static Block? Bloc(BlocManuel bloc, IReadOnlyList<string> mots)
    {
        switch (bloc)
        {
            case TitreManuel t:
                return new Paragraph(new Run(t.Texte)) { FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) };

            case ParagrapheManuel p:
                return Paragraphe(p.Segments, mots);

            case ListeManuel l:
                var liste = new System.Windows.Documents.List
                {
                    MarkerStyle = l.Numerotee ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Margin = new Thickness(0, 0, 0, 10),
                    Padding = new Thickness(22, 0, 0, 0),
                };
                foreach (var element in l.Elements)
                {
                    var paragraphe = Paragraphe(element, mots);
                    paragraphe.Margin = new Thickness(0, 0, 0, 5);
                    liste.ListItems.Add(new ListItem(paragraphe));
                }
                return liste;

            case TableauManuel t:
                var tableau = new Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 12) };
                foreach (var largeur in t.Largeurs)
                    tableau.Columns.Add(new TableColumn
                    {
                        Width = largeur is { } pourcentage ? new GridLength(pourcentage, GridUnitType.Star)
                            : new GridLength(Math.Max(1, 100 - t.Largeurs.Sum(x => x ?? 0)) / Math.Max(1, t.Largeurs.Count(x => x is null)), GridUnitType.Star),
                    });
                var groupe = new TableRowGroup();
                foreach (var ligne in t.Lignes)
                {
                    var rangee = new TableRow();
                    foreach (var cellule in ligne.Cellules)
                    {
                        var paragraphe = Paragraphe(cellule, mots);
                        if (ligne.Entete)
                        {
                            paragraphe.FontSize = 12;
                            paragraphe.SetResourceReference(TextElement.ForegroundProperty, "TexteDiscret");
                        }
                        var case_ = new TableCell(paragraphe) { Padding = new Thickness(6, 5, 8, 5), BorderThickness = new Thickness(0, 0, 0, 1) };
                        case_.SetResourceReference(TableCell.BorderBrushProperty, "Bordure");
                        rangee.Cells.Add(case_);
                    }
                    groupe.Rows.Add(rangee);
                }
                tableau.RowGroups.Add(groupe);
                return tableau;

            case ImageManuel i:
                using (var flux = Manuel.Image(i.Fichier))
                {
                    if (flux is null)
                        return null;
                    var source = new BitmapImage();
                    source.BeginInit();
                    source.CacheOption = BitmapCacheOption.OnLoad;
                    source.StreamSource = flux;
                    source.EndInit();
                    source.Freeze();

                    var cadre = new StackPanel { Margin = new Thickness(0, 6, 0, 12) };
                    var image = new Image { Source = source, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
                    // Jamais plus grande que la capture, et réduite comme dans le manuel PDF.
                    image.MaxWidth = source.PixelWidth * i.Largeur;
                    var bord = new Border { Child = image, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Center };
                    bord.SetResourceReference(Border.BorderBrushProperty, "Bordure");
                    cadre.Children.Add(bord);
                    if (!string.IsNullOrWhiteSpace(i.Legende))
                    {
                        var legende = new TextBlock { Text = i.Legende, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 6, 0, 0) };
                        legende.SetResourceReference(TextBlock.ForegroundProperty, "TexteDiscret");
                        cadre.Children.Add(legende);
                    }
                    return new BlockUIContainer(cadre);
                }

            case EncartManuel e:
                var encart = new Section { Padding = new Thickness(14, 10, 14, 4), Margin = new Thickness(0, 6, 0, 12), BorderThickness = new Thickness(5, 0, 0, 0) };
                encart.SetResourceReference(Section.BackgroundProperty, "LigneAlternee");
                encart.SetResourceReference(Section.BorderBrushProperty, e.Attention ? "Avertissement" : "Accent1");
                foreach (var b in e.Blocs)
                    if (Bloc(b, mots) is { } element)
                        encart.Blocks.Add(element);
                return encart;

            default:
                return null;
        }
    }

    private static Paragraph Paragraphe(IReadOnlyList<SegmentManuel> segments, IReadOnlyList<string> mots)
    {
        var paragraphe = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var segment in segments)
            foreach (var run in Surligner(segment.Texte, mots))
            {
                if (segment.Gras)
                    run.FontWeight = FontWeights.SemiBold;
                if (segment.Touche)
                {
                    run.FontFamily = new FontFamily("Consolas");
                    run.SetResourceReference(TextElement.BackgroundProperty, "LigneAlternee");
                }
                paragraphe.Inlines.Add(run);
            }
        return paragraphe;
    }

    /// <summary>Morceaux de texte, ceux qui correspondent à la recherche surlignés (sans tenir compte des accents).</summary>
    private static IEnumerable<Run> Surligner(string texte, IReadOnlyList<string> mots)
    {
        if (mots.Count == 0)
        {
            yield return new Run(texte);
            yield break;
        }

        // La normalisation garde la longueur du texte pour les lettres accentuées du français (é → e).
        var normalise = ManuelViewModel.Normaliser(texte);
        if (normalise.Length != texte.Length)
        {
            yield return new Run(texte);
            yield break;
        }

        var motif = string.Join("|", mots.Select(Regex.Escape));
        var position = 0;
        foreach (Match m in Regex.Matches(normalise, motif))
        {
            if (m.Index > position)
                yield return new Run(texte[position..m.Index]);
            var trouve = new Run(texte.Substring(m.Index, m.Length)) { FontWeight = FontWeights.Bold };
            trouve.SetResourceReference(TextElement.BackgroundProperty, "Accent3Clair");
            yield return trouve;
            position = m.Index + m.Length;
        }
        if (position < texte.Length)
            yield return new Run(texte[position..]);
    }
}

internal static class ExtensionsLecteur
{
    /// <summary>Barre de défilement interne du lecteur (pour revenir en haut à chaque chapitre).</summary>
    public static ScrollViewer? Defilement(this FlowDocumentScrollViewer lecteur)
    {
        lecteur.ApplyTemplate();
        return lecteur.Template?.FindName("PART_ContentHost", lecteur) as ScrollViewer;
    }
}
