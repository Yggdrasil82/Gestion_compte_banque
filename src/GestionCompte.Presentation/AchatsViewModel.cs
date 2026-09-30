using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Core;
using GestionCompte.Core.Achats;
using GestionCompte.Data.Achats;

namespace GestionCompte.Presentation;

/// <summary>Achat à ajouter au prévisionnel (nom et prix proposés).</summary>
public sealed record DemandeAchat(string Libelle, decimal Prix);

/// <summary>
/// Module « Achats » : recherche du meilleur prix par Gemini et Mistral (offres fusionnées, prix relus sur les pages),
/// liste de sites modifiable, suivi du prix de produits avec prix cible, et « Prévoir l'achat » dans le prévisionnel.
/// </summary>
public sealed partial class AchatsViewModel : ObservableObject
{
    private readonly ServicesIA _ia;
    private readonly IDialogues _dialogues;
    private readonly LecteurPages _lecteur;
    private readonly Func<DateTime> _maintenant;
    private readonly FichierAchats _fichier;

    /// <param name="dossier">Dossier des données de l'application (« achats.json »).</param>
    /// <param name="http">Client HTTP pour relire les pages produits (remplacé dans les tests).</param>
    /// <param name="relirePrix">Relit les prix des produits suivis au démarrage (une fois par jour).</param>
    public AchatsViewModel(string dossier, ServicesIA ia, IDialogues dialogues, HttpClient? http = null,
        Func<DateTime>? maintenant = null, bool relirePrix = true)
    {
        _ia = ia;
        _dialogues = dialogues;
        _lecteur = new LecteurPages(http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromSeconds(30),
        });
        _maintenant = maintenant ?? (() => DateTime.Now);
        _fichier = new FichierAchats(dossier);
        try
        {
            _fichier.Charger();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Erreur = $"La liste des achats n'a pas pu être lue : {e.Message}";
        }
        _ia.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IADisponibles));
        MettreAJourSites();
        MettreAJourProduits();
        Chargement = relirePrix && _fichier.Produits.Any(p => p.Releves.LastOrDefault()?.Date.Date != _maintenant().Date)
            ? RelirePrix()
            : Task.CompletedTask;
    }

    /// <summary>Relecture des prix lancée au démarrage.</summary>
    public Task Chargement { get; }

    /// <summary>Demande d'ajout d'un achat au prévisionnel (gérée par l'écran principal).</summary>
    public event EventHandler<DemandeAchat>? AchatAPrevoir;

    [ObservableProperty] private string _statut = "";
    [ObservableProperty] private string _erreur = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RechercherCommand), nameof(RelirePrixCommand))]
    private bool _occupe;

    [ObservableProperty] private string _titreNavigation = "Achats";

    // ---- Recherche par les IA ----

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RechercherCommand), nameof(OuvrirSitesCommand))]
    private string _recherche = "";

    public bool IADisponibles => _ia.Disponibles;

    [ObservableProperty] private IReadOnlyList<OffreViewModel> _offres = Array.Empty<OffreViewModel>();
    [ObservableProperty] private string _resume = "";

    public bool AvecOffres => Offres.Count > 0;

    partial void OnOffresChanged(IReadOnlyList<OffreViewModel> value) => OnPropertyChanged(nameof(AvecOffres));

    private bool PeutRechercher() => !Occupe && Recherche.Trim().Length >= 2;

    [RelayCommand(CanExecute = nameof(PeutRechercher))]
    private async Task Rechercher()
    {
        var produit = Recherche.Trim();
        var assistants = _ia.Assistants();
        if (assistants.Count == 0)
        {
            Erreur = _ia.Actives
                ? "Aucune IA réglée : saisissez une clé Gemini ou Mistral (Configuration › Intelligence artificielle), ou utilisez la liste des sites."
                : "Les IA sont coupées (Configuration › Intelligence artificielle) : utilisez la liste des sites.";
            return;
        }

        Occupe = true;
        Erreur = "";
        Offres = Array.Empty<OffreViewModel>();
        Resume = "";
        Statut = $"Recherche par {string.Join(" et ", assistants.Select(a => a.Nom))}…";
        try
        {
            var reponses = await Task.WhenAll(assistants.Select(async ia =>
            {
                try
                {
                    return (ia, Offres: await RechercheOffres.RechercherAsync(ia, produit), Erreur: (string?)null);
                }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
                {
                    return (ia, Offres: (IReadOnlyList<OffreTrouvee>)Array.Empty<OffreTrouvee>(),
                        Erreur: e is OperationCanceledException ? $"{ia.Nom} n'a pas répondu à temps." : e.Message);
                }
            }));

            var offres = FusionOffres.Fusionner(reponses.SelectMany(r => r.Offres));
            Statut = offres.Count > 0 ? "Vérification des prix sur les pages…" : "";
            await _lecteur.VerifierAsync(offres);
            offres = FusionOffres.Fusionner(offres);

            Offres = offres.Select(o => new OffreViewModel(o)).ToList();
            var parIA = string.Join(", ", reponses.Select(r => $"{r.ia.Nom} : {r.Offres.Count}"));
            Resume = offres.Count == 0
                ? $"Aucune offre trouvée ({parIA})."
                : $"{offres.Count} offres ({parIA}), la moins chère : {Euros(offres[0].Prix)} chez {offres[0].Site}.";
            Erreur = string.Join("\n", reponses.Where(r => r.Erreur is not null).Select(r => r.Erreur));
            Statut = offres.Count == 0 ? "" : "Les prix « à vérifier » n'ont pas pu être relus sur la page : ouvrez-la avant d'acheter.";
        }
        finally
        {
            Occupe = false;
        }
    }

    [RelayCommand]
    private void OuvrirOffre(OffreViewModel? offre)
    {
        if (offre is not null)
            _dialogues.OuvrirLien(offre.Offre.Adresse);
    }

    [RelayCommand]
    private void SuivreOffre(OffreViewModel? offre)
    {
        if (offre is null)
            return;
        var o = offre.Offre;
        if (_fichier.Produits.Any(p => FusionOffres.Cle(p.Adresse) == FusionOffres.Cle(o.Adresse)))
        {
            Statut = "Ce produit est déjà suivi.";
            return;
        }
        var produit = new ProduitSuivi { Nom = string.IsNullOrWhiteSpace(o.Titre) ? $"{Recherche.Trim()} ({o.Site})" : $"{o.Titre} ({o.Site})", Adresse = o.Adresse };
        // Seul un prix relu sur la page compte comme relevé ; sinon il sera lu à la prochaine relecture.
        if (o.Verification != VerificationOffre.AVerifier)
            produit.Noter(new RelevePrix { Date = _maintenant(), Prix = o.Prix });
        _fichier.Produits.Add(produit);
        Enregistrer();
        MettreAJourProduits();
        Statut = $"Prix suivi : {produit.Nom}.";
    }

    [RelayCommand]
    private void PrevoirOffre(OffreViewModel? offre)
    {
        if (offre is not null)
            AchatAPrevoir?.Invoke(this, new DemandeAchat(string.IsNullOrWhiteSpace(offre.Titre) ? Recherche.Trim() : offre.Titre, offre.Prix));
    }

    // ---- Sites ----

    [ObservableProperty] private IReadOnlyList<SiteRecherche> _sites = Array.Empty<SiteRecherche>();
    [ObservableProperty] private SiteRecherche? _siteSelectionne;

    private void MettreAJourSites() => Sites = _fichier.Sites.ToList();

    private bool ARecherche() => Recherche.Trim().Length > 0;

    [RelayCommand]
    private void OuvrirSite(SiteRecherche? site)
    {
        if (site is null)
            return;
        if (!ARecherche())
        {
            Erreur = "Tapez d'abord le produit cherché.";
            return;
        }
        _dialogues.OuvrirLien(site.AdressePour(Recherche));
    }

    /// <summary>Ouvre la recherche sur tous les sites cochés (un onglet du navigateur par site).</summary>
    [RelayCommand(CanExecute = nameof(ARecherche))]
    private void OuvrirSites()
    {
        foreach (var site in _fichier.Sites.Where(s => s.Actif))
            _dialogues.OuvrirLien(site.AdressePour(Recherche));
    }

    [RelayCommand]
    private void AjouterSite()
    {
        if (_dialogues.DemanderSite(null) is not { } site)
            return;
        _fichier.Sites.Add(site);
        Enregistrer();
        MettreAJourSites();
    }

    [RelayCommand]
    private void ModifierSite(SiteRecherche? site)
    {
        if (site is null || _dialogues.DemanderSite(site) is not { } modifie)
            return;
        site.Nom = modifie.Nom;
        site.Adresse = modifie.Adresse;
        Enregistrer();
        MettreAJourSites();
    }

    [RelayCommand]
    private void SupprimerSite(SiteRecherche? site)
    {
        if (site is null || !_dialogues.Confirmer("Retirer le site", $"Retirer {site.Nom} de la liste des sites ?"))
            return;
        _fichier.Sites.Remove(site);
        Enregistrer();
        MettreAJourSites();
    }

    [RelayCommand]
    private void SiteCoche() => Enregistrer();

    [RelayCommand]
    private void ReinitialiserSites()
    {
        if (!_dialogues.Confirmer("Liste des sites", "Revenir à la liste de sites d'origine ? Les sites ajoutés seront retirés."))
            return;
        _fichier.Sites.Clear();
        _fichier.Sites.AddRange(SiteRecherche.ParDefaut());
        Enregistrer();
        MettreAJourSites();
    }

    // ---- Produits suivis ----

    [ObservableProperty] private IReadOnlyList<ProduitViewModel> _produits = Array.Empty<ProduitViewModel>();

    public bool AvecProduits => Produits.Count > 0;

    private void MettreAJourProduits()
    {
        Produits = _fichier.Produits.Select(p => new ProduitViewModel(p)).ToList();
        var alertes = _fichier.Produits.Count(p => p.CibleAtteinte);
        TitreNavigation = alertes > 0 ? $"Achats ({alertes})" : "Achats";
        OnPropertyChanged(nameof(AvecProduits));
    }

    [RelayCommand]
    private async Task SuivreAdresse()
    {
        var adresse = _dialogues.DemanderNom("Suivre le prix d'un produit",
            "Adresse de la page du produit (copiée depuis le navigateur) :", "")?.Trim();
        if (string.IsNullOrEmpty(adresse))
            return;
        if (FusionOffres.Domaine(adresse) is null)
        {
            Erreur = "Adresse incorrecte : copiez l'adresse complète de la page (https://…).";
            return;
        }

        Occupe = true;
        Erreur = "";
        Statut = "Lecture de la page…";
        var produit = new ProduitSuivi { Adresse = adresse, Nom = FusionOffres.Domaine(adresse)! };
        try
        {
            var page = await _lecteur.LireAsync(adresse);
            produit.Nom = Raccourcir(page.Titre) ?? produit.Nom;
            produit.Noter(new RelevePrix
            {
                Date = _maintenant(),
                Prix = page.Prix,
                Erreur = page.Prix is null ? "Prix introuvable sur la page." : null,
            });
            Statut = page.Prix is { } prix ? $"Prix actuel : {Euros(prix)}." : "Produit ajouté, mais la page n'indique pas son prix de façon lisible.";
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or ArgumentException or IOException)
        {
            produit.Noter(new RelevePrix { Date = _maintenant(), Erreur = e.Message });
            Statut = $"Produit ajouté, mais la page n'a pas pu être lue : {e.Message}";
        }
        finally
        {
            Occupe = false;
        }

        _fichier.Produits.Add(produit);
        Enregistrer();
        MettreAJourProduits();
    }

    private bool PeutRelire() => !Occupe;

    /// <summary>Relit le prix de chaque produit suivi (au démarrage, puis à la demande).</summary>
    [RelayCommand(CanExecute = nameof(PeutRelire))]
    private async Task RelirePrix()
    {
        if (_fichier.Produits.Count == 0)
            return;
        Occupe = true;
        Statut = "Relecture des prix suivis…";
        try
        {
            await Task.WhenAll(_fichier.Produits.Select(async produit =>
            {
                try
                {
                    var page = await _lecteur.LireAsync(produit.Adresse);
                    produit.Noter(new RelevePrix
                    {
                        Date = _maintenant(),
                        Prix = page.Prix,
                        Erreur = page.Prix is null ? "Prix introuvable sur la page." : null,
                    });
                }
                catch (Exception e) when (e is HttpRequestException or OperationCanceledException or ArgumentException or IOException)
                {
                    produit.Noter(new RelevePrix { Date = _maintenant(), Erreur = e.Message });
                }
            }));
            Enregistrer();
            MettreAJourProduits();
            var atteints = _fichier.Produits.Where(p => p.CibleAtteinte).Select(p => p.Nom).ToList();
            Statut = atteints.Count > 0 ? $"Prix cible atteint : {string.Join(", ", atteints)}." : "Prix suivis relus.";
        }
        finally
        {
            Occupe = false;
        }
    }

    [RelayCommand]
    private void ModifierCible(ProduitViewModel? produit)
    {
        if (produit is null)
            return;
        var saisie = _dialogues.DemanderNom("Prix cible",
            $"Prix à partir duquel vous voulez être prévenu pour « {produit.Nom} » (vide : pas d'alerte) :",
            produit.Produit.PrixCible is { } cible ? Montants.Formater(cible) : "");
        if (saisie is null)
            return;
        if (string.IsNullOrWhiteSpace(saisie))
            produit.Produit.PrixCible = null;
        else if (Montants.TryLire(saisie, out var montant) && montant > 0)
            produit.Produit.PrixCible = montant;
        else
        {
            Erreur = "Prix cible incorrect.";
            return;
        }
        Enregistrer();
        MettreAJourProduits();
    }

    [RelayCommand]
    private void RenommerProduit(ProduitViewModel? produit)
    {
        if (produit is null || _dialogues.DemanderNom("Renommer", "Nom du produit :", produit.Nom) is not { Length: > 0 } nom)
            return;
        produit.Produit.Nom = nom.Trim();
        Enregistrer();
        MettreAJourProduits();
    }

    [RelayCommand]
    private void OuvrirProduit(ProduitViewModel? produit)
    {
        if (produit is not null)
            _dialogues.OuvrirLien(produit.Produit.Adresse);
    }

    [RelayCommand]
    private void SupprimerProduit(ProduitViewModel? produit)
    {
        if (produit is null || !_dialogues.Confirmer("Ne plus suivre", $"Ne plus suivre le prix de « {produit.Nom} » ?"))
            return;
        _fichier.Produits.Remove(produit.Produit);
        Enregistrer();
        MettreAJourProduits();
    }

    [RelayCommand]
    private void PrevoirProduit(ProduitViewModel? produit)
    {
        if (produit is null)
            return;
        if (produit.Produit.DernierPrix is not { } prix)
        {
            Erreur = "Le prix de ce produit n'est pas connu : ouvrez la page pour le voir.";
            return;
        }
        AchatAPrevoir?.Invoke(this, new DemandeAchat(produit.Nom, prix));
    }

    private void Enregistrer()
    {
        try
        {
            _fichier.Enregistrer();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Erreur = $"La liste des achats n'a pas pu être enregistrée : {e.Message}";
        }
    }

    private static string? Raccourcir(string? titre) =>
        titre is null ? null : titre.Length > 90 ? titre[..90].TrimEnd() + "…" : titre;

    internal static string Euros(decimal montant) => $"{Montants.Formater(montant)} €";
}

