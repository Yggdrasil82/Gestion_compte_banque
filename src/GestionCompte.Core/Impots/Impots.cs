namespace GestionCompte.Core.Impots;

/// <summary>Comment un revenu du budget est imposé.</summary>
public enum TypeRevenuFiscal
{
    /// <summary>Pas imposable (remboursements, virements entre comptes, aides…).</summary>
    NonImposable,

    /// <summary>Salaire, traitement, pension : déduction de 10 % ou frais réels.</summary>
    Salaire,

    /// <summary>Loyer d'une location vide (revenus fonciers, micro-foncier).</summary>
    LoyerNu,

    /// <summary>Loyer d'une location meublée non professionnelle (LMNP, micro-BIC).</summary>
    LoyerMeuble,
}

/// <summary>Une tranche du barème : le taux s'applique à partir de <paramref name="Seuil"/> (revenu par part).</summary>
public sealed record TrancheImpot(decimal Seuil, decimal Taux);

/// <summary>
/// Barème et taux de l'impôt sur le revenu. Les valeurs par défaut sont celles de la loi de finances pour 2026 (revenus
/// 2025) et de la loi de financement de la sécurité sociale pour 2026 ; elles se corrigent dans le module.
/// </summary>
public sealed class ParametresImpot
{
    public List<TrancheImpot> Tranches { get; set; } = new()
    {
        new(0m, 0m), new(11_600m, 0.11m), new(29_579m, 0.30m), new(84_577m, 0.41m), new(181_917m, 0.45m),
    };

    /// <summary>Décote : montant pour une personne seule et pour un couple, diminué de <see cref="TauxDecote"/> × impôt.</summary>
    public decimal DecoteSeul { get; set; } = 897m;

    public decimal DecoteCouple { get; set; } = 1_483m;

    public decimal TauxDecote { get; set; } = 0.4525m;

    /// <summary>Avantage maximal du quotient familial par demi-part (enfants).</summary>
    public decimal PlafondDemiPart { get; set; } = 1_807m;

    /// <summary>Avantage maximal de la part entière du premier enfant d'un parent seul (case T).</summary>
    public decimal PlafondParentIsole { get; set; } = 4_262m;

    /// <summary>Déduction forfaitaire de 10 % des salaires : minimum et maximum pour chaque personne du foyer.</summary>
    public decimal TauxAbattementSalaires { get; set; } = 0.10m;

    public decimal AbattementMinimum { get; set; } = 509m;

    public decimal AbattementMaximum { get; set; } = 14_555m;

    /// <summary>Micro-foncier (location vide) : abattement et plafond des loyers.</summary>
    public decimal AbattementMicroFoncier { get; set; } = 0.30m;

    public decimal PlafondMicroFoncier { get; set; } = 15_000m;

    /// <summary>Micro-BIC (location meublée de longue durée) : abattement et plafond des loyers.</summary>
    public decimal AbattementMicroBic { get; set; } = 0.50m;

    public decimal PlafondMicroBic { get; set; } = 77_700m;

    /// <summary>Prélèvements sociaux sur les revenus fonciers (location vide).</summary>
    public decimal PrelevementsSociauxFonciers { get; set; } = 0.172m;

    /// <summary>Prélèvements sociaux sur la location meublée et les placements (bourse).</summary>
    public decimal PrelevementsSociauxPlacements { get; set; } = 0.186m;

    /// <summary>Part impôt de la flat tax (prélèvement forfaitaire unique) sur les plus-values.</summary>
    public decimal TauxForfaitaire { get; set; } = 0.128m;

    /// <summary>Plafond d'épargne retraite (PER) : part des revenus professionnels, minimum et maximum.</summary>
    public decimal TauxPlafondPer { get; set; } = 0.10m;

    public decimal PlafondPerMinimum { get; set; } = 4_710m;

    public decimal PlafondPerMaximum { get; set; } = 37_680m;

    /// <summary>Dons : réduction et limite (part du revenu imposable).</summary>
    public decimal TauxReductionDons { get; set; } = 0.66m;

    public decimal LimiteDons { get; set; } = 0.20m;

    /// <summary>Emploi à domicile : crédit d'impôt, plafond des dépenses, majoration par enfant et plafond majoré.</summary>
    public decimal TauxEmploiDomicile { get; set; } = 0.50m;

    public decimal PlafondEmploiDomicile { get; set; } = 12_000m;

