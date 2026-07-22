# Plan de lancement — vendre DesTinGOOD Pro

Objectif : passer de « produit fini » à « premières ventes », avec le minimum de frais
fixes. Réaliste, pas magique : les premières ventes arrivent en général **1 à 3 semaines**
après le début de la distribution, et elles dépendent surtout de la régularité des posts.

## Le funnel (déjà branché dans le code)

```
TikTok / Reddit / X / Discord
        │
        ▼
Landing (marketing/landing.html, hébergée gratuitement)
        │                         │
        ▼                         ▼
Gumroad « gratuit » (0 €)   Gumroad « pro » (19 €)
installe l'app, capture     paiement → tu génères la clé
l'email                     (seller/) → email au client
        │                         ▲
        ▼                         │
Dans l'app : fonction Pro cliquée → fenêtre Pro
→ essai 7 jours → lien « 🛒 Acheter la licence Pro » ──┘
```

Les adresses sont déjà câblées dans la landing **et** dans l'app (`LicenseKeyForm.BuyUrl`) :

- Produit gratuit : `https://destingood.gumroad.com/l/gratuit`
- Produit Pro : `https://destingood.gumroad.com/l/pro`

⚠️ Elles supposent le **nom d'utilisateur Gumroad `destingood`** et ces **permaliens exacts**.
Si tu choisis autre chose, dis-le-moi : je mets à jour le code et la landing (2 minutes).

## Étape 0 — Légal (gratuit, à lancer en parallèle, ~30 min + quelques jours de délai)

- Déclare une **micro-entreprise** sur `autoentrepreneur.urssaf.fr` (activité : édition de
  logiciels). C'est gratuit et obligatoire pour encaisser régulièrement.
- Gumroad vend en son nom (« merchant of record ») et collecte la TVA UE des acheteurs à ta
  place ; toi, tu déclares simplement ton chiffre d'affaires à l'URSSAF (~21-25 % de
  cotisations). Vérifie ces points au moment de l'inscription — je ne suis pas juriste.
