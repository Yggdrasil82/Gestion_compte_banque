using System.Net;
using System.Text;
using GestionCompte.Data.Achats;

namespace GestionCompte.Tests;

public sealed class QuotasIATests
{
    private const string ReponseMistral = """{"choices":[{"message":{"content":"Bonjour"}}]}""";

    public QuotasIATests() => ErreursIA.AttenteParDefaut = TimeSpan.FromMilliseconds(10);

    [Fact]
    public async Task Mistral_reessaie_une_fois_apres_trop_de_demandes()
    {
        var http = new FauxHttp(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        var reponse = await new Mistral(new HttpClient(http), "cle").DemanderAsync("Bonjour ?", avecRecherche: false);

        Assert.Equal("Bonjour", reponse);
        Assert.Equal(2, http.Appels);
    }

    [Fact]
    public async Task Mistral_explique_la_limite_si_le_nouvel_essai_echoue_aussi()
    {
        var http = new FauxHttp(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        var erreur = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new Mistral(new HttpClient(http), "cle").DemanderAsync("Bonjour ?", avecRecherche: false));

        Assert.Contains("1 par seconde", erreur.Message);
        Assert.Equal(2, http.Appels);
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
        public int Appels { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
        {
            Appels++;
            var statut = _statuts.Count > 1 ? _statuts.Dequeue() : _statuts.Peek();
            var corps = statut == HttpStatusCode.OK ? ReponseMistral : Corps;
            return Task.FromResult(new HttpResponseMessage(statut) { Content = new StringContent(corps, Encoding.UTF8, "application/json") });
        }
    }
}