    public decimal MajorationEmploiDomicile { get; set; } = 1_500m;

    public decimal PlafondEmploiDomicileMajore { get; set; } = 15_000m;
}

/// <summary>Le foyer fiscal et ce qui a déjà été versé dans l'année.</summary>
public sealed class FoyerFiscal
{
    public bool EnCouple { get; set; }

    /// <summary>Enfants à charge. Une personne seule avec enfants est comptée comme parent isolé (case T).</summary>
    public int Enfants { get; set; }

    /// <summary>Taux du prélèvement à la source (en %), celui de la fiche de paie ou de impots.gouv.fr.</summary>
    public decimal TauxPrelevement { get; set; }

    /// <summary>Frais réels des salaires de « Vous » ; null pour la déduction de 10 %.</summary>
    public decimal? FraisReels { get; set; }

    /// <summary>Versements de l'année sur un plan d'épargne retraite (PER).</summary>
    public decimal VersementsPer { get; set; }

    /// <summary>Dons aux associations.</summary>
    public decimal Dons { get; set; }

    /// <summary>Dépenses d'emploi à domicile (ménage, garde, jardinage…), aides déduites.</summary>
    public decimal EmploiDomicile { get; set; }

    /// <summary>Gains de bourse imposés au barème plutôt qu'à la flat tax (option globale de la déclaration, case 2OP).</summary>
    public bool OptionBareme { get; set; }

    public FoyerFiscal Copier() => (FoyerFiscal)MemberwiseClone();
}

/// <summary>Personnes du foyer à qui un salaire peut appartenir.</summary>
public static class PersonnesFoyer
{
    public const string Vous = "Vous";
    public const string Conjoint = "Conjoint";

    public static IReadOnlyList<string> Toutes { get; } = new[] { Vous, Conjoint, "Enfant 1", "Enfant 2", "Enfant 3", "Enfant 4" };

    /// <summary>Personne d'un revenu ; « Vous » si elle n'est pas précisée (réglages d'avant la 2.15.2).</summary>
    public static string De(string? personne) => string.IsNullOrWhiteSpace(personne) ? Vous : personne;
}

/// <summary>Un revenu annuel, déjà ramené au montant imposable avant abattement (salaire net imposable, loyers perçus).</summary>
/// <param name="Personne">Personne du foyer qui perçoit le salaire : la déduction de 10 % se calcule pour chacune.</param>
public sealed record RevenuFiscal(string Libelle, TypeRevenuFiscal Type, decimal Montant, string? Personne = null);

/// <summary>Une idée pour payer moins, avec l'économie estimée quand elle se chiffre.</summary>
public sealed record PisteImpot(string Titre, string Texte, decimal? Economie);

/// <summary>Résultat de l'estimation.</summary>
public sealed record EstimationImpot
{
    public decimal Salaires { get; init; }
    public decimal DeductionSalaires { get; init; }
    public bool FraisReelsRetenus { get; init; }
    public decimal LoyersNus { get; init; }
    public decimal FoncierImposable { get; init; }
    public decimal LoyersMeubles { get; init; }
    public decimal MeubleImposable { get; init; }
    public decimal DeductionPer { get; init; }
    public decimal PlafondPer { get; init; }

    /// <summary>Plus-values de bourse (compte-titres) de l'année, positives ou négatives.</summary>
    public decimal PlusValues { get; init; }

    /// <summary>Revenu net imposable au barème.</summary>
    public decimal RevenuImposable { get; init; }

    public decimal Parts { get; init; }

    /// <summary>Impôt du barème après quotient familial, avant décote.</summary>
    public decimal ImpotBrut { get; init; }

    public bool QuotientPlafonne { get; init; }
    public decimal Decote { get; init; }
    public decimal ReductionDons { get; init; }
    public decimal CreditEmploiDomicile { get; init; }

    /// <summary>Impôt sur le revenu au barème, réductions et crédits déduits (négatif : remboursement).</summary>
    public decimal ImpotRevenu { get; init; }

    /// <summary>Part impôt (12,8 %) de la flat tax sur les plus-values.</summary>
    public decimal ImpotForfaitaire { get; init; }

    /// <summary>Prélèvements sociaux sur les loyers et les plus-values.</summary>
    public decimal PrelevementsSociaux { get; init; }

    /// <summary>Tranche marginale d'imposition.</summary>
    public decimal Tmi { get; init; }

