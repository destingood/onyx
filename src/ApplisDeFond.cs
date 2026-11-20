using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// CE QUI RESTE OUVERT PENDANT QUE TU JOUES — et qui n'a rien à y faire.
    ///
    /// Le Mode Jeu savait suspendre les services de Windows. Mais sur une machine de travail, ce
    /// ne sont pas eux qui pèsent : ce sont les APPLICATIONS restées ouvertes. Un éditeur de code
    /// et ses agents, quatorze processus Node lancés par des serveurs MCP, un Roblox Studio oublié,
    /// neuf gestionnaires Razer pour l'éclairage du clavier. Plusieurs gigaoctets de RAM, et
    /// surtout des dizaines de processus qui se réveillent, prennent leur tour d'ordonnancement et
    /// rendent la main un peu trop tard — ce qui, manette en main, s'appelle un micro-freeze.
    ///
    /// ONYX les met en mode Efficacité le temps d'une partie (priorité Idle + EcoQoS, comme le
    /// Gestionnaire des tâches). RIEN N'EST FERMÉ, rien n'est suspendu : aucun travail perdu,
    /// aucune application à rouvrir. Deux règles tiennent tout le module :
    ///
    ///   1. TOUT EST RÉVERSIBLE. L'état d'origine de chaque processus (priorité, EcoQoS) est noté
    ///      sur disque AVANT le changement, remis à l'identique à la sortie, et rejoué au
    ///      lancement suivant si ONYX a planté (voir GameBoost). Un processus déjà en Efficacité
    ///      n'est pas touché ; un processus que Windows refuse de modifier reste tel quel.
    ///
    ///   2. ON NE SE TIRE PAS DANS LE PIED. ONYX, la chaîne de ses ancêtres, le jeu et la fenêtre
    ///      au premier plan sont protégés : ralentir le processus dont on descend, ou le jeu
    ///      lui-même, serait absurde.
    ///
    /// Rien n'est désinstallé, rien n'est désactivé au démarrage. Seuls les SERVICES des familles
    /// sont arrêtés, puis relancés à la sortie (marqueur de reprise de GameBoost).
    /// </summary>
    internal static class ApplisDeFond
    {
        // ==================================================================
        //  Catalogue
        // ==================================================================

        public sealed class Categorie
        {
            public string Cle;                  // identifiant stable, écrit dans le fichier de réglages
            public string Libelle;              // ce que l'utilisateur lit
            public string Pourquoi;             // ce que ça change pendant la partie (affiché sous l'interrupteur)
            public string[] Processus = new string[0];          // noms EXACTS, sans « .exe »
            public string[] PrefixesProcessus = new string[0];  // familles entières (Razer en a neuf)
            public string[] Services = new string[0];
            public string[] PrefixesServices = new string[0];
        }

        /// <summary>
        /// Les cinq familles. Chaque entrée vient d'un relevé sur machine réelle, pas d'une liste
        /// recopiée : ce sont des processus effectivement vus en train de tourner pendant qu'un jeu
        /// tournait aussi.
        ///
        /// Deux absences volontaires, parce que les ralentir coûte plus cher que ce que ça rapporte :
        /// « git » (une opération interrompue laisse un .git/index.lock et un dépôt à réparer, pour
        /// quelques mégaoctets) et « ssh-agent » (il ne consomme rien, mais le tuer oblige à
        /// ressaisir les phrases de passe). Discord et Wallpaper Engine ne sont dans aucune
        /// catégorie : le premier sert PENDANT la partie, le second se met en pause tout seul.
        /// </summary>
        public static readonly Categorie[] Catalogue =
        {
            new Categorie {
                Cle = "ia", Libelle = "Assistants IA",
                Pourquoi = "Claude, Codex, Cursor, Ollama, LM Studio. Chaque agent garde des processus "
                         + "et des serveurs ouverts en attente. Tes conversations sont conservées sur "
                         + "le disque : elles seront là au retour.",
                Processus = new[] { "claude", "codex", "Cursor", "Paseo", "ollama", "ollama app", "LM Studio", "lms" },
                Services  = new[] { "WSAIFabricSvc" }
            },

            new Categorie {
                Cle = "dev", Libelle = "Outils de développement",
                Pourquoi = "VS Code, Visual Studio, Docker Desktop, adb. La machine virtuelle de Docker "
                         + "et de WSL reste allumée : l'arrêter fermerait tes conteneurs et tes terminaux.",
                Processus = new[] { "Code", "devenv", "Docker Desktop", "com.docker.backend", "com.docker.build", "adb" },
                // Ni com.docker.service ni wslservice : les arrêter éteint Docker et WSL, c'est-à-dire
                // FERME des applications. Le Mode Jeu ne ferme plus rien.
                Services  = new[] { "VSStandardCollectorService150", "VSInstallerElevationService" }
            },

            new Categorie {
                Cle = "roblox", Libelle = "Roblox (Studio et lanceurs en veille)",
                Pourquoi = "Roblox Studio pèse plusieurs gigaoctets, et le lanceur laisse des processus "
                         + "en veille dans la zone de notification. Un Roblox que tu lances APRÈS "
                         + "l'activation n'est jamais touché.",
                Processus = new[] { "RobloxStudioBeta", "RobloxPlayerBeta", "RobloxCrashHandler", "RobloxPlayerInstaller" }
            },

            new Categorie {
                Cle = "razer", Libelle = "Razer (Synapse, Chroma, gestionnaires)",
                Pourquoi = "Éclairage Chroma et ses neuf gestionnaires de périphériques. Ils restent "
                         + "ouverts, en priorité basse : tes macros et ton éclairage continuent de "
                         + "fonctionner.",
                PrefixesProcessus = new[] { "Razer", "Rz" }
                // Pas de PrefixesServices : arrêter le service Chroma fait tomber ses gestionnaires,
                // c'est-à-dire FERME des applications. Le Mode Jeu ne ferme plus rien.
            },

            new Categorie {
                Cle = "node", Libelle = "Processus Node.js",
                Pourquoi = "Serveurs MCP, compilateurs en surveillance, scripts laissés derrière eux par "
                         + "un éditeur fermé. Ils se réveillent en boucle pour ne rien faire.",
                Processus = new[] { "node" }
            }
        };

        /// <summary>
        /// Cœur de Windows : ces processus ne sont JAMAIS visés, quelle que soit la catégorie. Les
        /// y toucher est soit impossible, soit un écran bleu. Liste partagée avec le Copilote, qui ne
        /// les propose jamais non plus comme « gourmands ».
        /// </summary>
        private static readonly string[] Vitaux =
        {
            "idle", "system", "registry", "memory compression", "secure system", "vmmem", "vmmemwsl",
            "csrss", "smss", "wininit", "winlogon", "services", "lsass", "svchost",
            "dwm", "fontdrvhost", "sihost", "audiodg", "wudfhost", "conhost"
        };

        // ==================================================================
        //  Décisions PURES (testables sans machine)
        // ==================================================================

        /// <summary>PUR : ce processus appartient-il au cœur de Windows ? (jamais touché)</summary>
        public static bool EstVital(string nom)
        {
            if (string.IsNullOrEmpty(nom)) return true;   // sans nom, on s'abstient
            foreach (string v in Vitaux)
                if (string.Equals(nom, v, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>PUR : ce nom de processus appartient-il à cette catégorie ?</summary>
        public static bool Correspond(Categorie c, string nomProcessus)
        {
            if (c == null || string.IsNullOrEmpty(nomProcessus)) return false;
            foreach (string n in c.Processus)
                if (string.Equals(nomProcessus, n, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (string p in c.PrefixesProcessus)
                if (nomProcessus.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>PUR : ce nom de service appartient-il à cette catégorie ?</summary>
        public static bool CorrespondService(Categorie c, string nomService)
        {
            if (c == null || string.IsNullOrEmpty(nomService)) return false;
            foreach (string n in c.Services)
                if (string.Equals(nomService, n, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (string p in c.PrefixesServices)
                if (nomService.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>PUR : la catégorie qui réclame ce processus, ou null. Le cœur de Windows n'est
        /// revendiqué par personne.</summary>
        public static Categorie CategorieDe(IEnumerable<Categorie> actives, string nomProcessus)
        {
            if (actives == null || EstVital(nomProcessus)) return null;
            foreach (Categorie c in actives) if (Correspond(c, nomProcessus)) return c;
            return null;
        }

        /// <summary>
        /// PUR : les familles (noms) dont au moins un membre a une fenêtre. Plus utilisé par le Mode
        /// Jeu, qui ne ferme plus rien ; conservé pour l'aperçu BT_FOND, qui distingue une
        /// application à fenêtre de ses auxiliaires muets (Cursor = UNE fenêtre et vingt auxiliaires).
        /// </summary>
        public static HashSet<string> NomsPolis(IEnumerable<Cible> cibles)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (cibles == null) return set;
            foreach (Cible c in cibles) if (c != null && c.Fenetre) set.Add(c.Nom);
            return set;
        }

        /// <summary>
        /// PUR : faut-il épargner ce processus ? Trois raisons, et une seule suffit — c'est le cœur
        /// de Windows, c'est ONYX ou l'un de ses ancêtres (le tuer nous tuerait, et les services
        /// resteraient suspendus), c'est le jeu ou la fenêtre que tu regardes.
        /// </summary>
        public static bool EstProtege(string nom, int pid, ICollection<int> pidsProteges)
        {
            if (EstVital(nom)) return true;
            return pidsProteges != null && pidsProteges.Contains(pid);
        }

        // ==================================================================
        //  Réglages : quelles catégories sont actives
        // ==================================================================

        // Même convention que bt-gamemode-excl.txt : le fichier liste ce qui est EXCLU. Pas de
        // fichier = tout est actif, ce qui est le comportement voulu à la première utilisation.
        private static string ExclPath { get { return AppPaths.File("bt-gamemode-applis.txt"); } }

        /// <summary>Catégories que l'utilisateur a mises hors jeu (clés).</summary>
        public static HashSet<string> ExclusionsChargees()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(ExclPath))
                    foreach (string l in File.ReadAllLines(ExclPath))
                    { string s = l.Trim(); if (s.Length > 0) set.Add(s); }
            }
            catch { }
            return set;
        }

        public static void EnregistrerExclusions(IEnumerable<string> cles)
        {
            try { File.WriteAllLines(ExclPath, new List<string>(cles)); } catch { }
        }

        /// <summary>
        /// Les catégories réellement appliquées à la prochaine activation.
        /// BT_FOND_OFF=1 les neutralise toutes : le harnais de tests active le Mode Jeu POUR DE VRAI
        /// sur la machine de développement, et il n'a pas à ralentir l'éditeur de celui qui compile.
        /// </summary>
        public static List<Categorie> Actives()
        {
            var l = new List<Categorie>();
            try { if (Environment.GetEnvironmentVariable("BT_FOND_OFF") == "1") return l; } catch { }
            var excl = ExclusionsChargees();
            foreach (Categorie c in Catalogue) if (!excl.Contains(c.Cle)) l.Add(c);
            return l;
        }

        public static Categorie ParCle(string cle)
        {
            foreach (Categorie c in Catalogue)
                if (string.Equals(c.Cle, cle, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        // ==================================================================
        //  Machine
        // ==================================================================

        /// <summary>
        /// PID à ne jamais toucher : ONYX, toute sa chaîne d'ancêtres, la fenêtre au premier plan,
        /// et le jeu passé en argument (le MODE JEU AUTO nous dit lequel l'a déclenché — sans ça,
        /// lancer Roblox mettrait Roblox en Efficacité).
        /// </summary>
        public static HashSet<int> PidsProteges(string jeuProtege)
        {
            var graines = new List<int>();
            try { graines.Add(Process.GetCurrentProcess().Id); } catch { }

            // La fenêtre que l'utilisateur regarde au moment de l'activation : c'est la sienne.
            try { int fg = Native.ForegroundPid(); if (fg > 0) graines.Add(fg); }
            catch { }

            // Le jeu : tous ses processus, pas seulement le premier trouvé.
            if (!string.IsNullOrEmpty(jeuProtege))
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName(jeuProtege))
                    { try { graines.Add(p.Id); } catch { } finally { try { p.Dispose(); } catch { } } }
                }
                catch { }
            }

            // Et les ANCÊTRES de chacun. Un processus muet est arrêté avec tout son arbre : si le
            // jeu — ou ONYX — descend de lui, l'arrêter les emporterait. Protéger la chaîne des
            // parents, c'est refuser par construction de scier la branche sur laquelle on est assis.
            var set = new HashSet<int>();
            Dictionary<int, int> parents = Parents();
            foreach (int g in graines)
            {
                set.Add(g);
                foreach (int a in Ancetres(g, parents)) set.Add(a);
            }
            return set;
        }

        /// <summary>Chaîne des processus parents (bornée : une table de PID recyclés pourrait
        /// boucler).</summary>
        private static List<int> Ancetres(int pid, Dictionary<int, int> parents)
        {
            var l = new List<int>();
            try
            {
                int cur = pid;
                for (int n = 0; n < 12; n++)
                {
                    int par;
                    if (!parents.TryGetValue(cur, out par) || par <= 0 || par == cur) break;
                    if (l.Contains(par)) break;
                    l.Add(par);
                    cur = par;
                }
            }
            catch { }
            return l;
        }

        /// <summary>Table PID → PID parent (WMI, comme SvcHost.ServicesParPid).</summary>
        private static Dictionary<int, int> Parents()
        {
            var d = new Dictionary<int, int>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId FROM Win32_Process"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        try { d[Convert.ToInt32(mo["ProcessId"])] = Convert.ToInt32(mo["ParentProcessId"]); }
                        catch { }
                    }
            }
            catch { }
            return d;
        }

        /// <summary>Services en cours d'exécution (nom tel que Windows le connaît).</summary>
        public static List<string> ServicesEnCours()
        {
            var l = new List<string>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Service WHERE State = 'Running'"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        string n = Convert.ToString(mo["Name"]);
                        if (!string.IsNullOrEmpty(n)) l.Add(n);
                    }
            }
            catch { }
            return l;
        }

        /// <summary>Une cible retenue par l'inspection (sert aussi à l'aperçu en lecture seule).</summary>
        public sealed class Cible
        {
            public string Nom;
            public int Pid;
            public long Mo;
            public bool Fenetre;        // true = a une fenêtre (sert à l'aperçu BT_FOND)
            public Categorie Cat;
        }

        /// <summary>
        /// LECTURE SEULE : ce qui SERAIT mis en Efficacité. Sépare volontairement l'inspection de l'acte —
        /// c'est ce qui permet de vérifier le périmètre sans rien casser (hook BT_FOND=1).
        /// </summary>
        public static List<Cible> Inspecter(IEnumerable<Categorie> actives, ICollection<int> pidsProteges)
        {
            var cibles = new List<Cible>();
            if (actives == null) return cibles;
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        string nom = p.ProcessName;
                        Categorie c = CategorieDe(actives, nom);
                        if (c == null) continue;
                        if (EstProtege(nom, p.Id, pidsProteges)) continue;
                        bool fen = false;
                        try { fen = p.MainWindowHandle != IntPtr.Zero; } catch { }
                        cibles.Add(new Cible { Nom = nom, Pid = p.Id, Mo = p.WorkingSet64 / 1048576, Fenetre = fen, Cat = c });
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { }
            return cibles;
        }

        public sealed class Resultat
        {
            public int Mis, Deja, Refus;
            public readonly List<string> ServicesArretes = new List<string>();   // à relancer à la sortie
            public readonly List<Efficacite.Entree> Annules = new List<Efficacite.Entree>();   // journalisés puis refusés : rien n'a changé
        }

        /// <summary>
        /// Met en mode Efficacité (priorité Idle + EcoQoS, comme le Gestionnaire des tâches) les
        /// processus des catégories actives. AUCUN processus n'est fermé ni suspendu. Les services
        /// des catégories, eux, sont arrêtés (puis relancés par l'appelant), en dernier ; aucun de
        /// ceux du catalogue ne ferme une application en s'arrêtant.
        /// </summary>
        /// <param name="avantChangement">Appelé avec l'état D'ORIGINE d'un processus JUSTE AVANT de le
        /// modifier, pour que l'appelant l'inscrive à son journal de rétablissement : si ONYX meurt
        /// entre les deux, le processus rétabli n'avait rien changé ; l'inverse le laisserait en
        /// Efficacité sans que personne le sache. Rend false si l'inscription a échoué : le
        /// processus n'est alors pas touché.</param>
        /// <param name="avantArret">Appelé avec le nom du service JUSTE AVANT de l'arrêter, pour que
        /// l'appelant l'inscrive à son marqueur de reprise. Si ONYX meurt entre les deux, on
        /// relancera au prochain lancement un service qui tournait déjà : sans effet. L'inverse
        /// laisserait un service arrêté que plus personne ne connaît.</param>
        public static Resultat MettreEnEfficacite(IEnumerable<Categorie> actives, ICollection<int> pidsProteges,
                                                  Action<string, int> log, Func<Efficacite.Entree, bool> avantChangement,
                                                  Action<string> avantArret)
        {
            var res = new Resultat();
            var listeActives = new List<Categorie>(actives ?? new Categorie[0]);
            if (listeActives.Count == 0) return res;

            // --- 1. Processus : une passe, un essai isolé par processus ----------------------------
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        string nom = p.ProcessName;
                        if (CategorieDe(listeActives, nom) == null) continue;
                        if (EstProtege(nom, p.Id, pidsProteges)) continue;

                        long debut;
                        int prioOrig;
                        uint ctl, etat;
                        try
                        {
                            debut = p.StartTime.ToUniversalTime().Ticks;   // sans lui, impossible de rétablir le BON processus
                            prioOrig = (int)p.PriorityClass;
                        }
                        catch { res.Refus++; continue; }
                        if (!Native.LitEco(p.Id, out ctl, out etat)) { res.Refus++; continue; }

                        if (Efficacite.DejaEnEfficacite(prioOrig, ctl, etat)) { res.Deja++; continue; }

                        var entree = new Efficacite.Entree
                        {
                            Pid = p.Id, DebutTicks = debut, Nom = nom, Priorite = prioOrig, Controle = ctl, Etat = etat
                        };
                        // Journal non écrit (disque plein, fichier verrouillé) : on ne touche à rien,
                        // un changement que personne ne saurait défaire est exactement ce qu'on évite.
                        if (avantChangement != null && !avantChangement(entree)) { res.Refus++; continue; }

                        bool prioFaite = false, ecoFaite = false;
                        try { p.PriorityClass = ProcessPriorityClass.Idle; prioFaite = true; } catch { }
                        if (prioFaite) ecoFaite = Native.EcritEco(p.Id, 1, 1);
                        if (prioFaite && ecoFaite) { res.Mis++; continue; }

                        // Un des deux a refusé : on remet ce qui a été changé, le processus reste tel quel,
                        // et l'appelant le retire de son journal (sinon la sortie écraserait un réglage
                        // que l'utilisateur ferait entre-temps).
                        if (prioFaite) { try { p.PriorityClass = (ProcessPriorityClass)prioOrig; } catch { } }
                        res.Annules.Add(entree);
                        res.Refus++;
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { }

            // --- 2. Services ------------------------------------------------------------------
            foreach (string svc in ServicesEnCours())
            {
                bool concerne = false;
                foreach (Categorie x in listeActives) if (CorrespondService(x, svc)) { concerne = true; break; }
                if (!concerne) continue;
                try
                {
                    if (avantArret != null) avantArret(svc);
                    if (!Sys.StopService(svc)) continue;   // refusé (dépendances, droits) : on n'en fait pas état
                    res.ServicesArretes.Add(svc);
                }
                catch { }
            }

            return res;
        }
    }
}
