using GestionCompte.Core;
using GestionCompte.Core.Modeles;
using GestionCompte.Data;

namespace GestionCompte.Tests;

/// <summary>Virements liés entre deux comptes : l'opération inverse est tenue à jour dans l'autre compte.</summary>
public sealed class VirementsLiesTests : IDisposable
{
    private const string Courant = "compte.db";
    private const string Livret = "compte-2.db";
    private static readonly PeriodeMois Octobre = new(2026, 10);
    private static readonly PeriodeMois Novembre = new(2026, 11);
    private static readonly PeriodeMois Decembre = new(2026, 12);

    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    private static CompteBancaire Compte(PeriodeMois premier, int nombreMois)
    {
        var compte = new CompteBancaire(new ConfigurationBudget { PremierMois = premier });
        for (var i = 0; i < nombreMois; i++)
            compte.CreerMoisSuivant();
        return compte;
    }

    private static Operation Virement(CompteBancaire compte, PeriodeMois periode, decimal debit, string libelle = "Virement livret")
    {
        var operation = new Operation(libelle, debit) { CompteLie = Livret, IdLien = VirementsLies.NouveauLien() };
        compte.Trouver(periode)!.Operations.Add(operation);
        return operation;
    }

    private static List<Operation> Lies(CompteBancaire compte) =>
        compte.Mois.SelectMany(m => m.Operations).Where(o => o.IdLien is not null).ToList();

    [Fact]
    public void Synchroniser_NouveauVirement_CreditCreeDansLAutreCompteEtMoisCree()
    {
        var courant = Compte(Octobre, 2);
        var virement = Virement(courant, Novembre, 200m);
        var livret = Compte(Octobre, 1);

        var resultat = VirementsLies.Synchroniser(courant, Courant, livret, Livret);

        Assert.True(resultat.Modifie);
        Assert.Equal(new[] { Novembre }, resultat.MoisCrees);
        var double_ = Assert.Single(livret.Trouver(Novembre)!.Operations);
        Assert.Equal(("Virement livret", 0m, 200m), (double_.Libelle, double_.Debit, double_.Credit));
        Assert.Equal((Courant, virement.IdLien), (double_.CompteLie, double_.IdLien));
        Assert.False(VirementsLies.Synchroniser(courant, Courant, livret, Livret).Modifie);
    }

    [Fact]
    public void Synchroniser_ModificationEtSuppression_SuiventDeLAutreCote()
    {
        var courant = Compte(Octobre, 2);
        var virement = Virement(courant, Novembre, 200m);
        var livret = Compte(Octobre, 2);
        VirementsLies.Synchroniser(courant, Courant, livret, Livret);

        virement.Debit = 250m;
        virement.Libelle = "Épargne vacances";
        livret.Trouver(Novembre)!.Operations[0].Pointee = true;
        VirementsLies.Synchroniser(courant, Courant, livret, Livret);
        var double_ = Assert.Single(Lies(livret));
        Assert.Equal(("Épargne vacances", 250m), (double_.Libelle, double_.Credit));
        Assert.True(double_.Pointee); // le pointage reste propre à chaque compte

        courant.Trouver(Novembre)!.Operations.Remove(virement);
        Assert.True(VirementsLies.Synchroniser(courant, Courant, livret, Livret).Modifie);
        Assert.Empty(Lies(livret));
    }

    [Fact]
    public void Synchroniser_DoubleModifie_ReporteDansLePremierCompte()
    {
        var courant = Compte(Octobre, 2);
        Virement(courant, Novembre, 200m);
        var livret = Compte(Octobre, 2);
        VirementsLies.Synchroniser(courant, Courant, livret, Livret);

        Lies(livret)[0].Credit = 300m;
        VirementsLies.Synchroniser(livret, Livret, courant, Courant);

        Assert.Equal(300m, Assert.Single(Lies(courant)).Debit);
    }

    [Fact]
    public void Synchroniser_VirementDelieOuChangeDeCompte_RetireDeLAncienCompte()
    {
        var courant = Compte(Octobre, 1);
        var virement = Virement(courant, Octobre, 80m);
        var livret = Compte(Octobre, 1);
        VirementsLies.Synchroniser(courant, Courant, livret, Livret);

        virement.CompteLie = "compte-3.db";
        VirementsLies.Synchroniser(courant, Courant, livret, Livret);

        Assert.Empty(Lies(livret));
    }