    /// <summary>Tout ce qui est dû : impôt sur le revenu, flat tax et prélèvements sociaux.</summary>
    public decimal Total => ImpotRevenu + ImpotForfaitaire + PrelevementsSociaux;

    /// <summary>Impôt sur le revenu (barème et flat tax) rapporté au revenu imposable.</summary>
    public decimal TauxMoyen { get; init; }

    /// <summary>Revenus soumis au prélèvement à la source : salaires et loyers imposables.</summary>
    public decimal BasePrelevement { get; init; }

    /// <summary>Prélevé dans l'année au taux saisi.</summary>
    public decimal Preleve { get; init; }

    /// <summary>Taux de prélèvement qui correspondrait à l'estimation (en %).</summary>
    public decimal TauxCalcule { get; init; }

    /// <summary>Reste à payer (positif) ou à rembourser (négatif) l'été suivant, sur l'impôt au barème.</summary>
    public decimal Solde => ImpotRevenu - Preleve;

    public IReadOnlyList<string> Remarques { get; init; } = Array.Empty<string>();
}

/// <summary>Estimation simplifiée de l'impôt sur le revenu d'un foyer (indicative, sans valeur officielle).</summary>
public static class CalculImpot
{
    public static EstimationImpot Calculer(ParametresImpot p, FoyerFiscal foyer, IEnumerable<RevenuFiscal> revenus, decimal plusValues)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(foyer);
        var liste = revenus.ToList();
        var remarques = new List<string>();

