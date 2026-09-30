using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using GestionCompte.Core.Mail;

namespace GestionCompte.Data.Mail;

/// <summary>
/// Carnet d'adresses et historique des mails envoyés, communs à tous les comptes (« mail.json » dans le dossier des données).
/// </summary>
public sealed class CarnetMail
{
    public const string NomFichier = "mail.json";

    /// <summary>Nombre d'envois gardés dans l'historique.</summary>
    public const int TailleHistorique = 500;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _chemin;

    public CarnetMail(string dossier) => _chemin = Path.Combine(dossier, NomFichier);

    public List<Contact> Contacts { get; private set; } = new();

    /// <summary>Envois, le plus récent en premier.</summary>
    public List<EnvoiMail> Historique { get; private set; } = new();

    public void Charger()
    {
        if (!File.Exists(_chemin))
            return;
        var contenu = JsonSerializer.Deserialize<Fichier>(File.ReadAllText(_chemin), Options)
            ?? throw new InvalidDataException("Le carnet d'adresses est illisible.");
        Contacts = contenu.Contacts ?? new List<Contact>();
        Historique = contenu.Historique ?? new List<EnvoiMail>();
    }

    public void Enregistrer()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_chemin)!);
        var temporaire = _chemin + ".tmp";
        File.WriteAllText(temporaire, JsonSerializer.Serialize(new Fichier(1, Contacts, Historique), Options));
        File.Move(temporaire, _chemin, true);
    }

    /// <summary>Note un envoi dans l'historique et, s'il a réussi, chez chaque destinataire du carnet (ajouté s'il n'y est pas).</summary>
    public void NoterEnvoi(EnvoiMail envoi)
    {
        Historique.Insert(0, envoi);
        if (Historique.Count > TailleHistorique)
            Historique.RemoveRange(TailleHistorique, Historique.Count - TailleHistorique);
        if (!envoi.Reussi)
            return;

        foreach (var adresse in envoi.Destinataires)
        {
            var contact = Contacts.FirstOrDefault(c => string.Equals(c.Email, adresse, StringComparison.OrdinalIgnoreCase));
            if (contact is null)
            {
                contact = new Contact { Email = adresse };
                Contacts.Add(contact);
            }
            contact.DernierEnvoi = envoi.Date;
            contact.NombreEnvois++;
        }
    }

    /// <summary>
    /// Ajoute les contacts Google absents du carnet (même adresse = même contact) et complète les noms manquants.
    /// Renvoie le nombre de contacts ajoutés.
    /// </summary>
    public int Fusionner(IEnumerable<(string Nom, string Email)> contactsGoogle)
    {
        var ajoutes = 0;
        foreach (var (nom, email) in contactsGoogle)
        {
            if (!AdressesMail.Valide(email))
                continue;
            var existant = Contacts.FirstOrDefault(c => string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase));
            if (existant is null)
            {
                Contacts.Add(new Contact { Nom = nom, Email = email.Trim(), Source = SourceContact.Google });
                ajoutes++;
            }
            else if (string.IsNullOrWhiteSpace(existant.Nom) && !string.IsNullOrWhiteSpace(nom))
                existant.Nom = nom;
        }
        return ajoutes;
    }

    private sealed record Fichier(int Version, List<Contact>? Contacts, List<EnvoiMail>? Historique);
}

/// <summary>Lecture des contacts Google (contacts enregistrés et « autres contacts » à qui l'on a déjà écrit).</summary>
public static class ContactsGoogle
{
    private const string Contacts = "https://people.googleapis.com/v1/people/me/connections?personFields=names,emailAddresses&pageSize=1000";
    private const string AutresContacts = "https://people.googleapis.com/v1/otherContacts?readMask=names,emailAddresses&pageSize=1000";

    public static async Task<IReadOnlyList<(string Nom, string Email)>> LireAsync(HttpClient http,
        Func<CancellationToken, Task<string>> jeton, bool avecAutres, CancellationToken annulation = default)
    {
        var resultat = new List<(string, string)>();
        await LirePagesAsync(http, jeton, Contacts, "connections", resultat, annulation);
        if (avecAutres)
            await LirePagesAsync(http, jeton, AutresContacts, "otherContacts", resultat, annulation);
        return resultat;
    }

    private static async Task LirePagesAsync(HttpClient http, Func<CancellationToken, Task<string>> jeton, string adresse, string liste,
        List<(string, string)> resultat, CancellationToken annulation)
    {
        string? page = null;
        do
        {
            using var requete = new HttpRequestMessage(HttpMethod.Get,
                adresse + (page is null ? "" : $"&pageToken={Uri.EscapeDataString(page)}"));
            requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await jeton(annulation));
            using var reponse = await http.SendAsync(requete, annulation);
            var texte = await reponse.Content.ReadAsStringAsync(annulation);
            if (!reponse.IsSuccessStatusCode)
            {
                if (reponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    throw new InvalidOperationException(
                        "Google refuse l'accès aux contacts : activez « People API » dans la console Google Cloud, puis reconnectez le compte Google en autorisant les contacts.");
                throw new HttpRequestException($"Google a refusé la lecture des contacts ({(int)reponse.StatusCode}).");
            }

            using var json = JsonDocument.Parse(texte);
            if (json.RootElement.TryGetProperty(liste, out var personnes))
            {
                foreach (var personne in personnes.EnumerateArray())
                {
                    var nom = personne.TryGetProperty("names", out var noms) && noms.GetArrayLength() > 0
                                                                          && noms[0].TryGetProperty("displayName", out var n)
                        ? n.GetString() ?? ""
                        : "";
                    if (!personne.TryGetProperty("emailAddresses", out var adresses))
                        continue;
                    foreach (var a in adresses.EnumerateArray())
                        if (a.TryGetProperty("value", out var valeur) && valeur.GetString() is { Length: > 0 } email)
                            resultat.Add((nom, email));
                }
            }
            page = json.RootElement.TryGetProperty("nextPageToken", out var suivante) ? suivante.GetString() : null;
        } while (page is not null);
    }
}
