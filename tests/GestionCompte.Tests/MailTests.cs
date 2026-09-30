using System.Net;
using System.Text;
using GestionCompte.Core.Documents;
using GestionCompte.Core.Mail;
using GestionCompte.Data;
using GestionCompte.Data.Documents;
using GestionCompte.Data.Mail;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class MailTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), $"gestioncompte-mail-{Guid.NewGuid():N}");

    public MailTests() => Directory.CreateDirectory(_dossier);

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, true);
    }

    [Fact]
    public void Les_adresses_saisies_sont_decoupees_et_verifiees()
    {
        var (adresses, invalides) = AdressesMail.Decouper("a@b.fr; Assurance <contact@assur.fr>, A@B.fr ; pas-une-adresse");

        Assert.Equal(new[] { "a@b.fr", "contact@assur.fr" }, adresses);
        Assert.Equal(new[] { "pas-une-adresse" }, invalides);
    }

    [Fact]
    public void Le_carnet_garde_contacts_et_historique_et_fusionne_les_contacts_google()
    {
        var carnet = new CarnetMail(_dossier);
        carnet.Contacts.Add(new Contact { Nom = "", Email = "banque@exemple.fr" });
        carnet.NoterEnvoi(new EnvoiMail { Date = new DateTime(2026, 11, 15, 10, 0, 0), Destinataires = { "assurance@exemple.fr" }, Reussi = true });
        carnet.NoterEnvoi(new EnvoiMail { Date = new DateTime(2026, 11, 15, 11, 0, 0), Destinataires = { "echec@exemple.fr" }, Reussi = false, Erreur = "refusé" });

        var ajoutes = carnet.Fusionner(new[] { ("Ma banque", "BANQUE@exemple.fr"), ("Jean", "jean@exemple.fr"), ("Sans adresse", "") });
        carnet.Enregistrer();

        Assert.Equal(1, ajoutes);
        var relu = new CarnetMail(_dossier);
        relu.Charger();
        Assert.Equal(new[] { "banque@exemple.fr", "assurance@exemple.fr", "jean@exemple.fr" }, relu.Contacts.Select(c => c.Email));
        Assert.Equal("Ma banque", relu.Contacts[0].Nom);
        Assert.Equal(1, relu.Contacts[1].NombreEnvois);
        Assert.Equal(SourceContact.Google, relu.Contacts[2].Source);
        Assert.Equal(2, relu.Historique.Count);
        Assert.False(relu.Historique[0].Reussi); // le plus récent d'abord
    }

    [Fact]
    public async Task Gmail_recoit_le_mail_au_format_standard_avec_ses_pieces_jointes()
    {
        var faux = new FauxHttp((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"1\"}") });
        var envoi = new EnvoiGmail(new HttpClient(faux), _ => Task.FromResult("jeton"), null, null);

        await envoi.EnvoyerAsync(new MessageMail(new[] { "assurance@exemple.fr" }, new[] { "moi@exemple.fr" }, "Attestation",
            "Bonjour,\nci-joint mon attestation.", new[] { new PieceJointe("attestation.pdf", Encoding.ASCII.GetBytes("%PDF-demo")) }));

        var requete = Assert.Single(faux.Requetes);
        Assert.Equal("message/rfc822", requete.Type);
        Assert.Equal("Bearer jeton", requete.Autorisation);
        Assert.Contains("To: assurance@exemple.fr", requete.Corps);
        Assert.Contains("Cc: moi@exemple.fr", requete.Corps);
        Assert.Contains("Subject: Attestation", requete.Corps);
        Assert.Contains("filename=attestation.pdf", requete.Corps);
    }

    [Fact]
    public async Task Gmail_explique_quand_le_droit_d_envoi_manque()
    {
        var faux = new FauxHttp((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"error\":{\"message\":\"Request had insufficient authentication scopes.\"}}"),
        });
        var envoi = new EnvoiGmail(new HttpClient(faux), _ => Task.FromResult("jeton"), null, null);

        var erreur = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            envoi.EnvoyerAsync(new MessageMail(new[] { "a@b.fr" }, Array.Empty<string>(), "x", "", Array.Empty<PieceJointe>())));
        Assert.Contains("reconnectez", erreur.Message);
    }

    [Fact]
    public async Task Les_contacts_google_sont_lus_page_par_page()
    {
        var faux = new FauxHttp((adresse, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(adresse.Contains("otherContacts")
                ? "{\"otherContacts\":[{\"emailAddresses\":[{\"value\":\"ancien@exemple.fr\"}]}]}"
                : adresse.Contains("pageToken=p2")
                    ? "{\"connections\":[{\"names\":[{\"displayName\":\"Paul\"}],\"emailAddresses\":[{\"value\":\"paul@exemple.fr\"}]}]}"
                    : "{\"connections\":[{\"names\":[{\"displayName\":\"Anne\"}],\"emailAddresses\":[{\"value\":\"anne@exemple.fr\"},{\"value\":\"anne.pro@exemple.fr\"}]},{\"names\":[{\"displayName\":\"Sans mail\"}]}],\"nextPageToken\":\"p2\"}"),
        });

        var contacts = await ContactsGoogle.LireAsync(new HttpClient(faux), _ => Task.FromResult("jeton"), avecAutres: true);

        Assert.Equal(new[] { ("Anne", "anne@exemple.fr"), ("Anne", "anne.pro@exemple.fr"), ("Paul", "paul@exemple.fr"), ("", "ancien@exemple.fr") }, contacts);
    }

    [Fact]
    public async Task L_ecran_envoie_le_mail_note_l_historique_et_le_contact()
    {
        var envoi = new FauxEnvoi();
        var (vm, _) = await Creer(envoi);
        vm.Destinataires = "assurance@exemple.fr";
        vm.Objet = "Attestation";
        vm.Corps = "Bonjour";
        var source = Path.Combine(_dossier, "facture.pdf");
        await File.WriteAllBytesAsync(source, new byte[] { 1, 2, 3 });
        _dialogues.Fichier = source;
        vm.JoindreFichierCommand.Execute(null);
        vm.JoindreFichierCommand.Execute(null);
        Assert.Equal(new[] { "facture.pdf", "facture (2).pdf" }, vm.PiecesJointes.Select(p => p.Nom));
        Assert.Equal("2 pièces jointes · 6 o", vm.TaillePieces);

        await vm.EnvoyerCommand.ExecuteAsync(null);

        var message = Assert.Single(envoi.Messages);
        Assert.Equal(2, message.PiecesJointes.Count);
        Assert.Equal("", vm.Erreur);
        Assert.Equal("Mail envoyé à assurance@exemple.fr.", vm.Statut);
        Assert.Empty(vm.PiecesJointes);
        Assert.Equal("", vm.Objet);
        var trace = Assert.Single(vm.Historique);
        Assert.True(trace.Reussi);
        Assert.Equal("Envoyé · facture.pdf, facture (2).pdf", trace.Detail);
        Assert.Equal("assurance@exemple.fr", Assert.Single(vm.Contacts).Email);

        // L'historique et le carnet sont gardés pour la prochaine fois.
        var (relu, _) = await Creer(envoi);
        Assert.Single(relu.Historique);
        relu.ContactSelectionne = relu.Contacts[0];
        relu.EcrireAuContactCommand.Execute(null);
        relu.EcrireAuContactCommand.Execute(null);
        Assert.Equal("assurance@exemple.fr", relu.Destinataires);
        relu.ReprendreCommand.Execute(relu.Historique[0]);
        Assert.Equal("Attestation", relu.Objet);
    }

    [Fact]
    public async Task Plusieurs_contacts_s_ecrivent_se_suppriment_ensemble_et_le_carnet_se_vide()
    {
        var (vm, _) = await Creer(new FauxEnvoi());
        foreach (var adresse in new[] { "a@exemple.fr", "b@exemple.fr", "c@exemple.fr", "d@exemple.fr" })
        {
            _dialogues.Contact = new Contact { Email = adresse };
            vm.NouveauContactCommand.Execute(null);
        }
        Assert.Equal(4, vm.NombreContacts);

        vm.ContactsSelectionnes = vm.Contacts.Where(c => c.Email is "a@exemple.fr" or "b@exemple.fr").ToList();
        vm.EcrireAuContactCommand.Execute(null);
        Assert.Equal("a@exemple.fr; b@exemple.fr", vm.Destinataires);

        vm.SupprimerContactCommand.Execute(null);
        Assert.Equal(new[] { "c@exemple.fr", "d@exemple.fr" }, vm.Contacts.Select(c => c.Email).Order());
        Assert.Empty(vm.ContactsSelectionnes);

        Assert.True(vm.ViderCarnetCommand.CanExecute(null));
        vm.ViderCarnetCommand.Execute(null);
        Assert.Equal(0, vm.NombreContacts);
        Assert.False(vm.ViderCarnetCommand.CanExecute(null));
        var (relu, _) = await Creer(new FauxEnvoi());
        Assert.Empty(relu.Contacts);
    }

    [Fact]
    public async Task Un_echec_d_envoi_garde_le_mail_et_le_note_dans_l_historique()
    {
        var envoi = new FauxEnvoi { Erreur = "Identifiant ou mot de passe refusé par le serveur d'envoi." };
        var (vm, _) = await Creer(envoi);
        vm.Destinataires = "a@b.fr";
        vm.Objet = "Test";

        await vm.EnvoyerCommand.ExecuteAsync(null);

        Assert.Equal("Le mail n'est pas parti : Identifiant ou mot de passe refusé par le serveur d'envoi.", vm.Erreur);
        Assert.Equal("Test", vm.Objet);
        Assert.False(Assert.Single(vm.Historique).Reussi);
        Assert.Empty(vm.Contacts);
    }

    [Fact]
    public async Task Une_adresse_incorrecte_bloque_l_envoi()
    {
        var envoi = new FauxEnvoi();
        var (vm, _) = await Creer(envoi);
        vm.Destinataires = "a@b.fr; mauvaise";

        await vm.EnvoyerCommand.ExecuteAsync(null);

        Assert.Equal("Adresse incorrecte : mauvaise", vm.Erreur);
        Assert.Empty(envoi.Messages);
    }

    [Fact]
    public async Task Les_documents_du_coffre_se_joignent_meme_proteges()
    {
        var (vm, documents) = await Creer(new FauxEnvoi());
        var coffre = new CoffreDocuments(new StockageDossier(documents.DossierLocal), 1_000);
        await coffre.ChargerAsync();
        await coffre.ConfigurerProtectionAsync("mot-de-passe");
        var rib = await coffre.AjouterAsync(new DocumentImportant { Nom = "RIB", NomFichier = "rib.pdf" }, new byte[] { 5, 5 });
        await coffre.ProtegerAsync(rib, true);
        await coffre.AjouterAsync(new DocumentImportant { Nom = "Bail", NomFichier = "bail.pdf" }, new byte[] { 6 });
        await documents.ActualiserCommand.ExecuteAsync(null);
        _dialogues.Choix = new[] { 0, 1 };
        _dialogues.MotDePasse = "mot-de-passe";

        await vm.JoindreDocumentsCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "bail.pdf", "rib.pdf" }, vm.PiecesJointes.Select(p => p.Nom));
        Assert.Equal(new byte[] { 5, 5 }, vm.PiecesJointes[1].Piece.Contenu);
        Assert.Equal("Bail", vm.Objet);
    }

    [Fact]
    public async Task Le_bilan_et_un_document_s_envoient_depuis_leur_onglet()
    {
        var chemin = Path.Combine(_dossier, "compte.db");
        new DepotSqlite(chemin).Enregistrer(ConfigurationParDefaut.CreerDemoHistorique());
        var vm = new MainViewModel(new DepotSqlite(chemin), _dialogues, new DateTime(2026, 11, 15), new ApparenceViewModel(null));
        await vm.Documents.Chargement;
        Assert.True(vm.Bilan.EnvoiParMailPossible);

        vm.Bilan.EnvoyerParMailCommand.Execute(null);

        Assert.Equal(MainViewModel.OngletMail, vm.OngletSelectionne);
        var piece = Assert.Single(vm.Mail.PiecesJointes);
        Assert.Equal("Bilan 2026.pdf", piece.Nom);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(piece.Piece.Contenu, 0, 4));

        vm.Apparence.ModuleMail = false;
        Assert.Equal(MainViewModel.OngletConfiguration, vm.OngletSelectionne);
        Assert.False(vm.Bilan.EnvoiParMailPossible);
        Assert.False(vm.Documents.EnvoiParMailPossible);
    }

    [Fact]
    public void Sans_compte_pret_l_envoi_est_impossible()
    {
        var google = new CompteGoogle(new SecretsEnMemoire(), _dialogues);
        var documents = new DocumentsViewModel(new ApparenceViewModel(null), _dossier, google, _dialogues, new DateOnly(2026, 11, 15),
            () => Array.Empty<string>(), 1_000);
        var reglages = new ApparenceViewModel(null);
        var secrets = new SecretsEnMemoire();
        var vm = new MailViewModel(reglages, _dossier, google, secrets, _dialogues, documents, () => null);

        Assert.False(vm.EnvoyerCommand.CanExecute(null));
        Assert.StartsWith("Gmail : connectez", vm.CompteEnvoi);

        _dialogues.Smtp = new SaisieSmtp(new ReglagesSmtp("smtp.orange.fr", 465, SecuriteSmtp.Ssl, "romain@orange.fr", "", "Romain"), "secret-test");
        vm.ReglerSmtpCommand.Execute(null);

        Assert.True(vm.ModeSmtp);
        Assert.Equal(ModeEnvoiMail.Smtp, reglages.ModeEnvoiMail);
        Assert.True(vm.EnvoyerCommand.CanExecute(null));
        Assert.Equal("Serveur smtp.orange.fr : romain@orange.fr", vm.CompteEnvoi);
        Assert.Equal("secret-test", secrets.Lire(MailViewModel.SecretSmtpMotDePasse));
    }

    private readonly Dialogues _dialogues = new();

    private async Task<(MailViewModel, DocumentsViewModel)> Creer(FauxEnvoi envoi)
    {
        var google = new CompteGoogle(new SecretsEnMemoire(), _dialogues);
        var documents = new DocumentsViewModel(new ApparenceViewModel(null), _dossier, google, _dialogues, new DateOnly(2026, 11, 15),
            () => Array.Empty<string>(), 1_000);
        await documents.Chargement;
        var vm = new MailViewModel(new ApparenceViewModel(null), _dossier, google, new SecretsEnMemoire(), _dialogues, documents, () => null,
            () => envoi);
        vm.ComptePret = true;
        return (vm, documents);
    }

    private sealed class FauxEnvoi : IEnvoiMail
    {
        public List<MessageMail> Messages { get; } = new();
        public string? Erreur { get; init; }
        public string Description => "Test";

        public Task EnvoyerAsync(MessageMail message, CancellationToken annulation = default)
        {
            if (Erreur is not null)
                throw new InvalidOperationException(Erreur);
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FauxHttp : HttpMessageHandler
    {
        private readonly Func<string, HttpRequestMessage, HttpResponseMessage> _reponse;

        public FauxHttp(Func<string, HttpRequestMessage, HttpResponseMessage> reponse) => _reponse = reponse;

        public List<(string Type, string Autorisation, string Corps)> Requetes { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
        {
            Requetes.Add((requete.Content?.Headers.ContentType?.MediaType ?? "", requete.Headers.Authorization?.ToString() ?? "",
                requete.Content is null ? "" : await requete.Content.ReadAsStringAsync(annulation)));
            return _reponse(requete.RequestUri!.ToString(), requete);
        }
    }

    private sealed class Dialogues : IDialogues
    {
        public string? Fichier { get; set; }
        public IReadOnlyList<int>? Choix { get; set; }
        public string? MotDePasse { get; set; }
        public SaisieSmtp? Smtp { get; set; }
        public Contact? Contact { get; set; }

        public Contact? DemanderContact(Contact? actuel) => Contact;

        public bool Confirmer(string titre, string message) => true;
        public void Erreur(string message) => throw new InvalidOperationException(message);
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => null;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => null;
        public void OuvrirDossier(string dossier) { }
        public string? ChoisirFichierAJoindre() => Fichier;
        public IReadOnlyList<int>? ChoisirParmi(string titre, string message, IReadOnlyList<string> elements) => Choix;
        public string? DemanderMotDePasse(string titre, string message, bool confirmer) => MotDePasse;
        public SaisieSmtp? DemanderReglagesSmtp(ReglagesSmtp? actuels) => Smtp;
    }
}