        // Déduction de 10 % (minimum et maximum) ou frais réels : pour chaque personne du foyer.
        var salairesParPersonne = liste.Where(r => r.Type == TypeRevenuFiscal.Salaire)
            .GroupBy(r => PersonnesFoyer.De(r.Personne))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Montant));
        var salaires = salairesParPersonne.Values.Sum();
        decimal deduction = 0, salairesNets = 0;
        var fraisReels = false;
        var netsParPersonne = new Dictionary<string, decimal>();
        foreach (var (personne, montant) in salairesParPersonne)
        {
            var forfait = montant <= 0 ? 0 : Math.Min(montant, Math.Clamp(Arrondi(montant * p.TauxAbattementSalaires),
                p.AbattementMinimum, p.AbattementMaximum));
            var deduit = forfait;
            if (personne == PersonnesFoyer.Vous && foyer.FraisReels is { } frais && frais > forfait)
            {
                fraisReels = true;
                deduit = Math.Min(Math.Max(0, montant), frais);
            }
            deduction += deduit;
            salairesNets += montant - deduit;
            netsParPersonne[personne] = montant - deduit;
        }

        var loyersNus = liste.Where(r => r.Type == TypeRevenuFiscal.LoyerNu).Sum(r => r.Montant);
        var foncier = Arrondi(Math.Max(0, loyersNus) * (1 - p.AbattementMicroFoncier));
        if (loyersNus > p.PlafondMicroFoncier)
            remarques.Add($"Loyers de location vide au-dessus de {Montants.Formater(p.PlafondMicroFoncier)} € : le régime réel est obligatoire " +
                          "(charges réelles déduites). L'estimation garde l'abattement de 30 %.");
        var loyersMeubles = liste.Where(r => r.Type == TypeRevenuFiscal.LoyerMeuble).Sum(r => r.Montant);
        var meuble = Arrondi(Math.Max(0, loyersMeubles) * (1 - p.AbattementMicroBic));
        if (loyersMeubles > p.PlafondMicroBic)
            remarques.Add($"Loyers meublés au-dessus de {Montants.Formater(p.PlafondMicroBic)} € : le régime réel est obligatoire.");

        // Plafond PER : propre à chaque déclarant (vous, et le conjoint d'un couple), cumulable dans un couple.
        var declarants = foyer.EnCouple ? new[] { PersonnesFoyer.Vous, PersonnesFoyer.Conjoint } : new[] { PersonnesFoyer.Vous };
        var plafondPer = declarants.Sum(d => Math.Clamp(Arrondi(netsParPersonne.GetValueOrDefault(d) * p.TauxPlafondPer),
            p.PlafondPerMinimum, p.PlafondPerMaximum));
        var deductionPer = Math.Min(Math.Max(0, foyer.VersementsPer), plafondPer);
        if (foyer.VersementsPer > plafondPer)
            remarques.Add($"Versements PER au-dessus du plafond estimé ({Montants.Formater(plafondPer)} €) : le plafond réel, sur l'avis " +
                          "d'impôt, peut être plus haut (plafonds non utilisés des 3 années précédentes).");

        var bareme = foyer.OptionBareme ? Math.Max(0, plusValues) : 0;
        var revenuImposable = Math.Max(0, salairesNets + foncier + meuble + bareme - deductionPer);
        var parts = Parts(foyer);
        var (brut, plafonne) = ImpotBareme(p, foyer, revenuImposable, parts);
        var decoteMax = foyer.EnCouple ? p.DecoteCouple : p.DecoteSeul;
        var decote = Math.Min(brut, Math.Max(0, Arrondi(decoteMax - p.TauxDecote * brut)));
        var apresDecote = brut - decote;
        var dons = Arrondi(Math.Min(Math.Max(0, foyer.Dons), revenuImposable * p.LimiteDons) * p.TauxReductionDons);
        var reductionDons = Math.Min(dons, apresDecote);
        var plafondEmploi = Math.Min(p.PlafondEmploiDomicile + p.MajorationEmploiDomicile * Math.Max(0, foyer.Enfants), p.PlafondEmploiDomicileMajore);
        var creditEmploi = Arrondi(Math.Min(Math.Max(0, foyer.EmploiDomicile), plafondEmploi) * p.TauxEmploiDomicile);
        var impotRevenu = apresDecote - reductionDons - creditEmploi;

        var gains = Math.Max(0, plusValues);
        var forfaitaire = foyer.OptionBareme ? 0 : Arrondi(gains * p.TauxForfaitaire);
        var sociaux = Arrondi(foncier * p.PrelevementsSociauxFonciers + meuble * p.PrelevementsSociauxPlacements
                              + gains * p.PrelevementsSociauxPlacements);
        if (plusValues < 0)
            remarques.Add($"Moins-values de bourse ({Montants.Formater(-plusValues)} €) : elles se reportent sur les plus-values des 10 années suivantes.");

        var basePrelevement = salaires + foncier + meuble;
        var preleve = Arrondi(basePrelevement * Math.Max(0, foyer.TauxPrelevement) / 100);
        var tauxCalcule = basePrelevement <= 0 ? 0 : Math.Round(Math.Max(0, apresDecote) / basePrelevement * 100, 1);

        return new EstimationImpot
        {
            Salaires = salaires,
            DeductionSalaires = deduction,
            FraisReelsRetenus = fraisReels,
            LoyersNus = loyersNus,
            FoncierImposable = foncier,
            LoyersMeubles = loyersMeubles,
            MeubleImposable = meuble,
            DeductionPer = deductionPer,
            PlafondPer = plafondPer,
            PlusValues = plusValues,
            RevenuImposable = revenuImposable,
            Parts = parts,
            ImpotBrut = brut,
            QuotientPlafonne = plafonne,
            Decote = decote,
            ReductionDons = reductionDons,
            CreditEmploiDomicile = creditEmploi,
            ImpotRevenu = impotRevenu,
            ImpotForfaitaire = forfaitaire,
            PrelevementsSociaux = sociaux,
            Tmi = Taux(p, parts == 0 ? 0 : revenuImposable / parts),
            TauxMoyen = revenuImposable + (foyer.OptionBareme ? 0 : gains) <= 0 ? 0
                : Math.Round(Math.Max(0, impotRevenu + forfaitaire) / (revenuImposable + (foyer.OptionBareme ? 0 : gains)) * 100, 1),
            BasePrelevement = basePrelevement,
            Preleve = preleve,
            TauxCalcule = tauxCalcule,
            Remarques = remarques,
        };
    }

    /// <summary>Parts du foyer : 1 (2 en couple), une demi-part pour chacun des deux premiers enfants, une part ensuite.</summary>
    public static decimal Parts(FoyerFiscal foyer)
    {
        var enfants = Math.Max(0, foyer.Enfants);
        var parts = (foyer.EnCouple ? 2m : 1m) + Math.Min(enfants, 2) * 0.5m + Math.Max(0, enfants - 2);
        if (!foyer.EnCouple && enfants > 0)
            parts += 0.5m; // parent isolé
        return parts;
    }

    /// <summary>Impôt du barème avec le plafonnement du quotient familial.</summary>
    private static (decimal Impot, bool Plafonne) ImpotBareme(ParametresImpot p, FoyerFiscal foyer, decimal revenu, decimal parts)
    {
        var base_ = foyer.EnCouple ? 2m : 1m;
        var avecEnfants = Arrondi(Bareme(p, revenu / parts) * parts);
        if (parts <= base_)
            return (avecEnfants, false);
        var sansEnfants = Arrondi(Bareme(p, revenu / base_) * base_);
        var demiParts = (parts - base_) * 2;
        var plafond = !foyer.EnCouple && foyer.Enfants > 0
            ? p.PlafondParentIsole + (demiParts - 2) * p.PlafondDemiPart
            : demiParts * p.PlafondDemiPart;
        return sansEnfants - avecEnfants > plafond ? (sansEnfants - plafond, true) : (avecEnfants, false);
    }

    /// <summary>Impôt pour une part.</summary>
    public static decimal Bareme(ParametresImpot p, decimal revenuParPart)
    {
        var tranches = p.Tranches.OrderBy(t => t.Seuil).ToList();
        decimal impot = 0;
        for (var i = 0; i < tranches.Count; i++)
        {
            var haut = i + 1 < tranches.Count ? tranches[i + 1].Seuil : decimal.MaxValue;
            if (revenuParPart <= tranches[i].Seuil)
                break;
            impot += (Math.Min(revenuParPart, haut) - tranches[i].Seuil) * tranches[i].Taux;
        }
        return impot;
    }

    /// <summary>Taux de la tranche où tombe le revenu par part.</summary>
    public static decimal Taux(ParametresImpot p, decimal revenuParPart) =>
        p.Tranches.Where(t => revenuParPart > t.Seuil).OrderBy(t => t.Seuil).LastOrDefault()?.Taux ?? 0;

    /// <summary>Idées chiffrées pour payer moins, en refaisant le calcul avec chaque changement.</summary>
    public static IReadOnlyList<PisteImpot> Pistes(ParametresImpot p, FoyerFiscal foyer, IReadOnlyList<RevenuFiscal> revenus, decimal plusValues)
    {
        var actuel = Calculer(p, foyer, revenus, plusValues);
        var pistes = new List<PisteImpot>();
        decimal Gain(Action<FoyerFiscal> changement)
        {
            var autre = foyer.Copier();
            changement(autre);
            return actuel.Total - Calculer(p, autre, revenus, plusValues).Total;
        }

        var tmi = Pourcent(actuel.Tmi);
        var margePer = actuel.PlafondPer - actuel.DeductionPer;
        if (actuel.ImpotRevenu > 0 && margePer > 0)
        {
            var versement = Math.Min(margePer, 1_000m);
            var gain = Gain(f => f.VersementsPer += versement);
            if (gain > 0)
                pistes.Add(new PisteImpot("Épargne retraite (PER)",
                    $"Verser {Montants.Formater(versement)} € sur un PER avant le 31/12 réduit l'impôt d'environ {Montants.Formater(gain)} € " +
                    $"(tranche à {tmi}). Plafond restant estimé : {Montants.Formater(margePer)} €. L'argent est bloqué jusqu'à la retraite " +
                    "(sauf achat de la résidence principale) et imposé à la sortie.", gain));
        }

        if (actuel.ImpotRevenu - actuel.CreditEmploiDomicile > 0)
        {
            var gain = Gain(f => f.Dons += 100m);
            if (gain > 0)
                pistes.Add(new PisteImpot("Dons",
                    $"Un don de 100 € à une association réduit l'impôt de {Montants.Formater(gain)} € (66 %, dans la limite de 20 % du revenu imposable). " +
                    "Gardez le reçu fiscal.", gain));
        }

        var plafondEmploi = Math.Min(p.PlafondEmploiDomicile + p.MajorationEmploiDomicile * Math.Max(0, foyer.Enfants), p.PlafondEmploiDomicileMajore);
        if (foyer.EmploiDomicile < plafondEmploi)
        {
            var depense = Math.Min(plafondEmploi - foyer.EmploiDomicile, 1_000m);
            var gain = Gain(f => f.EmploiDomicile += depense);
            pistes.Add(new PisteImpot("Emploi à domicile",
                $"{Montants.Formater(depense)} € de ménage, jardinage, garde d'enfant ou soutien scolaire (CESU, organisme agréé) donnent " +
                $"{Montants.Formater(gain)} € de crédit d'impôt, remboursé même sans impôt à payer. Plafond : {Montants.Formater(plafondEmploi)} € de dépenses par an.",
                gain));
        }

        if (actuel.Salaires > 0 && !actuel.FraisReelsRetenus)
            pistes.Add(new PisteImpot("Frais réels",
                $"La déduction de 10 % retire {Montants.Formater(actuel.DeductionSalaires)} € des salaires. Si vos frais de travail " +
                "(kilomètres domicile-travail au barème, repas, double résidence…) dépassent ce montant, déclarez les frais réels : " +
                $"chaque tranche de 1 000 € en plus fait environ {Montants.Formater(Arrondi(1_000m * actuel.Tmi))} € d'impôt en moins.", null));

        if (plusValues > 0)
        {
            var autre = foyer.Copier();
            autre.OptionBareme = !foyer.OptionBareme;
            var gain = actuel.Total - Calculer(p, autre, revenus, plusValues).Total;
            if (!foyer.OptionBareme && gain > 0)
                pistes.Add(new PisteImpot("Bourse : option pour le barème",
                    $"Avec une tranche à {tmi}, déclarer les plus-values au barème (case 2OP) plutôt qu'à la flat tax ferait environ " +
                    $"{Montants.Formater(gain)} € d'impôt en moins. L'option vaut pour tous les revenus de placement de l'année.", gain));
            else if (foyer.OptionBareme && gain > 0)
                pistes.Add(new PisteImpot("Bourse : rester à la flat tax",
                    $"Sans l'option pour le barème, les plus-values seraient taxées à 12,8 % : environ {Montants.Formater(gain)} € d'impôt en moins.", gain));

            var economiePea = Arrondi(plusValues * p.TauxForfaitaire);
            pistes.Add(new PisteImpot("Bourse : PEA plutôt que compte-titres",
                $"Les plus-values du compte-titres ({Montants.Formater(plusValues)} € cette année) paient 12,8 % d'impôt en plus des prélèvements sociaux. " +
                $"Dans un PEA de plus de 5 ans, seuls les prélèvements sociaux sont dus : environ {Montants.Formater(economiePea)} € de moins " +
                "pour les mêmes gains (actions européennes seulement, versements jusqu'à 150 000 €).", null));
        }

        if (actuel.LoyersNus > 0)
            pistes.Add(new PisteImpot("Location vide : régime réel",
                "Le micro-foncier retire 30 % des loyers pour les charges. Si vos charges réelles (intérêts d'emprunt, travaux, taxe foncière, " +
                "assurance, gestion) dépassent 30 % des loyers, le régime réel (déclaration 2044) est plus intéressant ; un déficit foncier " +
                "se déduit du revenu global jusqu'à 10 700 € par an. Le choix vaut pour 3 ans.", null));
        if (actuel.LoyersMeubles > 0)
            pistes.Add(new PisteImpot("Location meublée : régime réel (LMNP)",
                "Au réel, l'amortissement du logement et du mobilier s'ajoute aux charges : les loyers meublés ne sont souvent plus imposés " +
                "pendant des années. Depuis 2025, les amortissements déduits sont réintégrés dans la plus-value à la revente. " +
                "Un expert-comptable (souvent en partie remboursé par une réduction d'impôt) aide à faire ce choix.", null));
        else if (actuel.LoyersNus > 0)
            pistes.Add(new PisteImpot("Location meublée",
                $"Louer meublé : le micro-BIC retire {Pourcent(p.AbattementMicroBic)} des loyers au lieu de {Pourcent(p.AbattementMicroFoncier)}, " +
                $"mais les prélèvements sociaux passent de {Pourcent(p.PrelevementsSociauxFonciers)} à {Pourcent(p.PrelevementsSociauxPlacements)}.",
                null));

        return pistes;
    }

    private static decimal Arrondi(decimal montant) => Math.Round(montant, 0, MidpointRounding.AwayFromZero);

    public static string Pourcent(decimal taux) =>
        $"{(taux * 100).ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))} %";
}

/// <summary>Type fiscal choisi pour un revenu d'un compte, et montant imposable de l'année saisi à la main (ou null).</summary>
public sealed record AffectationRevenu(string Compte, string Revenu, TypeRevenuFiscal Type, decimal? MontantImposable, string? Personne = null);

/// <summary>Revenu ajouté à la main (perçu hors des comptes suivis), montant de l'année.</summary>
public sealed record RevenuManuel(string Libelle, TypeRevenuFiscal Type, decimal Montant, string? Personne = null);
