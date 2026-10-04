using GestionCompte.Core;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Presentation;

/// <summary>Configurations de départ : vierge (nouvelle installation) ou d'exemple (captures d'écran).</summary>
public static class ConfigurationParDefaut
{
    public const string Epargne = "Épargne";
    public const string RembFamille = "Remb. famille";

    /// <summary>
    /// Configuration vierge d'une nouvelle installation : postes courants à 0 €, sans aucune donnée personnelle.
    /// </summary>
    public static ConfigurationBudget Creer(PeriodeMois premierMois)
    {
        var configuration = new ConfigurationBudget { PremierMois = premierMois, SoldeInitial = 0m };

        configuration.Revenus.Add(new ModeleRevenu("Salaire", 0m));
        configuration.Enveloppes.AddRange(new[]
        {
            new ModeleEnveloppe("Courses", 0m),
            new ModeleEnveloppe("Carburant", 0m),
        });
        configuration.ComptesCumul.Add(new CompteCumul(Epargne));
        configuration.Charges.AddRange(new[]
        {
            new ModeleCharge("Loyer", 0m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Électricité", 0m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Eau", 0m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Internet", 0m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Mobile", 0m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Assurance habitation", 0m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Épargne", 0m, CompteCumul: Epargne, Categorie: Categorie.Epargne),
        });
        configuration.Regles.AddRange(RegleClassement.ParDefaut);

        return configuration;
    }

