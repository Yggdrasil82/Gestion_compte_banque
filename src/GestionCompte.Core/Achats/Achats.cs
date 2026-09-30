using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GestionCompte.Core.Achats;

/// <summary>Site marchand de la liste modifiable : l'adresse contient {recherche}, remplacé par les mots cherchés.</summary>
public sealed class SiteRecherche
{
    public const string Marqueur = "{recherche}";

    public string Nom { get; set; } = "";
    public string Adresse { get; set; } = "";
    public bool Actif { get; set; } = true;

    public string AdressePour(string recherche) => Adresse.Replace(Marqueur, Uri.EscapeDataString(recherche.Trim()));

    public static List<SiteRecherche> ParDefaut() => new()
    {
        new() { Nom = "Amazon", Adresse = "https://www.amazon.fr/s?k={recherche}" },
        new() { Nom = "Cdiscount", Adresse = "https://www.cdiscount.com/search/10/{recherche}.html" },
        new() { Nom = "Fnac", Adresse = "https://www.fnac.com/SearchResult/ResultList.aspx?Search={recherche}" },
        new() { Nom = "Darty", Adresse = "https://www.darty.com/nav/recherche?text={recherche}" },
        new() { Nom = "Boulanger", Adresse = "https://www.boulanger.com/resultats?tr={recherche}" },
        new() { Nom = "Leboncoin", Adresse = "https://www.leboncoin.fr/recherche?text={recherche}" },
        new() { Nom = "idealo", Adresse = "https://www.idealo.fr/prechcat.html?q={recherche}" },
        new() { Nom = "LeDénicheur", Adresse = "https://ledenicheur.fr/search?search={recherche}" },
    };
}

/// <summary>Prix relevé sur la page d'un produit suivi (ou erreur de lecture).</summary>
public sealed class RelevePrix
{
    public DateTime Date { get; set; }
    public decimal? Prix { get; set; }
    public string? Erreur { get; set; }
}

/// <summary>Produit dont l'application relit le prix à chaque ouverture.</summary>
public sealed class ProduitSuivi
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nom { get; set; } = "";
    public string Adresse { get; set; } = "";
    public decimal? PrixCible { get; set; }
    public List<RelevePrix> Releves { get; set; } = new();

    /// <summary>Nombre de relevés gardés par produit.</summary>
    public const int RelevesMaximum = 120;

    [System.Text.Json.Serialization.JsonIgnore]
    public decimal? DernierPrix => Releves.LastOrDefault(r => r.Prix is not null)?.Prix;

    /// <summary>Prix du relevé réussi précédant le dernier (pour afficher la baisse ou la hausse).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal? PrixPrecedent => Releves.Where(r => r.Prix is not null).SkipLast(1).LastOrDefault()?.Prix;

    [System.Text.Json.Serialization.JsonIgnore]
    public decimal? PlusBas => Releves.Where(r => r.Prix is not null).Min(r => r.Prix);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool CibleAtteinte => PrixCible is { } cible && DernierPrix is { } prix && prix <= cible;

    public void Noter(RelevePrix releve)
    {
        // Un seul relevé par jour : le plus récent remplace celui du même jour.
        var dernier = Releves.LastOrDefault();
        if (dernier is not null && dernier.Date.Date == releve.Date.Date)
            Releves.RemoveAt(Releves.Count - 1);
        Releves.Add(releve);
        if (Releves.Count > RelevesMaximum)
            Releves.RemoveRange(0, Releves.Count - RelevesMaximum);
    }
}

public enum SourceOffre
{
    Gemini,
    Groq,
}

public enum VerificationOffre
{
    /// <summary>Page non relue (lecture impossible ou bloquée par le site).</summary>
    AVerifier,

    /// <summary>Prix relu sur la page, identique à celui donné par l'IA.</summary>
    Verifie,

    /// <summary>Prix relu sur la page, différent de celui donné par l'IA : c'est celui de la page qui est gardé.</summary>
    Corrige,
}

/// <summary>Offre trouvée par une IA (et éventuellement vérifiée sur la page).</summary>
public sealed class OffreTrouvee
{
    public string Site { get; set; } = "";
    public string Titre { get; set; } = "";
    public decimal Prix { get; set; }
    public string Adresse { get; set; } = "";
    public string? Remarque { get; set; }
    public HashSet<SourceOffre> Sources { get; set; } = new();
    public VerificationOffre Verification { get; set; } = VerificationOffre.AVerifier;
}

