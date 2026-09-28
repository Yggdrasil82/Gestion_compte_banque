using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GestionCompte.Core.Import;

/// <summary>Une opération lue dans un relevé bancaire.</summary>
/// <param name="Identifiant">Identifiant unique donné par la banque (FITID) : sert à éviter les doublons.</param>
/// <param name="Montant">Négatif pour un débit, positif pour un crédit.</param>
/// <param name="LibelleBanque">Libellé tel qu'écrit par la banque.</param>
public sealed record OperationBancaire(string Identifiant, DateOnly Date, decimal Montant, string LibelleBanque, string Type)
{
    /// <summary>Libellé nettoyé : espaces en trop et date d'achat carte (« 30/08/26 ») retirés.</summary>
    public string Libelle => ReleveOfx.NettoyerLibelle(LibelleBanque);
}

/// <param name="Solde">Solde du compte donné par la banque (LEDGERBAL), ou null s'il est absent.</param>
/// <param name="Compte">Numéro du compte bancaire (ACCTID), ou null s'il est absent.</param>
public sealed record ReleveBancaire(
    IReadOnlyList<OperationBancaire> Operations, decimal? Solde, DateOnly? DateSolde, string? Compte = null);

/// <summary>
/// Lecture des relevés au format OFX (« Money »), en version 1 (SGML, balises non fermées) comme en version 2 (XML).
/// </summary>
public static class ReleveOfx
{
    private static readonly Regex Balise = new(@"<(/?)([A-Za-z0-9.]+)>([^<]*)", RegexOptions.Compiled);
    private static readonly Regex DateCarte = new(@"\s+\d{2}/\d{2}(/\d{2,4})?\s*$", RegexOptions.Compiled);
    private static readonly Regex Espaces = new(@"\s+", RegexOptions.Compiled);

    public static ReleveBancaire Lire(string chemin)
    {
        var octets = File.ReadAllBytes(chemin);
        return LireTexte(Decoder(octets));
    }

    /// <summary>Décode le fichier selon l'en-tête CHARSET (1252 par défaut pour les banques françaises), ou UTF-8 pour l'OFX 2.</summary>
    public static string Decoder(byte[] octets)
    {
        var debut = Encoding.ASCII.GetString(octets, 0, Math.Min(octets.Length, 600));
        if (debut.Contains("encoding=\"UTF-8\"", StringComparison.OrdinalIgnoreCase) || debut.Contains("CHARSET:UTF-8", StringComparison.OrdinalIgnoreCase))
            return new UTF8Encoding(false).GetString(octets);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252).GetString(octets);
    }

    public static ReleveBancaire LireTexte(string texte)
    {
        if (!texte.Contains("<OFX>", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Ce fichier n'est pas un relevé au format OFX.");

        var operations = new List<OperationBancaire>();
        Dictionary<string, string>? courante = null;
        decimal? solde = null;
        DateOnly? dateSolde = null;
        string? compte = null;
        var dansSolde = false;

        foreach (Match m in Balise.Matches(texte))
        {
            var fermante = m.Groups[1].Value == "/";
            var nom = m.Groups[2].Value.ToUpperInvariant();
            var valeur = m.Groups[3].Value.Trim();

            switch (nom)
            {
                case "STMTTRN" when !fermante:
                    courante = new Dictionary<string, string>();
                    continue;
                case "STMTTRN":
                    if (courante is not null)
                        operations.Add(VersOperation(courante, operations.Count));
                    courante = null;
                    continue;
                case "LEDGERBAL":
                    dansSolde = !fermante;
                    continue;
            }

            if (fermante || valeur.Length == 0)
                continue;

            if (courante is not null)
                courante[nom] = valeur;
            else if (nom == "ACCTID")
                compte ??= valeur;
            else if (dansSolde && nom == "BALAMT")
                solde = LireMontant(valeur);
            else if (dansSolde && nom == "DTASOF")
                dateSolde = LireDate(valeur);
        }

        return new ReleveBancaire(operations, solde, dateSolde, compte);
    }

    private static OperationBancaire VersOperation(Dictionary<string, string> valeurs, int rang)
    {
        var montant = LireMontant(valeurs.GetValueOrDefault("TRNAMT") ?? throw new FormatException("Opération sans montant (TRNAMT)."));
        var date = LireDate(valeurs.GetValueOrDefault("DTPOSTED") ?? throw new FormatException("Opération sans date (DTPOSTED)."));
        var libelle = string.Join(" ", new[] { valeurs.GetValueOrDefault("NAME"), valeurs.GetValueOrDefault("MEMO") }
            .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct());
        // Sans FITID, un identifiant stable est construit à partir du contenu.
        var identifiant = valeurs.GetValueOrDefault("FITID")
            ?? $"{date:yyyyMMdd}|{montant.ToString(CultureInfo.InvariantCulture)}|{libelle}|{rang}";

        return new OperationBancaire(identifiant, date, montant, libelle, valeurs.GetValueOrDefault("TRNTYPE") ?? "");
    }

    public static decimal LireMontant(string texte)
    {
        var nettoye = texte.Trim().TrimStart('+').Replace(" ", "").Replace(',', '.');
        return decimal.Parse(nettoye, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    public static DateOnly LireDate(string texte)
    {
        if (texte.Length < 8)
            throw new FormatException($"Date invalide : {texte}");
        return DateOnly.ParseExact(texte[..8], "yyyyMMdd", CultureInfo.InvariantCulture);
    }

    public static string NettoyerLibelle(string libelle)
    {
        var texte = Espaces.Replace(libelle.Trim(), " ");
        texte = DateCarte.Replace(texte, "");
        return texte.Trim();
    }
}
