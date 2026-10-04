using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Core;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;
using GestionCompte.Data;

namespace GestionCompte.Presentation;

/// <summary>
/// Module « Prêts » : prêts en cours à paliers (immobilier, PTZ…), tableau d'amortissement expliqué,
/// simulation de remboursement anticipé avec les règles de la banque, export Excel et ajout aux charges.
/// </summary>
public sealed partial class PretsViewModel : ObservableObject
{
    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");
    private readonly CompteBancaire _compte;
    private readonly IDialogues _dialogues;
    private readonly Action _donneesModifiees;
    private readonly Action _chargesModifiees;

    /// <param name="moisDuJour">Mois en cours : échéance du mois, capital restant dû et premier mois proposé pour le remboursement anticipé.</param>
    /// <param name="donneesModifiees">Appelé après une modification des prêts (à enregistrer).</param>
    /// <param name="chargesModifiees">Appelé après l'ajout d'une échéance aux charges de la configuration.</param>
    public PretsViewModel(CompteBancaire compte, PeriodeMois moisDuJour, IDialogues dialogues, Action donneesModifiees, Action chargesModifiees,
        int selection = 0)
    {
        _compte = compte;
        MoisDuJour = moisDuJour;
        _dialogues = dialogues;
        _donneesModifiees = donneesModifiees;
        _chargesModifiees = chargesModifiees;

        Liste = new ListeEditable<PretViewModel>(compte.Prets.Select(Creer), () => Creer(NouveauPret()), ListeReorganisee);
        Liste.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ListeEditable<PretViewModel>.Selection))
                SelectionChangee(reinitialiserSimulation: true);
        };
        Liste.Selection = Liste.Elements.Count == 0 ? null : Liste.Elements[Math.Clamp(selection, 0, Liste.Elements.Count - 1)];
    }

    public PeriodeMois MoisDuJour { get; }

    /// <summary>Compte dont les prêts sont affichés.</summary>
    public CompteBancaire Compte => _compte;

    public ListeEditable<PretViewModel> Liste { get; }

    /// <summary>Mois proposés pour la 1re échéance et la signature (de 1990 à 2070).</summary>
    public static readonly IReadOnlyList<ChoixPeriode> TousLesMois =
        Enumerable.Range(0, 81 * 12).Select(i => new ChoixPeriode(new PeriodeMois(1990 + i / 12, i % 12 + 1))).ToList();

    public static readonly IReadOnlyList<string> NomsTypesAssurance = new[] { "% du capital restant", "% du montant emprunté", "€ par mois" };

    // Listes des cellules (propriétés d'instance : l'écran ne lie pas les propriétés statiques).
    public IReadOnlyList<ChoixPeriode> Mois => TousLesMois;
    public IReadOnlyList<string> TypesAssurance => NomsTypesAssurance;

    public PretViewModel? Selection => Liste.Selection;

    public bool ASelection => Liste.Selection is not null;

    public int IndexSelection => Selection is null ? 0 : Liste.Elements.IndexOf(Selection);

    // ---- Totaux de tous les prêts ----

    public decimal TotalEcheancesDuMois => Liste.Elements.Sum(p => p.EcheanceDuMois);

    public decimal TotalCapitalRestant => Liste.Elements.Sum(p => p.CapitalRestant);

    public string TexteTotaux => Liste.Elements.Count == 0
        ? "Aucun prêt : ajoutez-en un avec le bouton +."
        : $"{Liste.Elements.Count} prêt{(Liste.Elements.Count > 1 ? "s" : "")} : {Montants.Formater(TotalEcheancesDuMois)} € prélevés en {MoisDuJour.Libelle}, " +
          $"{Montants.Formater(TotalCapitalRestant)} € de capital restant dû.";

    // ---- Paliers du prêt sélectionné ----

    [RelayCommand(CanExecute = nameof(PeutModifierPaliers))]
    private void AjouterPalier()
    {
        Selection!.AjouterPalier();
        SupprimerPalierCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(PeutSupprimerPalier))]
    private void SupprimerPalier()
    {
        Selection!.SupprimerPalier();
        SupprimerPalierCommand.NotifyCanExecuteChanged();
    }

    private bool PeutModifierPaliers() => Selection is not null;

    [RelayCommand(CanExecute = nameof(PeutModifierPaliers))]
    private void EffacerSignature() => Selection!.Signature = null;

    private bool PeutSupprimerPalier() => Selection is { Paliers.Count: > 1 };

    /// <summary>Calcule l'échéance du dernier palier pour que le prêt soit soldé à la date prévue.</summary>
    [RelayCommand(CanExecute = nameof(PeutModifierPaliers))]
    private void CalculerDernierPalier()
    {
        var pret = Selection!;
        if (CalculPret.MensualiteDernierPalier(pret.Modele) is not { } mensualite)
        {
            _dialogues.Erreur("Indiquez d'abord le montant emprunté et le nombre de mois du dernier palier.");
            return;
        }
        pret.Paliers[^1].Mensualite = mensualite;
    }

    // ---- Remboursement anticipé ----

    /// <summary>Mois proposés : ceux du prêt sélectionné, à partir du mois en cours.</summary>
    public IReadOnlyList<ChoixPeriode> MoisAnticipe => Selection is { } p
        ? p.Resultat.Where(e => e.Periode >= MoisDuJour && e.CapitalRestant > 0).Select(e => new ChoixPeriode(e.Periode)).ToList()
        : Array.Empty<ChoixPeriode>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Anticipe))]
    private ChoixPeriode? _dateAnticipe;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Anticipe))]
    private decimal _montantAnticipe;

    /// <summary>Remboursement financé par une autre banque (rachat) : l'exonération après N ans ne s'applique pas.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Anticipe))]
    private bool _rachat;

    partial void OnDateAnticipeChanged(ChoixPeriode? value) => AnticipeChange();
    partial void OnMontantAnticipeChanged(decimal value) => AnticipeChange();
    partial void OnRachatChanged(bool value) => AnticipeChange();

    public ResultatAnticipe? Anticipe => Selection is { } p && DateAnticipe is { } date
        ? CalculPret.SimulerAnticipe(p.Modele, date.Periode, MontantAnticipe, Rachat)
        : null;

    public bool AnticipeValide => Anticipe is { Erreur: null };

    public string ErreurAnticipe => Selection is null ? ""
        : DateAnticipe is null ? "Ce prêt n'a plus d'échéance à venir."
        : Anticipe?.Erreur ?? "";

    public string TexteIndemnite => Anticipe?.Indemnite is { } i ? Montants.Formater(i.Montant) + " €" : "";

    public string ExplicationIndemnite => Anticipe?.Indemnite?.Explication ?? "";

    public string TexteRembourse => Anticipe is { Erreur: null } a
        ? (a.Solde
            ? $"Prêt soldé : {Montants.Formater(a.CapitalRembourse)} € remboursés (tout le capital restant dû après l'échéance de {DateAnticipe!.Libelle})."
            : $"{Montants.Formater(a.CapitalRembourse)} € remboursés sur {Montants.Formater(a.CapitalRestantAvant)} € restant dus après l'échéance de {DateAnticipe!.Libelle}.")
        : "";

    public OptionAnticipeViewModel? OptionDuree { get; private set; }

    public OptionAnticipeViewModel? OptionMensualite { get; private set; }

    public string SansOptionMensualite => Anticipe is { Erreur: null } a ? a.SansOptionMensualite : "";

    public bool AvecOptionMensualite => OptionMensualite is not null;

    public string ConseilAnticipe
    {
        get
        {
            if (OptionDuree is not { } duree)
                return "";
            if (OptionMensualite is not { } mensualite)
                return duree.Option.GainNet > 0
                    ? $"Gain net estimé : {Montants.Formater(duree.Option.GainNet)} € (intérêts et assurance économisés, indemnité déduite)."
                    : "Avec l'indemnité, ce remboursement ne fait rien gagner.";
            var ecart = duree.Option.GainNet - mensualite.Option.GainNet;
            return $"Réduire la durée fait gagner {Montants.Formater(ecart)} € de plus ; réduire la mensualité laisse " +
                   $"{Montants.Formater(mensualite.Option.AncienneMensualite - mensualite.Option.NouvelleMensualite)} € de plus chaque mois.";
        }
    }

    // ---- Actions ----

    [RelayCommand(CanExecute = nameof(PeutModifierPaliers))]
    private void Exporter()
    {
        var pret = Selection!;
        var destination = _dialogues.ChoisirFichierExport($"{pret.Nom}.xlsx");
        if (destination is null)
            return;
        try
        {
            ExportExcel.ExporterPret(pret.Modele, destination, AnticipeValide ? Anticipe : null, DateAnticipe?.Periode);
        }
        catch (Exception e)
        {
            _dialogues.Erreur($"Le fichier Excel n'a pas pu être créé (est-il ouvert dans Excel ?).\n\n{e.Message}");
        }
    }

    /// <summary>Ajoute l'échéance du mois aux charges de la configuration (ou met à jour la charge du même nom).</summary>
    [RelayCommand(CanExecute = nameof(PeutAjouterAuxCharges))]
    private void AjouterAuxCharges()
    {
        var pret = Selection!;
        var montant = pret.ProchaineEcheance;
        var nom = pret.NomCharge;
        var charges = _compte.Configuration.Charges;
        var index = charges.FindIndex(c => CalculateurMois.MemeNom(c.Nom, nom));
        var message = index >= 0
            ? $"Remplacer le montant de la charge « {charges[index].Nom} » ({Montants.Formater(charges[index].Debit)} €) par {Montants.Formater(montant)} € ?"
            : $"Ajouter la charge « {nom} » de {Montants.Formater(montant)} € par mois à la configuration ?";
        message += "\n\nElle sera reprise dans chaque nouveau mois. Pensez à la mettre à jour quand l'échéance change de palier.";
        if (!_dialogues.Confirmer("Ajouter aux charges", message))
            return;

        if (index >= 0)
            charges[index] = charges[index] with { Debit = montant };
        else
            charges.Add(new ModeleCharge(nom, montant, Categorie: Categorie.Essentiel));
        _chargesModifiees();
    }

    private bool PeutAjouterAuxCharges() => Selection is { ProchaineEcheance: > 0 };

    /// <summary>Recalcule les indicateurs liés aux charges (après un changement de la configuration).</summary>
    public void RafraichirTout()
    {
        foreach (var pret in Liste.Elements)
            pret.Rafraichir();
        SelectionChangee(reinitialiserSimulation: false);
    }

    private PretImmobilier NouveauPret()
    {
        var pret = new PretImmobilier($"Prêt {_compte.Prets.Count + 1}", 150000m, 3.5m, MoisDuJour.Suivant(),
            new[] { new PalierPret(240, 0m) }, 0.30m);
        pret.Paliers[0] = new PalierPret(240, CalculPret.MensualiteDernierPalier(pret) ?? 0m);
        return pret;
    }

    private PretViewModel Creer(PretImmobilier modele) => new(modele, this, PretModifie);

    private void PretModifie(PretViewModel pret)
    {
        if (pret == Selection)
            SelectionChangee(reinitialiserSimulation: false);
        else
            TotauxChanges();
        _donneesModifiees();
    }

    private void ListeReorganisee()
    {
        _compte.Prets.Clear();
        _compte.Prets.AddRange(Liste.Elements.Select(p => p.Modele));
        SelectionChangee(reinitialiserSimulation: true);
        _donneesModifiees();
    }

    internal bool ChargeExiste(string nom) => _compte.Configuration.Charges.Any(c => CalculateurMois.MemeNom(c.Nom, nom));

    private void TotauxChanges()
    {
        OnPropertyChanged(nameof(TotalEcheancesDuMois));
        OnPropertyChanged(nameof(TotalCapitalRestant));
        OnPropertyChanged(nameof(TexteTotaux));
    }

    private void SelectionChangee(bool reinitialiserSimulation)
    {
        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(ASelection));
        OnPropertyChanged(nameof(MoisAnticipe));
        TotauxChanges();

        var mois = MoisAnticipe;
        if (reinitialiserSimulation || DateAnticipe is null || mois.All(m => m.Periode != DateAnticipe.Periode))
        {
            DateAnticipe = mois.FirstOrDefault(m => m.Periode == MoisDuJour) ?? mois.FirstOrDefault();
            if (reinitialiserSimulation && Selection is { } p)
                MontantAnticipe = Math.Max(1000m, decimal.Ceiling(p.Montant * p.Modele.MinimumPourcent / 100m / 1000m) * 1000m);
        }
        else
            DateAnticipe = mois.First(m => m.Periode == DateAnticipe.Periode);
        AnticipeChange();

        AjouterPalierCommand.NotifyCanExecuteChanged();
        SupprimerPalierCommand.NotifyCanExecuteChanged();
        CalculerDernierPalierCommand.NotifyCanExecuteChanged();
        EffacerSignatureCommand.NotifyCanExecuteChanged();
        ExporterCommand.NotifyCanExecuteChanged();
        AjouterAuxChargesCommand.NotifyCanExecuteChanged();
    }

    private void AnticipeChange()
    {
        var anticipe = Anticipe;
        OptionDuree = anticipe is { Erreur: null, Duree: { } duree } ? new OptionAnticipeViewModel(duree, anticipe, reduireMensualite: false) : null;
        OptionMensualite = anticipe is { Erreur: null, Mensualite: { } mensualite }
            ? new OptionAnticipeViewModel(mensualite, anticipe, reduireMensualite: true)
            : null;
        foreach (var nom in new[]
                 {
                     nameof(Anticipe), nameof(AnticipeValide), nameof(ErreurAnticipe), nameof(TexteIndemnite), nameof(ExplicationIndemnite),
                     nameof(TexteRembourse), nameof(OptionDuree), nameof(OptionMensualite), nameof(SansOptionMensualite),
                     nameof(AvecOptionMensualite), nameof(ConseilAnticipe),
                 })
            OnPropertyChanged(nom);
    }

    internal static string Taux(decimal valeur) => valeur.ToString("0.###", Francais);
}

