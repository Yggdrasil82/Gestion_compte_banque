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

    private const string Entete = "datetime,account_type,type,name,symbol,shares,price,amount,fee,tax,transaction_id\n";

    [Fact]
    public void Import_DividendeAvecNombreDeTitres_ResteUnDividende()
    {
        // Trade Republic indique sur la ligne du dividende le nombre de titres détenus.
        var lecture = ImportTradeRepublic.Lire(Entete +
            "2026-07-02T10:00:00Z,DEFAULT,DIVIDEND,Exemple,XX0000000001,1.303225,,1.11,,-0.35,d1\n" +
            "2026-07-03T10:00:00Z,DEFAULT,INTEREST_PAYMENT,,,,,0.42,,,d2\n");

        Assert.Equal(new[] { TypeOperationBourse.Dividende, TypeOperationBourse.Interets }, lecture.Operations.Select(o => o.Type));
    }

    [Fact]
    public void Importer_DeNouveau_CorrigeLesOperationsMalLues()
    {
        var portefeuille = new Portefeuille();
        var dividende = ImportTradeRepublic.Lire(Entete + "2026-07-02T10:00:00Z,DEFAULT,DIVIDEND,Exemple,XX0000000001,2,,1.11,,-0.35,d1\n")
            .Operations.Single();
        portefeuille.Ajouter(new[]
        {
            dividende with { Type = TypeOperationBourse.Achat }, // lu par une version précédente
        });

        var (ajoutees, corrigees) = portefeuille.Importer(new[] { dividende });

        Assert.Equal((0, 1), (ajoutees, corrigees));
        Assert.Equal(TypeOperationBourse.Dividende, Assert.Single(portefeuille.Operations).Type);
        Assert.Empty(CalculBourse.Calculer(portefeuille).Positions);
        Assert.Equal((0, 0), portefeuille.Importer(new[] { dividende }));
    }

    [Fact]
    public void Calcul_VirementVersLePea_NiVerseNiRetireSurLeTout()
    {
        var portefeuille = new Portefeuille();
        portefeuille.Ajouter(ImportTradeRepublic.Lire(Entete +
            "2026-05-13T06:00:00Z,DEFAULT,TRANSFER_INSTANT_INBOUND,,,,,200,,,v1\n" +
            "2026-05-19T16:19:45Z,PEA,TRANSFER_IN,,,,,99.70,,,v2\n" +
            "2026-05-19T16:19:45Z,DEFAULT,TRANSFER_OUT,,,,,-99.70,,,v3\n").Operations);

        var tout = CalculBourse.Calculer(portefeuille);
        Assert.Equal((200m, 0m, 200m), (tout.Versements, tout.Retraits, tout.Especes));
        Assert.Equal(99.70m, CalculBourse.Calculer(portefeuille, EnveloppeBourse.Pea).Versements);
        Assert.Equal(99.70m, CalculBourse.Calculer(portefeuille, EnveloppeBourse.CompteTitres).Retraits);
    }

    [Fact]
    public void Calcul_PlusValue_AvecEtSansFraisDAchat()
    {
        var portefeuille = new Portefeuille();
        portefeuille.Ajouter(ImportTradeRepublic.Lire(Entete +
            "2026-04-14T11:00:00Z,DEFAULT,BUY,Exemple,XX0000000001,0.651621,314.60,-205.00,-1.00,-0.82,a1\n" +
            "2026-07-13T11:00:00Z,DEFAULT,SELL,Exemple,XX0000000001,-0.651621,332.70,216.79,-1.00,,a2\n").Operations);

        var bilan = CalculBourse.Calculer(portefeuille);

        Assert.Equal(8.97m, bilan.PlusValuesRealisees);       // 215,79 − 206,82 (frais et taxe d'achat compris)
        Assert.Equal(10.79m, bilan.PlusValuesHorsFraisAchat); // 215,79 − 205,00, comme Trade Republic
    }

    [Fact]
    public void Calcul_GainNet_EtFlatTaxDuCompteTitres()
    {
        var portefeuille = new Portefeuille();
        portefeuille.Ajouter(ImportTradeRepublic.Lire(Entete +
            "2025-03-01T10:00:00Z,DEFAULT,BUY,A,XX0000000001,2,50,-100.00,-1.00,,a1\n" +
            "2025-06-01T10:00:00Z,DEFAULT,SELL,A,XX0000000001,-1,80,80.00,-1.00,,a2\n" +   // 2025 : +28,50, pas dans l'année
            "2026-02-01T10:00:00Z,DEFAULT,SELL,A,XX0000000001,-1,90,90.00,-1.00,,a3\n" +   // 2026 : +38,50
            "2026-03-01T10:00:00Z,PEA,BUY,B,XX0000000002,10,10,-100.00,-1.00,,b1\n" +
            "2026-04-01T10:00:00Z,PEA,SELL,B,XX0000000002,-10,15,150.00,-1.00,,b2\n" +   // PEA : +48, non imposé
            "2026-05-01T10:00:00Z,DEFAULT,DIVIDEND,A,XX0000000001,1,,10.00,,-3.00,d1\n").Operations);

        var bilan = CalculBourse.Calculer(portefeuille, null, 2026);

        Assert.Equal(115.00m, bilan.PlusValuesRealisees);
        Assert.Equal(3.00m, bilan.TaxesSurRevenus);
        Assert.Equal(122.00m, bilan.GainRealiseNet);      // 115 + 10 − 3
        Assert.Equal(38.50m, bilan.PlusValuesImposables);
        Assert.Equal(12.09m, bilan.FlatTax);              // 31,4 % de 38,50 (taux 2026)
        Assert.Equal(109.91m, bilan.NetApresImpot);
        Assert.Equal(8.55m, CalculBourse.Calculer(portefeuille, null, 2025).FlatTax); // 30 % de 28,50 (taux 2025)
        Assert.Equal(0m, CalculBourse.Calculer(portefeuille, EnveloppeBourse.Pea, 2026).FlatTax);
    }

    [Fact]
    public void Calcul_FlatTax_NulleEnCasDePerte()
    {
        var portefeuille = new Portefeuille();
        portefeuille.Ajouter(ImportTradeRepublic.Lire(Entete +
            "2026-01-01T10:00:00Z,DEFAULT,BUY,A,XX0000000001,1,100,-100.00,-1.00,,a1\n" +
            "2026-02-01T10:00:00Z,DEFAULT,SELL,A,XX0000000001,-1,80,80.00,-1.00,,a2\n").Operations);

        var bilan = CalculBourse.Calculer(portefeuille, null, 2026);

        Assert.Equal(-22.00m, bilan.PlusValuesImposables);
        Assert.Equal(0m, bilan.FlatTax);
        Assert.Equal(-22.00m, bilan.NetApresImpot);
    }

    [Fact]
    public void AideDecision_IndicateursDuPortefeuille()
    {
        var portefeuille = new Portefeuille();
        portefeuille.Ajouter(ImportTradeRepublic.Lire(Entete +
            "2026-01-10T10:00:00Z,DEFAULT,TRANSFER_INSTANT_INBOUND,,,,,1000,,,v1\n" +
            "2026-01-12T10:00:00Z,DEFAULT,BUY,Exemple FR,FR0000000001,10,50,-500.00,-1.00,,a1\n" +
            "2026-01-13T10:00:00Z,DEFAULT,BUY,Exemple US,US0000000001,1,20,-20.00,-1.00,,a2\n" +
            "2026-02-01T10:00:00Z,DEFAULT,SELL,Exemple US,US0000000001,-1,40,40.00,-1.00,,a3\n" +
            "2026-05-13T10:00:00Z,PEA,TRANSFER_IN,,,,,300,,,p1\n" +
            "2026-05-13T10:00:00Z,DEFAULT,TRANSFER_OUT,,,,,-300,,,p2\n" +
            "2026-05-14T10:00:00Z,PEA,BUY,Monde,FR0000000002,30,10,-300.00,,,p3\n").Operations);
        portefeuille.Cours["FR0000000001"] = new CoursTitre(40m, null, true, null);
        portefeuille.Cours["FR0000000002"] = new CoursTitre(10m, null, true, null);
        portefeuille.Ajouter(new[]
        {
            new OperationBourse("c1", new DateTime(2026, 5, 14), EnveloppeBourse.Pea, TypeOperationBourse.Achat, "Monde", "FR0000000002", "FUND", 0, 0, 0, 0, 0, ""),
        });

        var indicateurs = AideDecisionBourse.Calculer(portefeuille, new DateTime(2026, 10, 11)).ToDictionary(i => i.Titre);

        Assert.Equal(NiveauIndicateur.Attention, indicateurs["Poids des frais"].Niveau);              // 1 € sur 20 € = 5 %
        Assert.Contains("5,0 %", indicateurs["Poids des frais"].Texte);
        Assert.Equal(NiveauIndicateur.Attention, indicateurs["Concentration"].Niveau);                // Exemple FR : 400 € sur 700 €
        Assert.Contains("13/05/2031", indicateurs["PEA ou compte-titres"].Texte);
        Assert.Contains("Exemple FR est une action européenne", indicateurs["PEA ou compte-titres"].Texte);
        Assert.Contains("compenseraient 18,00 €", indicateurs["Fiscalité 2026"].Texte);              // +18 de plus-values, −101 latents
        Assert.Contains("100,00 € par mois depuis janvier 2026", indicateurs["Rythme et espèces"].Texte); // 1 000 € en 10 mois, virement PEA exclu
        Assert.Contains("Exemple FR (compte-titres, autres)", AideDecisionBourse.Resume(portefeuille, new DateTime(2026, 10, 11)));
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
        Assert.Equal(36.00m, bilan.PlusValuesHorsFraisAchat); // 620 − 1 460 × 10 / 25
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
