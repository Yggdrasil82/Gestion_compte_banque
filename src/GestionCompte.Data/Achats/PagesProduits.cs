using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using GestionCompte.Core.Achats;

namespace GestionCompte.Data.Achats;

public sealed record PageProduit(decimal? Prix, string? Titre, string AdresseFinale);

/// <summary>Lecture du prix affiché sur la page d'un produit (données structurées de la page).</summary>
public sealed class LecteurPages
{
    private readonly HttpClient _http;

    public LecteurPages(HttpClient http) => _http = http;

    public async Task<PageProduit> LireAsync(string adresse, CancellationToken annulation = default)
    {
        if (!Uri.TryCreate(adresse, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Adresse de page incorrecte (elle doit commencer par https://).");

        using var requete = new HttpRequestMessage(HttpMethod.Get, uri);
        requete.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        requete.Headers.AcceptLanguage.ParseAdd("fr-FR,fr;q=0.9");
        requete.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
        using var reponse = await _http.SendAsync(requete, HttpCompletionOption.ResponseHeadersRead, annulation);
        if (reponse.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Le site bloque la lecture automatique : ouvrez la page pour voir le prix.");
        if (!reponse.IsSuccessStatusCode)
            throw new HttpRequestException($"Page inaccessible ({(int)reponse.StatusCode}).");

        // Pages très lourdes : les 3 premiers Mo suffisent (les données structurées sont en tête).
        await using var flux = await reponse.Content.ReadAsStreamAsync(annulation);
        using var lecteur = new StreamReader(flux);
        var tampon = new char[3 * 1024 * 1024];
        var lus = await lecteur.ReadBlockAsync(tampon.AsMemory(), annulation);
        var html = new string(tampon, 0, lus);
        return new PageProduit(LecturePrix.Extraire(html), LecturePrix.Titre(html),
            reponse.RequestMessage?.RequestUri?.ToString() ?? adresse);
    }

    /// <summary>
    /// Relit le prix des offres trouvées par les IA (4 pages à la fois, 20 secondes au plus par page) :
    /// l'adresse finale remplace les liens de redirection, et le prix de la page remplace celui de l'IA s'il diffère.
    /// </summary>
    public async Task VerifierAsync(IEnumerable<OffreTrouvee> offres, CancellationToken annulation = default)
    {
        using var limite = new SemaphoreSlim(4);
        await Task.WhenAll(offres.Select(async offre =>
        {
            await limite.WaitAsync(annulation);
            try
            {
                using var delai = CancellationTokenSource.CreateLinkedTokenSource(annulation);
                delai.CancelAfter(TimeSpan.FromSeconds(20));
                var page = await LireAsync(offre.Adresse, delai.Token);
                offre.Adresse = page.AdresseFinale;
                if (page.Prix is { } prix)
                {
                    offre.Verification = Math.Abs(prix - offre.Prix) <= Math.Max(0.01m, offre.Prix * 0.01m)
                        ? VerificationOffre.Verifie
                        : VerificationOffre.Corrige;
                    offre.Prix = prix;
                }
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or ArgumentException or IOException)
            {
                // Reste « à vérifier ».
            }
            finally
            {
                limite.Release();
            }
        }));
    }
}

/// <summary>Sites de recherche et produits suivis, communs à tous les comptes (« achats.json » dans le dossier des données).</summary>
public sealed class FichierAchats
{
    public const string NomFichier = "achats.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _chemin;

    public FichierAchats(string dossier) => _chemin = Path.Combine(dossier, NomFichier);

    public List<SiteRecherche> Sites { get; private set; } = SiteRecherche.ParDefaut();
    public List<ProduitSuivi> Produits { get; private set; } = new();

    public void Charger()
    {
        if (!File.Exists(_chemin))
            return;
        var contenu = JsonSerializer.Deserialize<Contenu>(File.ReadAllText(_chemin), Options)
            ?? throw new InvalidDataException("La liste des achats est illisible.");
        Sites = contenu.Sites ?? SiteRecherche.ParDefaut();
        Produits = contenu.Produits ?? new List<ProduitSuivi>();
    }

    public void Enregistrer()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_chemin)!);
        var temporaire = _chemin + ".tmp";
        File.WriteAllText(temporaire, JsonSerializer.Serialize(new Contenu(1, Sites, Produits), Options));
        File.Move(temporaire, _chemin, true);
    }

    private sealed record Contenu(int Version, List<SiteRecherche>? Sites, List<ProduitSuivi>? Produits);
}
