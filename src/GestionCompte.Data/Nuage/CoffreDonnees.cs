using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GestionCompte.Data.Documents;

namespace GestionCompte.Data.Nuage;

/// <summary>Application ouverte sur un PC : les autres PC sont prévenus tant que le verrou est récent.</summary>
/// <param name="Signe">Dernier signe de vie (renouvelé régulièrement) ; au-delà de <see cref="CoffreDonnees.DureeVerrou"/>, le verrou est ignoré.</param>
/// <param name="Ferme">L'application a été fermée normalement.</param>
public sealed record VerrouDonnees(string Poste, string Session, DateTime Depuis, DateTime Signe, bool Ferme)
{
    /// <summary>Application encore ouverte sur un autre PC (ou dans une autre session).</summary>
    public bool BloqueAutreSession(string session, DateTime maintenant) =>
        !Ferme && Session != session && maintenant - Signe < CoffreDonnees.DureeVerrou;
}

/// <summary>
/// Toutes les données de l'application (comptes, préférences, documents, clés des IA…) rangées dans un seul fichier chiffré
/// (AES-256-GCM) d'un dossier « Mon Budget - Données » du Google Drive. La clé de chiffrement est elle-même chiffrée avec le
/// mot de passe et avec la clé de secours, dans « protection.json ». Le même format sert à la copie chiffrée gardée sur un
/// PC de confiance (lue hors connexion).
/// </summary>
public sealed class CoffreDonnees
{
    public const string NomDossierDrive = "Mon Budget - Données";
    public const string NomDonnees = "donnees.coffre";
    public const string NomProtection = "protection.json";
    public const string NomVerrou = "verrou.json";
    public const int LongueurMinimale = CoffreDocuments.LongueurMinimale;

    /// <summary>Un verrou sans signe de vie depuis ce délai vient d'une application arrêtée brutalement : il est ignoré.</summary>
    public static readonly TimeSpan DureeVerrou = TimeSpan.FromMinutes(15);

    private readonly IStockageDocuments _stockage;
    private readonly int _iterations;
    private Protection? _protection;
    private byte[]? _cle;

    /// <param name="iterations">Coût du calcul de la clé depuis le mot de passe (réduit dans les tests).</param>
    public CoffreDonnees(IStockageDocuments stockage, int iterations = ChiffrementDocuments.IterationsParDefaut)
    {
        _stockage = stockage;
        _iterations = iterations;
    }

    public IStockageDocuments Stockage => _stockage;

    /// <summary>Copie chiffrée tenue à jour à chaque lecture ou envoi (PC de confiance : ouverture hors connexion).</summary>
    public IStockageDocuments? Copie { get; set; }

    public bool Deverrouille => _cle is not null;

    /// <summary>Des données ont déjà été mises dans ce stockage.</summary>
    public async Task<bool> ExisteAsync(CancellationToken annulation = default) =>
        await LireProtectionAsync(annulation) is not null;

    /// <summary>Premier envoi : choisit le mot de passe, chiffre le dossier et renvoie la clé de secours à garder.</summary>
    public async Task<string> CreerAsync(string dossier, string motDePasse, CancellationToken annulation = default)
    {
        VerifierMotDePasse(motDePasse);
        var cle = ChiffrementDocuments.NouvelleCle();
        var secours = ChiffrementDocuments.NouvelleCleSecours();
        var sel = ChiffrementDocuments.NouveauSel();
        var selSecours = ChiffrementDocuments.NouveauSel();
        var protection = new Protection(1,
            sel, ChiffrementDocuments.Chiffrer(ChiffrementDocuments.DeriverCle(motDePasse, sel, _iterations), cle),
            selSecours, ChiffrementDocuments.Chiffrer(
                ChiffrementDocuments.DeriverCle(ChiffrementDocuments.NormaliserCleSecours(secours), selSecours, _iterations), cle),
            _iterations);
        _cle = cle;
        // Les données d'abord : une protection sans données ne doit jamais exister.
        await EnvoyerAsync(dossier, annulation);
        await EcrireProtectionAsync(protection, annulation);
        return secours;
    }

    /// <summary>false si le mot de passe est faux.</summary>
    public async Task<bool> DeverrouillerAsync(string motDePasse, CancellationToken annulation = default)
    {
        var protection = await LireProtectionAsync(annulation)
            ?? throw new FileNotFoundException("Aucune donnée n'a été trouvée dans Google Drive.");
        var cle = Ouvrir(protection.CleParMotDePasse, motDePasse, protection.Sel, protection.Iterations);
        if (cle is not null)
            _cle = cle;
        return cle is not null;
    }

