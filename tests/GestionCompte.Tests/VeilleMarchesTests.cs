using System.Net;
using GestionCompte.Core;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class VeilleMarchesTests
{
    /// <summary>Point de veille inventé, au format écrit par la veille automatique.</summary>
    public const string Exemple = """
        {
          "version": 1,
          "date": "2026-11-09",
          "score": 52,
          "scorePrecedent": 41,
          "tendance": "hausse",
          "alerte": false,
          "resume": "Inquiétudes sur le coût de l'IA, taux stables.",
          "facteurs": [
            { "nom": "Taux et banques centrales", "score": 35, "tendance": "stable", "explication": "Pas de surprise attendue.", "sources": [] },
            { "nom": "IA et technologie", "score": 68, "tendance": "hausse", "explication": "Doutes sur la rentabilité des data centers.",
              "sources": [ { "titre": "Article A", "url": "https://exemple.fr/a" }, { "titre": "Lien douteux", "url": "javascript:alert(1)" } ] }
          ],
          "secteurs": [
            { "nom": "Semi-conducteurs", "score": 72, "tendance": "hausse" },
            { "nom": "Pétrole et gaz", "score": 30, "tendance": "baisse" }
          ]
        }
        """;

    [Fact]
    public void Lire_PointComplet()
    {
        var v = VeilleMarches.Lire(Exemple);

        Assert.Equal(new DateOnly(2026, 11, 9), v.Date);
        Assert.Equal(52, v.Score);
        Assert.Equal(NiveauVeille.Modere, v.Niveau);
        Assert.Equal("↗ en hausse (+11 points depuis le point précédent)", v.TendanceTexte);
        Assert.Equal("Point du lundi 9 novembre 2026", v.DateTexte);
        // Facteurs du plus risqué au moins risqué ; les liens non https sont écartés.
        Assert.Equal(new[] { "IA et technologie", "Taux et banques centrales" }, v.Facteurs.Select(f => f.Nom));
        Assert.Equal(NiveauVeille.Eleve, v.Facteurs[0].Niveau);
        Assert.Equal(new[] { "https://exemple.fr/a" }, v.Facteurs[0].Sources.Select(s => s.Url));
        Assert.Equal(2, v.Secteurs.Count);
    }

    [Theory]
    [InlineData("pas du json")]
    [InlineData("{}")]
    [InlineData("{\"date\":\"09/11/2026\"}")]
    public void Lire_FichierIllisible(string json) => Assert.Throws<FormatException>(() => VeilleMarches.Lire(json));

    [Fact]
    public void Lire_ScoresBornesEtChampsAbsents()
    {
        var v = VeilleMarches.Lire("""{ "date": "2026-11-09", "score": 140, "tendance": "n'importe" }""");

        Assert.Equal(100, v.Score);
        Assert.Equal("stable", v.Tendance);
        Assert.Null(v.ScorePrecedent);
        Assert.Empty(v.Facteurs);
    }

    [Fact]
    public void Exposer_PortefeuilleCroiseAvecLesSecteurs()
    {
        var v = VeilleMarches.Lire(Exemple);

        // ETF et secteur sans score : score global (52) ; semi-conducteurs 72 (élevé) ; pétrole 30.
        var e = v.Exposer(new[] { ("ETF monde", 50m), ("Semi-conducteurs", 25m), ("Pétrole et gaz", 15m), ("Non classé", 10m) })!;

        Assert.Equal((int)Math.Round((50 * 52 + 25 * 72 + 15 * 30 + 10 * 52) / 100m), e.Score);
        Assert.Equal(25m, e.PartRisqueEleve);
        Assert.Equal(new[] { "Semi-conducteurs" }, e.SecteursEleves);
        Assert.Null(v.Exposer(Array.Empty<(string, decimal)>()));
    }

    [Fact]
    public async Task ViewModel_ReglageLectureEtRetrait()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var secrets = new SecretsEnMemoire();
        var http = new FauxGitHub(HttpStatusCode.OK, Exemple);
        var dialogues = new Dialogues { Saisie = new SaisieVeille("moncompte/veille-marches", "cle-de-test") };
        var vm = new VeilleMarchesViewModel(secrets, dialogues, new ServiceVeille(new HttpClient(http)));
        Assert.True(vm.NonConfiguree);
        await vm.ChargerSiBesoinAsync();
        Assert.Equal(0, http.Appels);

        await vm.ConfigurerCommand.ExecuteAsync(null);

        Assert.True(vm.Configuree);
        Assert.Equal("cle-de-test", secrets.Lire(VeilleMarchesViewModel.SecretCle));
        Assert.Equal("https://api.github.com/repos/moncompte/veille-marches/contents/veille.json", http.Adresse);
        Assert.Equal("Bearer cle-de-test", http.Autorisation);
        Assert.Equal(52, vm.Veille!.Score);
        Assert.False(vm.DetailOuvert);

        // Une source s'ouvre dans le navigateur ; déjà lu : pas de nouvel appel.
        vm.OuvrirSourceCommand.Execute(vm.Veille.Facteurs[0].Sources[0]);
        Assert.Equal("https://exemple.fr/a", dialogues.LienOuvert);
        await vm.ChargerSiBesoinAsync();
        Assert.Equal(1, http.Appels);

        vm.RetirerCommand.Execute(null);
        Assert.False(vm.Configuree);
        Assert.Null(vm.Veille);
        Assert.Null(secrets.Lire(VeilleMarchesViewModel.SecretCle));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "refusée")]
    [InlineData(HttpStatusCode.NotFound, "introuvable")]
    public async Task ViewModel_ErreurGitHub_Expliquee(HttpStatusCode code, string extrait)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var secrets = new SecretsEnMemoire();
        secrets.Ecrire(VeilleMarchesViewModel.SecretDepot, "moncompte/veille-marches");
        secrets.Ecrire(VeilleMarchesViewModel.SecretCle, "cle");
        var vm = new VeilleMarchesViewModel(secrets, new Dialogues(), new ServiceVeille(new HttpClient(new FauxGitHub(code, ""))));

        await vm.ActualiserCommand.ExecuteAsync(null);

        Assert.Null(vm.Veille);
        Assert.Contains(extrait, vm.Etat);
        Assert.False(vm.EnCours);
    }

    [Fact]
    public void ViewModel_DepotMalEcrit_Refuse()
    {
        var secrets = new SecretsEnMemoire();
        var dialogues = new Dialogues { Saisie = new SaisieVeille("https://github.com/x", "cle"), ErreursTolerees = true };
        var vm = new VeilleMarchesViewModel(secrets, dialogues, new ServiceVeille(new HttpClient(new FauxGitHub(HttpStatusCode.OK, Exemple))));

        vm.ConfigurerCommand.Execute(null);

        Assert.False(vm.Configuree);
        Assert.NotNull(dialogues.DerniereErreur);
    }

    [Fact]
    public void Bourse_ExpositionCalculeeSurCePC()
    {
        var compte = ConfigurationParDefaut.CreerDemoBourse();
        var secrets = new SecretsEnMemoire();
        var veille = new VeilleMarchesViewModel(secrets, new Dialogues());
        var bourse = new BourseViewModel(compte, new Dialogues(), () => { }, new ServiceCoursBourse(new HttpClient(new FauxGitHub(HttpStatusCode.NotFound, ""))),
            new DateTime(2026, 11, 9)) { Veille = veille };
        Assert.False(bourse.AvecExpositionVeille);
        var changements = new List<string?>();
        bourse.PropertyChanged += (_, e) => changements.Add(e.PropertyName);

        veille.Afficher(VeilleMarches.Lire(Exemple));

        Assert.Contains(nameof(BourseViewModel.ExpositionVeille), changements);
        Assert.StartsWith("Votre portefeuille : risque ", bourse.ExpositionVeille);
    }

    private sealed class FauxGitHub(HttpStatusCode code, string contenu) : HttpMessageHandler
    {
        public int Appels { get; private set; }
        public string? Adresse { get; private set; }
        public string? Autorisation { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Appels++;
            Adresse = request.RequestUri!.ToString();
            Autorisation = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(contenu) });
        }
    }

    private sealed class Dialogues : IDialogues
    {
        public SaisieVeille? Saisie { get; init; }
        public bool ErreursTolerees { get; init; }
        public string? LienOuvert { get; private set; }
        public string? DerniereErreur { get; private set; }

        public bool Confirmer(string titre, string message) => true;

        public void Erreur(string message)
        {
            if (!ErreursTolerees)
                throw new InvalidOperationException(message);
            DerniereErreur = message;
        }

        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => null;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => null;
        public void OuvrirDossier(string dossier) { }
        public SaisieVeille? DemanderVeille(string depot, bool cleExistante) => Saisie;
        public void OuvrirLien(string adresse) => LienOuvert = adresse;
    }
}
