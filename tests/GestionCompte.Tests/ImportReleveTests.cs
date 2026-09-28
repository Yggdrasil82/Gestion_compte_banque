using System.Text;
using GestionCompte.Core;
using GestionCompte.Core.Import;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Tests;

public class ImportReleveTests
{
    /// <summary>Relevé OFX 1 (SGML) au format de la banque de l'utilisateur, avec des valeurs inventées.</summary>
    internal static string Ofx(params string[] operations) => $"""
        Content-Type: application/x-ofx
        OFXHEADER:100
        DATA:OFXSGML
        VERSION:102
        SECURITY:NONE
        ENCODING:USASCII
        CHARSET:1252
        COMPRESSION:NONE
        OLDFILEUID:NONE
        NEWFILEUID:NONE
        <OFX>
        <SIGNONMSGSRSV1>
        <SONRS>
        <STATUS>
        <CODE>0
        <SEVERITY>INFO
        </STATUS>
        <DTSERVER>20261028192928
        <LANGUAGE>FRA
        </SONRS>
        </SIGNONMSGSRSV1>
        <BANKMSGSRSV1>
        <STMTTRNRS>
        <TRNUID>00
        <STMTRS>
        <CURDEF>EUR
        <BANKACCTFROM>
        <BANKID>11111
        <BRANCHID>22222
        <ACCTID>33333A
        <ACCTTYPE>CHECKING
        </BANKACCTFROM>
        <BANKTRANLIST>
        <DTSTART>20261001120000
        <DTEND>20261027120000
        {string.Join("\n", operations)}
        </BANKTRANLIST>
        <LEDGERBAL>
        <BALAMT>-483.45
        <DTASOF>20261027000000
        </LEDGERBAL>
        <AVAILBAL>
        <BALAMT>-483.45
        <DTASOF>20261027000000
        </AVAILBAL>
        </STMTRS>
        </STMTTRNRS>
        </BANKMSGSRSV1>
        </OFX>
        """;

    internal static string Ligne(string type, string date, string montant, string id, string libelle) => $"""
        <STMTTRN>
        <TRNTYPE>{type}
        <DTPOSTED>{date}
        <TRNAMT>{montant}
        <FITID>{id}
        <NAME>{libelle}
        </STMTTRN>
        """;

    // ---- Lecture OFX ----

    [Fact]
    public void Lire_OfxSgml_OperationsEtSolde()
    {
        var releve = ReleveOfx.LireTexte(Ofx(
            Ligne("DEBIT", "20261001", "-41.42", "A1", "CB  COFIDIS AMAZON   30/09/26"),
            Ligne("CREDIT", "20261001", "+12.50", "A2", "VIREMENT CPMS"),
            Ligne("XFER", "20261007", "-17.99", "A3", "PRLV SEPA FREE MOBILE")));

        Assert.Equal(3, releve.Operations.Count);
        var cb = releve.Operations[0];
        Assert.Equal("A1", cb.Identifiant);
        Assert.Equal(new DateOnly(2026, 10, 1), cb.Date);
        Assert.Equal(-41.42m, cb.Montant);
        Assert.Equal("CB COFIDIS AMAZON", cb.Libelle);
        Assert.Equal(12.50m, releve.Operations[1].Montant);
        Assert.Equal("XFER", releve.Operations[2].Type);
        Assert.Equal(-483.45m, releve.Solde);
        Assert.Equal(new DateOnly(2026, 10, 27), releve.DateSolde);
    }

    [Fact]
    public void Lire_OfxXml_AvecBalisesFermantes()
    {
        var releve = ReleveOfx.LireTexte("""
            <?xml version="1.0" encoding="UTF-8"?>
            <OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><BANKTRANLIST>
            <STMTTRN><TRNTYPE>DEBIT</TRNTYPE><DTPOSTED>20261003120000[+1:CET]</DTPOSTED><TRNAMT>-8,50</TRNAMT>
            <FITID>X9</FITID><NAME>BOULANGERIE</NAME><MEMO>PAIEMENT CARTE</MEMO></STMTTRN>
            </BANKTRANLIST><LEDGERBAL><BALAMT>100.00</BALAMT><DTASOF>20261003</DTASOF></LEDGERBAL></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>
            """);

        var operation = Assert.Single(releve.Operations);
        Assert.Equal(-8.50m, operation.Montant);
        Assert.Equal(new DateOnly(2026, 10, 3), operation.Date);
        Assert.Equal("BOULANGERIE PAIEMENT CARTE", operation.Libelle);
        Assert.Equal(100m, releve.Solde);
    }

