namespace GestionCompte.Data.Documents;

/// <summary>Endroit où le coffre range ses fichiers : un dossier du PC (éventuellement synchronisé) ou Google Drive.</summary>
public interface IStockageDocuments
{
    /// <summary>Ex. « Dossier C:\…\Documents » ou « Google Drive ».</summary>
    string Description { get; }

    /// <summary>Contenu du fichier, ou null s'il n'existe pas.</summary>
    Task<byte[]?> LireAsync(string nom, CancellationToken annulation = default);

    Task EcrireAsync(string nom, byte[] contenu, CancellationToken annulation = default);

    Task SupprimerAsync(string nom, CancellationToken annulation = default);

    /// <summary>Noms de tous les fichiers du coffre (pour copier le coffre vers un autre emplacement).</summary>
    Task<IReadOnlyList<string>> ListerAsync(CancellationToken annulation = default);
}

/// <summary>Coffre dans un dossier : celui de l'application, ou un dossier synchronisé par Google Drive pour ordinateur.</summary>
public sealed class StockageDossier : IStockageDocuments
{
    public StockageDossier(string dossier) => Dossier = Path.GetFullPath(dossier);

    public string Dossier { get; }

    public string Description => $"Dossier {Dossier}";

    public async Task<byte[]?> LireAsync(string nom, CancellationToken annulation = default)
    {
        var chemin = Chemin(nom);
        return File.Exists(chemin) ? await File.ReadAllBytesAsync(chemin, annulation) : null;
    }

    public async Task EcrireAsync(string nom, byte[] contenu, CancellationToken annulation = default)
    {
        Directory.CreateDirectory(Dossier);
        // Écriture dans un fichier temporaire puis remplacement : jamais de fichier à moitié écrit.
        var chemin = Chemin(nom);
        var temporaire = chemin + ".tmp";
        await File.WriteAllBytesAsync(temporaire, contenu, annulation);
        File.Move(temporaire, chemin, overwrite: true);
    }

    public Task SupprimerAsync(string nom, CancellationToken annulation = default)
    {
        File.Delete(Chemin(nom));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListerAsync(CancellationToken annulation = default) =>
        Task.FromResult<IReadOnlyList<string>>(Directory.Exists(Dossier)
            ? Directory.GetFiles(Dossier).Select(Path.GetFileName).Where(n => !n!.EndsWith(".tmp")).ToList()!
            : new List<string>());

    private string Chemin(string nom)
    {
        if (nom.Contains('/') || nom.Contains('\\') || nom.Contains(".."))
            throw new ArgumentException("Nom de fichier invalide.", nameof(nom));
        return Path.Combine(Dossier, nom);
    }
}
