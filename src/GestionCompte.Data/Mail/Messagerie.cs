using System.Net.Http.Headers;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace GestionCompte.Data.Mail;

public sealed record PieceJointe(string Nom, byte[] Contenu);

public sealed record MessageMail(
    IReadOnlyList<string> Destinataires,
    IReadOnlyList<string> Copies,
    string Objet,
    string Corps,
    IReadOnlyList<PieceJointe> PiecesJointes);

public interface IEnvoiMail
{
    /// <summary>Ex. « Gmail (romain@gmail.com) » ou « SMTP smtp.orange.fr ».</summary>
    string Description { get; }

    Task EnvoyerAsync(MessageMail message, CancellationToken annulation = default);
}

public enum SecuriteSmtp
{
    /// <summary>Port 465 : connexion chiffrée dès le départ.</summary>
    Ssl,

    /// <summary>Port 587 : chiffrement négocié (STARTTLS).</summary>
    StartTls,
}

/// <summary>Réglages d'un serveur d'envoi (le mot de passe est gardé à part, chiffré par Windows).</summary>
public sealed record ReglagesSmtp(string Serveur, int Port, SecuriteSmtp Securite, string Identifiant, string Expediteur, string NomAffiche)
{
    public string AdresseExpediteur => string.IsNullOrWhiteSpace(Expediteur) ? Identifiant : Expediteur;
}

/// <summary>Serveurs d'envoi courants, pour pré-remplir les réglages.</summary>
public sealed record ServeurConnu(string Nom, string Serveur, int Port, SecuriteSmtp Securite)
{
    public static IReadOnlyList<ServeurConnu> Liste { get; } = new ServeurConnu[]
    {
        new("Orange", "smtp.orange.fr", 465, SecuriteSmtp.Ssl),
        new("Free", "smtp.free.fr", 465, SecuriteSmtp.Ssl),
        new("SFR", "smtp.sfr.fr", 465, SecuriteSmtp.Ssl),
        new("Bouygues Telecom", "smtp.bbox.fr", 587, SecuriteSmtp.StartTls),
        new("La Poste", "smtp.laposte.net", 465, SecuriteSmtp.Ssl),
        new("Yahoo", "smtp.mail.yahoo.com", 465, SecuriteSmtp.Ssl),
        new("Gmail (mot de passe d'application)", "smtp.gmail.com", 465, SecuriteSmtp.Ssl),
    };

    public override string ToString() => Nom;
}

public static class Messages
{
    /// <summary>Message au format standard des mails (MIME), texte simple et pièces jointes.</summary>
    public static MimeMessage Construire(MessageMail message, string? expediteur, string? nomAffiche)
    {
        var mime = new MimeMessage();
        if (!string.IsNullOrWhiteSpace(expediteur))
            mime.From.Add(new MailboxAddress(nomAffiche ?? "", expediteur));
        foreach (var adresse in message.Destinataires)
            mime.To.Add(MailboxAddress.Parse(adresse));
        foreach (var adresse in message.Copies)
            mime.Cc.Add(MailboxAddress.Parse(adresse));
        mime.Subject = message.Objet;

        var corps = new BodyBuilder { TextBody = message.Corps };
        foreach (var piece in message.PiecesJointes)
            corps.Attachments.Add(piece.Nom, piece.Contenu);
        mime.Body = corps.ToMessageBody();
        return mime;
    }

    /// <summary>Taille maximale des pièces jointes (Gmail refuse au-delà d'environ 25 Mo).</summary>
    public const long TailleMaximale = 24L * 1024 * 1024;
}

/// <summary>Envoi par un serveur SMTP (Orange, Free… ou Gmail avec un mot de passe d'application).</summary>
public sealed class EnvoiSmtp : IEnvoiMail
{
    private readonly ReglagesSmtp _reglages;
    private readonly string _motDePasse;

    public EnvoiSmtp(ReglagesSmtp reglages, string motDePasse)
    {
        _reglages = reglages;
        _motDePasse = motDePasse;
    }

    public string Description => $"{_reglages.AdresseExpediteur} (serveur {_reglages.Serveur})";

    public async Task EnvoyerAsync(MessageMail message, CancellationToken annulation = default)
    {
        var mime = Messages.Construire(message, _reglages.AdresseExpediteur, _reglages.NomAffiche);
        using var client = new SmtpClient { Timeout = 60_000 };
        try
        {
            await client.ConnectAsync(_reglages.Serveur, _reglages.Port,
                _reglages.Securite == SecuriteSmtp.Ssl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, annulation);
            await client.AuthenticateAsync(_reglages.Identifiant, _motDePasse, annulation);
            await client.SendAsync(mime, annulation);
            await client.DisconnectAsync(true, annulation);
        }
        catch (AuthenticationException)
        {
            throw new InvalidOperationException("Identifiant ou mot de passe refusé par le serveur d'envoi.");
        }
        catch (Exception e) when (e is SmtpCommandException or SmtpProtocolException or SslHandshakeException
                                      or System.Net.Sockets.SocketException or IOException)
        {
            throw new InvalidOperationException($"Le serveur d'envoi {_reglages.Serveur} a refusé le mail : {e.Message}");
        }
    }
}

/// <summary>Envoi par l'API Gmail avec la connexion Google de l'application (droit « envoyer des mails » seulement).</summary>
public sealed class EnvoiGmail : IEnvoiMail
{
    private const string Adresse = "https://gmail.googleapis.com/upload/gmail/v1/users/me/messages/send?uploadType=media";

    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<string>> _jeton;
    private readonly string? _expediteur;
    private readonly string? _nomAffiche;

    public EnvoiGmail(HttpClient http, Func<CancellationToken, Task<string>> jeton, string? expediteur, string? nomAffiche)
    {
        _http = http;
        _jeton = jeton;
        _expediteur = expediteur;
        _nomAffiche = nomAffiche;
    }

    public string Description => _expediteur is null ? "Gmail" : $"{_expediteur} (Gmail)";

    public async Task EnvoyerAsync(MessageMail message, CancellationToken annulation = default)
    {
        // Sans expéditeur, Gmail met l'adresse du compte connecté.
        var mime = Messages.Construire(message, _expediteur, _nomAffiche);
        using var flux = new MemoryStream();
        await mime.WriteToAsync(flux, annulation);

        using var requete = new HttpRequestMessage(HttpMethod.Post, Adresse)
        {
            Content = new ByteArrayContent(flux.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("message/rfc822") } },
        };
        requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _jeton(annulation));
        using var reponse = await _http.SendAsync(requete, annulation);
        if (reponse.IsSuccessStatusCode)
            return;

        var texte = await reponse.Content.ReadAsStringAsync(annulation);
        if (reponse.StatusCode == System.Net.HttpStatusCode.Forbidden && texte.Contains("insufficient", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Le compte Google n'autorise pas l'envoi de mails : reconnectez-le (Configuration › Compte Google) en cochant « Envoyer des e-mails ».");
        throw new HttpRequestException($"Gmail a refusé le mail ({(int)reponse.StatusCode}) : {Extrait(texte)}");
    }

    private static string Extrait(string texte)
    {
        try
        {
            using var json = JsonDocument.Parse(texte);
            if (json.RootElement.TryGetProperty("error", out var erreur) && erreur.TryGetProperty("message", out var message))
                return message.GetString() ?? texte;
        }
        catch (JsonException)
        {
        }
        return texte.Length > 300 ? texte[..300] + "…" : texte;
    }
}