/// <summary>Un prêt : ses conditions (modifiables), ses paliers et son tableau d'amortissement.</summary>
public sealed class PretViewModel : ObservableObject
{
    private readonly PretsViewModel _parent;
    private readonly Action<PretViewModel> _modifie;

    public PretViewModel(PretImmobilier modele, PretsViewModel parent, Action<PretViewModel> modifie)
    {
        Modele = modele;
        _parent = parent;
        _modifie = modifie;
        Paliers = new ObservableCollection<PalierViewModel>(modele.Paliers.Select((_, i) => new PalierViewModel(this, i)));
        Resultat = Array.Empty<EcheancePret>();
        Rafraichir();
    }

    public PretImmobilier Modele { get; }

    public ObservableCollection<PalierViewModel> Paliers { get; }

    public IReadOnlyList<EcheancePret> Resultat { get; private set; }

    public string Nom
    {
        get => Modele.Nom;
        set => Modifier(value ?? "", Modele.Nom, v => Modele.Nom = v);
    }

    public decimal Montant
    {
        get => Modele.Montant;
        set => Modifier(Math.Max(0m, value), Modele.Montant, v => Modele.Montant = v);
    }

    public decimal TauxAnnuel
    {
        get => Modele.TauxAnnuel;
        set => Modifier(Math.Max(0m, value), Modele.TauxAnnuel, v => Modele.TauxAnnuel = v);
    }

