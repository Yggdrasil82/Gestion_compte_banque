using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GestionCompte.Core.Documents;
using GestionCompte.Core.Mail;
using GestionCompte.Data;
using GestionCompte.Data.Documents;
using GestionCompte.Data.Mail;
using GestionCompte.Presentation;

namespace GestionCompte.App;

/// <summary>
/// Ouvre l'application sur des données d'exemple, enregistre une image PNG de chaque écran et de chaque apparence, puis quitte.
/// Utilisé par GitHub Actions (option « --captures dossier ») pour montrer l'interface sans PC Windows.
/// </summary>
internal static class Captures
{
    private const double Largeur = 1400;
    private const double Hauteur = 880;

    /// <summary>L'aide au budget est longue : elle est capturée en entier sur une image plus haute.</summary>
    private const double HauteurAide = 1260;

    /// <summary>Le module Crédits : comparaison, tableau d'amortissement et calcul inverse.</summary>
    private const double HauteurCredits = 960;

    /// <summary>Le bilan : chiffres clés, graphique, pistes et tous les postes.</summary>
    private const double HauteurBilan = 1420;

    /// <summary>La configuration aussi : toutes ses cartes sur une seule image.</summary>
    private const double HauteurConfiguration = 1880;

    /// <summary>Mail : rédaction, carnet d'adresses et historique.</summary>
    private const double HauteurMail = 960;

