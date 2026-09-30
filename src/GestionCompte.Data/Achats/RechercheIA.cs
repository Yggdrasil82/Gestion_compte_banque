using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GestionCompte.Core.Achats;

namespace GestionCompte.Data.Achats;

/// <summary>IA gratuite interrogée par l'application (clé personnelle saisie dans l'application).</summary>
public interface IAssistantIA
{
    SourceOffre Source { get; }

    string Nom { get; }

    /// <summary>Réponse texte à une demande ; <paramref name="avecRecherche"/> : l'IA peut chercher sur internet.</summary>
    Task<string> DemanderAsync(string demande, bool avecRecherche, CancellationToken annulation = default);
}

public static class RechercheOffres
{
    /// <summary>Demande envoyée aux IA : seul le nom du produit cherché part, aucune donnée du budget.</summary>
    public static string Demande(string produit) =>
        "Recherche sur internet les offres actuelles pour acheter ce produit en France : « " + produit.Trim() + " ».\n" +
        "Donne jusqu'à 8 offres de sites marchands différents, prix TTC en euros, livrables en France, du moins cher au plus cher.\n" +
        "Réponds uniquement avec un tableau JSON, sans aucun texte autour, au format :\n" +
        "[{\"site\":\"Nom du site\",\"titre\":\"Nom exact du produit\",\"prix\":123.45,\"url\":\"https://…\",\"remarque\":\"frais de port, occasion, reconditionné, stock…\"}]\n" +
        "N'invente aucune offre ni adresse : n'inclus que des pages trouvées pendant la recherche. Si le produit est d'occasion ou reconditionné, dis-le dans la remarque.";

    public static async Task<IReadOnlyList<OffreTrouvee>> RechercherAsync(IAssistantIA ia, string produit, CancellationToken annulation = default) =>
        Extraire(await ia.DemanderAsync(Demande(produit), avecRecherche: true, annulation), ia.Source);

    /// <summary>Offres du tableau JSON contenu dans la réponse (même entouré de texte ou de ```json).</summary>
    public static IReadOnlyList<OffreTrouvee> Extraire(string reponse, SourceOffre source)
    {
        var debut = reponse.IndexOf('[');
        var fin = reponse.LastIndexOf(']');
        if (debut < 0 || fin <= debut)
            return Array.Empty<OffreTrouvee>();

        JsonNode? tableau;
        try
        {
            tableau = JsonNode.Parse(reponse[debut..(fin + 1)], documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            return Array.Empty<OffreTrouvee>();
        }

        var offres = new List<OffreTrouvee>();
        foreach (var element in tableau as JsonArray ?? new JsonArray())
        {
            if (element is not JsonObject o)
                continue;
            var adresse = Texte(o, "url") ?? Texte(o, "lien") ?? "";
            var prix = o["prix"] is JsonValue v
                ? v.TryGetValue<decimal>(out var d) ? d : LecturePrix.Nombre(v.ToString())
                : null;
            if (prix is not > 0 || FusionOffres.Domaine(adresse) is not { } domaine)
                continue;
            offres.Add(new OffreTrouvee
            {
                Site = Texte(o, "site") ?? domaine,
                Titre = Texte(o, "titre") ?? "",
                Prix = decimal.Round(prix.Value, 2),
                Adresse = adresse,
                Remarque = Texte(o, "remarque"),
                Sources = { source },
            });
        }
        return offres;
    }

    private static string? Texte(JsonObject o, string nom) =>
        o[nom] is JsonValue v && v.ToString().Trim() is { Length: > 0 } t ? t : null;
}

/// <summary>Gemini (Google AI Studio) : la recherche Google est intégrée à la réponse.</summary>
public sealed class Gemini : IAssistantIA
{
    public const string ModeleParDefaut = "gemini-flash-latest";

    private readonly HttpClient _http;
    private readonly string _cle;
    private readonly string _modele;

    public Gemini(HttpClient http, string cle, string? modele = null)
    {
        _http = http;
        _cle = cle;
        _modele = string.IsNullOrWhiteSpace(modele) ? ModeleParDefaut : modele.Trim();
    }

    public SourceOffre Source => SourceOffre.Gemini;
    public string Nom => "Gemini";

