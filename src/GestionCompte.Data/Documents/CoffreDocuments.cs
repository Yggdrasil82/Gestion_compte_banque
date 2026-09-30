using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GestionCompte.Core.Documents;

namespace GestionCompte.Data.Documents;

/// <summary>
/// Coffre des documents importants, commun à tous les comptes : une liste de fiches (« documents.json ») et un fichier
/// par document (« doc-{id}.dat »). Les documents protégés sont chiffrés ; les autres sont gardés tels quels.
/// </summary>
public sealed class CoffreDocuments
{
    public const string NomIndex = "documents.json";

    /// <summary>Taille maximale d'un document ajouté (scans, PDF…).</summary>
    public const long TailleMaximale = 50L * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IStockageDocuments _stockage;
    private readonly int _iterations;
    private List<DocumentImportant> _documents = new();
    private Protection? _protection;
    private byte[]? _cle;

    /// <param name="iterations">Coût du calcul de la clé depuis le mot de passe (réduit dans les tests).</param>
    public CoffreDocuments(IStockageDocuments stockage, int iterations = ChiffrementDocuments.IterationsParDefaut)
    {
        _stockage = stockage;
        _iterations = iterations;
    }

    public IStockageDocuments Stockage => _stockage;

    public IReadOnlyList<DocumentImportant> Documents => _documents;

    /// <summary>Un mot de passe a été choisi (au moins un document a été protégé un jour).</summary>
    public bool ProtectionConfiguree => _protection is not null;

    /// <summary>Le mot de passe a été saisi pendant cette session : les documents protégés s'ouvrent.</summary>
    public bool Deverrouille => _cle is not null;

    public static string NomFichier(DocumentImportant document) => $"doc-{document.Id}.dat";

    public async Task ChargerAsync(CancellationToken annulation = default)
    {
        var contenu = await _stockage.LireAsync(NomIndex, annulation);
        if (contenu is null)
        {
            _documents = new List<DocumentImportant>();
            _protection = null;
            return;
        }

        var index = JsonSerializer.Deserialize<Index>(contenu, Options)
            ?? throw new InvalidDataException("La liste des documents est illisible.");
        _documents = index.Documents ?? new List<DocumentImportant>();
        _protection = index.Protection;
    }

    /// <summary>Enregistre les fiches (après une modification du nom, de l'échéance…).</summary>
    public Task EnregistrerAsync(CancellationToken annulation = default) =>
        _stockage.EcrireAsync(NomIndex, JsonSerializer.SerializeToUtf8Bytes(new Index(1, _documents, _protection), Options), annulation);

    public async Task<DocumentImportant> AjouterAsync(DocumentImportant document, byte[] contenu, CancellationToken annulation = default)
    {
        Verifier(contenu);
        document.Taille = contenu.Length;
        if (document.Protege)
            contenu = ChiffrementDocuments.Chiffrer(CleRequise(), contenu);
        await _stockage.EcrireAsync(NomFichier(document), contenu, annulation);
        _documents.Add(document);
        await EnregistrerAsync(annulation);
        return document;
    }

    public async Task RemplacerAsync(DocumentImportant document, byte[] contenu, string nomFichier, CancellationToken annulation = default)
    {
        Verifier(contenu);
        var stocke = document.Protege ? ChiffrementDocuments.Chiffrer(CleRequise(), contenu) : contenu;
        await _stockage.EcrireAsync(NomFichier(document), stocke, annulation);
        document.NomFichier = nomFichier;
        document.Taille = contenu.Length;
        await EnregistrerAsync(annulation);
    }

    /// <summary>Contenu d'origine du document (déchiffré s'il est protégé).</summary>
    public async Task<byte[]> LireAsync(DocumentImportant document, CancellationToken annulation = default)
    {
        var contenu = await _stockage.LireAsync(NomFichier(document), annulation)
            ?? throw new FileNotFoundException($"Le fichier du document « {document.Nom} » est introuvable dans le coffre.");
        return document.Protege ? ChiffrementDocuments.Dechiffrer(CleRequise(), contenu) : contenu;
    }

    public async Task SupprimerAsync(DocumentImportant document, CancellationToken annulation = default)
    {
        _documents.Remove(document);
        await EnregistrerAsync(annulation);
        await _stockage.SupprimerAsync(NomFichier(document), annulation);
    }

    /// <summary>Chiffre (ou déchiffre) le fichier d'un document : le coffre doit être déverrouillé.</summary>
    public async Task ProtegerAsync(DocumentImportant document, bool proteger, CancellationToken annulation = default)
    {
        if (document.Protege == proteger)
            return;
        var contenu = await LireAsync(document, annulation);
        var stocke = proteger ? ChiffrementDocuments.Chiffrer(CleRequise(), contenu) : contenu;
        await _stockage.EcrireAsync(NomFichier(document), stocke, annulation);
        document.Protege = proteger;
        await EnregistrerAsync(annulation);
    }

    // ---- Mot de passe et clé de secours ----

