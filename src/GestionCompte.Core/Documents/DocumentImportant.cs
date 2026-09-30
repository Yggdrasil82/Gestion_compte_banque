namespace GestionCompte.Core.Documents;

public enum CategorieDocument
{
    Identite,
    Banque,
    Logement,
    Vehicule,
    Sante,
    Impots,
    Contrats,
    Garanties,
    Autres,
}

/// <summary>Un document important du coffre (pièce d'identité, RIB, contrat…) : sa fiche et son fichier.</summary>
public sealed class DocumentImportant
{
    /// <summary>Identifiant unique ; le fichier est rangé sous ce nom dans le coffre.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Nom { get; set; } = "";

    public CategorieDocument Categorie { get; set; } = CategorieDocument.Autres;

    /// <summary>Date du document (signature, émission…), facultative.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>Fin de contrat, date d'expiration, fin de garantie… ; null s'il n'y en a pas.</summary>
    public DateOnly? Echeance { get; set; }

    /// <summary>Nombre de jours avant l'échéance à partir duquel le rappel s'affiche.</summary>
    public int RappelJours { get; set; } = 30;

    public string Notes { get; set; } = "";

    /// <summary>Charge de la configuration liée (ex. contrat « Assurance Voiture »), ou null.</summary>
    public string? Charge { get; set; }

    /// <summary>Nom du fichier d'origine (ex. « carte-identite.pdf ») : son extension sert à l'ouvrir.</summary>
    public string NomFichier { get; set; } = "";

    public long Taille { get; set; }

    /// <summary>Fichier chiffré avec le mot de passe du coffre.</summary>
    public bool Protege { get; set; }

    public DateTime AjouteLe { get; set; } = DateTime.Now;

    public static string NomCategorie(CategorieDocument categorie) => categorie switch
    {
        CategorieDocument.Identite => "Identité",
        CategorieDocument.Banque => "Banque",
        CategorieDocument.Logement => "Logement",
        CategorieDocument.Vehicule => "Véhicule",
        CategorieDocument.Sante => "Santé",
        CategorieDocument.Impots => "Impôts",
        CategorieDocument.Contrats => "Contrats",
        CategorieDocument.Garanties => "Garanties",
        _ => "Autres",
    };
}

/// <param name="JoursRestants">Jours avant l'échéance (négatif : échéance dépassée).</param>
public sealed record RappelDocument(DocumentImportant Document, int JoursRestants)
{
    public bool Depasse => JoursRestants < 0;

    /// <summary>Ex. « Assurance voiture : échéance dans 12 jours (15/10/2026) ».</summary>
    public string Texte
    {
        get
        {
            var date = Document.Echeance!.Value.ToString("dd/MM/yyyy", Montants.Francais);
            var quand = JoursRestants switch
            {
                < -1 => $"échéance dépassée depuis {-JoursRestants} jours",
                -1 => "échéance dépassée depuis hier",
                0 => "échéance aujourd'hui",
                1 => "échéance demain",
                _ => $"échéance dans {JoursRestants} jours",
            };
            return $"{Document.Nom} : {quand} ({date})";
        }
    }
}

public static class RappelsDocuments
{
    /// <summary>Documents dont l'échéance approche (ou est dépassée depuis moins d'un an), la plus proche d'abord.</summary>
    public static IReadOnlyList<RappelDocument> Calculer(IEnumerable<DocumentImportant> documents, DateOnly aujourdHui) =>
        documents
            .Where(d => d.Echeance is not null)
            .Select(d => new RappelDocument(d, d.Echeance!.Value.DayNumber - aujourdHui.DayNumber))
            .Where(r => r.JoursRestants <= Math.Max(0, r.Document.RappelJours) && r.JoursRestants > -365)
            .OrderBy(r => r.JoursRestants)
            .ToList();
}
