using System.Text.Json;
using GestionCompte.Core.Bourse;

namespace GestionCompte.Data;

/// <summary>Le portefeuille de bourse enregistré en JSON dans la table « bourse ».</summary>
internal sealed class PortefeuilleEnregistre
{
    public List<OperationEnregistree> Operations { get; set; } = new();
    public List<CoursEnregistre> Cours { get; set; } = new();

    internal sealed class OperationEnregistree
    {
        public string Id { get; set; } = "";
        public DateTime Date { get; set; }
        public EnveloppeBourse Enveloppe { get; set; }
        public TypeOperationBourse Type { get; set; }
        public string? Nom { get; set; }
        public string? Isin { get; set; }
        public string? Classe { get; set; }
        public decimal Quantite { get; set; }
        public decimal Prix { get; set; }
        public decimal Montant { get; set; }
        public decimal Frais { get; set; }
        public decimal Taxes { get; set; }
        public string Description { get; set; } = "";
    }

    internal sealed class CoursEnregistre
    {
        public string Isin { get; set; } = "";
        public decimal? Valeur { get; set; }
        public DateTime? Date { get; set; }
        public bool Manuel { get; set; }
        public string? Symbole { get; set; }
    }

    public static string Ecrire(Portefeuille portefeuille) => JsonSerializer.Serialize(new PortefeuilleEnregistre
    {
        Operations = portefeuille.Operations.Select(o => new OperationEnregistree
        {
            Id = o.Identifiant, Date = o.Date, Enveloppe = o.Enveloppe, Type = o.Type, Nom = o.Nom, Isin = o.Isin, Classe = o.Classe,
            Quantite = o.Quantite, Prix = o.Prix, Montant = o.Montant, Frais = o.Frais, Taxes = o.Taxes, Description = o.Description,
        }).ToList(),
        Cours = portefeuille.Cours.Select(c => new CoursEnregistre
        {
            Isin = c.Key, Valeur = c.Value.Valeur, Date = c.Value.Date, Manuel = c.Value.Manuel, Symbole = c.Value.Symbole,
        }).ToList(),
    });

    /// <summary>Relit le portefeuille dans <paramref name="portefeuille"/> ; des données illisibles laissent le portefeuille vide.</summary>
    public static void Lire(string json, Portefeuille portefeuille)
    {
        try
        {
            if (JsonSerializer.Deserialize<PortefeuilleEnregistre>(json) is not { } e)
                return;
            portefeuille.Ajouter(e.Operations.Select(o => new OperationBourse(o.Id, o.Date, o.Enveloppe, o.Type, o.Nom, o.Isin, o.Classe,
                o.Quantite, o.Prix, o.Montant, o.Frais, o.Taxes, o.Description)));
            foreach (var c in e.Cours.Where(c => c.Isin.Length > 0))
                portefeuille.Cours[c.Isin] = new CoursTitre(c.Valeur, c.Date, c.Manuel, c.Symbole);
        }
        catch (JsonException)
        {
        }
    }
}
