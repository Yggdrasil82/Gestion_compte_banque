using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GestionCompte.Core.Bourse;
using GestionCompte.Data.Documents;

namespace GestionCompte.Presentation;

/// <summary>Un titre du portefeuille partagé avec Fortunix : seulement ce qu'il faut pour croiser avec la veille.</summary>
public sealed record TitrePartage(string Nom, string Isin, string Enveloppe, string Secteur, decimal Valeur, decimal Part);

/// <summary>Portefeuille partagé avec l'application Fortunix Bourses (titres et parts, aucune opération bancaire).</summary>
public sealed record PortefeuillePartage(DateTime Date, string Compte, decimal Valeur, IReadOnlyList<TitrePartage> Titres)
{
    /// <summary>Titres détenus de tout le portefeuille (compte-titres et PEA), les plus gros d'abord.</summary>
    public static PortefeuillePartage Depuis(Portefeuille portefeuille, string compte, DateTime maintenant)
    {
        var bilan = CalculBourse.Calculer(portefeuille, null, maintenant.Year);
        var total = bilan.Positions.Sum(p => p.Valeur);
        var titres = bilan.Positions
            .OrderByDescending(p => p.Valeur)
            .Select(p => new TitrePartage(p.Nom, p.Isin, p.Enveloppe == EnveloppeBourse.Pea ? "PEA" : "Compte-titres",
                portefeuille.SecteurDe(p.Isin), p.Valeur, total == 0 ? 0 : Math.Round(p.Valeur / total * 100, 1)))
            .ToList();
        return new PortefeuillePartage(maintenant, compte, total, titres);
    }
}

/// <summary>
/// Fichier « Fortunix - portefeuille.coffre » : le portefeuille en JSON, chiffré en AES-256-GCM avec une clé tirée du mot de passe
/// (PBKDF2-SHA256). Le sel et le nombre d'itérations sont écrits en clair dans l'enveloppe JSON, pour que Fortunix le relise.
/// </summary>
public static class FichierPortefeuille
{
    public const string Nom = "Fortunix - portefeuille.coffre";
    public const string Format = "fortunix-portefeuille";
    public const int LongueurMinimale = 8;

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static byte[] Creer(PortefeuillePartage portefeuille, byte[] cle, byte[] sel, int iterations)
    {
        var contenu = JsonSerializer.SerializeToUtf8Bytes(portefeuille, Options);
        var enveloppe = new JsonObject
        {
            ["format"] = Format,
            ["version"] = 1,
            ["iterations"] = iterations,
            ["sel"] = Convert.ToBase64String(sel),
            ["donnees"] = Convert.ToBase64String(ChiffrementDocuments.Chiffrer(cle, contenu)),
        };
        return Encoding.UTF8.GetBytes(enveloppe.ToJsonString());
    }

    /// <exception cref="CryptographicException">Mot de passe faux ou fichier abîmé.</exception>
    /// <exception cref="FormatException">Ce n'est pas un fichier portefeuille.</exception>
    public static PortefeuillePartage Lire(byte[] fichier, string motDePasse)
    {
        JsonNode? racine;
        try
        {
            racine = JsonNode.Parse(fichier);
        }
        catch (JsonException e)
        {
            throw new FormatException("Ce n'est pas un fichier portefeuille de Mon Budget.", e);
        }
        if (racine?["format"]?.GetValue<string>() != Format)
            throw new FormatException("Ce n'est pas un fichier portefeuille de Mon Budget.");
        var sel = Convert.FromBase64String(racine["sel"]!.GetValue<string>());
        var cle = ChiffrementDocuments.DeriverCle(motDePasse, sel, racine["iterations"]!.GetValue<int>());
        var contenu = ChiffrementDocuments.Dechiffrer(cle, Convert.FromBase64String(racine["donnees"]!.GetValue<string>()));
        return JsonSerializer.Deserialize<PortefeuillePartage>(contenu, Options) ?? throw new FormatException("Fichier portefeuille vide.");
    }
}