    public ChoixPeriode? PremiereEcheance
    {
        get => PretsViewModel.TousLesMois.FirstOrDefault(p => p.Periode == Modele.PremiereEcheance) ?? new ChoixPeriode(Modele.PremiereEcheance);
        set
        {
            if (value is not null)
                Modifier(value.Periode, Modele.PremiereEcheance, v => Modele.PremiereEcheance = v);
        }
    }

    public decimal Assurance
    {
        get => Modele.Assurance;
        set => Modifier(Math.Max(0m, value), Modele.Assurance, v => Modele.Assurance = v);
    }

    public string TypeAssurance
    {
        get => PretsViewModel.NomsTypesAssurance[(int)Modele.TypeAssurance];
        set
        {
            var index = PretsViewModel.NomsTypesAssurance.ToList().IndexOf(value);
            if (index >= 0)
                Modifier((AssurancePret)index, Modele.TypeAssurance, v => Modele.TypeAssurance = v);
        }
    }

    public ChoixPeriode? Signature
    {
        get => Modele.Signature is { } s ? PretsViewModel.TousLesMois.FirstOrDefault(p => p.Periode == s) : null;
        set => Modifier(value?.Periode, Modele.Signature, v => Modele.Signature = v);
    }

    public bool SansIndemnite
    {
        get => Modele.SansIndemnite;
        set => Modifier(value, Modele.SansIndemnite, v => Modele.SansIndemnite = v);
    }

