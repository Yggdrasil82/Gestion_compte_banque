namespace GestionCompte.Presentation;

/// <summary>
/// Autres comptes de l'application vers lesquels un virement peut être lié (colonne « Virement avec »),
/// avec la correspondance entre le nom affiché et le fichier du compte, qui ne change pas quand on le renomme.
/// </summary>
public sealed class ComptesLiables
{
    private readonly IReadOnlyList<(string Id, string Nom)> _comptes;

    public ComptesLiables(IEnumerable<(string Id, string Nom)> comptes)
    {
        _comptes = comptes.ToList();
        Noms = new[] { "" }.Concat(_comptes.Select(c => c.Nom)).ToList();
    }

    /// <summary>Un seul compte : pas de virement lié possible.</summary>
    public static ComptesLiables Aucun { get; } = new(Array.Empty<(string, string)>());

    /// <summary>Choix de la colonne (vide = virement non lié).</summary>
    public IReadOnlyList<string> Noms { get; }

    public bool Disponibles => _comptes.Count > 0;

    /// <summary>Nom affiché d'un compte, vide s'il n'existe plus.</summary>
    public string Nom(string? id) => _comptes.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)).Nom ?? "";

    public string? Id(string? nom) =>
        string.IsNullOrWhiteSpace(nom) ? null : _comptes.FirstOrDefault(c => c.Nom == nom).Id;
}
