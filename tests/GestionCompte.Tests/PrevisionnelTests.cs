using GestionCompte.Core;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Tests;

public class PrevisionnelTests
{
    // Avec la configuration d'octobre 2026, chaque mois simulé ajoute 622,18 € au solde.
    private const decimal GainMensuel = 622.18m;

    [Fact]
    public void SansMois_SimuleDepuisLePremierMoisEtLeSoldeInitial()
    {
        var configuration = DonneesExcel.ConfigurationOctobre2026();
        configuration.SoldeInitial = 100m;
        var compte = new CompteBancaire(configuration);

        var prevision = Previsionnel.Calculer(compte, moisReels: 6, moisPrevus: 12);

        Assert.Equal(12, prevision.Mois.Count);
        Assert.All(prevision.Mois, m => Assert.False(m.Reel));
        Assert.Equal(new PeriodeMois(2026, 10), prevision.Mois[0].Periode);
        Assert.Equal(new PeriodeMois(2027, 9), prevision.Mois[^1].Periode);
        Assert.Equal(100m + GainMensuel, prevision.Mois[0].SoldeFin);
        Assert.Equal(100m + 12 * GainMensuel, prevision.Mois[^1].SoldeFin);
    }

    [Fact]
    public void MoisReels_SuivisDesMoisSimules()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        var octobre = compte.CreerMoisSuivant();
        octobre.Operations.Add(new Operation("Réparation", debit: 300m));

        var prevision = Previsionnel.Calculer(compte, moisReels: 6, moisPrevus: 3);

