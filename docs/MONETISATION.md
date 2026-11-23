# Monétisation ONYX

Le système est prêt mais **désactivé** dans `config/monetisation.json`. Aucun compte de paiement ou d’affiliation n’est connecté. Ajouter ce code ne produit pas de revenus à lui seul.

## Les trois possibilités

- **Partenaire direct** : une entreprise paie pour un emplacement dans Bons plans. ONYX affiche une carte « SPONSORISÉ · annonceur », avec lien et bouton de fermeture. Le contrat et le paiement se font avec le partenaire ; ONYX ne facture rien automatiquement.
- **Affiliation** : un réseau rémunère les achats attribués à tes liens. Les boutons concernés portent une mention « Lien affilié ». Les prix et le classement des offres ne changent pas.
- **Soutien volontaire** : ton lien public de paiement ajoute un bouton « Soutenir ONYX » dans Bons plans. Aucune fonction ne devient payante.

Pour démarrer, GOG propose https://affiliate.gog.com/ et Fanatical présente ses programmes sur https://support.fanatical.com/hc/en-us/articles/360001415777-Affiliate-Partner-FAQ. Déclare ONYX comme application Windows avec comparateur et demande l’accord pour ce mode de diffusion. L’acceptation, les commissions et les paiements dépendent du programme.

## Activation après obtention de tes liens

1. Ouvre le compte à ton nom auprès du service choisi et renseigne les informations de paiement directement chez lui.
2. Récupère un lien public de soutien ou un modèle de lien profond autorisé pour ton application. Ne mets aucune clé secrète, mot de passe ou donnée bancaire dans le dépôt.
3. Modifie `config/monetisation.json`, puis reconstruis ONYX. Le fichier est embarqué dans l’exécutable : pas de configuration distante, pas de téléchargement de publicité ni de modification cachée d’une version publiée.
4. Vérifie l’aperçu et les liens avec les outils de test officiels du réseau. Ne génère pas de clics ou achats artificiels.
5. Publie la nouvelle version. Les commissions validées se consultent dans le tableau de bord du partenaire.

Le lien Gumroad historique présent ailleurs dans le dépôt n’est pas réutilisé automatiquement : aucun lien de paiement n’a été confirmé pour ce système.

### Soutien

Active `enabled` et remplace `supportUrl: null` par **ton propre lien HTTPS public**. Tant que le lien manque, le bouton reste masqué.

### Partenaire direct

Dans `sponsor`, renseigne :

```json
{
  "id": "campagne-unique",
  "advertiser": "Nom de ton partenaire",
  "title": "Titre convenu avec le partenaire",
  "description": "Description courte de son offre",
  "url": "https://adresse-publique-du-partenaire.fr/offre",
  "startsUtc": "2027-01-01T00:00:00Z",
  "endsUtc": "2027-02-01T00:00:00Z"
}
```

C’est un exemple à remplacer, pas une campagne réelle. Une campagne nécessite des dates valides et un annonceur identifié. Elle ne s’affiche pas avant son début ni après sa fin. La date est vérifiée à l’ouverture de Bons plans, chaque minute pendant l’affichage et au clic. Fermer la carte la masque pour la session de cette page.

### Liens affiliés

Chaque entrée de `affiliateRules` contient :

- `enabled` : activation de cette règle, en plus de l’activation globale ;
- `merchantHost` : domaine exact de la boutique, par exemple `www.gog.com` ; les sous-domaines et domaines voisins ne correspondent pas automatiquement ;
- `urlTemplate` : le modèle HTTPS fourni par le réseau, avec **une seule** occurrence de `{url}` à l’emplacement du lien produit encodé.

Ne devine pas les paramètres d’un programme. Il faut un modèle qui accepte une URL de destination encodée dans un lien profond. ONYX remplace `{url}` par l’URL originale complète encodée, y compris sa requête. Un réseau qui utilise une autre méthode nécessite une intégration spécifique.

La règle s’applique seulement aux liens commerciaux explicites dans Bons plans et le comparateur. Elle ne s’applique pas à Actualités, aux sources ou aux liens d’historique. Les redirections CheapShark ne sont pas résolues pour injecter une affiliation ; n’ajoute pas de règle sur ce domaine. Steam n’a aucune affiliation configurée par défaut.

## Vérifications

```powershell
dotnet run --project tests/ONYX.Tests.csproj -c Release
dotnet build BTOptimizer.csproj -c Debug -o build/monetisation-ui-test -p:DefineConstants=BTTEST
$env:BT_UITEST='1'
$env:BT_MONET_UI='1'
$env:BT_UISHOT='E:\bt\build\monetisation-preview'
dotnet build/monetisation-ui-test/BTOptimizer.dll
```

Le harnais affiche des exemples explicitement marqués « DÉMONSTRATION », ne clique sur aucun lien et vérifie que la configuration livrée est toujours inactive. Les exemples de test ne sont pas des partenaires ni des revenus réels.

## Désactivation et confidentialité

`enabled: false` masque le partenaire et le soutien, et conserve les URL d’origine. Un JSON invalide, une version inconnue ou une URL non valide ne déclenche pas de monétisation.

ONYX n’ajoute aucun SDK publicitaire, pixel, profil utilisateur, télémétrie de clic, cookie publicitaire ou fenêtre intrusive. Le navigateur n’est ouvert qu’après un clic. Les sites visités et réseaux d’affiliation appliquent ensuite leurs propres pratiques de suivi. Aucun revenu estimé ou garanti n’est affiché dans ONYX.
