using System.IO;
using System.Security.Cryptography;
using System.Text;
using GestionCompte.Presentation;

namespace GestionCompte.App;

/// <summary>
/// Identifiants gardés sur ce PC (connexion Google Drive…), chiffrés par Windows pour la session de l'utilisateur :
/// un fichier copié sur un autre PC ou un autre compte Windows est illisible.
/// </summary>
public sealed class SecretsWindows : ISecretsLocaux
{
    private static readonly byte[] Entropie = Encoding.UTF8.GetBytes("GestionCompte.Secrets");
    private readonly string _dossier;

    public SecretsWindows(string dossier) => _dossier = dossier;

    private string Chemin(string nom) => Path.Combine(_dossier, $"{nom}.secret");

    public string? Lire(string nom)
    {
        try
        {
            var chemin = Chemin(nom);
            return File.Exists(chemin)
                ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(chemin), Entropie, DataProtectionScope.CurrentUser))
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return null; // illisible : il faudra se reconnecter
        }
    }

    public IEnumerable<string> Noms() =>
        Directory.Exists(_dossier)
            ? Directory.EnumerateFiles(_dossier, "*.secret").Select(f => Path.GetFileNameWithoutExtension(f)).ToList()
            : Enumerable.Empty<string>();

    public void Ecrire(string nom, string? valeur)
    {
        var chemin = Chemin(nom);
        if (valeur is null)
        {
            File.Delete(chemin);
            return;
        }
        Directory.CreateDirectory(_dossier);
        File.WriteAllBytes(chemin, ProtectedData.Protect(Encoding.UTF8.GetBytes(valeur), Entropie, DataProtectionScope.CurrentUser));
    }
}
