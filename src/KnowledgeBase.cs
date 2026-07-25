using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Base de connaissances LOCALE (RAG) du Copilote : une base PC/gaming intégrée + les
    /// documents que l'utilisateur dépose dans le dossier « bt-savoir\ », vectorisés via Ollama
    /// (nomic-embed-text). À chaque question, on récupère les extraits les plus pertinents et on
    /// les donne au modèle AVANT qu'il réponde → réponses ancrées et précises. 100 % local,
    /// gratuit ; l'index est mis en cache (bt-kb-index.txt) et reconstruit si le savoir change.
    /// </summary>
    internal static class KnowledgeBase
    {
        private sealed class Chunk { public string Source; public string Text; public float[] Vec; public DateTime When; }

        // Fraîcheur : au-delà de ce délai, un document de bt-savoir est signalé « peut-être daté »
        // (le savoir périmé est LA cause n°1 d'échec RAG en entreprise — on le rend visible).
        private const int StaleMonths = 18;
        internal static bool IsStale(DateTime when) { return when != DateTime.MinValue && when < DateTime.Now.AddMonths(-StaleMonths); }
        /// <summary>Étiquette de fraîcheur d'une source (« · maj 03/2024 · ⚠ peut-être daté »), ou "" si intégré.</summary>
        internal static string FreshTag(DateTime when)
        {
            if (when == DateTime.MinValue) return "";
            return " · maj " + when.ToString("MM/yyyy") + (IsStale(when) ? " · ⚠ peut-être daté" : "");
        }

        private static readonly object Gate = new object();
        private static List<Chunk> _index;          // en mémoire (chargé/reconstruit une fois)
        private static string _signature;           // empreinte du savoir source (détecte un changement)

        private static string Dir { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-savoir"); } }
        private static string IndexPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-kb-index.txt"); } }

        // --- Base PC/gaming INTÉGRÉE (chaque ligne = un fait interrogeable) ---
        private static readonly string[] Builtin =
        {
            "Un crash dont le module fautif est nvlddmkm, nvwgf2umx, amdkmdag ou igdkmd vient du PILOTE GRAPHIQUE : le réinstaller proprement avec DDU (gratuit) et mettre le pilote à jour.",
            "Le code d'exception c0000005 est une violation d'accès mémoire : souvent une RAM instable (profil XMP/EXPO trop agressif), un overclock, ou des fichiers Windows abîmés (réparer avec DISM puis SFC).",
            "Le code c0000409 est un dépassement de tampon (stack buffer overrun) : mettre l'application à jour et vérifier l'intégrité de ses fichiers.",
            "Une erreur msvcp140.dll, vcruntime140.dll ou msvcr signale un Visual C++ Redistributable manquant : l'installer (gratuit) résout la plupart des jeux qui ne démarrent pas.",
            "L'exception e0434352 est une exception .NET non gérée : installer ou réparer le .NET Desktop Runtime.",
            "Un jeu bloqué à 60 FPS alors que l'écran est 144/240 Hz : passer l'écran à sa fréquence max (Paramètres d'affichage) — gain de fluidité immédiat et gratuit.",
            "En jeu en ligne, ce qui compte n'est pas le débit mais la stabilité : un ping bas ET régulier. La gigue (variation du ping) et la perte de paquets causent des à-coups ; préférer un câble Ethernet au Wi-Fi.",
            "Un GPU au-dessus de 85 °C se bride tout seul (throttling) et les FPS s'effondrent : nettoyer la poussière, améliorer le flux d'air, régler une courbe de ventilation (Fan Control, gratuit).",
            "Activer le profil XMP/EXPO dans le BIOS fait tourner la RAM à sa vraie vitesse : des FPS gratuits déjà payés. Sans lui, la RAM tourne bridée.",
            "DLSS (NVIDIA) et FSR (AMD) calculent l'image en plus petit puis l'agrandissent : beaucoup de FPS gagnés pour une perte visuelle minime, à activer dans les options du jeu.",
            "HAGS (planification GPU accélérée matériel) aide ou gêne selon les jeux et pilotes : c'est un réglage à tester, réversible.",
            "Un DNS lent ralentit chaque connexion à un serveur ou une boutique en jeu : basculer vers Cloudflare (1.1.1.1) ou Google (8.8.8.8), gratuit et réversible.",
            "Windows Update qui remplace un pilote GPU fraîchement installé est une cause classique du pilote qui « revient tout seul » après un DDU : empêcher Windows d'installer des pilotes.",
            "Les fonds d'écran animés (Wallpaper Engine, Lively) sont de vrais tueurs de FPS : les couper avant une session de jeu.",
            "Un anti-triche (EasyAntiCheat, BattlEye, Vanguard/vgc) qui plante : le réparer/réinstaller depuis le dossier du jeu et s'assurer qu'aucun autre anti-triche ne tourne.",
            "Le TRIM garde un SSD rapide dans la durée ; certains « optimiseurs » le coupent à tort. Le fichier d'échange (pagefile) ne doit pas être désactivé, sinon plantages « out of memory ».",
            "Un PC qui n'a pas VRAIMENT redémarré depuis longtemps (le démarrage rapide de Windows endort au lieu d'éteindre) accumule fuites et états bancals : un vrai redémarrage règle beaucoup de soucis, gratuitement.",
            "Écran noir au démarrage : vérifier le câble et la bonne entrée, brancher l'écran sur la CARTE GRAPHIQUE (pas la carte mère), faire un reset d'alimentation (bouton power 15 s débranché), et passer par le mode sans échec (3 arrêts forcés).",
            "Plus d'internet alors que tout est branché : réinitialiser la pile réseau (Winsock + TCP/IP + DNS), souvent cassée par un VPN ou un antivirus, puis redémarrer.",
            "Plus de son : relancer le moteur audio de Windows (services), vérifier le bon périphérique de sortie et le volume de l'application.",
            "La réparation d'intégrité de Windows (DISM /RestoreHealth puis SFC /scannow) répare les fichiers système corrompus : le remède aux soucis « impossibles à régler » qui persistent malgré tout.",
            "Trop de programmes au démarrage ralentissent l'allumage et restent en fond : en couper (réversible) accélère le PC sans rien désinstaller.",

            // --- Base étendue : perfs, thermique, réseau, crashs, stockage, écran, outils ---
            "Quand la VRAM de la carte graphique déborde (textures Ultra en 4K sur une petite carte), le jeu pioche dans la RAM système bien plus lente → grosses saccades. Baisser la qualité des textures d'un cran est le remède gratuit.",
            "Si en jeu le GPU est utilisé à moins de ~95 % (visible dans l'overlay ou HWiNFO) et que les FPS plafonnent, c'est souvent le PROCESSEUR ou la RAM qui limite : baisser la résolution n'aidera pas ; réduire distance d'affichage, ombres et foule oui.",
            "Un navigateur avec beaucoup d'onglets (Chrome, Edge) mange RAM et CPU en fond et fait chuter les FPS : le fermer avant de jouer libère des ressources, gratuitement.",
            "Le mode d'alimentation « Performances élevées » (ou « Performances optimales ») empêche le CPU de se brider en jeu : à activer dans les options d'alimentation de Windows, réversible.",
            "Le Mode Jeu de Windows priorise le jeu au premier plan et limite les tâches de fond pendant la partie : à laisser activé (Paramètres → Jeux → Mode Jeu).",
            "Le Resizable BAR (NVIDIA) / Smart Access Memory (AMD) laisse le CPU accéder à toute la VRAM d'un coup : quelques % de FPS gratuits sur les cartes récentes, à activer dans le BIOS.",
            "Les micro-saccades des premières minutes d'un jeu récent viennent souvent de la COMPILATION DES SHADERS : elles s'estompent après un premier passage. Garder le pilote GPU à jour ; inutile de vider le cache shaders sans raison.",
            "Limiter les FPS un peu sous la fréquence de l'écran (ex. 141 pour un 144 Hz) réduit la latence, la chaleur et le bruit des ventilateurs, et supprime le déchirement avec G-Sync/FreeSync.",
            "Les captures en fond (barre de jeu Xbox, Instant Replay de NVIDIA, Radeon ReLive) et les superpositions (Discord, Steam, RTSS) coûtent des FPS et causent parfois saccades ou plantages : les désactiver si le jeu rame.",
            "Le plein écran EXCLUSIF donne moins de latence que le sans-bordure (borderless). Désactiver les « optimisations plein écran » sur l'exe du jeu peut aider si l'affichage saccade.",
            "Un processeur à 95-100 °C se bride (throttling thermique) : nettoyer le ventirad, refaire la pâte thermique, ou l'undervolter (Ryzen : Curve Optimizer/PBO ; Intel : offset) pour gagner en température sans perdre de perf.",
            "Installer la RAM en DOUBLE CANAL (2 barrettes dans les bons slots, souvent A2/B2) double la bande passante mémoire : gros gain, surtout avec un graphique intégré (APU/iGPU).",
            "16 Go de RAM est le minimum confortable pour le jeu moderne ; avec 8 Go, Windows + le jeu + le navigateur saturent et provoquent des saccades. La RAM doit tourner en XMP/EXPO.",
            "Undervolter la carte graphique (MSI Afterburner, gratuit) baisse température et consommation à performances quasi identiques : moins de bruit et moins de throttling.",
            "Une alimentation (PSU) trop faible ou vieillissante cause redémarrages et écrans noirs sous charge, surtout avec un GPU récent : viser une marge confortable en watts et une alim de qualité.",
            "Des micro-coupures de son et de souris viennent souvent d'un PILOTE qui monopolise le CPU (latence DPC) : LatencyMon (gratuit) identifie le fichier .sys coupable (souvent réseau, Wi-Fi ou son) — le mettre à jour ou revenir à une version stable.",
            "Une souris réglée à 125 Hz ajoute ~7 ms d'input lag ; la passer à 1000 Hz (logiciel du fabricant) la rend bien plus réactive.",
            "En Wi-Fi, la bande 5 GHz est plus rapide et stable à courte portée que la 2,4 GHz (plus encombrée). Pour le jeu compétitif, l'Ethernet reste imbattable.",
            "Windows peut couper l'alimentation de la carte réseau pour économiser : décocher « Autoriser l'ordinateur à éteindre ce périphérique » (Gestionnaire de périphériques) évite des micro-déconnexions.",
            "Le « rubber-banding » (personnage qui se téléporte en arrière) vient de la PERTE DE PAQUETS ou d'un ping instable, pas d'un manque de FPS : tester la qualité de la connexion et privilégier le câble.",
            "Choisir le bon SERVEUR/région (le plus proche) baisse le ping bien plus sûrement que n'importe quel « optimiseur réseau » : vérifier la région dans les options du jeu.",
            "Le bufferbloat (tampon du routeur qui gonfle quand quelqu'un télécharge) fait exploser le ping en jeu : activer la QoS/SQM du routeur ou limiter les téléchargements pendant les parties.",
            "Un écran bleu WHEA_UNCORRECTABLE_ERROR pointe le MATÉRIEL : overclock/undervolt instable, RAM ou CPU en limite. Repasser tout en valeurs par défaut (BIOS) puis tester la stabilité (OCCT).",
            "Un écran bleu IRQL_NOT_LESS_OR_EQUAL ou SYSTEM_SERVICE_EXCEPTION vient presque toujours d'un PILOTE défectueux : le mettre à jour, ou revenir à une version antérieure s'il est apparu après une mise à jour.",
            "Un écran noir récurrent avec « le pilote d'affichage a cessé de répondre » (TDR, nvlddmkm) signale un souci de pilote GPU ou d'overclock : réinstaller le pilote (DDU) et retirer l'OC.",
            "Dans l'Observateur d'événements, un « Kernel-Power 41 » signifie que le PC s'est éteint brutalement (pas d'arrêt propre) : suspecter l'alimentation, une surchauffe, ou un overclock instable.",
            "« DXGI_ERROR_DEVICE_REMOVED », « device hung » ou « dispositif de rendu perdu » = le GPU a décroché : réinstaller le pilote proprement (DDU), retirer l'overclock mémoire du GPU, vérifier alimentation et températures.",
            "Des plantages aléatoires et des écrans bleus variés (violation d'accès c0000005) trahissent souvent une RAM instable : tester avec MemTest86 (gratuit, plusieurs passes) ou le Diagnostic mémoire Windows ; baisser le profil XMP/EXPO si erreurs.",
            "Pour savoir si un plantage vient du matériel : stresser le CPU (OCCT, Prime95) et le GPU (OCCT, FurMark), gratuits. Un crash sous stress = instabilité matérielle ou thermique ; stable au repos mais crash en jeu = souvent pilote ou températures.",
            "Un jeu qui plante ou affiche des textures cassées : vérifier l'intégrité de ses fichiers (Steam : Propriétés → Fichiers installés → Vérifier ; Epic : Gérer → Vérifier) remplace les fichiers corrompus.",
            "Installer les jeux et Windows sur un SSD (surtout NVMe) réduit énormément les temps de chargement et les saccades de streaming des textures par rapport à un disque dur mécanique.",
            "Un SSD rempli à plus de ~90 % ralentit et peut saccader : garder 10-15 % d'espace libre. CrystalDiskInfo (gratuit) surveille sa santé (attribut « Intégrité » / durée de vie).",
            "On défragmente un disque dur MÉCANIQUE, jamais un SSD : Windows s'en charge tout seul pour le HDD, et sur SSD c'est le TRIM (automatique) qui entretient la vitesse.",
            "Combo faible latence sans déchirement : G-Sync/FreeSync activé + V-Sync activé DANS LE PANNEAU du pilote (pas dans le jeu) + une limite de FPS 3 sous la fréquence de l'écran. NVIDIA Reflex, s'il est proposé, réduit encore la latence.",
            "Un effet de traînée (ghosting) derrière les objets en mouvement se règle avec l'« overdrive » de l'écran (menu OSD) : monter d'un cran sans exagérer, sinon inverse ghosting (traînée claire).",
            "Vérifier que l'écran tourne bien à sa fréquence max (Paramètres → Affichage → Avancé) : après un changement de câble ou de pilote, Windows le remet parfois à 60 Hz.",
            "Outils de diagnostic GRATUITS de référence : HWiNFO64 (températures/tensions/horloges), GPU-Z et CPU-Z (infos matériel), MSI Afterburner + RivaTuner (overlay FPS + undervolt), CapFrameX (frametimes et 1% low), CrystalDiskInfo (santé disque).",
            "PC devenu lent d'un coup, ventilateurs qui s'emballent au repos : passer un scan Malwarebytes (version gratuite) et regarder le Gestionnaire des tâches pour un processus inconnu gourmand (parfois un mineur de cryptomonnaie caché).",
            "Riot Vanguard (Valorant) exige le TPM 2.0 et le Secure Boot activés dans le BIOS, et tourne dès le démarrage de Windows : c'est normal et voulu, pas un virus.",
            "Deux anti-triche noyau, ou un anti-triche plus un logiciel de triche/injection en même temps, se battent et font planter : n'installer que le nécessaire, ne jamais lancer de cheat (bannissement + malware).",
            "Pour isoler un conflit logiciel (plantages, lenteurs) : faire un « démarrage minimal » (msconfig → Services : masquer ceux de Microsoft puis tout décocher ; onglet Démarrage : tout désactiver), puis réactiver par moitié pour trouver le coupable.",
            "Règle d'or du dépannage : changer UNE chose à la fois et tester, sinon impossible de savoir ce qui a aidé ou cassé.",
            "Face à un jeu qui plante ou rame, commencer par mettre à jour le PILOTE GRAPHIQUE (site NVIDIA/AMD/Intel) : c'est la cause n°1. Une réinstallation propre avec DDU règle les cas tenaces.",
            "Libérer de l'espace sans risque : Assistant Stockage et Nettoyage de disque (fichiers temporaires, cache, ancienne Windows.old), vider la corbeille et le cache des navigateurs.",
            "Aucun « optimiseur » ne CRÉE de la puissance : le vrai gain vient de retirer ce qui freine (throttling, RAM bridée, pilote abîmé, fond gourmand) et d'utiliser les réglages du jeu (DLSS/FSR, qualité). Se méfier des logiciels qui promettent « +200 % de FPS ».",
            "Overclocker RAM/GPU/CPU peut gagner quelques FPS mais réduit la stabilité et, si poussé, la durée de vie : tester longuement (OCCT, MemTest86) et rester dans des limites sûres. L'undervolt, lui, est sans risque et souvent bénéfique.",
            "Le stuttering (saccades régulières) se diagnostique avec les FRAMETIMES (CapFrameX, ou l'overlay 1% low), pas la moyenne de FPS : une moyenne élevée peut cacher de gros pics qui gâchent le ressenti.",
            "La « latence » ressentie en jeu = latence système (souris → écran) : la réduire passe par NVIDIA Reflex / Anti-Lag, une limite de FPS, un écran à haute fréquence, et un polling souris élevé — pas par un « boost » magique.",
            "Après une grosse mise à jour Windows, un pilote ou un réglage peut être réinitialisé : re-vérifier fréquence d'écran, plan d'alimentation, pilote GPU et Mode Jeu si les perfs baissent soudainement.",

            // --- Base étendue (suite) : périphériques, audio, écran, Windows, réseau avancé, portable ---
            "Le réglage « Améliorer la précision du pointeur » de Windows est de l'ACCÉLÉRATION de souris : la désactiver (Souris → Options du pointeur) rend la visée constante et prévisible, essentiel en FPS.",
            "Une manette qui « drift » (bouge seule) : la recalibrer, nettoyer le stick à l'air sec ; si ça persiste, le potentiomètre est usé (remplaçable). Le « raw input » activé dans le jeu donne une visée 1:1 sans accélération Windows.",
            "Un son qui grésille ou craque vient souvent du PILOTE AUDIO ou d'un conflit de fréquence : mettre à jour le pilote (Realtek/carte son), désactiver le « mode exclusif » des applications, et garder une même fréquence d'échantillonnage (44,1 ou 48 kHz).",
            "Pour un meilleur micro sans matériel : la suppression de bruit logicielle (NVIDIA Broadcast sur carte RTX, gratuit) enlève ventilateurs et bruits de clavier.",
            "Deux écrans à des fréquences DIFFÉRENTES (ex. 144 Hz + 60 Hz) peuvent saccader sur le principal : mettre les deux à la même fréquence, ou débrancher le secondaire pendant le jeu, aide souvent.",
            "Un affichage délavé (noirs gris) en HDMI vient souvent d'une plage de couleurs LIMITÉE (limited/YCbCr) au lieu de COMPLÈTE (full/RGB) : la corriger dans le panneau NVIDIA/AMD.",
            "La génération d'images (DLSS 3 Frame Generation, FSR 3) multiplie les FPS affichés mais AJOUTE de la latence et exige un framerate de base correct (~60) : l'activer avec Reflex, l'éviter en compétitif.",
            "Un scintillement ou des écrans noirs aléatoires sur le bureau (souvent multi-écrans NVIDIA) peuvent venir du MPO (Multi-Plane Overlay) : le désactiver via un réglage registre connu corrige fréquemment le souci.",
            "L'« Auto HDR » de Windows peut délaver certains jeux SDR : le désactiver par jeu si les couleurs semblent fades. Un vrai HDR nécessite un écran certifié (DisplayHDR 600+).",
            "Le suréchantillonnage DLDSR (NVIDIA) / VSR (AMD) rend l'image plus nette en calculant au-dessus de la résolution de l'écran : coûteux en perf, superbe sur des jeux plus légers.",
            "Ne jamais mettre un jeu en priorité « Temps réel » dans le Gestionnaire des tâches : cela peut geler Windows (souris, son). Windows gère déjà bien les priorités.",
            "Sur les processeurs Intel récents (cœurs P et E), Windows 11 répartit mieux les tâches que Windows 10 (Thread Director) : garder Windows et le BIOS à jour évite qu'un jeu tourne sur les mauvais cœurs.",
            "L'isolation du noyau / intégrité de la mémoire (VBS/HVCI) coûte quelques % de FPS mais protège contre des attaques : c'est un ARBITRAGE sécurité/perf — ne la couper qu'en connaissance de cause.",
            "OneDrive qui synchronise en fond peut saturer le disque et l'upload : le mettre en pause pendant le jeu si le PC rame ou si la connexion est saturée.",
            "Ajouter le dossier des jeux aux EXCLUSIONS de l'antivirus (Windows Defender → Exclusions) évite que l'analyse en temps réel cause des micro-saccades au chargement — seulement pour des dossiers de confiance.",
            "Les logiciels RGB/monitoring de fabricants (iCUE, Armoury Crate, Dragon Center…) tournent en fond et entrent parfois en conflit : n'en garder qu'un, ou les fermer pendant le jeu.",
            "Un « NAT strict/type 3 » gêne le matchmaking et le multijoueur : activer l'UPnP sur la box, ou ouvrir les ports du jeu (port forwarding), le passe en modéré/ouvert.",
            "Repérer une perte de paquets : « ping -t 8.8.8.8 » (ou vers le serveur du jeu) pendant une partie, et « pathping » pour voir quel saut du trajet perd des paquets.",
            "Un VPN AJOUTE en général de la latence (détour par un serveur) ; il ne « réduit le ping » que dans le cas rare où il contourne un mauvais routage de l'opérateur.",
            "Un câble Ethernet Cat 5e suffit largement pour le jeu (1 Gb/s) : inutile de payer plus cher ; un câble abîmé ou très long peut en revanche causer des pertes.",
            "Sur portable, la chaleur bride vite : jouer BRANCHÉ sur secteur pour la pleine puissance, surélever l'arrière pour l'air, dépoussiérer les grilles, envisager un undervolt ; un tapis de refroidissement aide un peu.",
            "Bon flux d'air : un peu plus d'air ENTRANT que sortant (légère surpression) réduit la poussière ; ventilateurs avant/bas en aspiration, arrière/haut en extraction.",
            "Sur GPU récent, surveiller le point chaud (hotspot) et la température MÉMOIRE (souvent la plus élevée) avec HWiNFO : un écart énorme edge/hotspot trahit un mauvais contact du dissipateur (pâte/pads à refaire).",
            "Le connecteur d'alimentation 12VHPWR des GPU récents (RTX 40) doit être ENFONCÉ à fond et clipsé : un branchement partiel peut chauffer ou fondre. Vérifier qu'il est bien enclenché.",
            "La pâte thermique s'applique en fine couche (un pois au centre suffit) : en mettre trop n'améliore rien ; elle sèche en 2-4 ans et se refait pour regagner quelques degrés.",
            "Isoler une RAM instable : tester barrette par barrette (une seule à la fois, dans le slot recommandé) ; si une seule fait planter, elle est probablement en cause.",
            "Réenfoncer (reseat) le GPU et les barrettes de RAM règle des pannes de démarrage dues à un mauvais contact, surtout après un transport du PC.",
            "Une mise à jour du BIOS peut corriger de l'instabilité ou une incompatibilité (RAM/CPU récents), mais comporte un risque : la faire sur secteur, avec le bon fichier, sans couper l'alimentation.",
            "Réinitialiser le BIOS (Clear CMOS ou « Load Optimized Defaults ») annule un overclock instable et fait souvent redémarrer un PC qui ne « postait » plus.",
            "Un PC qui plante SEULEMENT en jeu pointe vers le GPU, l'alimentation ou la chaleur ; un plantage AUSSI au repos oriente plutôt vers la RAM, le stockage ou Windows.",
            "Mettre le jeu sur un disque DIFFÉRENT de Windows évite que les accès disque de l'OS et du jeu se gênent : utile surtout si l'un des deux est un vieux HDD.",
            "Un HDD qui claque, répond lentement, ou dont CrystalDiskInfo montre des « secteurs réalloués » est en train de mourir : sauvegarder immédiatement.",
            "DirectStorage accélère le chargement en laissant le GPU décompresser les données : bénéfique surtout avec un SSD NVMe rapide, dans les jeux compatibles.",
            "Réglages compétitifs pour le FPS max et la clarté : qualité basse/moyenne, couper flou de mouvement, profondeur de champ et V-Sync, garder des textures correctes si la VRAM suit, activer Reflex/Anti-Lag.",
            "Le flou de mouvement (motion blur) et le grain de film masquent l'action rapide : les désactiver améliore la lisibilité dans les jeux nerveux.",
            "L'anti-aliasing TAA adoucit mais peut rendre l'image floue en mouvement ; DLAA (NVIDIA) ou un léger sharpening (netteté) compensent.",
            "Windows Update fournit la plupart des pilotes, mais pour le GPU le pilote OFFICIEL (NVIDIA/AMD/Intel) est plus à jour et complet.",
            "Avant une grosse manip (overclock, mise à jour BIOS, nettoyage), créer un POINT DE RESTAURATION : il permet de revenir en arrière si ça tourne mal.",
            "Une réinstallation propre de Windows reste le dernier recours efficace contre une accumulation de soucis logiciels : sauvegarder ses données d'abord.",
            "Sur un portable gaming, vérifier que le jeu utilise bien le GPU DÉDIÉ (et pas l'intégré) : le forcer dans le panneau du pilote ou les Paramètres graphiques de Windows.",
            "Le « MUX switch » d'un portable, s'il existe, connecte l'écran directement au GPU dédié (sans passer par l'iGPU) : gain de FPS notable, à activer dans le logiciel du constructeur.",
            "Désinstaller les logiciels inutiles et bloatwares préinstallés allège le PC ; en cas de doute sur un programme, chercher son nom avant de le retirer.",
            "Une souris ou un clavier branché sur un hub USB de mauvaise qualité peut « lagguer » : le brancher directement sur un port de la carte mère (à l'arrière) règle parfois des soucis d'input.",
            "Le « coil whine » (sifflement électrique) d'un GPU/alimentation sous forte charge est désagréable mais rarement dangereux : limiter les FPS (donc la charge) le réduit souvent.",

            // --- Base étendue (3) : overclocking, refroidissement, OLED, stream, VR, boot, panneaux pilote ---
            "Overclocker un GPU (MSI Afterburner) : monter la limite de puissance au max, puis +offset sur le core par paliers d'environ 15 MHz et sur la mémoire, en testant la stabilité (jeu + OCCT) à chaque étape ; un crash ou des artefacts = trop loin, redescendre.",
            "L'undervolt GPU via la courbe tension/fréquence d'Afterburner garde les performances en baissant la tension : plus frais, plus silencieux, souvent AUSSI stable qu'à défaut — la meilleure optimisation matérielle sans risque.",
            "Overclocker un CPU demande d'augmenter multiplicateur ET tension, donc plus de chaleur et d'usure : sur les CPU récents, l'auto-boost (PBO chez AMD) fait déjà presque tout ; l'undervolt (Curve Optimizer) est souvent plus malin que l'OC brut.",
            "Overclocker la RAM = viser une fréquence plus haute ou resserrer les timings, puis valider LONGUEMENT (MemTest86, TestMem5) : une RAM instable provoque des plantages sournois, difficiles à diagnostiquer.",
            "Un watercooling AIO (tout-en-un) refroidit mieux qu'un petit ventirad, pas forcément qu'un gros haut de gamme : monter le radiateur en haut ou à l'avant, tuyaux vers le bas, pour éviter que la pompe aspire une bulle d'air.",
            "Une pompe d'AIO bruyante ou un CPU qui chauffe brutalement peut venir d'une bulle d'air : incliner/allumer le PC dans différentes positions aide à la déloger ; la pompe doit tourner à pleine vitesse.",
            "Ventilateurs PWM (4 broches) : vitesse pilotée finement ; DC (3 broches) : plus grossièrement. Une courbe de ventilation (BIOS ou Fan Control) équilibre bruit et températures.",
            "Les écrans OLED risquent le marquage (burn-in) sur les éléments FIXES (barres des tâches, HUD) : varier le contenu, baisser la luminosité des logos, et laisser tourner le « rafraîchissement des pixels » automatique réduit fortement le risque.",
            "HDMI 2.1 ou DisplayPort sont nécessaires pour le 4K à 120 Hz ou un haut rafraîchissement : un vieux câble ou l'HDMI 2.0 bride la fréquence. Pour du 144 Hz et plus, préférer le DisplayPort.",
            "Overclocker la fréquence d'un écran (via CRU) peut marcher mais risque des images sautées (frame skipping) ou un écran noir : à tenter prudemment, c'est réversible.",
            "Streamer/enregistrer sans perdre de FPS : utiliser l'encodeur MATÉRIEL du GPU (NVENC sur NVIDIA, AMF sur AMD) plutôt que le CPU (x264) — quasi gratuit en performance et de bonne qualité.",
            "En stream, des images perdues (dropped) viennent du RÉSEAU (upload saturé) → baisser le bitrate ; des images ignorées (skipped/rendering lag) viennent d'un GPU/CPU surchargé → baisser la qualité d'encodage.",
            "En VR, la fluidité est vitale (sinon nausée) : viser la fréquence native du casque ; l'ASW/Motion Smoothing insère des images quand le PC ne suit pas, au prix d'artefacts. Un bon câble/port USB et un GPU costaud sont clés.",
            "PC qui ne démarre plus dans Windows : forcer 3 arrêts pendant le boot ouvre l'Environnement de récupération → « Réparation du démarrage », restauration système, ou invite de commandes (bootrec /fixmbr, /fixboot, /rebuildbcd).",
            "Boucle de redémarrage après une mise à jour ou un pilote : démarrer en mode sans échec (via la récupération) pour désinstaller le coupable, ou revenir à un point de restauration.",
            "« Bootmgr is missing » ou « No boot device » : vérifier l'ordre de démarrage dans le BIOS et le bon disque sélectionné ; un câble SATA ou un M.2 mal enfoncé peut faire disparaître le disque.",
            "DPI de la souris et sensibilité en jeu sont distincts : un DPI raisonnable (800-1600) + la sensibilité du jeu donnent une visée précise ; un DPI énorme n'améliore rien et amplifie le tremblement.",
            "Interrupteurs de clavier mécaniques : linéaires (rouges) pour le jeu, tactiles (bruns) polyvalents, clicky (bleus) bruyants. L'anti-ghosting/NKRO garantit que toutes les touches enfoncées sont bien lues.",
            "Un périphérique sans-fil moderne (dongle 2,4 GHz dédié) a une latence quasi identique au filaire ; le Bluetooth ajoute de la latence — à éviter pour le jeu.",
            "Panneau NVIDIA, réglages utiles : « Mode faible latence » sur Ultra (ou Reflex en jeu), « Gestion de l'alimentation » sur Performances maximales pour éviter les baisses de fréquence, synchro verticale gérée ici plutôt que dans le jeu avec G-Sync.",
            "Équivalents AMD (Adrenalin) : Radeon Anti-Lag (latence), Enhanced Sync (anti-déchirement sans l'input lag de la V-Sync), Radeon Chill (chaleur/conso), Radeon Super Resolution (upscaling à l'échelle du système).",
            "Installer les PILOTES DE CHIPSET de la carte mère (site du fabricant) est souvent oublié : ils gèrent l'alimentation des cœurs et l'USB, et corrigent des soucis de perf/stabilité, surtout sur AMD Ryzen.",
            "Ports USB qui déconnectent ou génèrent de la latence : désactiver l'économie d'énergie USB (« suspension sélective USB » dans le plan d'alimentation, et l'alimentation de chaque hub USB dans le Gestionnaire de périphériques).",
            "Comparer un PC ou valider un OC : 3DMark (GPU/gaming), Cinebench (CPU multi-cœur), Unigine (stabilité GPU). Un score bien en dessous d'une config identique en ligne trahit un frein.",
            "Un score de benchmark anormalement bas trahit un frein : température (throttling), RAM en simple canal ou sans XMP, mode d'alimentation en économie, ou pilote obsolète.",
            "Options de lancement Steam (clic droit → Propriétés) : utiles pour forcer un paramètre ou contourner un bug. « Vérifier l'intégrité des fichiers » répare un jeu qui plante ou a des fichiers manquants.",
            "Un jeu stocke une config et un cache de shaders : renommer/supprimer le dossier de config (souvent dans Documents ou %localappdata%) réinitialise des réglages corrompus qui empêchent le lancement — sauvegarder d'abord.",
            "Steam pré-compile les shaders au téléchargement et aux mises à jour : le laisser finir réduit les saccades des premières minutes de jeu.",
            "L'input lag total est une chaîne : souris (polling) → jeu (Reflex, FPS) → GPU → câble → écran (fréquence, temps de réponse). Activer le « mode jeu » de l'écran (menu OSD) coupe son traitement d'image et baisse la latence.",
            "Le « temps de réponse » (ms) d'une dalle et sa fréquence (Hz) sont deux choses différentes : un 144 Hz à dalle lente laisse des traînées ; l'overdrive de l'écran ajuste cela.",
            "Un GPU lourd qui penche (GPU sag) fatigue le port PCIe : un support (anti-sag bracket) évite les faux contacts à long terme.",
            "Le nettoyage se fait à l'air SEC (bombe d'air ou souffleur), jamais à l'aspirateur (électricité statique) ; bloquer les ventilateurs pendant le soufflage pour ne pas les faire tourner à vide.",
            "Une prise multiple surchargée ou une mauvaise terre peut causer coil whine et instabilité : brancher le PC sur une prise correcte, idéalement via un parasurtenseur ou un onduleur.",
            "Réduire les animations de Windows (Accessibilité → Effets visuels, ou « Ajuster pour de meilleures performances ») allège un PC modeste, sans impact sur les FPS en jeu.",
            "L'« optimisation pour les jeux fenêtrés » de Windows 11 améliore la latence du mode fenêtré/sans-bordure : à laisser activée.",
            "Trop de notifications en jeu : activer l'Assistant de concentration / Ne pas déranger pendant le jeu évite interruptions et micro-saccades.",
            "Sauvegarder ses données sur un disque externe ou le cloud est la seule vraie protection contre une panne de disque, un ransomware ou une fausse manip : un SSD peut lâcher sans prévenir.",
            "Fermer les applis de fond inutiles avant de jouer (navigateur, Spotify, retouche photo) libère RAM et CPU ; la fonction « Prépare ma partie » d'ONYX le fait proprement et de façon réversible.",
            "Une résolution inférieure à celle de l'écran doit rester en plein écran pour être nette ; un ratio « étiré » (stretched) est parfois utilisé en compétitif pour agrandir les cibles.",
            "Des anti-triche récents exigent Secure Boot et TPM 2.0 : les activer dans le BIOS. Un PC en BIOS legacy/MBR peut nécessiter une conversion en UEFI/GPT (mbr2gpt) au préalable.",
            "La qualité de filtrage des textures et « Optimiser pour les performances » dans le panneau pilote apportent peu et dégradent parfois l'image : laisser sur « Qualité » par défaut est généralement le meilleur choix.",
            "Un stuttering qui n'apparaît QU'EN ligne (pas en solo) vient du réseau, pas du PC : le diagnostiquer côté connexion (ping, perte de paquets), pas côté FPS.",

            // --- Base étendue (4) : références, diagnostic, erreurs courantes, Windows, réseau, upgrade ---
            "Températures NORMALES : un CPU tourne vers 30-50 °C au repos et 60-85 °C en charge (au-delà de ~90-95 °C il se bride) ; un GPU vers 30-50 °C au repos et 60-83 °C en jeu — le point chaud (hotspot) et la mémoire peuvent monter vers 90-95 °C sur les cartes récentes, c'est prévu.",
            "Des ventilateurs bruyants au repos viennent souvent d'une courbe trop agressive, d'un logiciel qui charge le CPU en fond, ou de poussière : vérifier températures (HWiNFO) et Gestionnaire des tâches avant de s'inquiéter.",
            "Ne pas confondre : FPS bas = image peu fluide en moyenne (GPU/CPU trop juste ou réglages trop hauts) ; stuttering = à-coups ponctuels malgré des FPS corrects (shaders, RAM, fond, disque) ; input lag = délai entre l'action et l'écran (souris, V-Sync, écran). Trois problèmes, trois remèdes.",
            "Le déchirement (screen tearing) coupe l'image horizontalement car jeu et écran ne sont pas synchronisés : G-Sync/FreeSync le corrige sans input lag ; la V-Sync le corrige mais en ajoute.",
            "« Out of video memory » / « mémoire vidéo insuffisante » : la VRAM déborde — baisser textures et résolution, fermer les applis qui utilisent le GPU, mettre le pilote à jour.",
            "« D3D device lost/removed » ou « le périphérique a été supprimé » : le GPU a décroché — réinstaller le pilote (DDU), retirer l'overclock mémoire, vérifier alimentation et températures.",
            "« Application has stopped working » / « a cessé de fonctionner » au lancement d'un jeu : vérifier l'intégrité des fichiers, installer Visual C++ et DirectX, mettre à jour le pilote GPU, désactiver les overlays.",
            "Téléchargement Steam lent : changer la RÉGION de téléchargement (Paramètres → Téléchargements) vers une proche moins saturée, et vérifier que l'antivirus ne scanne pas chaque fichier.",
            "« Disk write error » sur Steam : vérifier l'espace libre et la protection en écriture, lancer « Vérifier l'intégrité », ou déplacer la bibliothèque ; un disque défaillant peut aussi être en cause.",
            "Disque à 100 % dans le Gestionnaire des tâches : coupables fréquents = SysMain/Superfetch et l'indexation sur un vieux HDD, l'antivirus, ou le fichier d'échange sur un disque lent. Passer Windows sur SSD règle la plupart des cas.",
            "RAM qui se remplit anormalement : un processus qui fuit (redémarrer l'appli), trop d'onglets, ou de la mémoire « en veille » (standby) — normale, Windows la libère au besoin.",
            "Processus « Antimalware Service Executable » (Defender) gourmand : c'est une analyse en cours ; exclure les gros dossiers de confiance (jeux) et planifier l'analyse hors usage réduit l'impact.",
            "Démarrage lent : trop d'applis au démarrage, le démarrage rapide qui buggue, ou un vieux HDD système. Couper les lancements inutiles et passer sur SSD accélère nettement.",
            "PC qui se réveille tout seul de veille : voir les périphériques autorisés (powercfg /devicequery wake_armed) et décocher « autoriser ce périphérique à sortir l'ordinateur de veille » sur la souris ou la carte réseau.",
            "Écran noir au réveil de veille alors que le PC est allumé : souvent le pilote GPU — le mettre à jour ; en dépannage immédiat, Win+Ctrl+Maj+B redémarre le pilote graphique à chaud.",
            "Aucun son ou mauvais périphérique : cliquer l'icône volume → choisir la bonne sortie ; « Son » → Périphériques pour activer/désactiver ; certains jeux ont leur propre sélection de sortie audio.",
            "Souris/clavier non détectés : changer de port USB (à l'arrière, sur la carte mère), tester sans hub ni rallonge de mauvaise qualité.",
            "Écho ou souffle en vocal (Discord) : activer la suppression d'écho et de bruit, baisser le volume des enceintes ou utiliser un casque, et régler la sensibilité d'entrée.",
            "Réinitialiser le réseau en douceur : « ipconfig /flushdns » (vide le cache DNS), « ipconfig /release » puis « /renew » (renouvelle l'IP) ; en dernier recours « netsh winsock reset » puis redémarrage.",
            "Wi-Fi qui décroche : dégager/rapprocher la box, privilégier le 5 GHz, mettre à jour le pilote de la carte Wi-Fi, désactiver son économie d'énergie ; l'Ethernet reste la solution la plus stable.",
            "Un test de débit sépare deux choses : le DÉBIT (Mb/s, pour télécharger) et le PING/latence (ms, pour le jeu). Pour le jeu, un ping bas et stable prime sur un gros débit.",
            "Savoir quoi améliorer : GPU à ~100 % en jeu et CPU peu chargé → le GPU limite (upgrade GPU ou baisser les réglages) ; CPU saturé et GPU sous-utilisé → viser le CPU/la RAM.",
            "Plus de RAM n'augmente pas les FPS si tu en as déjà assez (16 Go) ; passer de 8 à 16 Go supprime en revanche beaucoup de saccades. La VITESSE de la RAM (XMP) aide surtout les APU et les jeux CPU-limités.",
            "Un SSD n'augmente pas les FPS mais supprime les temps de chargement et les saccades de streaming de textures : c'est souvent l'upgrade le plus ressenti au quotidien.",
            "Texte flou : vérifier la résolution NATIVE de l'écran et l'échelle (Affichage → Échelle 100/125/150 %) ; l'outil ClearType (recherche Windows) affine le rendu des polices.",
            "Manette sur PC : les manettes Xbox sont reconnues nativement ; pour une DualShock/DualSense, passer par Steam Input ou DS4Windows (gratuit) assure la compatibilité.",
            "Alt-Tab lent ou plantages en plein écran exclusif : passer le jeu en « plein écran fenêtré (borderless) » rend le basculement instantané, au prix d'un chouïa de latence.",
            "Baisse de FPS après un moment de jeu : souvent la CHALEUR (throttling) qui monte, ou une fuite mémoire du jeu — surveiller les températures (HWiNFO) et redémarrer le jeu si la RAM se remplit.",
            "Micro-coupures régulières toutes les X secondes : suspecter le Wi-Fi qui scanne, un disque qui se rendort, ou un logiciel de fond périodique (sauvegarde, synchro) — tester en Ethernet et en fermant le fond.",
            "Réinstaller le runtime DirectX de juin 2010 (gratuit, Microsoft) apporte les anciennes DLL (d3dx9…) réclamées par beaucoup de jeux : il COMPLÈTE le DirectX de Windows, il ne le remplace pas.",
            "Un vieux jeu qui refuse de tourner sur Windows récent : essayer le mode de compatibilité (clic droit → Propriétés → Compatibilité) et l'exécution en administrateur.",
            "HAGS (planification GPU matérielle) et ReBAR sont à TESTER par jeu : bénéfiques sur certains, neutres ou négatifs sur d'autres — les deux sont réversibles.",
            "Baisser les réglages coûteux et peu visibles (ombres, occlusion ambiante, reflets, distance de vue) gagne beaucoup de FPS pour une perte visuelle faible — mieux que tout mettre au minimum.",
            "La RÉSOLUTION est le réglage le plus coûteux : DLSS/FSR/XeSS gardent une résolution élevée en calculant moins — le meilleur compromis netteté/FPS aujourd'hui.",
            "Un site qui promet de « booster vos FPS » ou « nettoyer votre PC » via un .exe à télécharger est presque toujours une arnaque ou un malware : s'en tenir aux outils reconnus et gratuits, depuis leur site officiel.",
            "Ne jamais donner l'accès à distance de son PC à un « support technique » non sollicité (faux appels ou pop-ups « Microsoft ») : c'est une escroquerie classique.",
            "Écran qui reste en 60 Hz malgré un écran 144 Hz : vérifier le câble (DisplayPort ou HDMI 2.1), la bonne entrée, et régler la fréquence dans Paramètres → Affichage → Avancé ; certains câbles bas de gamme bloquent le haut rafraîchissement.",

            // ═══ RÉSEAU (diagnostic latence, perte de paquets, box, ports) ═══
            "Diagnostic latence en 3 temps : ping vers ta box (192.168.1.1, doit être <1 ms), ping vers 8.8.8.8 (ta latence internet réelle), ping vers le serveur du jeu. Ping déjà haut vers la box = souci LOCAL (Wi-Fi, câble) ; haut seulement au-delà = FAI ou serveur.",
            "Mesurer la perte de paquets : « ping -n 100 8.8.8.8 » (regarder « perdus = X% ») ou « pathping 8.8.8.8 » qui montre à quel saut la perte apparaît. Perte dès le 1er saut = ton réseau local ; plus loin = FAI/serveur.",
            "Un ping qui grimpe SEULEMENT quand quelqu'un télécharge à la maison = bufferbloat : activer la QoS/SQM du routeur (limiter un peu le débit) lisse la latence en jeu.",
            "Le lag du soir (18h-23h) vient souvent de la congestion : réseau du FAI saturé, serveur du jeu bondé, ou Wi-Fi encombré par les voisins (changer de canal 5 GHz dans la box).",
            "NAT strict/type 3 (Xbox/PlayStation) gêne le multijoueur et le matchmaking. Le rendre modéré/ouvert : activer l'UPnP sur la box, ou rediriger (port forwarding) les ports du jeu vers l'IP locale fixe de la machine.",
            "Rediriger des ports : trouver les ports du jeu sur le site de l'éditeur, puis dans l'interface de la box (souvent 192.168.1.1) les pointer vers l'IP locale de la machine, idéalement réservée en DHCP.",
            "Ethernet ne marche pas : tester un autre câble et un autre port, vérifier la LED du port, mettre à jour le pilote réseau ; « ipconfig » — une adresse en 169.254.x.x signifie qu'aucune IP n'a été attribuée (souci DHCP/box).",
            "Double NAT (box + un autre routeur en cascade) casse le port forwarding et aggrave le NAT : mettre le second appareil en mode bridge / point d'accès.",
            "MTU standard = 1500 (1492 en PPPoE/ADSL) : rarement la cause d'un lag, à ne toucher qu'en dernier recours. Un câble Cat 5e suffit pour 1 Gb/s.",
            "Un test de débit sépare le DÉBIT (Mb/s, pour télécharger) et le PING/latence (ms, pour le jeu) : pour le jeu, un ping bas et STABLE prime sur un gros débit. Le Wi-Fi et le CPL ajoutent de la latence face à l'Ethernet.",

            // ═══ CRASHS / ÉCRANS BLEUS (codes BSOD + méthodo Observateur) ═══
            "Méthodo Observateur d'événements : Journaux Windows → Système et Application, filtrer « Erreur » et « Critique » à l'heure du crash. « Application Error » (1000) donne l'exe et le module fautifs ; « Kernel-Power » (41) = extinction brutale ; « BugCheck » (1001) = code de l'écran bleu.",
            "Écran bleu : noter le STOP CODE (ex. VIDEO_TDR_FAILURE). Le minidump (C:\\Windows\\Minidump) s'analyse GRATUITEMENT avec BlueScreenView ou WhoCrashed, qui pointent souvent le pilote (.sys) coupable.",
            "Désactiver le redémarrage automatique (Système → Paramètres avancés → Démarrage et récupération) permet de LIRE le code d'écran bleu avant qu'il disparaisse.",
            "BSOD VIDEO_TDR_FAILURE (nvlddmkm.sys / atikmpag.sys) : pilote GPU — réinstaller proprement (DDU), retirer l'overclock, vérifier alimentation et températures.",
            "BSOD IRQL_NOT_LESS_OR_EQUAL : accès mémoire invalide par un PILOTE — mettre à jour ou revenir en arrière le pilote récemment changé ; tester la RAM si ça persiste.",
            "BSOD PAGE_FAULT_IN_NONPAGED_AREA ou MEMORY_MANAGEMENT : souvent RAM instable ou pilote — MemTest86 plusieurs passes, baisser le profil XMP/EXPO, réparer les fichiers système (DISM puis SFC).",
            "BSOD WHEA_UNCORRECTABLE_ERROR : erreur MATÉRIELLE (CPU/RAM/overclock instable, parfois alimentation) — tout remettre par défaut dans le BIOS puis tester la stabilité (OCCT).",
            "BSOD DPC_WATCHDOG_VIOLATION : un pilote (souvent stockage/SSD ou chipset) bloque trop longtemps — mettre à jour le firmware du SSD et les pilotes de chipset/AHCI.",
            "BSOD CLOCK_WATCHDOG_TIMEOUT : un cœur CPU ne répond plus — souvent un overclock CPU instable ou un souci d'alimentation/thermique : revenir aux valeurs par défaut.",
            "BSOD KERNEL_SECURITY_CHECK_FAILURE ou SYSTEM_SERVICE_EXCEPTION : corruption (pilote, RAM, fichiers système) — SFC/DISM, MemTest86, mise à jour des pilotes ; un antivirus tiers est parfois en cause.",
            "BSOD CRITICAL_PROCESS_DIED ou INACCESSIBLE_BOOT_DEVICE : fichiers système ou pilote de disque cassés — réparation du démarrage, DISM/SFC, vérifier le disque (chkdsk).",
            "Redémarrages SANS écran bleu (le PC coupe net) : suspecter l'alimentation (PSU), une surchauffe (arrêt de sécurité), ou un OC instable ; « Kernel-Power 41 » dans les journaux le confirme.",
            "Freeze complet (image figée, son qui boucle) : souvent GPU (pilote/OC/alim), RAM instable, ou surchauffe — mêmes pistes matérielles qu'un écran bleu.",
            "Après avoir identifié un pilote coupable : le désinstaller, redémarrer, installer la DERNIÈRE version depuis le site du fabricant ; si le souci est apparu APRÈS une mise à jour, revenir en arrière (Gestionnaire de périphériques → Propriétés → Pilote → Restaurer).",

            // ═══ PC PORTABLES (thermique, MUX, alimentation) ═══
            "Un portable gaming bride quand il chauffe : jouer BRANCHÉ sur secteur (pas sur batterie), sur une surface dure (pas un lit qui bouche les grilles), arrière surélevé, grilles dépoussiérées.",
            "Sur batterie, un portable réduit VOLONTAIREMENT ses performances : pour la pleine puissance, rester branché et choisir le profil « Performances/Turbo » dans le logiciel du constructeur (Armoury Crate, MSI Center, Omen…).",
            "Forcer un jeu sur le GPU DÉDIÉ d'un portable (pas l'intégré Intel/AMD) : Paramètres graphiques de Windows, ou panneau NVIDIA → « Processeur graphique préféré : GPU hautes performances ».",
            "Le MUX switch, s'il existe, relie l'écran directement au GPU dédié (sans passer par l'iGPU) : de quelques % à plus de 10 % de FPS, à activer dans le logiciel du constructeur (redémarrage requis).",
            "Undervolter le CPU d'un portable (ThrottleStop côté Intel, si non verrouillé) réduit fortement chaleur et throttling à performances égales : souvent le levier le plus efficace sur un laptop.",
            "Un portable qui s'éteint en jeu : surchauffe (throttling puis arrêt de sécurité), ou chargeur trop juste (CPU+GPU dépassent ce qu'il fournit, la batterie compense puis lâche) — utiliser le chargeur d'origine.",
            "Batterie de portable qui GONFLE : danger — arrêter de l'utiliser, ne pas percer, la faire remplacer (risque d'incendie).",

            // ═══ STREAMING / OBS ═══
            "OBS, réglage clé : encodeur MATÉRIEL (NVENC sur NVIDIA, AMF sur AMD, QuickSync sur Intel) plutôt que x264 (CPU) — quasi gratuit en FPS et de bonne qualité sur GPU récent.",
            "Bitrate de stream : rester sous ~70-80 % de ta vitesse d'UPLOAD (ex. ~6000 kbps en 1080p pour Twitch). Trop haut pour ton upload = images perdues.",
            "OBS, comprendre les images manquées : perdues (dropped) = réseau/upload saturé → baisser le bitrate ; ignorées (rendering lag) = GPU surchargé → baisser la qualité/résolution ; sautées (skipped, encodage) = CPU/GPU trop juste.",
            "Streamer en 1080p60 demande une bonne machine ; 1080p30 ou 900p60 soulage. La résolution de SORTIE (canvas) peut être inférieure à celle du jeu.",
            "Désynchro audio/vidéo dans OBS : ajouter un décalage (offset) sur la source audio dans le mixeur (Filtres → Décalage de synchro).",
            "Micro propre sans matériel : filtre de suppression de bruit (RNNoise, gratuit, dans OBS) + un noise gate coupent ventilateurs et bruits de fond.",
            "Le double PC (un pour jouer, un pour encoder via carte de capture ou NDI) supprime tout impact du stream sur le jeu — la solution des streamers exigeants.",
            "Écran noir dans OBS en capturant un jeu : préférer « Capture de jeu » (Game Capture) à la capture d'écran pour le plein écran ; lancer OBS en administrateur aide pour certains jeux.",

            // ═══ OVERCLOCK / UNDERVOLT (détaillé) ═══
            "Undervolt GPU (le plus sûr) : dans MSI Afterburner, ouvrir la courbe (Ctrl+F), fixer une fréquence cible à une tension plus basse (ex. 900 mV), aplatir la courbe au-delà, tester en jeu/OCCT. Résultat : plus frais, plus silencieux, souvent aussi rapide.",
            "Overclock GPU : limite de puissance au max, puis +core par paliers de ~15 MHz en testant, puis +mémoire par paliers de ~50 MHz. Artefacts (points, scintillement) ou crash = trop loin, redescendre de 2 crans.",
            "Attention à la mémoire GPU : trop poussée, elle corrige silencieusement ses erreurs et FAIT BAISSER les perfs sans planter — viser le point stable et rapide, pas le maximum.",
            "Overclock CPU : sur les puces récentes l'auto-boost fait presque tout. AMD : activer PBO + un Curve Optimizer NÉGATIF (undervolt) gagne perfs et fraîcheur ; Intel : un offset de tension négatif réduit la chaleur.",
            "Overclock RAM : d'abord activer XMP/EXPO (déjà un OC). Aller plus loin (fréquence, timings serrés) demande de valider LONGUEMENT (MemTest86 ou TestMem5, plusieurs heures) — une RAM instable plante de façon imprévisible.",
            "Valider un OC : températures sous contrôle (HWiNFO) ET stabilité (OCCT pour CPU+RAM, jeu réel + FurMark/OCCT pour GPU) plusieurs heures sans erreur, artefact ni crash.",
            "L'undervolt ne présente quasi aucun risque (on baisse tension et chaleur) ; l'overclock pousse tension et chaleur, use plus vite s'il est agressif, et peut annuler une garantie — rester raisonnable.",
            "« Silicon lottery » : deux puces identiques n'overclockent pas pareil — copier les réglages d'un autre PC ne garantit rien, il faut valider les siens. Gain réel d'un OC : souvent 5-10 %, moins qu'un undervolt qui supprime le throttling.",

            // ═══ JEUX POPULAIRES (soucis connus + réglages) ═══
            "Valorant : exige TPM 2.0 + Secure Boot (BIOS). Une erreur « VAN » au lancement vient souvent de Vanguard : redémarrer le PC (Vanguard démarre avec Windows). Jeu très léger : viser un ping bas et un écran haute fréquence.",
            "CS2 (Counter-Strike 2) : très dépendant du CPU. Couper les applis de fond, activer le mode faible latence / Reflex, désactiver la V-Sync, privilégier une haute fréquence d'écran (144 Hz+) pour la réactivité.",
            "Fortnite : le mode de rendu « Performances » (au lieu de DirectX 12) débloque énormément de FPS sur PC modeste. Vider le cache shaders si micro-saccades après une mise à jour.",
            "Apex Legends : plafonné à 144 FPS par défaut — le débloquer via l'option de lancement « +fps_max unlimited ». 16 Go de RAM conseillés ; sensible aux fuites mémoire sur longues sessions.",
            "League of Legends : très léger, tourne sur presque tout. Les lags viennent quasi toujours du RÉSEAU (bon serveur, câble) ou d'un pic de latence, rarement des FPS.",
            "GTA V / GTA Online : SSD réduit fortement les chargements. Le MSAA est très coûteux (le baisser), régler la distance d'affichage et la densité de population gagne beaucoup de FPS.",
            "Call of Duty (Warzone / MW) : énorme consommateur de VRAM et de stockage — baisser la qualité des textures si la VRAM sature (saccades), garder de l'espace disque libre, vérifier les fichiers après chaque grosse mise à jour.",
            "Minecraft : la version Java dépend du CPU et de la RAM allouée (mais trop de RAM allouée nuit) ; installer Fabric + Sodium (gratuit) multiplie les FPS. La version Bedrock est plus légère.",
            "Cyberpunk 2077 et jeux lourds : activer DLSS/FSR/XeSS est quasi indispensable, surtout en ray tracing ; la génération d'images (FG) aide si le framerate de base est correct (~60).",
            "Rocket League : compétitif et léger — désactiver la V-Sync, activer une limite de FPS élevée et stable, viser un ping bas ; la fluidité prime sur les graphismes.",
            "Un jeu qui « stutter » à la première rencontre d'un effet ou d'un ennemi = compilation de shaders à la volée : ça s'atténue en rejouant la zone ; garder le pilote GPU à jour et laisser Steam pré-compiler.",

            // ═══ Distillé de GamingPCSetup (djdallmann, licence MIT — crédit dû), recherches mesurées ═══
            "Savoir utile (recherche mesurée, projet GamingPCSetup) : contrairement au conseil répandu « désactive la modération d'interruption (interrupt moderation) de la carte réseau », les mesures (xperf/iperf) montrent qu'un réglage MOYEN ou ADAPTATIF donne un meilleur ressenti d'input sous charge mixte (audio + GPU + USB + réseau). La couper n'aide que pour un usage purement réseau/CPU.",
            "Le NetworkThrottlingIndex : le tweak « désactive-le » est discutable ; des mesures suggèrent plutôt de le GARDER activé avec une valeur modérée (≈10-20). C'est un réglage registre avancé, à ne toucher qu'en connaissance de cause. (d'après GamingPCSetup)",
            "Une carte réseau en mode MSI/MSI-X (défaut sur la plupart des cartes modernes) alloue ISR et DPC aux mêmes cœurs CPU → traitement plus efficace. Vérifier avec « Get-NetAdapterHardwareInfo | fl » (MsiInterruptSupported / MsiXInterruptSupported = True). (d'après GamingPCSetup)",
            "Par défaut, Windows concentre beaucoup de travail réseau sur le Cœur 0 du CPU : lier les files RSS (Receive Side Scaling) à d'autres cœurs (ex. Set-NetAdapterRSS -BaseProcessorNumber 2 sur un 4-cœurs) peut réduire la latence de traitement DPC. Avancé, nécessite MSI activé. (d'après GamingPCSetup)",
            "Les fonctions d'« offloading » de la carte réseau déchargent le CPU d'une partie du traitement des paquets → plus de temps CPU pour le jeu : à laisser activées par défaut. (d'après GamingPCSetup)",
            "Désactiver « NetBIOS sur TCP/IP » (propriétés TCP/IPv4 → Avancé → WINS) retire un service d'écoute SYSTEM inutile chez la plupart des particuliers : petit gain de propreté et de sécurité, réversible. (d'après GamingPCSetup)",
            "Le « Flow Control » de la carte réseau : le désactiver peut CAUSER des pertes de trames dans certains cas (le streaming vidéo peut en souffrir) — mieux vaut le laisser par défaut sauf raison précise. (d'après GamingPCSetup)",
            "Philosophie d'optimisation sérieuse (projet GamingPCSetup) : MESURER avec des outils (Windows Performance Toolkit / xperf, LatencyMon pour les DPC) plutôt qu'empiler des tweaks « miracles » copiés-collés — beaucoup n'ont aucun effet mesurable, voire nuisent.",
            "La résolution du minuteur (timer) par défaut de Windows est ~15,6 ms ; certains jeux la baissent d'eux-mêmes. La forcer globalement (vieux tweak) a un effet variable, ce n'est pas une solution universelle. (d'après GamingPCSetup)",
            "Nettoyer la lentille du capteur optique de la souris (air sec, ou coton-tige léger) quand le curseur devient erratique : la poussière dessus dégrade le suivi. (d'après GamingPCSetup)",
            "Le LOD (Lift-Off Distance) d'une souris — la hauteur à laquelle elle cesse de suivre quand on la soulève — varie selon la surface : un tapis usé peut modifier le suivi à LOD égal. (d'après GamingPCSetup)",
            "La modération d'interruption existe aussi pour les CONTRÔLEURS USB et influence la latence des périphériques (souris/clavier) : un facteur de réactivité perçue au-delà du seul polling rate. (d'après GamingPCSetup)",
            "Diagnostic système fin : le Windows Performance Toolkit (WPR/WPA), les Sysinternals (Microsoft) et LatencyMon montrent OÙ le temps est passé (pilote, DPC, ISR) au lieu de deviner. (d'après GamingPCSetup)",
            "Parasites/interférences électriques (écran, câbles) : router les câbles de données à l'écart des câbles d'alimentation, utiliser des câbles blindés et un châssis bien relié réduit le couplage — cause rare mais réelle. (d'après GamingPCSetup)",
            "Standardiser sa config PC (mêmes réglages BIOS/Windows documentés, vérifiables) permet de reproduire un état stable et de repérer vite ce qui a changé quand un souci apparaît. (esprit du projet GamingPCSetup)",
            "Base de savoir de référence, libre (MIT) et sourcée pour aller plus loin sur l'optimisation PC gaming : le projet GamingPCSetup de djdallmann sur GitHub — approche par la mesure et les preuves.",
        };

        /// <summary>Empreinte du savoir SOURCE (intégré + fichiers utilisateur + modèle) : sert à
        /// savoir s'il faut reconstruire l'index.</summary>
        private static string Signature(List<Chunk> source)
        {
            var sb = new StringBuilder(LocalBrain.EmbedModelName() ?? "none").Append('|');
            foreach (var c in source) sb.Append(c.Source).Append(':').Append(c.Text.Length).Append(';');
            int h = sb.ToString().GetHashCode();
            return h.ToString();
        }

        // Assemble les morceaux SOURCE (intégré + dossier utilisateur), sans vecteurs.
        private static List<Chunk> Collect()
        {
            var list = new List<Chunk>();
            var seen = new HashSet<string>(StringComparer.Ordinal);   // déduplication des passages identiques
            foreach (string t in Builtin) { if (seen.Add(t)) list.Add(new Chunk { Source = "PC/gaming (intégré)", Text = t, When = DateTime.MinValue }); }
            try
            {
                if (Directory.Exists(Dir))
                    // Récursif : les SOUS-DOSSIERS organisent le savoir (livres/chapitres, façon
                    // wiki BookStack). Formats des exports BookStack : Markdown, texte, HTML.
                    foreach (string f in Directory.GetFiles(Dir, "*", SearchOption.AllDirectories))
                    {
                        string rel0 = f.Substring(Dir.Length).TrimStart('\\', '/');
                        if (IsExcludedRel(rel0)) continue;   // _lisez-moi, _archive\… → hors index (gestion de la désuétude)
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        bool img = ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".tif" || ext == ".tiff";
                        if (ext != ".txt" && ext != ".md" && ext != ".markdown" && ext != ".html" && ext != ".htm"
                            && ext != ".pdf" && ext != ".docx" && !img) continue;
                        string raw;
                        if (ext == ".pdf") { raw = ExtractPdf(f); if (string.IsNullOrEmpty(raw)) continue; }
                        else if (ext == ".docx") { raw = ExtractDocx(f); if (string.IsNullOrEmpty(raw)) continue; }
                        else if (img) { raw = Ocr.Read(f); if (string.IsNullOrEmpty(raw)) continue; }   // OCR (captures, photos de manuel)
                        else { try { raw = File.ReadAllText(f); } catch { continue; } }
                        if (ext == ".html" || ext == ".htm") raw = StripHtml(raw);
                        string src = RelSource(f);
                        DateTime when; try { when = File.GetLastWriteTime(f); } catch { when = DateTime.Now; }
                        foreach (string ch in SplitChunks(raw, 600))
                        {
                            if (!seen.Add(ch)) continue;   // doublon exact (même passage ailleurs) → ignoré
                            list.Add(new Chunk { Source = src, Text = ch, When = when });
                        }
                    }
            }
            catch { }
            return list;
        }

        // Un fichier/dossier dont un segment commence par « _ » est HORS index : le mode d'emploi
        // (_lisez-moi) et surtout le dossier _archive\ pour ranger le contenu PÉRIMÉ sans polluer
        // le RAG (l'archivage séparé est une bonne pratique de gouvernance de la base).
        internal static bool IsExcludedRel(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return false;
            foreach (string seg in rel.Split('\\', '/')) if (seg.StartsWith("_", StringComparison.Ordinal)) return true;
            return false;
        }

        // Source lisible = chemin RELATIF sous bt-savoir (ex. « Reseau/DNS.md ») → montre l'arbo.
        private static string RelSource(string full)
        {
            try
            {
                string rel = full.Substring(Dir.Length).TrimStart('\\', '/');
                return rel.Length > 0 ? rel : Path.GetFileName(full);
            }
            catch { return Path.GetFileName(full); }
        }

        // Texte d'un PDF (manuels, fiches, articles…) via PdfPig — 100 % local, aucun OCR
        // (les PDF scannés-image ne rendent rien : c'est attendu).
        private static string ExtractPdf(string path)
        {
            try
            {
                var sb = new StringBuilder();
                using (var doc = UglyToad.PdfPig.PdfDocument.Open(path))
                    foreach (var page in doc.GetPages())
                    {
                        sb.Append(page.Text).Append('\n');
                        if (sb.Length > 200000) break;   // garde-fou sur un très gros PDF
                    }
                return sb.ToString();
            }
            catch { return ""; }
        }

        // Texte d'un .docx (Word) : c'est un ZIP ; on lit word/document.xml et on retire le balisage.
        // Sans dépendance (System.IO.Compression). Les paragraphes deviennent des sauts de ligne.
        private static string ExtractDocx(string path)
        {
            try
            {
                using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
                {
                    var entry = zip.GetEntry("word/document.xml");
                    if (entry == null) return "";
                    string xml;
                    using (var sr = new StreamReader(entry.Open())) xml = sr.ReadToEnd();
                    xml = System.Text.RegularExpressions.Regex.Replace(xml, "(?i)</w:p>", "\n");   // fin de paragraphe → saut
                    xml = System.Text.RegularExpressions.Regex.Replace(xml, "(?s)<[^>]+>", "");     // retire les balises
                    return System.Net.WebUtility.HtmlDecode(xml);
                }
            }
            catch { return ""; }
        }

        // Texte lisible d'un HTML (export BookStack, page web enregistrée…).
        private static string StripHtml(string html)
        {
            try
            {
                html = System.Text.RegularExpressions.Regex.Replace(html, "(?is)<(script|style|head|nav|footer)[^>]*>.*?</\\1>", " ");
                html = System.Text.RegularExpressions.Regex.Replace(html, "(?s)<[^>]+>", " ");
                html = System.Net.WebUtility.HtmlDecode(html);
            }
            catch { }
            return html;
        }

        // Découpe SÉMANTIQUE : on regroupe des PHRASES entières jusqu'à ~max caractères, sans jamais
        // couper une phrase en deux → chunks « autonomes » et cohérents (meilleurs embeddings, moins
        // d'hallucination). Une phrase plus longue que max est tranchée en dernier recours.
        internal static IEnumerable<string> SplitChunks(string text, int max)
        {
            text = System.Text.RegularExpressions.Regex.Replace(text ?? "", "\\s+", " ").Trim();
            if (text.Length == 0) yield break;
            string[] sentences = System.Text.RegularExpressions.Regex.Split(text, "(?<=[.!?…])\\s+");
            var sb = new StringBuilder();
            foreach (string raw in sentences)
            {
                string sent = raw.Trim();
                if (sent.Length == 0) continue;
                if (sb.Length > 0 && sb.Length + 1 + sent.Length > max)
                {
                    string chunk = sb.ToString().Trim();
                    if (chunk.Length >= 20) yield return chunk;
                    sb.Length = 0;
                }
                if (sent.Length > max)   // phrase géante : tranchage de secours
                {
                    for (int i = 0; i < sent.Length; i += max)
                    {
                        string piece = sent.Substring(i, Math.Min(max, sent.Length - i)).Trim();
                        if (piece.Length >= 20) yield return piece;
                    }
                    continue;
                }
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(sent);
            }
            if (sb.Length > 0) { string chunk = sb.ToString().Trim(); if (chunk.Length >= 20) yield return chunk; }
        }

        /// <summary>Construit l'index (vectorise ce qui manque) si le serveur + le modèle d'embed
        /// sont là. Rapide si rien n'a changé (cache). À appeler en tâche de fond.</summary>
        public static void EnsureIndex()
        {
            try
            {
                if (!LocalBrain.ServerUp(1200) || !LocalBrain.HasEmbedModel()) return;
                var source = Collect();
                string sig = Signature(source);
                lock (Gate) { if (_index != null && _signature == sig) return; }

                var cached = LoadCache();   // texte → vecteur (réutilise ce qui existe déjà)
                var built = new List<Chunk>();
                foreach (var c in source)
                {
                    float[] v;
                    if (cached.TryGetValue(c.Text, out v)) { c.Vec = v; built.Add(c); continue; }
                    v = LocalBrain.Embed(c.Text, false);
                    if (v != null) { c.Vec = v; built.Add(c); }
                }
                SaveCache(built);
                lock (Gate) { _index = built; _signature = sig; }
            }
            catch { }
        }

        /// <summary>Extraits les plus pertinents pour la question (top-k), ou "" si la base n'est
        /// pas prête. Le modèle lit ces extraits pour ancrer sa réponse.</summary>
        // --- Re-ranking HYBRIDE : score lexical (recouvrement de mots-clés) combiné au sémantique ---
        private const double LexWeight = 0.12;   // affine le classement sans écraser le sémantique
        private static readonly HashSet<string> LexStop = new HashSet<string>(StringComparer.Ordinal)
        {
            "les","des","une","mon","ton","son","ses","est","que","qui","pas","sur","par","aux","ces","mes","tes",
            "pour","avec","dans","mais","donc","quoi","cette","cela","sont","elle","vous","nous","leur","plus",
            "tout","tous","fait","etre","avoir","quel","comment","vers","chez","sans","sous","c'est"
        };

        private static string Deacc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                string f = s.Normalize(NormalizationForm.FormD);
                var sb = new StringBuilder(f.Length);
                foreach (char c in f)
                    if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
                return sb.ToString();
            }
            catch { return s; }
        }

        /// <summary>Fraction (0..1) des mots-clés significatifs de la requête présents dans le texte.
        /// Garde les acronymes PC courts (dns, fps, ssd, gpu, dpc…) qui sont très distinctifs.</summary>
        internal static double LexicalScore(string query, string text)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text)) return 0.0;
            string t = Deacc(text.ToLowerInvariant());
            var terms = new List<string>();
            foreach (string w in System.Text.RegularExpressions.Regex.Split(Deacc(query.ToLowerInvariant()), "[^a-z0-9]+"))
                if (w.Length >= 3 && !LexStop.Contains(w) && !terms.Contains(w)) terms.Add(w);
            if (terms.Count == 0) return 0.0;
            int hit = 0;
            foreach (string term in terms) if (t.Contains(term)) hit++;
            return (double)hit / terms.Count;
        }

        public static string Search(string query, int k)
        {
            try
            {
                List<Chunk> idx;
                lock (Gate) idx = _index;
                if (idx == null) { EnsureIndex(); lock (Gate) idx = _index; }
                if (idx == null || idx.Count == 0) return "";
                float[] q = LocalBrain.Embed(query, true);
                if (q == null) return "";

                // 1) Récupération SÉMANTIQUE : cosinus, on garde ce qui passe le seuil de pertinence.
                var cand = new List<KeyValuePair<double, Chunk>>();   // clé = score combiné (ré-ordonné)
                foreach (var c in idx)
                {
                    if (c.Vec == null) continue;
                    double cos = Cosine(q, c.Vec);
                    if (cos < 0.35) continue;   // trop peu pertinent : le lexical ne doit pas le repêcher
                    // 2) Re-ranking HYBRIDE : cosinus + recouvrement lexical (termes exacts).
                    double combined = cos + LexicalScore(query, c.Text) * LexWeight;
                    cand.Add(new KeyValuePair<double, Chunk>(combined, c));
                }
                if (cand.Count == 0) return "";
                cand.Sort(delegate (KeyValuePair<double, Chunk> a, KeyValuePair<double, Chunk> b) { return b.Key.CompareTo(a.Key); });

                var sb = new StringBuilder("Base de connaissances (extraits pertinents) :\n");
                int n = Math.Min(k, cand.Count); int kept = 0;
                for (int i = 0; i < n; i++)
                {
                    var c = cand[i].Value;
                    sb.Append("- ").Append(c.Text).Append(" [").Append(c.Source).Append(FreshTag(c.When)).Append("]\n");
                    kept++;
                }
                return kept > 0 ? sb.ToString() : "";
            }
            catch { return ""; }
        }

        /// <summary>Sources actuellement indexées (pour « que contient ta base »).</summary>
        public static string Describe()
        {
            var source = Collect();
            var bySrc = new Dictionary<string, int>();
            var whenOf = new Dictionary<string, DateTime>();
            foreach (var c in source)
            {
                int n; bySrc.TryGetValue(c.Source, out n); bySrc[c.Source] = n + 1;
                if (!whenOf.ContainsKey(c.Source)) whenOf[c.Source] = c.When;
            }
            var sb = new StringBuilder();
            foreach (var kv in bySrc) sb.Append("• ").Append(kv.Key).Append(FreshTag(whenOf[kv.Key])).Append(" : ").Append(kv.Value).Append(" passage(s)\n");
            return sb.ToString().TrimEnd();
        }

        /// <summary>Force la reconstruction au prochain EnsureIndex (ex. après ajout de documents).</summary>
        public static void Invalidate() { lock (Gate) { _index = null; _signature = null; } try { if (File.Exists(IndexPath)) File.Delete(IndexPath); } catch { } }

        /// <summary>Crée le dossier bt-savoir et y dépose un mode d'emploi si vide.</summary>
        public static string FolderPath()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string readme = Path.Combine(Dir, "_lisez-moi.txt");
                if (!File.Exists(readme))
                    File.WriteAllText(readme,
                        "Dépose ici tes documents : .txt, .md, .html, .pdf, .docx (Word), et images (.png/.jpg — lues par OCR).\n" +
                        "Notes de dépannage, config, manuels, captures d'écran, procédures…\n" +
                        "Les SOUS-DOSSIERS organisent le savoir (ex. Reseau\\, Jeux\\) — façon wiki.\n" +
                        "Le Copilote les lit et s'en sert pour te répondre plus précisément.\n\n" +
                        "PDF : texte extrait automatiquement (un PDF scanné-image passe par l'OCR si tu l'exportes en .png).\n" +
                        "Images : le texte est reconnu par l'OCR intégré de Windows (aucun téléchargement).\n" +
                        "Astuce BookStack : exporte un livre/une page en Markdown, HTML ou PDF et dépose le fichier ici.\n\n" +
                        "Fraîcheur : au-delà de 18 mois, un document est signalé « peut-être daté » à la citation.\n" +
                        "Archivage : place le contenu PÉRIMÉ dans un sous-dossier « _archive\\ » — il est CONSERVÉ mais\n" +
                        "  IGNORÉ par le Copilote (les noms commençant par « _ » ne sont jamais indexés).\n\n" +
                        "Pour une recherche encore plus précise (surtout en français), dis « installe bge-m3 ».\n" +
                        "Après tout ajout, dis « recharge mon savoir ».\n");
            }
            catch { }
            return Dir;
        }

        // ---- cache disque (texte + vecteur) : 1 ligne par morceau ----
        private static Dictionary<string, float[]> LoadCache()
        {
            var map = new Dictionary<string, float[]>();
            try
            {
                if (!File.Exists(IndexPath)) return map;
                string want = LocalBrain.EmbedModelName() ?? "none";
                foreach (string line in File.ReadAllLines(IndexPath))
                {
                    // En-tête « #MODEL\t<nom> » : vecteurs d'un AUTRE modèle → cache incompatible, on rejette.
                    if (line.StartsWith("#MODEL\t", StringComparison.Ordinal))
                    {
                        if (line.Substring(7).Trim() != want) return new Dictionary<string, float[]>();
                        continue;
                    }
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    string text = line.Substring(0, tab).Replace("\\n", "\n");
                    string[] parts = line.Substring(tab + 1).Split(',');
                    var v = new float[parts.Length];
                    bool ok = true;
                    for (int i = 0; i < parts.Length; i++) if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])) { ok = false; break; }
                    if (ok) map[text] = v;
                }
            }
            catch { }
            return map;
        }

        private static void SaveCache(List<Chunk> chunks)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("#MODEL\t").Append(LocalBrain.EmbedModelName() ?? "none").Append('\n');
                foreach (var c in chunks)
                {
                    if (c.Vec == null) continue;
                    sb.Append(c.Text.Replace("\n", "\\n")).Append('\t');
                    for (int i = 0; i < c.Vec.Length; i++) { if (i > 0) sb.Append(','); sb.Append(c.Vec[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
                    sb.Append('\n');
                }
                File.WriteAllText(IndexPath, sb.ToString());
            }
            catch { }
        }

        private static double Cosine(float[] a, float[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            double d = 0, na = 0, nb = 0;
            for (int i = 0; i < n; i++) { d += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
            return (na > 0 && nb > 0) ? d / (Math.Sqrt(na) * Math.Sqrt(nb)) : 0;
        }
    }
}
