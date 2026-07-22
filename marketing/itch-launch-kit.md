# Kit de lancement itch.io — DesTinGOOD (beta gratuite)

Tout ce qu'il faut pour publier la beta gratuite sur itch.io. Sections « à coller » prêtes à
l'emploi. Décisions actées : **gratuit d'abord** (mur Pro dormant, `License.FreePhase = true`),
**non signé** pour l'instant, monétisation **achat unique / version majeure** plus tard.

---

## 1) Création du projet (Dashboard → Create new project)

| Champ | Valeur |
|---|---|
| **Title** | DesTinGOOD |
| **Project URL** | `destingood` → `https://TON-COMPTE.itch.io/destingood` (reporte cette URL dans `landing.html`, remplace `VOTRE-PAGE`) |
| **Short description / tagline** | Optimiseur PC gaming pour Windows — plus de FPS, moins de latence, 100 % réversible. |
| **Classification** | **Tools** (pas « Games ») |
| **Kind of project** | **Downloadable** |
| **Release status** | **In development** (c'est une beta) |
| **Pricing** | **No payments** (gratuit) — ou **Name your own price** avec minimum **0** si tu veux accepter les dons |
| **Platforms** | ☑ Windows |

> Prix : garde **0 €** pendant toute la beta. itch permet de passer à un prix fixe plus tard
> **sans recréer le projet** — c'est le moment où tu flip `FreePhase = false` et où tu montes
> la build signée.

---

## 2) Métadonnées

**Genre :** Utilities / Tools
**Tags (jusqu'à 10) :** `optimization`, `windows`, `gaming`, `fps`, `performance`, `utility`,
`system`, `tweaks`, `latency`, `français`

**Community :** Comments (ON) — utile pour récolter les retours de beta et bâtir la réputation.

---

## 3) Texte de la page (à coller dans « Details »)

> **DesTinGOOD — l'optimiseur PC gaming, honnête et réversible**
>
> Plus de FPS, moins de latence, un Windows qui respire — **sans injection** (compatible
> anticheat) et **100 % réversible** : une sauvegarde du registre et un point de restauration
> sont créés **avant** toute modification.
>
> **🎁 Beta gratuite : tout est débloqué.** Pendant la beta, l'intégralité des fonctions est
> gratuite, le temps de finir la signature de code. Les utilisateurs de la beta recevront la
> version **Pro offerte** le jour du passage payant.
>
> **Ce que ça fait**
> - **177 optimisations** réversibles (souris, CPU/alim, GPU, réseau, services…) — en manuel
>   ou en **1 clic** adapté à ton matériel.
> - **Mode SIMPLE** : un interrupteur par réglage, effet immédiat, sauvegarde automatique.
> - **Mes jeux** : un boost par jeu (léger / complet), détection automatique.
> - **Entretien du PC** : 6 routines en 1 clic (temporaires, TRIM, caches GPU, DNS…).
> - **Diagnostic** : bilan santé /100, mesure FPS & latence, moniteur matériel, stabilité.
> - **Viseur** personnalisable, mode jeu automatique, et plus.
>
> **⚠️ Au premier lancement**, Windows affiche « éditeur inconnu » (SmartScreen) : clique
> **« Informations complémentaires » → « Exécuter quand même »**. L'application n'est pas
> encore signée — c'est en cours. Elle demande les **droits administrateur** pour appliquer
> les réglages. Aucune donnée n'est envoyée : tout est **local**.
>
> **Configuration :** Windows 10 / 11 (64 bits). L'édition autonome inclut le runtime .NET.
>
> Un bug, une idée ? **Laisse un commentaire** — c'est une beta, ton retour compte.

---

## 4) Note « Install instructions » (champ dédié d'itch)

```
1. Télécharge DesTinGOOD-Setup.exe.
2. Si Windows affiche « Windows a protégé votre ordinateur » :
   clique « Informations complémentaires » puis « Exécuter quand même ».
   (L'app n'est pas encore signée ; la signature de code arrive.)
3. Autorise les droits administrateur (obligatoire pour modifier les réglages système).
4. Tout est réversible : bouton « Tout rétablir » + points de restauration.
```

---

## 5) Captures d'écran à faire (dans la VM, thème sombre néon)

À téléverser dans l'ordre — la 1re sert de vignette :

1. **Fenêtre principale** (bandeau + liste d'optimisations + « TOUT OPTIMISER ») — la vitrine.
2. **Mode SIMPLE** (les interrupteurs pilule néon).
3. **Mes jeux** (tuiles + niveaux Aucun/Léger/Complet).
4. **Bilan santé /100** ou **mesure FPS/latence** (preuve « ça mesure », pas que « ça coche »).
5. **Entretien du PC** (les 6 routines).

Format : PNG, largeur ≥ 1280 px. Recadre proprement (pas tout le bureau).

**Cover image (630×500 recommandé) :** fond noir `#000000`, wordmark, accent néon `#00FF88`.
Pas de mascotte, pas de badge — cohérent avec la charte.

---

## 6) Uploads

- **DesTinGOOD-Setup.exe** (édition autonome, runtime inclus, ~37 Mo) → coche **« This file
  will be downloaded on the platform: Windows »**.
- Optionnel : **version légère** (~6 Mo, sans runtime) pour les PC déjà en .NET Desktop 10 —
  nomme-la clairement « nécessite .NET Desktop 10 ».
- **Nom de fichier versionné** (`DesTinGOOD-Setup-v14.31.exe`) pour que les mises à jour soient
  lisibles dans l'historique des devlogs.

---

## 7) Pièges itch.io spécifiques (à connaître)

- **Avertissement navigateur** : un `.exe` non signé téléchargé depuis itch peut déclencher un
  « ce fichier peut endommager votre ordinateur » côté navigateur. Le certificat de signature
  réglera ça — d'ici là, l'« Install instructions » ci-dessus l'explique.
- **Faux positifs antivirus** : ton app ajoute des exclusions Defender et touche services/registre
  → certains AV peuvent la flagger. Si un utilisateur le signale, propose **VirusTotal** et la
  transparence (code réversible, aucune injection). La signature réduira ces cas.
- **Publie en « Restricted / draft » d'abord**, teste le lien de téléchargement toi-même
  (depuis un autre PC / la VM), puis passe **« Public »**.
- **Devlog** à chaque version : itch notifie les abonnés — gratuit pour fidéliser la base.

---

## 8) Le jour de la monétisation (plus tard, hors beta)

1. Certificat de signature obtenu → build **signée** (`Sign.bat` / `signing.local.cfg`).
2. `License.FreePhase = false` → le mur Pro se rebranche.
3. Sur itch : passe le projet de **0 €** à **prix fixe** (achat unique). itch gère paiement +
   remise du fichier réservé aux acheteurs.
4. Offre une **clé Pro gratuite** aux soutiens de la beta (via ton keygen `seller/` ou un
   fichier réservé itch).
5. Bascule le statut **« In development » → « Released »**.