    public decimal IndemniteMoisInterets
    {
        get => Modele.IndemniteMoisInterets;
        set => Modifier(Math.Clamp(value, 0m, 12m), Modele.IndemniteMoisInterets, v => Modele.IndemniteMoisInterets = v);
    }

    public decimal IndemnitePlafond
    {
        get => Modele.IndemnitePlafond;
        set => Modifier(Math.Clamp(value, 0m, 100m), Modele.IndemnitePlafond, v => Modele.IndemnitePlafond = v);
    }

    public int ExonerationAnnees
    {
        get => Modele.ExonerationAnnees;
        set => Modifier(Math.Clamp(value, 0, 50), Modele.ExonerationAnnees, v => Modele.ExonerationAnnees = v);
    }

    public decimal MinimumPourcent
    {
        get => Modele.MinimumPourcent;
        set => Modifier(Math.Clamp(value, 0m, 100m), Modele.MinimumPourcent, v => Modele.MinimumPourcent = v);
    }

    public bool DureeSeuleAvantDernierPalier
    {
        get => Modele.DureeSeuleAvantDernierPalier;
        set => Modifier(value, Modele.DureeSeuleAvantDernierPalier, v => Modele.DureeSeuleAvantDernierPalier = v);
    }

    // ---- Résultats ----

