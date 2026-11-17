# Plan de lancement — faire connaître l'app (100 % gratuite)

Objectif : passer de « produit fini » à « premiers utilisateurs », avec le minimum de frais
fixes. **L'app est entièrement gratuite** : l'édition Pro, les clés de licence, l'essai
7 jours et le générateur de clés ont été retirés. Ce plan ne parle donc plus de ventes ni
de livraison de clés — seulement de distribution, et d'un soutien volontaire.

## Le funnel (déjà branché dans le code)

```
TikTok / Reddit / X / Discord
        │
        ▼
Landing (marketing/landing.html, hébergée gratuitement)
        │
        ▼
Gumroad « gratuit » (0 € + montant libre)
installe l'app, capture l'email
        │
        ▼
Dans l'app : tout est débloqué. Le bouton doré « Soutenir ONYX »
de l'accueil ouvre la boutique — il ne débloque rien.
```

Les adresses sont déjà câblées dans la landing **et** dans l'app (`PageDashboard.SupportUrl`) :

- Téléchargement : `https://fluide.gumroad.com/l/gratuit`
- Soutien (bouton de l'app + carte « Soutenir » de la landing) : `https://fluide.gumroad.com`

⚠️ Elles supposent le **nom d'utilisateur Gumroad `fluide`** et ce **permalien exact**.
Si tu choisis autre chose, dis-le-moi : je mets à jour le code et la landing (2 minutes).

⚠️ **À faire côté Gumroad (je ne peux pas le faire à ta place)** : dépublier les produits
`annuel` et `pro` s'ils existent. Le lien de soutien ouvre la boutique entière : tant
qu'ils sont en ligne, un visiteur y verra une édition Pro qui n'existe plus. Si des
personnes l'ont déjà achetée, le geste à leur égard (remboursement, mot de remerciement)
est ta décision — dans l'app, elles gardent tout, comme tout le monde.

## Étape 0 — Légal (gratuit, à lancer en parallèle, ~30 min + quelques jours de délai)

- Déclare une **micro-entreprise** sur `autoentrepreneur.urssaf.fr` (activité : édition de
  logiciels). C'est gratuit et obligatoire pour encaisser régulièrement — ça reste vrai
  pour des soutiens volontaires.
- Gumroad vend en son nom (« merchant of record ») et collecte la TVA UE des acheteurs à ta
  place ; toi, tu déclares simplement ton chiffre d'affaires à l'URSSAF (~21-25 % de
  cotisations). Vérifie ces points au moment de l'inscription — je ne suis pas juriste.
- Ta propre checklist `seller/README.md` reste valable : EULA relue, pas de binaire tiers
  redistribué (l'app installe via winget → a priori OK, à confirmer avant le 1er post).

## Étape 1 — Compte Gumroad (~30 min, à faire par toi)

1. Crée le compte sur gumroad.com (je ne peux pas créer de comptes à ta place).
2. Nom d'utilisateur : **fluide** — s'il est pris, prends **fluidegg** et dis-le-moi :
   je re-câble l'app, la landing et le kit en 2 minutes.
3. Renseigne le versement bancaire (payouts) et vérifie l'identité si demandé.

## Étape 2 — Le produit (~20 min)

Texte prêt à coller dans `posts-lancement.md`, section « Fiche produit ».

| | Permalien | Prix | Type Gumroad | Contenu |
|---|---|---|---|---|
| Fluide (gratuit) | `gratuit` | 0 € (+ « pay what you want ») | Produit | Setup autonome (~37 Mo) + setup léger (~6 Mo) |

- Le « pay what you want » **est** le soutien : celui qui veut donner met un montant, les
  autres laissent 0 €. Aucune fonction n'en dépend.
- Ce produit n'est pas un détail : chaque téléchargement te donne un **email** à qui
  annoncer les mises à jour.

## Étape 3 — Construire le livrable — ✅ FAIT (22/07/2026)

- ✅ `AppURL` renseignée dans `installer/BTOptimizer.iss` (la boutique ; remplace-la par
  l'URL de la landing quand elle sera hébergée).
- ✅ Installateur AUTONOME construit : **`installer/Output/Fluide-Setup-14.32.0.0.exe`**
  (~40 Mo — identité Fluide complète : nom, logo frametime, indigo `#818CF8`, animation
  du QG, panneau Discord). ⚠️ **Périmé** : cet installateur contient encore le verrou
  Pro. Reconstruis-en un depuis la version sans clé (`Build-Standalone.bat`) avant de
  l'uploader sur le produit Gumroad.
- ✅ Interface v14.32 validée par le harnais hors-écran (8 pages × 3 tailles, 40/40
  fenêtres du menu, 0 erreur) ; capture réelle du QG intégrée à la landing (`app.png`).
- 🔁 Avant chaque diffusion : vérifier que le compteur « Optimisations au total » du QG
  correspond au chiffre de la landing et des posts (actuellement **176**).
- ✅ Vérifié : **aucun binaire tiers embarqué** dans cet installateur (le composant NVIDIA
  optionnel est vide tant que `tools/npi/` n'existe pas) — case juridique de la checklist réglée.
- Reste (optionnel) : build léger ~6 Mo (`Build-Installer.bat`) si tu veux offrir cette
  variante.

## Étape 4 — Héberger la landing (gratuit, ~15 min)

- ✅ Dossier prêt à déposer : **`marketing/site/`** (contient `index.html`). Le plus
  simple : **Netlify Drop** (app.netlify.com/drop) — glisse-dépose ce dossier, tu
  obtiens une URL HTTPS immédiatement.
- Alternative : GitHub Pages (repo public séparé, juste le fichier).
- Reporte cette URL dans `installer/BTOptimizer.iss` (`AppURL`) et sur tes profils sociaux.

## Étape 5 — Distribution J1 → J7 (c'est ici que l'adoption se décide)

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
- **Semaine 2-3** : compte développeur **Microsoft Store** (19 $ une seule fois) pour y
  lister l'app — le Store règle la confiance et ajoute de la visibilité. Le certificat de
  signature OV (~250-400 €/an) n'est plus financé par des ventes : c'est un coût à
  décider selon les soutiens reçus.

## Concurrence — FPSDoctor (seul concurrent direct identifié)

Relevé sur fpsdoctor.com le 22/07/2026 (à re-vérifier avant d'utiliser les chiffres) :

| | FPSDoctor | Fluide |
|---|---|---|
| Gratuit | 13 optimisations, analyse de base | **Tout** : 176 optimisations, diagnostic, 1-clic adapté au matériel, presets, mode jeu |
| Payant | 59 €/an (abonnement) · 150 € à vie | **Rien** — pas d'édition payante |
| Preuves | Témoignages « +380 FPS », « 3x stabilité garantie » | **Mesure avant/après sur TON PC** (FPS, latence, score /100) |
| Anticheat / réversibilité | non mentionnés sur le site | cœur du produit (zéro injection, tout réversible, sauvegardes) |

Ce que ça t'apprend :

- **Le marché existe** : 7 000+ utilisateurs revendiqués, en France, à 59 €/an. Ton
  argument n'est plus « moins cher », c'est « gratuit, en entier ».
- **Leur force n'est pas le produit, c'est la distribution** (communauté Discord, TikTok,
  témoignages). C'est exactement ce que ton calendrier J1→J7 doit construire — le produit
  seul ne suffira pas.
- **Ton angle gagnant** : eux promettent des chiffres invérifiables ; toi tu mesures.
  Eux ne parlent ni d'anticheat ni de retour arrière ; chez toi c'est structurel.
  Ne les attaque jamais nommément en public — compare de façon générique et factuelle
  (« les optimiseurs du marché… »), et laisse les joueurs faire le rapprochement.
- **Prix (ta décision, appliquée)** : gratuit, sans édition payante. Celui qui compare
  voit « ce qu'ils vendent sur abonnement, à 0 € ». Contrepartie honnête : plus aucun
  revenu direct — le projet ne se finance que par des soutiens volontaires — et leur
  preuve sociale (7 000 joueurs) pèse toujours ; tes mesures avant/après publiées sont
  ce qui compense au démarrage.

## Attentes réalistes (à relire les jours de doute)

- Il n'y a plus de ventes à compter. La mesure du lancement, c'est le nombre de
  **téléchargements** (les emails du produit Gumroad) et la qualité des retours.
- Les soutiens volontaires existent mais ne sont pas un revenu prévisible : ne budgète
  rien dessus (certificat de signature compris) avant d'avoir de vrais chiffres. Sur un
  soutien : − ~10 % Gumroad, puis ~21-25 % de cotisations.
- Le produit est bon et le funnel est branché ; la seule variable, c'est le nombre de
  personnes qui voient l'app chaque jour. La régularité bat l'intensité.

## Prochaines actions où je peux t'aider

Compte Gumroad créé ? Reviens me voir pour : page FAQ sur la landing,
fiche Microsoft Store, adaptation des posts selon les premiers retours, version anglaise.