public sealed class OffreViewModel
{
    public OffreViewModel(OffreTrouvee offre) => Offre = offre;

    public OffreTrouvee Offre { get; }

    public string Site => Offre.Site;
    public string Titre => Offre.Titre;
    public decimal Prix => Offre.Prix;
    public string Remarque => Offre.Remarque ?? "";

    /// <summary>Ex. « Gemini + Mistral ».</summary>
    public string Sources => string.Join(" + ", Offre.Sources.OrderBy(s => s));

    public bool Verifie => Offre.Verification != VerificationOffre.AVerifier;

    public string Verification => Offre.Verification switch
    {
        VerificationOffre.Verifie => "prix vérifié sur la page",
        VerificationOffre.Corrige => "prix relu sur la page (l'IA donnait un autre prix)",
        _ => "à vérifier",
    };
}

public sealed class ProduitViewModel
{
    public ProduitViewModel(ProduitSuivi produit) => Produit = produit;

    public ProduitSuivi Produit { get; }

    public string Nom => Produit.Nom;
    public string Site => FusionOffres.Domaine(Produit.Adresse) ?? "";
    public decimal? Prix => Produit.DernierPrix;
    public bool CibleAtteinte => Produit.CibleAtteinte;

    public string PrixTexte => Produit.DernierPrix is { } prix ? AchatsViewModel.Euros(prix) : "prix inconnu";

