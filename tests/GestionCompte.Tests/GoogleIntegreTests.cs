using GestionCompte.Data.Documents;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class GoogleIntegreTests
{
    [Fact]
    public void Les_identifiants_chiffres_se_relisent_et_ne_sont_pas_lisibles_en_clair()
    {
        var identifiants = new IdentifiantsGoogle("123-abc.apps.googleusercontent.com", "GOCSPX-exemple");
        var donnees = GoogleIntegre.Chiffrer(identifiants);

        Assert.DoesNotContain("apps.googleusercontent", donnees);
        Assert.DoesNotContain("GOCSPX", donnees);
        Assert.Equal(identifiants, GoogleIntegre.Dechiffrer(donnees));
    }

    [Theory]
    [InlineData("__GOOGLE_INTEGRE__")]
    [InlineData("")]
    [InlineData("pas du base64 !")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void Sans_identifiants_integres_valides_rien_n_est_renvoye(string donnees) =>
        Assert.Null(GoogleIntegre.Dechiffrer(donnees));

    [Fact]
    public void Le_depot_ne_contient_pas_d_identifiants_integres() =>
        Assert.Null(GoogleIntegre.Identifiants);

    [Fact]
    public async Task Avec_des_identifiants_integres_la_page_de_google_s_ouvre_sans_rien_demander()
    {
        var dialogues = new Dialogues();
        var google = new CompteGoogle(new SecretsEnMemoire(), dialogues, integres: new IdentifiantsGoogle("id-integre", "secret"));
        using var annulation = new CancellationTokenSource();
        dialogues.Ouvert = _ => annulation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => google.ConnecterAsync(annulation.Token));

        Assert.False(dialogues.IdentifiantsDemandes);
        Assert.Contains("client_id=id-integre", dialogues.Adresse);
    }

    private sealed class Dialogues : IDialogues
    {
        public bool IdentifiantsDemandes { get; private set; }
        public string Adresse { get; private set; } = "";
        public Action<string>? Ouvert { get; set; }
        public bool Confirmer(string titre, string message) => false;
        public void Erreur(string message) { }
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => null;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => null;
        public void OuvrirDossier(string dossier) { }

        public IdentifiantsGoogle? DemanderIdentifiantsGoogle(IdentifiantsGoogle? actuels)
        {
            IdentifiantsDemandes = true;
            return null;
        }

        public void OuvrirLien(string adresse)
        {
            Adresse = adresse;
            Ouvert?.Invoke(adresse);
        }
    }
}
