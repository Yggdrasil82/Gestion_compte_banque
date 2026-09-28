using ClosedXML.Excel;
using GestionCompte.Core;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Data;

/// <summary>Export d'un mois dans un classeur Excel (.xlsx) : revenus, enveloppes, opérations et résumé.</summary>
public static class ExportExcel
{
    private const string FormatEuros = "#,##0.00 \"€\";-#,##0.00 \"€\"";
    private static readonly XLColor Bleu = XLColor.FromHtml("#1565C0");
    private static readonly XLColor BleuClair = XLColor.FromHtml("#E6F1FC");
    private static readonly XLColor Rouge = XLColor.FromHtml("#D93A3A");

    public static void ExporterMois(CompteBancaire compte, PeriodeMois periode, string chemin)
    {
        ArgumentNullException.ThrowIfNull(compte);
        var mois = compte.Trouver(periode) ?? throw new KeyNotFoundException($"Le mois {periode} n'existe pas.");
        var resultat = compte.Calculer(periode);

        using var classeur = new XLWorkbook();
        var feuille = classeur.Worksheets.Add(periode.Libelle);

        feuille.Cell(1, 1).Value = periode.Libelle;
        feuille.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(18).Font.SetFontColor(Bleu);
        var ligne = 3;

        // Résumé
        ligne = Titre(feuille, ligne, "Résumé");
        foreach (var (libelle, montant) in new (string, decimal)[]
        {
            ("Ancien solde", resultat.AncienSolde),
            ("Revenus", resultat.TotalRevenus),
            ("Solde de départ", resultat.SoldeDepart),
            ("Réservé pour les enveloppes", resultat.Enveloppes.Sum(e => e.Reste)),
            ("Débits", mois.Operations.Sum(o => o.Debit)),
            ("Crédits", mois.Operations.Sum(o => o.Credit)),
            ("Solde prévisionnel de fin de mois", resultat.SoldeFinPrevisionnel),
        })
        {
            feuille.Cell(ligne, 1).Value = libelle;
            Montant(feuille.Cell(ligne, 2), montant);
            ligne++;
        }
        feuille.Range(ligne - 1, 1, ligne - 1, 2).Style.Font.SetBold().Fill.SetBackgroundColor(BleuClair);
        ligne++;

        // Revenus
        ligne = Titre(feuille, ligne, "Revenus");
        ligne = Entete(feuille, ligne, "Revenu", "Montant");
        foreach (var revenu in mois.Revenus)
        {
            feuille.Cell(ligne, 1).Value = revenu.Nom;
            Montant(feuille.Cell(ligne, 2), revenu.Montant);
            ligne++;
        }
        ligne++;

        // Enveloppes
        ligne = Titre(feuille, ligne, "Enveloppes");
        ligne = Entete(feuille, ligne, "Enveloppe", "Budget", "Dépensé", "Reste");
        foreach (var enveloppe in resultat.Enveloppes)
        {
            feuille.Cell(ligne, 1).Value = enveloppe.Nom;
            Montant(feuille.Cell(ligne, 2), enveloppe.Budget);
            Montant(feuille.Cell(ligne, 3), enveloppe.Depense);
            Montant(feuille.Cell(ligne, 4), enveloppe.Reste);
            if (enveloppe.Depassee)
                feuille.Cell(ligne, 3).Style.Font.SetFontColor(Rouge);
            ligne++;
        }
        ligne++;

        // Opérations
        ligne = Titre(feuille, ligne, "Opérations");
        ligne = Entete(feuille, ligne, "Libellé", "Débit", "Crédit", "Solde", "Pointé", "Enveloppe", "Compte cumulé");
        var soldes = resultat.Lignes.Where(l => l.Type == TypeLigne.Operation).Select(l => l.Solde).ToList();
        foreach (var (operation, solde) in mois.Operations.Zip(soldes))
        {
            feuille.Cell(ligne, 1).Value = operation.Libelle;
            if (operation.Debit != 0)
                Montant(feuille.Cell(ligne, 2), operation.Debit);
            if (operation.Credit != 0)
                Montant(feuille.Cell(ligne, 3), operation.Credit);
            Montant(feuille.Cell(ligne, 4), solde);
            feuille.Cell(ligne, 4).Style.Font.SetBold();
            if (solde < 0)
                feuille.Cell(ligne, 4).Style.Font.SetFontColor(Rouge);
            feuille.Cell(ligne, 5).Value = operation.Pointee ? "✓" : "";
            feuille.Cell(ligne, 5).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            feuille.Cell(ligne, 6).Value = operation.Enveloppe ?? "";
            feuille.Cell(ligne, 7).Value = operation.CompteCumul ?? "";
            ligne++;
        }

        // Largeurs fixes : le calcul automatique demande des polices installées.
        foreach (var (colonne, largeur) in new[] { (1, 34), (2, 14), (3, 14), (4, 14), (5, 9), (6, 16), (7, 18) })
            feuille.Column(colonne).Width = largeur;

        classeur.SaveAs(chemin);
    }

    private static int Titre(IXLWorksheet feuille, int ligne, string titre)
    {
        feuille.Cell(ligne, 1).Value = titre;
        feuille.Cell(ligne, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(Bleu);
        return ligne + 1;
    }

    private static int Entete(IXLWorksheet feuille, int ligne, params string[] colonnes)
    {
        for (var i = 0; i < colonnes.Length; i++)
            feuille.Cell(ligne, i + 1).Value = colonnes[i];
        feuille.Range(ligne, 1, ligne, colonnes.Length).Style
            .Font.SetBold()
            .Fill.SetBackgroundColor(BleuClair)
            .Border.SetBottomBorder(XLBorderStyleValues.Thin);
        return ligne + 1;
    }

    private static void Montant(IXLCell cellule, decimal montant)
    {
        cellule.Value = montant;
        cellule.Style.NumberFormat.Format = FormatEuros;
    }
}
