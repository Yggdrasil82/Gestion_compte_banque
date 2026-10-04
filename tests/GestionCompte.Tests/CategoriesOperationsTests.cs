using GestionCompte.Core;
using GestionCompte.Core.Modeles;
using GestionCompte.Data;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class CategoriesOperationsTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"));

    public CategoriesOperationsTests() => Directory.CreateDirectory(_dossier);

    public void Dispose() => Directory.Delete(_dossier, recursive: true);

    private static CompteBancaire CompteAvecCategories()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.Configuration.CategoriesOperations.Add(new CategorieOperation("Agen", "#2E9E5B"));
        compte.Configuration.CategoriesOperations.Add(new CategorieOperation("Maison", "#2F7FD8"));
        var octobre = compte.CreerMoisSuivant();
        octobre.Operations.Clear();
        octobre.Operations.AddRange(new[]
        {
            new Operation("Courses", debit: 50m),
            new Operation("Taxe foncière", debit: 300m) { CategorieOperation = "Maison" },
            new Operation("Crédit logement Agen", debit: 400m) { CategorieOperation = "Agen", Pointee = true },
            new Operation("Loyer Agen", credit: 550m) { CategorieOperation = "Agen" },
        });
        return compte;
    }

    [Fact]
    public void Enregistrement_GardeLesCategoriesEtLaCategorieDesOperations()
    {
        var depot = new DepotSqlite(Path.Combine(_dossier, "compte.db"));
        depot.Enregistrer(CompteAvecCategories());

        var relu = depot.Charger()!;

        Assert.Equal(new[] { new CategorieOperation("Agen", "#2E9E5B"), new CategorieOperation("Maison", "#2F7FD8") },
            relu.Configuration.CategoriesOperations);
        Assert.Equal(new string?[] { null, "Maison", "Agen", "Agen" }, relu.Mois[0].Operations.Select(o => o.CategorieOperation));
    }

    [Fact]
    public void RangeesParCategorie_SoldeLigneParLigneDansLOrdreAffiche()
    {
        var compte = CompteAvecCategories();
        var mois = new MoisViewModel(compte, compte.Mois[0], () => { });

        Assert.True(mois.ACategories);
        Assert.True(mois.RangerParCategorie);
        Assert.Equal(new[] { "Crédit logement Agen", "Loyer Agen", "Taxe foncière", "Courses" },
            mois.OperationsAffichees.Select(o => o.Libelle));

        var lignes = mois.OperationsAffichees;
        var avant = lignes[0].Solde + 400m;
        Assert.Equal(avant - 400m + 550m, lignes[1].Solde);
        Assert.Equal(mois.SoldeFin, lignes[^1].Solde);
        Assert.Equal(150m, lignes[0].TotalCategorie);
        Assert.Equal("#2E9E5B", lignes[0].CouleurCategorie);
        Assert.Equal("Sans catégorie", lignes[^1].GroupeCategorie);
        Assert.Equal("#7D8A93", lignes[^1].CouleurGroupe);

        // Ordre de saisie : le solde est celui du calcul du mois.
        mois.RangerParCategorie = false;
        Assert.Equal(new[] { "Courses", "Taxe foncière", "Crédit logement Agen", "Loyer Agen" },
            mois.OperationsAffichees.Select(o => o.Libelle));
        var calcul = compte.Calculer(compte.Mois[0].Periode).Lignes.Where(l => l.Type == Core.Calculs.TypeLigne.Operation).Select(l => l.Solde);
        Assert.Equal(calcul, mois.OperationsAffichees.Select(o => o.Solde));
    }

    [Fact]
    public void ChangerLaCategorie_RangeLOperationEtEnregistre()
    {
        var compte = CompteAvecCategories();
        var enregistrements = 0;
        var mois = new MoisViewModel(compte, compte.Mois[0], () => enregistrements++);
        var courses = mois.Operations.Elements.Single(o => o.Libelle == "Courses");

        courses.Categorie = "Maison";

        Assert.Equal("Maison", compte.Mois[0].Operations[0].CategorieOperation);
        Assert.Equal(1, enregistrements);
        Assert.Equal(new[] { "Crédit logement Agen", "Loyer Agen", "Courses", "Taxe foncière" },
            mois.OperationsAffichees.Select(o => o.Libelle));
        Assert.Equal(-350m, courses.TotalCategorie);

        courses.Categorie = "";
        Assert.Null(compte.Mois[0].Operations[0].CategorieOperation);
    }

    [Fact]
    public void SansCategorieConfiguree_PasDeColonne()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.CreerMoisSuivant();

        var mois = new MoisViewModel(compte, compte.Mois[0], () => { });

        Assert.False(mois.ACategories);
    }

    [Fact]
    public void Configuration_AjouterUneCategorieAvecSaCouleur()
    {
        var configuration = DonneesExcel.ConfigurationOctobre2026();
        var vm = new ConfigurationViewModel(configuration, premierMoisModifiable: true, () => { });

        vm.CategoriesOperations.AjouterCommand.Execute(null);
        vm.CategoriesOperations.Selection!.Nom = "Agen";
        vm.CategoriesOperations.Selection.Couleur = ConfigurationViewModel.Couleurs.Single(c => c.Nom == "Rouge");

        Assert.Equal(new[] { new CategorieOperation("Agen", "#D64545") }, configuration.CategoriesOperations);
    }
}
