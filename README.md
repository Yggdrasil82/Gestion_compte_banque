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

## Télécharger l'application

À chaque envoi de code, GitHub Actions compile l'application sous Windows, lance les tests et prend des
captures d'écran. Le fichier `GestionCompte.exe` (aucune installation nécessaire) se télécharge dans l'onglet
**Actions** du dépôt → dernière exécution réussie → section **Artifacts** → `GestionCompte-windows`.

Les données sont enregistrées automatiquement dans `Documents\GestionCompte\compte.db`.

## Développement

Outils gratuits : [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet test tests/GestionCompte.Tests
```

L'interface WPF (`src/GestionCompte.App`) ne se compile que sous Windows.
