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
            new ModeleCharge("Loyer", 801m),
            new ModeleCharge("Mobile Killian", 9.99m),
            new ModeleCharge("Épargne", 0m, CompteCumul: Epargne),
            new ModeleCharge("Remb. Pascale", 100m, CompteCumul: RembPascale),
            new ModeleCharge("Mobile", 9.99m),
            new ModeleCharge("Crédit Voiture", 487m),
            new ModeleCharge("Assurance Voiture", 135.89m),
            new ModeleCharge("Mobile Anaëlle", 8.99m),
            new ModeleCharge("Abo Xbox", 18m),
            new ModeleCharge("Assurance LCL", 3.8m),
            new ModeleCharge("Spotify", 18.99m),
            new ModeleCharge("Mobile Adien", 15.99m),
            new ModeleCharge("Internet", 2m),
            new ModeleCharge("Cantine", 150m),
            new ModeleCharge("Électricité", 145m),
            new ModeleCharge("Eau", 45m),
            new ModeleCharge("Alarme", 65m),
            new ModeleCharge("Poubelle", 61.18m),
        });

        return configuration;
    }

    /// <summary>Compte d'exemple avec deux mois remplis, pour les captures d'écran.</summary>
    public static CompteBancaire CreerDemo()
    {
        var compte = new CompteBancaire(Creer(new PeriodeMois(2026, 10)));
        compte.Configuration.SoldeInitial = 350m;

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
