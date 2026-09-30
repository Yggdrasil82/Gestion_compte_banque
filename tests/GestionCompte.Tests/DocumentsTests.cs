using System.Net;
using System.Text;
using System.Text.Json;
using GestionCompte.Core.Documents;
using GestionCompte.Data;
using GestionCompte.Data.Documents;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class DocumentsTests : IDisposable
{
    private const int Iterations = 1_000;
    private static readonly DateOnly Aujourdhui = new(2026, 11, 15);
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), $"gestioncompte-docs-{Guid.NewGuid():N}");

    public DocumentsTests() => Directory.CreateDirectory(_dossier);

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, true);
    }

    private CoffreDocuments Coffre(string sousDossier = "coffre") =>
        new(new StockageDossier(Path.Combine(_dossier, sousDossier)), Iterations);

    [Fact]
    public async Task Un_document_ajoute_est_relu_apres_rechargement()
    {
        var coffre = Coffre();
        await coffre.ChargerAsync();
        var contenu = Encoding.UTF8.GetBytes("RIB de test");
        await coffre.AjouterAsync(new DocumentImportant { Nom = "RIB", NomFichier = "rib.pdf", Echeance = Aujourdhui.AddDays(10) }, contenu);

        var relu = Coffre();
        await relu.ChargerAsync();

        var document = Assert.Single(relu.Documents);
        Assert.Equal("RIB", document.Nom);
        Assert.Equal(Aujourdhui.AddDays(10), document.Echeance);
        Assert.Equal(contenu.Length, document.Taille);
        Assert.Equal(contenu, await relu.LireAsync(document));
    }

    [Fact]
    public async Task Un_document_protege_est_chiffre_et_s_ouvre_avec_le_mot_de_passe_ou_la_cle_de_secours()
    {
        var coffre = Coffre();
        await coffre.ChargerAsync();
        var contenu = Encoding.UTF8.GetBytes("Carte d'identité");
        var document = await coffre.AjouterAsync(new DocumentImportant { Nom = "CNI" }, contenu);
        var secours = await coffre.ConfigurerProtectionAsync("motdepasse1");
        await coffre.ProtegerAsync(document, true);

        var fichier = await File.ReadAllBytesAsync(Path.Combine(_dossier, "coffre", CoffreDocuments.NomFichier(document)));
        Assert.NotEqual(contenu, fichier);

        var relu = Coffre();
        await relu.ChargerAsync();
        var docRelu = Assert.Single(relu.Documents);
        Assert.True(docRelu.Protege);
        await Assert.ThrowsAsync<InvalidOperationException>(() => relu.LireAsync(docRelu));
        Assert.False(relu.Deverrouiller("mauvais-mot"));
        Assert.True(relu.Deverrouiller("motdepasse1"));
        Assert.Equal(contenu, await relu.LireAsync(docRelu));

        // Mot de passe oublié : la clé de secours (saisie sans tirets, en minuscules) permet d'en choisir un autre.
        var oublie = Coffre();
        await oublie.ChargerAsync();
        Assert.False(await oublie.RecupererAsync("AAAAA-BBBBB", "nouveau-mdp"));
        Assert.True(await oublie.RecupererAsync(secours.Replace("-", "").ToLowerInvariant(), "nouveau-mdp"));
        Assert.Equal(contenu, await oublie.LireAsync(oublie.Documents[0]));

        var apres = Coffre();
        await apres.ChargerAsync();
        Assert.False(apres.Deverrouiller("motdepasse1"));
        Assert.True(apres.Deverrouiller("nouveau-mdp"));
    }

    [Fact]
    public async Task Changer_le_mot_de_passe_garde_la_cle_de_secours_et_retirer_la_protection_rend_le_fichier_lisible()
    {
        var coffre = Coffre();
        await coffre.ChargerAsync();
        var contenu = new byte[] { 1, 2, 3 };
        var document = await coffre.AjouterAsync(new DocumentImportant { Nom = "Passeport" }, contenu);
        var secours = await coffre.ConfigurerProtectionAsync("premier-mdp");
        await coffre.ProtegerAsync(document, true);
        await coffre.ChangerMotDePasseAsync("second-mdp");
        await Assert.ThrowsAsync<ArgumentException>(() => coffre.ChangerMotDePasseAsync("court"));

        var relu = Coffre();
        await relu.ChargerAsync();
        Assert.False(relu.Deverrouiller("premier-mdp"));
        Assert.True(relu.Deverrouiller("second-mdp"));
        await relu.ProtegerAsync(relu.Documents[0], false);
        relu.Verrouiller();
        Assert.Equal(contenu, await relu.LireAsync(relu.Documents[0]));

        var recupere = Coffre();
        await recupere.ChargerAsync();
        Assert.True(await recupere.RecupererAsync(secours, "troisieme-mdp"));
    }

    [Fact]
    public async Task La_copie_vers_un_autre_emplacement_garde_fiches_et_fichiers()
    {
        var coffre = Coffre();
        await coffre.ChargerAsync();
        await coffre.AjouterAsync(new DocumentImportant { Nom = "Bail" }, new byte[] { 4, 5 });
        await coffre.AjouterAsync(new DocumentImportant { Nom = "Facture" }, new byte[] { 6 });

        var destination = new StockageDossier(Path.Combine(_dossier, "drive"));
        Assert.Equal(2, await coffre.CopierVersAsync(destination));

        var copie = Coffre("drive");
        await copie.ChargerAsync();
        Assert.Equal(new[] { "Bail", "Facture" }, copie.Documents.Select(d => d.Nom).OrderBy(n => n));
        Assert.Equal(new byte[] { 4, 5 }, await copie.LireAsync(copie.Documents.First(d => d.Nom == "Bail")));
    }

    [Fact]
    public async Task Le_stockage_en_dossier_refuse_les_noms_qui_sortent_du_dossier()
    {
        var stockage = new StockageDossier(Path.Combine(_dossier, "coffre"));
        await Assert.ThrowsAsync<ArgumentException>(() => stockage.EcrireAsync("../evasion.txt", new byte[] { 1 }));
        await Assert.ThrowsAsync<ArgumentException>(() => stockage.LireAsync("sous\\fichier"));
    }

    [Fact]
    public void Les_rappels_listent_les_echeances_proches_ou_depassees()
    {
        var documents = new[]
        {
            new DocumentImportant { Nom = "Assurance", Echeance = Aujourdhui.AddDays(12) },
            new DocumentImportant { Nom = "Passeport", Echeance = Aujourdhui.AddDays(-3) },
            new DocumentImportant { Nom = "Contrôle technique", Echeance = Aujourdhui.AddDays(80), RappelJours = 90 },
            new DocumentImportant { Nom = "Lointain", Echeance = Aujourdhui.AddDays(200) },
            new DocumentImportant { Nom = "Très ancien", Echeance = Aujourdhui.AddDays(-400) },
            new DocumentImportant { Nom = "Sans échéance" },
        };

        var rappels = RappelsDocuments.Calculer(documents, Aujourdhui);

        Assert.Equal(new[] { "Passeport", "Assurance", "Contrôle technique" }, rappels.Select(r => r.Document.Nom));
        Assert.True(rappels[0].Depasse);
        Assert.Equal("Passeport : échéance dépassée depuis 3 jours (12/11/2026)", rappels[0].Texte);
        Assert.Equal("Assurance : échéance dans 12 jours (27/11/2026)", rappels[1].Texte);
    }

    [Theory]
    [InlineData("Carte identité Romain", CategorieDocument.Identite)]
    [InlineData("RIB compte joint", CategorieDocument.Banque)]
    [InlineData("Avis impot 2026", CategorieDocument.Impots)]
    [InlineData("Carte grise", CategorieDocument.Vehicule)]
    [InlineData("Attestation mutuelle", CategorieDocument.Sante)]
    [InlineData("Contrat assurance habitation", CategorieDocument.Logement)]
    [InlineData("Facture lave-linge", CategorieDocument.Garanties)]
    [InlineData("Photo", CategorieDocument.Autres)]
    public void La_categorie_est_devinee_d_apres_le_nom(string nom, CategorieDocument attendue) =>
        Assert.Equal(attendue, DocumentsViewModel.CategorieProbable(nom));

    [Fact]
    public async Task L_ecran_ajoute_un_document_enregistre_sa_fiche_et_filtre_la_liste()
    {
        var source = Path.Combine(_dossier, "Attestation mutuelle.pdf");
        await File.WriteAllBytesAsync(source, new byte[] { 9, 9, 9 });
        var dialogues = new Dialogues { Document = source };
        var vm = new DocumentsViewModel(new ApparenceViewModel(null), _dossier, new CompteGoogle(new SecretsEnMemoire(), dialogues), dialogues, Aujourdhui,
            () => new[] { "Mutuelle", "Loyer" }, Iterations);
        await vm.Chargement;
        Assert.True(vm.Vide);
        Assert.Equal(new[] { DocumentsViewModel.Aucune, "Mutuelle", "Loyer" }, vm.Charges);

        await vm.AjouterCommand.ExecuteAsync(null);

        var fiche = Assert.Single(vm.Documents);
        Assert.Same(fiche, vm.Selection);
        Assert.Equal("Attestation mutuelle", fiche.Nom);
        Assert.Equal("Santé", fiche.Categorie);
        Assert.Equal("Attestation mutuelle.pdf · 3 o", fiche.Fichier);

        fiche.Echeance = new DateTime(2026, 12, 1);
        fiche.Charge = "Mutuelle";
        fiche.Notes = "Contrat n° 1234";
        await vm.AttendreEnregistrementAsync();
        Assert.Equal("Documents (1)", vm.TitreNavigation);

        var relu = new DocumentsViewModel(new ApparenceViewModel(null), _dossier, new CompteGoogle(new SecretsEnMemoire(), dialogues), dialogues, Aujourdhui,
            () => Array.Empty<string>(), Iterations);
        await relu.Chargement;
        var ficheRelue = Assert.Single(relu.Documents);
        Assert.Equal("Mutuelle", ficheRelue.Charge);
        Assert.Equal("01/12/2026", ficheRelue.EcheanceTexte);
        Assert.Single(relu.Rappels);

        relu.Recherche = "1234";
        Assert.Single(relu.Documents);
        relu.Recherche = "";
        relu.Filtre = "Identité";
        Assert.Empty(relu.Documents);
    }

    [Fact]
    public async Task L_ecran_protege_un_document_puis_demande_le_mot_de_passe_pour_l_enregistrer()
    {
        var source = Path.Combine(_dossier, "Passeport.pdf");
        await File.WriteAllBytesAsync(source, new byte[] { 7, 7 });
        var dialogues = new Dialogues { Document = source, MotDePasse = "secret-du-coffre" };
        var vm = new DocumentsViewModel(new ApparenceViewModel(null), _dossier, new CompteGoogle(new SecretsEnMemoire(), dialogues), dialogues, Aujourdhui,
            () => Array.Empty<string>(), Iterations);
        await vm.Chargement;
        await vm.AjouterCommand.ExecuteAsync(null);

        await vm.ProtegerCommand.ExecuteAsync(null);

        Assert.Equal("", vm.Erreur);
        Assert.NotNull(dialogues.CleSecours);
        Assert.True(vm.Selection!.Protege);
        Assert.True(vm.ProtectionConfiguree);

        vm.VerrouillerCommand.Execute(null);
        Assert.False(vm.Deverrouille);
        dialogues.Emplacement = Path.Combine(_dossier, "copie.pdf");
        await vm.EnregistrerSousCommand.ExecuteAsync(null);

        Assert.Equal(2, dialogues.MotsDePasseDemandes); // création de la protection, puis déverrouillage
        Assert.Equal(new byte[] { 7, 7 }, await File.ReadAllBytesAsync(dialogues.Emplacement));
    }

    [Fact]
    public async Task Choisir_un_dossier_synchronise_propose_d_y_copier_les_documents()
    {
        var source = Path.Combine(_dossier, "Bail.pdf");
        await File.WriteAllBytesAsync(source, new byte[] { 1 });
        var synchronise = Path.Combine(_dossier, "Mon Drive", "Documents");
        var dialogues = new Dialogues { Document = source, Dossier = synchronise };
        var reglages = new ApparenceViewModel(null);
        var vm = new DocumentsViewModel(reglages, _dossier, new CompteGoogle(new SecretsEnMemoire(), dialogues), dialogues, Aujourdhui,
            () => Array.Empty<string>(), Iterations);
        await vm.Chargement;
        await vm.AjouterCommand.ExecuteAsync(null);

        await vm.ChoisirDossierSynchroniseCommand.ExecuteAsync(null);

        Assert.Equal(EmplacementDocuments.Dossier, reglages.EmplacementDocuments);
        Assert.Equal(synchronise, reglages.DossierDocuments);
        Assert.True(File.Exists(Path.Combine(synchronise, CoffreDocuments.NomIndex)));
        Assert.Single(vm.Documents);
        Assert.Contains(synchronise, vm.Emplacement);
    }

    [Fact]
    public async Task Le_stockage_google_drive_cree_son_dossier_puis_lit_ecrit_et_supprime()
    {
        var drive = new FauxDrive();
        var http = new HttpClient(drive);
        var stockage = new StockageGoogleDrive(http, _ => Task.FromResult("jeton-test"));
        var coffre = new CoffreDocuments(stockage, Iterations);
        await coffre.ChargerAsync();
        var document = await coffre.AjouterAsync(new DocumentImportant { Nom = "RIB" }, new byte[] { 1, 2 });
        await coffre.RemplacerAsync(document, new byte[] { 3 }, "rib2.pdf");

        Assert.Equal(1, drive.DossiersCrees);
        Assert.All(drive.Autorisations, a => Assert.Equal("Bearer jeton-test", a));

        // Un autre PC relit le même drive.
        var autre = new CoffreDocuments(new StockageGoogleDrive(http, _ => Task.FromResult("jeton-test")), Iterations);
        await autre.ChargerAsync();
        var relu = Assert.Single(autre.Documents);
        Assert.Equal("rib2.pdf", relu.NomFichier);
        Assert.Equal(new byte[] { 3 }, await autre.LireAsync(relu));
        Assert.Equal(1, drive.DossiersCrees);

        await autre.SupprimerAsync(relu);
        Assert.Single(drive.Fichiers.Values, f => !f.Dossier); // l'index seul
    }

    [Fact]
    public void Le_module_documents_masque_ramene_a_la_configuration()
    {
        var chemin = Path.Combine(_dossier, "compte.db");
        var vm = new MainViewModel(new DepotSqlite(chemin), new Dialogues(), new DateTime(2026, 11, 15), new ApparenceViewModel(null));
        vm.OngletSelectionne = MainViewModel.OngletDocuments;

        vm.Apparence.ModuleDocuments = false;

        Assert.Equal(MainViewModel.OngletConfiguration, vm.OngletSelectionne);
        Assert.Equal(Path.Combine(_dossier, "Documents"), vm.Documents.DossierLocal);
    }

    /// <summary>Imitation minimale de l'API Google Drive v3 (dossier, liste, envoi, mise à jour, lecture, suppression).</summary>
    private sealed class FauxDrive : HttpMessageHandler
    {
        public sealed record Fichier(string Nom, string? Parent, bool Dossier, byte[] Contenu);

        public Dictionary<string, Fichier> Fichiers { get; } = new();
        public List<string> Autorisations { get; } = new();
        public int DossiersCrees { get; private set; }
        private int _suivant;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
        {
            Autorisations.Add(requete.Headers.Authorization?.ToString() ?? "");
            var adresse = requete.RequestUri!;
            var chemin = adresse.AbsolutePath;
            var q = Uri.UnescapeDataString(adresse.Query);

            if (requete.Method == HttpMethod.Get && chemin == "/drive/v3/files")
            {
                IEnumerable<KeyValuePair<string, Fichier>> resultat = q.Contains("mimeType = 'application/vnd.google-apps.folder'")
                    ? Fichiers.Where(f => f.Value.Dossier)
                    : Fichiers.Where(f => q.Contains($"'{f.Value.Parent}' in parents"));
                return Json(new { files = resultat.Select(f => new { id = f.Key, name = f.Value.Nom }) });
            }
            if (requete.Method == HttpMethod.Post && chemin == "/drive/v3/files")
            {
                DossiersCrees++;
                var id = $"d{++_suivant}";
                var nom = JsonDocument.Parse(await requete.Content!.ReadAsStringAsync(annulation)).RootElement.GetProperty("name").GetString()!;
                Fichiers[id] = new Fichier(nom, null, true, Array.Empty<byte>());
                return Json(new { id });
            }
            if (requete.Method == HttpMethod.Post && chemin == "/upload/drive/v3/files")
            {
                var parties = ((MultipartContent)requete.Content!).ToList();
                var meta = JsonDocument.Parse(await parties[0].ReadAsStringAsync(annulation)).RootElement;
                var id = $"f{++_suivant}";
                Fichiers[id] = new Fichier(meta.GetProperty("name").GetString()!, meta.GetProperty("parents")[0].GetString(), false,
                    await parties[1].ReadAsByteArrayAsync(annulation));
                return Json(new { id });
            }

            var cle = chemin.Split('/').Last();
            if (!Fichiers.TryGetValue(cle, out var fichier))
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            if (requete.Method == HttpMethod.Patch)
            {
                Fichiers[cle] = fichier with { Contenu = await requete.Content!.ReadAsByteArrayAsync(annulation) };
                return Json(new { id = cle });
            }
            if (requete.Method == HttpMethod.Delete)
            {
                Fichiers.Remove(cle);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fichier.Contenu) };
        }

        private static HttpResponseMessage Json(object valeur) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(valeur), Encoding.UTF8, "application/json") };
    }

    private sealed class Dialogues : IDialogues
    {
        public string? Document { get; set; }
        public string? Dossier { get; set; }
        public string? Emplacement { get; set; }
        public string? MotDePasse { get; set; }
        public string? CleSecours { get; private set; }
        public int MotsDePasseDemandes { get; private set; }

        public bool Confirmer(string titre, string message) => true;
        public void Erreur(string message) => throw new InvalidOperationException(message);
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => null;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => null;
        public void OuvrirDossier(string dossier) { }
        public string? ChoisirDocument() => Document;
        public string? ChoisirDossier(string titre) => Dossier;
        public string? ChoisirEmplacementFichier(string nomParDefaut) => Emplacement;

        public string? DemanderMotDePasse(string titre, string message, bool confirmer)
        {
            MotsDePasseDemandes++;
            return MotDePasse;
        }

        public void AfficherCleSecours(string cle) => CleSecours = cle;
    }
}