    public IReadOnlyList<LignePretViewModel> Tableau { get; private set; } = Array.Empty<LignePretViewModel>();

    public EcheancePret? EcheanceCourante => CalculPret.EcheanceDu(Resultat, _parent.MoisDuJour);

    /// <summary>Prélèvement du mois en cours (0 si le prêt n'a pas d'échéance ce mois-ci).</summary>
    public decimal EcheanceDuMois => EcheanceCourante?.Mensualite ?? 0m;

    /// <summary>Échéance du mois en cours, ou la prochaine si le prêt n'a pas encore commencé.</summary>
    public decimal ProchaineEcheance => (EcheanceCourante ?? Resultat.FirstOrDefault(e => e.Periode > _parent.MoisDuJour))?.Mensualite ?? 0m;

    public decimal CapitalRestant => CalculPret.CapitalRestantAu(Modele, Resultat, _parent.MoisDuJour);

    public decimal InteretsRestants => Resultat.Where(e => e.Periode > _parent.MoisDuJour).Sum(e => e.Interets);

    public decimal AssuranceRestante => Resultat.Where(e => e.Periode > _parent.MoisDuJour).Sum(e => e.Assurance);

    public int EcheancesRestantes => Resultat.Count(e => e.Periode > _parent.MoisDuJour);

    public decimal CoutInterets => Resultat.Sum(e => e.Interets);

    public decimal CoutAssurance => Resultat.Sum(e => e.Assurance);

    public decimal CoutTotal => CoutInterets + CoutAssurance;

    public string Fin => Resultat.Count > 0 ? Resultat[^1].Periode.Libelle : "—";

    public string DureeTexte => $"{Resultat.Count} mois" + (Resultat.Count % 12 == 0 && Resultat.Count > 0 ? $" ({Resultat.Count / 12} ans)" : "");

    public string NomCharge => Credit.EstUnCredit(Nom) ? Nom.Trim() : $"Prêt {Nom.Trim()}";

    public bool DansLesCharges => _parent.ChargeExiste(NomCharge);

    public string TexteBoutonCharges => DansLesCharges ? "Mettre à jour la charge" : "Ajouter aux charges";

