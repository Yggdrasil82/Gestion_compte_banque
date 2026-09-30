using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GestionCompte.Core.Lettres;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GestionCompte.Data.Lettres;

/// <summary>Mise en page d'une lettre : expéditeur à gauche, destinataire à droite, lieu et date, objet, corps, signature.</summary>
public static class ExportLettre
{
    public static void ExporterPdf(Lettre lettre, Coordonnees coordonnees, string chemin)
    {
        ArgumentNullException.ThrowIfNull(lettre);
        ArgumentNullException.ThrowIfNull(coordonnees);
        QuestPDF.Settings.License = LicenseType.Community;

        QuestPDF.Fluent.Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(60);
            page.MarginVertical(50);
            page.DefaultTextStyle(style => style.FontSize(11).LineHeight(1.25f));

            page.Content().Column(contenu =>
            {
                contenu.Spacing(4);
                foreach (var ligne in coordonnees.Lignes())
                    contenu.Item().Text(ligne);

                if (!string.IsNullOrWhiteSpace(lettre.Destinataire))
                    contenu.Item().PaddingTop(18).AlignRight().Width(230).Column(destinataire =>
                    {
                        foreach (var ligne in Lignes(lettre.Destinataire))
                            destinataire.Item().Text(ligne);
                    });

                contenu.Item().PaddingTop(24).AlignRight().Text(lettre.LieuDate(coordonnees));
                contenu.Item().PaddingTop(20).Text(texte =>
                {
                    texte.Span("Objet : ").Bold();
                    texte.Span(lettre.Objet).Bold();
                });
                contenu.Item().PaddingTop(16).Column(corps =>
                {
                    corps.Spacing(8);
                    foreach (var paragraphe in Paragraphes(lettre.Corps))
                        corps.Item().Text(paragraphe);
                });
                if (!string.IsNullOrWhiteSpace(coordonnees.Nom))
                    contenu.Item().PaddingTop(28).AlignRight().PaddingRight(40).Text(coordonnees.Nom.Trim());
            });
        })).GeneratePdf(chemin);
    }

    public static void ExporterWord(Lettre lettre, Coordonnees coordonnees, string chemin)
    {
        ArgumentNullException.ThrowIfNull(lettre);
        ArgumentNullException.ThrowIfNull(coordonnees);
        using var document = WordprocessingDocument.Create(chemin, WordprocessingDocumentType.Document);
        var principal = document.AddMainDocumentPart();
        var corps = new Body();

        foreach (var ligne in coordonnees.Lignes())
            corps.Append(Paragraphe(ligne));
        if (!string.IsNullOrWhiteSpace(lettre.Destinataire))
        {
            corps.Append(Paragraphe(""));
            foreach (var ligne in Lignes(lettre.Destinataire))
                corps.Append(Paragraphe(ligne, retraitGauche: 5100));
        }
        corps.Append(Paragraphe(""));
        corps.Append(Paragraphe(lettre.LieuDate(coordonnees), aDroite: true));
        corps.Append(Paragraphe(""));
        corps.Append(Paragraphe($"Objet : {lettre.Objet}", gras: true));
        corps.Append(Paragraphe(""));
        foreach (var paragraphe in Paragraphes(lettre.Corps))
            corps.Append(Paragraphe(paragraphe, espaceApres: 160));
        if (!string.IsNullOrWhiteSpace(coordonnees.Nom))
        {
            corps.Append(Paragraphe(""));
            corps.Append(Paragraphe(coordonnees.Nom.Trim(), retraitGauche: 5100));
        }
        corps.Append(new SectionProperties(
            new DocumentFormat.OpenXml.Wordprocessing.PageSize { Width = 11906, Height = 16838 },
            new PageMargin { Top = 1134, Bottom = 1134, Left = 1276, Right = 1276, Header = 709, Footer = 709, Gutter = 0 }));

        principal.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(corps);
        principal.Document.Save();
    }

    private static Paragraph Paragraphe(string texte, bool gras = false, bool aDroite = false, int retraitGauche = 0, int espaceApres = 0)
    {
        var proprietes = new ParagraphProperties(new SpacingBetweenLines { After = espaceApres.ToString(), Before = "0" });
        if (aDroite)
            proprietes.Append(new Justification { Val = JustificationValues.Right });
        if (retraitGauche > 0)
            proprietes.Append(new Indentation { Left = retraitGauche.ToString() });
        var style = new RunProperties(new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" }, new FontSize { Val = "22" });
        if (gras)
            style.Append(new Bold());
        return new Paragraph(proprietes, new Run(style, new Text(texte) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static IEnumerable<string> Lignes(string texte) =>
        texte.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Paragraphes séparés par une ligne vide ; les retours à la ligne simples sont gardés dans le paragraphe.</summary>
    public static IEnumerable<string> Paragraphes(string corps) =>
        corps.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => string.Join(" ", p.Split('\n', StringSplitOptions.TrimEntries)).Trim())
            .Where(p => p.Length > 0);
}

/// <summary>Coordonnées et lettres enregistrées (lettres.json, sur ce PC).</summary>
public sealed class FichierLettres
{
    public const string NomFichier = "lettres.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _chemin;

    public FichierLettres(string dossier) => _chemin = Path.Combine(dossier, NomFichier);

    public Coordonnees Coordonnees { get; private set; } = new();
    public List<Lettre> Lettres { get; private set; } = new();

    public void Charger()
    {
        if (!File.Exists(_chemin))
            return;
        var contenu = JsonSerializer.Deserialize<Contenu>(File.ReadAllText(_chemin), Options)
            ?? throw new InvalidDataException("Le fichier des lettres est illisible.");
        Coordonnees = contenu.Coordonnees ?? new Coordonnees();
        Lettres = contenu.Lettres ?? new List<Lettre>();
    }

    public void Enregistrer()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_chemin)!);
        var temporaire = _chemin + ".tmp";
        File.WriteAllText(temporaire, JsonSerializer.Serialize(new Contenu(1, Coordonnees, Lettres), Options));
        File.Move(temporaire, _chemin, true);
    }

    private sealed record Contenu(int Version, Coordonnees? Coordonnees, List<Lettre>? Lettres);
}
