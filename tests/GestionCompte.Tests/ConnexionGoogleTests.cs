using System.Net;
using System.Net.Sockets;
using System.Text;
using GestionCompte.Data.Documents;

namespace GestionCompte.Tests;

public sealed class ConnexionGoogleTests
{
    [Fact]
    public async Task Une_connexion_muette_du_navigateur_ne_bloque_pas_le_retour_de_google()
    {
        var http = new HttpClient(new FauxJeton());
        var connexion = new ConnexionGoogle(http, new IdentifiantsGoogle("id", "secret"), null);
        var ouverte = new TaskCompletionSource<string>();

        var connecter = connexion.ConnecterAsync(ouverte.SetResult);
        var adresse = await ouverte.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var parametres = adresse[(adresse.IndexOf('?') + 1)..].Split('&')
            .Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        var retour = new Uri(parametres["redirect_uri"]);

        // Connexion ouverte à l'avance par le navigateur, sans rien envoyer.
        using var muette = new TcpClient();
        await muette.ConnectAsync(IPAddress.Loopback, retour.Port);

        using var navigateur = new TcpClient();
        await navigateur.ConnectAsync(IPAddress.Loopback, retour.Port);
        var flux = navigateur.GetStream();
        await flux.WriteAsync(Encoding.ASCII.GetBytes($"GET /?state={parametres["state"]}&code=abc HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n"));
        var page = await new StreamReader(flux).ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10));

        await connecter.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("Connexion réussie", page);
        Assert.Equal("jeton-long", connexion.JetonRenouvellement);
    }

    [Fact]
    public async Task Google_muet_a_la_validation_du_code_donne_une_erreur_qui_nomme_l_etape()
    {
        var http = new HttpClient(new GoogleMuet()) { Timeout = TimeSpan.FromSeconds(1) };
        var connexion = new ConnexionGoogle(http, new IdentifiantsGoogle("id", "secret"), null);
        var ouverte = new TaskCompletionSource<string>();
        var etapes = new List<string>();

        var connecter = connexion.ConnecterAsync(ouverte.SetResult, default, etapes.Add);
        var adresse = await ouverte.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var parametres = adresse[(adresse.IndexOf('?') + 1)..].Split('&')
            .Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        using var navigateur = new TcpClient();
        await navigateur.ConnectAsync(IPAddress.Loopback, new Uri(parametres["redirect_uri"]).Port);
        await navigateur.GetStream().WriteAsync(Encoding.ASCII.GetBytes($"GET /?state={parametres["state"]}&code=abc HTTP/1.1\r\n\r\n"));

        var erreur = await Assert.ThrowsAsync<TimeoutException>(() => connecter.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("validation du code", erreur.Message);
        Assert.Contains("Code reçu de Google, validation…", etapes);
        Assert.Contains("Étape : Code reçu", GestionCompte.Presentation.CompteGoogle.MessageEchec(etapes[^1], erreur, TimeSpan.FromSeconds(75)));
    }

    private sealed class GoogleMuet : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
        {
            await Task.Delay(Timeout.Infinite, annulation);
            throw new InvalidOperationException();
        }
    }

    private sealed class FauxJeton : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"a","refresh_token":"jeton-long","scope":"email","expires_in":3600}""",
                    Encoding.UTF8, "application/json"),
            });
    }
}