    [Fact]
    public void Synchroniser_AvantLePremierMoisDeLAutreCompte_Ignore()
    {
        var courant = Compte(Octobre, 2);
        Virement(courant, Octobre, 50m);
        var livret = Compte(Novembre, 1);

        var resultat = VirementsLies.Synchroniser(courant, Courant, livret, Livret);

        Assert.Equal(1, resultat.Ignores);
        Assert.Empty(Lies(livret));
    }

    [Fact]
    public void Synchroniser_ChargeLiee_ChargeInverseEtVirementDeChaqueMoisSansDoublon()
    {
        var courant = Compte(Octobre, 0);
        courant.Configuration.Charges.Add(new ModeleCharge("Épargne livret", 150m, Categorie: Categorie.Epargne)
            { CompteLie = Livret, Lien = "abc" });
        courant.CreerMoisSuivant();
        var livret = Compte(Octobre, 0);

        VirementsLies.Synchroniser(courant, Courant, livret, Livret);

        var miroir = Assert.Single(livret.Configuration.Charges);
        Assert.Equal(("Épargne livret", 0m, 150m, Courant, "abc"), (miroir.Nom, miroir.Debit, miroir.Credit, miroir.CompteLie, miroir.Lien));
        Assert.Equal("abc:2026-10", Assert.Single(livret.Trouver(Octobre)!.Operations).IdLien);

        // Le livret crée décembre (avant le compte courant) : le virement du mois porte le même lien des deux côtés.
        livret.CreerMoisSuivant();
        livret.CreerMoisSuivant();
        VirementsLies.Synchroniser(livret, Livret, courant, Courant);
        Assert.Equal(Decembre, courant.Mois[^1].Periode);
        Assert.Equal("abc:2026-12", Assert.Single(courant.Trouver(Decembre)!.Operations).IdLien);
        Assert.Equal(150m, courant.Trouver(Decembre)!.Operations[0].Debit);
        VirementsLies.Synchroniser(courant, Courant, livret, Livret);
        Assert.Single(livret.Trouver(Decembre)!.Operations);

        courant.Configuration.Charges.Clear();
        VirementsLies.Synchroniser(courant, Courant, livret, Livret);
        Assert.Empty(livret.Configuration.Charges);
    }

    [Fact]
    public void AppliquerConfiguration_ChargeLiee_LieLOperationDuMois()
    {
        var courant = Compte(Octobre, 1);
        courant.Configuration.Charges.Add(new ModeleCharge("Épargne livret", 150m));
        courant.AppliquerConfiguration(courant.Mois[0]);
        courant.Configuration.Charges[0] = courant.Configuration.Charges[0] with { CompteLie = Livret, Lien = "abc" };

        courant.AppliquerConfiguration(courant.Mois[0]);

        var operation = Assert.Single(courant.Mois[0].Operations);
        Assert.Equal((Livret, "abc:2026-10"), (operation.CompteLie, operation.IdLien));
    }

    [Fact]
    public void CopierPourNouveauCompte_ChargeLiee_NestPlusLiee()
    {
        var configuration = new ConfigurationBudget { PremierMois = Octobre };
        configuration.Charges.Add(new ModeleCharge("Épargne livret", 150m) { CompteLie = Livret, Lien = "abc" });

        var copie = configuration.CopierPourNouveauCompte(Novembre).Charges.Single();

        Assert.Null(copie.CompteLie);
        Assert.Null(copie.Lien);
    }

    [Fact]
    public void Depot_EnregistreEtRelitLesLiens()
    {
        var courant = Compte(Octobre, 0);
        courant.Configuration.Charges.Add(new ModeleCharge("Épargne livret", 150m) { CompteLie = Livret, Lien = "abc" });
        courant.CreerMoisSuivant();
        var virement = Virement(courant, Octobre, 20m);
        var depot = new DepotSqlite(Path.Combine(_dossier, Courant));

        depot.Enregistrer(courant);
        var relu = depot.Charger(Octobre)!;

        Assert.Equal((Livret, "abc"), (relu.Configuration.Charges[0].CompteLie, relu.Configuration.Charges[0].Lien));
        Assert.Equal(Lies(courant).Select(o => (o.CompteLie, o.IdLien)), Lies(relu).Select(o => (o.CompteLie, o.IdLien)));
        Assert.Contains(Lies(relu), o => o.IdLien == virement.IdLien);
    }

