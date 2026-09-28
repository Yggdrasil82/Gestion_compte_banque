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

        return configuration;
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
        octobre.Operations.AddRange(new[]
        {
            new Operation("Leclerc", debit: 142.35m) { Enveloppe = "Courses", Pointee = true },
            new Operation("Lidl", debit: 63.80m) { Enveloppe = "Courses", Pointee = true },
            new Operation("Plein Total", debit: 71.20m) { Enveloppe = "Carburant", Pointee = true },
            new Operation("Plein Esso", debit: 48.90m) { Enveloppe = "Carburant", Pointee = true },
        });

        var novembre = compte.CreerMoisSuivant();
        novembre.Revenus.Single(r => r.Nom == "NDF").Montant = 86.40m;
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

        return compte;
    }
}
