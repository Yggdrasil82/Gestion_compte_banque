using System.Net;
using GestionCompte.Core;
using GestionCompte.Core.Bourse;
using GestionCompte.Core.Modeles;
using GestionCompte.Data;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

/// <summary>Module « Bourse » : import de l'export Trade Republic, prix moyen pondéré, plus-values, cours Yahoo.</summary>
public sealed class BourseTests : IDisposable
{
    private readonly string _dossier = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_dossier, recursive: true);

    private static Portefeuille Demo() => ConfigurationParDefaut.CreerDemoBourse().Portefeuille;

    [Fact]
    public void Import_ExportDemo_TypesEtEnveloppes()
    {
        var lecture = ImportTradeRepublic.Lire(ConfigurationParDefaut.ExportBourseDemo());

        Assert.Equal(13, lecture.Operations.Count);
        Assert.Equal(0, lecture.Ignorees);
        var vente = lecture.Operations.Single(o => o.Identifiant == "demo-10");
        Assert.Equal(TypeOperationBourse.Vente, vente.Type);
        Assert.Equal(EnveloppeBourse.Pea, vente.Enveloppe);
        Assert.Equal(-10m, vente.Quantite);
        Assert.Equal(620m, vente.Net);
        Assert.Equal(TypeOperationBourse.Achat, lecture.Operations.Single(o => o.Identifiant == "demo-08").Type); // plan d'investissement
        Assert.Equal(TypeOperationBourse.Dividende, lecture.Operations.Single(o => o.Identifiant == "demo-07").Type);
        Assert.Equal(TypeOperationBourse.Interets, lecture.Operations.Single(o => o.Identifiant == "demo-11").Type);
        Assert.Equal(TypeOperationBourse.Retrait, lecture.Operations.Single(o => o.Identifiant == "demo-13").Type);
        var versement = lecture.Operations.Single(o => o.Identifiant == "demo-01");
        Assert.Equal((TypeOperationBourse.Versement, EnveloppeBourse.CompteTitres), (versement.Type, versement.Enveloppe));
    }

    [Fact]
    public void Import_PointVirgule_GuillemetsEtBom()
    {
        var texte = "﻿\"datetime\";\"account_type\";\"type\";\"name\";\"symbol\";\"shares\";\"price\";\"amount\";\"fee\";\"tax\";\"transaction_id\"\r\n" +
                    "\"2026-01-05T10:00:00Z\";\"PEA\";\"BUY\";\"Société \"\"Exemple\"\"\";\"FR0000000001\";\"2\";\"10.5\";\"-21\";\"-1\";\"\";\"x1\"\r\n" +
                    "\"pas une date\";\"PEA\";\"BUY\";\"\";\"\";\"\";\"\";\"\";\"\";\"\";\"x2\"\r\n";

        Assert.True(ImportTradeRepublic.Reconnait(texte));
        var lecture = ImportTradeRepublic.Lire(texte);

        var achat = Assert.Single(lecture.Operations);
        Assert.Equal("Société \"Exemple\"", achat.Nom);
        Assert.Equal(-22m, achat.Net);
        Assert.Equal(1, lecture.Ignorees);
    }

    [Fact]
    public void Import_AutreFichier_Refuse()
    {
        Assert.False(ImportTradeRepublic.Reconnait("Date;Libellé;Montant\n01/01/2026;Courses;-12,50"));
        Assert.Throws<FormatException>(() => ImportTradeRepublic.Lire("Date;Libellé;Montant\n"));
    }

    [Fact]
    public void Ajouter_DeuxFois_SansDoublon()
    {
        var portefeuille = Demo();

        var ajoutees = portefeuille.Ajouter(ImportTradeRepublic.Lire(ConfigurationParDefaut.ExportBourseDemo()).Operations);

        Assert.Equal(0, ajoutees);
        Assert.Equal(13, portefeuille.Operations.Count);
    }

    [Fact]
    public void Calcul_PrixMoyenPondere_EtPlusValues()
    {
        var bilan = CalculBourse.Calculer(Demo());

        Assert.Equal(8500m, bilan.Versements);
        Assert.Equal(200m, bilan.Retraits);
        Assert.Equal(2168.00m, bilan.Especes);
        // TotalEnergies : 25 titres pour 1 461 € (frais compris), 10 vendus 620 € net → 620 − 584,40.
        Assert.Equal(35.60m, bilan.PlusValuesRealisees);
        Assert.Equal(37.07m, bilan.Dividendes);
        Assert.Equal(11.07m, bilan.FraisEtTaxes);

        var total = bilan.Positions.Single(p => p.Isin == "FR0000120271");
        Assert.Equal(15m, total.Quantite);
        Assert.Equal(876.60m, total.PrixRevient);
        Assert.Equal(58.44m, total.Pru);
        var monde = bilan.Positions.Single(p => p.Isin == "IE00B4L5Y983");
        Assert.Equal(22.9853m, monde.Quantite);
        Assert.Equal(2203.00m, monde.PrixRevient);
        Assert.Equal(2492.07m, monde.Valeur);
        // Air Liquide : taxe sur les transactions financières dans le prix de revient.
        Assert.Equal(705.11m, bilan.Positions.Single(p => p.Isin == "FR0000120073").PrixRevient);

        Assert.Equal(6937.97m, bilan.ValeurTitres);
        Assert.Equal(737.26m, bilan.PlusValueLatente);
        Assert.Equal(9105.97m, bilan.ValeurTotale);
        Assert.Equal(805.97m, bilan.Gain);
    }

    [Fact]
    public void Calcul_ParEnveloppe()
    {
        var pea = CalculBourse.Calculer(Demo(), EnveloppeBourse.Pea);

        Assert.Equal(new[] { "FR0011871128", "FR0000120271" }, pea.Positions.Select(p => p.Isin));
        Assert.Equal(1762.75m, pea.Especes);
        Assert.Equal(5000m, pea.Versements);
    }

    [Fact]
    public void Calcul_SansCours_EstimeAuPrixDeRevient()
    {
        var portefeuille = Demo();
        portefeuille.Cours.Clear();

        var bilan = CalculBourse.Calculer(portefeuille);

        Assert.Equal(4, bilan.SansCours);
        Assert.Equal(0m, bilan.PlusValueLatente);
        Assert.Equal(bilan.PrixRevient, bilan.ValeurTitres);
    }

    [Fact]
    public void Calcul_VenteDeTitresAchetesAvantLExport_NeDescendPasSousZero()
    {
        var portefeuille = new Portefeuille();
        portefeuille.Ajouter(new[]
        {
            new OperationBourse("a", new DateTime(2026, 1, 1), EnveloppeBourse.CompteTitres, TypeOperationBourse.Vente,
                "X", "XX0000000001", null, -5, 10, 50, -1, 0, "Vente"),
        });

        var bilan = CalculBourse.Calculer(portefeuille);

        Assert.Empty(bilan.Positions);
        Assert.Equal(49m, bilan.PlusValuesRealisees);
    }

    [Fact]
    public void Depot_EnregistreEtRelitLePortefeuille()
    {
        var chemin = Path.Combine(_dossier, "bourse.db");
        new DepotSqlite(chemin).Enregistrer(ConfigurationParDefaut.CreerDemoBourse());

        var relu = new DepotSqlite(chemin).Charger(new PeriodeMois(2026, 11))!;

        Assert.Equal(13, relu.Portefeuille.Operations.Count);
        Assert.Equal(Demo().Operations, relu.Portefeuille.Operations);
        Assert.Equal(new CoursTitre(182.60m, new DateTime(2026, 11, 14, 17, 35, 0), true, "AI.PA"), relu.Portefeuille.Cours["FR0000120073"]);
        Assert.Equal(805.97m, CalculBourse.Calculer(relu.Portefeuille).Gain);
    }

    [Fact]
    public void Yahoo_ChoisitUnePlaceEnEuros()
    {
        const string json = """
            { "quotes": [ { "symbol": "IWDA.L", "exchange": "LSE" }, { "symbol": "SWDA.MI" }, { "symbol": "EUNL.DE" } ] }
            """;

        Assert.Equal("EUNL.DE", ServiceCoursBourse.ChoisirSymbole(json));
        Assert.Equal("IWDA.L", ServiceCoursBourse.ChoisirSymbole("""{ "quotes": [ { "symbol": "IWDA.L" } ] }"""));
        Assert.Null(ServiceCoursBourse.ChoisirSymbole("""{ "quotes": [] }"""));
    }

    [Fact]
    public async Task Yahoo_CoursEnDollars_ConvertiEnEuros()
    {
        var service = new ServiceCoursBourse(new HttpClient(new FauxYahoo()));

        var cours = await service.ChercherAsync("US0378331005", null);

        Assert.NotNull(cours);
        Assert.Equal("AAPL", cours.Symbole);
        Assert.Equal(180m, cours.Valeur); // 200 $ × 0,9
    }

    [Fact]
    public async Task ViewModel_ImportPuisCours()
    {
        var compte = new CompteBancaire(new ConfigurationBudget { PremierMois = new PeriodeMois(2026, 11) });
        var enregistrements = 0;
        var vm = new BourseViewModel(compte, new Dialogues(), () => enregistrements++, new ServiceCoursBourse(new HttpClient(new FauxYahoo())),
            new DateTime(2026, 11, 15), _ => ConfigurationParDefaut.ExportBourseDemo());
        Assert.True(vm.Vide);

        vm.Importer("export.csv");
        Assert.Equal("13 opérations importées.", vm.Statut);
        vm.Importer("export.csv");
        Assert.Equal("0 opération importée, 13 déjà présentes.", vm.Statut);
        Assert.Equal(1, enregistrements);
        Assert.False(vm.Vide);
        Assert.True(vm.AEnveloppes);
        Assert.Equal(4, vm.Positions.Count);
        Assert.Equal(13, vm.Historique.Count);

        SynchronizationContext.SetSynchronizationContext(null); // tableau recréé tout de suite (pas de saisie en cours)
        vm.Positions.Single(p => p.Isin == "FR0000120271").Cours = 60m;
        Assert.Equal(60m, vm.Positions.Single(p => p.Isin == "FR0000120271").Cours);
        Assert.Equal(60m, compte.Portefeuille.Cours["FR0000120271"].Valeur);
        Assert.True(compte.Portefeuille.Cours["FR0000120271"].Manuel);
        Assert.Equal(3, vm.Bilan.SansCours);

        await vm.ActualiserCoursCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.Bilan.SansCours); // le faux Yahoo donne un cours pour chaque titre
        Assert.StartsWith("4 cours mis à jour.", vm.Statut);

        vm.Filtre = 2;
        Assert.Equal(2, vm.Positions.Count);
        Assert.All(vm.Historique, o => Assert.Equal("PEA", o.Enveloppe));
    }

    [Fact]
    public void VueEnsemble_ValeurDesPortefeuilles()
    {
        var principal = ConfigurationParDefaut.CreerDemoLivret();
        var bourse = ConfigurationParDefaut.CreerDemoBourse();

        var ensemble = new VueEnsembleViewModel(new[]
        {
            new CompteEnsemble("Livret A", principal, true),
            new CompteEnsemble("Trade Republic", bourse, false),
        }, new PeriodeMois(2026, 11));

        Assert.True(ensemble.ABourse);
        Assert.Null(ensemble.Lignes[0].Bourse);
        Assert.Equal(9105.97m, ensemble.Lignes[1].Bourse);
        Assert.Equal(9105.97m, ensemble.TotalBourse);
        Assert.Equal(ensemble.TotalActuel + 9105.97m, ensemble.Patrimoine);
    }

    /// <summary>Yahoo Finance d'exemple : tout ISIN donne le symbole AAPL, coté 200 $, et 1 $ = 0,90 €.</summary>
    private sealed class FauxYahoo : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
        {
            var adresse = requete.RequestUri!.ToString();
            var json = adresse.Contains("/search")
                ? """{ "quotes": [ { "symbol": "AAPL" } ] }"""
                : adresse.Contains("USDEUR")
                    ? """{ "chart": { "result": [ { "meta": { "currency": "EUR", "regularMarketPrice": 0.9, "regularMarketTime": 1794700000 } } ] } }"""
                    : """{ "chart": { "result": [ { "meta": { "currency": "USD", "regularMarketPrice": 200, "regularMarketTime": 1794700000 } } ] } }""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    private sealed class Dialogues : IDialogues
    {
        public bool Confirmer(string titre, string message) => true;
        public void Erreur(string message) => throw new InvalidOperationException(message);
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => null;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => null;
        public void OuvrirDossier(string dossier) { }
    }
}
