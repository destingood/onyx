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
                Sys.ApplyGpuOc(pl, log);
                return 0;
            }

            bool apply = string.Equals(args[0], "-apply", StringComparison.OrdinalIgnoreCase);
            bool revert = string.Equals(args[0], "-revert", StringComparison.OrdinalIgnoreCase);
            if (!apply && !revert)
            {
                log("Argument inconnu : " + args[0] + " (attendu : -apply, -revert ou -gpuoc).", 3);
                return 2;
            }
            string mode = args.Length > 1 ? args[1].ToLowerInvariant() : "profile";
            bool doBackup = args.Any(a => string.Equals(a, "-backup", StringComparison.OrdinalIgnoreCase));
            bool doPoint = args.Any(a => string.Equals(a, "-restorepoint", StringComparison.OrdinalIgnoreCase));

            Sys.Init();
            List<Tweak> all = Catalog.All();
            List<Tweak> sel;
            switch (mode)
            {
                case "reco":    sel = all.Where(t => t.Recommended).ToList(); break;
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
    }
}