/// <summary>
/// Carte « Fortunix (téléphone) » de l'onglet Bourse : dépose le portefeuille chiffré dans un dossier synchronisé par Google Drive,
/// tenu à jour après chaque changement (import, cours, secteur). Fortunix le lit avec le compte Google de la personne à qui le
/// dossier est partagé, et le mot de passe choisi ici.
/// </summary>
public sealed partial class PartageFortunixViewModel : ObservableObject
{
    public const string SecretDossier = "fortunix-dossier";
    public const string SecretMotDePasse = "fortunix-mot-de-passe";
    public const string SecretSel = "fortunix-sel";
    public const string SecretCompte = "fortunix-compte";

    private static readonly CultureInfo Francais = CultureInfo.GetCultureInfo("fr-FR");
    private readonly ISecretsLocaux _secrets;
    private readonly IDialogues _dialogues;
    private readonly int _iterations;
    private readonly Func<DateTime> _maintenant;
    private byte[]? _cle;

    /// <param name="iterations">Coût du calcul de la clé (réduit dans les tests).</param>
    public PartageFortunixViewModel(ISecretsLocaux secrets, IDialogues dialogues, int iterations = ChiffrementDocuments.IterationsParDefaut,
        Func<DateTime>? maintenant = null)
    {
        _secrets = secrets;
        _dialogues = dialogues;
        _iterations = iterations;
        _maintenant = maintenant ?? (() => DateTime.Now);
        MettreAJour();
    }

    public string? Dossier => _secrets.Lire(SecretDossier);

