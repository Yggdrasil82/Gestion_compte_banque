using System.Text.RegularExpressions;

namespace GestionCompte.Core.Mail;

public enum SourceContact
{
    Manuel,
    Google,
}

/// <summary>Contact du carnet d'adresses (ajouté à la main ou importé de Google).</summary>
public sealed class Contact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nom { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary>Organisme ou rôle (assurance, banque, propriétaire…).</summary>
    public string Notes { get; set; } = "";

    public SourceContact Source { get; set; } = SourceContact.Manuel;

    /// <summary>Date du dernier mail envoyé à ce contact (les contacts récents sont proposés en premier).</summary>
    public DateTime? DernierEnvoi { get; set; }

    public int NombreEnvois { get; set; }

    /// <summary>Ex. « Assurance Auto &lt;contact@assurance.fr&gt; ».</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Affichage => string.IsNullOrWhiteSpace(Nom) ? Email : $"{Nom} <{Email}>";
}

/// <summary>Un mail envoyé (ou tenté) depuis l'application.</summary>
public sealed class EnvoiMail
{
    public DateTime Date { get; set; }
    public string Compte { get; set; } = "";
    public List<string> Destinataires { get; set; } = new();
    public string Objet { get; set; } = "";

    /// <summary>Début du message (pour se souvenir de ce qui a été envoyé).</summary>
    public string Extrait { get; set; } = "";

    public List<string> PiecesJointes { get; set; } = new();
    public bool Reussi { get; set; }
    public string? Erreur { get; set; }
}

public static partial class AdressesMail
{
    [GeneratedRegex(@"^[^@\s<>,;]+@[^@\s<>,;]+\.[^@\s<>,;]+$")]
    private static partial Regex Format();

    public static bool Valide(string adresse) => Format().IsMatch(adresse.Trim());

    /// <summary>
    /// Adresses d'une saisie « a@b.fr; Nom &lt;c@d.fr&gt;, e@f.fr » (séparateurs ; ou ,). Renvoie aussi les morceaux invalides.
    /// </summary>
    public static (IReadOnlyList<string> Adresses, IReadOnlyList<string> Invalides) Decouper(string saisie)
    {
        var adresses = new List<string>();
        var invalides = new List<string>();
        foreach (var morceau in saisie.Split(new[] { ';', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var adresse = morceau;
            var debut = morceau.LastIndexOf('<');
            if (debut >= 0 && morceau.EndsWith('>'))
                adresse = morceau[(debut + 1)..^1].Trim();
            if (Valide(adresse))
            {
                if (!adresses.Contains(adresse, StringComparer.OrdinalIgnoreCase))
                    adresses.Add(adresse);
            }
            else
                invalides.Add(morceau);
        }
        return (adresses, invalides);
    }
}
