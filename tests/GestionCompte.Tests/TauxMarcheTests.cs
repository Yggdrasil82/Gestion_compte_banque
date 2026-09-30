using GestionCompte.Core.Achats;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;
using GestionCompte.Data.Achats;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class TauxMarcheTests
{
    private static readonly DateTime Aujourdhui = new(2026, 11, 15);

    [Theory]
    [InlineData(TypeCredit.Immobilier, 240, 200000, "20 ans et plus")]
    [InlineData(TypeCredit.Immobilier, 180, 200000, "de 10 à moins de 20 ans")]
    [InlineData(TypeCredit.AutoMoto, 48, 15000, "plus de 6 000 €")]
    [InlineData(TypeCredit.Consommation, 18, 2500, "jusqu'à 3 000 €")]
    public void La_categorie_suit_le_decoupage_de_la_banque_de_france(TypeCredit type, int dureeMois, decimal montant, string attendu)
    {
        var categorie = RechercheTaux.Categorie(type, dureeMois, montant);
        Assert.Contains(attendu, categorie);
        // Seule la tranche part, jamais le montant exact.
        Assert.DoesNotContain(montant.ToString("0"), categorie);
    }

    [Fact]
    public void Les_taux_sont_lus_dans_la_reponse_meme_ecrits_en_texte()
    {
        var taux = RechercheTaux.Extraire("""
            Voici :
            ```json
            {"bas":"3,05 %","moyen":3.4,"haut":3.95,"usure":"5.87","assurance":0.3,"periode":"novembre 2026",
             "sources":[{"nom":"Courtier","url":"https://www.courtier-exemple.fr/taux"},{"nom":"Sans lien"}]}
            ```
            """, "Gemini");

        Assert.NotNull(taux);
        Assert.Equal(3.05m, taux.Bas);
        Assert.Equal(3.4m, taux.Moyen);
        Assert.Equal(5.87m, taux.Usure);
        Assert.Equal(0.3m, taux.Assurance);
        Assert.Equal("novembre 2026", taux.Periode);
        Assert.Single(taux.Liens);
        Assert.Null(RechercheTaux.Extraire("{\"bas\":null,\"moyen\":null}", "Groq"));
        Assert.Null(RechercheTaux.Extraire("Je ne sais pas.", "Groq"));
    }

    [Fact]
    public void Sans_taux_le_message_montre_le_debut_de_la_reponse()
    {
        Assert.Equal("(vide)", CreditsViewModel.Extrait("  \n "));
        Assert.Equal("Je n'ai pas accès à internet.", CreditsViewModel.Extrait("Je n'ai pas\n accès à internet."));
        Assert.EndsWith("…", CreditsViewModel.Extrait(new string('a', 300)));
    }

    [Fact]
    public void Le_taeg_approche_ajoute_l_assurance()
    {
        Assert.Equal(3.8m, RechercheTaux.TaegApproche(new SimulationCredit("A", 200000m, 3.5m, 240, new PeriodeMois(2026, 12), 0.3m)));
        Assert.Equal(3.8m, RechercheTaux.TaegApproche(new SimulationCredit("B", 200000m, 3.5m, 240, new PeriodeMois(2026, 12), 50m, TypeAssurance.ParMois)));
    }

    [Fact]
    public void Les_deux_ia_cherchent_les_taux_et_le_taux_moyen_est_repris_avec_alerte_d_usure()
    {
        var compte = ConfigurationParDefaut.CreerDemo();
        compte.SimulationsCredit.Clear();
        compte.SimulationsCredit.Add(new SimulationCredit("Maison", 200000m, 6.2m, 240, new PeriodeMois(2026, 12), 0.3m));
        var gemini = new FausseIA(SourceOffre.Gemini, """{"bas":3.1,"moyen":3.45,"haut":3.9,"usure":5.9,"assurance":0.28,"periode":"novembre 2026","sources":[]}""");
        var groq = new FausseIA(SourceOffre.Groq, """{"bas":3.2,"moyen":3.5,"haut":4.0,"usure":5.87,"assurance":0.3,"periode":"novembre 2026","sources":[]}""");
        var ia = new ServicesIA(new SecretsEnMemoire(), new Dialogues(), assistantsTest: () => new IAssistantIA[] { gemini, groq });
        var enregistrements = 0;
        var credits = new CreditsViewModel(compte, new[] { new ChoixPeriode(new PeriodeMois(2026, 12)) }, new Dialogues(),
            () => enregistrements++, () => { }, ia, () => Aujourdhui);

        credits.ChercherTauxCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.Equal(2, credits.TauxTrouves.Count);
        Assert.Contains("20 ans et plus", gemini.Demandes.Single());
        Assert.DoesNotContain("200", gemini.Demandes.Single().Replace("2026", ""));
        Assert.Equal(5.87m, credits.Usure);
        Assert.Contains("dépasse le taux d'usure", credits.AlerteUsure);

        credits.ReprendreTauxCommand.Execute(credits.TauxTrouves[0]);
        Assert.Equal(3.45m, compte.SimulationsCredit[0].TauxAnnuel);
        Assert.Equal(0.28m, compte.SimulationsCredit[0].Assurance);
        Assert.Equal("", credits.AlerteUsure);
        Assert.True(enregistrements > 0);

        // Une autre durée change de catégorie : l'ancienne usure ne s'applique plus.
        credits.Selection!.Duree = 8;
        credits.Selection.TauxAnnuel = 9m;
        Assert.Equal("", credits.AlerteUsure);
    }

    private sealed class FausseIA : IAssistantIA
    {
        private readonly string _reponse;

        public FausseIA(SourceOffre source, string reponse)
        {
            Source = source;
            _reponse = reponse;
        }

        public List<string> Demandes { get; } = new();
        public SourceOffre Source { get; }
        public string Nom => Source.ToString();

        public Task<string> DemanderAsync(string demande, bool avecRecherche, CancellationToken annulation = default)
        {
            Assert.True(avecRecherche);
            Demandes.Add(demande);
            return Task.FromResult(_reponse);
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