    /// <summary>Mot de passe oublié : la clé de secours permet d'en choisir un nouveau (elle reste valable).</summary>
    public async Task<bool> RecupererAsync(string cleSecours, string nouveauMotDePasse, CancellationToken annulation = default)
    {
        VerifierMotDePasse(nouveauMotDePasse);
        var protection = await LireProtectionAsync(annulation)
            ?? throw new FileNotFoundException("Aucune donnée n'a été trouvée dans Google Drive.");
        var cle = Ouvrir(protection.CleParSecours, ChiffrementDocuments.NormaliserCleSecours(cleSecours),
            protection.SelSecours, protection.Iterations);
        if (cle is null)
            return false;
        _cle = cle;
        await EcrireProtectionAsync(AvecMotDePasse(protection, cle, nouveauMotDePasse), annulation);
        return true;
    }

    /// <summary>Change le mot de passe (coffre déverrouillé) ; la clé de secours ne change pas.</summary>
    public async Task ChangerMotDePasseAsync(string nouveauMotDePasse, CancellationToken annulation = default)
    {
        VerifierMotDePasse(nouveauMotDePasse);
        var cle = CleRequise();
        var protection = _protection ?? await LireProtectionAsync(annulation)
            ?? throw new FileNotFoundException("Aucune donnée n'a été trouvée dans Google Drive.");
        await EcrireProtectionAsync(AvecMotDePasse(protection, cle, nouveauMotDePasse), annulation);
    }

    /// <summary>Télécharge les données et les déchiffre dans <paramref name="dossier"/> (vidé avant).</summary>
    public async Task TelechargerAsync(string dossier, CancellationToken annulation = default)
    {
        var chiffre = await _stockage.LireAsync(NomDonnees, annulation)
            ?? throw new FileNotFoundException("Le fichier des données est introuvable dans Google Drive.");
        Paquet.Deballer(ChiffrementDocuments.Dechiffrer(CleRequise(), chiffre), dossier);
        await EcrireCopieAsync(chiffre, annulation);
    }

    /// <summary>Chiffre tout le dossier et l'envoie (remplace la version précédente).</summary>
    public async Task EnvoyerAsync(string dossier, CancellationToken annulation = default)
    {
        var chiffre = ChiffrementDocuments.Chiffrer(CleRequise(), Paquet.Emballer(dossier));
        // La copie d'abord : sans connexion, les dernières modifications restent au moins sur ce PC.
        await EcrireCopieAsync(chiffre, annulation);
        await _stockage.EcrireAsync(NomDonnees, chiffre, annulation);
    }

    private async Task EcrireCopieAsync(byte[]? donnees, CancellationToken annulation)
    {
        if (Copie is null)
            return;
        try
        {
            if (donnees is not null)
                await Copie.EcrireAsync(NomDonnees, donnees, annulation);
            if (_protection is not null)
                await Copie.EcrireAsync(NomProtection, SerialiserProtection(_protection), annulation);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Copie de confort : son échec ne bloque pas l'enregistrement dans Google Drive.
        }
    }

    // ---- Verrou ----

