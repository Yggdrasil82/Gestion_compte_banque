using System.Globalization;
using System.Text;

namespace GestionCompte.Core.Bourse;

/// <summary>Résultat de la lecture d'un export : opérations lues et lignes ignorées (illisibles).</summary>
public sealed record LectureReleveBourse(IReadOnlyList<OperationBourse> Operations, int Ignorees);

/// <summary>
/// Lecture de l'export CSV des transactions de Trade Republic (application mobile › Profil › Relevés › Export des transactions).
/// Colonnes utilisées : datetime, account_type (DEFAULT = compte-titres, PEA), category, type, asset_class, name, symbol (ISIN),
/// shares, price, amount, fee, tax, description, transaction_id.
/// </summary>
public static class ImportTradeRepublic
{
    private static readonly string[] Obligatoires = { "datetime", "account_type", "type", "amount", "transaction_id" };

    /// <summary>Vrai si le texte ressemble à un export Trade Republic (en-tête reconnu).</summary>
    public static bool Reconnait(string texte)
    {
        var entete = Lignes(texte).FirstOrDefault();
        return entete is not null && Obligatoires.All(c => entete.Contains(c, StringComparer.OrdinalIgnoreCase));
    }

    public static LectureReleveBourse Lire(string texte)
    {
        var lignes = Lignes(texte).ToList();
        if (lignes.Count == 0 || !Reconnait(texte))
            throw new FormatException("Ce fichier n'est pas un export des transactions de Trade Republic (en-tête non reconnu).");

        var colonnes = lignes[0].Select((nom, i) => (nom, i)).ToDictionary(x => x.nom.Trim(), x => x.i, StringComparer.OrdinalIgnoreCase);
        string? Valeur(IReadOnlyList<string> ligne, string nom) =>
            colonnes.TryGetValue(nom, out var i) && i < ligne.Count && !string.IsNullOrWhiteSpace(ligne[i]) ? ligne[i].Trim() : null;
        decimal Nombre(IReadOnlyList<string> ligne, string nom) =>
            Valeur(ligne, nom) is { } v && decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0m;

        var operations = new List<OperationBourse>();
        var ignorees = 0;
        foreach (var ligne in lignes.Skip(1))
        {
            if (ligne.All(string.IsNullOrWhiteSpace))
                continue;
            var identifiant = Valeur(ligne, "transaction_id");
            var dateTexte = Valeur(ligne, "datetime") ?? Valeur(ligne, "date");
            if (identifiant is null || dateTexte is null
                || !DateTime.TryParse(dateTexte, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
            {
                ignorees++;
                continue;
            }

            var quantite = Nombre(ligne, "shares");
            var montant = Nombre(ligne, "amount");
            var isin = Valeur(ligne, "symbol");
            var type = Valeur(ligne, "type") ?? "";
            var enveloppe = string.Equals(Valeur(ligne, "account_type"), "PEA", StringComparison.OrdinalIgnoreCase)
                ? EnveloppeBourse.Pea : EnveloppeBourse.CompteTitres;

            operations.Add(new OperationBourse(identifiant, date, enveloppe, Classer(type, isin, quantite, montant),
                Valeur(ligne, "name"), isin, Valeur(ligne, "asset_class"), quantite, Nombre(ligne, "price"), montant,
                Nombre(ligne, "fee"), Nombre(ligne, "tax"), Valeur(ligne, "description") ?? type));
        }
        return new LectureReleveBourse(operations, ignorees);
    }

    /// <summary>Type d'opération d'après le type Trade Republic, et sinon d'après les titres et le sens du montant.</summary>
    public static TypeOperationBourse Classer(string type, string? isin, decimal quantite, decimal montant)
    {
        var t = type.ToUpperInvariant();
        if (isin is not null && quantite != 0)
            return quantite > 0 ? TypeOperationBourse.Achat : TypeOperationBourse.Vente;
        if (t is "BUY" or "SAVINGS_PLAN" && isin is not null)
            return TypeOperationBourse.Achat;
        if (t == "SELL" && isin is not null)
            return TypeOperationBourse.Vente;
        if (t.Contains("DIVIDEND") || t.Contains("DISTRIBUTION"))
            return TypeOperationBourse.Dividende;
        if (t.Contains("INTEREST"))
            return TypeOperationBourse.Interets;
        if (t.Contains("TAX"))
            return TypeOperationBourse.Impot;
        if (t.Contains("FEE"))
            return TypeOperationBourse.Frais;
        if (t.Contains("TRANSFER") || t.Contains("DEPOSIT") || t.Contains("WITHDRAW") || t.Contains("PAYMENT") || t.Contains("CARD"))
            return montant >= 0 ? TypeOperationBourse.Versement : TypeOperationBourse.Retrait;
        return TypeOperationBourse.Autre;
    }

    /// <summary>Lignes d'un CSV (séparateur virgule ou point-virgule, champs entre guillemets).</summary>
    private static IEnumerable<IReadOnlyList<string>> Lignes(string texte)
    {
        texte = texte.TrimStart('﻿');
        var premiere = texte.Split('\n', 2)[0];
        var separateur = premiere.Count(c => c == ';') > premiere.Count(c => c == ',') ? ';' : ',';

        var champs = new List<string>();
        var champ = new StringBuilder();
        var guillemets = false;
        for (var i = 0; i < texte.Length; i++)
        {
            var c = texte[i];
            if (guillemets)
            {
                if (c == '"' && i + 1 < texte.Length && texte[i + 1] == '"')
                {
                    champ.Append('"');
                    i++;
                }
                else if (c == '"')
                    guillemets = false;
                else
                    champ.Append(c);
            }
            else if (c == '"')
                guillemets = true;
            else if (c == separateur)
            {
                champs.Add(champ.ToString());
                champ.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < texte.Length && texte[i + 1] == '\n')
                    i++;
                champs.Add(champ.ToString());
                champ.Clear();
                yield return champs;
                champs = new List<string>();
            }
            else
                champ.Append(c);
        }
        if (champ.Length > 0 || champs.Count > 0)
        {
            champs.Add(champ.ToString());
            yield return champs;
        }
    }
}
