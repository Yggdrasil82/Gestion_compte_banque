using System.Security.Cryptography;
using GestionCompte.Core;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class PartageFortunixTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"));
    private static readonly DateTime Maintenant = new(2026, 11, 9, 18, 42, 0, DateTimeKind.Local);

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    private string Fichier => Path.Combine(_dossier, FichierPortefeuille.Nom);

    private PartageFortunixViewModel Partage(SecretsEnMemoire secrets, Dialogues dialogues) =>
        new(secrets, dialogues, iterations: 1000, maintenant: () => Maintenant);

    [Fact]
    public void Configurer_DeposeLePortefeuilleChiffre()
    {
        var compte = ConfigurationParDefaut.CreerDemoBourse();
        var secrets = new SecretsEnMemoire();
        var partage = Partage(secrets, new Dialogues(_dossier, "mot-de-passe-1"));

        Assert.True(partage.Configurer("Trade Republic", compte.Portefeuille));

        Assert.True(partage.Actif);
        Assert.Contains("partagé le 09/11/2026 à 18:42", partage.Etat);
        var contenu = File.ReadAllBytes(Fichier);
        Assert.DoesNotContain("Amundi", System.Text.Encoding.UTF8.GetString(contenu));
        var relu = FichierPortefeuille.Lire(contenu, "mot-de-passe-1");
        Assert.Equal("Trade Republic", relu.Compte);
        Assert.NotEmpty(relu.Titres);
        Assert.Equal(100m, Math.Round(relu.Titres.Sum(t => t.Part)), 0);
        Assert.Equal(relu.Valeur, relu.Titres.Sum(t => t.Valeur));
        Assert.Throws<AuthenticationTagMismatchException>(() => FichierPortefeuille.Lire(contenu, "autre-mot-de-passe"));
    }

    [Fact]
    public void Bourse_ChangementEnregistre_MetAJourLeFichierDuCompteSeulement()
    {
        var compte = ConfigurationParDefaut.CreerDemoBourse();
        var secrets = new SecretsEnMemoire();
        var partage = Partage(secrets, new Dialogues(_dossier, "mot-de-passe-1"));
        var enregistrements = 0;
        var bourse = new BourseViewModel(compte, new Dialogues(_dossier, null), () => enregistrements++,
            new ServiceCoursBourse(new HttpClient()), Maintenant) { Partage = partage, NomCompte = "Trade Republic" };
        Assert.False(bourse.PartageDeCeCompte);

        bourse.PartagerFortunixCommand.Execute(null);
        Assert.True(bourse.PartageDeCeCompte);
        File.Delete(Fichier);
        bourse.BasculerIdeesCommand.Execute(null);

        Assert.Equal(1, enregistrements);
        Assert.True(File.Exists(Fichier));

        // Un autre compte ne remplace pas le fichier partagé.
        File.Delete(Fichier);
        partage.Publier("Compte courant", compte.Portefeuille);
        Assert.False(File.Exists(Fichier));
    }

    [Theory]
    [InlineData(null, "mot-de-passe-1")]
    [InlineData("dossier", null)]
    [InlineData("dossier", "court")]
    public void Configurer_AnnuleOuMotDePasseTropCourt_RienNEstPartage(string? dossier, string? motDePasse)
    {
        var secrets = new SecretsEnMemoire();
        var dialogues = new Dialogues(dossier is null ? null : _dossier, motDePasse);
        var partage = Partage(secrets, dialogues);

        Assert.False(partage.Configurer("Trade Republic", ConfigurationParDefaut.CreerDemoBourse().Portefeuille));

        Assert.False(partage.Actif);
        Assert.False(File.Exists(Fichier));
        Assert.Equal(motDePasse == "court", dialogues.DerniereErreur is not null);
    }

    [Fact]
    public void Arreter_SupprimeLeFichierEtLesReglages()
    {
        var secrets = new SecretsEnMemoire();
        var partage = Partage(secrets, new Dialogues(_dossier, "mot-de-passe-1"));
        partage.Configurer("Trade Republic", ConfigurationParDefaut.CreerDemoBourse().Portefeuille);

        partage.ArreterCommand.Execute(null);

        Assert.False(partage.Actif);
        Assert.False(File.Exists(Fichier));
        Assert.Null(secrets.Lire(PartageFortunixViewModel.SecretMotDePasse));
        Assert.True(new PartageFortunixViewModel(secrets, new Dialogues(null, null)).NonActif);
    }

    [Fact]
    public void Lire_FichierEtranger_Refuse() =>
        Assert.Throws<FormatException>(() => FichierPortefeuille.Lire("{\"format\":\"autre\"}"u8.ToArray(), "x"));

    private sealed class Dialogues(string? dossier, string? motDePasse) : IDialogues
    {
        public string? DerniereErreur { get; private set; }

        public bool Confirmer(string titre, string message) => true;
        public void Erreur(string message) => DerniereErreur = message;
        public string? ChoisirDossier(string titre) => dossier;
        public string? DemanderMotDePasse(string titre, string message, bool confirmer) => motDePasse;
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => null;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => null;
        public void OuvrirDossier(string dossier) { }
    }
}