    /// <summary>Ex. « −12,00 € depuis le relevé précédent », ou l'erreur du dernier relevé.</summary>
    public string Evolution
    {
        get
        {
            var dernier = Produit.Releves.LastOrDefault();
            if (dernier?.Erreur is { } erreur)
                return $"Dernière lecture impossible : {erreur}";
            if (Produit.DernierPrix is not { } prix || Produit.PrixPrecedent is not { } avant || prix == avant)
                return Produit.PlusBas is { } bas && Produit.Releves.Count > 1 ? $"stable · plus bas {AchatsViewModel.Euros(bas)}" : "";
            var ecart = prix - avant;
            return $"{(ecart > 0 ? "+" : "−")}{AchatsViewModel.Euros(Math.Abs(ecart))} depuis le relevé précédent · plus bas {AchatsViewModel.Euros(Produit.PlusBas ?? prix)}";
        }
    }

    public bool Baisse => Produit.DernierPrix < Produit.PrixPrecedent;

    public string Cible => Produit.PrixCible is { } cible
        ? CibleAtteinte ? $"Prix cible atteint ({AchatsViewModel.Euros(cible)})" : $"Cible : {AchatsViewModel.Euros(cible)}"
        : "Pas de prix cible";

    public string DateReleve => Produit.Releves.LastOrDefault() is { } r ? $"relevé le {r.Date.ToString("dd/MM/yyyy", Montants.Francais)}" : "";
}
