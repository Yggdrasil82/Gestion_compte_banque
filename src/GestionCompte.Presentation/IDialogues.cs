namespace GestionCompte.Presentation;

/// <summary>Fenêtres de dialogue, fournies par l'application Windows (remplacées par des faux dans les tests).</summary>
public interface IDialogues
{
    bool Confirmer(string titre, string message);

    void Erreur(string message);

    /// <summary>Demande où enregistrer une copie ; null si l'utilisateur annule.</summary>
    string? ChoisirFichierSauvegarde(string nomParDefaut);

    /// <summary>Demande où enregistrer un export Excel ; null si l'utilisateur annule.</summary>
    string? ChoisirFichierExport(string nomParDefaut);

    /// <summary>Demande où enregistrer un export PDF ; null si l'utilisateur annule.</summary>
    string? ChoisirFichierPdf(string nomParDefaut) => null;

    /// <summary>Demande où enregistrer un document Word ; null si l'utilisateur annule.</summary>
    string? ChoisirFichierWord(string nomParDefaut) => null;

    /// <summary>Coordonnées de l'expéditeur des lettres ; null si annulé.</summary>
    Core.Lettres.Coordonnees? DemanderCoordonnees(Core.Lettres.Coordonnees actuelles) => null;

    // ---- Documents ----

    /// <summary>Demande quel fichier ajouter au coffre ; null si l'utilisateur annule.</summary>
    string? ChoisirDocument() => null;

    /// <summary>Demande où enregistrer un fichier (copie d'un document, sauvegarde du coffre) ; null si annulé.</summary>
    string? ChoisirEmplacementFichier(string nomParDefaut) => null;

    /// <summary>Demande un dossier (ex. dossier synchronisé par Google Drive) ; null si annulé.</summary>
    string? ChoisirDossier(string titre) => null;

    /// <summary>Demande un mot de passe, saisi deux fois si <paramref name="confirmer"/> ; null si annulé.</summary>
    string? DemanderMotDePasse(string titre, string message, bool confirmer) => null;

    /// <summary>Montre la clé de secours du coffre, à noter ou imprimer.</summary>
    void AfficherCleSecours(string cle) { }

    /// <summary>Demande l'identifiant d'application Google (avec le guide pour le créer) ; null si annulé.</summary>
    Data.Documents.IdentifiantsGoogle? DemanderIdentifiantsGoogle(Data.Documents.IdentifiantsGoogle? actuels) => null;

    /// <summary>Ouvre un fichier avec le programme habituel de Windows.</summary>
    void OuvrirFichier(string chemin) { }

    /// <summary>Ouvre une adresse dans le navigateur.</summary>
    void OuvrirLien(string adresse) { }

    void Information(string titre, string message) { }

    /// <summary>Demande quel fichier joindre à un mail ; null si annulé.</summary>
    string? ChoisirFichierAJoindre() => null;

    /// <summary>Liste à cocher (ex. documents à joindre) ; renvoie les positions cochées, null si annulé.</summary>
    IReadOnlyList<int>? ChoisirParmi(string titre, string message, IReadOnlyList<string> elements) => null;

    /// <summary>Saisie ou modification d'un contact ; null si annulé.</summary>
    Core.Mail.Contact? DemanderContact(Core.Mail.Contact? actuel) => null;

    /// <summary>Réglages du serveur d'envoi (SMTP) ; mot de passe null = garder celui déjà enregistré. Null si annulé.</summary>
    SaisieSmtp? DemanderReglagesSmtp(Data.Mail.ReglagesSmtp? actuels) => null;

    /// <summary>Clé API d'une IA (avec le lien pour la créer) ; null si annulé.</summary>
    /// <param name="cleExistante">Une clé est déjà enregistrée : la laisser vide la garde.</param>
    SaisieIA? DemanderCleIA(string nom, string adresseCle, bool cleExistante, string modele, string modeleParDefaut) => null;

    /// <summary>Ajout ou modification d'un site de la liste de recherche ; null si annulé.</summary>
    Core.Achats.SiteRecherche? DemanderSite(Core.Achats.SiteRecherche? actuel) => null;

    /// <summary>Mois, libellé et montant d'un achat à ajouter au prévisionnel ; null si annulé.</summary>
    AchatPrevu? DemanderAchatPrevu(string libelle, decimal montant, IReadOnlyList<ChoixPeriode> periodes) => null;

    /// <summary>Demande quel relevé bancaire (.ofx) importer ; null si l'utilisateur annule.</summary>
    string? ChoisirReleve();

    /// <summary>Demande quelle sauvegarde restaurer ; null si l'utilisateur annule.</summary>
    string? ChoisirFichierARestaurer();

    void OuvrirDossier(string dossier);

    /// <summary>Demande quelle réinitialisation faire ; null si l'utilisateur annule.</summary>
    DemandeReinitialisation? ChoisirReinitialisation() => null;

    /// <summary>Demande le nom d'un nouveau compte et la configuration à copier ; null si l'utilisateur annule.</summary>
    DemandeNouveauCompte? DemanderNouveauCompte(IReadOnlyList<string> comptes, string compteActif) => null;

    /// <summary>Demande un texte (ex. nouveau nom d'un compte) ; null si l'utilisateur annule.</summary>
    string? DemanderNom(string titre, string message, string valeur) => null;
}

public sealed record AchatPrevu(Core.Modeles.PeriodeMois Periode, string Libelle, decimal Montant);

/// <param name="MotDePasse">null : garder le mot de passe déjà enregistré.</param>
public sealed record SaisieSmtp(Data.Mail.ReglagesSmtp Reglages, string? MotDePasse);

/// <param name="CopierDe">Nom du compte dont la configuration est copiée ; null = configuration vierge.</param>
public sealed record DemandeNouveauCompte(string Nom, string? CopierDe);

public enum ChoixReinitialisation
{
    /// <summary>Efface les mois et les opérations prévues ; la configuration est gardée.</summary>
    EffacerMois,

    /// <summary>Efface toutes les données (pour passer l'application à une autre personne).</summary>
    ToutEffacer,
}

/// <param name="CopieAvant">Enregistrer d'abord une copie des données (choix de l'emplacement).</param>
public sealed record DemandeReinitialisation(ChoixReinitialisation Choix, bool CopieAvant);
