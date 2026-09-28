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