public static partial class LecturePrix
{
    /// <summary>
    /// Prix d'une page produit, d'après les données structurées (JSON-LD « Product/Offer », microdonnées itemprop="price",
    /// balises « product:price:amount »). Null si la page n'en donne pas.
    /// </summary>
    public static decimal? Extraire(string html)
    {
        foreach (Match bloc in JsonLd().Matches(html))
        {
            try
            {
                using var json = JsonDocument.Parse(WebUtility.HtmlDecode(bloc.Groups[1].Value).Trim(),
                    new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                if (PrixJson(json.RootElement, 0) is { } prix)
                    return prix;
            }
            catch (JsonException)
            {
                // Bloc mal formé : on essaie les suivants.
            }
        }

        foreach (var motif in new[] { MetaPrix(), ItemPropPrix() })
            foreach (Match m in motif.Matches(html))
                if (Nombre(WebUtility.HtmlDecode(m.Groups[1].Value)) is { } prix)
                    return prix;
        return null;
    }

    /// <summary>Titre de la page (og:title, sinon balise title), pour nommer un produit suivi.</summary>
    public static string? Titre(string html)
    {
        var m = OgTitre().Match(html);
        if (!m.Success)
            m = BaliseTitre().Match(html);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value).Trim() is { Length: > 0 } titre ? titre : null : null;
    }

    private static decimal? PrixJson(JsonElement element, int profondeur)
    {
        if (profondeur > 8)
            return null;
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var e in element.EnumerateArray())
                    if (PrixJson(e, profondeur + 1) is { } p)
                        return p;
                return null;
            case JsonValueKind.Object:
                // Offre : price, sinon lowPrice (AggregateOffer).
                if (element.TryGetProperty("offers", out var offres) && PrixJson(offres, profondeur + 1) is { } prixOffre)
                    return prixOffre;
                foreach (var nom in new[] { "price", "lowPrice" })
                    if (element.TryGetProperty(nom, out var valeur) && Valeur(valeur) is { } p)
                        return p;
                foreach (var nom in new[] { "@graph", "mainEntity", "itemListElement", "item" })
                    if (element.TryGetProperty(nom, out var enfant) && PrixJson(enfant, profondeur + 1) is { } p)
                        return p;
                return null;
            default:
                return null;
        }
    }

    private static decimal? Valeur(JsonElement valeur) => valeur.ValueKind switch
    {
        JsonValueKind.Number when valeur.TryGetDecimal(out var d) && d > 0 => d,
        JsonValueKind.String => Nombre(valeur.GetString()),
        _ => null,
    };

    /// <summary>« 1 299,99 € », « 1299.99 », « 1.299,99 » → 1299,99.</summary>
    public static decimal? Nombre(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte))
            return null;
        var t = new string(texte.Where(c => char.IsDigit(c) || c is ',' or '.').ToArray());
        if (t.Length == 0)
            return null;
        var virgule = t.LastIndexOf(',');
        var point = t.LastIndexOf('.');
        var separateur = Math.Max(virgule, point);
        // Séparateur décimal : le dernier, s'il est suivi d'une ou deux décimales.
        if (separateur >= 0 && t.Length - separateur - 1 is 1 or 2)
            t = t[..separateur].Replace(",", "").Replace(".", "") + "." + t[(separateur + 1)..];
        else
            t = t.Replace(",", "").Replace(".", "");
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var prix) && prix > 0 ? prix : null;
    }

    [GeneratedRegex(@"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex JsonLd();

    [GeneratedRegex(@"<meta[^>]+(?:property|name)\s*=\s*[""'](?:product:price:amount|og:price:amount)[""'][^>]*content\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex MetaPrix();

    [GeneratedRegex(@"itemprop\s*=\s*[""']price[""'][^>]*content\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ItemPropPrix();

    [GeneratedRegex(@"<meta[^>]+property\s*=\s*[""']og:title[""'][^>]*content\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex OgTitre();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BaliseTitre();
}

public static class FusionOffres
{
    /// <summary>
    /// Regroupe les offres des deux IA : même page (adresse sans paramètres) ou même site au même prix = une seule offre,
    /// avec les deux sources. Les offres sont triées du moins cher au plus cher.
    /// </summary>
    public static List<OffreTrouvee> Fusionner(IEnumerable<OffreTrouvee> offres)
    {
        var resultat = new List<OffreTrouvee>();
        foreach (var offre in offres.Where(o => o.Prix > 0))
        {
            var double_ = resultat.FirstOrDefault(r =>
                (Cle(r.Adresse) is { } a && a == Cle(offre.Adresse))
                || (Domaine(r.Adresse) is { } d && d == Domaine(offre.Adresse) && r.Prix == offre.Prix));
            if (double_ is null)
            {
                resultat.Add(offre);
                continue;
            }
            double_.Sources.UnionWith(offre.Sources);
            if (string.IsNullOrWhiteSpace(double_.Remarque))
                double_.Remarque = offre.Remarque;
        }
        return resultat.OrderBy(o => o.Prix).ThenBy(o => o.Site, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Adresse sans paramètres ni « www. », pour reconnaître la même page.</summary>
    public static string? Cle(string adresse) =>
        Uri.TryCreate(adresse, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? $"{Domaine(adresse)}{uri.AbsolutePath.TrimEnd('/')}".ToLowerInvariant()
            : null;

    public static string? Domaine(string adresse) =>
        Uri.TryCreate(adresse, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host).ToLowerInvariant()
            : null;
}
