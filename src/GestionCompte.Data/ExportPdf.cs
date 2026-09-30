using System.Globalization;
using System.Text;
using GestionCompte.Core;
using GestionCompte.Core.Calculs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GestionCompte.Data;

/// <summary>Export du bilan en PDF (A4) : résumé, graphique mois par mois, postes de dépenses et pistes d'économie.</summary>
public static class ExportPdf
{
    private const string Bleu = "#1565C0";
    private const string BleuClair = "#E6F1FC";
    private const string Vert = "#2E9D5B";
    private const string Orange = "#E8742C";
    private const string Violet = "#7E57C2";
    private const string Rouge = "#D93A3A";
    private const string Gris = "#6B7280";

    public static void ExporterBilan(ResultatBilan bilan, string titre, string chemin)
    {
        ArgumentNullException.ThrowIfNull(bilan);
        // Licence gratuite de QuestPDF pour les particuliers et petites structures.
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(style => style.FontSize(9.5f));

            page.Header().PaddingBottom(10).Column(entete =>
            {
                entete.Item().Text(titre).FontSize(20).Bold().FontColor(Bleu);
                entete.Item().Text(bilan.Etendue).FontColor(Gris);
            });

            page.Content().Column(contenu =>
            {
                contenu.Spacing(12);
                contenu.Item().Row(tuiles =>
                {
                    tuiles.Spacing(8);
                    Tuile(tuiles.RelativeItem(), "Revenus", Euros(bilan.Revenus), Vert);
                    Tuile(tuiles.RelativeItem(), "Dépenses", Euros(bilan.Depenses), Orange);
                    Tuile(tuiles.RelativeItem(), "Épargne", Euros(bilan.Epargne), Violet,
                        bilan.TauxEpargne is { } taux ? $"{Pourcentage(taux)} des revenus" : null);
                    Tuile(tuiles.RelativeItem(), "Solde", Euros(bilan.SoldeFin), Bleu,
                        $"{(bilan.Variation >= 0 ? "+" : "")}{Euros(bilan.Variation)} depuis le début ({Euros(bilan.SoldeDebut)})");
                });

                if (bilan.Mois.Count > 0)
                {
                    contenu.Item().Text("Mois par mois").FontSize(13).Bold().FontColor(Bleu);
                    contenu.Item().Height(170).Svg(Graphique(bilan)).FitArea();
                    contenu.Item().Row(legende =>
                    {
                        legende.Spacing(4);
                        foreach (var (nom, couleur) in new[] { ("Revenus", Vert), ("Dépenses", Orange), ("Épargne", Violet) })
                        {
                            legende.ConstantItem(8).AlignMiddle().Height(8).Background(couleur);
                            legende.AutoItem().PaddingRight(10).Text(nom).FontColor(Gris);
                        }
                    });
                }

                contenu.Item().Text("Postes de dépenses").FontSize(13).Bold().FontColor(Bleu);
                contenu.Item().Table(table =>
                {
                    table.ColumnsDefinition(colonnes =>
                    {
                        colonnes.RelativeColumn(3.2f);
                        colonnes.RelativeColumn(1.4f);
                        colonnes.RelativeColumn(1.4f);
                        colonnes.RelativeColumn(1.4f);
                        colonnes.RelativeColumn(1.1f);
                        colonnes.RelativeColumn(1.1f);
                    });
                    table.Header(entete =>
                    {
                        foreach (var nom in new[] { "Poste", "Type", "Total", "Par mois", "% revenus", "Évolution" })
                            entete.Cell().Background(BleuClair).Padding(4).Text(nom).Bold();
                    });
                    foreach (var poste in bilan.Postes)
                    {
                        Cellule(table).Text(poste.Nom);
                        Cellule(table).Text(Bilan.NomType(poste.Type)).FontColor(Gris);
                        Cellule(table).AlignRight().Text(Euros(poste.Total));
                        Cellule(table).AlignRight().Text(Euros(poste.MoyenneMensuelle));
                        Cellule(table).AlignRight().Text(Pourcentage(poste.PartRevenus, 1));
                        var evolution = poste.Evolution is { } e ? $"{(e >= 0 ? "+" : "")}{Pourcentage(e)}" : "";
                        Cellule(table).AlignRight().Text(evolution)
                            .FontColor(CouleurEvolution(poste));
                    }
                });

                if (bilan.Pistes.Count > 0)
                {
                    contenu.Item().Text("Pistes d'économie").FontSize(13).Bold().FontColor(Bleu);
                    foreach (var piste in bilan.Pistes)
                    {
                        contenu.Item().BorderLeft(3).BorderColor(Vert).PaddingLeft(8).Column(bloc =>
                        {
                            bloc.Item().Text(texte =>
                            {
                                texte.Span(piste.Titre).Bold();
                                if (piste.GainAnnuel is { } gain)
                                    texte.Span($"   jusqu'à {Euros(gain)} par an").FontColor(Vert).Bold();
                            });
                            bloc.Item().Text(piste.Detail).FontColor(Gris);
                        });
                    }
                }
            });

            page.Footer().AlignCenter().Text(texte =>
            {
                texte.Span("Gestion compte : bilan calculé sur votre PC · page ").FontColor(Gris).FontSize(8);
                texte.CurrentPageNumber().FontColor(Gris).FontSize(8);
                texte.Span(" / ").FontColor(Gris).FontSize(8);
                texte.TotalPages().FontColor(Gris).FontSize(8);
            });
        })).GeneratePdf(chemin);
    }

    private static void Tuile(IContainer conteneur, string titre, string valeur, string couleur, string? detail = null) =>
        conteneur.BorderLeft(4).BorderColor(couleur).Background("#F7F9FC").Padding(8).Column(colonne =>
        {
            colonne.Item().Text(titre).FontColor(Gris).FontSize(8.5f);
            colonne.Item().Text(valeur).Bold().FontSize(12).FontColor(couleur);
            if (detail is not null)
                colonne.Item().Text(detail).FontColor(Gris).FontSize(8);
        });

    /// <summary>Hausse d'une dépense en rouge, baisse en vert ; pour l'épargne, c'est l'inverse.</summary>
    private static string CouleurEvolution(PosteBilan poste)
    {
        var hausse = poste.Evolution >= Bilan.SeuilHausse;
        var baisse = poste.Evolution <= -Bilan.SeuilHausse;
        if (poste.Type == TypePoste.Epargne)
            (hausse, baisse) = (baisse, hausse);
        return hausse ? Rouge : baisse ? Vert : Gris;
    }

    private static IContainer Cellule(TableDescriptor table) =>
        table.Cell().BorderBottom(0.5f).BorderColor("#E5E7EB").PaddingVertical(3).PaddingHorizontal(4);

    /// <summary>Barres des revenus et des dépenses (épargne empilée au-dessus) pour chaque mois, en SVG.</summary>
    private static string Graphique(ResultatBilan bilan)
    {
        const double largeur = 520, hauteur = 170, gauche = 52, bas = 20, haut = 6;
        var max = (double)bilan.Mois.Max(m => Math.Max(m.Revenus, m.Depenses + m.Epargne));
        var pas = PasArrondi(max / 4);
        max = Math.Max(pas, Math.Ceiling(max / pas) * pas);
        var zone = hauteur - bas - haut;
        double Y(double valeur) => haut + zone - valeur / max * zone;

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{largeur}\" height=\"{hauteur}\" viewBox=\"0 0 {largeur} {hauteur}\">");
        for (var v = 0.0; v <= max + pas / 2; v += pas)
        {
            var y = Y(v);
            svg.Append(CultureInfo.InvariantCulture, $"<line x1=\"{gauche}\" y1=\"{y:0.#}\" x2=\"{largeur}\" y2=\"{y:0.#}\" stroke=\"#E5E7EB\" stroke-width=\"0.8\"/>");
            svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{gauche - 5}\" y=\"{y + 3:0.#}\" font-size=\"8\" fill=\"{Gris}\" text-anchor=\"end\">{v.ToString("N0", Montants.Francais).Replace('\u202F', ' ')} €</text>");
        }

        var colonne = (largeur - gauche) / bilan.Mois.Count;
        var barre = Math.Min(14, colonne * 0.32);
        for (var i = 0; i < bilan.Mois.Count; i++)
        {
            var m = bilan.Mois[i];
            var x = gauche + i * colonne + colonne / 2;
            Rectangle(svg, x - barre - 1, Y((double)m.Revenus), barre, (double)m.Revenus / max * zone, Vert);
            var depenses = (double)Math.Max(0, m.Depenses) / max * zone;
            var epargne = (double)Math.Max(0, m.Epargne) / max * zone;
            Rectangle(svg, x + 1, haut + zone - depenses, barre, depenses, Orange);
            Rectangle(svg, x + 1, haut + zone - depenses - epargne, barre, epargne, Violet);
            var mois = new DateTime(m.Periode.Annee, m.Periode.Mois, 1).ToString("MMM yy", Montants.Francais);
            svg.Append(CultureInfo.InvariantCulture, $"<text x=\"{x:0.#}\" y=\"{hauteur - 5}\" font-size=\"7.5\" fill=\"{Gris}\" text-anchor=\"middle\">{mois}</text>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static void Rectangle(StringBuilder svg, double x, double y, double largeur, double hauteur, string couleur)
    {
        if (hauteur > 0)
            svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{x:0.##}\" y=\"{y:0.##}\" width=\"{largeur:0.##}\" height=\"{hauteur:0.##}\" rx=\"1.5\" fill=\"{couleur}\"/>");
    }

    /// <summary>Pas de graduation « rond » : 1, 2 ou 5 × une puissance de 10.</summary>
    private static double PasArrondi(double brut)
    {
        if (brut <= 0)
            return 100;
        var puissance = Math.Pow(10, Math.Floor(Math.Log10(brut)));
        var normalise = brut / puissance;
        return (normalise <= 1 ? 1 : normalise <= 2 ? 2 : normalise <= 5 ? 5 : 10) * puissance;
    }

    // Espace insécable classique : l'espace fine des milliers n'existe pas dans toutes les polices.
    private static string Euros(decimal montant) => $"{Montants.Formater(montant).Replace('\u202F', '\u00A0')}\u00A0€";

    private static string Pourcentage(decimal part, int decimales = 0) =>
        $"{decimal.Round(part * 100, decimales).ToString(decimales == 0 ? "0" : "0.0", Montants.Francais)} %";
}