    public async Task<string> DemanderAsync(string demande, bool avecRecherche, CancellationToken annulation = default)
    {
        var corps = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = demande }),
            }),
        };
        if (avecRecherche)
            corps["tools"] = new JsonArray(new JsonObject { ["google_search"] = new JsonObject() });

        using var requete = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(_modele)}:generateContent")
        {
            Content = new StringContent(corps.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        requete.Headers.Add("x-goog-api-key", _cle);
        using var reponse = await _http.SendAsync(requete, annulation);
        var texte = await reponse.Content.ReadAsStringAsync(annulation);
        if (!reponse.IsSuccessStatusCode)
            throw new HttpRequestException(ErreursIA.Message(Nom, reponse.StatusCode, texte));

        using var json = JsonDocument.Parse(texte);
        var resultat = new StringBuilder();
        if (json.RootElement.TryGetProperty("candidates", out var candidats) && candidats.GetArrayLength() > 0
            && candidats[0].TryGetProperty("content", out var contenu) && contenu.TryGetProperty("parts", out var parties))
        {
            foreach (var partie in parties.EnumerateArray())
                if (partie.TryGetProperty("text", out var t))
                    resultat.Append(t.GetString());
        }
        return resultat.ToString();
    }
}

/// <summary>Mistral (La Plateforme) : recherche web par l'outil « web_search » des conversations.</summary>
public sealed class Mistral : IAssistantIA
{
    public const string ModeleParDefaut = "mistral-medium-latest";

    private readonly HttpClient _http;
    private readonly string _cle;
    private readonly string _modele;

    public Mistral(HttpClient http, string cle, string? modele = null)
    {
        _http = http;
        _cle = cle;
        _modele = string.IsNullOrWhiteSpace(modele) ? ModeleParDefaut : modele.Trim();
    }

    public SourceOffre Source => SourceOffre.Mistral;
    public string Nom => "Mistral";

    public async Task<string> DemanderAsync(string demande, bool avecRecherche, CancellationToken annulation = default)
    {
        JsonObject corps;
        string adresse;
        if (avecRecherche)
        {
            adresse = "https://api.mistral.ai/v1/conversations";
            corps = new JsonObject
            {
                ["model"] = _modele,
                ["inputs"] = demande,
                ["tools"] = new JsonArray(new JsonObject { ["type"] = "web_search" }),
                ["store"] = false,
            };
        }
        else
        {
            adresse = "https://api.mistral.ai/v1/chat/completions";
            corps = new JsonObject
            {
                ["model"] = _modele,
                ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = demande }),
            };
        }

        using var requete = new HttpRequestMessage(HttpMethod.Post, adresse)
        {
            Content = new StringContent(corps.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cle);
        using var reponse = await _http.SendAsync(requete, annulation);
        var texte = await reponse.Content.ReadAsStringAsync(annulation);
        if (!reponse.IsSuccessStatusCode)
            throw new HttpRequestException(ErreursIA.Message(Nom, reponse.StatusCode, texte));

        using var json = JsonDocument.Parse(texte);
        var resultat = new StringBuilder();
        if (json.RootElement.TryGetProperty("outputs", out var sorties))
        {
            foreach (var sortie in sorties.EnumerateArray())
                if (sortie.TryGetProperty("type", out var type) && type.GetString() == "message.output"
                    && sortie.TryGetProperty("content", out var contenu))
                    AjouterContenu(contenu, resultat);
        }
        else if (json.RootElement.TryGetProperty("choices", out var choix) && choix.GetArrayLength() > 0
                 && choix[0].TryGetProperty("message", out var message) && message.TryGetProperty("content", out var contenu))
            AjouterContenu(contenu, resultat);
        return resultat.ToString();
    }

    /// <summary>Le contenu est un texte, ou une liste de morceaux (texte et références des pages consultées).</summary>
    private static void AjouterContenu(JsonElement contenu, StringBuilder resultat)
    {
        if (contenu.ValueKind == JsonValueKind.String)
            resultat.Append(contenu.GetString());
        else if (contenu.ValueKind == JsonValueKind.Array)
            foreach (var morceau in contenu.EnumerateArray())
                if (morceau.TryGetProperty("type", out var t) && t.GetString() == "text" && morceau.TryGetProperty("text", out var texte))
                    resultat.Append(texte.GetString());
    }
}

public static class ErreursIA
{
    public static string Message(string ia, HttpStatusCode statut, string texte)
    {
        var detail = texte;
        try
        {
            using var json = JsonDocument.Parse(texte);
            var racine = json.RootElement;
            if (racine.TryGetProperty("error", out var erreur))
                racine = erreur;
            if (racine.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                detail = message.GetString() ?? texte;
        }
        catch (JsonException)
        {
        }
        if (detail.Length > 200)
            detail = detail[..200] + "…";

        if (detail.Contains("API key", StringComparison.OrdinalIgnoreCase))
            statut = HttpStatusCode.Unauthorized;
        return statut switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"{ia} refuse la clé API (vérifiez-la dans Configuration › Intelligence artificielle). {detail}",
            HttpStatusCode.TooManyRequests => $"{ia} : limite gratuite atteinte pour le moment, réessayez plus tard.",
            HttpStatusCode.NotFound => $"{ia} : modèle introuvable ({detail}).",
            _ => $"{ia} a refusé la demande ({(int)statut}) : {detail}",
        };
    }
}
