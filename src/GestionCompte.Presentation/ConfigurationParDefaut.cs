using GestionCompte.Core;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Presentation;

/// <summary>Configuration proposée au premier lancement, reprise du classeur Excel (tout est modifiable).</summary>
public static class ConfigurationParDefaut
{
    public const string Epargne = "Épargne";
    public const string RembPascale = "Remb. Pascale";

    public static ConfigurationBudget Creer(PeriodeMois premierMois)
    {
        var configuration = new ConfigurationBudget { PremierMois = premierMois, SoldeInitial = 0m };

        configuration.Revenus.AddRange(new[]
        {
            new ModeleRevenu("Salaire Romain", 2800m),
            new ModeleRevenu("NDF", 0m),
            new ModeleRevenu("CAF", 600m),
            new ModeleRevenu("Autres", 0m),
        });

        configuration.Enveloppes.AddRange(new[]
        {
            new ModeleEnveloppe("Courses", 400m),
            new ModeleEnveloppe("Carburant", 100m),
        });

        configuration.ComptesCumul.Add(new CompteCumul(Epargne));
        configuration.ComptesCumul.Add(new CompteCumul(RembPascale));

        configuration.Charges.AddRange(new[]
        {
            new ModeleCharge("Loyer", 801m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Mobile Killian", 9.99m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Épargne", 0m, CompteCumul: Epargne, Categorie: Categorie.Epargne),
            new ModeleCharge("Remb. Pascale", 100m, CompteCumul: RembPascale, Categorie: Categorie.Essentiel),
            new ModeleCharge("Mobile", 9.99m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Crédit Voiture", 487m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Assurance Voiture", 135.89m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Mobile Anaëlle", 8.99m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Abo Xbox", 18m, Categorie: Categorie.Confort),
            new ModeleCharge("Assurance LCL", 3.8m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Spotify", 18.99m, Categorie: Categorie.Confort),
            new ModeleCharge("Mobile Adien", 15.99m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Internet", 2m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Cantine", 150m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Électricité", 145m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Eau", 45m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Alarme", 65m, Categorie: Categorie.Confort),
            new ModeleCharge("Poubelle", 61.18m, Categorie: Categorie.Essentiel),
        });

        configuration.Regles.AddRange(RegleClassement.ParDefaut);

        return configuration;
    }

    /// <summary>Relevé OFX d'exemple (décembre 2026, mois pas encore créé), pour les captures d'écran de l'import.</summary>
    public static string ReleveDemo()
    {
        static string Ligne(string type, string date, string montant, string id, string libelle) =>
            $"<STMTTRN>\n<TRNTYPE>{type}\n<DTPOSTED>{date}\n<TRNAMT>{montant}\n<FITID>{id}\n<NAME>{libelle}\n</STMTTRN>\n";

        return "OFXHEADER:100\nDATA:OFXSGML\nVERSION:102\nCHARSET:1252\n<OFX>\n<BANKMSGSRSV1>\n<STMTTRNRS>\n<STMTRS>\n<CURDEF>EUR\n<BANKTRANLIST>\n"
            + Ligne("XFER", "20261202", "-801.00", "DEMO01", "PRLV SEPA LOYER SCI LES TILLEULS")
            + Ligne("XFER", "20261205", "-9.99", "DEMO02", "PRLV SEPA FREE MOBILE")
            + Ligne("DEBIT", "20261206", "-58.40", "DEMO03", "CB LECLERC DRIVE 05/12/26")
            + Ligne("XFER", "20261208", "-152.30", "DEMO04", "PRLV SEPA EDF ELECTRICITE")
            + Ligne("DEBIT", "20261210", "-41.42", "DEMO05", "CB  COFIDIS AMAZON   09/12/26")
            + Ligne("DEBIT", "20261212", "-52.10", "DEMO06", "CB ESSO EXPRESS 11/12/26")
            + Ligne("XFER", "20261215", "-18.99", "DEMO07", "PRLV SEPA SPOTIFY")
            + Ligne("DEBIT", "20261218", "-23.80", "DEMO08", "CB BOULANGERIE DU MARCHE 17/12/26")
            + Ligne("CREDIT", "20261227", "+2812.35", "DEMO09", "VIR SALAIRE ROMAIN")
            + Ligne("CREDIT", "20261228", "+12.50", "DEMO10", "VIREMENT CPMS")
            + "</BANKTRANLIST>\n<LEDGERBAL>\n<BALAMT>4677.68\n<DTASOF>20261228000000\n</LEDGERBAL>\n</STMTRS>\n</STMTTRNRS>\n</BANKMSGSRSV1>\n</OFX>\n";
    }

    /// <summary>Compte d'exemple avec deux mois remplis, pour les captures d'écran.</summary>
    public static CompteBancaire CreerDemo()
    {
        var configuration = Creer(new PeriodeMois(2026, 10));
        configuration.SoldeInitial = 350m;
        configuration.ComptesCumul[1] = new CompteCumul(RembPascale, Objectif: 1500m);
        var compte = new CompteBancaire(configuration);
        compte.OperationsPrevues.AddRange(new[]
        {
            new OperationPrevue(new PeriodeMois(2026, 12), "Cadeaux de Noël", debit: 450m),
            new OperationPrevue(new PeriodeMois(2026, 12), "Prime de fin d'année", credit: 600m),
            new OperationPrevue(new PeriodeMois(2027, 7), "Vacances d'été", debit: 1800m),
            new OperationPrevue(new PeriodeMois(2027, 9), "Taxe foncière", debit: 950m),
        });
        compte.ObjectifsEpargne.Add(new ObjectifEpargne("Vacances d'été", 1800m, new PeriodeMois(2027, 6), dejaEpargne: 300m));
        compte.ObjectifsEpargne.Add(new ObjectifEpargne("Fonds d'urgence", 3000m, new PeriodeMois(2027, 12)));

        var octobre = compte.CreerMoisSuivant();
        octobre.Operations.ForEach(o => o.Pointee = true);
        octobre.Revenus.ForEach(r => r.Recu = true);
        octobre.Operations.AddRange(new[]
        {
            new Operation("Leclerc", debit: 142.35m) { Enveloppe = "Courses", Pointee = true },
            new Operation("Lidl", debit: 63.80m) { Enveloppe = "Courses", Pointee = true },
            new Operation("Plein Total", debit: 71.20m) { Enveloppe = "Carburant", Pointee = true },
            new Operation("Plein Esso", debit: 48.90m) { Enveloppe = "Carburant", Pointee = true },
        });

        var novembre = compte.CreerMoisSuivant();
        novembre.Revenus.Single(r => r.Nom == "NDF").Montant = 86.40m;
        novembre.Revenus.ForEach(r => r.Recu = true);
        novembre.Operations.Single(o => o.CompteCumul == Epargne).Debit = 150m;
        foreach (var operation in novembre.Operations.Take(8))
            operation.Pointee = true;
        novembre.Operations.AddRange(new[]
        {
            new Operation("Carrefour", debit: 118.64m) { Enveloppe = "Courses", Pointee = true },
            new Operation("Marché", debit: 27.50m) { Enveloppe = "Courses" },
            new Operation("Plein Total", debit: 65.00m) { Enveloppe = "Carburant" },
            new Operation("Remboursement mutuelle", credit: 42.30m),
        });

        // Poubelles prélevées un mois sur deux (novembre, janvier, mars…).
        var charges = compte.Configuration.Charges;
        var poubelle = charges.FindIndex(c => c.Nom == "Poubelle");
        charges[poubelle] = charges[poubelle] with { Frequence = 2, Depart = novembre.Periode };

        return compte;
    }
}