    [Fact]
    public void Signature_ChangeAvecLesVirements()
    {
        var courant = Compte(Octobre, 1);
        var avant = VirementsLies.Signature(courant, Livret);
        var virement = Virement(courant, Octobre, 20m);
        var apres = VirementsLies.Signature(courant, Livret);
        virement.Pointee = true;

        Assert.NotEqual(avant, apres);
        Assert.Equal(apres, VirementsLies.Signature(courant, Livret));
        Assert.Equal(new[] { Livret }, VirementsLies.ComptesLies(courant));
    }
}

/// <summary>Manuel intégré (rubrique « Aide »).</summary>
public sealed class ManuelTests
{
    [Fact]
    public void Charger_ChapitresDuManuelAvecLeursCaptures()
    {
        var chapitres = Presentation.Manuel.Charger();

        Assert.True(chapitres.Count >= 18);
        Assert.Equal("1. Premiers pas", chapitres[0].Titre);
        Assert.DoesNotContain(chapitres, c => c.Titre == "Sommaire");
        var images = chapitres.SelectMany(c => c.Blocs).OfType<Presentation.ImageManuel>().ToList();
        Assert.NotEmpty(images);
        foreach (var image in images)
        {
            using var flux = Presentation.Manuel.Image(image.Fichier);
            Assert.True(flux is not null, $"Capture absente : {image.Fichier}");
        }
    }

    [Fact]
    public void Lire_BlocsDuHtml()
    {
        const string html = """
            <section class="couverture"><h1>Mon Budget</h1></section>
            <section class="page sommaire"><h2>Sommaire</h2><ol><li>Premiers pas</li></ol>
              <h2>1. Premiers pas</h2>
              <p>Lancez <b>GestionCompte.exe</b> (<span class="touche">Ctrl + J</span>).</p>
              <div class="encart attention"><p>Avertissement</p></div>
              <ul><li>Un</li><li>Deux &amp; trois</li></ul>
            </section>
            <section class="page"><h3>Suite</h3>
              <figure class="demi"><img src="images/a.png" alt=""><figcaption>Légende</figcaption></figure>
              <table><tr><th style="width:28%">Zone</th><th>Rôle</th></tr><tr><td><b>Compte</b></td><td>Choix</td></tr></table>
            </section>
            """;

        var chapitre = Assert.Single(Presentation.Manuel.Lire(html));

        Assert.Equal("1. Premiers pas", chapitre.Titre);
        var paragraphe = Assert.IsType<Presentation.ParagrapheManuel>(chapitre.Blocs[0]);
        Assert.Equal(new Presentation.SegmentManuel("GestionCompte.exe", Gras: true), paragraphe.Segments[1]);
        Assert.Equal(new Presentation.SegmentManuel("Ctrl + J", Touche: true), paragraphe.Segments[3]);
        Assert.True(Assert.IsType<Presentation.EncartManuel>(chapitre.Blocs[1]).Attention);
        Assert.Equal("Deux & trois", Assert.IsType<Presentation.ListeManuel>(chapitre.Blocs[2]).Elements[1][0].Texte);
        Assert.Equal(new Presentation.TitreManuel(3, "Suite"), chapitre.Blocs[3]);
        Assert.Equal(new Presentation.ImageManuel("images/a.png", "Légende", 0.78), chapitre.Blocs[4]);
        var tableau = Assert.IsType<Presentation.TableauManuel>(chapitre.Blocs[5]);
        Assert.Equal(new double?[] { 28, null }, tableau.Largeurs);
        Assert.True(tableau.Lignes[0].Entete);
    }

    [Fact]
    public void Recherche_SansAccentsNiMajuscules()
    {
        var vm = new Presentation.ManuelViewModel(Presentation.Manuel.Charger());

        vm.Recherche = "remboursement anticipe";

        Assert.Contains(vm.ChapitresAffiches, c => c.Titre.Contains("Prêts"));
        Assert.Same(vm.ChapitresAffiches[0], vm.Selection);
        vm.Recherche = "zzzz";
        Assert.Empty(vm.ChapitresAffiches);
        Assert.Equal("Aucun chapitre ne contient ce texte.", vm.Resultat);
    }
}