    public static void Lancer(App app, string dossier)
    {
        dossier = Path.GetFullPath(dossier);
        Directory.CreateDirectory(dossier);
        var fichierErreur = Path.Combine(dossier, "erreur.txt");

        app.DispatcherUnhandledException += (_, e) =>
        {
            File.WriteAllText(fichierErreur, e.Exception.ToString());
            e.Handled = true;
            app.Shutdown(3);
        };

        // Sécurité : ne jamais bloquer le serveur de compilation.
        var delaiMaximum = new DispatcherTimer { Interval = TimeSpan.FromSeconds(120) };
        delaiMaximum.Tick += (_, _) =>
        {
            File.WriteAllText(fichierErreur, "Délai dépassé pendant les captures.");
            app.Shutdown(2);
        };
        delaiMaximum.Start();

        // Deux comptes d'exemple : le compte courant et un livret.
        var dossierDemo = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"gestioncompte-demo-{Guid.NewGuid():N}")).FullName;
        var registre = RegistreComptes.Charger(dossierDemo);
        new DepotSqlite(registre.Chemin(registre.Actif)).Enregistrer(ConfigurationParDefaut.CreerDemo());
        new DepotSqlite(registre.Chemin(registre.Ajouter("Livret A"))).Enregistrer(ConfigurationParDefaut.CreerDemoLivret());
        CreerCarnetDemo(dossierDemo);
        // Serveur d'envoi d'exemple (jamais utilisé : aucune connexion pendant les captures).
        var secrets = new SecretsEnMemoire();
        secrets.Ecrire(MailViewModel.SecretSmtp, System.Text.Json.JsonSerializer.Serialize(
            new ReglagesSmtp("smtp.orange.fr", 465, SecuriteSmtp.Ssl, "prenom.nom@exemple.fr", "", "Prénom Nom")));
        secrets.Ecrire(MailViewModel.SecretSmtpMotDePasse, "demo");
        var vm = new MainViewModel(registre, new Dialogues(), new DateTime(2026, 11, 15), null, secrets);
        vm.Apparence.Changee += (_, _) => Themes.Appliquer(app, vm.Apparence.Ambiance, vm.Apparence.Sombre);
        Themes.Appliquer(app, vm.Apparence.Ambiance, vm.Apparence.Sombre);

        // Le contenu de la fenêtre est rendu hors écran, à taille fixe :
        // l'écran du serveur de compilation est trop petit pour la fenêtre réelle.
        var fenetre = new MainWindow();
        var contenu = (UIElement)fenetre.Content;
        fenetre.Content = null;
        var hote = new Border { Child = contenu, DataContext = vm, Width = Largeur, Height = Hauteur };
        // Le contenu reste rattaché à la fenêtre (jamais affichée) pour recevoir les changements de couleurs.
        fenetre.Content = hote;
        hote.SetResourceReference(Border.BackgroundProperty, "Fond");
        hote.SetResourceReference(TextElement.ForegroundProperty, "Texte");
        TextElement.SetFontFamily(hote, new FontFamily("Segoe UI"));
        TextElement.SetFontSize(hote, 13);

        var etapes = new (string Nom, Ambiance Ambiance, bool Sombre, int Onglet, bool MoisPrecedent)[]
        {
            ("1-mois-ocean", Ambiance.Ocean, false, MainViewModel.OngletMois, false),
            ("2-mois-ocean-sombre", Ambiance.Ocean, true, MainViewModel.OngletMois, false),
            ("3-mois-pastel", Ambiance.Pastel, false, MainViewModel.OngletMois, false),
            ("4-mois-pastel-sombre", Ambiance.Pastel, true, MainViewModel.OngletMois, false),
            ("5-mois-nuit-octobre", Ambiance.Nuit, false, MainViewModel.OngletMois, true),
            ("6-previsionnel-ocean", Ambiance.Ocean, false, MainViewModel.OngletPrevisionnel, false),
            ("7-previsionnel-nuit", Ambiance.Nuit, false, MainViewModel.OngletPrevisionnel, false),
            ("8-previsionnel-pastel", Ambiance.Pastel, false, MainViewModel.OngletPrevisionnel, false),
            ("9-configuration-ocean", Ambiance.Ocean, false, MainViewModel.OngletConfiguration, false),
            ("10-aide-ocean", Ambiance.Ocean, false, MainViewModel.OngletAide, false),
            ("11-aide-nuit", Ambiance.Nuit, false, MainViewModel.OngletAide, false),
            ("12-import-ocean", Ambiance.Ocean, false, MainViewModel.OngletImport, false),
            ("13-import-pastel-sombre", Ambiance.Pastel, true, MainViewModel.OngletImport, false),
            ("14-ensemble-ocean", Ambiance.Ocean, false, MainViewModel.OngletEnsemble, false),
            ("15-credits-ocean", Ambiance.Ocean, false, MainViewModel.OngletCredits, false),
            ("16-credits-pastel-sombre", Ambiance.Pastel, true, MainViewModel.OngletCredits, false),
        };

        // Aperçu d'import sur un relevé d'exemple (rien n'est validé).
        var dossierReleve = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"gestioncompte-{Guid.NewGuid():N}")).FullName;
        var releve = Path.Combine(dossierReleve, "releve-decembre-2026.ofx");
        File.WriteAllText(releve, ConfigurationParDefaut.ReleveDemo());
        vm.OuvrirImport(releve);

        async Task Capturer((string Nom, Ambiance Ambiance, bool Sombre, int Onglet, bool MoisPrecedent) etape)
        {
            vm.Apparence.Ambiance = etape.Ambiance;
            vm.Apparence.ModeSombre = etape.Sombre;
            vm.OngletSelectionne = etape.Onglet;
            if (etape.MoisPrecedent)
                vm.MoisPrecedentCommand.Execute(null);

            var hauteur = etape.Onglet switch
            {
                MainViewModel.OngletAide => HauteurAide,
                MainViewModel.OngletCredits => HauteurCredits,
                MainViewModel.OngletBilan => HauteurBilan,
                MainViewModel.OngletConfiguration => HauteurConfiguration,
                MainViewModel.OngletMail => HauteurMail,
                _ => Hauteur,
            };
            hote.Height = hauteur;
            await MettreEnPage(hote, hauteur);
            Enregistrer(hote, Path.Combine(dossier, etape.Nom + ".png"), hauteur);

            if (etape.MoisPrecedent)
                vm.MoisSuivantCommand.Execute(null);
        }

        app.Dispatcher.InvokeAsync(async () =>
        {
            foreach (var etape in etapes)
                await Capturer(etape);

            // Bilan : sur un troisième compte d'exemple qui a presque deux ans de mois.
            new DepotSqlite(registre.Chemin(registre.Ajouter("Compte joint"))).Enregistrer(ConfigurationParDefaut.CreerDemoHistorique());
            vm.CompteActif = "Compte joint";
            await Capturer(("17-bilan-ocean", Ambiance.Ocean, false, MainViewModel.OngletBilan, false));
            vm.Bilan.Selection = vm.Bilan.Choix[0];
            await Capturer(("18-bilan-12-mois-pastel-sombre", Ambiance.Pastel, true, MainViewModel.OngletBilan, false));

            // Documents : un coffre d'exemple (faux fichiers), dont un document protégé et des échéances proches.
            await vm.Documents.Chargement;
            await CreerCoffreDemo(vm.Documents.DossierLocal);
            await vm.Documents.ActualiserCommand.ExecuteAsync(null);
            vm.Documents.Selection = vm.Documents.Documents.FirstOrDefault(d => d.Nom == "Assurance voiture");
            await Capturer(("19-documents-ocean", Ambiance.Ocean, false, MainViewModel.OngletDocuments, false));
            vm.Documents.Selection = vm.Documents.Documents.FirstOrDefault(d => d.Protege);
            await Capturer(("20-documents-nuit-sombre", Ambiance.Nuit, true, MainViewModel.OngletDocuments, false));

            // Mail : un mail prêt à partir avec un document du coffre et le bilan en pièces jointes.
            vm.Mail.ModeSmtp = true;
            vm.Mail.Destinataires = "contact@assurance-exemple.fr";
            vm.Mail.Corps = "Bonjour,\n\nVeuillez trouver ci-joint mon attestation d'assurance et le bilan de mon budget.\n\nCordialement,\nPrénom Nom";
            await vm.Mail.JoindreDocumentAsync(vm.Documents.Tous.First(d => d.Nom == "Assurance voiture"));
            vm.Mail.JoindreBilanPdf();
            vm.Mail.Objet = "Attestation d'assurance voiture";
            vm.Mail.ContactSelectionne = vm.Mail.Contacts.FirstOrDefault();
            await Capturer(("21-mail-ocean", Ambiance.Ocean, false, MainViewModel.OngletMail, false));
            vm.Mail.ModeGmail = true;
            await Capturer(("22-mail-gmail-pastel-sombre", Ambiance.Pastel, true, MainViewModel.OngletMail, false));

            Directory.Delete(dossierDemo, true);
            Directory.Delete(dossierReleve, true);
            app.Shutdown(0);
        });
    }

    private static void CreerCarnetDemo(string dossier)
    {
        var carnet = new CarnetMail(dossier);
        carnet.Contacts.AddRange(new[]
        {
            new Contact { Nom = "Assurance auto (exemple)", Email = "contact@assurance-exemple.fr", Notes = "Contrat voiture n° 0000-DEMO" },
            new Contact { Nom = "Agence immobilière (exemple)", Email = "gestion@agence-exemple.fr", Notes = "Bail de l'appartement" },
            new Contact { Nom = "Conseiller bancaire (exemple)", Email = "conseiller@banque-exemple.fr", Notes = "Agence du centre" },
            new Contact { Nom = "Mutuelle (exemple)", Email = "adherents@mutuelle-exemple.fr", Source = SourceContact.Google },
            new Contact { Nom = "Camille", Email = "camille@exemple.fr", Source = SourceContact.Google },
        });
        carnet.NoterEnvoi(new EnvoiMail
        {
            Date = new DateTime(2026, 10, 2, 9, 15, 0), Compte = "prenom.nom@exemple.fr", Destinataires = { "conseiller@banque-exemple.fr" },
            Objet = "Demande de RIB", Reussi = false, Erreur = "Le serveur d'envoi n'a pas répondu.",
        });
        carnet.NoterEnvoi(new EnvoiMail
        {
            Date = new DateTime(2026, 10, 2, 9, 20, 0), Compte = "prenom.nom@exemple.fr", Destinataires = { "conseiller@banque-exemple.fr" },
            Objet = "Demande de RIB", Reussi = true,
        });
        carnet.NoterEnvoi(new EnvoiMail
        {
            Date = new DateTime(2026, 11, 3, 18, 42, 0), Compte = "prenom.nom@exemple.fr", Destinataires = { "gestion@agence-exemple.fr" },
            Objet = "Attestation d'assurance habitation", PiecesJointes = { "attestation-habitation.pdf" }, Reussi = true,
        });
        carnet.Enregistrer();
    }

    private static async Task CreerCoffreDemo(string dossier)
    {
        var coffre = new CoffreDocuments(new StockageDossier(dossier), 1_000);
        await coffre.ChargerAsync();
        await coffre.ConfigurerProtectionAsync("capture-demo");
        var documents = new (string Nom, CategorieDocument Categorie, string Fichier, int Taille, DateOnly? Date, DateOnly? Echeance, string? Charge, bool Protege)[]
        {
            ("Carte d'identité", CategorieDocument.Identite, "cni-recto-verso.pdf", 412_000, new(2019, 3, 2), new(2034, 3, 2), null, true),
            ("Passeport", CategorieDocument.Identite, "passeport.jpg", 1_830_000, new(2016, 11, 10), new(2026, 11, 10), null, true),
            ("RIB compte courant", CategorieDocument.Banque, "rib.pdf", 48_000, new(2024, 1, 15), null, null, false),
            ("Bail de l'appartement", CategorieDocument.Logement, "bail.pdf", 2_600_000, new(2022, 9, 1), null, null, false),
            ("Assurance voiture", CategorieDocument.Vehicule, "attestation-assurance.pdf", 156_000, new(2025, 11, 27), new(2026, 11, 27), "Assurance Voiture", false),
            ("Contrôle technique", CategorieDocument.Vehicule, "controle-technique.pdf", 230_000, new(2024, 12, 20), new(2026, 12, 20), null, false),
            ("Attestation mutuelle", CategorieDocument.Sante, "mutuelle-2026.pdf", 95_000, new(2026, 1, 5), new(2027, 1, 1), null, false),
            ("Avis d'impôt 2026", CategorieDocument.Impots, "avis-impot-2026.pdf", 310_000, new(2026, 8, 28), null, null, false),
            ("Facture lave-linge", CategorieDocument.Garanties, "facture-lave-linge.pdf", 120_000, new(2025, 6, 12), new(2027, 6, 12), null, false),
        };
        foreach (var d in documents)
        {
            var document = await coffre.AjouterAsync(new DocumentImportant
            {
                Nom = d.Nom, Categorie = d.Categorie, NomFichier = d.Fichier, Date = d.Date, Echeance = d.Echeance, Charge = d.Charge,
                RappelJours = d.Categorie == CategorieDocument.Vehicule ? 45 : 30,
                Notes = d.Nom == "Assurance voiture" ? "Contrat n° 0000-DEMO, renouvellement automatique" : "",
            }, new byte[d.Taille]);
            if (d.Protege)
                await coffre.ProtegerAsync(document, true);
        }
    }

    private static async Task MettreEnPage(FrameworkElement element, double hauteur)
    {
        for (var passe = 0; passe < 3; passe++)
        {
            element.Measure(new Size(Largeur, hauteur));
            element.Arrange(new Rect(0, 0, Largeur, hauteur));
            element.UpdateLayout();
            await Task.Delay(250);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
    }

    private static void Enregistrer(FrameworkElement element, string chemin, double hauteur)
    {
        var image = new RenderTargetBitmap((int)Largeur, (int)hauteur, 96, 96, PixelFormats.Pbgra32);
        image.Render(element);

        var encodeur = new PngBitmapEncoder();
        encodeur.Frames.Add(BitmapFrame.Create(image));
        using var flux = File.Create(chemin);
        encodeur.Save(flux);
    }
}
