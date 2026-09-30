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

    /// <summary>Export d'une simulation de crédit : conditions, coût et tableau d'amortissement.</summary>
    public static void ExporterCredit(SimulationCredit simulation, string chemin)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var resultat = Credit.Calculer(simulation);

        using var classeur = new XLWorkbook();
        var feuille = classeur.Worksheets.Add("Crédit");
        feuille.Cell(1, 1).Value = Credit.Libelle(simulation);
        feuille.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(18).Font.SetFontColor(Bleu);
        var ligne = 3;

        ligne = Titre(feuille, ligne, "Conditions");
        void Info(string libelle, XLCellValue valeur, bool euros = false)
        {
            feuille.Cell(ligne, 1).Value = libelle;
            feuille.Cell(ligne, 2).Value = valeur;
            if (euros)
                feuille.Cell(ligne, 2).Style.NumberFormat.Format = FormatEuros;
            ligne++;
        }
        Info("Type", simulation.Type switch
        {
            TypeCredit.AutoMoto => "Auto / moto",
            TypeCredit.Consommation => "Consommation",
            _ => "Immobilier",
        });
        Info("Montant emprunté", simulation.Montant, euros: true);
        Info("Taux annuel (%)", simulation.TauxAnnuel);
        Info("Durée (mois)", simulation.DureeMois);
        Info("Première échéance", simulation.PremiereEcheance.Libelle);
        Info("Mensualité hors assurance", resultat.MensualiteHorsAssurance, euros: true);
        Info("Assurance par mois", resultat.AssuranceMensuelle, euros: true);
        Info("Mensualité", resultat.Mensualite, euros: true);
        Info("Coût des intérêts", resultat.CoutInterets, euros: true);
        Info("Coût de l'assurance", resultat.CoutAssurance, euros: true);
        Info("Coût total du crédit", resultat.CoutTotal, euros: true);
        feuille.Range(ligne - 1, 1, ligne - 1, 2).Style.Font.SetBold().Fill.SetBackgroundColor(BleuClair);
        ligne++;

        ligne = Titre(feuille, ligne, "Tableau d'amortissement");
        ligne = Entete(feuille, ligne, "N°", "Mois", "Mensualité", "Capital", "Intérêts", "Assurance", "Capital restant dû");
        foreach (var echeance in resultat.Tableau)
        {
            feuille.Cell(ligne, 1).Value = echeance.Numero;
            feuille.Cell(ligne, 2).Value = echeance.Periode.Libelle;
            Montant(feuille.Cell(ligne, 3), echeance.Mensualite);
            Montant(feuille.Cell(ligne, 4), echeance.Capital);
            Montant(feuille.Cell(ligne, 5), echeance.Interets);
            Montant(feuille.Cell(ligne, 6), echeance.Assurance);
            Montant(feuille.Cell(ligne, 7), echeance.CapitalRestant);
            ligne++;
        }

        foreach (var (colonne, largeur) in new[] { (1, 28), (2, 18), (3, 14), (4, 14), (5, 14), (6, 14), (7, 18) })
            feuille.Column(colonne).Width = largeur;

        classeur.SaveAs(chemin);
    }

    /// <summary>Export du bilan : totaux, mois par mois, postes de dépenses et pistes d'économie.</summary>
    public static void ExporterBilan(ResultatBilan bilan, string titre, string chemin)
    {
        ArgumentNullException.ThrowIfNull(bilan);

        using var classeur = new XLWorkbook();
        var feuille = classeur.Worksheets.Add("Bilan");
        feuille.Cell(1, 1).Value = titre;
        feuille.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(18).Font.SetFontColor(Bleu);
        feuille.Cell(2, 1).Value = bilan.Etendue;
        var ligne = 4;

        ligne = Titre(feuille, ligne, "Résumé");
        foreach (var (libelle, montant) in new (string, decimal)[]
        {
            ("Revenus", bilan.Revenus),
            ("Dépenses", bilan.Depenses),
            ("Épargne", bilan.Epargne),
            ($"Solde au début ({bilan.Mois.FirstOrDefault()?.Periode.Libelle})", bilan.SoldeDebut),
            ($"Solde à la fin ({bilan.Mois.LastOrDefault()?.Periode.Libelle})", bilan.SoldeFin),
            ("Variation du solde", bilan.Variation),
        })
        {
            feuille.Cell(ligne, 1).Value = libelle;
            Montant(feuille.Cell(ligne, 2), montant);
            ligne++;
        }
        feuille.Cell(ligne, 1).Value = "Taux d'épargne";
        if (bilan.TauxEpargne is { } taux)
        {
            feuille.Cell(ligne, 2).Value = taux;
            feuille.Cell(ligne, 2).Style.NumberFormat.Format = "0.0 %";
        }
        ligne += 2;

        ligne = Titre(feuille, ligne, "Mois par mois");
        ligne = Entete(feuille, ligne, "Mois", "Revenus", "Dépenses", "Épargne", "Solde de fin");
        foreach (var mois in bilan.Mois)
        {
            feuille.Cell(ligne, 1).Value = mois.Periode.Libelle;
            Montant(feuille.Cell(ligne, 2), mois.Revenus);
            Montant(feuille.Cell(ligne, 3), mois.Depenses);
            Montant(feuille.Cell(ligne, 4), mois.Epargne);
            Montant(feuille.Cell(ligne, 5), mois.SoldeFin);
            if (mois.SoldeFin < 0)
                feuille.Cell(ligne, 5).Style.Font.SetFontColor(Rouge);
            ligne++;
        }
        ligne++;

        ligne = Titre(feuille, ligne, "Postes de dépenses");
        ligne = Entete(feuille, ligne, "Poste", "Total", "Par mois", "Part des revenus", "Année d'avant (par mois)", "Évolution", "Type");
        foreach (var poste in bilan.Postes)
        {
            feuille.Cell(ligne, 1).Value = poste.Nom;
            Montant(feuille.Cell(ligne, 2), poste.Total);
            Montant(feuille.Cell(ligne, 3), poste.MoyenneMensuelle);
            feuille.Cell(ligne, 4).Value = poste.PartRevenus;
            feuille.Cell(ligne, 4).Style.NumberFormat.Format = "0.0 %";
            if (poste.MoyennePrecedente is { } avant)
                Montant(feuille.Cell(ligne, 5), avant);
            if (poste.Evolution is { } evolution)
            {
                feuille.Cell(ligne, 6).Value = evolution;
                feuille.Cell(ligne, 6).Style.NumberFormat.Format = "+0 %;-0 %;0 %";
                if (evolution >= Bilan.SeuilHausse && poste.Type != TypePoste.Epargne)
                    feuille.Cell(ligne, 6).Style.Font.SetFontColor(Rouge);
            }
            feuille.Cell(ligne, 7).Value = Bilan.NomType(poste.Type);
            ligne++;
        }
        ligne++;

        ligne = Titre(feuille, ligne, "Pistes d'économie");
        ligne = Entete(feuille, ligne, "Piste", "Gain possible par an", "Détail");
        foreach (var piste in bilan.Pistes)
        {
            feuille.Cell(ligne, 1).Value = piste.Titre;
            if (piste.GainAnnuel is { } gain)
                Montant(feuille.Cell(ligne, 2), gain);
            feuille.Cell(ligne, 3).Value = piste.Detail;
            ligne++;
        }

        foreach (var (colonne, largeur) in new[] { (1, 44), (2, 16), (3, 16), (4, 16), (5, 22), (6, 12), (7, 16) })
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
