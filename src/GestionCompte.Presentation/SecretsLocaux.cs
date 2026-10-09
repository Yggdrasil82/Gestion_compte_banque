namespace GestionCompte.Presentation;

/// <summary>
/// Secrets gardés sur ce PC (identifiant d'application Google, jeton de connexion) : chiffrés par Windows dans
/// l'application ; en mémoire dans les tests et les captures.
/// </summary>
public interface ISecretsLocaux
{
    string? Lire(string nom);

    /// <param name="valeur">null : efface le secret.</param>
    void Ecrire(string nom, string? valeur);

    /// <summary>Noms des secrets enregistrés (copiés avec les données quand elles vont dans Google Drive).</summary>
    IEnumerable<string> Noms() => Array.Empty<string>();
}

public sealed class SecretsEnMemoire : ISecretsLocaux
{
    private readonly Dictionary<string, string> _secrets = new();

    public string? Lire(string nom) => _secrets.GetValueOrDefault(nom);

    public void Ecrire(string nom, string? valeur)
    {
        if (valeur is null)
            _secrets.Remove(nom);
        else
            _secrets[nom] = valeur;
    }

    public IEnumerable<string> Noms() => _secrets.Keys.ToList();
}
