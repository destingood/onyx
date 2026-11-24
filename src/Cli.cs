using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Mode ligne de commande (sans interface) :
    ///   BTOptimizer.exe -apply reco|esport|all|profile [-backup] [-restorepoint]
    ///   BTOptimizer.exe -revert profile|all
    ///   BTOptimizer.exe -prove   (optimisation prouvée : lots mesurés, régressions annulées)
    ///   BTOptimizer.exe -allerretour [all|ids=a,b,c]   (applique puis rétablit chaque réglage et compare
    ///                    à l'état d'origine ; refusé hors machine virtuelle, code 7)
    /// -apply et -revert acceptent aussi ids=a,b,c à la place du mode.
    /// Utilisé par le gardien de démarrage (tâche planifiée). Journal :
    /// bt-optimizer-log.txt à côté de l'exe. Code retour 0 = succès.
    /// </summary>
    internal static class Cli
    {
        public static int Run(string[] args)
        {
            var lines = new List<string>();
            Action<string, int> log = delegate(string message, int level)
            {
                string tag = level == 1 ? "OK  " : (level == 2 ? "ATT." : (level == 3 ? "ERR " : "INFO"));
                lines.Add("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] [" + tag + "] " + message);
            };

            int code = 1;
            try
            {
                code = Execute(args, log);
            }
            catch (Exception ex)
            {
                log("Erreur fatale CLI : " + ex, 3);
            }

            try
            {
                string logPath = AppPaths.File("bt-optimizer-log.txt");
                File.AppendAllText(logPath,
                    "===== CLI " + string.Join(" ", args) + " =====" + Environment.NewLine +
                    string.Join(Environment.NewLine, lines.ToArray()) + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
            return code;
        }

        private static int Execute(string[] args, Action<string, int> log)
        {
            if (string.Equals(args[0], "-gpuoc", StringComparison.OrdinalIgnoreCase))
            {
                int pl;
                if (!Sys.LoadGpuOcConfig(out pl))
                {
                    log("Configuration OC GPU introuvable : " + Sys.GpuOcConfigPath, 3);
                    return 3;
                }
                log("Ré-application du power limit GPU (pl=" + pl + " W).", 0);
                // Le code de retour compte : c'est une TÂCHE PLANIFIÉE qui appelle ceci au
                // démarrage. En rendant 0 quoi qu'il arrive, un power limit qui ne s'applique
                // plus (pilote changé, carte remplacée) restait invisible dans l'historique de
                // la tâche — l'utilisateur croyait son réglage actif pendant des mois.
                if (!Sys.ApplyGpuOc(pl, log)) { log("Le power limit n'a pas pu être ré-appliqué.", 3); return 4; }
                return 0;
            }

            if (string.Equals(args[0], "-prove", StringComparison.OrdinalIgnoreCase))
            {
                Sys.Init();
                PreuveRun.Rapport pr = PreuveRun.Lance(Catalog.All(), log, null);
                foreach (string l in pr.Texte().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                    log(l, 0);
                if (pr.Echec || pr.Erreur != null) return 4;
                return pr.RedemarrageRequis ? 10 : 0;   // 10 : redémarrage à faire pour finir la preuve
            }

            if (string.Equals(args[0], "-topologie", StringComparison.OrdinalIgnoreCase))
            {
                List<Topologie.Proc> procs = Topologie.Lit();
                log("Processeur : " + Topologie.Libelle(procs), 0);
                log("Cœurs préférés pour le jeu : " + string.Join(",", Topologie.Preferes(procs)), 0);
                List<int> autres = Topologie.Autres(procs);
                log("Logiciels de fond pendant le jeu : " + (autres.Count > 0 ? string.Join(",", autres) : "laissés à Windows"), 0);
                foreach (Topologie.Proc p in procs)
                    log("  logique " + p.Index + " : cœur " + p.Coeur + ", classe " + p.Efficacite + ", L3 n°" + p.L3 + " (" + (p.L3Octets / (1024 * 1024)) + " Mo)", 0);
                return 0;
            }

            if (string.Equals(args[0], "-coherence", StringComparison.OrdinalIgnoreCase))
            {
                NvDrs.Lecture l = NvDrs.Lit("Overwatch.exe");
                log("Pilote NVIDIA (Overwatch.exe" + (l.ProfilJeu ? ", profil du jeu" : ", profil global") + ") : V-Sync "
                    + CoherenceFps.DepuisPilote(l.VSync) + (l.VSync != null ? " (0x" + l.VSync.Value.ToString("X8") + ")" : "")
                    + ", limiteur " + (l.Limiteur == null ? "?" : l.Limiteur.Value.ToString()) + ", G-SYNC global "
                    + (l.GSync == null ? "?" : l.GSync.Value.ToString()) + (l.Motif != null ? " — " + l.Motif : "") + (l.DernierCode != 0 ? " (code NVAPI " + l.DernierCode + ", profil du jeu " + l.CodeJeu + ")" : ""), 0);
                log("Lectures NVAPI (id:code=valeur) : " + l.Trace, 0);
                List<string> constats = Diagnostics.CoherenceOverwatch();
                if (constats.Count == 0) log("Aucune incohérence FPS / rafraîchissement trouvée.", 1);
                foreach (string c in constats) log(c, 2);
                return 0;
            }

            if (string.Equals(args[0], "-allerretour", StringComparison.OrdinalIgnoreCase))
            {
                // Garde-fou AVANT tout le reste : ce test applique les réglages pour de vrai.
                string fabricant, modele;
                if (!LitMachine(out fabricant, out modele))
                {
                    log("Test aller-retour refusé : fabricant ou modèle de la machine illisible (WMI). Aucune écriture.", 3);
                    return 7;
                }
                if (!EtatOrigine.EstMachineDeTest(fabricant, modele))
                {
                    log("Test aller-retour refusé : « " + fabricant + " / " + modele + " » n'est pas une machine virtuelle. Aucune écriture.", 3);
                    return 7;
                }
                Sys.Init();
                string spec = args.Length > 1 ? args[1] : "all";
                List<Tweak> aTester;
                if (string.Equals(spec, "all", StringComparison.OrdinalIgnoreCase)) aTester = Catalog.All();
                else if (spec.StartsWith("ids=", StringComparison.OrdinalIgnoreCase)) aTester = SelectionParIds(Catalog.All(), spec, log);
                else
                {
                    log("Mode inconnu pour -allerretour : " + spec + " (attendu : all ou ids=a,b,c).", 3);
                    return 3;
                }
                if (aTester == null) return 3;
                log("Test aller-retour de " + aTester.Count + " optimisation(s) sur " + fabricant + " / " + modele + ".", 0);
                return AllerRetour.Lance(aTester, log);
            }

            bool apply = string.Equals(args[0], "-apply", StringComparison.OrdinalIgnoreCase);
            bool revert = string.Equals(args[0], "-revert", StringComparison.OrdinalIgnoreCase);
            if (!apply && !revert)
            {
                log("Argument inconnu : " + args[0] + " (attendu : -apply, -revert, -prove, -allerretour, -topologie, -coherence ou -gpuoc).", 3);
                return 2;
            }
            string mode = args.Length > 1 ? args[1].ToLowerInvariant() : "profile";
            bool doBackup = args.Any(a => string.Equals(a, "-backup", StringComparison.OrdinalIgnoreCase));
            bool doPoint = args.Any(a => string.Equals(a, "-restorepoint", StringComparison.OrdinalIgnoreCase));

            Sys.Init();
            List<Tweak> all = Catalog.All();
            List<Tweak> sel;
            if (mode.StartsWith("ids=", StringComparison.Ordinal))
            {
                sel = SelectionParIds(all, mode, log);
                if (sel == null) return 3;
                mode = "ids";
            }
            else switch (mode)
            {
                case "reco":   sel = all.Where(t => t.Recommended).ToList(); break;
                case "esport":  sel = all.Where(t => t.Esport).ToList(); break;
                case "all":     sel = all; break;
                case "profile":
                default:
                    List<string> ids = Sys.LoadProfile();
                    if (ids.Count == 0)
                    {
                        log("Profil vide ou introuvable : " + Sys.ProfilePath, 3);
                        return 3;
                    }
                    sel = all.Where(t => ids.Contains(t.Id)).ToList();
                    break;
            }
            if (sel.Count == 0)
            {
                log("Aucune optimisation sélectionnée (mode " + mode + ").", 3);
                return 3;
            }
            log((apply ? "Application" : "Rétablissement") + " de " + sel.Count + " optimisation(s), mode " + mode + ".", 0);
            EngineResult r = Engine.Run(sel, apply, apply && doBackup, apply && doPoint, log);
            if (r.PrepFailed) return 4;
            return r.Ko == 0 ? 0 : 5;
        }

        /// <summary>Fabricant et modèle (WMI Win32_ComputerSystem) ; faux si illisibles ou vides.</summary>
        private static bool LitMachine(out string fabricant, out string modele)
        {
            fabricant = ""; modele = "";
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
                    foreach (System.Management.ManagementBaseObject mo in s.Get())
                    {
                        fabricant = Convert.ToString(mo["Manufacturer"]) ?? "";
                        modele = Convert.ToString(mo["Model"]) ?? "";
                        break;
                    }
            }
            catch (Exception)
            {
                fabricant = ""; modele = "";
                return false;
            }
            return fabricant.Trim().Length > 0 || modele.Trim().Length > 0;
        }

        /// <summary>
        /// ids=a,b,c : réglages du catalogue dont l'Id figure dans la liste (virgules, espaces ignorés,
        /// casse ignorée). Ids inconnus : avertissement. Aucun trouvé : erreur et null.
        /// </summary>
        private static List<Tweak> SelectionParIds(List<Tweak> all, string spec, Action<string, int> log)
        {
            string liste = spec.Substring(spec.IndexOf('=') + 1);
            var voulus = liste.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            var sel = new List<Tweak>();
            foreach (string id in voulus)
            {
                Tweak t = all.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                if (t == null) { log("Identifiant inconnu : " + id, 2); continue; }
                if (!sel.Contains(t)) sel.Add(t);
            }
            if (sel.Count == 0) log("Aucun identifiant valide dans « " + spec + " ».", 3);
            return sel.Count == 0 ? null : sel;
        }
    }
}
