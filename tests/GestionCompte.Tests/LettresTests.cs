using DocumentFormat.OpenXml.Packaging;
using GestionCompte.Core.Achats;
using GestionCompte.Core.Lettres;
using GestionCompte.Core.Modeles;
using GestionCompte.Data.Achats;
using GestionCompte.Data.Lettres;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class LettresTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), $"gestioncompte-lettres-{Guid.NewGuid():N}");
    private readonly Dialogues _dialogues = new();
    private static readonly DateTime Aujourdhui = new(2026, 11, 15);

    private const string ReponseGemini = """
        **Objet : Résiliation de mon abonnement internet**

        Madame, Monsieur,

        Je vous informe de ma décision de résilier mon abonnement n° [numéro de contrat].

        Veuillez agréer, Madame, Monsieur, mes salutations distinguées.
        """;

    public LettresTests() => Directory.CreateDirectory(_dossier);

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, true);
    }

    [Fact]
    public void La_demande_a_l_ia_ne_contient_pas_les_coordonnees()
    {
        var modele = ModeleLettre.Liste[0];
        var demande = RedactionLettre.Demande(modele, new[] { ("Organisme", "Box Internet"), ("Numéro de client ou de contrat", ""), ("Motif (facultatif)", "déménagement") }, "ton ferme");

        Assert.Contains("Organisme : Box Internet", demande);
        Assert.Contains("Motif (facultatif) : déménagement", demande);
        Assert.DoesNotContain("Numéro de client", demande);
        Assert.Contains("Précisions : ton ferme", demande);
        Assert.Contains("entre crochets", demande);
    }

    [Fact]
    public void La_reponse_est_decoupee_en_objet_et_corps()
    {
        var (objet, corps) = RedactionLettre.Decouper(ReponseGemini);

        Assert.Equal("Résiliation de mon abonnement internet", objet);
        Assert.StartsWith("Madame, Monsieur,", corps);
        Assert.EndsWith("salutations distinguées.", corps);
    }

    [Fact]
    public void Les_coordonnees_et_la_date_encadrent_la_lettre()
    {
        var coordonnees = new Coordonnees { Nom = "Camille Martin", Adresse = "3 rue des Lilas\n69000 Lyon", Ville = "Lyon", Email = "camille@exemple.fr" };
        var lettre = new Lettre { Date = Aujourdhui, Destinataire = "Box Internet\nService résiliation", Objet = "Résiliation", Corps = "Madame, Monsieur,\n\nTexte." };

        Assert.Equal(new[] { "Camille Martin", "3 rue des Lilas", "69000 Lyon", "camille@exemple.fr" }, coordonnees.Lignes());
        Assert.Equal("Fait à Lyon, le 15 novembre 2026", lettre.LieuDate(coordonnees));
        var texte = lettre.TexteComplet(coordonnees);
        Assert.StartsWith("Camille Martin", texte);
        Assert.Contains("Objet : Résiliation", texte);
        Assert.Equal("Le 15 novembre 2026", lettre.LieuDate(new Coordonnees()));
    }

    [Fact]
    public void Les_deux_ia_proposent_chacune_une_version_et_on_garde_celle_choisie()
    {
        var ia = new ServicesIA(new SecretsEnMemoire(), _dialogues, assistantsTest: () => new IAssistantIA[]
        {
            new FausseIA(SourceOffre.Gemini, ReponseGemini),
            new FausseIA(SourceOffre.Groq, "Objet : Résiliation\n\nMadame, Monsieur,\n\nVersion Groq."),
        });
        var lettres = new LettresViewModel(_dossier, ia, _dialogues, () => Aujourdhui);
        Assert.False(lettres.RedigerCommand.CanExecute(null));

        lettres.Champs[0].Valeur = "Box Internet";
        Assert.True(lettres.RedigerCommand.CanExecute(null));
        lettres.RedigerCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.Equal(new[] { "Gemini", "Groq" }, lettres.Versions.Select(v => v.Source));
        lettres.ChoisirVersionCommand.Execute(lettres.Versions[1]);
        Assert.Equal("Box Internet", lettres.Destinataire);
        Assert.Equal("Résiliation", lettres.Objet);
        Assert.Contains("Version Groq.", lettres.Corps);

        lettres.Corps += "\n\nAjout manuel.";
        lettres.GarderCommand.Execute(null);
        var relues = new LettresViewModel(_dossier, ia, _dialogues, () => Aujourdhui);
        Assert.Single(relues.Historique);
        Assert.Contains("Ajout manuel.", relues.Historique[0].Lettre.Corps);
    }

    [Fact]
    public void Une_ia_en_erreur_n_empeche_pas_l_autre()
    {
        var ia = new ServicesIA(new SecretsEnMemoire(), _dialogues, assistantsTest: () => new IAssistantIA[]
        {
            new FausseIA(SourceOffre.Gemini, null),
            new FausseIA(SourceOffre.Groq, ReponseGemini),
        });
        var lettres = new LettresViewModel(_dossier, ia, _dialogues, () => Aujourdhui);
        lettres.Champs[0].Valeur = "Box";
        lettres.RedigerCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.Contains("limite gratuite", lettres.Erreur);
        Assert.Single(lettres.Versions, v => v.Reussie);
    }

    [Fact]
    public void Les_coordonnees_sont_gardees_et_la_lettre_part_par_mail_en_pdf()
    {
        _dialogues.Coordonnees = new Coordonnees { Nom = "Camille Martin", Adresse = "3 rue des Lilas", Ville = "Lyon" };
        var ia = new ServicesIA(new SecretsEnMemoire(), _dialogues, assistantsTest: () => new IAssistantIA[] { new FausseIA(SourceOffre.Gemini, ReponseGemini) });
        var lettres = new LettresViewModel(_dossier, ia, _dialogues, () => Aujourdhui);
        Assert.True(lettres.CoordonneesVides);
        lettres.ModifierCoordonneesCommand.Execute(null);
        Assert.False(lettres.CoordonneesVides);

        lettres.Champs[0].Valeur = "Box Internet";
        lettres.RedigerCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        lettres.ChoisirVersionCommand.Execute(lettres.Versions[0]);
        LettreAEnvoyer? envoi = null;
        lettres.EnvoiParMailDemande += (_, l) => envoi = l;
        lettres.EnvoyerParMailCommand.Execute(null);

        Assert.NotNull(envoi);
        Assert.Equal("Résiliation de mon abonnement internet", envoi.Objet);
        Assert.StartsWith("Camille Martin", envoi.Texte);
        Assert.EndsWith(".pdf", envoi.Piece.Nom);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(envoi.Piece.Contenu, 0, 4));
        Assert.Equal("Camille Martin", new LettresViewModel(_dossier, ia, _dialogues).ResumeCoordonnees.Split(" · ")[0]);
    }

    [Fact]
    public void La_lettre_est_exportee_en_word()
    {
        var chemin = Path.Combine(_dossier, "lettre.docx");
        var lettre = new Lettre { Date = Aujourdhui, Destinataire = "Banque", Objet = "Frais", Corps = "Madame, Monsieur,\n\nPremier paragraphe.\n\nCordialement." };
        ExportLettre.ExporterWord(lettre, new Coordonnees { Nom = "Camille Martin" }, chemin);

        using var document = WordprocessingDocument.Open(chemin, false);
        var texte = document.MainDocumentPart!.Document.Body!.InnerText;
        Assert.Contains("Camille Martin", texte);
        Assert.Contains("Objet : Frais", texte);
        Assert.Contains("Premier paragraphe.", texte);
    }

    [Fact]
    public void Le_resume_du_budget_donne_les_chiffres_sans_libelles_d_operations()
    {
        var compte = ConfigurationParDefaut.CreerDemoHistorique();
        var resume = ResumeBudget.Construire(compte, compte.Mois[^1].Periode);

        Assert.Contains("revenus", resume);
        Assert.Contains("Prévisionnel des 6 prochains mois", resume);
        Assert.Contains("Bilan des 12 derniers mois", resume);
        // Les noms de charges et de postes apparaissent ; les libellés propres aux opérations, non.
        var noms = compte.Configuration.Charges.Select(c => c.Nom).ToHashSet();
        var libelles = compte.Mois.SelectMany(m => m.Operations).Select(o => o.Libelle)
            .Where(l => !string.IsNullOrWhiteSpace(l) && !noms.Contains(l) && !noms.Any(n => l.Contains(n))).Distinct().Take(20).ToList();
        Assert.NotEmpty(libelles);
        foreach (var libelle in libelles)
            Assert.DoesNotContain(libelle, resume);
    }

    [Fact]
    public void L_assistant_envoie_le_resume_si_coche_et_ne_garde_que_les_ia_choisies()
    {
        var gemini = new FausseIA(SourceOffre.Gemini, "Réduisez les loisirs.");
        var groq = new FausseIA(SourceOffre.Groq, "Épargnez 50 € de plus.");
        var ia = new ServicesIA(new SecretsEnMemoire(), _dialogues, assistantsTest: () => new IAssistantIA[] { gemini, groq });
        var assistant = new AssistantViewModel(ia, () => "Solde : 1 234 €");
        Assert.Equal("Solde : 1 234 €", assistant.Resume);

        assistant.Question = "Où économiser ?";
        assistant.EnvoyerCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        Assert.Equal(3, assistant.Conversation.Count);
        Assert.Contains("Solde : 1 234 €", gemini.Demandes.Single());
        Assert.Equal("", assistant.Question);

        assistant.InclureResume = false;
        assistant.IAChoisie = "Groq";
        assistant.PoserCommand.ExecuteAsync("Et ensuite ?").GetAwaiter().GetResult();
        Assert.Single(gemini.Demandes);
        var derniere = groq.Demandes.Last();
        Assert.DoesNotContain("Solde : 1 234 €", derniere);
        Assert.Contains("Réduisez les loisirs.", derniere);
        Assert.Equal("Groq", assistant.Conversation.Last().Auteur);

        assistant.EffacerCommand.Execute(null);
        Assert.False(assistant.AvecConversation);
    }

    private sealed class FausseIA : IAssistantIA
    {
        private readonly string? _reponse;

        public FausseIA(SourceOffre source, string? reponse)
        {
            Source = source;
            _reponse = reponse;
        }

        public List<string> Demandes { get; } = new();
        public SourceOffre Source { get; }
        public string Nom => Source.ToString();

        public Task<string> DemanderAsync(string demande, bool avecRecherche, CancellationToken annulation = default)
        {
            Demandes.Add(demande);
            return _reponse is null ? throw new HttpRequestException($"{Nom} : limite gratuite atteinte.") : Task.FromResult(_reponse);
        }
    }

    private sealed class Dialogues : IDialogues
    {
        public Coordonnees? Coordonnees { get; set; }

        public bool Confirmer(string titre, string message) => true;
        public void Erreur(string message) => throw new InvalidOperationException(message);
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => null;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => null;
        public void OuvrirDossier(string dossier) { }
        public Coordonnees? DemanderCoordonnees(Coordonnees actuelles) => Coordonnees;
    }
}