    /// <summary>Choisit le mot de passe du coffre ; renvoie la clé de secours à garder de côté.</summary>
    public async Task<string> ConfigurerProtectionAsync(string motDePasse, CancellationToken annulation = default)
    {
        if (_protection is not null)
            throw new InvalidOperationException("Le mot de passe du coffre est déjà choisi.");
        VerifierMotDePasse(motDePasse);

        var cle = ChiffrementDocuments.NouvelleCle();
        var secours = ChiffrementDocuments.NouvelleCleSecours();
        _protection = Proteger(cle, motDePasse, ChiffrementDocuments.NormaliserCleSecours(secours));
        _cle = cle;
        await EnregistrerAsync(annulation);
        return secours;
    }

    /// <summary>Déverrouille le coffre pour la session ; false si le mot de passe est faux.</summary>
    public bool Deverrouiller(string motDePasse)
    {
        if (_protection is null)
            return false;
        var cle = Ouvrir(_protection.CleParMotDePasse, motDePasse, _protection.Sel, _protection.Iterations);
        _cle = cle ?? _cle;
        return cle is not null;
    }

    /// <summary>Mot de passe oublié : la clé de secours permet d'en choisir un nouveau (elle reste valable).</summary>
    public async Task<bool> RecupererAsync(string cleSecours, string nouveauMotDePasse, CancellationToken annulation = default)
    {
        if (_protection is null)
            return false;
        VerifierMotDePasse(nouveauMotDePasse);
        var cle = Ouvrir(_protection.CleParSecours, ChiffrementDocuments.NormaliserCleSecours(cleSecours),
            _protection.SelSecours, _protection.Iterations);
        if (cle is null)
            return false;

        var sel = ChiffrementDocuments.NouveauSel();
        _protection = _protection with
        {
            Sel = sel,
            CleParMotDePasse = ChiffrementDocuments.Chiffrer(ChiffrementDocuments.DeriverCle(nouveauMotDePasse, sel, _protection.Iterations), cle),
        };
        _cle = cle;
        await EnregistrerAsync(annulation);
        return true;
    }

    /// <summary>Change le mot de passe (le coffre doit être déverrouillé) ; la clé de secours ne change pas.</summary>
    public async Task ChangerMotDePasseAsync(string nouveauMotDePasse, CancellationToken annulation = default)
    {
        VerifierMotDePasse(nouveauMotDePasse);
        var cle = CleRequise();
        var sel = ChiffrementDocuments.NouveauSel();
        _protection = _protection! with
        {
            Sel = sel,
            CleParMotDePasse = ChiffrementDocuments.Chiffrer(ChiffrementDocuments.DeriverCle(nouveauMotDePasse, sel, _protection.Iterations), cle),
        };
        await EnregistrerAsync(annulation);
    }

    public void Verrouiller() => _cle = null;

    /// <summary>Copie tout le coffre (fiches et fichiers, tels quels) vers un autre emplacement.</summary>
    public async Task<int> CopierVersAsync(IStockageDocuments destination, CancellationToken annulation = default)
    {
        var copies = 0;
        foreach (var document in _documents)
        {
            var contenu = await _stockage.LireAsync(NomFichier(document), annulation);
            if (contenu is null)
                continue;
            await destination.EcrireAsync(NomFichier(document), contenu, annulation);
            copies++;
        }
        await destination.EcrireAsync(NomIndex, JsonSerializer.SerializeToUtf8Bytes(new Index(1, _documents, _protection), Options), annulation);
        return copies;
    }

    public const int LongueurMinimale = 8;

    private static void VerifierMotDePasse(string motDePasse)
    {
        if (string.IsNullOrEmpty(motDePasse) || motDePasse.Length < LongueurMinimale)
            throw new ArgumentException($"Le mot de passe doit faire au moins {LongueurMinimale} caractères.");
    }

    private static void Verifier(byte[] contenu)
    {
        if (contenu.Length > TailleMaximale)
            throw new ArgumentException($"Le document dépasse {TailleMaximale / 1024 / 1024} Mo.");
    }

    private byte[] CleRequise() => _cle ?? throw new InvalidOperationException("Le coffre est verrouillé : saisissez son mot de passe.");

    private Protection Proteger(byte[] cle, string motDePasse, string secours)
    {
        var sel = ChiffrementDocuments.NouveauSel();
        var selSecours = ChiffrementDocuments.NouveauSel();
        return new Protection(
            sel, ChiffrementDocuments.Chiffrer(ChiffrementDocuments.DeriverCle(motDePasse, sel, _iterations), cle),
            selSecours, ChiffrementDocuments.Chiffrer(ChiffrementDocuments.DeriverCle(secours, selSecours, _iterations), cle),
            _iterations);
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

    private sealed record Index(int Version, List<DocumentImportant>? Documents, Protection? Protection);

    /// <param name="CleParMotDePasse">Clé du coffre chiffrée avec le mot de passe.</param>
    /// <param name="CleParSecours">Clé du coffre chiffrée avec la clé de secours.</param>
    private sealed record Protection(byte[] Sel, byte[] CleParMotDePasse, byte[] SelSecours, byte[] CleParSecours, int Iterations);
}
