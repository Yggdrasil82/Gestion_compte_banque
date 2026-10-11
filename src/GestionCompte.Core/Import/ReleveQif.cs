using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GestionCompte.Core.Import;

/// <summary>
/// Lecture des relevés au format QIF (« Quicken », proposé par Fortuneo et d'autres banques) : une ligne par champ
/// (D date, T montant, P bénéficiaire, M note), chaque opération terminée par « ^ ». Le QIF ne donne ni solde, ni
/// numéro de compte, ni identifiant d'opération.
/// </summary>
public static class ReleveQif
{
    private static readonly Regex Date = new(@"^\s*(\d{1,2})[/.\-](\d{1,2})[/.\-'](\d{2}|\d{4})\s*$", RegexOptions.Compiled);

    public static ReleveBancaire Lire(string chemin) => LireTexte(Decoder(File.ReadAllBytes(chemin)));

    /// <summary>UTF-8 quand le fichier en est (Fortuneo), sinon Windows-1252 (autres banques françaises).</summary>
    public static string Decoder(byte[] octets)
    {
        try
        {
            var texte = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(octets);
            return texte.TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(octets);
        }
    }

    public static ReleveBancaire LireTexte(string texte)
    {
        var lignes = texte.Replace("\r", "").Split('\n');
        if (!lignes.Any(l => l.StartsWith("!Type:", StringComparison.OrdinalIgnoreCase)))
            throw new FormatException("Ce fichier n'est pas un relevé au format QIF.");

        // Opérations brutes d'abord : le format des dates (jour/mois ou mois/jour) se déduit de l'ensemble du fichier.
        var brutes = new List<Dictionary<char, string>>();
        var courante = new Dictionary<char, string>();
        var dansCompte = false;
        foreach (var ligne in lignes.Select(l => l.TrimEnd()))
        {
            if (ligne.Length == 0)
                continue;
            if (ligne.StartsWith('!'))
            {
                // « !Account » ouvre la description du compte (jusqu'au « ^ ») ; « !Type: » ouvre la liste des opérations.
                dansCompte = ligne.StartsWith("!Account", StringComparison.OrdinalIgnoreCase);
                courante.Clear();
                continue;
            }
            if (ligne[0] == '^')
            {
                if (!dansCompte && courante.Count > 0)
                    brutes.Add(courante);
                courante = new Dictionary<char, string>();
                dansCompte = false;
                continue;
            }
            if (!dansCompte && !courante.ContainsKey(ligne[0]))
                courante[ligne[0]] = ligne[1..].Trim();
        }
        if (!dansCompte && courante.ContainsKey('D') && courante.ContainsKey('T'))
            brutes.Add(courante);

        var moisDAbord = MoisDAbord(brutes.Select(b => b.GetValueOrDefault('D')).OfType<string>());
        var occurrences = new Dictionary<string, int>();
        var operations = new List<OperationBancaire>();
        foreach (var brute in brutes)
        {
            var date = LireDate(brute.GetValueOrDefault('D') ?? throw new FormatException("Opération sans date (ligne D)."), moisDAbord);
            var montant = LireMontant(brute.GetValueOrDefault('T') ?? brute.GetValueOrDefault('U')
                                      ?? throw new FormatException("Opération sans montant (ligne T)."));
            var libelle = string.Join(" ", new[] { brute.GetValueOrDefault('P'), brute.GetValueOrDefault('M') }
                .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct());

            // Sans identifiant de la banque : date, montant et libellé, numérotés quand le même jour en compte plusieurs
            // (l'identifiant reste le même d'un export à l'autre, les doublons sont donc reconnus).
            var cle = $"{date:yyyyMMdd}|{montant.ToString(CultureInfo.InvariantCulture)}|{libelle}";
            occurrences[cle] = occurrences.GetValueOrDefault(cle) + 1;
            operations.Add(new OperationBancaire($"QIF|{cle}|{occurrences[cle]}", date, montant, libelle,
                montant < 0 ? "DEBIT" : "CREDIT"));
        }

        return new ReleveBancaire(operations, null, null);
    }

    /// <summary>Dates au format mois/jour (banques américaines) seulement si le fichier le prouve ; jour/mois sinon.</summary>
    private static bool MoisDAbord(IEnumerable<string> dates)
    {
        var jourDAbord = false;
        var moisDAbord = false;
        foreach (var texte in dates)
        {
            var m = Date.Match(texte);
            if (!m.Success)
                continue;
            jourDAbord |= int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) > 12;
            moisDAbord |= int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) > 12;
        }
        return moisDAbord && !jourDAbord;
    }

    private static DateOnly LireDate(string texte, bool moisDAbord)
    {
        var m = Date.Match(texte);
        if (!m.Success)
            throw new FormatException($"Date invalide : {texte}");
        var premier = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var second = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        var annee = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        if (annee < 100)
            annee += 2000;
        try
        {
            return moisDAbord ? new DateOnly(annee, premier, second) : new DateOnly(annee, second, premier);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new FormatException($"Date invalide : {texte}");
        }
    }

    /// <summary>« -1,234.56 », « -1 234,56 » ou « -27.28 » : le dernier séparateur est celui des décimales.</summary>
    public static decimal LireMontant(string texte)
    {
        var nettoye = texte.Trim().TrimStart('+').Replace(" ", "").Replace(" ", "");
        var virgule = nettoye.LastIndexOf(',');
        var point = nettoye.LastIndexOf('.');
        nettoye = virgule > point
            ? nettoye.Replace(".", "").Replace(',', '.')
            : nettoye.Replace(",", "");
        return decimal.Parse(nettoye, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }
}

/// <summary>Relevé bancaire OFX ou QIF, reconnu à son contenu.</summary>
public static class Releves
{
    public static ReleveBancaire Lire(string chemin)
    {
        var octets = File.ReadAllBytes(chemin);
        var debut = Encoding.ASCII.GetString(octets, 0, Math.Min(octets.Length, 600));
        return debut.Contains("!Type:", StringComparison.OrdinalIgnoreCase) || debut.Contains("!Account", StringComparison.OrdinalIgnoreCase)
            ? ReleveQif.LireTexte(ReleveQif.Decoder(octets))
            : ReleveOfx.LireTexte(ReleveOfx.Decoder(octets));
    }
}
