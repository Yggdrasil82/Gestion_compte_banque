using System.Globalization;
using System.Text;

namespace GestionCompte.Core.Bourse;

public enum NiveauIndicateur
{
    /// <summary>Simple information.</summary>
    Info,
    /// <summary>Point favorable.</summary>
    Bon,
    /// <summary>Point à surveiller.</summary>
    Attention,
}

/// <summary>Un indicateur de l'aide à la décision : constat chiffré, sans conseil d'achat ou de vente.</summary>
public sealed record IndicateurBourse(string Titre, string Texte, NiveauIndicateur Niveau);

/// <summary>
/// Aide à la décision du module Bourse : poids des frais, concentration, PEA, optimisation fiscale de fin d'année et
/// rythme des versements, calculés sur tout le portefeuille. Ce sont des constats, pas des conseils d'investissement.
/// </summary>
public static class AideDecisionBourse
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Au-delà, les frais d'un ordre sont jugés lourds (1 € sur un ordre de moins de 100 €).</summary>
    public const decimal SeuilFrais = 0.01m;

    /// <summary>Au-delà, une action seule pèse trop dans les titres.</summary>
    public const decimal SeuilConcentration = 0.25m;

    /// <summary>Au-delà, la part des espèces non investies est signalée.</summary>
    public const decimal SeuilEspeces = 0.10m;

    /// <summary>Pays de l'Union européenne et de l'Espace économique européen (actions éligibles au PEA), d'après l'ISIN.</summary>
    private static readonly HashSet<string> PaysPea = new(StringComparer.OrdinalIgnoreCase)
    {
        "FR", "DE", "NL", "BE", "LU", "IT", "ES", "PT", "IE", "AT", "FI", "SE", "DK", "PL", "CZ", "SK", "SI", "HU", "RO",
        "BG", "HR", "GR", "CY", "MT", "EE", "LV", "LT", "NO", "IS", "LI",
    };

    public static IReadOnlyList<IndicateurBourse> Calculer(Portefeuille portefeuille, DateTime aujourdhui)
    {
        ArgumentNullException.ThrowIfNull(portefeuille);
        if (portefeuille.Vide)
            return Array.Empty<IndicateurBourse>();
        var bilan = CalculBourse.Calculer(portefeuille, null, aujourdhui.Year);
        return new[]
            {
                Frais(portefeuille),
                Concentration(bilan),
                Pea(portefeuille, bilan, aujourdhui),
                FinDAnnee(bilan, aujourdhui),
                Rythme(portefeuille, bilan, aujourdhui),
            }
            .OfType<IndicateurBourse>()
            .ToList();
    }

    private static IndicateurBourse? Frais(Portefeuille portefeuille)
    {
        var ordres = portefeuille.Operations
            .Where(o => o.EstTitre && o.Montant != 0)
            .Select(o => (Montant: Math.Abs(o.Montant), Frais: -o.Frais))
            .ToList();
        if (ordres.Count == 0)
            return null;
        var total = ordres.Sum(o => o.Frais);
        var pourcent = total / ordres.Sum(o => o.Montant) * 100;
        var texte = new StringBuilder($"{ordres.Count} ordre{S(ordres.Count)}, {Euros(total)} de frais, soit {pourcent.ToString("0.0", Francais)} % des montants.");
        var chers = ordres.Where(o => o.Frais / o.Montant > SeuilFrais).ToList();
        if (chers.Count > 0)
        {
            var pire = chers.MaxBy(o => o.Frais / o.Montant);
            texte.Append($" {chers.Count} ordre{S(chers.Count)} {(chers.Count > 1 ? "ont" : "a")} coûté plus de 1 % en frais " +
                         $"(jusqu'à {(pire.Frais / pire.Montant * 100).ToString("0.0", Francais)} % sur un ordre de {Euros(pire.Montant)}). " +
                         "Pour de petits montants réguliers, un plan d'épargne Trade Republic est sans frais.");
        }
        return new IndicateurBourse("Poids des frais", texte.ToString(), chers.Count > 0 ? NiveauIndicateur.Attention : NiveauIndicateur.Bon);
    }

    private static IndicateurBourse? Concentration(BilanBourse bilan)
    {
        var total = bilan.ValeurTitres;
        if (bilan.Positions.Count == 0 || total <= 0)
            return null;
        var repartition = bilan.Positions
            .GroupBy(p => Classe(p.Classe))
            .Select(g => (Classe: g.Key, Part: g.Sum(p => p.Valeur) / total))
            .OrderByDescending(x => x.Part)
            .Select(x => $"{x.Classe} {Pourcent(x.Part)}");
        var texte = new StringBuilder($"Répartition : {string.Join(", ", repartition)}.");
        // Un ETF est déjà diversifié : seules les actions seules sont signalées.
        var lourdes = bilan.Positions
            .Where(p => !EstFonds(p.Classe) && p.Valeur / total > SeuilConcentration)
            .OrderByDescending(p => p.Valeur)
            .ToList();
        foreach (var p in lourdes)
            texte.Append($" {p.Nom} pèse {Pourcent(p.Valeur / total)} des titres : une baisse de cette seule action se verrait beaucoup.");
        if (lourdes.Count == 0)
            texte.Append($" Aucune action ne dépasse {Pourcent(SeuilConcentration)} des titres (les ETF, déjà diversifiés, ne sont pas comptés).");
        return new IndicateurBourse("Concentration", texte.ToString(), lourdes.Count > 0 ? NiveauIndicateur.Attention : NiveauIndicateur.Bon);
    }

    private static IndicateurBourse? Pea(Portefeuille portefeuille, BilanBourse bilan, DateTime aujourdhui)
    {
        var texte = new StringBuilder();
        var operationsPea = portefeuille.Operations.Where(o => o.Enveloppe == EnveloppeBourse.Pea).ToList();
        if (operationsPea.Count > 0)
        {
            var ouverture = operationsPea.Min(o => o.Date).Date;
            var cinqAns = ouverture.AddYears(5);
            texte.Append(aujourdhui.Date < cinqAns
                ? $"PEA ouvert le {ouverture:dd/MM/yyyy} (première opération) : à partir du {cinqAns:dd/MM/yyyy}, ses gains retirés ne paieront plus " +
                  "que les prélèvements sociaux (18,6 %) au lieu de 31,4 %. Avant cette date, un retrait ferme le PEA."
                : $"PEA de plus de 5 ans (ouvert le {ouverture:dd/MM/yyyy}) : ses gains retirés ne paient que les prélèvements sociaux (18,6 %).");
        }
        var eligibles = bilan.Positions
            .Where(p => p.Enveloppe == EnveloppeBourse.CompteTitres && !EstFonds(p.Classe) && p.Isin.Length >= 2 && PaysPea.Contains(p.Isin[..2]))
            .Select(p => p.Nom)
            .ToList();
        if (eligibles.Count > 0)
        {
            if (texte.Length > 0)
                texte.Append(' ');
            var plusieurs = eligibles.Count > 1;
            texte.Append($"{Liste(eligibles)} {(plusieurs ? "sont des actions européennes, éligibles" : "est une action européenne, éligible")} " +
                         $"au PEA : {(operationsPea.Count > 0 ? (plusieurs ? "achetées dans le PEA" : "achetée dans le PEA") : "dans un PEA")}, " +
                         $"{(plusieurs ? "leurs" : "ses")} futures plus-values seraient moins imposées qu'au compte-titres.");
        }
        return texte.Length == 0 ? null : new IndicateurBourse("PEA ou compte-titres", texte.ToString(), NiveauIndicateur.Info);
    }

    private static IndicateurBourse? FinDAnnee(BilanBourse bilan, DateTime aujourdhui)
    {
        var annee = aujourdhui.Year;
        var pertes = bilan.Positions
            .Where(p => p.Enveloppe == EnveloppeBourse.CompteTitres && p.Cours is not null && p.PlusValue < 0)
            .OrderBy(p => p.PlusValue)
            .ToList();
        var imposables = bilan.PlusValuesImposables;
        if (imposables > 0 && pertes.Count > 0)
        {
            var compensable = Math.Min(imposables, -pertes.Sum(p => p.PlusValue));
            return new IndicateurBourse($"Fiscalité {annee}",
                $"Plus-values {annee} du compte-titres : {Signe(imposables)}, flat tax prévue {Euros(bilan.FlatTax)}. " +
                $"Les lignes en moins-value ({string.Join(", ", pertes.Select(p => $"{p.Nom} {Signe(p.PlusValue)}"))}), si elles étaient vendues " +
                $"avant le 31/12, compenseraient {Euros(compensable)} de ces plus-values, soit environ " +
                $"{Euros(Math.Round(compensable * bilan.TauxFlatTax, 2))} d'impôt en moins. À peser selon vos projets : ce n'est pas un conseil de vente.",
                NiveauIndicateur.Info);
        }
        if (imposables > 0)
            return new IndicateurBourse($"Fiscalité {annee}",
                $"Plus-values {annee} du compte-titres : {Signe(imposables)}, flat tax prévue {Euros(bilan.FlatTax)}. " +
                "Aucune ligne du compte-titres n'est en moins-value pour les compenser.", NiveauIndicateur.Info);
        if (imposables < 0)
            return new IndicateurBourse($"Fiscalité {annee}",
                $"Les ventes {annee} du compte-titres font une perte de {Euros(-imposables)} : pas d'impôt cette année, et cette perte " +
                "se reporte sur les plus-values des 10 années suivantes (à indiquer dans la déclaration de revenus).", NiveauIndicateur.Bon);
        return null;
    }

    private static IndicateurBourse? Rythme(Portefeuille portefeuille, BilanBourse bilan, DateTime aujourdhui)
    {
        var internes = CalculBourse.VirementsInternes(portefeuille.Operations);
        var versements = portefeuille.Operations.Where(o => o.Type == TypeOperationBourse.Versement && !internes.Contains(o)).ToList();
        var texte = new StringBuilder();
        if (versements.Count > 0)
        {
            var debut = versements.Min(o => o.Date);
            var mois = Math.Max(1, (aujourdhui.Year - debut.Year) * 12 + aujourdhui.Month - debut.Month + 1);
            texte.Append($"Versé en moyenne {Euros(Math.Round(bilan.Versements / mois, 2))} par mois depuis " +
                         $"{debut.ToString("MMMM yyyy", Francais)} ({Euros(bilan.Versements)} en {mois} mois).");
        }
        var especes = bilan.ValeurTotale <= 0 ? 0 : bilan.Especes / bilan.ValeurTotale;
        if (texte.Length > 0)
            texte.Append(' ');
        texte.Append($"Espèces non investies : {Euros(bilan.Especes)} ({Pourcent(especes)} du portefeuille)");
        texte.Append(especes > SeuilEspeces ? " : une part importante attend d'être investie." : ".");
        return new IndicateurBourse("Rythme et espèces", texte.ToString(), especes > SeuilEspeces ? NiveauIndicateur.Attention : NiveauIndicateur.Info);
    }

    /// <summary>
    /// Résumé du portefeuille pour l'Assistant : chiffres et lignes (nom, enveloppe, type, poids, plus-value), sans ISIN ni
    /// numéro de compte.
    /// </summary>
    public static string Resume(Portefeuille portefeuille, DateTime aujourdhui)
    {
        if (portefeuille.Vide)
            return "";
        var bilan = CalculBourse.Calculer(portefeuille, null, aujourdhui.Year);
        var texte = new StringBuilder();
        texte.AppendLine($"Portefeuille en bourse (Trade Republic) : valeur {Euros(bilan.ValeurTotale)} dont titres {Euros(bilan.ValeurTitres)} " +
                         $"et espèces {Euros(bilan.Especes)} ; argent versé net {Euros(bilan.VerseNet)} ; plus-value latente {Signe(bilan.PlusValueLatente)} ; " +
                         $"plus-values des ventes {Signe(bilan.PlusValuesRealisees)} ; dividendes et intérêts {Euros(bilan.Dividendes)} ; " +
                         $"frais et taxes payés {Euros(bilan.FraisEtTaxes)} ; flat tax prévue {aujourdhui.Year} {Euros(bilan.FlatTax)}.");
        foreach (var p in bilan.Positions.OrderByDescending(p => p.Valeur))
            texte.AppendLine($"- {p.Nom} ({(p.Enveloppe == EnveloppeBourse.Pea ? "PEA" : "compte-titres")}, {Classe(p.Classe).ToLowerInvariant()}) : " +
                             $"{Euros(p.Valeur)}, {Pourcent(bilan.ValeurTitres == 0 ? 0 : p.Valeur / bilan.ValeurTitres)} des titres, " +
                             $"plus-value {Signe(p.PlusValue)}{(p.PlusValuePourcent is { } pc ? $" ({pc.ToString("+0.0;-0.0;0", Francais)} %)" : "")}.");
        foreach (var indicateur in Calculer(portefeuille, aujourdhui))
            texte.AppendLine($"{indicateur.Titre} : {indicateur.Texte}");
        return texte.ToString().TrimEnd();
    }

    private static bool EstFonds(string? classe) => classe is not null &&
        (classe.Contains("FUND", StringComparison.OrdinalIgnoreCase) || classe.Contains("ETF", StringComparison.OrdinalIgnoreCase));

    private static string Classe(string? classe) => classe?.ToUpperInvariant() switch
    {
        "STOCK" => "Actions",
        "BOND" => "Obligations",
        "CRYPTO" => "Crypto",
        _ when EstFonds(classe) => "ETF et fonds",
        _ => "Autres",
    };

    private static string Liste(IReadOnlyList<string> noms) =>
        noms.Count == 1 ? noms[0] : $"{string.Join(", ", noms.Take(noms.Count - 1))} et {noms[^1]}";

    private static string S(int nombre) => nombre > 1 ? "s" : "";

    private static string Euros(decimal montant) => $"{Montants.Formater(montant)} €";

    private static string Signe(decimal montant) => $"{(montant > 0 ? "+" : "")}{Montants.Formater(montant)} €";

    private static string Pourcent(decimal part) => $"{Math.Round(part * 100).ToString("0", Francais)} %";
}
