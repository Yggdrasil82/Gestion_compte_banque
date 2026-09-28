using GestionCompte.Core.Modeles;

namespace GestionCompte.Tests;

/// <summary>Configuration reprise de la feuille « Octobre 2026 » du fichier Excel d'origine.</summary>
internal static class DonneesExcel
{
    public const string Epargne = "Économie";
    public const string RembPascale = "Remb. Pascale";

    public static ConfigurationBudget ConfigurationOctobre2026(decimal? objectifRembPascale = null)
    {
        var configuration = new ConfigurationBudget
        {
            PremierMois = new PeriodeMois(2026, 10),
            SoldeInitial = 0m,
        };

        configuration.Revenus.AddRange(new[]
        {
            new ModeleRevenu("Salaire Romain", 2600m),
            new ModeleRevenu("NDF", 0m),
            new ModeleRevenu("CAF", 600m),
            new ModeleRevenu("Autres", 0m),
        });

        configuration.Enveloppes.AddRange(new[]
        {
            new ModeleEnveloppe("Courses", 400m),
            new ModeleEnveloppe("Carburant", 100m),
        });

        configuration.Charges.AddRange(new[]
        {
            new ModeleCharge("Loyer", 801m),
            new ModeleCharge("Mobile Killian", 9.99m),
            new ModeleCharge("Économie", 0m, CompteCumul: Epargne),
            new ModeleCharge("Remb Pascale", 100m, CompteCumul: RembPascale),
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

        configuration.ComptesCumul.Add(new CompteCumul(Epargne));
        configuration.ComptesCumul.Add(new CompteCumul(RembPascale, Objectif: objectifRembPascale));

        return configuration;
    }
}