    /// <summary>Alerte quand les paliers ne soldent pas le prêt (la dernière échéance devient très différente).</summary>
    public string Avertissement
    {
        get
        {
            if (Resultat.Count == 0)
                return "Indiquez le montant emprunté et au moins un palier.";
            var derniere = Resultat[^1];
            var prevue = Modele.Paliers.LastOrDefault(p => p.NombreMois > 0)?.Mensualite ?? 0m;
            if (Resultat.Count < Modele.DureeMois)
                return $"Les échéances saisies soldent le prêt dès {derniere.Periode.Libelle}, avant la fin des paliers : vérifiez les montants.";
            return Math.Abs(derniere.HorsAssurance - prevue) > Math.Max(5m, prevue * 0.05m)
                ? $"La dernière échéance ({Montants.Formater(derniere.HorsAssurance)} €) ne correspond pas au dernier palier : " +
                  "vérifiez les paliers, ou utilisez « Calculer » pour le dernier."
                : "";
        }
    }

    // ---- Explications (infobulles) ----

    public string ExplicationEcheanceDuMois => EcheanceCourante is { } e
        ? $"Prélevé en {e.Periode.Libelle} : {Montants.Formater(e.Capital)} € de capital + {Montants.Formater(e.Interets)} € d'intérêts + " +
          $"{Montants.Formater(e.Assurance)} € d'assurance. Survolez une ligne du tableau pour le détail du calcul."
        : $"Pas d'échéance en {_parent.MoisDuJour.Libelle} (prêt pas encore commencé ou déjà remboursé).";

    public string ExplicationCapitalRestant =>
        $"Capital encore dû à la banque après l'échéance de {_parent.MoisDuJour.Libelle} : montant emprunté moins le capital déjà remboursé. " +
        "C'est la somme à rembourser pour solder le prêt (plus l'indemnité éventuelle).";

    public string ExplicationInteretsRestants =>
        $"Somme des intérêts des {EcheancesRestantes} échéances à venir, si le prêt va à son terme. " +
        "Chaque mois : capital restant dû × taux ÷ 12. C'est ce qu'un remboursement anticipé peut faire économiser.";

    public string ExplicationCout =>
        $"Total des intérêts ({Montants.Formater(CoutInterets)} €) et de l'assurance ({Montants.Formater(CoutAssurance)} €) sur toute la durée du prêt, " +
        "hors frais de dossier et de garantie.";

    public string ExplicationAssurance => Modele.TypeAssurance switch
    {
        AssurancePret.CapitalRestant =>
            $"Assurance = capital restant dû après l'échéance × {PretsViewModel.Taux(Modele.Assurance)} % ÷ 12 : elle baisse chaque mois avec le capital.",
        AssurancePret.CapitalInitial =>
            $"Assurance = montant emprunté × {PretsViewModel.Taux(Modele.Assurance)} % ÷ 12 : la même chaque mois.",
        _ => $"Assurance fixe de {Montants.Formater(Modele.Assurance)} € par mois.",
    };

    // ---- Paliers ----

    internal void ModifierPalier(int index, PalierPret palier)
    {
        if (Modele.Paliers[index] == palier)
            return;
        Modele.Paliers[index] = palier;
        Rafraichir();
        _modifie(this);
    }

    internal void AjouterPalier()
    {
        Modele.Paliers.Add(new PalierPret(60, Modele.Paliers.LastOrDefault()?.Mensualite ?? 0m));
        Paliers.Add(new PalierViewModel(this, Modele.Paliers.Count - 1));
        Rafraichir();
        _modifie(this);
    }

    internal void SupprimerPalier()
    {
        if (Modele.Paliers.Count <= 1)
            return;
        Modele.Paliers.RemoveAt(Modele.Paliers.Count - 1);
        Paliers.RemoveAt(Paliers.Count - 1);
        Rafraichir();
        _modifie(this);
    }

