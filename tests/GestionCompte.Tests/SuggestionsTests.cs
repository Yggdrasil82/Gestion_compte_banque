using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public class SuggestionsTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "suggestions-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    // Réponses inventées, au format de Google Sheets (CSV publié).
    private const string Reponses =
        "Horodateur,Votre suggestion,Version\r\n" +
        "02/10/2026 18:05:12,Ajouter un thème vert,2.10.0\r\n" +
        "\"11/10/2026 09:30:00\",\"Exporter en CSV, avec \"\"séparateur\"\" ;\nsur deux lignes\",2.11.0\r\n" +
        "05/10/2026 12:00:00,,2.10.0\r\n";

    [Fact]
    public void Lire_PlusRecentesDAbord_ChampsEntreGuillemets()
    {
        var liste = SuggestionsViewModel.Lire(Reponses, null);

        Assert.Equal(2, liste.Count);
        Assert.Equal("Exporter en CSV, avec \"séparateur\" ;\nsur deux lignes", liste[0].Texte);
        Assert.Equal("2.11.0", liste[0].Version);
        Assert.Equal(new DateTime(2026, 10, 11, 9, 30, 0), liste[0].Date);
        Assert.Equal("11/10/2026 à 09:30 · version 2.11.0", liste[0].Entete);
        Assert.All(liste, s => Assert.True(s.Nouvelle));
    }

    [Fact]
    public void Lire_PageWeb_Refusee()
    {
        Assert.Throws<FormatException>(() => SuggestionsViewModel.Lire("<!DOCTYPE html><html>", null));
    }

    [Fact]
    public async Task Actualiser_CompteLesNouvelles_PuisMarquerVues()
    {
        var apparence = new ApparenceViewModel(Path.Combine(_dossier, "preferences.json"))
        {
            LienReponsesSuggestions = "https://exemple.invalid/reponses.csv",
            SuggestionsVuesJusquA = new DateTime(2026, 10, 5),
        };
        var vm = new SuggestionsViewModel(apparence, new Dialogues(), "2.11.0", telecharger: _ => Task.FromResult(Reponses));

        await vm.ActualiserAsync();

        Assert.True(vm.OngletDisponible);
        Assert.Equal(1, vm.NombreNouvelles);
        Assert.Equal("Suggestions (1)", vm.TitreNavigation);

        vm.MarquerVues();

        Assert.Equal("Suggestions", vm.TitreNavigation);
        Assert.Equal(new DateTime(2026, 10, 11, 9, 30, 0), new ApparenceViewModel(Path.Combine(_dossier, "preferences.json")).SuggestionsVuesJusquA);
    }

    [Fact]
    public async Task Actualiser_SansLien_NeLitRien()
    {
        var lu = false;
        var vm = new SuggestionsViewModel(new ApparenceViewModel(null), new Dialogues(), "2.11.0",
            telecharger: _ => { lu = true; return Task.FromResult(Reponses); });

        await vm.ActualiserAsync();

        Assert.False(lu);
        Assert.False(vm.OngletDisponible);
    }

    [Fact]
    public async Task EnregistrerLien_RefuseUnLienNonHttps()
    {
        var dialogues = new Dialogues();
        var apparence = new ApparenceViewModel(null);
        var vm = new SuggestionsViewModel(apparence, dialogues, "2.11.0", telecharger: _ => Task.FromResult(Reponses));

        vm.LienReponses = "docs.google.com/reponses";
        await vm.EnregistrerLienCommand.ExecuteAsync(null);
        Assert.Single(dialogues.Erreurs);
        Assert.Equal("", apparence.LienReponsesSuggestions);

        vm.LienReponses = " https://exemple.invalid/pub?output=csv ";
        await vm.EnregistrerLienCommand.ExecuteAsync(null);
        Assert.Equal("https://exemple.invalid/pub?output=csv", apparence.LienReponsesSuggestions);
        Assert.Equal(2, vm.Liste.Count);
    }

    [Fact]
    public void Suggerer_OuvreLeFormulaireAvecLaVersion()
    {
        var dialogues = new Dialogues();
        var vm = new SuggestionsViewModel(new ApparenceViewModel(null), dialogues, "2.11.0",
            "https://docs.google.com/forms/d/e/EXEMPLE/viewform?usp=pp_url&entry.123=0.0.0");

        vm.SuggererCommand.Execute(null);

        Assert.True(vm.FormulaireDisponible);
        Assert.Equal("https://docs.google.com/forms/d/e/EXEMPLE/viewform?usp=pp_url&entry.123=2.11.0", Assert.Single(dialogues.Liens));
    }

    private sealed class Dialogues : IDialogues
    {
        public List<string> Liens { get; } = new();
        public List<string> Erreurs { get; } = new();

        public bool Confirmer(string titre, string message) => true;
        public void Erreur(string message) => Erreurs.Add(message);
        public string? ChoisirFichierSauvegarde(string nomParDefaut) => null;
        public string? ChoisirFichierExport(string nomParDefaut) => null;
        public string? ChoisirFichierARestaurer() => null;
        public string? ChoisirReleve() => null;
        public void OuvrirDossier(string dossier) { }
        public void OuvrirLien(string adresse) => Liens.Add(adresse);
    }
}
