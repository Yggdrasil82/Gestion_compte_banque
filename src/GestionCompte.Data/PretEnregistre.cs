using System.Text.Json;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Data;

/// <summary>Un prêt enregistré en JSON dans la table « pret » (montants en texte pour garder la valeur exacte).</summary>
internal sealed class PretEnregistre
{
    public string Nom { get; set; } = "";
    public decimal Montant { get; set; }
    public decimal Taux { get; set; }
    public int Annee { get; set; }
    public int Mois { get; set; }
    public List<PalierEnregistre> Paliers { get; set; } = new();
    public decimal Assurance { get; set; }
    public AssurancePret TypeAssurance { get; set; }
    public int? SignatureAnnee { get; set; }
    public int? SignatureMois { get; set; }
    public bool SansIndemnite { get; set; }
    public decimal IndemniteMoisInterets { get; set; } = 6m;
    public decimal IndemnitePlafond { get; set; } = 3m;
    public int ExonerationAnnees { get; set; }
    public decimal MinimumPourcent { get; set; } = 10m;
    public bool DureeSeuleAvantDernierPalier { get; set; } = true;

    internal sealed class PalierEnregistre
    {
        public int NombreMois { get; set; }
        public decimal Mensualite { get; set; }
    }

    public static string Ecrire(PretImmobilier pret) => JsonSerializer.Serialize(new PretEnregistre
    {
        Nom = pret.Nom,
        Montant = pret.Montant,
        Taux = pret.TauxAnnuel,
        Annee = pret.PremiereEcheance.Annee,
        Mois = pret.PremiereEcheance.Mois,
        Paliers = pret.Paliers.Select(p => new PalierEnregistre { NombreMois = p.NombreMois, Mensualite = p.Mensualite }).ToList(),
        Assurance = pret.Assurance,
        TypeAssurance = pret.TypeAssurance,
        SignatureAnnee = pret.Signature?.Annee,
        SignatureMois = pret.Signature?.Mois,
        SansIndemnite = pret.SansIndemnite,
        IndemniteMoisInterets = pret.IndemniteMoisInterets,
        IndemnitePlafond = pret.IndemnitePlafond,
        ExonerationAnnees = pret.ExonerationAnnees,
        MinimumPourcent = pret.MinimumPourcent,
        DureeSeuleAvantDernierPalier = pret.DureeSeuleAvantDernierPalier,
    });

    /// <summary>Relit un prêt ; null si les données sont illisibles (le reste du compte reste chargé).</summary>
    public static PretImmobilier? Lire(string json)
    {
        try
        {
            var e = JsonSerializer.Deserialize<PretEnregistre>(json);
            if (e is null)
                return null;
            return new PretImmobilier(e.Nom, e.Montant, e.Taux, new PeriodeMois(e.Annee, e.Mois),
                e.Paliers.Select(p => new PalierPret(p.NombreMois, p.Mensualite)), e.Assurance, e.TypeAssurance)
            {
                Signature = e.SignatureAnnee is { } annee && e.SignatureMois is { } mois ? new PeriodeMois(annee, mois) : null,
                SansIndemnite = e.SansIndemnite,
                IndemniteMoisInterets = e.IndemniteMoisInterets,
                IndemnitePlafond = e.IndemnitePlafond,
                ExonerationAnnees = e.ExonerationAnnees,
                MinimumPourcent = e.MinimumPourcent,
                DureeSeuleAvantDernierPalier = e.DureeSeuleAvantDernierPalier,
            };
        }
        catch (Exception ex) when (ex is JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
