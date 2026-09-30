using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using GestionCompte.Core.Achats;
using GestionCompte.Data;
using GestionCompte.Data.Achats;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class AchatsTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), $"gestioncompte-achats-{Guid.NewGuid():N}");
    private readonly Dialogues _dialogues = new();
    private static readonly DateTime Maintenant = new(2026, 11, 15, 10, 0, 0);

    public AchatsTests() => Directory.CreateDirectory(_dossier);

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, true);
    }

    [Theory]
    [InlineData("""<script type="application/ld+json">{"@type":"Product","name":"TV","offers":{"@type":"Offer","price":"499.99","priceCurrency":"EUR"}}</script>""", "499.99")]
    [InlineData("""<script type='application/ld+json'>{"@graph":[{"@type":"WebPage"},{"@type":"Product","offers":[{"price":1299}]}]}</script>""", "1299")]
    [InlineData("""<script type="application/ld+json">{"@type":"Product","offers":{"@type":"AggregateOffer","lowPrice":"89,90"}}</script>""", "89.90")]
    [InlineData("""<meta property="product:price:amount" content="1 049,00">""", "1049.00")]
    [InlineData("""<span itemprop="price" content="19.5">19,50 €</span>""", "19.5")]
    public void Le_prix_est_lu_dans_les_donnees_de_la_page(string html, string attendu) =>
        Assert.Equal(decimal.Parse(attendu, System.Globalization.CultureInfo.InvariantCulture), LecturePrix.Extraire(html));

    [Fact]
    public void Une_page_sans_prix_structure_ne_donne_rien_et_le_titre_est_lu()
    {
        const string html = "<html><head><title>Lave-linge X &amp; Y</title></head><body>Prix : 399 €</body></html>";
        Assert.Null(LecturePrix.Extraire(html));
        Assert.Equal("Lave-linge X & Y", LecturePrix.Titre(html));
    }

    [Theory]
    [InlineData("1 299,99 €", "1299.99")]
    [InlineData("1.299,99", "1299.99")]
    [InlineData("1,299.99", "1299.99")]
    [InlineData("1.299", "1299")]
    [InlineData("12,5", "12.5")]
    public void Les_prix_ecrits_de_facons_differentes_sont_compris(string texte, string attendu) =>
        Assert.Equal(decimal.Parse(attendu, System.Globalization.CultureInfo.InvariantCulture), LecturePrix.Nombre(texte));

    [Fact]
    public void Les_offres_des_deux_ia_sont_fusionnees_et_triees_par_prix()
    {
        var gemini = RechercheOffres.Extraire("""
            Voici les offres :
            ```json
            [{"site":"Darty","titre":"Aspirateur Z","prix":199.99,"url":"https://www.darty.com/aspirateur-z?ref=1"},
             {"site":"Fnac","titre":"Aspirateur Z","prix":"189,00 €","url":"https://www.fnac.com/z"},
             {"site":"Sans lien","prix":10},
             {"site":"Gratuit ?","prix":0,"url":"https://exemple.fr/x"}]
            ```
            """, SourceOffre.Gemini);
        var mistral = RechercheOffres.Extraire("""[{"site":"Darty","prix":199.99,"url":"https://darty.com/aspirateur-z"},{"site":"Boulanger","prix":205,"url":"https://www.boulanger.com/z","remarque":"livraison 9,99 €"}]""", SourceOffre.Mistral);

        var offres = FusionOffres.Fusionner(gemini.Concat(mistral));

        Assert.Equal(new[] { "Fnac", "Darty", "Boulanger" }, offres.Select(o => o.Site));
        Assert.Equal(new[] { SourceOffre.Gemini, SourceOffre.Mistral }, offres[1].Sources.OrderBy(s => s));
        Assert.Equal("livraison 9,99 €", offres[2].Remarque);
    }

    [Fact]
    public async Task Gemini_utilise_la_recherche_google_et_mistral_l_outil_web()
    {
        var faux = new FauxHttp(requete => requete.RequestUri!.Host.Contains("mistral")
            ? Json("""{"outputs":[{"type":"tool.execution","name":"web_search"},{"type":"message.output","content":[{"type":"text","text":"[{\"site\":\"A\",\"prix\":10,\"url\":\"https://a.fr/p\"}]"},{"type":"tool_reference","url":"https://a.fr/p"}]}]}""")
            : Json("""{"candidates":[{"content":{"parts":[{"text":"[{\"site\":\"B\",\"prix\":12,"},{"text":"\"url\":\"https://b.fr/p\"}]"}]}}]}"""));
        var http = new HttpClient(faux);

        var parGemini = await RechercheOffres.RechercherAsync(new Gemini(http, "cle-g"), "casque");
        var parMistral = await RechercheOffres.RechercherAsync(new Mistral(http, "cle-m"), "casque");

        Assert.Equal("B", Assert.Single(parGemini).Site);
        Assert.Equal("A", Assert.Single(parMistral).Site);
        var g = faux.Requetes[0];
        Assert.Equal("cle-g", g.Cle);
        Assert.Contains("google_search", g.Corps);
        Assert.Contains("gemini-flash-lite-latest:generateContent", g.Adresse);
        var m = faux.Requetes[1];
        Assert.Equal("Bearer cle-m", m.Autorisation);
        Assert.Equal("web_search", JsonNode.Parse(m.Corps)!["tools"]![0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Une_cle_refusee_donne_un_message_clair()
    {
        var faux = new FauxHttp(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"code":400,"message":"API key not valid. Please pass a valid API key."}}"""),
        });

        var erreur = await Assert.ThrowsAsync<HttpRequestException>(() => new Gemini(new HttpClient(faux), "mauvaise").DemanderAsync("x", false));

        Assert.StartsWith("Gemini refuse la clé API", erreur.Message);
    }

    [Fact]
    public async Task La_recherche_interroge_les_deux_ia_verifie_les_prix_et_garde_les_erreurs()
    {
        var pages = new FauxHttp(requete => requete.RequestUri!.Host switch
        {
            "a.fr" => Html("""<script type="application/ld+json">{"offers":{"price":"99.00"}}</script>"""),
            "b.fr" => Html("""<script type="application/ld+json">{"offers":{"price":"115.00"}}</script>"""),
            _ => new HttpResponseMessage(HttpStatusCode.Forbidden),
        });
        var ia = new ServicesIA(new SecretsEnMemoire(), _dialogues, assistantsTest: () => new IAssistantIA[]
        {
            new FausseIA(SourceOffre.Gemini, """[{"site":"A","titre":"Casque","prix":99,"url":"https://a.fr/casque"},{"site":"C","prix":90,"url":"https://c.fr/casque"}]"""),
            new FausseIA(SourceOffre.Mistral, null),
        });
        var vm = new AchatsViewModel(_dossier, ia, _dialogues, new HttpClient(pages), () => Maintenant);
        vm.Recherche = "casque";

        await vm.RechercherCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "C", "A" }, vm.Offres.Select(o => o.Site));
        Assert.Equal("à vérifier", vm.Offres[0].Verification);
        Assert.Equal("prix vérifié sur la page", vm.Offres[1].Verification);
        Assert.Equal("2 offres (Gemini : 2, Mistral : 0), la moins chère : 90,00 € chez C.", vm.Resume);
        Assert.Equal("Mistral : limite gratuite atteinte.", vm.Erreur);
    }

    [Fact]
    public async Task Sans_ia_la_recherche_renvoie_vers_la_liste_des_sites()
    {
        var ia = new ServicesIA(new SecretsEnMemoire(), _dialogues);
        var vm = new AchatsViewModel(_dossier, ia, _dialogues, new HttpClient(new FauxHttp(_ => new HttpResponseMessage(HttpStatusCode.NotFound))), () => Maintenant);
        vm.Recherche = "lave linge";

        await vm.RechercherCommand.ExecuteAsync(null);
        vm.OuvrirSitesCommand.Execute(null);

        Assert.StartsWith("Aucune IA réglée", vm.Erreur);
        Assert.Equal(8, _dialogues.Liens.Count);
        Assert.Contains("https://www.amazon.fr/s?k=lave%20linge", _dialogues.Liens);
    }

    [Fact]
    public async Task Un_produit_suivi_garde_ses_releves_et_signale_le_prix_cible()
    {
        var prix = "250.00";
        var pages = new FauxHttp(_ => Html($"""<title>Vélo électrique</title><meta property="product:price:amount" content="{prix}">"""));
        var jour = Maintenant;
        var vm = new AchatsViewModel(_dossier, new ServicesIA(new SecretsEnMemoire(), _dialogues), _dialogues, new HttpClient(pages), () => jour);
        _dialogues.Reponses.Enqueue("https://www.velo.fr/electrique");

        await vm.SuivreAdresseCommand.ExecuteAsync(null);

        var produit = Assert.Single(vm.Produits);
        Assert.Equal("Vélo électrique", produit.Nom);
        Assert.Equal("250,00 €", produit.PrixTexte);
        _dialogues.Reponses.Enqueue("230");
        vm.ModifierCibleCommand.Execute(produit);
        Assert.Equal("Achats", vm.TitreNavigation);

        // Le lendemain, à l'ouverture : le prix est relu et la cible est atteinte.
        prix = "219.00";
        jour = Maintenant.AddDays(1);
        var relu = new AchatsViewModel(_dossier, new ServicesIA(new SecretsEnMemoire(), _dialogues), _dialogues, new HttpClient(pages), () => jour);
        await relu.Chargement;

        var p = Assert.Single(relu.Produits);
        Assert.True(p.CibleAtteinte);
        Assert.Equal("Achats (1)", relu.TitreNavigation);
        Assert.Equal("−31,00 € depuis le relevé précédent · plus bas 219,00 €", p.Evolution);
        Assert.Equal(2, p.Produit.Releves.Count);
    }

    [Fact]
    public async Task Prevoir_l_achat_l_ajoute_au_previsionnel()
    {
        var chemin = Path.Combine(_dossier, "compte.db");
        new DepotSqlite(chemin).Enregistrer(ConfigurationParDefaut.CreerDemoHistorique());
        var vm = new MainViewModel(new DepotSqlite(chemin), _dialogues, new DateTime(2026, 11, 15), new ApparenceViewModel(null));
        var avant = vm.Previsionnel.OperationsPrevues.Elements.Count;
        _dialogues.Achat = periodes => new AchatPrevu(periodes[2].Periode, "Vélo électrique", 219m);
        await vm.Achats.Chargement;
        vm.Achats.PrevoirOffreCommand.Execute(new OffreViewModel(new OffreTrouvee { Site = "A", Titre = "Vélo", Prix = 219m, Adresse = "https://a.fr" }));

        Assert.Equal(MainViewModel.OngletPrevisionnel, vm.OngletSelectionne);
        Assert.Equal(avant + 1, vm.Previsionnel.OperationsPrevues.Elements.Count);
        Assert.Contains(new DepotSqlite(chemin).Charger(new(2026, 11))!.OperationsPrevues, o => o.Libelle == "Vélo électrique" && o.Debit == 219m);

        vm.OngletSelectionne = MainViewModel.OngletAchats;
        vm.Apparence.ModuleAchats = false;
        Assert.Equal(MainViewModel.OngletConfiguration, vm.OngletSelectionne);
    }

    [Fact]
    public void Couper_les_ia_empeche_tout_envoi()
    {
        var secrets = new SecretsEnMemoire();
        secrets.Ecrire(ServicesIA.SecretGemini, "cle");
        var ia = new ServicesIA(secrets, _dialogues);
        Assert.True(ia.Disponibles);
        Assert.Equal("Gemini : clé enregistrée (gemini-flash-lite-latest)", ia.EtatGemini);

        ia.Actives = false;

        Assert.False(ia.Disponibles);
        Assert.Empty(ia.Assistants());
        Assert.False(new ServicesIA(secrets, _dialogues).Actives);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Html(string html) =>
        new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    private sealed class FausseIA : IAssistantIA
    {
        private readonly string? _reponse;

        public FausseIA(SourceOffre source, string? reponse)
        {
            Source = source;
            _reponse = reponse;
        }

        public SourceOffre Source { get; }
        public string Nom => Source.ToString();

        public Task<string> DemanderAsync(string demande, bool avecRecherche, CancellationToken annulation = default) =>
            _reponse is null ? throw new HttpRequestException($"{Nom} : limite gratuite atteinte.") : Task.FromResult(_reponse);
    }

    private sealed class FauxHttp : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _reponse;

        public FauxHttp(Func<HttpRequestMessage, HttpResponseMessage> reponse) => _reponse = reponse;

        public List<(string Adresse, string? Cle, string Autorisation, string Corps)> Requetes { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
        {
            Requetes.Add((requete.RequestUri!.ToString(),
                requete.Headers.TryGetValues("x-goog-api-key", out var cles) ? cles.First() : null,
                requete.Headers.Authorization?.ToString() ?? "",
                requete.Content is null ? "" : await requete.Content.ReadAsStringAsync(annulation)));
            var reponse = _reponse(requete);
            reponse.RequestMessage = requete;
            return reponse;
        }
    }

    private sealed class Dialogues : IDialogues
    {
        public List<string> Liens { get; } = new();
        public Queue<string> Reponses { get; } = new();
        public Func<IReadOnlyList<ChoixPeriode>, AchatPrevu?>? Achat { get; set; }

        public bool Confirmer(string titre, string message) => true;
        public void Erreur(string message) => throw new InvalidOperationException(message);
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => null;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => null;
        public void OuvrirDossier(string dossier) { }
        public void OuvrirLien(string adresse) => Liens.Add(adresse);
        public string? DemanderNom(string titre, string message, string valeur) => Reponses.Count > 0 ? Reponses.Dequeue() : null;
        public AchatPrevu? DemanderAchatPrevu(string libelle, decimal montant, IReadOnlyList<ChoixPeriode> periodes) => Achat?.Invoke(periodes);
    }
}
