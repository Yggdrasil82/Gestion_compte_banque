using System.Text.Json;
using System.Text.Json.Serialization;
using GestionCompte.Core.Impots;

namespace GestionCompte.Data.Impots;

/// <summary>Réglages du module Impôts (« impots.json » dans le dossier des données, communs à tous les comptes).</summary>
public sealed class FichierImpots
{
    public const string NomFichier = "impots.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _chemin;

    public FichierImpots(string dossier) => _chemin = Path.Combine(dossier, NomFichier);

    public ParametresImpot Parametres { get; set; } = new();
    public FoyerFiscal Foyer { get; set; } = new();
    public List<AffectationRevenu> Affectations { get; set; } = new();
    public List<RevenuManuel> RevenusManuels { get; set; } = new();

    public void Charger()
    {
        if (!File.Exists(_chemin))
            return;
        var contenu = JsonSerializer.Deserialize<Contenu>(File.ReadAllText(_chemin), Options)
            ?? throw new InvalidDataException("Le fichier des impôts est illisible.");
        Parametres = contenu.Parametres ?? new ParametresImpot();
        Foyer = contenu.Foyer ?? new FoyerFiscal();
        Affectations = contenu.Affectations ?? new List<AffectationRevenu>();
        RevenusManuels = contenu.RevenusManuels ?? new List<RevenuManuel>();
    }

    public void Enregistrer()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_chemin)!);
        var temporaire = _chemin + ".tmp";
        File.WriteAllText(temporaire, JsonSerializer.Serialize(new Contenu(1, Parametres, Foyer, Affectations, RevenusManuels), Options));
        File.Move(temporaire, _chemin, true);
    }

    private sealed record Contenu(int Version, ParametresImpot? Parametres, FoyerFiscal? Foyer, List<AffectationRevenu>? Affectations,
        List<RevenuManuel>? RevenusManuels);
}
