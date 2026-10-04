using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GestionCompte.Core.Achats;
using GestionCompte.Core.Lettres;
using GestionCompte.Core.Documents;
using GestionCompte.Core.Mail;
using GestionCompte.Data;
using GestionCompte.Data.Achats;
using GestionCompte.Data.Lettres;
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
    private const double HauteurCredits = 1380;

    /// <summary>Le bilan : chiffres clés, graphique, pistes et tous les postes.</summary>
    private const double HauteurBilan = 1420;

    /// <summary>La configuration aussi : toutes ses cartes sur une seule image.</summary>
    private const double HauteurConfiguration = 2080;

    /// <summary>Mail : rédaction, carnet d'adresses et historique.</summary>
    private const double HauteurMail = 960;

    /// <summary>Module Prêts : liste, paliers, conditions, tableau d'amortissement et remboursement anticipé.</summary>
    private const double HauteurPrets = 2000;

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
        var delaiMaximum = new DispatcherTimer { Interval = TimeSpan.FromSeconds(180) };
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
        CreerAchatsDemo(dossierDemo);
        CreerLettresDemo(dossierDemo);
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
                MainViewModel.OngletAchats => HauteurMail,
                MainViewModel.OngletLettres => HauteurMail,
                MainViewModel.OngletAssistant => HauteurMail,
                MainViewModel.OngletPrets => HauteurPrets,
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
            // Écran d'accueil (état final de l'animation du canard).
            var accueil = new LogoAnime();
            accueil.AfficherFin();
            accueil.Measure(new Size(accueil.Width, accueil.Height));
            accueil.Arrange(new Rect(0, 0, accueil.Width, accueil.Height));
            accueil.UpdateLayout();
            await Task.Delay(250);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var imageAccueil = new RenderTargetBitmap((int)accueil.Width, (int)accueil.Height, 96, 96, PixelFormats.Pbgra32);
            imageAccueil.Render(accueil);
            var encodeurAccueil = new PngBitmapEncoder();
            encodeurAccueil.Frames.Add(BitmapFrame.Create(imageAccueil));
            using (var flux = File.Create(Path.Combine(dossier, "00-accueil.png")))
                encodeurAccueil.Save(flux);

            foreach (var etape in etapes)
                await Capturer(etape);

            // Prêts : le prêt lissé, avant le dernier palier (seule la durée peut baisser)…
            vm.Prets.Liste.Selection = vm.Prets.Liste.Elements[0];
            await Capturer(("31-prets-ocean", Ambiance.Ocean, false, MainViewModel.OngletPrets, false));
            // … puis sans cette règle, pour comparer les deux options.
            vm.Prets.Selection!.DureeSeuleAvantDernierPalier = false;
            vm.Prets.MontantAnticipe = 30000m;
            await Capturer(("32-prets-pastel-sombre", Ambiance.Pastel, true, MainViewModel.OngletPrets, false));

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

            // Achats : offres d'exemple (aucune IA n'est appelée pendant les captures) et prix suivis.
            vm.Achats.Recherche = "Aspirateur balai sans fil X200";
            vm.Achats.Offres = OffresDemo().Select(o => new OffreViewModel(o)).ToList();
            vm.Achats.Resume = "5 offres (Gemini : 5), la moins chère : 219,99 € chez Boulanger.";
            await Capturer(("23-achats-ocean", Ambiance.Ocean, false, MainViewModel.OngletAchats, false));
            await Capturer(("24-achats-nuit-sombre", Ambiance.Nuit, true, MainViewModel.OngletAchats, false));

            // Lettres : deux versions d'exemple (aucune IA n'est appelée), la première gardée.
            vm.Lettres.ModeleChoisi = ModeleLettre.Liste[0];
            vm.Lettres.Champs[0].Valeur = "Box Internet Exemple";
            vm.Lettres.Champs[1].Valeur = "123456789";
            vm.Lettres.Champs[2].Valeur = "31 décembre 2026";
            vm.Lettres.Champs[3].Valeur = "déménagement";
            vm.Lettres.Versions = new[]
            {
                new VersionLettreViewModel("Gemini", "Résiliation de mon abonnement internet – contrat n° 123456789", LettreDemo, null),
                new VersionLettreViewModel("Groq", "Demande de résiliation – abonnement n° 123456789",
                    "Madame, Monsieur,\n\nPar la présente, je vous demande de bien vouloir résilier mon abonnement internet (contrat n° 123456789) à compter du 31 décembre 2026, en raison de mon déménagement.\n\nJe vous remercie de m'adresser une confirmation écrite de cette résiliation.\n\nJe vous prie d'agréer, Madame, Monsieur, mes salutations distinguées.", null),
            };
            vm.Lettres.ChoisirVersionCommand.Execute(vm.Lettres.Versions[0]);
            vm.Lettres.Destinataire = "Box Internet Exemple\nService résiliation\nTSA 12345\n75000 Paris";
            vm.Lettres.Statut = "Version de Gemini gardée : relisez-la, complétez le destinataire, puis exportez-la ou envoyez-la.";
            await Capturer(("25-lettres-ocean", Ambiance.Ocean, false, MainViewModel.OngletLettres, false));
            await Capturer(("26-lettres-pastel-sombre", Ambiance.Pastel, true, MainViewModel.OngletLettres, false));

            // Assistant : conversation d'exemple sur le compte joint (aucune IA n'est appelée).
            vm.Assistant.Conversation.Add(new MessageAssistantViewModel("Vous", "Où puis-je faire des économies ?", deVous: true));
            vm.Assistant.Conversation.Add(new MessageAssistantViewModel("Gemini",
                "D'après vos chiffres, trois pistes :\n• Carburant : l'enveloppe est dépassée ce mois-ci ; regrouper les trajets ou comparer les stations peut faire gagner 15 à 20 € par mois.\n• Abonnements : vérifiez ceux que vous utilisez peu (plateformes, mobile).\n• Courses : vous êtes sous le budget, gardez ce rythme et virez la différence sur votre épargne.", deVous: false));
            vm.Assistant.Conversation.Add(new MessageAssistantViewModel("Groq",
                "Votre taux d'épargne est correct. Le poste le plus élevé après le loyer est l'alimentation : fixer une liste et un jour de courses par semaine aide souvent à réduire de 5 à 10 %. Pensez aussi à renégocier votre assurance habitation à l'échéance.", deVous: false));
            await Capturer(("27-assistant-ocean", Ambiance.Ocean, false, MainViewModel.OngletAssistant, false));
            await Capturer(("28-assistant-nuit-sombre", Ambiance.Nuit, true, MainViewModel.OngletAssistant, false));

            // Crédits : taux du moment d'exemple (aucune IA n'est appelée) et alerte d'usure.
            vm.CompteActif = vm.NomsComptes[0];
            if (vm.Credits.Selection is null)
                vm.Credits.Liste.AjouterCommand.Execute(null);
            vm.Credits.Selection!.Type = CreditsViewModel.NomsTypes[0];
            vm.Credits.Selection.TauxAnnuel = 5.8m;
            vm.Credits.TauxTrouves = TauxDemo().Select(t => new TauxMarcheViewModel(t)).ToList();
            vm.Credits.CategorieCherchee = vm.Credits.CategorieSelection;
            vm.Credits.Usure = 5.87m;
            vm.Credits.StatutTaux = "Moyennes trouvées sur internet, à vérifier sur les sources : ce ne sont pas des offres de banque.";
            await Capturer(("29-credits-taux-ocean", Ambiance.Ocean, false, MainViewModel.OngletCredits, false));
            vm.Credits.ReprendreTauxCommand.Execute(vm.Credits.TauxTrouves[0]);
            await Capturer(("30-credits-taux-pastel-sombre", Ambiance.Pastel, true, MainViewModel.OngletCredits, false));

            Directory.Delete(dossierDemo, true);
            Directory.Delete(dossierReleve, true);
            app.Shutdown(0);
        });
    }

    private const string LettreDemo =
        "Madame, Monsieur,\n\nJe vous informe par la présente de ma décision de résilier mon abonnement internet, contrat n° 123456789, avec effet au 31 décembre 2026, en raison de mon déménagement.\n\n" +
        "Je vous remercie de bien vouloir procéder à la clôture de mon compte à cette date et de m'indiquer les modalités de restitution du matériel.\n\n" +
        "Dans l'attente de votre confirmation, je vous prie d'agréer, Madame, Monsieur, l'expression de mes salutations distinguées.";

    /// <summary>Coordonnées fictives et une lettre déjà gardée.</summary>
    private static void CreerLettresDemo(string dossier)
    {
        var fichier = new FichierLettres(dossier);
        fichier.Charger();
        fichier.Coordonnees.Nom = "Camille Martin";
        fichier.Coordonnees.Adresse = "12 rue des Exemples\n69000 Lyon";
        fichier.Coordonnees.Telephone = "06 00 00 00 00";
        fichier.Coordonnees.Email = "camille.martin@exemple.fr";
        fichier.Coordonnees.Ville = "Lyon";
        fichier.Lettres.Add(new Lettre
        {
            Date = new DateTime(2026, 10, 3),
            Modele = "Contestation de frais bancaires",
            Destinataire = "Banque Exemple\nAgence centrale",
            Objet = "Contestation de frais d'incident",
            Corps = "Madame, Monsieur,\n\n…",
        });
        fichier.Enregistrer();
    }

    private static IEnumerable<Core.Calculs.TauxMarche> TauxDemo()
    {
        var gemini = new Core.Calculs.TauxMarche { Source = "Gemini", Bas = 3.05m, Moyen = 3.40m, Haut = 3.90m, Usure = 5.87m, Assurance = 0.28m, Periode = "novembre 2026" };
        gemini.Liens.Add(new Core.Calculs.SourceTaux("Courtier exemple", "https://www.courtier-exemple.fr/barometre"));
        gemini.Liens.Add(new Core.Calculs.SourceTaux("Taux d'usure (exemple)", "https://www.exemple.fr/taux-usure"));
        yield return gemini;
        // Exemple de réponse de mémoire (recherche refusée par la clé) : pas de sources, taux indicatifs.
        yield return new Core.Calculs.TauxMarche { Source = "Groq", Bas = 3.15m, Moyen = 3.50m, Haut = 4.00m, Usure = 5.87m, Assurance = 0.30m, Periode = "novembre 2026", SansRecherche = true };
    }

    private static void CreerAchatsDemo(string dossier)
    {
        var fichier = new FichierAchats(dossier);
        var aujourdhui = DateTime.Today;
        ProduitSuivi Produit(string nom, string adresse, decimal? cible, params decimal[] prix)
        {
            var produit = new ProduitSuivi { Nom = nom, Adresse = adresse, PrixCible = cible };
            for (var i = 0; i < prix.Length; i++)
                produit.Noter(new RelevePrix { Date = aujourdhui.AddDays(i - prix.Length + 1), Prix = prix[i] });
            return produit;
        }
        fichier.Produits.Add(Produit("Aspirateur balai sans fil X200 (Boulanger)", "https://www.boulanger.com/ref/exemple-x200", 230m, 259.99m, 239.99m, 219.99m));
        fichier.Produits.Add(Produit("Lave-linge 9 kg classe A (Darty)", "https://www.darty.com/nav/achat/exemple-lave-linge", 450m, 499m, 499m));
        fichier.Produits.Add(Produit("Vélo électrique ville (Decathlon)", "https://www.decathlon.fr/p/exemple-velo", null, 1299m, 1349m));
        fichier.Enregistrer();
    }

    private static IEnumerable<OffreTrouvee> OffresDemo()
    {
        OffreTrouvee Offre(string site, decimal prix, string adresse, VerificationOffre verification, string? remarque, params SourceOffre[] sources)
        {
            var offre = new OffreTrouvee { Site = site, Titre = "Aspirateur balai sans fil X200", Prix = prix, Adresse = adresse, Verification = verification, Remarque = remarque };
            offre.Sources.UnionWith(sources);
            return offre;
        }
        yield return Offre("Boulanger", 219.99m, "https://www.boulanger.com/ref/exemple-x200", VerificationOffre.Verifie, "Livraison gratuite, retrait en magasin", SourceOffre.Gemini);
        yield return Offre("Cdiscount", 224.90m, "https://www.cdiscount.com/exemple-x200", VerificationOffre.Corrige, "Vendu par un vendeur partenaire", SourceOffre.Gemini);
        yield return Offre("Fnac", 229.99m, "https://www.fnac.com/exemple-x200", VerificationOffre.Verifie, null, SourceOffre.Gemini);
        yield return Offre("Amazon", 234.00m, "https://www.amazon.fr/dp/EXEMPLE", VerificationOffre.AVerifier, "Prix non relu (site protégé)", SourceOffre.Gemini);
        yield return Offre("Darty", 249.99m, "https://www.darty.com/exemple-x200", VerificationOffre.Verifie, "Garantie 2 ans + extension possible", SourceOffre.Gemini);
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