    /// <summary>Configuration d'exemple (montants inventés), base des données de démonstration.</summary>
    public static ConfigurationBudget CreerExemple(PeriodeMois premierMois)
    {
        var configuration = new ConfigurationBudget { PremierMois = premierMois, SoldeInitial = 0m };

        configuration.Revenus.AddRange(new[]
        {
            new ModeleRevenu("Salaire", 2800m),
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
        configuration.ComptesCumul.Add(new CompteCumul(RembFamille));

        configuration.Charges.AddRange(new[]
        {
            new ModeleCharge("Loyer", 801m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Mobile enfant 1", 9.99m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Épargne", 0m, CompteCumul: Epargne, Categorie: Categorie.Epargne),
            new ModeleCharge("Remb. famille", 100m, CompteCumul: RembFamille, Categorie: Categorie.Essentiel),
            new ModeleCharge("Mobile", 9.99m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Crédit Voiture", 487m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Assurance Voiture", 135.89m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Mobile enfant 2", 8.99m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Abo Xbox", 18m, Categorie: Categorie.Confort),
            new ModeleCharge("Assurance banque", 3.8m, Categorie: Categorie.Essentiel),
            new ModeleCharge("Spotify", 18.99m, Categorie: Categorie.Confort),
            new ModeleCharge("Mobile enfant 3", 15.99m, Categorie: Categorie.Essentiel),
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

    /// <summary>
    /// Relevé OFX d'exemple (décembre 2026, mois pas encore créé), pour les captures d'écran de l'import ;
    /// sa première ligne date d'octobre (chevauchement de relevé) et a le montant de « Marché », pas encore pointé en novembre.
    /// </summary>
    public static string ReleveDemo()
    {
        static string Ligne(string type, string date, string montant, string id, string libelle) =>
            $"<STMTTRN>\n<TRNTYPE>{type}\n<DTPOSTED>{date}\n<TRNAMT>{montant}\n<FITID>{id}\n<NAME>{libelle}\n</STMTTRN>\n";

        return "OFXHEADER:100\nDATA:OFXSGML\nVERSION:102\nCHARSET:1252\n<OFX>\n<BANKMSGSRSV1>\n<STMTTRNRS>\n<STMTRS>\n<CURDEF>EUR\n<BANKTRANLIST>\n"
            + Ligne("DEBIT", "20261030", "-27.50", "DEMO00", "CB WERO 29/10/26")
            + Ligne("XFER", "20261202", "-801.00", "DEMO01", "PRLV SEPA LOYER SCI LES TILLEULS")
            + Ligne("XFER", "20261205", "-9.99", "DEMO02", "PRLV SEPA FREE MOBILE")
            + Ligne("DEBIT", "20261206", "-58.40", "DEMO03", "CB LECLERC DRIVE 05/12/26")
            + Ligne("XFER", "20261208", "-152.30", "DEMO04", "PRLV SEPA EDF ELECTRICITE")
            + Ligne("DEBIT", "20261210", "-41.42", "DEMO05", "CB  COFIDIS AMAZON   09/12/26")
            + Ligne("DEBIT", "20261212", "-52.10", "DEMO06", "CB ESSO EXPRESS 11/12/26")
            + Ligne("XFER", "20261215", "-18.99", "DEMO07", "PRLV SEPA SPOTIFY")
            + Ligne("DEBIT", "20261218", "-23.80", "DEMO08", "CB BOULANGERIE DU MARCHE 17/12/26")
            + Ligne("CREDIT", "20261227", "+2812.35", "DEMO09", "VIR SALAIRE")
            + Ligne("CREDIT", "20261228", "+12.50", "DEMO10", "VIREMENT CPMS")
            + "</BANKTRANLIST>\n<LEDGERBAL>\n<BALAMT>4650.18\n<DTASOF>20261228000000\n</LEDGERBAL>\n</STMTRS>\n</STMTTRNRS>\n</BANKMSGSRSV1>\n</OFX>\n";
    }

    /// <summary>Livret d'épargne d'exemple (second compte des captures d'écran).</summary>
    public static CompteBancaire CreerDemoLivret()
    {
        var configuration = new ConfigurationBudget { PremierMois = new PeriodeMois(2026, 10), SoldeInitial = 2400m };
        configuration.Charges.Add(new ModeleCharge("Versement depuis le compte courant", 0m, Credit: 150m, Categorie: Categorie.Epargne));
        configuration.Regles.AddRange(RegleClassement.ParDefaut);
        var compte = new CompteBancaire(configuration);
        compte.OperationsPrevues.Add(new OperationPrevue(new PeriodeMois(2027, 7), "Vacances d'été", debit: 1000m));
        foreach (var mois in new[] { compte.CreerMoisSuivant(), compte.CreerMoisSuivant() })
            mois.Operations.ForEach(o => o.Pointee = true);
        compte.Mois[^1].Operations.Add(new Operation("Intérêts", credit: 12.40m) { Pointee = true });
        return compte;
    }

    /// <summary>
    /// Compte d'exemple sur deux ans (janvier 2025 à octobre 2026), pour les captures du bilan annuel :
    /// hausse de l'électricité et des restaurants en 2026, carburant souvent dépassé, courses sous le budget.
    /// </summary>
    public static CompteBancaire CreerDemoHistorique()
    {
        var configuration = CreerExemple(new PeriodeMois(2025, 1));
        configuration.SoldeInitial = 1200m;
        var compte = new CompteBancaire(configuration);
        var fin = new PeriodeMois(2026, 10);
        for (var i = 0; compte.ProchainMois <= fin; i++)
        {
            var mois = compte.CreerMoisSuivant();
            var p = mois.Periode;
            var annee2026 = p.Annee == 2026;
            mois.Revenus.ForEach(r => r.Recu = true);
            mois.Operations.ForEach(o => o.Pointee = true);
            mois.Operations.Single(o => o.CompteCumul == Epargne).Debit = annee2026 ? 150m : 100m;
            if (annee2026)
                mois.Operations.Single(o => o.Libelle == "Électricité").Debit = 172m;

            // Montants inventés, variés d'un mois à l'autre mais toujours les mêmes d'une compilation à l'autre.
            var variation = (i * 37 % 11) - 5;
            mois.Operations.AddRange(new[]
            {
                new Operation("Leclerc", debit: 180m + variation * 6) { Enveloppe = "Courses", Pointee = true },
                new Operation("Lidl", debit: 95m + variation * 4) { Enveloppe = "Courses", Pointee = true },
                new Operation("Plein Total", debit: 62m + variation * 3) { Enveloppe = "Carburant", Pointee = true },
                new Operation("Plein Esso", debit: 48m + (i % 3) * 9) { Enveloppe = "Carburant", Pointee = true },
                new Operation("Restaurant", debit: (annee2026 ? 85m : 40m) + (i % 4) * 5) { Pointee = true },
            });
            if (p.Mois == 12)
                mois.Operations.Add(new Operation("Cadeaux de Noël", debit: 420m) { Pointee = true });
            if (p.Mois == 7)
                mois.Operations.Add(new Operation("Vacances", debit: annee2026 ? 1350m : 1100m) { Pointee = true });
            if (p.Mois == 3)
                mois.Operations.Add(new Operation("Remboursement mutuelle", credit: 64.20m) { Pointee = true });
        }

        return compte;
    }

    /// <summary>Compte d'exemple avec deux mois remplis, pour les captures d'écran.</summary>
    public static CompteBancaire CreerDemo()
    {
        var configuration = CreerExemple(new PeriodeMois(2026, 10));
        configuration.SoldeInitial = 350m;
        configuration.ComptesCumul[1] = new CompteCumul(RembFamille, Objectif: 1500m);
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
        compte.SimulationsCredit.Add(new SimulationCredit("Maison 20 ans", 180000m, 3.4m, 240, new PeriodeMois(2027, 3), 0.30m));
        compte.SimulationsCredit.Add(new SimulationCredit("Maison 25 ans", 180000m, 3.55m, 300, new PeriodeMois(2027, 3), 0.30m));
        compte.SimulationsCredit.Add(new SimulationCredit("Moto", 9000m, 5.9m, 48, new PeriodeMois(2027, 1), 12m, TypeAssurance.ParMois)
            { Type = TypeCredit.AutoMoto });

        // Prêts fictifs : un prêt lissé en 3 paliers et un prêt à taux zéro avec 5 ans de différé.
        var maison = new PretImmobilier("Maison", 165000m, 1.35m, new PeriodeMois(2021, 3),
            new[] { new PalierPret(60, 640m), new PalierPret(180, 520m), new PalierPret(60, 0m) }, 0.26m)
        {
            Signature = new PeriodeMois(2019, 1),
            ExonerationAnnees = 7,
        };
        maison.Paliers[2] = new PalierPret(60, Core.Calculs.CalculPret.MensualiteDernierPalier(maison) ?? 0m);
        compte.Prets.Add(maison);
        compte.Prets.Add(new PretImmobilier("PTZ", 24000m, 0m, new PeriodeMois(2021, 3),
            new[] { new PalierPret(60, 0m), new PalierPret(180, 133.33m) }, 0.26m) { SansIndemnite = true });

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

        // Catégories d'opérations choisies à la main en novembre (captures du rangement par catégorie).
        compte.Configuration.CategoriesOperations.AddRange(new[]
        {
            new CategorieOperation("Maison", "#2F7FD8"),
            new CategorieOperation("Voiture", "#E8892B"),
            new CategorieOperation("Enfants", "#8A5CD1"),
        });
        foreach (var (categorie, libelles) in new[]
                 {
                     ("Maison", new[] { "Loyer", "Électricité", "Eau", "Alarme", "Poubelle", "Internet" }),
                     ("Voiture", new[] { "Crédit Voiture", "Assurance Voiture", "Plein Total" }),
                     ("Enfants", new[] { "Mobile enfant 1", "Mobile enfant 2", "Mobile enfant 3", "Cantine" }),
                 })
        {
            foreach (var operation in novembre.Operations.Where(o => libelles.Contains(o.Libelle)))
                operation.CategorieOperation = categorie;
            var modeles = compte.Configuration.Charges;
            for (var i = 0; i < modeles.Count; i++)
                if (libelles.Contains(modeles[i].Nom))
                    modeles[i] = modeles[i] with { CategorieOperation = categorie };
        }

        // Poubelles prélevées un mois sur deux (novembre, janvier, mars…).
        var charges = compte.Configuration.Charges;
        var poubelle = charges.FindIndex(c => c.Nom == "Poubelle");
        charges[poubelle] = charges[poubelle] with { Frequence = 2, Depart = novembre.Periode };

        return compte;
    }
}
