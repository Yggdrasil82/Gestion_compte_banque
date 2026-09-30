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

## Prochainement

- Module Bourse / Trade Republic (en attente).
- Virements liés entre comptes.

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

### IA gratuites (Gemini et Mistral)

Dans Configuration › Intelligence artificielle, chacun saisit ses propres clés gratuites :
[Google AI Studio](https://aistudio.google.com/apikey) pour Gemini et [Mistral](https://console.mistral.ai/api-keys).
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
