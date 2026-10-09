using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GestionCompte.Core.Achats;
using GestionCompte.Core.Lettres;
using GestionCompte.Core.Mail;
using GestionCompte.Data.Documents;
using GestionCompte.Data.Mail;
using GestionCompte.Presentation;
using Microsoft.Win32;

namespace GestionCompte.App;

/// <summary>Fenêtres de dialogue Windows standard.</summary>
public sealed class Dialogues : IDialogues
{
    private const string Titre = "Gestion Compte";
    private const string FiltreFichiers = "Données Gestion Compte (*.db)|*.db|Tous les fichiers (*.*)|*.*";

    private static Window? Fenetre => Application.Current?.MainWindow;

    public bool Confirmer(string titre, string message) =>
        Afficher(message, titre, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Erreur(string message) => Afficher(message, Titre, MessageBoxButton.OK, MessageBoxImage.Error);

    private static MessageBoxResult Afficher(string message, string titre, MessageBoxButton boutons, MessageBoxImage icone) =>
        Fenetre is { } fenetre
            ? MessageBox.Show(fenetre, message, titre, boutons, icone)
            : MessageBox.Show(message, titre, boutons, icone);

    public string? ChoisirFichierSauvegarde(string nomParDefaut)
    {
        var dialogue = new SaveFileDialog
        {
            Title = "Enregistrer une copie des données",
            FileName = nomParDefaut,
            Filter = FiltreFichiers,
            DefaultExt = ".db",
            OverwritePrompt = true,
        };
        return dialogue.ShowDialog(Fenetre) == true ? dialogue.FileName : null;
    }

    public string? ChoisirFichierPdf(string nomParDefaut)
    {
        var dialogue = new SaveFileDialog
        {
            Title = "Exporter en PDF",
            FileName = nomParDefaut,
            Filter = "Document PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            OverwritePrompt = true,
        };
        return dialogue.ShowDialog(Fenetre) == true ? dialogue.FileName : null;
    }

    public string? ChoisirFichierWord(string nomParDefaut)
    {
        var dialogue = new SaveFileDialog
        {
            Title = "Enregistrer en Word",
            FileName = nomParDefaut,
            Filter = "Document Word (*.docx)|*.docx",
            DefaultExt = ".docx",
            OverwritePrompt = true,
        };
        return dialogue.ShowDialog(Fenetre) == true ? dialogue.FileName : null;
    }

    public string? ChoisirFichierExport(string nomParDefaut)
    {
        var dialogue = new SaveFileDialog
        {
            Title = "Exporter vers Excel",
            FileName = nomParDefaut,
            Filter = "Classeur Excel (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            OverwritePrompt = true,
        };
        return dialogue.ShowDialog(Fenetre) == true ? dialogue.FileName : null;
    }

    public string? ChoisirFichierARestaurer()
    {
        var dialogue = new OpenFileDialog
        {
            Title = "Restaurer une sauvegarde",
            Filter = FiltreFichiers,
            CheckFileExists = true,
        };
        return dialogue.ShowDialog(Fenetre) == true ? dialogue.FileName : null;
    }

    public string? ChoisirReleve()
    {
        var dialogue = new OpenFileDialog
        {
            Title = "Importer un relevé bancaire",
            Filter = "Relevé bancaire OFX (*.ofx;*.qfx)|*.ofx;*.qfx",
            CheckFileExists = true,
        };
        return dialogue.ShowDialog(Fenetre) == true ? dialogue.FileName : null;
    }

    public string? ChoisirExportBourse()
    {
        var dialogue = new OpenFileDialog
        {
            Title = "Importer l'export de Trade Republic",
            Filter = "Export des transactions (*.csv)|*.csv",
            CheckFileExists = true,
        };
        return dialogue.ShowDialog(Fenetre) == true ? dialogue.FileName : null;
    }

    public DemandeReinitialisation? ChoisirReinitialisation()
    {
        var fenetre = new FenetreReinitialisation { Owner = Fenetre };
        return fenetre.ShowDialog() == true ? fenetre.Demande : null;
    }

    public DemandeNouveauCompte? DemanderNouveauCompte(IReadOnlyList<string> comptes, string compteActif)
    {
        var fenetre = new FenetreNouveauCompte(comptes, compteActif) { Owner = Fenetre };
        return fenetre.ShowDialog() == true ? fenetre.Demande : null;
    }

    public bool ProposerMiseAJour(string nouvelle, string actuelle, string nouveautes) =>
        new FenetreMiseAJour(nouvelle, actuelle, nouveautes) { Owner = Fenetre }.ShowDialog() == true;

    public int? ChoisirOption(string titre, string message, IReadOnlyList<string> options)
    {
        int? choix = null;
        var fenetre = new Window
        {
            Title = titre,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = Fenetre is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = Fenetre is null,
            Owner = Fenetre,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13,
        };
        fenetre.SetResourceReference(Control.BackgroundProperty, "Fond");
        fenetre.SetResourceReference(Control.ForegroundProperty, "Texte");
        var boutons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        for (var i = 0; i < options.Count; i++)
        {
            var position = i;
            var bouton = new Button { Content = options[i], Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(8, 0, 0, 0), IsDefault = i == 0 };
            if (i == 0)
                bouton.Style = (Style)Application.Current.FindResource("BoutonPrincipal");
            bouton.Click += (_, _) =>
            {
                choix = position;
                fenetre.Close();
            };
            boutons.Children.Add(bouton);
        }
        fenetre.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, boutons },
        };
        fenetre.ShowDialog();
        return choix;
    }

    public string? DemanderNom(string titre, string message, string valeur)
    {
        var fenetre = new FenetreNom(titre, message, valeur) { Owner = Fenetre };
        return fenetre.ShowDialog() == true ? fenetre.Valeur : null;
    }

    public string? ChoisirDocument()
    {
        var dialogue = new OpenFileDialog
        {
            Title = "Ajouter un document au coffre",
            Filter = "Documents (*.pdf;*.jpg;*.jpeg;*.png;*.docx;*.xlsx;*.odt;*.txt)|*.pdf;*.jpg;*.jpeg;*.png;*.heic;*.docx;*.doc;*.xlsx;*.xls;*.odt;*.txt|Tous les fichiers (*.*)|*.*",
            CheckFileExists = true,
        };
        return dialogue.ShowDialog(Fenetre) == true ? dialogue.FileName : null;
    }

    public string? ChoisirEmplacementFichier(string nomParDefaut)
    {
        var extension = Path.GetExtension(nomParDefaut);
        var dialogue = new SaveFileDialog
        {
            Title = "Enregistrer sous",
            FileName = nomParDefaut,
            Filter = extension.Length > 1
                ? $"Fichier {extension.TrimStart('.').ToUpperInvariant()} (*{extension})|*{extension}|Tous les fichiers (*.*)|*.*"
                : "Tous les fichiers (*.*)|*.*",
            DefaultExt = extension,
            OverwritePrompt = true,
        };
        return dialogue.ShowDialog(Fenetre) == true ? dialogue.FileName : null;
    }

    public string? ChoisirDossier(string titre)
    {
        var dialogue = new OpenFolderDialog { Title = titre };
        return dialogue.ShowDialog(Fenetre) == true ? dialogue.FolderName : null;
    }

    public string? DemanderMotDePasse(string titre, string message, bool confirmer)
    {
        var fenetre = new FenetreMotDePasse(titre, message, confirmer) { Owner = Fenetre };
        return fenetre.ShowDialog() == true ? fenetre.Valeur : null;
    }

    public void AfficherCleSecours(string cle) => new FenetreCleSecours(cle) { Owner = Fenetre }.ShowDialog();

    public IdentifiantsGoogle? DemanderIdentifiantsGoogle(IdentifiantsGoogle? actuels)
    {
        var fenetre = new FenetreGoogle(actuels) { Owner = Fenetre };
        return fenetre.ShowDialog() == true ? fenetre.Identifiants : null;
    }

    public string? ChoisirFichierAJoindre()
    {
        var dialogue = new OpenFileDialog
        {
            Title = "Joindre un fichier au mail",
            Filter = "Tous les fichiers (*.*)|*.*",
            CheckFileExists = true,
        };
        return dialogue.ShowDialog(Fenetre) == true ? dialogue.FileName : null;
    }

    public IReadOnlyList<int>? ChoisirParmi(string titre, string message, IReadOnlyList<string> elements)
    {
        var fenetre = new FenetreChoix(titre, message, elements) { Owner = Fenetre };
        return fenetre.ShowDialog() == true ? fenetre.Choix : null;
    }

    public Contact? DemanderContact(Contact? actuel)
    {
        var fenetre = new FenetreContact(actuel) { Owner = Fenetre };
        return fenetre.ShowDialog() == true ? fenetre.Contact : null;
    }

    public Coordonnees? DemanderCoordonnees(Coordonnees actuelles)
    {
        var fenetre = new FenetreCoordonnees(actuelles) { Owner = Fenetre };
        return fenetre.ShowDialog() == true ? fenetre.Coordonnees : null;
    }

    public SaisieSmtp? DemanderReglagesSmtp(ReglagesSmtp? actuels)
    {
        var fenetre = new FenetreSmtp(actuels) { Owner = Fenetre };
        return fenetre.ShowDialog() == true ? fenetre.Saisie : null;
    }

    public SaisieIA? DemanderCleIA(string nom, string adresseCle, bool cleExistante, string modele, string modeleParDefaut)
    {
        var fenetre = new FenetreCleIA(nom, adresseCle, cleExistante, modele, modeleParDefaut) { Owner = Fenetre };
        return fenetre.ShowDialog() == true ? fenetre.Saisie : null;
    }

    public SiteRecherche? DemanderSite(SiteRecherche? actuel)
    {
        var fenetre = new FenetreSite(actuel) { Owner = Fenetre };
        return fenetre.ShowDialog() == true ? fenetre.Site : null;
    }

    public AchatPrevu? DemanderAchatPrevu(string libelle, decimal montant, IReadOnlyList<ChoixPeriode> periodes)
    {
        var fenetre = new FenetreAchatPrevu(libelle, montant, periodes) { Owner = Fenetre };
        return fenetre.ShowDialog() == true ? fenetre.Achat : null;
    }

    public void OuvrirFichier(string chemin) => Lancer(chemin, "Aucun programme n'est installé pour ouvrir ce type de fichier.");

    public void OuvrirLien(string adresse) => Lancer(adresse, $"Le navigateur n'a pas pu être ouvert. Adresse à ouvrir :\n{adresse}");

    private void Lancer(string cible, string message)
    {
        try
        {
            Process.Start(new ProcessStartInfo(cible) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Erreur(message);
        }
    }

    public void Information(string titre, string message) =>
        Afficher(message, titre, MessageBoxButton.OK, MessageBoxImage.Information);

    public void OuvrirDossier(string dossier) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dossier}\"") { UseShellExecute = true });
}
