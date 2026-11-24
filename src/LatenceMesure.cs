using System;
using System.Globalization;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// Lance PresentMon (Intel, MIT — tools\presentmon\PresentMon.exe) le temps d'une capture,
    /// puis confie son CSV à LatenceReelle. Lecture seule : rien n'est modifié sur la machine.
    /// La dernière mesure est gardée pour comparer « avant / après » un réglage.
    /// </summary>
    internal static class LatenceMesure
    {
        private const string Derniere = "bt-latence-derniere.txt";

        public static string Outil()
        {
            string b = AppDomain.CurrentDomain.BaseDirectory;
            string[] cands =
            {
                Path.Combine(b, @"tools\presentmon\PresentMon.exe"),
                Path.Combine(b, @"..\tools\presentmon\PresentMon.exe"),
                Path.Combine(b, "PresentMon.exe"),
            };
            foreach (string c in cands) if (File.Exists(c)) return Path.GetFullPath(c);
            return null;
        }

        /// <summary>exe : « Overwatch.exe », ou null pour l'application qui affiche le plus d'images.</summary>
        public static LatenceReelle.Releve Mesure(string exe, int secondes, out string erreur)
        {
            erreur = null;
            string outil = Outil();
            if (outil == null) { erreur = "PresentMon introuvable (attendu dans tools\\presentmon\\)."; return null; }
            secondes = Math.Max(5, Math.Min(120, secondes));
            string csv = Path.Combine(Path.GetTempPath(), "onyx-latence-" + Guid.NewGuid().ToString("N") + ".csv");
            string cible = string.IsNullOrEmpty(exe) ? "--exclude dwm.exe" : "--process_name \"" + exe + "\"";
            string commun = cible + " --output_file \"" + csv + "\" --timed " + secondes
                          + " --terminate_after_timed --stop_existing_session";
            try
            {
                NativeResult r = Sys.Run(outil, commun + " --track_pc_latency", (secondes + 20) * 1000);
                // Version de PresentMon sans PC Latency : on retente sans cette option.
                if (!File.Exists(csv)) r = Sys.Run(outil, commun, (secondes + 20) * 1000);
                if (!File.Exists(csv))
                {
                    erreur = "PresentMon n'a rien enregistré (code " + r.ExitCode + ")."
                           + (string.IsNullOrEmpty(r.Output) ? "" : " " + r.Output.Trim().Split('\n')[0]);
                    return null;
                }
                LatenceReelle.Releve rel = LatenceReelle.Analyse(File.ReadAllText(csv), exe);
                return rel;
            }
            catch (Exception ex) { erreur = "Mesure impossible (" + ex.Message + ")."; return null; }
            finally { try { File.Delete(csv); } catch { } }
        }

        public static LatenceReelle.Releve Precedente()
        {
            try
            {
                string p = AppPaths.File(Derniere);
                if (!File.Exists(p)) return null;
                var r = new LatenceReelle.Releve();
                foreach (string l in File.ReadAllLines(p))
                {
                    int i = l.IndexOf('=');
                    if (i <= 0) continue;
                    string k = l.Substring(0, i), v = l.Substring(i + 1);
                    double d; bool ok = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
                    if (k == "app") r.Application = v;
                    else if (k == "entrees" && ok) r.Entrees = (int)d;
                    else if (k == "mediane" && ok) r.EntreeMediane = d;
                    else if (k == "p95" && ok) r.EntreeP95 = d;
                    else if (k == "quand") r.Quand = v;
                }
                return r.EntreeMediane == null ? null : r;
            }
            catch { return null; }
        }

        public static void Memorise(LatenceReelle.Releve r)
        {
            if (r == null || r.EntreeMediane == null || r.Entrees < LatenceReelle.EntreesMin) return;   // une mesure trop maigre n'écrase pas la référence
            try
            {
                File.WriteAllLines(AppPaths.File(Derniere), new[]
                {
                    "app=" + r.Application,
                    "entrees=" + r.Entrees.ToString(CultureInfo.InvariantCulture),
                    "mediane=" + r.EntreeMediane.Value.ToString("R", CultureInfo.InvariantCulture),
                    "p95=" + (r.EntreeP95 ?? 0).ToString("R", CultureInfo.InvariantCulture),
                    "quand=" + DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                });
            }
            catch { }
        }

        /// <summary>Mesure, compare à la précédente (même jeu), mémorise. Texte prêt à afficher.</summary>
        public static string MesureEtCompare(string exe, int secondes)
        {
            string err;
            LatenceReelle.Releve r = Mesure(exe, secondes, out err);
            if (r == null) return err;
            string t = LatenceReelle.Resume(r);
            LatenceReelle.Releve avant = Precedente();
            if (r.Entrees < LatenceReelle.EntreesMin)
                t += "\r\n\r\nTrop peu d'entrées captées (" + r.Entrees + ", il en faut " + LatenceReelle.EntreesMin
                   + ") : bouge la souris et tire pendant toute la capture. Mesure non gardée comme référence.";
            else if (avant != null && string.Equals(avant.Application, r.Application, StringComparison.OrdinalIgnoreCase))
                t += "\r\n\r\nPar rapport à la mesure du " + avant.Quand + " :\r\n" + LatenceReelle.Texte(avant, r);
            else if (r.EntreeMediane != null)
                t += "\r\n\r\nMesure de référence enregistrée. Change UN réglage, puis mesure à nouveau : ONYX te dira s'il fait gagner.";
            Memorise(r);
            return t;
        }
    }
}
