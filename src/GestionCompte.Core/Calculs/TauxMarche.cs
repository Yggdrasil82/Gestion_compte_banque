using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GestionCompte.Core.Achats;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Core.Calculs;

/// <summary>Lien vers une page consultée par l'IA pour les taux.</summary>
public sealed record SourceTaux(string Nom, string Adresse);

/// <summary>Taux du moment trouvés par une IA pour un type de crédit et une durée (en % par an).</summary>
public sealed class TauxMarche
{
    public string Source { get; set; } = "";
    public decimal? Bas { get; set; }
    public decimal? Moyen { get; set; }
    public decimal? Haut { get; set; }

    /// <summary>Taux d'usure (TAEG maximum légal) de la catégorie, publié par la Banque de France.</summary>
    public decimal? Usure { get; set; }

    /// <summary>Taux annuel moyen de l'assurance emprunteur, en % du capital emprunté.</summary>
    public decimal? Assurance { get; set; }

    /// <summary>Période des taux (ex. « septembre 2026 »).</summary>
    public string Periode { get; set; } = "";

    public List<SourceTaux> Liens { get; } = new();

    /// <summary>Réponse faite sans recherche internet (refusée par la clé gratuite) : taux indicatifs.</summary>
    public bool SansRecherche { get; set; }

    public bool Vide => Moyen is null && Bas is null && Haut is null && Usure is null;
}

public static class RechercheTaux
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// Catégorie du crédit telle que la Banque de France la découpe pour le taux d'usure :
    /// par durée pour l'immobilier, par tranche de montant pour les autres prêts (jamais le montant exact).
    /// </summary>
    public static string Categorie(TypeCredit type, int dureeMois, decimal montant)
    {
        var duree = dureeMois % 12 == 0 ? $"{dureeMois / 12} ans" : $"{dureeMois} mois";
        return type switch
        {
            TypeCredit.Immobilier => $"crédit immobilier à taux fixe sur {duree} " +
                                     (dureeMois < 120 ? "(moins de 10 ans)" : dureeMois < 240 ? "(de 10 à moins de 20 ans)" : "(20 ans et plus)"),
            _ => $"{(type == TypeCredit.AutoMoto ? "crédit auto / moto" : "crédit à la consommation (prêt personnel)")} sur {duree}, " +
                 (montant <= 3000 ? "montant jusqu'à 3 000 €" : montant <= 6000 ? "montant de 3 000 à 6 000 €" : "montant de plus de 6 000 €"),
        };
    }

    /// <summary>Demande envoyée aux IA : la catégorie seulement, aucune donnée du budget.</summary>
    public static string Demande(string categorie, DateTime aujourdhui) =>
        $"Nous sommes le {aujourdhui.ToString("d MMMM yyyy", Francais)}. Recherche sur internet les taux actuels en France pour un {categorie}.\n" +
        "Donne : le taux nominal annuel bas (très bons dossiers), moyen et haut constatés ce mois-ci chez les courtiers et les banques ; " +
        "le taux d'usure en vigueur pour cette catégorie (publié chaque trimestre par la Banque de France) ; " +
        "le taux annuel moyen de l'assurance emprunteur en % du capital emprunté.\n" +
        "Réponds uniquement avec un objet JSON, sans texte autour, au format :\n" +
        "{\"bas\":3.10,\"moyen\":3.45,\"haut\":3.90,\"usure\":5.80,\"assurance\":0.30,\"periode\":\"septembre 2026\"," +
        "\"sources\":[{\"nom\":\"Nom du site\",\"url\":\"https://…\"}]}\n" +
        "Taux en % (3.45 pour 3,45 %). Mets null pour une valeur non trouvée. N'invente ni taux ni adresse : n'inclus que des pages trouvées pendant la recherche.";

    /// <summary>Taux de l'objet JSON contenu dans la réponse (même entouré de texte ou de ```json) ; null s'il n'y en a pas.</summary>
    public static TauxMarche? Extraire(string reponse, string source)
    {
        var debut = reponse.IndexOf('{');
        var fin = reponse.LastIndexOf('}');
        if (debut < 0 || fin <= debut)
            return null;

        JsonObject? objet;
        try
        {
            objet = JsonNode.Parse(reponse[debut..(fin + 1)], documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
        if (objet is null)
            return null;

        var taux = new TauxMarche
        {
            Source = source,
            Bas = Taux(objet["bas"]),
            Moyen = Taux(objet["moyen"]),
            Haut = Taux(objet["haut"]),
            Usure = Taux(objet["usure"]),
            Assurance = Taux(objet["assurance"], maximum: 3m),
            Periode = objet["periode"] is JsonValue p && p.ToString().Trim() is { Length: > 0 } t ? t : "",
        };
        foreach (var lien in objet["sources"] as JsonArray ?? new JsonArray())
        {
            if (lien is not JsonObject l || l["url"]?.ToString().Trim() is not { } adresse || FusionOffres.Domaine(adresse) is not { } domaine)
                continue;
            var nom = l["nom"]?.ToString().Trim();
            taux.Liens.Add(new SourceTaux(string.IsNullOrEmpty(nom) ? domaine : nom, adresse));
        }
        return taux.Vide ? null : taux;
    }

    /// <summary>Un taux plausible en % (entre 0 et 30), lu en nombre ou en texte (« 3,45 % »).</summary>
    private static decimal? Taux(JsonNode? noeud, decimal maximum = 30m)
    {
        if (noeud is not JsonValue valeur)
            return null;
        decimal? nombre = valeur.TryGetValue<decimal>(out var d) ? d
            : decimal.TryParse(valeur.ToString().Replace("%", "").Replace(',', '.').Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var t) ? t
            : null;
        return nombre is > 0 && nombre <= maximum ? decimal.Round(nombre.Value, 2) : null;
    }

    /// <summary>
    /// TAEG approché de la simulation : taux nominal + assurance (en % par an, ou ramenée au capital si elle est en € par mois).
    /// Sans les frais de dossier ni de garantie : le vrai TAEG est un peu plus élevé.
    /// </summary>
    public static decimal TaegApproche(SimulationCredit simulation) =>
        simulation.TauxAnnuel + (simulation.TypeAssurance == TypeAssurance.Pourcentage
            ? simulation.Assurance
            : simulation.Montant > 0 ? decimal.Round(simulation.Assurance * 12m / simulation.Montant * 100m, 2) : 0m);

    /// <summary>Taux d'usure retenu parmi les réponses : le plus bas, par prudence.</summary>
    public static decimal? Usure(IEnumerable<TauxMarche> resultats) =>
        resultats.Where(r => r.Usure is not null).Select(r => r.Usure!.Value).DefaultIfEmpty().Min() is var min && min > 0 ? min : null;
}
