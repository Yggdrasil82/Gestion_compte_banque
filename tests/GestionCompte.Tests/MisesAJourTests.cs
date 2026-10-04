using System.Net;
using System.Security.Cryptography;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

/// <summary>Mises à jour : versions publiées sur GitHub, téléchargement vérifié, remplacement de l'exe.</summary>
public sealed class MisesAJourTests : IDisposable
{
    private readonly string _dossier = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_dossier, recursive: true);

    private static readonly byte[] Exe = "nouvel exe"u8.ToArray();

    private static string Publication(string etiquette, string notes, bool brouillon = false, string? digest = null) => $$"""
        { "tag_name": "{{etiquette}}", "draft": {{(brouillon ? "true" : "false")}}, "prerelease": false, "body": "{{notes}}",
          "assets": [ { "name": "GestionCompte.exe", "size": {{Exe.Length}}, "browser_download_url": "https://exemple/{{etiquette}}/GestionCompte.exe"
                        {{(digest is null ? "" : $", \"digest\": \"sha256:{digest}\"")}} } ] }
        """;

    private static ServiceMisesAJour Service(string json, byte[]? exe = null) => new(new HttpClient(new FauxGitHub(json, exe ?? Exe)));

    [Fact]
    public async Task Chercher_VersionsPlusRecentes_NouveautesDeChacune()
    {
        var json = $"[{Publication("v2.9.0", "- Bourse")},{Publication("v2.8.1", "- Correction")},{Publication("v2.8.0", "- Mises à jour")},{Publication("v3.0.0", "brouillon", brouillon: true)}]";

        var disponible = await Service(json).ChercherAsync(new Version(2, 8, 0, 0));

        Assert.NotNull(disponible);
        Assert.Equal(new Version(2, 9, 0), disponible.Derniere.Numero);
        Assert.Equal("Version 2.9.0\n- Bourse\n\nVersion 2.8.1\n- Correction", disponible.Nouveautes);
    }

    [Fact]
    public async Task Chercher_DejaAJour_Null()
    {
        Assert.Null(await Service($"[{Publication("v2.8.0", "")}]").ChercherAsync(new Version(2, 8, 0)));
    }

    [Fact]
    public async Task Telecharger_EmpreinteVerifiee()
    {
        var bonne = Convert.ToHexString(SHA256.HashData(Exe)).ToLowerInvariant();
        var version = ServiceMisesAJour.Lire($"[{Publication("v2.9.0", "", digest: bonne)}]")[0];

        var chemin = await Service("[]").TelechargerAsync(version, _dossier);

        Assert.Equal(Exe, File.ReadAllBytes(chemin));

        var fausse = version with { Sha256 = new string('0', 64) };
        await Assert.ThrowsAsync<InvalidDataException>(() => Service("[]").TelechargerAsync(fausse, _dossier));
        Assert.False(File.Exists(chemin));
    }

    [Fact]
    public void Remplacer_AncienGardeJusquauProchainLancement()
    {
        var exe = Path.Combine(_dossier, "GestionCompte.exe");
        var nouveau = Path.Combine(_dossier, "GestionCompte.nouveau.exe");
        File.WriteAllText(exe, "ancien");
        File.WriteAllText(nouveau, "nouveau");

        ServiceMisesAJour.Remplacer(exe, nouveau);

        Assert.Equal("nouveau", File.ReadAllText(exe));
        Assert.Equal("ancien", File.ReadAllText(Path.Combine(_dossier, "GestionCompte.ancien.exe")));
        ServiceMisesAJour.NettoyerAncienneVersion(exe);
        Assert.False(File.Exists(Path.Combine(_dossier, "GestionCompte.ancien.exe")));
    }

    [Fact]
    public void ViewModel_SansExe_RechercheImpossible()
    {
        var vm = new MisesAJourViewModel(Service("[]"), null!, new Version(2, 8, 0, 0), exe: null);

        Assert.False(vm.RechercherCommand.CanExecute(null));
        Assert.Equal("Version installée : 2.8.0", vm.VersionActuelle);
    }

    private sealed class FauxGitHub(string json, byte[] exe) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation) =>
            Task.FromResult(requete.RequestUri!.Host == "api.github.com"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(exe) });
    }
}