    [Fact]
    public void Lire_SansFitid_IdentifiantStable()
    {
        var texte = Ofx("<STMTTRN>\n<TRNTYPE>DEBIT\n<DTPOSTED>20261005\n<TRNAMT>-3.00\n<NAME>CAFE\n</STMTTRN>");

        Assert.Equal(ReleveOfx.LireTexte(texte).Operations[0].Identifiant, ReleveOfx.LireTexte(texte).Operations[0].Identifiant);
    }

    [Fact]
    public void Lire_FichierWindows1252_AccentsConserves()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var octets = Encoding.GetEncoding(1252).GetBytes(Ofx(Ligne("DEBIT", "20261005", "-12.00", "E1", "CB PÂTISSERIE DÉLICES")));

        Assert.Equal("CB PÂTISSERIE DÉLICES", ReleveOfx.LireTexte(ReleveOfx.Decoder(octets)).Operations[0].Libelle);
    }

    [Fact]
    public void Lire_PasUnOfx_EstRefuse()
    {
        Assert.Throws<FormatException>(() => ReleveOfx.LireTexte("Date;Libellé;Montant"));
    }

    [Theory]
    [InlineData("CB  COFIDIS AMAZON   30/08/26", "CB COFIDIS AMAZON")]
    [InlineData("CB CARREFOUR 14/10", "CB CARREFOUR")]
    [InlineData("PRLV SEPA FREE MOBILE", "PRLV SEPA FREE MOBILE")]
    public void NettoyerLibelle(string brut, string attendu)
    {
        Assert.Equal(attendu, ReleveOfx.NettoyerLibelle(brut));
    }

    // ---- Rapprochement ----

    private static CompteBancaire CompteOctobre()
    {
        var configuration = DonneesExcel.ConfigurationOctobre2026();
        configuration.Regles.AddRange(RegleClassement.ParDefaut);
        var compte = new CompteBancaire(configuration);
        compte.CreerMoisSuivant();
        return compte;
    }

    private static ReleveBancaire Releve(params string[] lignes) => ReleveOfx.LireTexte(Ofx(lignes));

    [Fact]
    public void Preparer_MemeMontantEtLibelleProche_Rapprochee()
    {
        var plan = ImportReleve.Preparer(CompteOctobre(), Releve(
            Ligne("XFER", "20261005", "-801.00", "L1", "PRLV SEPA LOYER SCI DES PINS"),
            Ligne("XFER", "20261008", "-18.99", "L2", "PRLV SEPA SPOTIFY")));

        Assert.All(plan.Lignes, l => Assert.Equal(StatutImport.Rapprochee, l.Statut));
        Assert.Equal("Loyer", plan.Lignes[0].Choix!.Libelle);
        Assert.Equal("Spotify", plan.Lignes[1].Choix!.Libelle);
        Assert.Empty(plan.MoisACreer);
    }

    [Fact]
    public void Preparer_MontantDifferent_MontantAjuste()
    {
        var plan = ImportReleve.Preparer(CompteOctobre(), Releve(Ligne("XFER", "20261012", "-152.30", "E1", "PRLV SEPA EDF ELECTRICITE")));

        var ligne = Assert.Single(plan.Lignes);
        Assert.Equal(StatutImport.MontantAjuste, ligne.Statut);
        Assert.Equal("Électricité", ligne.Choix!.Libelle);
    }

    [Fact]
    public void Preparer_SalaireRecu_RevenuRecu()
    {
        var plan = ImportReleve.Preparer(CompteOctobre(), Releve(Ligne("CREDIT", "20261028", "+2650.40", "S1", "VIR SALAIRE ROMAIN")));

        var ligne = Assert.Single(plan.Lignes);
        Assert.Equal(StatutImport.RevenuRecu, ligne.Statut);
        Assert.Equal("Salaire Romain", ligne.Choix!.Libelle);
    }

    [Fact]
    public void Preparer_PaiementCarte_NouvelleAvecEnveloppe()
    {
        var plan = ImportReleve.Preparer(CompteOctobre(), Releve(
            Ligne("DEBIT", "20261010", "-64.20", "C1", "CB CARREFOUR MARKET 09/10/26"),
            Ligne("DEBIT", "20261011", "-45.00", "C2", "CB STATION TOTAL 10/10/26"),
            Ligne("DEBIT", "20261012", "-41.42", "C3", "CB COFIDIS AMAZON 11/10/26")));

        Assert.All(plan.Lignes, l => Assert.Equal(StatutImport.Nouvelle, l.Statut));
        Assert.Equal(new[] { "Courses", "Carburant", null }, plan.Lignes.Select(l => l.Enveloppe));
    }

    [Fact]
    public void Preparer_PrelevementMontantUnique_RapprocheMemeSansLibelle()
    {
        var plan = ImportReleve.Preparer(CompteOctobre(), Releve(Ligne("XFER", "20261006", "-45.00", "V1", "PRLV SEPA VEOLIA")));

        Assert.Equal(StatutImport.Rapprochee, plan.Lignes[0].Statut);
        Assert.Equal("Eau", plan.Lignes[0].Choix!.Libelle);
    }

    [Fact]
    public void Preparer_DeuxMontantsIdentiquesSansIndice_Nouvelle()
    {
        // Deux forfaits à 9,99 € : sans libellé proche, on ne devine pas lequel.
        var plan = ImportReleve.Preparer(CompteOctobre(), Releve(Ligne("XFER", "20261005", "-9.99", "M1", "PRLV SEPA OPERATEUR")));

        Assert.Equal(StatutImport.Nouvelle, plan.Lignes[0].Statut);
        Assert.Contains(plan.Lignes[0].Candidats, c => c.Libelle == "Mobile Killian");
    }

    [Fact]
    public void Appliquer_PointeAjusteAjouteEtMarqueLesRevenus()
    {
        var compte = CompteOctobre();
        var plan = ImportReleve.Preparer(compte, Releve(
            Ligne("XFER", "20261005", "-801.00", "L1", "PRLV SEPA LOYER"),
            Ligne("XFER", "20261012", "-152.30", "E1", "PRLV SEPA EDF ELECTRICITE"),
            Ligne("CREDIT", "20261028", "+2650.40", "S1", "VIR SALAIRE ROMAIN"),
            Ligne("DEBIT", "20261010", "-64.20", "C1", "CB CARREFOUR MARKET 09/10/26")));

        var resultat = ImportReleve.Appliquer(compte, plan);

        Assert.Equal((1, 1, 1, 1), (resultat.Rapprochees, resultat.Ajustees, resultat.RevenusRecus, resultat.Nouvelles));
        var octobre = compte.Mois[0];
        var loyer = octobre.Operations.Single(o => o.Libelle == "Loyer");
        Assert.True(loyer.Pointee);
        Assert.Equal("L1", loyer.IdentifiantBanque);
        Assert.Equal(152.30m, octobre.Operations.Single(o => o.Libelle == "Électricité").Debit);
        var salaire = octobre.Revenus.Single(r => r.Nom == "Salaire Romain");
        Assert.True(salaire.Recu);
        Assert.Equal(2650.40m, salaire.Montant);
        var carrefour = octobre.Operations.Single(o => o.IdentifiantBanque == "C1");
        Assert.Equal("CB CARREFOUR MARKET", carrefour.Libelle);
        Assert.Equal(("Courses", 64.20m, true), (carrefour.Enveloppe, carrefour.Debit, carrefour.Pointee));
    }

    [Fact]
    public void Appliquer_UneDeuxiemeFois_AucunDoublon()
    {
        var compte = CompteOctobre();
        var releve = Releve(
            Ligne("XFER", "20261005", "-801.00", "L1", "PRLV SEPA LOYER"),
            Ligne("DEBIT", "20261010", "-64.20", "C1", "CB CARREFOUR MARKET"));
        ImportReleve.Appliquer(compte, ImportReleve.Preparer(compte, releve));
        var nombre = compte.Mois[0].Operations.Count;

        var second = ImportReleve.Preparer(compte, releve);
        var resultat = ImportReleve.Appliquer(compte, second);

        Assert.All(second.Lignes, l => Assert.Equal(StatutImport.DejaImportee, l.Statut));
        Assert.Equal(0, resultat.Total);
        Assert.Equal(nombre, compte.Mois[0].Operations.Count);
    }

    [Fact]
    public void Appliquer_ChoixUtilisateur_Respecte()
    {
        var compte = CompteOctobre();
        var plan = ImportReleve.Preparer(compte, Releve(
            Ligne("XFER", "20261005", "-9.99", "M1", "PRLV SEPA OPERATEUR"),
            Ligne("DEBIT", "20261010", "-20.00", "C1", "CB MAGASIN"),
            Ligne("DEBIT", "20261011", "-5.00", "C2", "CB CAFE")));
        plan.Lignes[0].Choix = plan.Lignes[0].Candidats.Single(c => c.Libelle == "Mobile");
        plan.Lignes[1].Enveloppe = "Courses";
        plan.Lignes[2].Importer = false;

        var resultat = ImportReleve.Appliquer(compte, plan);

        var octobre = compte.Mois[0];
        Assert.True(octobre.Operations.Single(o => o.Libelle == "Mobile").Pointee);
        Assert.False(octobre.Operations.Single(o => o.Libelle == "Mobile Killian").Pointee);
        Assert.Equal("Courses", octobre.Operations.Single(o => o.IdentifiantBanque == "C1").Enveloppe);
        Assert.DoesNotContain(octobre.Operations, o => o.IdentifiantBanque == "C2");
        Assert.Equal(1, resultat.Ignorees);
    }

    [Fact]
    public void MoisPasEncoreCree_ProposeEtCreeLesMois()
    {
        var compte = CompteOctobre();
        var plan = ImportReleve.Preparer(compte, Releve(
            Ligne("XFER", "20261205", "-801.00", "L3", "PRLV SEPA LOYER"),
            Ligne("XFER", "20261105", "-801.00", "L2", "PRLV SEPA LOYER")));

        Assert.Equal(new[] { new PeriodeMois(2026, 11), new PeriodeMois(2026, 12) }, plan.MoisACreer);
        Assert.All(plan.Lignes, l => Assert.Equal(StatutImport.Rapprochee, l.Statut));
        Assert.Single(compte.Mois);

        var resultat = ImportReleve.Appliquer(compte, plan);

        Assert.Equal(3, compte.Mois.Count);
        Assert.Equal(plan.MoisACreer, resultat.MoisCrees);
        Assert.True(compte.Mois[2].Operations.Single(o => o.Libelle == "Loyer").Pointee);
    }

    [Fact]
    public void OperationAvantLePremierMois_Ignoree()
    {
        var plan = ImportReleve.Preparer(CompteOctobre(), Releve(Ligne("DEBIT", "20260915", "-10.00", "V1", "CB AVANT")));

        Assert.Equal(StatutImport.AvantDebut, plan.Lignes[0].Statut);
        Assert.False(plan.Lignes[0].Importer);
        Assert.Empty(plan.MoisACreer);
    }

    [Fact]
    public void SoldePointe_SoldeInitialPlusRevenusRecusEtOperationsPointees()
    {
        var compte = CompteOctobre();
        compte.Configuration.SoldeInitial = 100m;
        ImportReleve.Appliquer(compte, ImportReleve.Preparer(compte, Releve(
            Ligne("XFER", "20261005", "-801.00", "L1", "PRLV SEPA LOYER"),
            Ligne("CREDIT", "20261028", "+2650.40", "S1", "VIR SALAIRE ROMAIN"))));

        Assert.Equal(100m - 801m + 2650.40m, ImportReleve.SoldePointe(compte));
    }

    [Fact]
    public void SoldePointe_JusquALaDateDuReleve_IgnoreLesMoisSuivants()
    {
        var compte = CompteOctobre();
        compte.Configuration.SoldeInitial = 100m;
        compte.Mois[0].Revenus[0].Recu = true;
        var novembre = compte.CreerMoisSuivant();
        novembre.Revenus[0].Recu = true;
        novembre.Operations[0].Pointee = true;

        var octobre = 100m + compte.Mois[0].Revenus[0].Montant;
        Assert.Equal(octobre, ImportReleve.SoldePointe(compte, new DateOnly(2026, 10, 27)));
        // Relevé antérieur au premier mois : seul le solde de départ compte.
        Assert.Equal(100m, ImportReleve.SoldePointe(compte, new DateOnly(2026, 9, 27)));
        Assert.True(ImportReleve.SoldePointe(compte) != octobre);
    }

    [Theory]
    [InlineData("PRLV SEPA FREE MOBILE", "Mobile Killian", 0.5)]
    [InlineData("PRLV SEPA LOYER", "Loyer", 1.0)]
    [InlineData("CB CARREFOUR", "Loyer", 0.0)]
    [InlineData("PRLV SEPA EDF ELECTRICITE", "Électricité :", 1.0)]
    public void Ressemblance_MotsCommunsSansAccents(string a, string b, double attendu)
    {
        Assert.Equal(attendu, ImportReleve.Ressemblance(a, b), 3);
    }

    [Fact]
    public void Regles_MotEntierSeulement()
    {
        var enveloppes = new[] { "Carburant" };
        var regles = RegleClassement.ParDefaut;

        Assert.Equal("Carburant", ImportReleve.EnveloppePour(regles, "CB BP STATION", enveloppes));
        Assert.Null(ImportReleve.EnveloppePour(regles, "CB BPCE ASSURANCE", enveloppes));
        Assert.Null(ImportReleve.EnveloppePour(regles, "CB LECLERC", enveloppes));
        Assert.Equal("CARREFOUR", ImportReleve.MotCleSuggere("CB CARREFOUR MARKET"));
    }
}
