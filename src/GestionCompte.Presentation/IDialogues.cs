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

    /// <summary>Demande quelle sauvegarde restaurer ; null si l'utilisateur annule.</summary>
    string? ChoisirFichierARestaurer();

    void OuvrirDossier(string dossier);
}
