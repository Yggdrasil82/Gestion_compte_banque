using System.IO.Compression;
using System.Text.Json;
using GestionCompte.Data.Documents;

namespace GestionCompte.Data;

/// <summary>Contenu annoncé d'une sauvegarde globale (lu avant de restaurer).</summary>
public sealed record ManifesteSauvegarde(int Version, DateTime Date, List<string> Comptes, int Documents);

/// <summary>
/// Sauvegarde de tout le dossier des données dans un seul .zip : tous les comptes, leur liste, les préférences, les achats,
/// lettres et mails, et le coffre de Documents (où qu'il soit rangé). Les clés et mots de passe (secrets.json) n'y sont pas.
/// </summary>
public static class SauvegardeGlobale
{
    public const string NomManifeste = "sauvegarde-globale.json";

    /// <summary>Dossier du coffre de Documents dans le .zip.</summary>
    public const string DossierCoffre = "coffre";

    /// <summary>Copies de sécurité faites avant une restauration globale, rangées dans le dossier des données.</summary>
    public const string PrefixeCopieSecurite = "avant-restauration-globale-";

    private const string NomSecrets = "secrets.json";
    private const string DossierDocumentsLocal = "Documents";

    /// <summary>Crée le .zip ; <paramref name="coffre"/> est copié tel quel (documents protégés compris).</summary>
    public static async Task<ManifesteSauvegarde> CreerAsync(string dossierDonnees, string destination, CoffreDocuments? coffre,
        DateTime maintenant, CancellationToken annulation = default)
    {
        dossierDonnees = Path.GetFullPath(dossierDonnees);
        var temporaire = Path.Combine(Path.GetTempPath(), $"gestioncompte-sauvegarde-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(temporaire);
            foreach (var fichier in Fichiers(dossierDonnees))
            {
                var relatif = Path.GetRelativePath(dossierDonnees, fichier);
                var cible = Path.Combine(temporaire, relatif);
                Directory.CreateDirectory(Path.GetDirectoryName(cible)!);
                // Base SQLite : copie cohérente par la sauvegarde de SQLite, même si le fichier est ouvert.
                if (EstCompte(relatif))
                    new DepotSqlite(fichier).Sauvegarder(cible);
                else
                    File.Copy(fichier, cible);
            }

            var documents = 0;
            if (coffre is not null)
                documents = await coffre.CopierVersAsync(new StockageDossier(Path.Combine(temporaire, DossierCoffre)), annulation);

            var registre = RegistreComptes.Charger(dossierDonnees);
            var manifeste = new ManifesteSauvegarde(1, maintenant, registre.Comptes.Select(c => c.Nom).ToList(), documents);
            await File.WriteAllBytesAsync(Path.Combine(temporaire, NomManifeste), JsonSerializer.SerializeToUtf8Bytes(manifeste), annulation);

            var zip = Path.GetFullPath(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
            var zipTemporaire = zip + ".tmp";
            File.Delete(zipTemporaire);
            ZipFile.CreateFromDirectory(temporaire, zipTemporaire);
            File.Move(zipTemporaire, zip, overwrite: true);
            return manifeste;
        }
        finally
        {
            if (Directory.Exists(temporaire))
                Directory.Delete(temporaire, true);
        }
    }

    /// <summary>Lit l'annonce du .zip ; null si ce n'est pas une sauvegarde globale de l'application.</summary>
    public static ManifesteSauvegarde? LireManifeste(string zip)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            var entree = archive.GetEntry(NomManifeste);
            if (entree is null)
                return null;
            using var flux = entree.Open();
            return JsonSerializer.Deserialize<ManifesteSauvegarde>(flux);
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Remplace les données par celles du .zip : les comptes actuels sont retirés (une copie de sécurité doit avoir été faite
    /// avant), puis les fichiers sont recopiés ; le coffre est réécrit dans <paramref name="stockageCoffre"/> s'il est fourni.
    /// </summary>
    public static async Task RestaurerAsync(string zip, string dossierDonnees, IStockageDocuments? stockageCoffre,
        CancellationToken annulation = default)
    {
        dossierDonnees = Path.GetFullPath(dossierDonnees);
        var temporaire = Path.Combine(Path.GetTempPath(), $"gestioncompte-restauration-{Guid.NewGuid():N}");
        try
        {
            // Tout est extrait et vérifié avant de toucher aux données.
            var racine = Path.GetFullPath(temporaire) + Path.DirectorySeparatorChar;
            using (var archive = ZipFile.OpenRead(zip))
            {
                if (archive.GetEntry(NomManifeste) is null)
                    throw new InvalidDataException("Ce fichier n'est pas une sauvegarde globale de Mon Budget.");
                foreach (var entree in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entree.Name))
                        continue;
                    var chemin = Path.GetFullPath(Path.Combine(temporaire, entree.FullName));
                    if (!chemin.StartsWith(racine, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Chemin refusé dans la sauvegarde : {entree.FullName}");
                    Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
                    entree.ExtractToFile(chemin, overwrite: true);
                }
            }

            // Comptes actuels retirés : un compte absent de la sauvegarde ne doit pas revenir.
            Directory.CreateDirectory(dossierDonnees);
            foreach (var fichier in Directory.EnumerateFiles(dossierDonnees))
            {
                var nom = Path.GetFileName(fichier);
                if (EstCompte(nom) || string.Equals(nom, RegistreComptes.NomFichier, StringComparison.OrdinalIgnoreCase))
                    File.Delete(fichier);
            }

            var coffre = Path.Combine(temporaire, DossierCoffre);
            foreach (var fichier in Directory.EnumerateFiles(temporaire, "*", SearchOption.AllDirectories))
            {
                var relatif = Path.GetRelativePath(temporaire, fichier);
                if (relatif == NomManifeste || fichier.StartsWith(coffre + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || Exclu(relatif))
                    continue;
                var cible = Path.Combine(dossierDonnees, relatif);
                Directory.CreateDirectory(Path.GetDirectoryName(cible)!);
                File.Copy(fichier, cible, overwrite: true);
            }

            if (stockageCoffre is not null && Directory.Exists(coffre))
            {
                // Fichiers d'abord, liste des documents en dernier : le coffre n'annonce jamais un fichier absent.
                var source = new StockageDossier(coffre);
                var noms = await source.ListerAsync(annulation);
                foreach (var nom in noms.Where(n => n != CoffreDocuments.NomIndex).Append(CoffreDocuments.NomIndex))
                {
                    if (await source.LireAsync(nom, annulation) is { } contenu)
                        await stockageCoffre.EcrireAsync(nom, contenu, annulation);
                }
            }
        }
        finally
        {
            if (Directory.Exists(temporaire))
                Directory.Delete(temporaire, true);
        }
    }

    /// <summary>Fichiers du dossier des données mis dans la sauvegarde.</summary>
    private static IEnumerable<string> Fichiers(string dossier) =>
        Directory.Exists(dossier)
            ? Directory.EnumerateFiles(dossier, "*", SearchOption.AllDirectories)
                .Where(f => !Exclu(Path.GetRelativePath(dossier, f)))
                .OrderBy(f => f, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

    /// <summary>
    /// Laissés de côté : secrets, fichiers temporaires, copies de sécurité automatiques et coffre local (copié à part,
    /// par le coffre lui-même).
    /// </summary>
    private static bool Exclu(string relatif)
    {
        var nom = Path.GetFileName(relatif);
        var racine = !relatif.Contains(Path.DirectorySeparatorChar) && !relatif.Contains(Path.AltDirectorySeparatorChar);
        return string.Equals(nom, NomSecrets, StringComparison.OrdinalIgnoreCase)
               || nom.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
               || nom.EndsWith("-journal", StringComparison.OrdinalIgnoreCase)
               || nom.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
               || nom.EndsWith("-shm", StringComparison.OrdinalIgnoreCase)
               || (racine && nom.Contains(".avant-", StringComparison.OrdinalIgnoreCase))
               || (racine && nom.StartsWith(PrefixeCopieSecurite, StringComparison.OrdinalIgnoreCase))
               || relatif.StartsWith(DossierDocumentsLocal + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || relatif.StartsWith(DossierDocumentsLocal + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Base d'un compte : « compte.db », « compte-2.db »… à la racine du dossier.</summary>
    private static bool EstCompte(string relatif) =>
        !relatif.Contains(Path.DirectorySeparatorChar) && !relatif.Contains(Path.AltDirectorySeparatorChar)
        && relatif.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
        && !relatif.Contains(".avant-", StringComparison.OrdinalIgnoreCase);
}
