using System.Globalization;
using System.Text;
using GestionCompte.Core.Calculs;
using GestionCompte.Core.Modeles;

namespace GestionCompte.Core.Lettres;

/// <summary>Coordonnées de l'expéditeur, saisies une fois : ajoutées aux lettres par l'application, jamais envoyées à l'IA.</summary>
public sealed class Coordonnees
{
    public string Nom { get; set; } = "";
    public string Adresse { get; set; } = "";
    public string Telephone { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary>Ville pour « Fait à …, le … ».</summary>
    public string Ville { get; set; } = "";

    public bool Vides => string.IsNullOrWhiteSpace(Nom) && string.IsNullOrWhiteSpace(Adresse);

    /// <summary>Bloc de l'expéditeur, en haut à gauche de la lettre.</summary>
    public IEnumerable<string> Lignes()
    {
        if (!string.IsNullOrWhiteSpace(Nom))
            yield return Nom.Trim();
        foreach (var ligne in Adresse.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return ligne;
        if (!string.IsNullOrWhiteSpace(Telephone))
            yield return $"Tél. : {Telephone.Trim()}";
        if (!string.IsNullOrWhiteSpace(Email))
            yield return Email.Trim();
    }
}

/// <summary>Modèle de lettre proposé : l'IA rédige d'après la consigne et les informations saisies.</summary>
public sealed record ModeleLettre(string Titre, string Consigne, IReadOnlyList<string> Champs)
{
    public override string ToString() => Titre;

    public const string Libre = "Lettre libre";

    public static IReadOnlyList<ModeleLettre> Liste { get; } = new ModeleLettre[]
    {
        new("Résiliation d'un abonnement ou d'un contrat",
            "Lettre de résiliation d'un abonnement ou d'un contrat (box internet, téléphone, salle de sport, magazine…), en citant les droits applicables si utile.",
            new[] { "Organisme", "Numéro de client ou de contrat", "Date de résiliation souhaitée", "Motif (facultatif)" }),
        new("Résiliation d'une assurance",
            "Lettre de résiliation d'un contrat d'assurance (auto, habitation…), en s'appuyant sur la loi Hamon (après un an) ou sur l'échéance annuelle (loi Chatel) selon le cas.",
            new[] { "Assureur", "Numéro de contrat", "Type d'assurance", "Date de souscription ou d'échéance", "Nouvel assureur (facultatif)" }),
        new("Contestation de frais bancaires",
            "Lettre à sa banque pour contester des frais bancaires et en demander le remboursement, de façon courtoise et argumentée.",
            new[] { "Banque", "Frais contestés (dates et montants)", "Raison de la contestation" }),
        new("Réclamation ou remboursement d'une commande",
            "Lettre de réclamation à un vendeur (commande non livrée, produit défectueux ou non conforme) demandant un remboursement ou un échange, en rappelant les droits du consommateur.",
            new[] { "Vendeur", "Numéro de commande", "Date de la commande", "Problème rencontré", "Solution demandée" }),
        new("Garantie légale de conformité",
            "Lettre au vendeur pour faire jouer la garantie légale de conformité (réparation ou remplacement), en citant le Code de la consommation.",
            new[] { "Vendeur", "Produit", "Date d'achat", "Défaut constaté" }),
        new("Demande de délai de paiement",
            "Lettre demandant un échelonnement ou un délai de paiement pour une somme due, en proposant un échéancier réaliste.",
            new[] { "Créancier", "Référence (facture, dossier)", "Montant dû", "Échéancier proposé", "Raison (facultatif)" }),
        new("Restitution du dépôt de garantie",
            "Lettre au propriétaire ou à l'agence pour demander la restitution du dépôt de garantie après l'état des lieux de sortie, en rappelant les délais légaux.",
            new[] { "Propriétaire ou agence", "Adresse du logement", "Date de l'état des lieux de sortie", "Montant du dépôt" }),
        new("Changement d'adresse",
            "Lettre informant un organisme d'un changement d'adresse.",
            new[] { "Organisme", "Numéro de client ou de dossier", "Nouvelle adresse", "Date du changement" }),
        new(Libre, "Lettre rédigée d'après la demande ci-dessous.", new[] { "Votre demande" }),
    };
}

/// <summary>Une lettre : texte rédigé (par l'IA puis relu par l'utilisateur) et mise en page par l'application.</summary>
public sealed class Lettre
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; set; } = DateTime.Today;
    public string Modele { get; set; } = "";
    public string Destinataire { get; set; } = "";
    public string Objet { get; set; } = "";
    public string Corps { get; set; } = "";

