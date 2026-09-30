# Règles de confidentialité de Mon Budget

Dernière mise à jour : 30 septembre 2026.

Mon Budget est une application Windows de gestion de budget personnel. Elle fonctionne sur votre PC : il n'existe
aucun serveur Mon Budget, aucun compte Mon Budget, et le développeur ne reçoit ni ne collecte aucune donnée.

## Données enregistrées sur votre PC

Vos comptes, opérations, budgets, documents, lettres, contacts et réglages sont enregistrés uniquement sur votre PC
(dossier `Documents\GestionCompte` ou le dossier que vous avez choisi). Les clés d'IA et la connexion Google sont
chiffrées par Windows et ne quittent pas votre PC.

## Utilisation de votre compte Google

Si vous connectez votre compte Google (facultatif), l'application l'utilise seulement pour ce que vous demandez :

- **Google Drive** (`drive.file`) : enregistrer et relire les documents que vous déposez dans le coffre de
  l'application. Elle n'a accès qu'aux fichiers qu'elle a créés, pas au reste de votre Drive.
- **Gmail** (`gmail.send`) : envoyer les mails que vous écrivez dans l'application. Elle ne lit pas vos mails.
- **Contacts** (`contacts.readonly`, `contacts.other.readonly`) : proposer vos contacts comme destinataires.
  Elle ne les modifie pas.

Ces données passent directement entre votre PC et Google. Elles ne sont ni transmises à des tiers, ni utilisées pour
de la publicité, ni pour entraîner des modèles d'IA. L'utilisation des données reçues des API Google respecte la
[politique relative aux données utilisateur des services d'API Google](https://developers.google.com/terms/api-services-user-data-policy),
y compris les exigences d'utilisation limitée.

Vous pouvez déconnecter le compte à tout moment dans l'application (Configuration) ou sur
[myaccount.google.com/permissions](https://myaccount.google.com/permissions).

## Intelligences artificielles (facultatif)

Si vous saisissez une clé Gemini (Google) ou Groq, seul ce que vous demandez est envoyé à l'IA choisie :
le nom d'un produit pour une recherche de prix, la catégorie d'un crédit pour les taux, les informations d'une lettre
(sans vos nom, adresse ni coordonnées), ou votre question et, si vous le cochez, un résumé chiffré du budget
(sans libellés d'opérations ni numéro de compte). Une case permet de couper tout envoi aux IA.

## Contact

Pour toute question, ouvrez un ticket sur le dépôt
[github.com/Yggdrasil82/Gestion_compte_banque](https://github.com/Yggdrasil82/Gestion_compte_banque/issues).
