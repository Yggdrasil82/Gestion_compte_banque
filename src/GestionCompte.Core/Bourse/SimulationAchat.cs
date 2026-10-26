namespace GestionCompte.Core.Bourse;

/// <summary>Achat d'un titre à simuler : quantité, prix d'achat et de revente souhaités, frais et enveloppe.</summary>
/// <param name="FraisAchat">Courtage de l'achat ; null : tarif de Trade Republic (<see cref="SimulationAchat.FraisAchatParDefaut"/>).</param>
/// <param name="FraisVente">Courtage de la vente ; null : 1 €.</param>
/// <param name="Ttf">Taxe sur les transactions financières : actions de sociétés françaises de plus d'un milliard d'euros.</param>
public sealed record DemandeSimulationAchat(
    decimal Quantite, decimal PrixAchat, decimal PrixVente, EnveloppeBourse Enveloppe, bool Ttf, int Annee,
    decimal? FraisAchat = null, decimal? FraisVente = null);

/// <param name="CoutTotal">Achat, frais et taxe compris.</param>
/// <param name="VenteNette">Revente, frais déduits.</param>
/// <param name="Impot">Flat tax du compte-titres sur le gain (0 en cas de perte ou sur le PEA).</param>
/// <param name="PrixEquilibre">Prix de revente par titre qui couvre tous les frais (ni gain, ni perte).</param>
public sealed record ResultatSimulationAchat(
    decimal MontantAchat, decimal FraisAchat, decimal Ttf, decimal CoutTotal, decimal MontantVente, decimal FraisVente,
    decimal VenteNette, decimal GainNet, decimal? GainPourcent, decimal Impot, decimal TauxImpot, decimal GainApresImpot,
    decimal PrixEquilibre);

/// <summary>Gain potentiel d'un achat puis d'une revente, frais d'entrée et de sortie et impôt compris.</summary>
public static class SimulationAchat
{
    /// <summary>Taux de la taxe sur les transactions financières depuis le 1er avril 2025 (0,3 % avant).</summary>
    public const decimal TauxTtf = 0.004m;

    /// <summary>Courtage d'un ordre chez Trade Republic.</summary>
    public const decimal FraisOrdre = 1m;

    /// <summary>Courtage réduit d'un petit achat sur le PEA (ordre jusqu'à <see cref="PlafondPetitOrdrePea"/> €).</summary>
    public const decimal FraisPetitOrdrePea = 0.05m;

    public const decimal PlafondPetitOrdrePea = 200m;

    /// <summary>Courtage d'un achat chez Trade Republic : 1 €, ou 0,05 € sur le PEA jusqu'à 200 €.</summary>
    public static decimal FraisAchatParDefaut(EnveloppeBourse enveloppe, decimal montant) =>
        enveloppe == EnveloppeBourse.Pea && montant <= PlafondPetitOrdrePea ? FraisPetitOrdrePea : FraisOrdre;

    public static ResultatSimulationAchat Calculer(DemandeSimulationAchat d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var montantAchat = Math.Round(d.Quantite * d.PrixAchat, 2);
        var fraisAchat = d.FraisAchat ?? FraisAchatParDefaut(d.Enveloppe, montantAchat);
        var ttf = d.Ttf ? Math.Round(montantAchat * TauxTtf, 2) : 0;
        var cout = montantAchat + fraisAchat + ttf;
        var montantVente = Math.Round(d.Quantite * d.PrixVente, 2);
        var fraisVente = d.FraisVente ?? FraisOrdre;
        var venteNette = montantVente - fraisVente;
        var gain = venteNette - cout;
        // Plus-value imposable : prix de cession net de frais − prix d'acquisition frais et taxe compris.
        var taux = d.Enveloppe == EnveloppeBourse.CompteTitres ? BilanBourse.TauxFlatTaxPour(d.Annee) : 0;
        var impot = Math.Round(Math.Max(0, gain) * taux, 2);
        var equilibre = d.Quantite == 0 ? 0 : Math.Ceiling((cout + fraisVente) / d.Quantite * 100) / 100;
        return new ResultatSimulationAchat(montantAchat, fraisAchat, ttf, cout, montantVente, fraisVente, venteNette, gain,
            cout == 0 ? null : Math.Round(gain / cout * 100, 2), impot, taux, gain - impot, equilibre);
    }
}
