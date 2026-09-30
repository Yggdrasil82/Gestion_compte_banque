using System.Security.Cryptography;
using System.Text;

namespace GestionCompte.Data.Documents;

/// <summary>
/// Chiffrement des documents protégés : AES-256-GCM avec une clé du coffre tirée au hasard. Cette clé est elle-même
/// chiffrée deux fois : avec le mot de passe (PBKDF2-SHA256) et avec la clé de secours.
/// </summary>
public static class ChiffrementDocuments
{
    public const int IterationsParDefaut = 600_000;
    private const int TailleNonce = 12;
    private const int TailleTag = 16;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sans I, O, 0, 1 : pas de confusion à la lecture

    public static byte[] NouvelleCle() => RandomNumberGenerator.GetBytes(32);

    public static byte[] NouveauSel() => RandomNumberGenerator.GetBytes(16);

    public static byte[] DeriverCle(string secret, byte[] sel, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), sel, iterations, HashAlgorithmName.SHA256, 32);

    /// <summary>Renvoie nonce + tag + texte chiffré.</summary>
    public static byte[] Chiffrer(byte[] cle, byte[] contenu)
    {
        var resultat = new byte[TailleNonce + TailleTag + contenu.Length];
        var nonce = resultat.AsSpan(0, TailleNonce);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(cle, TailleTag);
        aes.Encrypt(nonce, contenu, resultat.AsSpan(TailleNonce + TailleTag), resultat.AsSpan(TailleNonce, TailleTag));
        return resultat;
    }

    /// <exception cref="CryptographicException">Mauvaise clé ou fichier abîmé.</exception>
    public static byte[] Dechiffrer(byte[] cle, byte[] chiffre)
    {
        if (chiffre.Length < TailleNonce + TailleTag)
            throw new CryptographicException("Fichier chiffré trop court.");
        var contenu = new byte[chiffre.Length - TailleNonce - TailleTag];
        using var aes = new AesGcm(cle, TailleTag);
        aes.Decrypt(chiffre.AsSpan(0, TailleNonce), chiffre.AsSpan(TailleNonce + TailleTag),
            chiffre.AsSpan(TailleNonce, TailleTag), contenu);
        return contenu;
    }

    /// <summary>Clé de secours lisible : 8 groupes de 5 caractères (200 bits de hasard), ex. « K7PQ2-… ».</summary>
    public static string NouvelleCleSecours()
    {
        var octets = RandomNumberGenerator.GetBytes(40);
        var texte = new StringBuilder();
        for (var i = 0; i < 40; i++)
        {
            if (i > 0 && i % 5 == 0)
                texte.Append('-');
            texte.Append(Alphabet[octets[i] % Alphabet.Length]);
        }
        return texte.ToString();
    }

    /// <summary>Clé de secours saisie : majuscules, sans espaces ni tirets.</summary>
    public static string NormaliserCleSecours(string cle) =>
        new(cle.ToUpperInvariant().Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
}
