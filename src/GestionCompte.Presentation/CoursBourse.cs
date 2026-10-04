using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GestionCompte.Presentation;

/// <summary>Cours trouvé sur internet, converti en euros.</summary>
public sealed record CoursTrouve(string Symbole, decimal Valeur, DateTime Date);

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
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    /// <summary>Dernier cours du titre ; <paramref name="symbole"/> (« CW8.PA ») évite la recherche par ISIN.</summary>
    public virtual async Task<CoursTrouve?> ChercherAsync(string isin, string? symbole, CancellationToken annulation = default)
    {
        symbole ??= ChoisirSymbole(await LireAsync($"https://query2.finance.yahoo.com/v1/finance/search?q={Uri.EscapeDataString(isin)}&quotesCount=10&newsCount=0", annulation));
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
        return new CoursTrouve(symbole, Math.Round(valeur, 4), date);
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
