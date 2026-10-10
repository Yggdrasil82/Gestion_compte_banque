# Gestion Compte Banque

Application Windows (WPF / .NET 8) de budget prévisionnel mensuel, reprenant le fonctionnement
du classeur Excel d'origine.

## Structure

| Dossier | Contenu |
|---|---|
| `src/GestionCompte.Core` | Moteur de calcul : configuration, mois, soldes, enveloppes, comptes cumulés |
| `src/GestionCompte.Data` | Enregistrement dans un fichier SQLite (par défaut `Documents\GestionCompte\compte.db`) et sauvegarde |
| `src/GestionCompte.Presentation` | Logique des écrans (navigation, saisie, enregistrement automatique), testable sans Windows |
| `src/GestionCompte.App` | Interface Windows WPF (fenêtres, tableaux) |
| `tests/GestionCompte.Tests` | Tests automatiques des calculs (reproduisent les chiffres d'Octobre 2026 du fichier Excel) |

## Règles de calcul

- **Solde de départ** du mois = ancien solde (solde de fin du mois précédent) + revenus du mois.
- **Enveloppes** (courses, carburant…) : réservent la partie du budget pas encore dépensée
  (`max(0, budget − dépenses de l'enveloppe)`) ; les dépenses saisies sont débitées normalement.
- **Solde courant** ligne par ligne : `solde précédent − débit + crédit`.
- **Comptes cumulés** (épargne, remboursements) : somme des débits des opérations liées, de mois en mois,
  avec un reste à atteindre si un objectif est défini.
- **Prévisionnel** : les mois suivant le dernier mois créé sont simulés avec la configuration (revenus habituels,
  charges, budgets complets des enveloppes) et les opérations ponctuelles prévues ; ces dernières sont ajoutées
  au mois lors de sa création. La date d'atteinte des objectifs des comptes cumulés est cherchée sur 10 ans.
- **Aide au budget** : analyse des charges (coût annuel, familles de charges), répartition 50/30/20
  (catégories Essentiel / Confort / Épargne), suivi réel des enveloppes sur les derniers mois terminés,
  simulateur « Et si… ? », objectifs d'épargne (mensualité et faisabilité selon l'excédent prévu) et alertes.
  Tous les calculs sont faits sur le PC, sans service extérieur.
- **Import des relevés (OFX)** : le fichier est lu sur le PC uniquement. Chaque opération bancaire est rapprochée
  d'une opération prévue du mois (même montant ou libellé proche, montant corrigé si besoin) ou d'un revenu,
  puis pointée ; les autres sont ajoutées, rangées dans une enveloppe grâce aux règles de classement
  (mot-clé du libellé → enveloppe). Les opérations déjà importées (identifiant FITID) sont ignorées, les mois
  manquants sont créés après confirmation, et le solde de la banque est comparé au solde pointé.
  **Ne jamais ajouter de vrai relevé dans ce dépôt (public).**

## Versions

