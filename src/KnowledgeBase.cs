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