    /// <summary>Recalcule le tableau et prévient l'écran.</summary>
    public void Rafraichir()
    {
        Resultat = CalculPret.Tableau(Modele);
        var mois = 0;
        foreach (var palier in Paliers)
        {
            palier.Debut = Modele.PremiereEcheance;
            for (var i = 0; i < mois; i++)
                palier.Debut = palier.Debut.Suivant();
            mois += Math.Max(0, palier.NombreMois);
            palier.Rafraichir();
        }
        Tableau = Resultat.Select(e => new LignePretViewModel(e, Modele, e.Periode == _parent.MoisDuJour)).ToList();
        foreach (var nom in new[]
                 {
                     nameof(Resultat), nameof(Tableau), nameof(EcheanceCourante), nameof(EcheanceDuMois), nameof(ProchaineEcheance),
                     nameof(CapitalRestant), nameof(InteretsRestants), nameof(AssuranceRestante), nameof(EcheancesRestantes),
                     nameof(CoutInterets), nameof(CoutAssurance), nameof(CoutTotal), nameof(Fin), nameof(DureeTexte), nameof(Avertissement),
                     nameof(ExplicationEcheanceDuMois), nameof(ExplicationCapitalRestant), nameof(ExplicationInteretsRestants),
                     nameof(ExplicationCout), nameof(ExplicationAssurance), nameof(NomCharge), nameof(DansLesCharges), nameof(TexteBoutonCharges),
                 })
            OnPropertyChanged(nom);
    }

    private void Modifier<T>(T valeur, T actuelle, Action<T> affecter, [System.Runtime.CompilerServices.CallerMemberName] string? propriete = null)
    {
        if (EqualityComparer<T>.Default.Equals(valeur, actuelle))
            return;
        affecter(valeur);
        OnPropertyChanged(propriete);
        Rafraichir();
        _modifie(this);
    }
}

/// <summary>Un palier d'un prêt : nombre de mois et échéance hors assurance.</summary>
public sealed class PalierViewModel : ObservableObject
{
    private readonly PretViewModel _pret;
    private readonly int _index;

    public PalierViewModel(PretViewModel pret, int index)
    {
        _pret = pret;
        _index = index;
        Debut = pret.Modele.PremiereEcheance;
        for (var i = 0; i < pret.Modele.Paliers.Take(index).Sum(p => Math.Max(0, p.NombreMois)); i++)
            Debut = Debut.Suivant();
    }

    private PalierPret Modele => _pret.Modele.Paliers[_index];

    public int Numero => _index + 1;

    public int NombreMois
    {
        get => Modele.NombreMois;
        set => _pret.ModifierPalier(_index, Modele with { NombreMois = Math.Clamp(value, 0, CalculPret.DureeMaximale) });
    }

    /// <summary>Échéance hors assurance (0 : différé, seule l'assurance est payée).</summary>
    public decimal Mensualite
    {
        get => Modele.Mensualite;
        set => _pret.ModifierPalier(_index, Modele with { Mensualite = Math.Max(0m, value) });
    }

    public PeriodeMois Debut { get; internal set; }

    public string Periode
    {
        get
        {
            if (NombreMois <= 0)
                return "—";
            var fin = Debut;
            for (var i = 1; i < NombreMois; i++)
                fin = fin.Suivant();
            return $"{Debut.Libelle} → {fin.Libelle}";
        }
    }

    public string Explication => Mensualite == 0
        ? "Différé : aucun capital remboursé pendant ce palier, seule l'assurance est prélevée (et les intérêts, s'il y en a, s'ajoutent au capital)."
        : $"Chaque mois de ce palier, {Montants.Formater(Mensualite)} € hors assurance : les intérêts du mois, le reste rembourse le capital.";

    internal void Rafraichir()
    {
        OnPropertyChanged(nameof(NombreMois));
        OnPropertyChanged(nameof(Mensualite));
        OnPropertyChanged(nameof(Periode));
        OnPropertyChanged(nameof(Explication));
    }
}