    /// <summary>Ex. « Fait à Lyon, le 15 novembre 2026 ».</summary>
    public string LieuDate(Coordonnees coordonnees) =>
        $"{(string.IsNullOrWhiteSpace(coordonnees.Ville) ? "Le" : $"Fait à {coordonnees.Ville.Trim()}, le")} {Date.ToString("d MMMM yyyy", Montants.Francais)}";

    /// <summary>Texte complet (pour le mail ou le presse-papiers).</summary>
    public string TexteComplet(Coordonnees coordonnees)
    {
        var texte = new StringBuilder();
        foreach (var ligne in coordonnees.Lignes())
            texte.AppendLine(ligne);
        texte.AppendLine();
        if (!string.IsNullOrWhiteSpace(Destinataire))
            texte.AppendLine(Destinataire.Trim()).AppendLine();
        texte.AppendLine(LieuDate(coordonnees)).AppendLine();
        texte.AppendLine($"Objet : {Objet}").AppendLine();
        texte.AppendLine(Corps.Trim()).AppendLine();
        if (!string.IsNullOrWhiteSpace(coordonnees.Nom))
            texte.AppendLine(coordonnees.Nom.Trim());
        return texte.ToString();
    }
}

public static class RedactionLettre
{
    /// <summary>
    /// Demande envoyée à l'IA : le modèle et les informations saisies, sans les coordonnées de l'utilisateur.
    /// </summary>
    public static string Demande(ModeleLettre modele, IEnumerable<(string Champ, string Valeur)> informations, string? precisions)
    {
        var texte = new StringBuilder();
        texte.AppendLine("Rédige en français le texte d'une lettre formelle (courrier papier ou mail), claire, polie et concise.");
        texte.AppendLine($"Type de lettre : {modele.Consigne}");
        texte.AppendLine("Informations :");
        foreach (var (champ, valeur) in informations.Where(i => !string.IsNullOrWhiteSpace(i.Valeur)))
            texte.AppendLine($"- {champ} : {valeur.Trim()}");
        if (!string.IsNullOrWhiteSpace(precisions))
            texte.AppendLine($"Précisions : {precisions.Trim()}");
        texte.AppendLine();
        texte.AppendLine("Format de la réponse, sans rien d'autre :");
        texte.AppendLine("Objet : (une ligne)");
        texte.AppendLine("(ligne vide)");
        texte.AppendLine("Le corps de la lettre, de la formule d'appel (« Madame, Monsieur, ») jusqu'à la formule de politesse incluse.");
        texte.AppendLine("N'écris ni l'en-tête (expéditeur, destinataire, lieu, date) ni la signature : l'application les ajoute. " +
                         "N'invente aucune information : si un élément manque, écris-le entre crochets, par exemple [numéro de contrat].");
        return texte.ToString();
    }

    /// <summary>Sépare « Objet : … » du corps ; enlève les ``` éventuels.</summary>
    public static (string Objet, string Corps) Decouper(string reponse)
    {
        var lignes = reponse.Replace("\r", "").Replace("```", "").Split('\n').ToList();
        var objet = "";
        var index = lignes.FindIndex(l => l.TrimStart('*', ' ').StartsWith("Objet", StringComparison.OrdinalIgnoreCase)
                                          && l.Contains(':'));
        if (index >= 0)
        {
            var ligne = lignes[index];
            objet = ligne[(ligne.IndexOf(':') + 1)..].Trim().Trim('*').Trim();
            lignes.RemoveRange(0, index + 1);
        }
        var corps = string.Join("\n", lignes).Trim();
        return (objet, corps);
    }
}

