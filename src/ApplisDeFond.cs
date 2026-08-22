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
    /// ONYX les ferme le temps d'une partie. Deux règles tiennent tout le module :
    ///
    ///   1. ON NE FAIT PERDRE AUCUN TRAVAIL. Une application qui a une fenêtre reçoit la même
    ///      demande de fermeture qu'un clic sur la croix. Si elle affiche « enregistrer les
    ///      modifications ? », elle RESTE OUVERTE — et le journal le dit, avec son nom. Aucune
    ///      fenêtre n'est jamais tuée de force. Les processus SANS fenêtre (node, agents en ligne
    ///      de commande, gestionnaires Razer) n'ont rien à enregistrer et ignorent la demande
    ///      polie : ceux-là seulement sont arrêtés net.
    ///
    ///   2. ON NE SE TIRE PAS DANS LE PIED. ONYX ne ferme jamais le processus dont il descend :
    ///      lancé depuis le terminal d'un éditeur, tuer l'éditeur emporterait ONYX avec lui — et
    ///      les services suspendus ne seraient jamais relancés. Le jeu et la fenêtre au premier
    ///      plan sont protégés pour la même raison de bon sens.
    ///
    /// Rien n'est désinstallé, rien n'est désactivé au démarrage : à la partie suivante, tout est
    /// là. Les applications ne sont pas relancées à la sortie — les rouvrir sans leurs fichiers ni
    /// leurs conversations ne rendrait rien, et personne n'a demandé qu'un éditeur surgisse tout
    /// seul au retour sur le bureau.
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
            public string Pourquoi;             // ce que ça coûte de le fermer (affiché sous l'interrupteur)
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
        /// Deux absences volontaires, parce que les tuer coûte plus cher que ce que ça rapporte :
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
                Pourquoi = "VS Code, Visual Studio, Docker, WSL, adb. Docker et WSL gardent une machine "
                         + "virtuelle allumée en permanence — c'est le poste le plus lourd de la liste.",
                Processus = new[] { "Code", "devenv", "Docker Desktop", "com.docker.backend", "com.docker.build", "adb" },
                Services  = new[] { "VSStandardCollectorService150", "VSInstallerElevationService", "com.docker.service", "wslservice" }
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
                Pourquoi = "Éclairage Chroma et ses neuf gestionnaires de périphériques. Tes souris et "
                         + "claviers gardent les réglages enregistrés dans leur mémoire interne ; seules "
                         + "les macros pilotées par Synapse sont indisponibles le temps de la partie.",
                PrefixesProcessus = new[] { "Razer", "Rz" },
                PrefixesServices  = new[] { "Razer", "Rz" }
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
        /// fermer est soit impossible, soit un écran bleu. Liste partagée avec le Copilote, qui ne
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

        /// <summary>PUR : ce processus appartient-il au cœur de Windows ? (jamais fermé)</summary>
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
        /// PUR : les noms qu'il faut traiter POLIMENT, fenêtre ou pas.
        ///
        /// Une application moderne est un troupeau : Cursor, c'est UNE fenêtre et vingt processus
        /// auxiliaires qui, eux, n'en ont aucune. Les juger un par un revient à tuer les vingt
        /// pendant que la fenêtre demande « enregistrer les modifications ? » — c'est-à-dire à
        /// détruire exactement le travail qu'on prétendait protéger. Dès qu'UN membre de la famille
        /// a une fenêtre, personne n'est tué : on ferme la fenêtre, et les auxiliaires s'en vont
        /// avec elle. Si elle résiste, la famille entière reste debout.
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
        //  Bilan
        // ==================================================================

        public sealed class BilanCategorie
        {
            public string Cle, Libelle;
            public int Fermes;                  // fermeture polie acceptée
            public int Forces;                  // sans fenêtre : arrêtés net
            public int ServicesArretes;
            public long MoRendus;
            public readonly List<string> Noms = new List<string>();      // noms distincts, pour le journal
            public readonly List<string> Refuses = new List<string>();   // ont gardé la main
        }

        public sealed class Bilan
        {
            public readonly List<BilanCategorie> Categories = new List<BilanCategorie>();
            public readonly List<string> ServicesArretes = new List<string>();   // à relancer à la sortie
            public long MoRendus;
            public int Total;                   // processus réellement partis

            public List<string> TousLesRefus()
            {
                var l = new List<string>();
                foreach (BilanCategorie c in Categories) foreach (string r in c.Refuses) if (!l.Contains(r)) l.Add(r);
                return l;
            }
        }

        /// <summary>
        /// PUR : la ligne de journal. Elle nomme ce qui est parti ET ce qui a résisté — une
        /// application qui refuse de se fermer sans qu'on le dise, c'est un utilisateur qui croit
        /// avoir rendu de la mémoire qu'il n'a pas rendue.
        /// </summary>
        public static string Texte(Bilan b)
        {
            if (b == null || (b.Total == 0 && b.ServicesArretes.Count == 0))
                return "Applications de fond : rien à fermer.";

            var sb = new StringBuilder("Applications de fond : ");
            var morceaux = new List<string>();
            foreach (BilanCategorie c in b.Categories)
            {
                if (c.Fermes + c.Forces + c.ServicesArretes == 0) continue;
                string detail = "";
                if (c.Noms.Count > 0)
                {
                    for (int i = 0; i < c.Noms.Count && i < 3; i++) detail += (detail.Length > 0 ? ", " : "") + c.Noms[i];
                    if (c.Noms.Count > 3) detail += " et " + (c.Noms.Count - 3) + " autre(s)";
                }
                if (c.ServicesArretes > 0)
                    detail += (detail.Length > 0 ? " + " : "") + c.ServicesArretes + " service(s)";
                morceaux.Add(c.Libelle + " (" + detail + ")");
            }
            sb.Append(string.Join(" · ", morceaux.ToArray()));
            sb.Append(" — ").Append(b.Total).Append(" processus fermés, ~").Append(Math.Max(0, b.MoRendus)).Append(" Mo rendus.");

            List<string> refus = b.TousLesRefus();
            if (refus.Count > 0)
            {
                sb.Append(" RESTÉS OUVERTS : ").Append(string.Join(", ", refus.ToArray()));
                sb.Append(" — ces fenêtres ont demandé confirmation (travail non enregistré ?) ; ONYX ne force jamais.");
            }
            return sb.ToString();
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
        /// sur la machine de développement, et il n'a pas à fermer l'éditeur de celui qui compile.
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
        /// lancer Roblox ferait fermer Roblox).
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
            public bool Fenetre;        // true = a une fenêtre, donc on demande poliment
            public Categorie Cat;
        }

        /// <summary>
        /// LECTURE SEULE : ce qui SERAIT fermé. Sépare volontairement l'inspection de l'acte —
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

        /// <summary>
        /// Ferme pour de bon. L'ordre compte : les fenêtres d'abord (elles ont le temps de
        /// s'enregistrer pendant qu'on s'occupe du reste), les processus muets ensuite, les services
        /// en dernier — arrêter le service Chroma de Razer fait tomber ses huit gestionnaires tout
        /// seuls, autant ne pas les avoir tués pour rien.
        /// </summary>
        /// <param name="avantArret">Appelé avec le nom du service JUSTE AVANT de l'arrêter, pour que
        /// l'appelant l'inscrive à son marqueur de reprise. Si ONYX meurt entre les deux, on
        /// relancera au prochain lancement un service qui tournait déjà : sans effet. L'inverse
        /// laisserait un service arrêté que plus personne ne connaît.</param>
        public static Bilan Fermer(IEnumerable<Categorie> actives, ICollection<int> pidsProteges,
                                   Action<string, int> log, Action<string> avantArret)
        {
            var bilan = new Bilan();
            var parCle = new Dictionary<string, BilanCategorie>(StringComparer.OrdinalIgnoreCase);
            var listeActives = new List<Categorie>(actives ?? new Categorie[0]);
            if (listeActives.Count == 0) return bilan;

            foreach (Categorie c in listeActives)
            {
                var bc = new BilanCategorie { Cle = c.Cle, Libelle = c.Libelle };
                parCle[c.Cle] = bc;
                bilan.Categories.Add(bc);
            }

            // --- 1. Inspection : une seule passe, on garde les objets Process vivants -----------
            var tous = new List<KeyValuePair<Process, Cible>>();
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    bool garde = false;
                    try
                    {
                        string nom = p.ProcessName;
                        Categorie c = CategorieDe(listeActives, nom);
                        if (c == null) continue;
                        if (EstProtege(nom, p.Id, pidsProteges)) continue;
                        bool fen = false;
                        try { fen = p.MainWindowHandle != IntPtr.Zero; } catch { }
                        var cible = new Cible { Nom = nom, Pid = p.Id, Mo = p.WorkingSet64 / 1048576, Fenetre = fen, Cat = c };
                        tous.Add(new KeyValuePair<Process, Cible>(p, cible));
                        garde = true;
                    }
                    catch { }
                    finally { if (!garde) { try { p.Dispose(); } catch { } } }
                }
            }
            catch { }

            // La famille entière suit sa fenêtre : voir NomsPolis.
            var cibles = new List<Cible>();
            foreach (KeyValuePair<Process, Cible> kv in tous) cibles.Add(kv.Value);
            HashSet<string> polis = NomsPolis(cibles);

            var avecFenetre = new List<KeyValuePair<Process, Cible>>();
            var sansFenetre = new List<KeyValuePair<Process, Cible>>();
            foreach (KeyValuePair<Process, Cible> kv in tous)
                (polis.Contains(kv.Value.Nom) ? avecFenetre : sansFenetre).Add(kv);

            // --- 2. Fenêtres : la demande polie, exactement comme un clic sur la croix ----------
            foreach (KeyValuePair<Process, Cible> kv in avecFenetre)
            {
                if (!kv.Value.Fenetre) continue;   // auxiliaire : il part avec sa fenêtre
                try { kv.Key.CloseMainWindow(); } catch { }
            }

            // Attente COMMUNE : cinq secondes pour tout le monde, pas cinq secondes chacun.
            if (avecFenetre.Count > 0)
            {
                var chrono = Stopwatch.StartNew();
                while (chrono.ElapsedMilliseconds < 5000)
                {
                    bool reste = false;
                    foreach (KeyValuePair<Process, Cible> kv in avecFenetre)
                    { try { if (!kv.Key.HasExited) { reste = true; break; } } catch { } }
                    if (!reste) break;
                    try { System.Threading.Thread.Sleep(200); } catch { break; }
                }
            }

            // --- 3. Processus muets : rien à enregistrer, ils ignorent la demande polie ---------
            foreach (KeyValuePair<Process, Cible> kv in sansFenetre)
            {
                try
                {
                    if (kv.Key.HasExited) continue;   // parti avec son parent
                    kv.Key.Kill(true);                // l'arbre : un agent laisse des enfants derrière lui
                    kv.Key.WaitForExit(2000);
                }
                catch { }
            }

            // --- 4. Comptage : on ne compte QUE ce qui est réellement parti --------------------
            foreach (KeyValuePair<Process, Cible> kv in avecFenetre) Compter(kv, parCle, bilan, false);
            foreach (KeyValuePair<Process, Cible> kv in sansFenetre) Compter(kv, parCle, bilan, true);
            foreach (KeyValuePair<Process, Cible> kv in avecFenetre) { try { kv.Key.Dispose(); } catch { } }
            foreach (KeyValuePair<Process, Cible> kv in sansFenetre) { try { kv.Key.Dispose(); } catch { } }

            // --- 5. Services ------------------------------------------------------------------
            foreach (string svc in ServicesEnCours())
            {
                Categorie c = null;
                foreach (Categorie x in listeActives) if (CorrespondService(x, svc)) { c = x; break; }
                if (c == null) continue;
                try
                {
                    if (avantArret != null) avantArret(svc);
                    if (!Sys.StopService(svc)) continue;   // refusé (dépendances, droits) : on n'en fait pas état
                    bilan.ServicesArretes.Add(svc);
                    BilanCategorie bc;
                    if (parCle.TryGetValue(c.Cle, out bc)) bc.ServicesArretes++;
                }
                catch { }
            }

            // --- 6. WSL : sa machine virtuelle ne se tue pas, elle s'éteint --------------------
            // vmmem/vmmemWSL n'est pas un processus qu'on arrête ; c'est la mémoire de la VM. La
            // seule façon propre de rendre ces gigaoctets est de demander l'extinction à WSL.
            if (parCle.ContainsKey("dev") && WslTourne())
            {
                try
                {
                    Sys.Run(Sys.Sys32("wsl.exe"), "--shutdown", 20000);
                    if (log != null) log("WSL : machine virtuelle éteinte (elle repartira à la première commande).", 0);
                }
                catch { }
            }

            return bilan;
        }

        private static void Compter(KeyValuePair<Process, Cible> kv, Dictionary<string, BilanCategorie> parCle, Bilan bilan, bool force)
        {
            try
            {
                if (!kv.Key.HasExited)
                {
                    // Toujours vivant : une fenêtre qui a demandé confirmation. On la nomme, on la laisse.
                    BilanCategorie bcr;
                    if (parCle.TryGetValue(kv.Value.Cat.Cle, out bcr) && !bcr.Refuses.Contains(kv.Value.Nom))
                        bcr.Refuses.Add(kv.Value.Nom);
                    return;
                }
                BilanCategorie bc;
                if (!parCle.TryGetValue(kv.Value.Cat.Cle, out bc)) return;
                if (force) bc.Forces++; else bc.Fermes++;
                bc.MoRendus += kv.Value.Mo;
                if (!bc.Noms.Contains(kv.Value.Nom)) bc.Noms.Add(kv.Value.Nom);
                bilan.MoRendus += kv.Value.Mo;
                bilan.Total++;
            }
            catch { }
        }

        private static bool WslTourne()
        {
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try { if (p.ProcessName.StartsWith("vmmem", StringComparison.OrdinalIgnoreCase)) return true; }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { }
            return false;
        }
    }
}
