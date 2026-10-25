using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GestionCompte.Presentation;

/// <summary>Cours trouvé sur internet, converti en euros, avec le secteur du titre quand il est demandé et connu.</summary>
public sealed record CoursTrouve(string Symbole, decimal Valeur, DateTime Date, string? Secteur = null);

/// <summary>
/// Cours des titres sur Yahoo Finance (gratuit, sans clé) : le symbole est cherché à partir de l'ISIN,
/// de préférence sur une place en euros (Paris, Xetra, Amsterdam…), puis le dernier cours est lu et converti en euros.
/// </summary>
public class ServiceCoursBourse
{
    /// <summary>Places en euros, de la préférée à la moins préférée.</summary>
    private static readonly string[] PlacesEuro = { ".PA", ".DE", ".AS", ".MI", ".F", ".MC", ".BR", ".LS", ".VI", ".IR", ".HE" };

    private readonly HttpClient _http;

    public ServiceCoursBourse(HttpClient? http = null)
    {
        _http = http ?? Data.Reseau.Client(TimeSpan.FromSeconds(20));
    }

    /// <summary>Dernier cours du titre ; <paramref name="symbole"/> (« CW8.PA ») évite la recherche par ISIN.</summary>
    /// <param name="avecSecteur">Cherche aussi le secteur (la recherche est alors faite même si le symbole est connu).</param>
    public virtual async Task<CoursTrouve?> ChercherAsync(string isin, string? symbole, CancellationToken annulation = default,
        bool avecSecteur = false)
    {
        string? secteur = null;
        if (symbole is null || avecSecteur)
        {
            var recherche = await LireAsync($"https://query2.finance.yahoo.com/v1/finance/search?q={Uri.EscapeDataString(isin)}&quotesCount=10&newsCount=0", annulation);
            symbole ??= ChoisirSymbole(recherche);
            if (avecSecteur && symbole is not null)
                secteur = LireSecteur(recherche, symbole);
        }
        if (symbole is null)
            return null;

        var cours = LireCours(await LireAsync($"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbole)}?range=1d&interval=1d", annulation));
        if (cours is null)
            return null;
        var (valeur, devise, date) = cours.Value;

        if (devise == "GBp" || devise == "GBX")
            (valeur, devise) = (valeur / 100, "GBP");
        if (!string.Equals(devise, "EUR", StringComparison.OrdinalIgnoreCase))
        {
            var change = LireCours(await LireAsync($"https://query1.finance.yahoo.com/v8/finance/chart/{devise.ToUpperInvariant()}EUR=X?range=1d&interval=1d", annulation));
            if (change is null)
                return null;
            valeur *= change.Value.Valeur;
        }
        return new CoursTrouve(symbole, Math.Round(valeur, 4), date, secteur);
    }

    /// <summary>
    /// Secteur en français d'un titre trouvé par la recherche (champs « quoteType », « sector » et « industry » de Yahoo Finance,
    /// classement Morningstar), ou null s'il n'est pas donné.
    /// </summary>
    public static string? LireSecteur(string json, string symbole)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("quotes", out var resultats) || resultats.ValueKind != JsonValueKind.Array)
            return null;
        string? Texte(JsonElement e, string nom) => e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var resultat = resultats.EnumerateArray().Cast<JsonElement?>()
            .FirstOrDefault(q => string.Equals(Texte(q!.Value, "symbol"), symbole, StringComparison.OrdinalIgnoreCase))
            ?? resultats.EnumerateArray().Cast<JsonElement?>().FirstOrDefault(q => Texte(q!.Value, "sector") is not null);
        return resultat is { } q ? Secteur(Texte(q, "quoteType"), Texte(q, "sector"), Texte(q, "industry")) : null;
    }

    /// <summary>Traduit le type, le secteur et l'activité de Yahoo Finance vers les secteurs proposés.</summary>
    public static string? Secteur(string? type, string? secteur, string? activite)
    {
        if (type is "ETF" or "MUTUALFUND")
            return "ETF";
        var a = activite ?? "";
        bool Contient(params string[] mots) => mots.Any(m => a.Contains(m, StringComparison.OrdinalIgnoreCase));
        if (Contient("Semiconductor"))
            return "Semi-conducteurs";
        if (Contient("Drug Manufacturers", "Biotechnology", "Pharmaceutical"))
            return "Pharmaceutique";
        if (Contient("Oil & Gas"))
            return "Pétrole et gaz";
        if (Contient("Aerospace & Defense"))
            return "Défense et aéronautique";
        if (Contient("Gold", "Silver", "Precious Metals", "Copper", "Steel", "Industrial Metals", "Aluminum", "Coking Coal"))
            return "Matières premières";
        if (Contient("Luxury Goods", "Household & Personal Products"))
            return "Luxe et cosmétique";
        return secteur switch
        {
            "Technology" => "Technologie",
            "Healthcare" => "Santé",
            "Energy" or "Utilities" => "Énergie",
            "Basic Materials" => "Matières premières",
            "Industrials" => "Industrie",
            "Financial Services" => "Banque et assurance",
            "Consumer Cyclical" or "Consumer Defensive" => "Consommation",
            "Real Estate" => "Immobilier",
            "Communication Services" => "Télécoms et médias",
            _ => null,
        };
    }

    private async Task<string> LireAsync(string adresse, CancellationToken annulation)
    {
        using var requete = new HttpRequestMessage(HttpMethod.Get, adresse);
        requete.Headers.UserAgent.Add(new ProductInfoHeaderValue("Mozilla", "5.0"));
        requete.Headers.UserAgent.Add(new ProductInfoHeaderValue("(compatible; MonBudget)"));
        using var reponse = await _http.SendAsync(requete, annulation);
        if (!reponse.IsSuccessStatusCode)
            throw new HttpRequestException($"Yahoo Finance a répondu {(int)reponse.StatusCode} ({reponse.ReasonPhrase}).");
        return await reponse.Content.ReadAsStringAsync(annulation);
    }

    /// <summary>Symbole à retenir parmi les résultats d'une recherche par ISIN : une place en euros si possible.</summary>
    public static string? ChoisirSymbole(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("quotes", out var resultats) || resultats.ValueKind != JsonValueKind.Array)
            return null;
        var symboles = resultats.EnumerateArray()
            .Select(r => r.TryGetProperty("symbol", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null)
            .OfType<string>()
            .ToList();
        foreach (var place in PlacesEuro)
            if (symboles.FirstOrDefault(s => s.EndsWith(place, StringComparison.OrdinalIgnoreCase)) is { } enEuro)
                return enEuro;
        return symboles.FirstOrDefault();
    }

    /// <summary>Dernier cours, devise et date d'une réponse « chart » de Yahoo Finance.</summary>
    public static (decimal Valeur, string Devise, DateTime Date)? LireCours(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("chart", out var chart)
            || !chart.TryGetProperty("result", out var resultats) || resultats.ValueKind != JsonValueKind.Array
            || resultats.GetArrayLength() == 0
            || !resultats[0].TryGetProperty("meta", out var meta)
            || !meta.TryGetProperty("regularMarketPrice", out var prix) || !prix.TryGetDecimal(out var valeur) || valeur <= 0)
            return null;
        var devise = meta.TryGetProperty("currency", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : "EUR";
        var date = meta.TryGetProperty("regularMarketTime", out var t) && t.TryGetInt64(out var secondes)
            ? DateTimeOffset.FromUnixTimeSeconds(secondes).LocalDateTime
            : DateTime.Now;
        return (valeur, devise, date);
    }

}
