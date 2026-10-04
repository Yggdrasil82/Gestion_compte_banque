namespace GestionCompte.Core.Modeles;

/// <summary>Base de calcul de l'assurance emprunteur d'un prêt.</summary>
public enum AssurancePret
{
    /// <summary>Taux annuel en % du capital restant dû après l'échéance (diminue avec le capital).</summary>
    CapitalRestant,

    /// <summary>Taux annuel en % du montant emprunté (fixe).</summary>
    CapitalInitial,

    /// <summary>Montant fixe par mois.</summary>
    ParMois,
}

/// <summary>Palier d'un prêt : un nombre de mois au même montant d'échéance hors assurance.</summary>
/// <param name="Mensualite">Capital + intérêts par mois ; 0 pour un différé total (seule l'assurance est payée).</param>
public sealed record PalierPret(int NombreMois, decimal Mensualite);

/// <summary>
/// Prêt en cours (immobilier, PTZ…) : ses conditions, ses paliers d'échéances et les règles
/// de remboursement anticipé de la banque.
/// </summary>
public sealed class PretImmobilier
{
    public PretImmobilier(string nom, decimal montant, decimal tauxAnnuel, PeriodeMois premiereEcheance, IEnumerable<PalierPret> paliers,
        decimal assurance = 0m, AssurancePret typeAssurance = AssurancePret.CapitalRestant)
    {
        Nom = nom;
        Montant = montant;
        TauxAnnuel = tauxAnnuel;
        PremiereEcheance = premiereEcheance;
        Paliers = paliers.ToList();
        Assurance = assurance;
        TypeAssurance = typeAssurance;
    }

    public string Nom { get; set; }

    /// <summary>Montant emprunté.</summary>
    public decimal Montant { get; set; }

    /// <summary>Taux nominal annuel fixe en % (ex. 1,6), hors assurance.</summary>
    public decimal TauxAnnuel { get; set; }

    public PeriodeMois PremiereEcheance { get; set; }

    public List<PalierPret> Paliers { get; }

    /// <summary>Taux annuel en % ou montant par mois, selon <see cref="TypeAssurance"/>.</summary>
    public decimal Assurance { get; set; }

    public AssurancePret TypeAssurance { get; set; }

    /// <summary>Mois de signature de l'offre (point de départ des anniversaires), s'il est connu.</summary>
    public PeriodeMois? Signature { get; set; }

    // ---- Conditions de remboursement anticipé (offre de prêt) ----

    /// <summary>Aucune indemnité de remboursement anticipé (ex. prêt à taux zéro).</summary>
    public bool SansIndemnite { get; set; }

    /// <summary>Indemnité : nombre de mois d'intérêts sur le capital remboursé (maximum légal : 6).</summary>
    public decimal IndemniteMoisInterets { get; set; } = 6m;

    /// <summary>Plafond de l'indemnité en % du capital remboursé (maximum légal : 3).</summary>
    public decimal IndemnitePlafond { get; set; } = 3m;

    /// <summary>Plus d'indemnité après ce nombre d'années depuis la signature (0 : jamais), sauf rachat par une autre banque.</summary>
    public int ExonerationAnnees { get; set; }

    /// <summary>Remboursement partiel minimal en % du montant emprunté (sauf pour solder le prêt).</summary>
    public decimal MinimumPourcent { get; set; } = 10m;

    /// <summary>Avant le dernier palier, un remboursement partiel ne peut que réduire la durée.</summary>
    public bool DureeSeuleAvantDernierPalier { get; set; } = true;

    /// <summary>Nombre total d'échéances prévues.</summary>
    public int DureeMois => Paliers.Sum(p => Math.Max(0, p.NombreMois));
}