    public async Task<VerrouDonnees?> LireVerrouAsync(CancellationToken annulation = default)
    {
        var contenu = await _stockage.LireAsync(NomVerrou, annulation);
        try
        {
            return contenu is null ? null : JsonSerializer.Deserialize<VerrouDonnees>(contenu);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Le verrou n'est jamais supprimé (son fichier garde le même identifiant dans Drive) : il est marqué « fermé ».</summary>
    public Task EcrireVerrouAsync(VerrouDonnees verrou, CancellationToken annulation = default) =>
        _stockage.EcrireAsync(NomVerrou, JsonSerializer.SerializeToUtf8Bytes(verrou), annulation);

    // ---- Interne ----

    private async Task<Protection?> LireProtectionAsync(CancellationToken annulation)
    {
        if (_protection is not null)
            return _protection;
        var contenu = await _stockage.LireAsync(NomProtection, annulation);
        if (contenu is null)
            return null;
        return _protection = JsonSerializer.Deserialize<Protection>(contenu)
            ?? throw new InvalidDataException("Le fichier de protection des données est illisible.");
    }

    private async Task EcrireProtectionAsync(Protection protection, CancellationToken annulation)
    {
        await _stockage.EcrireAsync(NomProtection, SerialiserProtection(protection), annulation);
        _protection = protection;
        await EcrireCopieAsync(null, annulation);
    }

    private static byte[] SerialiserProtection(Protection protection) =>
        JsonSerializer.SerializeToUtf8Bytes(protection, new JsonSerializerOptions { WriteIndented = true });

    private static Protection AvecMotDePasse(Protection protection, byte[] cle, string motDePasse)
    {
        var sel = ChiffrementDocuments.NouveauSel();
        return protection with
        {
            Sel = sel,
            CleParMotDePasse = ChiffrementDocuments.Chiffrer(ChiffrementDocuments.DeriverCle(motDePasse, sel, protection.Iterations), cle),
        };
    }

    private static byte[]? Ouvrir(byte[] cleChiffree, string secret, byte[] sel, int iterations)
    {
        try
        {
            return ChiffrementDocuments.Dechiffrer(ChiffrementDocuments.DeriverCle(secret, sel, iterations), cleChiffree);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static void VerifierMotDePasse(string motDePasse)
    {
        if (string.IsNullOrEmpty(motDePasse) || motDePasse.Length < LongueurMinimale)
            throw new ArgumentException($"Le mot de passe doit faire au moins {LongueurMinimale} caractères.");
    }

    private byte[] CleRequise() => _cle ?? throw new InvalidOperationException("Saisissez d'abord le mot de passe des données.");

    /// <param name="CleParMotDePasse">Clé des données chiffrée avec le mot de passe.</param>
    /// <param name="CleParSecours">Clé des données chiffrée avec la clé de secours.</param>
    private sealed record Protection(int Version, byte[] Sel, byte[] CleParMotDePasse, byte[] SelSecours, byte[] CleParSecours, int Iterations);

    /// <summary>Dossier des données mis dans une archive zip (avant chiffrement) et inversement.</summary>
    public static class Paquet
    {
        public static byte[] Emballer(string dossier)
        {
            using var memoire = new MemoryStream();
            using (var archive = new ZipArchive(memoire, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var fichier in Fichiers(dossier))
                {
                    var entree = archive.CreateEntry(Path.GetRelativePath(dossier, fichier).Replace('\\', '/'), CompressionLevel.Optimal);
                    entree.LastWriteTime = File.GetLastWriteTime(fichier);
                    using var source = new FileStream(fichier, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var cible = entree.Open();
                    source.CopyTo(cible);
                }
            }
            return memoire.ToArray();
        }

        /// <summary>Vide <paramref name="dossier"/> puis y extrait l'archive (aucun chemin ne peut sortir du dossier).</summary>
        public static void Deballer(byte[] zip, string dossier)
        {
            dossier = Path.GetFullPath(dossier);
            if (Directory.Exists(dossier))
                Directory.Delete(dossier, recursive: true);
            Directory.CreateDirectory(dossier);
            var racine = dossier.EndsWith(Path.DirectorySeparatorChar) ? dossier : dossier + Path.DirectorySeparatorChar;

            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            foreach (var entree in archive.Entries)
            {
                if (string.IsNullOrEmpty(entree.Name))
                    continue;
                var chemin = Path.GetFullPath(Path.Combine(dossier, entree.FullName));
                if (!chemin.StartsWith(racine, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Chemin refusé dans les données : {entree.FullName}");
                Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
                entree.ExtractToFile(chemin, overwrite: true);
            }
        }

        /// <summary>Résumé des fichiers (noms, tailles, dates) : change dès qu'un fichier est modifié.</summary>
        public static string Empreinte(string dossier) =>
            string.Join("|", Fichiers(dossier).Select(f =>
            {
                var infos = new FileInfo(f);
                return $"{Path.GetRelativePath(dossier, f)}:{infos.Length}:{infos.LastWriteTimeUtc.Ticks}";
            }));

        /// <summary>Fichiers à emballer : tout le dossier, sauf les fichiers temporaires.</summary>
        private static IEnumerable<string> Fichiers(string dossier) =>
            Directory.Exists(dossier)
                ? Directory.EnumerateFiles(dossier, "*", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                                && !f.EndsWith("-journal", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.Ordinal)
                : Enumerable.Empty<string>();
    }
}