- Ta propre checklist `seller/README.md` reste valable : EULA relue, pas de binaire tiers
  redistribué (l'app installe via winget → a priori OK, à confirmer avant le 1er post).

## Étape 1 — Compte Gumroad (~30 min, à faire par toi)

1. Crée le compte sur gumroad.com (je ne peux pas créer de comptes à ta place).
2. Nom d'utilisateur : **destingood** (voir plus haut).
3. Renseigne le versement bancaire (payouts) et vérifie l'identité si demandé.

## Étape 2 — Les deux produits (~45 min)

Textes prêts à coller dans `posts-lancement.md`, section « Fiches produit ».

| | Permalien | Prix | Contenu |
|---|---|---|---|
| DesTinGOOD (gratuit) | `gratuit` | 0 € (+ « pay what you want » activé) | Setup autonome (~37 Mo) + setup léger (~6 Mo) |
| DesTinGOOD Pro — licence à vie | `pro` | 19 € | Mêmes fichiers + note « clé envoyée par email sous 24 h » |

- Crée un **code promo `LANCEMENT`** (-30 %, valable 7 jours) plutôt que de baisser le prix :
  l'urgence est réelle et le prix affiché reste 19 €.
- Le produit gratuit n'est pas un détail : chaque téléchargement te donne un **email** à qui
  annoncer les mises à jour (et proposer Pro).

## Étape 3 — Construire le livrable (~30-60 min)

1. Renseigne `AppURL` dans `installer/BTOptimizer.iss` avec l'URL de ta landing (étape 5).
2. `Build-Standalone.bat` → puis compile `installer\BTOptimizer.iss` (Inno Setup / `ISCC.exe`).
3. Récupère le setup dans `installer\Output\` et uploade-le sur les **deux** produits Gumroad.
4. Refais un build léger (`Build-Installer.bat`) si tu veux aussi offrir la version ~6 Mo.
5. Teste : édition gratuite (fonctions Pro bien verrouillées), essai 7 jours, une clé de test.

## Étape 4 — Livrer une clé à chaque vente (2 min/vente)

À chaque email « Nouvelle vente » de Gumroad :

```
cd seller
dotnet run -- "Prénom Nom de l'acheteur"
```

→ réponds à l'acheteur avec la clé (modèle d'email dans `posts-lancement.md`).
**`seller/private.xml` est ton coffre-fort** : sauvegarde-le (clé USB + cloud chiffré) ;
s'il fuite, n'importe qui fabrique des clés ; si tu le perds, tu ne peux plus en émettre.
Quand les ventes deviennent régulières, reviens me voir : j'automatise la génération.

## Étape 5 — Héberger la landing (gratuit, ~15 min)

- Le plus simple : **Netlify Drop** (app.netlify.com/drop) — renomme `landing.html` en
  `index.html`, glisse-dépose, tu obtiens une URL en HTTPS immédiatement.
- Alternative : GitHub Pages (repo public séparé, juste le fichier).
- Reporte cette URL dans `installer/BTOptimizer.iss` (`AppURL`) et sur tes profils sociaux.

## Étape 6 — Distribution J1 → J7 (c'est ici que les ventes se décident)

Tous les textes sont prêts dans `posts-lancement.md`.

- **J1** : post « développeur indé » sur r/OptimizedGaming (EN) + thread X + 1er TikTok.
- **J2** : forum jeuxvideo.com (matériel/tech) + 1-2 serveurs Discord FR de jeux compétitifs
  (⚠️ uniquement dans les salons autorisant le partage de projets — lis les règles, sinon ban).
- **J3 → J7** : 1 TikTok par jour (3 scripts fournis, puis on itère sur ce qui marche),
  réponse à **tous** les commentaires, repost des meilleurs retours.
- Règle d'or : ton angle unique, c'est la **mesure honnête** (score /100, FPS/latence
  avant/après, rapport HTML). Jamais de promesse chiffrée (« +80 FPS ») — montre la mesure.

## L'objection n°1 : SmartScreen « éditeur inconnu »

- La landing l'assume déjà honnêtement (section Téléchargement) — ne l'esquive jamais.
- **Semaine 2-3, avec les premiers euros** : compte développeur **Microsoft Store**
  (19 $ une seule fois) et liste l'édition gratuite — le Store règle la confiance et ajoute
  de la visibilité, la clé Pro restant vendue sur Gumroad. Ensuite, certificat de signature
  OV (~250-400 €/an) quand le revenu le justifie.

## Concurrence — FPSDoctor (seul concurrent direct identifié)

Relevé sur fpsdoctor.com le 22/07/2026 (à re-vérifier avant d'utiliser les chiffres) :

| | FPSDoctor | DesTinGOOD |
|---|---|---|
| Gratuit | 13 optimisations, analyse de base | **173 optimisations** + toute la suite de diagnostic |
| Payant | 59 €/an (abonnement) · 150 € à vie | **19 € à vie**, pas d'abonnement |
| Preuves | Témoignages « +380 FPS », « 3x stabilité garantie » | **Mesure avant/après sur TON PC** (FPS, latence, score /100) |
| Anticheat / réversibilité | non mentionnés sur le site | cœur du produit (zéro injection, tout réversible, sauvegardes) |

Ce que ça t'apprend :

- **Le marché existe et paie** : 7 000+ utilisateurs revendiqués, en France, à 59 €/an.
  Tu n'es pas trop cher — tu es (très) sous le prix du marché.
- **Leur force n'est pas le produit, c'est la distribution** (communauté Discord, TikTok,
  témoignages). C'est exactement ce que ton calendrier J1→J7 doit construire — le produit
  seul ne suffira pas.
- **Ton angle gagnant** : eux promettent des chiffres invérifiables ; toi tu mesures.
  Eux ne parlent ni d'anticheat ni de retour arrière ; chez toi c'est structurel.
  Ne les attaque jamais nommément en public — compare de façon générique et factuelle
  (« les optimiseurs du marché… »), et laisse les joueurs faire le rapprochement.
- **Prix** : garde 19 € (prix de lancement, ~8× sous leur accès à vie). Quand tu dépasseras
  ~30 ventes/mois, tu pourras tester 29 € (toujours 5× moins cher) — ta décision.

## Attentes réalistes (à relire les jours de doute)

- Semaine 1 : 0 à 5 ventes. Mois 1 : 10 à 50 ventes **si** la distribution est tenue.
- Chaque vente : 19 € − ~10 % Gumroad ≈ 17 €, puis ~21-25 % de cotisations → **~13 € net**.
- Le produit est bon et le funnel est branché ; la seule variable, c'est le nombre de
  personnes qui voient l'app chaque jour. La régularité bat l'intensité.

## Prochaines actions où je peux t'aider

Compte Gumroad créé ? Reviens me voir pour : automatiser les clés, page FAQ sur la landing,
fiche Microsoft Store, adaptation des posts selon les premiers retours, version anglaise.
