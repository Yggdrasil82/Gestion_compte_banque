using System.Security.Cryptography;
using System.Text;
using GestionCompte.Data.Documents;

namespace GestionCompte.Presentation;

/// <summary>
/// Identifiant d'application Google glissé dans l'exe par la compilation GitHub (secrets du dépôt), chiffré :
/// le code du dépôt n'en contient pas. Sans lui, l'application demande l'identifiant à la connexion.
/// Le chiffrement évite une lecture simple de l'exe, sans empêcher une personne très motivée de le retrouver.
/// </summary>
public static class GoogleIntegre
{
    /// <summary>Remplacé par la compilation GitHub (voir .github/workflows/build.yml).</summary>
    private const string Donnees = "__GOOGLE_INTEGRE__";

    private static readonly byte[] Cle = SHA256.HashData(Encoding.UTF8.GetBytes("Mon Budget - Google"));

    public static IdentifiantsGoogle? Identifiants { get; } = Dechiffrer(Donnees);

    /// <summary>Même calcul que l'étape « Identifiants Google » de la compilation.</summary>
    public static string Chiffrer(IdentifiantsGoogle identifiants)
    {
        using var aes = Aes.Create();
        aes.Key = Cle;
        aes.GenerateIV();
        var chiffre = aes.EncryptCbc(Encoding.UTF8.GetBytes($"{identifiants.ClientId}\n{identifiants.ClientSecret}"), aes.IV);
        return Convert.ToBase64String([.. aes.IV, .. chiffre]);
    }

    public static IdentifiantsGoogle? Dechiffrer(string donnees)
    {
        if (donnees.Length == 0 || donnees.StartsWith("__"))
            return null;
        try
        {
            var octets = Convert.FromBase64String(donnees);
            using var aes = Aes.Create();
            aes.Key = Cle;
            var texte = Encoding.UTF8.GetString(aes.DecryptCbc(octets.AsSpan(16), octets.AsSpan(0, 16))).Split('\n');
            return texte is [{ Length: > 0 } id, { Length: > 0 } secret] ? new IdentifiantsGoogle(id.Trim(), secret.Trim()) : null;
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException)
        {
            return null;
        }
    }
}
