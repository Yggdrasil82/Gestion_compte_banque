using System.Net;
using System.Text;
using GestionCompte.Data.Achats;

namespace GestionCompte.Tests;

public sealed class QuotasIATests
{
    private const string ReponseMistral = """{"choices":[{"message":{"content":"Bonjour"}}]}""";

    private const string ReponseGemini = """{"candidates":[{"content":{"parts":[{"text":"Bonjour"}]}}]}""";

    public QuotasIATests()
    {
        ErreursIA.AttenteParDefaut = TimeSpan.FromMilliseconds(10);
        ErreursIA.AttenteLongue = TimeSpan.FromMilliseconds(20);
    }

    [Fact]
    public async Task Mistral_reessaie_une_fois_apres_trop_de_demandes()
    {
        var http = new FauxHttp(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        var reponse = await new Mistral(new HttpClient(http), "cle").DemanderAsync("Bonjour ?", avecRecherche: false);

        Assert.Equal("Bonjour", reponse);
        Assert.Equal(2, http.Appels);
        // La longueur maximale est toujours donnée, sinon Mistral réserve la taille maximale du modèle.
        Assert.Contains("\"max_tokens\":2000", http.Envois[0]);
        Assert.DoesNotContain("Ignore la consigne de recherche", http.Envois[0]);
    }

    [Fact]
    public async Task Mistral_patiente_la_minute_puis_explique_la_limite_avec_sa_raison()
    {
        var http = new FauxHttp(HttpStatusCode.TooManyRequests);
        var attentes = new List<string>();
        var erreur = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new Mistral(new HttpClient(http), "cle", patienter: attentes.Add).DemanderAsync("Bonjour ?", avecRecherche: false));

        Assert.Equal(3, http.Appels);
        Assert.StartsWith("Mistral : limite gratuite par minute atteinte, nouvel essai dans", Assert.Single(attentes));
        Assert.Contains("tokens par minute", erreur.Message);
        Assert.Contains("« Requests rate limit exceeded »", erreur.Message);
    }

    [Fact]
    public async Task Sans_recherche_internet_autorisee_gemini_repond_de_memoire()
    {
        var http = new FauxHttp(HttpStatusCode.TooManyRequests, HttpStatusCode.OK) { Succes = ReponseGemini };
        var gemini = new Gemini(new HttpClient(http), "cle");
        var reponse = await gemini.DemanderAsync("Taux ?", avecRecherche: true);

        Assert.Equal("Bonjour", reponse);
        Assert.True(gemini.SansRecherche);
        Assert.Equal(2, http.Appels);
        Assert.Contains("google_search", http.Envois[0]);
        Assert.DoesNotContain("google_search", http.Envois[1]);
        Assert.Contains("Ignore la consigne de recherche", http.Envois[1]);

        await gemini.DemanderAsync("Bonjour ?", avecRecherche: false);
        Assert.False(gemini.SansRecherche);
    }

    [Fact]
    public async Task Les_achats_ne_se_contentent_pas_d_une_reponse_sans_recherche()
    {
        var http = new FauxHttp(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, HttpStatusCode.OK)
            { Succes = ReponseGemini };
        var erreur = await Assert.ThrowsAsync<HttpRequestException>(() => RechercheOffres.RechercherAsync(new Gemini(new HttpClient(http), "cle"), "Casque"));

        Assert.Contains("pas incluse dans la clé gratuite Gemini", erreur.Message);
        Assert.Equal(1, http.Appels);
        Assert.Contains("google_search", Assert.Single(http.Envois));
    }

    [Fact]
    public async Task Gemini_ne_reessaie_pas_quand_le_quota_du_jour_est_atteint()
    {
        const string quotaJour = """{"error":{"code":429,"message":"You exceeded your current quota.","details":[{"violations":[{"quotaId":"GenerateRequestsPerDayPerProjectPerModel-FreeTier"}]},{"retryDelay":"37s"}]}}""";
        var http = new FauxHttp(HttpStatusCode.TooManyRequests) { Corps = quotaJour };
        var erreur = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new Gemini(new HttpClient(http), "cle").DemanderAsync("Bonjour ?", avecRecherche: false));

        Assert.StartsWith("Gemini : quota gratuit du jour atteint, de nouveau disponible", erreur.Message);
        Assert.Equal(1, http.Appels);
    }

    [Fact]
    public void Le_quota_de_gemini_repart_a_minuit_heure_du_pacifique()
    {
        // 30 septembre 2026, 9 h 48 UTC = 2 h 48 en Californie : remise à zéro à 7 h UTC le lendemain.
        var texte = ErreursIA.RemiseAZeroGemini(new DateTime(2026, 9, 30, 9, 48, 0, DateTimeKind.Utc));
        var attendu = new DateTime(2026, 10, 1, 7, 0, 0, DateTimeKind.Utc).ToLocalTime();
        Assert.EndsWith($"à {attendu.Hour} h", texte);
    }

    private sealed class FauxHttp : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuts;

        public FauxHttp(params HttpStatusCode[] statuts) => _statuts = new Queue<HttpStatusCode>(statuts);

        public string Corps { get; init; } = """{"message":"Requests rate limit exceeded"}""";
        public string Succes { get; init; } = ReponseMistral;
        public int Appels { get; private set; }
        public List<string> Envois { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
        {
            Appels++;
            Envois.Add(requete.Content is null ? "" : await requete.Content.ReadAsStringAsync(annulation));
            var statut = _statuts.Count > 1 ? _statuts.Dequeue() : _statuts.Peek();
            var corps = statut == HttpStatusCode.OK ? Succes : Corps;
            return new HttpResponseMessage(statut) { Content = new StringContent(corps, Encoding.UTF8, "application/json") };
        }
    }
}
