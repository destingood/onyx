using System;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Conseiller d'outils du Copilote : pour un BESOIN (« récupérer un fichier supprimé »,
    /// « tester ma RAM », « désinstaller proprement »…), propose le bon outil — celui de l'app
    /// (installable en 1 clic) quand il existe, sinon une RECOMMANDATION externe gratuite AVEC
    /// ses risques clairement énoncés. Contient aussi des mises en garde (outils à éviter).
    /// </summary>
    internal static class ToolAdvisor
    {
        /// <summary>Recommandation : texte prêt à afficher + éventuel id winget d'un outil du
        /// catalogue de l'app (→ bouton d'installation 1 clic). WingetId = null : outil externe
        /// (le texte dit où le prendre) ou simple avertissement.</summary>
        public sealed class Rec
        {
            public string Text;
            public string WingetId;   // dans le catalogue LibsForm → installable ; null sinon
        }

        // Chaque entrée : mots-clés ; nom ; id winget (null = externe/avertissement) ; ce que ça
        // fait ; le RISQUE ; où le télécharger (si externe). L'ordre compte (spécifique d'abord).
        private sealed class Item
        {
            public string[] Keys; public string Name; public string Winget;
            public string What; public string Risk; public string Source;
        }

        private static readonly Item[] Catalog =
        {
            // ---- Avertissements PRIORITAIRES (des outils à éviter) ----
            new Item { Keys = new[]{"driver updater","driver booster","mettre a jour les pilotes","maj pilotes auto","booster mon pc","pc booster","game booster"},
                Name = "⚠ À ÉVITER : les « driver updaters » et « PC boosters »",
                What = "Ces outils (Driver Booster, Advanced SystemCare, etc.) promettent d'optimiser en un clic",
                Risk = "Ce sont le plus souvent des logiciels-poubelle : faux problèmes gonflés, pubs, réglages agressifs qui CASSENT plus qu'ils ne réparent. Pour les pilotes, passe par NVIDIA/AMD/Intel directement. Pour optimiser, cette app le fait proprement et réversible." },
            new Item { Keys = new[]{"ccleaner","clean my pc","cleaner"},
                Name = "⚠ CCleaner : déconseillé",
                What = "CCleaner nettoie le disque et le registre",
                Risk = "Devenu lourd (pubs, télémétrie, a déjà été compromis par un malware en 2017), et le « nettoyage du registre » ne sert à rien et peut casser des choses. Utilise le Nettoyage disque intégré de l'app (dis « libère de l'espace ») — gratuit et sûr." },
            new Item { Keys = new[]{"faux support","fake support","arnaque support","microsoft m'a appele","support technique appel"},
                Name = "⚠ Arnaque au faux support",
                What = "Un « technicien Microsoft/Windows » qui t'appelle ou surgit en pop-up",
                Risk = "C'est TOUJOURS une arnaque : Microsoft n'appelle jamais. Ne donne JAMAIS l'accès à distance à ton PC ni de paiement. Raccroche/ferme la page. Si tu as déjà donné accès : coupe internet, change tes mots de passe depuis un autre appareil, lance un scan antivirus." },

            // ---- Récupération / disque / sauvegarde ----
            new Item { Keys = new[]{"recuperer un fichier","fichier supprime","recuperer des donnees","recuperer mes fichiers","undelete","corbeille videe"},
                Name = "Recuva (récupération de fichiers)", Source = "piriform.com/recuva",
                What = "récupère des fichiers supprimés récemment (même après la corbeille)",
                Risk = "Agis VITE et installe-le sur un AUTRE disque/clé que celui à récupérer : chaque écriture réduit les chances. Rien n'est garanti. Pour un cas grave, TestDisk/PhotoRec (gratuit) va plus loin mais perd les noms de fichiers." },
            new Item { Keys = new[]{"cloner mon disque","cloner disque","migrer vers ssd","copier windows sur ssd","transferer windows"},
                Name = "Macrium Reflect / MiniTool Partition Wizard (clonage)", Source = "le site de l'éditeur (version gratuite)",
                What = "clone ton disque entier vers un SSD (Windows compris, sans réinstaller)",
                Risk = "SAUVEGARDE tes fichiers importants AVANT : te tromper de disque de destination l'EFFACE totalement. Vérifie deux fois quel disque est la source et lequel est la cible." },
            new Item { Keys = new[]{"partition","partitionner","redimensionner disque","gerer les disques"},
                Name = "Gestion des disques de Windows (intégré) / MiniTool Partition Wizard",
                What = "crée, redimensionne ou fusionne des partitions",
                Risk = "Toute opération sur les partitions peut faire perdre des données : sauvegarde d'abord. Windows a déjà « Gestion des disques » (clic droit sur Démarrer) pour les cas simples, sans rien installer." },
            new Item { Keys = new[]{"cle usb windows","installer windows","reinstaller windows","clef bootable","support d'installation"},
                Name = "Media Creation Tool (Microsoft) / Rufus", Source = "microsoft.com/software-download",
                What = "crée une clé USB d'installation de Windows",
                Risk = "La création EFFACE entièrement la clé USB choisie : n'y laisse rien d'important. Télécharge Windows uniquement depuis le site officiel Microsoft." },

            // ---- Mémoire / stabilité ----
            new Item { Keys = new[]{"tester ma ram","tester la ram","memoire defectueuse","memtest","erreur memoire","ram defaillante"},
                Name = "MemTest86 (test mémoire)", Source = "memtest86.com",
                What = "teste tes barrettes de RAM à fond (LA cause de nombreux écrans bleus aléatoires)",
                Risk = "Se lance depuis une clé USB au démarrage (hors Windows) et tourne plusieurs heures. Sans danger, juste long. Une seule erreur = une barrette à remplacer ou le XMP à baisser." },

            // ---- Sécurité / malware ----
            new Item { Keys = new[]{"virus","malware","logiciel espion","spyware","trojan","cheval de troie","je suis infecte","scan antivirus","pop-up pub partout","publicites partout"},
                Name = "Malwarebytes Free (scan anti-malware)", Source = "malwarebytes.com",
                What = "détecte et retire les malwares/adwares que l'antivirus classique rate (scan à la demande)",
                Risk = "La version gratuite scanne et nettoie très bien ; refuse juste l'essai « Premium » à la fin (le gratuit suffit). Garde UN seul antivirus résident (Windows Defender fait déjà le job en fond). Redémarre après nettoyage." },
            new Item { Keys = new[]{"mot de passe oublie","oublie mon mot de passe windows","reset mot de passe windows","je suis bloque windows"},
                Name = "Réinitialisation du mot de passe Windows",
                What = "retrouver l'accès à ta session",
                Risk = "Le moyen SÛR et gratuit : si c'est un compte Microsoft, réinitialise-le sur account.live.com/password/reset depuis ton téléphone. Évite les « outils de crack » de mot de passe (souvent piégés). Un compte local oublié sans disque de réinitialisation est très difficile à récupérer sans perte." },

            // ---- Applications / entretien ----
            new Item { Keys = new[]{"desinstaller proprement","desinstaller completement","reste des fichiers apres desinstallation","virer une appli","supprimer un logiciel recalcitrant"},
                Name = "BCUninstaller / Revo Uninstaller Free (désinstallation propre)", Source = "leur site (version gratuite)",
                What = "désinstalle une appli ET nettoie les restes (dossiers, clés de registre) qu'elle laisse",
                Risk = "Ne supprime que ce qui appartient à l'appli visée ; ne coche pas des entrées système que tu ne reconnais pas. Fais un point de restauration avant (le Copilote peut le créer : dis « point de restauration »)." },
            new Item { Keys = new[]{"controle a distance","aider un ami a distance","prendre la main a distance","depanner a distance"},
                Name = "AnyDesk / TeamViewer (contrôle à distance, gratuit perso)", Source = "anydesk.com",
                What = "prendre la main sur un autre PC (ou laisser un proche de confiance dépanner le tien)",
                Risk = "Ne donne l'accès QU'À une personne que tu connais et as appelée toi-même. Un inconnu qui te demande de l'installer = arnaque : refuse. Coupe la session une fois fini." },

            // ---- Besoins couverts par le catalogue de l'app (installation 1 clic) ----
            new Item { Keys = new[]{"enregistrer mon ecran","enregistrer l'ecran","streamer","faire un live","capture video","filmer mon jeu","record gameplay"},
                Name = "OBS Studio (enregistrement & streaming)", Winget = "OBSProject.OBSStudio",
                What = "enregistre ou diffuse ton écran/ta partie, gratuitement et sans filigrane",
                Risk = "Gourmand pendant la capture : baisse la résolution/le débit si ça fait chuter tes FPS. Utilise l'encodeur matériel (NVENC sur NVIDIA) pour alléger le CPU." },
            new Item { Keys = new[]{"ouvrir un rar","fichier rar","fichier 7z","extraire une archive","decompresser","archive zip"},
                Name = "7-Zip (archives)", Winget = "7zip.7zip",
                What = "ouvre et crée les archives .zip, .rar, .7z… — léger et gratuit",
                Risk = "Aucun risque notable. N'ouvre pas une archive reçue d'un inconnu sans réfléchir : c'est un vecteur classique de virus." },

            // ---- Registre / avancé ----
            new Item { Keys = new[]{"editer le registre","modifier le registre","regedit","cle de registre"},
                Name = "Éditeur du Registre (regedit, intégré à Windows)",
                What = "modifier des réglages avancés de Windows",
                Risk = "Une mauvaise valeur peut empêcher Windows de démarrer. AVANT toute modif : Fichier → Exporter (sauvegarde la clé), et idéalement un point de restauration. Si un guide te demande d'éditer le registre « pour booster », méfie-toi — l'app le fait proprement et réversible." },
        };

        /// <summary>Un besoin est reconnu si, pour une clé, TOUS ses mots apparaissent dans la
        /// phrase (ordre libre : « supprimé un fichier » matche la clé « fichier supprime »). Les
        /// mots courts (&lt; 5) doivent être des mots entiers, les longs peuvent être en sous-chaîne.</summary>
        public static Rec Advise(string sNorm)
        {
            if (string.IsNullOrEmpty(sNorm)) return null;
            var words = new System.Collections.Generic.HashSet<string>(
                sNorm.Split(new[] { ' ', '\'', '-', ',', '.', ';', ':', '!', '?', '(', ')', '"', '/', '\n', '\r', '\t' },
                            StringSplitOptions.RemoveEmptyEntries));
            foreach (var it in Catalog)
                foreach (var k in it.Keys)
                    if (KeyMatches(words, sNorm, k))
                        return Build(it);
            return null;
        }

        private static bool KeyMatches(System.Collections.Generic.HashSet<string> words, string s, string key)
        {
            foreach (var kw in key.Split(' '))
            {
                if (kw.Length == 0) continue;
                bool hit = words.Contains(kw) || (kw.Length >= 5 && s.Contains(kw));
                if (!hit) return false;
            }
            return true;
        }

        private static Rec Build(Item it)
        {
            var sb = new StringBuilder();
            bool warnOnly = it.Winget == null && it.Source == null;   // pur avertissement
            if (warnOnly)
            {
                sb.Append(it.Name).Append("\n\n").Append(it.What).Append(".\n\n⚠ ").Append(it.Risk);
                return new Rec { Text = sb.ToString() };
            }
            sb.Append("Outil conseillé : ").Append(it.Name).Append("\n").Append("Ce que ça fait : ").Append(it.What).Append(".");
            sb.Append("\n\n⚠ Risque / précaution : ").Append(it.Risk);
            if (it.Winget != null)
                sb.Append("\n\nJe peux te l'installer en un clic (gratuit, via le catalogue de l'app).");
            else
                sb.Append("\n\nGratuit — à télécharger sur ").Append(it.Source).Append(". (Je ne l'installe pas moi-même : télécharge-le depuis le site officiel pour éviter les copies piégées.)");
            return new Rec { Text = sb.ToString(), WingetId = it.Winget };
        }
    }
}