/// <summary>Une ligne du tableau d'amortissement, avec le détail de son calcul en infobulle.</summary>
public sealed class LignePretViewModel
{
    public LignePretViewModel(EcheancePret echeance, PretImmobilier pret, bool moisEnCours)
    {
        Echeance = echeance;
        MoisEnCours = moisEnCours;
        var assurance = pret.TypeAssurance switch
        {
            AssurancePret.CapitalRestant =>
                $"{Montants.Formater(echeance.CapitalRestant + echeance.Anticipe)} € restant dus × {PretsViewModel.Taux(pret.Assurance)} % ÷ 12",
            AssurancePret.CapitalInitial => $"{Montants.Formater(pret.Montant)} € empruntés × {PretsViewModel.Taux(pret.Assurance)} % ÷ 12",
            _ => "montant fixe",
        };
        Explication =
            $"Échéance n° {echeance.Numero}, {echeance.Periode.Libelle} (palier {echeance.Palier + 1})\n" +
            $"Intérêts : {Montants.Formater(echeance.CapitalAvant)} € restant dus × {PretsViewModel.Taux(pret.TauxAnnuel)} % ÷ 12 = {Montants.Formater(echeance.Interets)} €\n" +
            $"Capital : {Montants.Formater(echeance.HorsAssurance)} € d'échéance − {Montants.Formater(echeance.Interets)} € d'intérêts = {Montants.Formater(echeance.Capital)} €\n" +
            $"Assurance : {assurance} = {Montants.Formater(echeance.Assurance)} €\n" +
            $"Prélevé : {Montants.Formater(echeance.Capital)} + {Montants.Formater(echeance.Interets)} + {Montants.Formater(echeance.Assurance)} = {Montants.Formater(echeance.Mensualite)} €\n" +
            $"Restant dû : {Montants.Formater(echeance.CapitalAvant)} − {Montants.Formater(echeance.Capital)} = {Montants.Formater(echeance.CapitalRestant + echeance.Anticipe)} €" +
            (echeance.Anticipe > 0 ? $"\nRemboursement anticipé : − {Montants.Formater(echeance.Anticipe)} € → {Montants.Formater(echeance.CapitalRestant)} €" : "");
    }

    public EcheancePret Echeance { get; }
    public bool MoisEnCours { get; }
    public int Numero => Echeance.Numero;
    public string Mois => Echeance.Periode.Libelle;
    public decimal Mensualite => Echeance.Mensualite;
    public decimal Capital => Echeance.Capital;
    public decimal Interets => Echeance.Interets;
    public decimal Assurance => Echeance.Assurance;
    public decimal CapitalRestant => Echeance.CapitalRestant;
    public string Explication { get; }
}

/// <summary>Une option de remboursement anticipé (réduire la durée ou la mensualité), pour l'affichage.</summary>
public sealed class OptionAnticipeViewModel
{
    public OptionAnticipeViewModel(OptionAnticipe option, ResultatAnticipe resultat, bool reduireMensualite)
    {
        Option = option;
        Titre = reduireMensualite ? "Réduire la mensualité" : "Réduire la durée";
        Fin = option.Fin?.Libelle ?? "Prêt soldé";
        MoisGagnes = option.MoisGagnes > 0 ? $"{option.MoisGagnes} mois de moins" : "Même fin";
        NouvelleMensualite = option.Fin is null ? "—" : Montants.Formater(option.NouvelleMensualite) + " €";
        AncienneMensualite = Montants.Formater(option.AncienneMensualite) + " €";
        InteretsEconomises = Montants.Formater(option.InteretsEconomises) + " €";
        AssuranceEconomisee = Montants.Formater(option.AssuranceEconomisee) + " €";
        GainNet = Montants.Formater(option.GainNet) + " €";
        GainPositif = option.GainNet >= 0;
        var indemnite = resultat.Indemnite?.Montant ?? 0m;
        Explication = (reduireMensualite
                ? "Les échéances restantes baissent toutes dans la même proportion, pour finir à la même date : " +
                  "facteur = capital restant × (1 + t)^n ÷ Σ échéance × (1 + t)^(n − k), avec t = taux ÷ 12."
                : "Les échéances restent les mêmes : le capital baisse plus vite et le prêt se termine plus tôt.") +
            $"\n\nIntérêts économisés = intérêts prévus après le remboursement ({Montants.Formater(option.InteretsEconomises)} € de moins) ; " +
            $"assurance économisée : {Montants.Formater(option.AssuranceEconomisee)} €." +
            $"\nGain net = {Montants.Formater(option.InteretsEconomises)} + {Montants.Formater(option.AssuranceEconomisee)} − {Montants.Formater(indemnite)} d'indemnité = {GainNet}.";
    }

    public OptionAnticipe Option { get; }
    public string Titre { get; }
    public string Fin { get; }
    public string MoisGagnes { get; }
    public string NouvelleMensualite { get; }
    public string AncienneMensualite { get; }
    public string InteretsEconomises { get; }
    public string AssuranceEconomisee { get; }
    public string GainNet { get; }
    public bool GainPositif { get; }
    public string Explication { get; }
}