- **1.0.0** : mois, prévisionnel, aide au budget, apparences.
- **1.1.0** : import des relevés bancaires OFX.
- **1.1.1** : import — solde comparé à la date du relevé, revenus « reçus » corrigés pour les mois pas encore commencés, message quand il n'y a rien à importer.
- **1.1.2** : fréquence des charges (tous les 2, 3, 6 ou 12 mois) : ajoutées seulement les mois concernés, coût ramené au mois dans l'aide au budget.
- **1.2.0** : réinitialisation (effacer les mois ou tout effacer pour une nouvelle personne), configuration vierge au premier lancement, données d'exemple anonymes.
- **1.3.0** : plusieurs comptes (un fichier par compte, configuration copiable), vue d'ensemble de tous les comptes, contrôle du numéro de compte à l'import.
- **1.3.1** : objectifs d'épargne calculés à partir du mois suivant le mois en cours ; un objectif peut être alimenté par un compte cumulé (ex. Économie) : déjà épargné et montants prévus comptés automatiquement.
- **1.3.2** : objectifs d'épargne déplaçables (Monter / Descendre) pour changer leur priorité.
- **1.3.3** : choix du mois dans une liste déroulante et bouton « Aujourd'hui » pour revenir au mois en cours.
- **1.4.0** : simulation de crédit (immobilier, auto / moto, consommation) dans l'aide au budget : plusieurs simulations comparées, tableau d'amortissement, taux d'endettement, calcul inverse (combien emprunter), ajout des échéances au prévisionnel, export Excel.
- **1.4.1** : enveloppe d'une opération remplie d'après son libellé (nom de l'enveloppe ou règle de classement) ; retouches de la simulation de crédit.
- **1.4.2** : correction du plantage au choix d'un mois dans la liste déroulante.
- **1.4.3** : nouvelle icône (canard grippe-sou sur son coffre-fort).
- **1.5.0** : la simulation de crédit devient le module « Crédits », un onglet à part qu'on peut masquer dans la configuration (réglage propre au PC).
- **2.0.0** : animation du canard à l'ouverture (désactivable) et nouveaux modules masquables dans Configuration › Modules :
  - « Bilan » : bilan d'une année ou des 12 derniers mois, graphique mois par mois, postes comparés aux mêmes mois un an plus tôt, pistes d'économie chiffrées, exports Excel et PDF ;
  - « Documents » : documents importants communs à tous les comptes (catégories, échéances avec rappels, charge liée, notes), rangés sur le PC, dans un dossier synchronisé ou directement dans Google Drive ; protection facultative par mot de passe avec clé de secours ;
  - « Mail » : envoi par Gmail (compte Google) ou par une autre messagerie (SMTP : Orange, Free…), pièces jointes tirées du coffre, du bilan (PDF) ou d'un fichier, carnet d'adresses (manuel et contacts Google) et historique des envois ;
  - « Achats » : recherche du meilleur prix par Gemini et Mistral (offres regroupées, triées, prix relus sur les pages), liste de sites marchands modifiable, suivi du prix de produits avec prix cible, et « Prévoir l'achat » dans le prévisionnel ;
  - « Lettres » : lettres types (résiliation, contestation de frais, réclamation, garantie, délai de paiement…) ou libres, une version proposée par Gemini et une par Mistral, relues puis enregistrées en PDF ou Word ou envoyées par mail ;
  - « Assistant » : questions sur le budget posées à Gemini et/ou Mistral à partir d'un résumé des chiffres affiché avant l'envoi ; il ne modifie rien.
- **2.1.0** : dans « Crédits », « Chercher les taux du moment » : Gemini et Mistral cherchent sur internet les taux bas, moyen et haut, le taux d'usure et le taux de l'assurance emprunteur pour le type et la durée du crédit (sources cliquables) ; « Reprendre le taux moyen » et alerte si la simulation dépasse le taux d'usure.
- **2.1.1** : Gemini utilise par défaut Flash-Lite (environ 500 demandes gratuites par jour au lieu d'une vingtaine) ; Mistral patiente 2 secondes et réessaie seul s'il répond « trop de demandes » ; les messages de quota donnent l'heure de remise à zéro (Gemini : 9 h, heure de Paris).
- **2.1.2** : les messages d'erreur des IA donnent la raison exacte de leur refus ; si la limite par minute est atteinte, l'IA patiente la minute (en l'annonçant) puis réessaie ; si la clé gratuite refuse la recherche internet, les taux sont donnés de mémoire avec la mention « taux indicatifs, non vérifiés sur internet » (les Achats, eux, ne proposent jamais d'offres sans recherche).
- **2.1.3** : Mistral reçoit une longueur maximale de réponse (sans elle, la limite gratuite de tokens par minute refusait chaque demande) ; sans recherche internet, l'IA donne des taux approximatifs marqués « indicatifs » ; dans Achats, Gemini explique que la recherche internet n'est pas incluse dans sa clé gratuite.
- **2.2.0** : Groq remplace Mistral (dont les clés API ne fonctionnent plus avec l'offre gratuite) : clé gratuite sans carte bancaire, modèle `openai/gpt-oss-120b` par défaut, pour l'Assistant, les Lettres et les taux indicatifs ; Groq ne cherche pas sur internet et ne sert donc pas dans Achats. Les anciennes clés Mistral sont effacées. Quand les taux sont indicatifs, le message l'indique au lieu de « trouvés sur internet ».
- **2.2.1** : sans recherche internet, les IA doivent toujours donner un chiffre approximatif (jamais « null ») et Groq répond obligatoirement en JSON ; quand une IA ne donne pas de taux, le message montre le début de sa réponse.
- **2.2.2** : connexion Google : une connexion ouverte à l'avance par le navigateur ne bloque plus le retour de Google (la connexion échouait après 5 minutes d'attente).
- **2.2.3** : connexion Google : le code renvoyé par Google est enregistré dès son arrivée, même si le navigateur coupe la page (la connexion échouait encore après 5 minutes).
- **2.2.4** : connexion Google : la carte « Compte Google » affiche chaque étape de la connexion, et un échec indique l'étape, la vraie raison et le temps écoulé.
- **2.3.0** : l'identifiant d'application Google peut être intégré à l'exe par la compilation GitHub (secrets du dépôt GOOGLE_CLIENT_ID et GOOGLE_CLIENT_SECRET, chiffrés dans l'exe, jamais dans le code) : « Connecter » ouvre alors directement Google. Mail : sélection de plusieurs contacts (Ctrl/Maj + clic) pour écrire ou supprimer, et bouton « Vider » du carnet.
- **2.4.0** : Groq cherche sur internet (outil « browser_search » des modèles GPT-OSS) pour les offres d'Achats et les taux du moment ; si la clé refuse la recherche, les taux sont donnés de mémoire (indicatifs) et Achats explique le refus.
- **2.4.1** : l'application s'ouvre sur le mois en cours, y compris après un import. Import d'un relevé qui chevauche deux mois : une opération datée d'un mois antérieur au mois en cours est rangée par défaut dans le mois en cours, avec l'alerte orange « Date antérieure au mois » ; la colonne « Mois » permet de la ranger dans le mois de sa date, et un bouton le fait pour toutes. Si le même montant existe déjà dans l'application (même avec un autre libellé), la ligne y est rattachée en statut « À vérifier » avec la question affichée dessous (ou décochée si ce montant est déjà pointé à la main dans le mois de sa date), et « Valider l'import » demande confirmation.
- **2.4.2** : premier relevé qui chevauche le premier mois géré : les opérations du mois juste avant le mois en cours sont importées dans le mois en cours (alerte « Date antérieure au mois », même montant « À vérifier ») au lieu d'être ignorées ; les mois plus anciens restent ignorés.
- **2.5.0** : catégories d'opérations (Configuration : nom et couleur, ex. « Agen ») choisies à la main dans le mois, ou données à une charge de la Configuration (reprise dans chaque nouveau mois) ; les opérations sont rangées par catégorie avec un bandeau de couleur et le total de chaque catégorie (case « Ranger par catégorie » pour revenir à l'ordre de saisie), le solde ligne par ligne suit l'ordre affiché ; les opérations pointées (à la main ou par l'import) sont surlignées en vert. Données au format 9.
- **2.6.0** : module « Prêts » (activable dans Configuration › Modules) : plusieurs prêts en cours (immobilier, PTZ…) à paliers et différé, assurance sur le capital restant dû, le montant emprunté ou fixe ; tableau d'amortissement calculé au centime avec le mois en cours surligné et le détail de chaque calcul en infobulle ; capital restant dû, intérêts restants et coût total ; simulation de remboursement anticipé avec les règles de l'offre (indemnité de 6 mois d'intérêts plafonnée à 3 %, exonération après N ans sauf rachat, minimum, réduction de durée seule avant le dernier palier) comparant « réduire la durée » et « réduire la mensualité » ; export Excel ; échéance ajoutée aux charges en un clic. Données au format 10.
- **2.7.0** : virements liés entre comptes : colonne « Virement avec » dans le mois et dans les charges de la Configuration ; l'opération inverse est créée dans l'autre compte (mois créé au besoin) et suit les modifications et suppressions, d'un côté comme de l'autre ; pour une charge, la charge inverse est ajoutée à la configuration de l'autre compte. Manuel d'utilisation intégré (onglet « Aide ») avec recherche sans accents. Données au format 11.
- **2.8.0** : mises à jour automatiques : à l'ouverture, et avec le bouton « Rechercher une mise à jour » (Configuration › Mises à jour), l'application cherche une version plus récente sur GitHub ; ses nouveautés s'affichent, puis elle se télécharge, remplace l'exe et redémarre après votre accord ; les données ne sont pas modifiées ; nouveau module « Bourse » (activable) : import de l'export CSV de Trade Republic (PEA et compte-titres, sans doublon), titres au prix moyen pondéré, plus-values latentes et réalisées, dividendes, frais, répartition ; cours sur Yahoo Finance ou saisis à la main ; valeur des placements dans la Vue d'ensemble ; données au format 12.
- **2.9.0** : données dans Google Drive (Configuration › Stockage des données) : tout est chiffré (AES-256) avec votre mot de passe, avec une clé de secours, puis rangé dans le dossier « Mon Budget - Données » ; à l'ouverture, connexion Google et mot de passe, puis envoi à chaque modification et à la fermeture ; prévient si l'application est déjà ouverte sur un autre PC ; PC de confiance : connexion gardée et lecture seule sans internet ; sur un autre PC, rien ne reste à la fermeture ; « Préparer une clé USB » pour lancer l'application de n'importe où ; les clés des IA voyagent avec les données ; prêts : durée modifiable (« 25 ans » ou 300 mois), le dernier palier est allongé et son échéance recalculée ; petits écrans : la barre de gauche défile.
- **2.9.1** : connexions internet plus sûres (adresse IPv4 essayée d'abord : plus d'attente de 2 minutes sur certains réseaux) ; carte Stockage des données : étape en cours affichée et cause exacte de toute erreur.
- **2.9.2** : démarrage noté dans un journal (%LOCALAPPDATA%\GestionCompte\demarrage.log) pour comprendre une fenêtre qui ne s'ouvre pas ; temps de lancement (clé USB, antivirus) et étapes de la connexion Google et de l'ouverture des données notés avec leur heure ; erreur affichée au lieu d'un démarrage silencieux ; l'application s'ouvre même si l'animation du canard reste bloquée.

## Prochainement

- Rien pour l'instant.

## Télécharger l'application

À chaque envoi de code, GitHub Actions compile l'application sous Windows, lance les tests et prend des
captures d'écran. Le fichier `GestionCompte.exe` (aucune installation nécessaire) se télécharge dans l'onglet
**Actions** du dépôt → dernière exécution réussie → section **Artifacts** → `GestionCompte-windows`.

Les données sont enregistrées automatiquement dans `Documents\GestionCompte\compte.db` (un fichier `compte-2.db`, `compte-3.db`… par compte supplémentaire, listés dans `comptes.json`).

## Documents, mails et compte Google

Le module « Documents » range les fichiers dans `Documents\GestionCompte\Documents` (par défaut), dans un dossier
choisi (par exemple un dossier de « Mon Drive » synchronisé par l'application Google Drive pour ordinateur), ou
directement dans Google Drive (dossier « Gestion compte - Documents »).

Pour la connexion au compte Google (Drive, Gmail, contacts : Configuration › Compte Google), chacun crée une fois
son propre identifiant d'application Google (gratuit) :

1. [Console Google Cloud](https://console.cloud.google.com/) : créer un projet.
2. « API et services » › « Bibliothèque » : activer **Google Drive API**, **Gmail API** et **People API**.
3. « Google Auth Platform » : type « Externe », puis dans « Audience », **Publier l'application**
   (sinon la connexion est à refaire tous les 7 jours).
4. « Clients » › « Créer un client » : type **Application de bureau**.
5. Dans l'application, « Connecter… » : coller l'ID client et le code secret, puis autoriser l'accès dans le navigateur
   (« Paramètres avancés » › « Accéder à… » si Google signale une application non validée : c'est la vôtre).

Droits demandés : dans Drive, seulement les fichiers créés par l'application (`drive.file`) ; dans Gmail, l'envoi
seul (`gmail.send`, pas de lecture de la boîte) ; lecture des contacts. L'identifiant et la connexion
restent sur le PC, chiffrés par Windows (`%LOCALAPPDATA%\GestionCompte\secrets`), comme le mot de passe
de la messagerie (SMTP) ; rien n'est enregistré dans ce dépôt. Le carnet d'adresses et l'historique des envois
sont dans `Documents\GestionCompte\mail.json`.

### IA gratuites (Gemini et Groq)

Dans Configuration › Intelligence artificielle, chacun saisit ses propres clés gratuites :
[Google AI Studio](https://aistudio.google.com/apikey) pour Gemini et [GroqCloud](https://console.groq.com/keys) pour Groq.
Avec les clés gratuites, aucune des deux ne cherche sur internet (Google réserve la recherche aux clés Gemini payantes) :
les taux de crédit sont alors donnés de mémoire et marqués « indicatifs », et la recherche de prix demande une clé Gemini payante.
Elles restent sur le PC, chiffrées par Windows. Pour une recherche de prix, seul le nom du produit est envoyé ; pour les taux de crédit, seulement le type, la durée et la tranche de montant (celle de la Banque de France) ;
une case coupe tout envoi aux IA. Les produits suivis et la liste des sites sont dans `Documents\GestionCompte\achats.json`.

Pour une lettre, seules les informations saisies (organisme, numéro de contrat…) partent aux IA : le nom, l'adresse
et les coordonnées sont ajoutés par l'application (`Documents\GestionCompte\lettres.json`, avec les lettres gardées).
L'assistant reçoit la question et, si la case est cochée, le résumé des chiffres affiché à l'écran (sans libellés
d'opérations, numéro de compte ni nom) ; la conversation n'est pas enregistrée.

Un document protégé est chiffré (AES-256) avec le mot de passe du coffre ; la clé de secours donnée à la création
du mot de passe permet d'en choisir un nouveau en cas d'oubli. Sans l'un ni l'autre, un document protégé est perdu.

## Développement

Outils gratuits : [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet test tests/GestionCompte.Tests
```

L'interface WPF (`src/GestionCompte.App`) ne se compile que sous Windows.
