# Plan de lancement — vendre Fluide Pro

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
Gumroad « gratuit » (0 €)   Gumroad « annuel » (49 €/an) · « pro » (127 € à vie)
installe l'app, capture     paiement → tu génères la clé (seller/, avec ou sans
l'email                     durée) → email au client
        │                         ▲
        ▼                         │
Dans l'app : fonction Pro cliquée → fenêtre Pro
→ essai 7 jours → lien « 🛒 Acheter la licence Pro » ──┘
```

Les adresses sont déjà câblées dans la landing **et** dans l'app (`LicenseKeyForm.BuyUrl`) :

- Produit gratuit : `https://fluide.gumroad.com/l/gratuit`
- Abonnement annuel (49 €/an) : `https://fluide.gumroad.com/l/annuel`
- Licence à vie (127 €) : `https://fluide.gumroad.com/l/pro`
- Dans l'app, le lien d'achat ouvre la boutique entière : `https://fluide.gumroad.com`

⚠️ Elles supposent le **nom d'utilisateur Gumroad `fluide`** et ces **permaliens exacts**.
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
2. Nom d'utilisateur : **fluide** — s'il est pris, prends **fluidegg** et dis-le-moi :
   je re-câble l'app, la landing et le kit en 2 minutes.
3. Renseigne le versement bancaire (payouts) et vérifie l'identité si demandé.

## Étape 2 — Les deux produits (~45 min)

Textes prêts à coller dans `posts-lancement.md`, section « Fiches produit ».

| | Permalien | Prix | Type Gumroad | Contenu |
|---|---|---|---|---|
| Fluide (gratuit) | `gratuit` | 0 € (+ « pay what you want ») | Produit | Setup autonome (~37 Mo) + setup léger (~6 Mo) |
| Fluide Pro — Annuel | `annuel` | 49 €/an | **Membership** (facturation récurrente annuelle) | Mêmes fichiers + « clé envoyée sous 24 h, renouvelée à chaque échéance » |
| Fluide Pro — À Vie | `pro` | 127 € | Produit | Mêmes fichiers + « clé à vie envoyée sous 24 h » |

- Crée un **code promo `LANCEMENT`** (−30 %, valable 7 jours) plutôt que de baisser les
  prix : annuel à 34,30 €, à vie à 88,90 €, et les prix affichés restent 49 / 127 €.
  À ces tarifs alignés sur le marché, sans preuve sociale au début, ce code est ton
  vrai déclencheur des premières ventes.
- Le produit gratuit n'est pas un détail : chaque téléchargement te donne un **email** à qui
  annoncer les mises à jour (et proposer Pro).

## Étape 3 — Construire le livrable — ✅ FAIT (22/07/2026)

- ✅ `AppURL` renseignée dans `installer/BTOptimizer.iss` (la boutique ; remplace-la par
  l'URL de la landing quand elle sera hébergée).
- ✅ Installateur AUTONOME construit : **`installer/Output/BTOptimizer-Setup-14.28.0.0.exe`**
  (~38 Mo — nouvelle interface QG, boosters dynamiques, licences à expiration, correctif
  viseur) → c'est LE fichier à uploader sur les **trois** produits Gumroad.
- ✅ Interface v14.28 validée par le harnais hors-écran (8 pages × 3 tailles, 39/39
  fenêtres du menu, 0 erreur) ; capture réelle du QG intégrée à la landing (`app.png`).
- 🔁 Avant chaque diffusion : vérifier que le compteur « Optimisations au total » du QG
  correspond au chiffre de la landing et des posts (actuellement **176**).
- ✅ Vérifié : **aucun binaire tiers embarqué** dans cet installateur (le composant NVIDIA
  optionnel est vide tant que `tools/npi/` n'existe pas) — case juridique de la checklist réglée.
- ✅ Cycle des clés **testé automatiquement contre le vrai `License.cs`** (avec ta clé
  privée, jamais copiée) : clé à vie OK · clé 365 j OK (« expire 2027-07-22 ») · clé
  expirée refusée avec le bon message · clé à date falsifiée rejetée.
- Reste (optionnel) : build léger ~6 Mo (`Build-Installer.bat`) si tu veux offrir cette
  variante, et un test visuel dans l'app (colle une clé, vérifie « jusqu'au … »).

## Étape 4 — Livrer une clé à chaque vente (2 min/vente)

À chaque email « Nouvelle vente » de Gumroad :

```
cd seller
dotnet run -- "Prénom Nom"          # licence À VIE (produit « pro », 127 €)
dotnet run -- "Prénom Nom" 365      # ABONNEMENT annuel (produit « annuel », 49 €/an)
```

→ réponds à l'acheteur avec la clé (modèle d'email dans `posts-lancement.md`).
**Renouvellements** : à chaque échéance annuelle (email Gumroad « subscription renewed »),
génère une nouvelle clé `365` et envoie-la — l'ancienne expire toute seule, l'app affiche
un message clair et retombe en édition gratuite si le client ne renouvelle pas.
**`seller/private.xml` est ton coffre-fort** : sauvegarde-le (clé USB + cloud chiffré) ;
s'il fuite, n'importe qui fabrique des clés ; si tu le perds, tu ne peux plus en émettre.
Quand les ventes deviennent régulières, reviens me voir : j'automatise la génération.

## Étape 5 — Héberger la landing (gratuit, ~15 min)

- ✅ Dossier prêt à déposer : **`marketing/site/`** (contient `index.html`). Le plus
  simple : **Netlify Drop** (app.netlify.com/drop) — glisse-dépose ce dossier, tu
  obtiens une URL HTTPS immédiatement.
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

| | FPSDoctor | Fluide |
|---|---|---|
| Gratuit | 13 optimisations, analyse de base | **176 optimisations** + toute la suite de diagnostic |
| Payant | 59 €/an (abonnement) · 150 € à vie | **49 €/an · 127 € à vie** (mêmes paliers, −15 %) |
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
- **Prix (ta décision, appliquée)** : alignement sur leurs paliers à **−15 %** — 49 €/an
  vs 59, 127 € à vie vs 150. L'acheteur qui compare voit « pareil, moins cher — et
  l'édition gratuite est 13× plus généreuse ». Contrepartie honnête : à prix quasi égal,
  leur preuve sociale (7 000 joueurs) pèse lourd ; ton code `LANCEMENT` (−30 %) et tes
  mesures avant/après publiées sont ce qui compense au démarrage.

## Attentes réalistes (à relire les jours de doute)

- Semaine 1 : 0 à 3 ventes. Mois 1 : 5 à 30 ventes **si** la distribution est tenue — un
  prix aligné marché convertit moins vite qu'un prix cassé, mais rapporte 6 à 7× par vente.
- À vie : 127 € − ~10 % Gumroad ≈ 114 €, puis ~21-25 % de cotisations → **~89 € net**.
- Annuel : 49 € → **~34 € net par client et par an**, qui se répète tant qu'il renouvelle —
  c'est cette ligne qui construit le revenu mensuel que tu cherches.
- Le produit est bon et le funnel est branché ; la seule variable, c'est le nombre de
  personnes qui voient l'app chaque jour. La régularité bat l'intensité.

## Prochaines actions où je peux t'aider

Compte Gumroad créé ? Reviens me voir pour : automatiser les clés, page FAQ sur la landing,
fiche Microsoft Store, adaptation des posts selon les premiers retours, version anglaise.