public static class ResumeBudget
{
    /// <summary>
    /// Résumé des chiffres du compte pour l'assistant (sans libellés d'opérations, sans numéro de compte ni nom) :
    /// mois en cours, charges, enveloppes, épargne, prévisionnel et bilan des 12 derniers mois.
    /// </summary>
    public static string Construire(CompteBancaire compte, PeriodeMois moisDuJour)
    {
        string E(decimal m) => $"{Montants.Formater(m)} €";
        var texte = new StringBuilder();
        var mois = compte.Trouver(moisDuJour) ?? compte.Mois.LastOrDefault();
        if (mois is not null)
        {
            var r = compte.Calculer(mois.Periode);
            texte.AppendLine($"Mois de {mois.Periode.Libelle} :");
            texte.AppendLine($"- revenus : {E(r.TotalRevenus)}, solde de départ : {E(r.SoldeDepart)}, solde prévu en fin de mois : {E(r.SoldeFinPrevisionnel)}");
            foreach (var enveloppe in r.Enveloppes)
                texte.AppendLine($"- enveloppe {enveloppe.Nom} : budget {E(enveloppe.Budget)}, dépensé {E(enveloppe.Depense)}{(enveloppe.Depassee ? " (dépassée)" : "")}");
        }
        else
            texte.AppendLine("Aucun mois créé pour l'instant.");

        var charges = compte.Configuration.Charges.Where(c => c.Debit > 0).ToList();
        if (charges.Count > 0)
        {
            texte.AppendLine("Charges mensuelles prévues :");
            foreach (var c in charges)
                texte.AppendLine($"- {c.Nom} : {E(c.Debit)}{(c.Frequence > 1 ? $" tous les {c.Frequence} mois" : "")}{(c.Categorie == Categorie.NonClassee ? "" : $" ({NomCategorie(c.Categorie)})")}");
        }

        foreach (var objectif in compte.ObjectifsEpargne)
            texte.AppendLine($"Objectif d'épargne {objectif.Nom} : {E(objectif.Montant)} pour {objectif.Echeance.Libelle}");
        if (mois is not null)
            foreach (var cumul in compte.ComptesCumulJusqua(mois.Periode))
                texte.AppendLine($"Compte cumulé {cumul.Nom} : {E(cumul.Total)}{(cumul.Objectif is { } o ? $" (objectif {E(o)})" : "")}");

        if (compte.Mois.Count > 0)
        {
            var prevision = Previsionnel.Calculer(compte, 0, 6);
            if (prevision.MoisPrevus.Any())
            {
                texte.AppendLine("Prévisionnel des 6 prochains mois (solde de fin) : " +
                                 string.Join(", ", prevision.MoisPrevus.Select(m => $"{m.Periode.Libelle} {E(m.SoldeFin)}")));
                if (prevision.PremierMoisNegatif is { } negatif)
                    texte.AppendLine($"Attention : solde négatif prévu en {negatif.Periode.Libelle}.");
            }
            foreach (var prevue in compte.OperationsPrevues.Where(o => o.Periode >= moisDuJour).Take(10))
                texte.AppendLine($"Opération prévue en {prevue.Periode.Libelle} : {prevue.Libelle} {(prevue.Debit > 0 ? "-" + E(prevue.Debit) : "+" + E(prevue.Credit))}");

            var (debut, fin) = Bilan.DouzeDerniersMois(moisDuJour);
            var bilan = Bilan.Calculer(compte, debut, fin);
            if (!bilan.Vide)
            {
                texte.AppendLine($"Bilan des 12 derniers mois ({bilan.Etendue}) : revenus {E(bilan.Revenus)}, dépenses {E(bilan.Depenses)}, épargne {E(bilan.Epargne)}.");
                texte.AppendLine("Principaux postes (moyenne par mois) : " +
                                 string.Join(", ", bilan.Postes.Take(8).Select(p => $"{p.Nom} {E(p.MoyenneMensuelle)}")));
            }
        }
        return texte.ToString().TrimEnd();
    }

    private static string NomCategorie(Categorie categorie) => categorie switch
    {
        Categorie.Essentiel => "essentiel",
        Categorie.Confort => "confort",
        Categorie.Epargne => "épargne",
        _ => "",
    };
}
