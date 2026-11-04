using GestionCompte.Core;
using GestionCompte.Core.Modeles;
using GestionCompte.Presentation;

namespace GestionCompte.Tests;

public sealed class DispositionMoisTests : IDisposable
{
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "GestionCompteTests", Guid.NewGuid().ToString("N"));

    private string Chemin => Path.Combine(_dossier, "preferences.json");

    public void Dispose()
    {
        if (Directory.Exists(_dossier))
            Directory.Delete(_dossier, recursive: true);
    }

    private static IEnumerable<string> Ids(IEnumerable<ModuleMois> modules) => modules.Select(m => m.Id);

    [Fact]
    public void ParDefaut_VueNormale_DispositionDOrigine()
    {
        var apparence = new ApparenceViewModel(Chemin);
        var d = apparence.DispositionMois;

        Assert.False(apparence.VueMoisEpuree);
        Assert.True(apparence.VueMoisNormale);
        Assert.Equal(new[] { "operations" }, Ids(d.Gauche));
        Assert.Equal(new[] { "revenus", "enveloppes", "detail", "cumuls" }, Ids(d.Droite));
        Assert.Empty(d.Masques);
        Assert.True(d.AfficherRevenus && d.AfficherBarres && d.AfficherEnveloppe && d.AfficherCompteCumul && d.AfficherVirement && d.AfficherSolde);
        Assert.False(d.DebitCreditSepares);
        Assert.Equal(FiltreOperations.APointer, d.Filtre);
    }

    [Fact]
    public void Deplacer_MasquerEtElements_RetenusEntreDeuxLancements()
    {
        var apparence = new ApparenceViewModel(Chemin);
        apparence.VueMoisEpuree = true;
        var d = apparence.DispositionMois;
        d.Deplacer("enveloppes", aGauche: true, position: 0);
        d.MasquerCommand.Execute(d.Droite.First(m => m.Id == "detail"));
        d.AfficherRevenus = false;
        d.AfficherBarres = false;
        d.DebitCreditSepares = true;
        d.Filtre = FiltreOperations.Toutes;

        var relue = new ApparenceViewModel(Chemin);
        var r = relue.DispositionMois;

        Assert.True(relue.VueMoisEpuree);
        Assert.Equal(new[] { "enveloppes", "operations" }, Ids(r.Gauche));
        Assert.Equal(new[] { "revenus", "cumuls" }, Ids(r.Droite));
        Assert.Equal(new[] { "detail" }, Ids(r.Masques));
        Assert.False(r.AfficherRevenus);
        Assert.False(r.AfficherBarres);
        Assert.True(r.AfficherDepenses);
        Assert.True(r.DebitCreditSepares);
        Assert.Equal(FiltreOperations.Toutes, r.Filtre);
    }

    [Fact]
    public void Deplacer_DansLaMemeColonne_VersLeBas()
    {
        var d = new DispositionMoisViewModel(new DispositionMois(), () => { });

        // « revenus » lâché sous « detail » (position 3 dans la liste d'avant le déplacement).
        d.Deplacer("revenus", aGauche: false, position: 3);

        Assert.Equal(new[] { "enveloppes", "detail", "revenus", "cumuls" }, Ids(d.Droite));
    }

    [Fact]
    public void Remettre_EtRevenirALOrigine()
    {
        var d = new DispositionMoisViewModel(new DispositionMois(), () => { });
        var operations = d.Gauche[0];
        d.MasquerCommand.Execute(operations);
        Assert.Empty(d.Gauche);
        Assert.True(d.AMasques);

        d.RemettreCommand.Execute(operations);
        Assert.Equal(new[] { "operations" }, Ids(d.Gauche));
        Assert.False(d.AMasques);

        d.Deplacer("cumuls", aGauche: true, position: 0);
        d.AfficherSolde = false;
        d.RevenirOrigineCommand.Execute(null);
        Assert.Equal(new[] { "operations" }, Ids(d.Gauche));
        Assert.Equal(new[] { "revenus", "enveloppes", "detail", "cumuls" }, Ids(d.Droite));
        Assert.True(d.AfficherSolde);
    }

    [Theory]
    [InlineData(FiltreOperations.APointer, false, true)]
    [InlineData(FiltreOperations.APointer, true, false)]
    [InlineData(FiltreOperations.Pointees, true, true)]
    [InlineData(FiltreOperations.Pointees, false, false)]
    [InlineData(FiltreOperations.Toutes, true, true)]
    public void Filtre_OperationsMontrees(FiltreOperations filtre, bool pointee, bool montree)
    {
        var d = new DispositionMoisViewModel(new DispositionMois { Filtre = filtre }, () => { });

        Assert.Equal(montree, d.Montre(pointee));
    }

    [Fact]
    public void ModuleInconnuOuDouble_Ignore()
    {
        var d = new DispositionMoisViewModel(new DispositionMois { Gauche = { "inconnu", "revenus" }, Droite = { "revenus" } }, () => { });

        Assert.Equal(new[] { "revenus", "operations" }, Ids(d.Gauche));
        Assert.Equal(new[] { "enveloppes", "detail", "cumuls" }, Ids(d.Droite));
    }

    [Fact]
    public void MontantSigne_DebitNegatifCreditPositif()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.CreerMoisSuivant();
        var mois = new MoisViewModel(compte, compte.Mois[0], () => { });
        var operation = mois.Operations.Elements[0];

        operation.Montant = -45.5m;
        Assert.Equal(45.5m, operation.Debit);
        Assert.Equal(0m, operation.Credit);
        Assert.False(operation.EstCredit);

        operation.Montant = 12m;
        Assert.Equal(0m, operation.Debit);
        Assert.Equal(12m, operation.Credit);
        Assert.True(operation.EstCredit);
    }

    [Fact]
    public void VueChangee_LEcranDuMoisEstRefait()
    {
        var apparence = new ApparenceViewModel(Chemin);
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.CreerMoisSuivant();
        var mois = new MoisViewModel(compte, compte.Mois[0], () => { }, apparence);
        Assert.False(mois.Epuree);

        apparence.VueMoisEpuree = true;

        Assert.True(new MoisViewModel(compte, compte.Mois[0], () => { }, apparence).Epuree);
        Assert.Same(apparence.DispositionMois, mois.Disposition);
    }
}