    /// <summary>Compte dont le portefeuille est partagé (un seul).</summary>
    public string? Compte => _secrets.Lire(SecretCompte);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NonActif))]
    private bool _actif;

    public bool NonActif => !Actif;

    [ObservableProperty] private string _etat = "";

    /// <summary>Dossier enregistré quand le fichier va dans le dossier Google Drive des données (« Stocker dans Google Drive »).</summary>
    public const string DossierDonneesDrive = "google-drive";

    /// <summary>
    /// Dossier Google Drive des données quand elles y sont rangées et modifiables (null sinon) : le fichier portefeuille y est
    /// déposé à côté du coffre, Fortunix le lit avec le même compte Google.
    /// </summary>
    public Func<IStockageDocuments?>? StockageDonneesDrive { get; set; }

    private readonly SemaphoreSlim _envoi = new(1, 1);

    private bool DansDrive => Dossier == DossierDonneesDrive;

    private string Destination => DansDrive ? $"Google Drive, dossier « {Data.Nuage.CoffreDonnees.NomDossierDrive} »" : Dossier ?? "";

    /// <summary>
    /// Choisit où déposer le fichier (le dossier Google Drive des données s'il y en a, sinon un dossier synchronisé) et le mot de
    /// passe, puis dépose le portefeuille de ce compte.
    /// </summary>
    public bool Configurer(string compte, Portefeuille portefeuille)
    {
        var dossier = StockageDonneesDrive?.Invoke() is not null
            ? DossierDonneesDrive
            : _dialogues.ChoisirDossier("Dossier synchronisé par Google Drive, lu par le téléphone");
        if (dossier is null)
            return false;
        var motDePasse = _dialogues.DemanderMotDePasse("Mot de passe du portefeuille",
            $"Choisissez le mot de passe qui chiffre le portefeuille ({FichierPortefeuille.LongueurMinimale} caractères ou plus). " +
            "Il sera demandé une fois dans Fortunix. Il reste sur ce PC, chiffré par Windows.", confirmer: true);
        if (motDePasse is null)
            return false;
        if (motDePasse.Length < FichierPortefeuille.LongueurMinimale)
        {
            _dialogues.Erreur($"Le mot de passe doit faire au moins {FichierPortefeuille.LongueurMinimale} caractères.");
            return false;
        }
        var sel = ChiffrementDocuments.NouveauSel();
        _secrets.Ecrire(SecretDossier, dossier);
        _secrets.Ecrire(SecretMotDePasse, motDePasse);
        _secrets.Ecrire(SecretSel, Convert.ToBase64String(sel));
        _secrets.Ecrire(SecretCompte, compte);
        _cle = null;
        MettreAJour();
        Publier(compte, portefeuille);
        return true;
    }

    /// <summary>Dépose le portefeuille s'il s'agit du compte partagé ; une erreur est affichée dans l'état, sans fenêtre.</summary>
    public void Publier(string compte, Portefeuille portefeuille) => _ = PublierAsync(compte, portefeuille);

    /// <summary>Dans un dossier : écrit tout de suite ; dans Google Drive : envoi en arrière-plan (un seul à la fois).</summary>
    public async Task PublierAsync(string compte, Portefeuille portefeuille)
    {
        if (!Actif || !string.Equals(compte, Compte, StringComparison.CurrentCultureIgnoreCase)
            || Dossier is not { } dossier || _secrets.Lire(SecretMotDePasse) is not { } motDePasse
            || _secrets.Lire(SecretSel) is not { } selTexte)
            return;
        var destination = Destination;
        try
        {
            var sel = Convert.FromBase64String(selTexte);
            _cle ??= ChiffrementDocuments.DeriverCle(motDePasse, sel, _iterations);
            var maintenant = _maintenant();
            var contenu = FichierPortefeuille.Creer(PortefeuillePartage.Depuis(portefeuille, compte, maintenant), _cle, sel, _iterations);
            if (DansDrive)
            {
                if (StockageDonneesDrive?.Invoke() is not { } drive)
                {
                    Etat = "Portefeuille non mis à jour : les données ne sont pas ouvertes depuis Google Drive (hors connexion ou lecture seule).";
                    return;
                }
                Etat = "Envoi du portefeuille dans Google Drive…";
                await _envoi.WaitAsync();
                try
                {
                    await drive.EcrireAsync(FichierPortefeuille.Nom, contenu);
                }
                finally
                {
                    _envoi.Release();
                }
            }
            else
            {
                Directory.CreateDirectory(dossier);
                var chemin = Path.Combine(dossier, FichierPortefeuille.Nom);
                var provisoire = chemin + ".tmp";
                File.WriteAllBytes(provisoire, contenu);
                File.Move(provisoire, chemin, overwrite: true);
            }
            Etat = $"Portefeuille « {compte} » partagé le {maintenant.ToString("dd/MM/yyyy 'à' HH:mm", Francais)}, dans {destination}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or HttpRequestException
                                      or InvalidOperationException or TaskCanceledException)
        {
            Etat = $"Le portefeuille n'a pas pu être déposé dans {destination} : {e.Message}";
        }
    }

    [RelayCommand]
    private async Task Arreter()
    {
        if (!_dialogues.Confirmer("Arrêter le partage avec Fortunix",
                "Le portefeuille ne sera plus mis à jour pour le téléphone et le fichier sera supprimé. Continuer ?"))
            return;
        try
        {
            if (DansDrive)
            {
                if (StockageDonneesDrive?.Invoke() is { } drive)
                    await drive.SupprimerAsync(FichierPortefeuille.Nom);
            }
            else if (Dossier is { } dossier)
                File.Delete(Path.Combine(dossier, FichierPortefeuille.Nom));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException
                                      or TaskCanceledException)
        {
        }
        foreach (var secret in new[] { SecretDossier, SecretMotDePasse, SecretSel, SecretCompte })
            _secrets.Ecrire(secret, null);
        _cle = null;
        MettreAJour();
    }

    /// <summary>Pour les captures : carte montrée comme si le partage était réglé.</summary>
    public void AfficherExemple(string etat)
    {
        Actif = true;
        Etat = etat;
    }

    private void MettreAJour()
    {
        Actif = Dossier is { Length: > 0 } && Compte is { Length: > 0 };
        Etat = Actif ? $"Partage du portefeuille « {Compte} » réglé, dans {Destination}." : "";
    }
}
