using System.Text.Json;

namespace GestionCompte.Data;

/// <summary>Un compte suivi par l'application : son nom affiché et son fichier de données.</summary>
public sealed record EntreeCompte(string Nom, string Fichier);

/// <summary>
/// Liste des comptes (un fichier SQLite par compte), enregistrée dans « comptes.json » à côté des données.
/// Sans ce fichier, il n'y a qu'un compte : « compte.db », celui des versions précédentes.
/// </summary>
public sealed class RegistreComptes
{
    public const string NomFichier = "comptes.json";
    public const string FichierPrincipal = "compte.db";
    public const string NomPrincipal = "Compte principal";

    private readonly List<EntreeCompte> _comptes;

    private RegistreComptes(string dossier, List<EntreeCompte> comptes, string actif)
    {
        Dossier = dossier;
        _comptes = comptes;
        Actif = comptes.FirstOrDefault(c => c.Fichier == actif) ?? comptes[0];
    }

    public string Dossier { get; }

    public IReadOnlyList<EntreeCompte> Comptes => _comptes;

    /// <summary>Compte ouvert (mémorisé pour le prochain lancement).</summary>
    public EntreeCompte Actif { get; private set; }

    private string CheminRegistre => Path.Combine(Dossier, NomFichier);

    public string Chemin(EntreeCompte compte) => Path.Combine(Dossier, compte.Fichier);

    /// <summary>Lit la liste des comptes ; un fichier absent ou illisible donne le seul compte principal.</summary>
    public static RegistreComptes Charger(string dossier)
    {
        dossier = Path.GetFullPath(dossier);
        var chemin = Path.Combine(dossier, NomFichier);
        try
        {
            if (File.Exists(chemin))
            {
                var contenu = JsonSerializer.Deserialize<Contenu>(File.ReadAllText(chemin));
                var comptes = contenu?.Comptes?
                    .Where(c => !string.IsNullOrWhiteSpace(c.Nom) && FichierValide(c.Fichier))
                    .DistinctBy(c => c.Fichier, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (comptes is { Count: > 0 })
                    return new RegistreComptes(dossier, comptes, contenu!.Actif ?? "");
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // Liste illisible : on retombe sur le compte principal, les fichiers de données ne sont pas touchés.
        }

        return new RegistreComptes(dossier, new List<EntreeCompte> { new(NomPrincipal, FichierPrincipal) }, FichierPrincipal);
    }

    /// <summary>Seuls des noms de fichiers simples, dans le dossier des données, sont acceptés.</summary>
    private static bool FichierValide(string? fichier) =>
        fichier is not null && System.Text.RegularExpressions.Regex.IsMatch(fichier, @"^[A-Za-z0-9][A-Za-z0-9_-]*\.db$");

    public bool NomDisponible(string nom, EntreeCompte? sauf = null) =>
        !string.IsNullOrWhiteSpace(nom)
        && !_comptes.Any(c => c != sauf && string.Equals(c.Nom.Trim(), nom.Trim(), StringComparison.CurrentCultureIgnoreCase));

    /// <summary>Ajoute un compte (fichier « compte-2.db », « compte-3.db »…) ; son fichier n'est pas encore créé.</summary>
    public EntreeCompte Ajouter(string nom)
    {
        if (!NomDisponible(nom))
            throw new ArgumentException($"Un compte s'appelle déjà « {nom} ».", nameof(nom));

        var numero = 2;
        string fichier;
        do
        {
            fichier = $"compte-{numero++}.db";
        } while (_comptes.Any(c => string.Equals(c.Fichier, fichier, StringComparison.OrdinalIgnoreCase))
                 || File.Exists(Path.Combine(Dossier, fichier)));

        var compte = new EntreeCompte(nom.Trim(), fichier);
        _comptes.Add(compte);
        Enregistrer();
        return compte;
    }

    public EntreeCompte Renommer(EntreeCompte compte, string nom)
    {
        if (!NomDisponible(nom, compte))
            throw new ArgumentException($"Un compte s'appelle déjà « {nom} ».", nameof(nom));

        var renomme = compte with { Nom = nom.Trim() };
        _comptes[IndexDe(compte)] = renomme;
        if (Actif == compte)
            Actif = renomme;
        Enregistrer();
        return renomme;
    }

    /// <summary>Retire un compte de la liste et supprime son fichier et ses copies de sécurité. Le dernier compte ne peut pas être supprimé.</summary>
    public void Supprimer(EntreeCompte compte)
    {
        if (_comptes.Count <= 1)
            throw new InvalidOperationException("Le dernier compte ne peut pas être supprimé.");

        var index = IndexDe(compte);
        new DepotSqlite(Chemin(compte)).EffacerTout();
        _comptes.RemoveAt(index);
        if (Actif == compte)
            Actif = _comptes[Math.Min(index, _comptes.Count - 1)];
        Enregistrer();
    }

    public void DefinirActif(EntreeCompte compte)
    {
        Actif = _comptes[IndexDe(compte)];
        Enregistrer();
    }

    /// <summary>
    /// Supprime tous les comptes, leurs copies de sécurité et la liste : il ne reste que le compte principal, vide.
    /// </summary>
    public void EffacerTout()
    {
        foreach (var compte in _comptes)
            new DepotSqlite(Chemin(compte)).EffacerTout();
        File.Delete(CheminRegistre);
        _comptes.Clear();
        _comptes.Add(new EntreeCompte(NomPrincipal, FichierPrincipal));
        Actif = _comptes[0];
    }

    private int IndexDe(EntreeCompte compte)
    {
        var index = _comptes.IndexOf(compte);
        return index >= 0 ? index : throw new ArgumentException("Ce compte n'est pas dans la liste.", nameof(compte));
    }

    private void Enregistrer()
    {
        Directory.CreateDirectory(Dossier);
        var contenu = new Contenu { Comptes = _comptes.ToList(), Actif = Actif.Fichier };
        File.WriteAllText(CheminRegistre, JsonSerializer.Serialize(contenu, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class Contenu
    {
        public List<EntreeCompte>? Comptes { get; set; }
        public string? Actif { get; set; }
    }
}