        Assert.Equal(4, prevision.Mois.Count);
        Assert.True(prevision.Mois[0].Reel);
        Assert.Equal(GainMensuel - 300m, prevision.Mois[0].SoldeFin);
        Assert.False(prevision.Mois[1].Reel);
        Assert.Equal(new PeriodeMois(2026, 11), prevision.Mois[1].Periode);
        Assert.Equal(prevision.Mois[0].SoldeFin, prevision.Mois[1].AncienSolde);
        Assert.Equal(GainMensuel - 300m + 3 * GainMensuel, prevision.Mois[^1].SoldeFin);
    }

    [Fact]
    public void MoisReels_SeulsLesDerniersSontGardes()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        for (var i = 0; i < 8; i++)
            compte.CreerMoisSuivant();

        var prevision = Previsionnel.Calculer(compte, moisReels: 6, moisPrevus: 0);

        Assert.Equal(6, prevision.Mois.Count);
        Assert.Equal(new PeriodeMois(2026, 12), prevision.Mois[0].Periode);
        Assert.Equal(2 * GainMensuel, prevision.Mois[0].AncienSolde);
    }

    [Fact]
    public void EntreesEtSorties_ExpliquentLeSolde()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());

        var mois = Previsionnel.Calculer(compte, 0, 1).Mois[0];

        Assert.Equal(3200m, mois.Entrees);
        Assert.Equal(mois.AncienSolde + mois.Entrees - mois.Sorties, mois.SoldeFin);
    }

    [Fact]
    public void EnveloppeDejaDepassee_NeFaussePasLeMoisReel()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        var octobre = compte.CreerMoisSuivant();
        octobre.Operations.Add(new Operation("Plein", debit: 150m) { Enveloppe = "Carburant" });

        var mois = Previsionnel.Calculer(compte, 1, 0).Mois[0];

        Assert.Equal(compte.Calculer(octobre.Periode).SoldeFinPrevisionnel, mois.SoldeFin);
        Assert.Equal(mois.AncienSolde + mois.Entrees - mois.Sorties, mois.SoldeFin);
    }

    [Fact]
    public void OperationsPrevues_ComptentDansLeMoisSimule()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.OperationsPrevues.Add(new OperationPrevue(new PeriodeMois(2026, 12), "Cadeaux de Noël", debit: 400m));
        compte.OperationsPrevues.Add(new OperationPrevue(new PeriodeMois(2026, 12), "Prime", credit: 500m));

        var mois = Previsionnel.Calculer(compte, 0, 3).Mois;

        Assert.Equal(2 * GainMensuel, mois[1].SoldeFin);
        Assert.Equal(3 * GainMensuel + 100m, mois[2].SoldeFin);
    }

    [Fact]
    public void CreerMois_AjouteLesOperationsPrevuesDuMoisEtLesRetireDeLaListe()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.OperationsPrevues.Add(new OperationPrevue(new PeriodeMois(2026, 10), "Taxe foncière", debit: 900m));
        compte.OperationsPrevues.Add(new OperationPrevue(new PeriodeMois(2027, 7), "Vacances", debit: 1200m) { CompteCumul = null });

        var octobre = compte.CreerMoisSuivant();

        Assert.Contains(octobre.Operations, o => o.Libelle == "Taxe foncière" && o.Debit == 900m);
        Assert.Single(compte.OperationsPrevues);
        Assert.Equal("Vacances", compte.OperationsPrevues[0].Libelle);
    }

    [Fact]
    public void PremierMoisNegatifEtPointBas()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.OperationsPrevues.Add(new OperationPrevue(new PeriodeMois(2027, 1), "Voiture", debit: 5000m));

        var prevision = Previsionnel.Calculer(compte, 0, 12);

        Assert.Equal(new PeriodeMois(2027, 1), prevision.PremierMoisNegatif!.Periode);
        Assert.Equal(new PeriodeMois(2027, 1), prevision.PointBas!.Periode);
        Assert.Equal(4 * GainMensuel - 5000m, prevision.PointBas.SoldeFin);
    }

    [Fact]
    public void SansDecouvert_PasDeMoisNegatif()
    {
        var prevision = Previsionnel.Calculer(new CompteBancaire(DonneesExcel.ConfigurationOctobre2026()), 0, 12);

        Assert.Null(prevision.PremierMoisNegatif);
    }

    [Fact]
    public void Echeance_DateDeFinDuRemboursement()
    {
        // 100 € par mois à partir d'octobre 2026 : 1 000 € atteints au 10e mois, juillet 2027.
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026(objectifRembFamille: 1000m));
        compte.CreerMoisSuivant();

        var echeance = Previsionnel.Calculer(compte, 6, 12).Echeances.Single();

        Assert.Equal(DonneesExcel.RembFamille, echeance.Nom);
        Assert.Equal(new PeriodeMois(2027, 7), echeance.AtteintEn);
        Assert.False(echeance.DejaAtteint);
    }

    [Fact]
    public void Echeance_AuDelaDeLHorizon_EstQuandMemeCalculee()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026(objectifRembFamille: 3000m));

        var echeance = Previsionnel.Calculer(compte, 0, 6).Echeances.Single();

        Assert.Equal(new PeriodeMois(2029, 3), echeance.AtteintEn);
    }

    [Fact]
    public void Echeance_DejaAtteinteDansLesMoisReels()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026(objectifRembFamille: 150m));
        compte.CreerMoisSuivant();
        compte.CreerMoisSuivant();

        var echeance = Previsionnel.Calculer(compte, 6, 12).Echeances.Single();

        Assert.True(echeance.DejaAtteint);
        Assert.Null(echeance.AtteintEn);
    }

    [Fact]
    public void Echeance_JamaisAtteinte()
    {
        var configuration = DonneesExcel.ConfigurationOctobre2026();
        configuration.ComptesCumul.Add(new CompteCumul("Voyage", Objectif: 5000m));

        var echeance = Previsionnel.Calculer(new CompteBancaire(configuration), 0, 12).Echeances.Single(e => e.Nom == "Voyage");

        Assert.Null(echeance.AtteintEn);
        Assert.False(echeance.DejaAtteint);
    }

    [Fact]
    public void Cumuls_SuiventLesMoisSimules()
    {
        var compte = new CompteBancaire(DonneesExcel.ConfigurationOctobre2026());
        compte.CreerMoisSuivant();

        var mois = Previsionnel.Calculer(compte, 6, 3).Mois;

        Assert.Equal(new[] { 100m, 200m, 300m, 400m }, mois.Select(m => m.Cumuls[DonneesExcel.RembFamille]));
    }

    [Fact]
    public void ComptesCumulsHomonymes_NeFontPasPlanter()
    {
        var configuration = DonneesExcel.ConfigurationOctobre2026();
        configuration.ComptesCumul.Add(new CompteCumul("Nouveau compte"));
        configuration.ComptesCumul.Add(new CompteCumul("Nouveau compte"));

        Assert.Equal(12, Previsionnel.Calculer(new CompteBancaire(configuration), 0, 12).Mois.Count);
    }
}
