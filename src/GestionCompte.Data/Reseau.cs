using System.Net;
using System.Net.Sockets;

namespace GestionCompte.Data;

/// <summary>
/// Clients HTTP de l'application. Certaines connexions IPv6 (box, réseau d'entreprise) acceptent la résolution du nom mais
/// ne laissent rien passer : sans précaution, la demande attend jusqu'au délai maximum. Ici, les adresses IPv4 sont essayées
/// en premier et chaque adresse a quelques secondes pour répondre avant de passer à la suivante.
/// </summary>
public static class Reseau
{
    /// <summary>Temps laissé à une adresse pour accepter la connexion.</summary>
    public static readonly TimeSpan DelaiConnexion = TimeSpan.FromSeconds(8);

    public static HttpClient Client(TimeSpan delai) => new(Gestionnaire()) { Timeout = delai };

    public static SocketsHttpHandler Gestionnaire() => new()
    {
        ConnectCallback = ConnecterAsync,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    private static async ValueTask<Stream> ConnecterAsync(SocketsHttpConnectionContext contexte, CancellationToken annulation)
    {
        var hote = contexte.DnsEndPoint.Host;
        var adresses = await Dns.GetHostAddressesAsync(hote, annulation);
        Exception? derniere = null;
        foreach (var adresse in OrdreDEssai(adresses))
        {
            var socket = new Socket(adresse.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            using var delai = CancellationTokenSource.CreateLinkedTokenSource(annulation);
            delai.CancelAfter(DelaiConnexion);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(adresse, contexte.DnsEndPoint.Port), delai.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception e) when (e is SocketException || (e is OperationCanceledException && !annulation.IsCancellationRequested))
            {
                socket.Dispose();
                derniere = e;
            }
        }
        throw new HttpRequestException($"Impossible de joindre {hote} (aucune adresse n'a répondu).", derniere);
    }

    /// <summary>IPv4 d'abord, puis IPv6.</summary>
    public static IEnumerable<IPAddress> OrdreDEssai(IEnumerable<IPAddress> adresses) =>
        adresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1);
}
