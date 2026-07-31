using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// « ÇA MARCHAIT HIER ! » — la photo quotidienne de l'état du système. Le Gardien enregistre
    /// chaque jour : version du pilote GPU, build Windows, programmes au démarrage, Go libres.
    /// Quand l'utilisateur demande « qu'est-ce qui a changé sur mon PC ? », on compare AUJOURD'HUI
    /// à la photo d'avant et on répond avec des FAITS (« nouveau pilote GPU », « +2 au démarrage »)
    /// au lieu de spéculer. Le Journal trace ce qu'ONYX fait ; StateDiff trace ce que le SYSTÈME
    /// fait dans le dos de l'utilisateur.
    /// </summary>
    internal static class StateDiff
    {
        private static string Dir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-etat"); }
        }

        /// <summary>La photo de l'instant : clé=valeur, tout mesuré en local.</summary>
        public static Dictionary<string, string> Capture()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var drv = Diagnostics.GpuDriver();
                if (drv != null) d["pilote_gpu"] = drv.Name + " v" + drv.Version;
            }
            catch { }
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    if (k != null) d["windows"] = Convert.ToString(k.GetValue("DisplayVersion", "")) + " build " + Convert.ToString(k.GetValue("CurrentBuildNumber", ""));
            }
            catch { }
            try
            {
                var names = new List<string>();
                foreach (var e in Sys.ListStartup()) if (e.Enabled && !string.IsNullOrEmpty(e.Name)) names.Add(e.Name.Trim());
                names.Sort(StringComparer.OrdinalIgnoreCase);
                d["demarrage"] = string.Join("|", names.ToArray());
            }
            catch { }
            try
            {
                var di = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                d["disque_libre_go"] = ((int)(di.AvailableFreeSpace / 1073741824)).ToString();
            }
            catch { }
            return d;
        }

        /// <summary>Écrit la photo du jour (bt-etat\AAAAMMJJ.txt) et purge au-delà de 30 jours.</summary>
        public static void SaveToday()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var lines = new List<string>();
                foreach (var kv in Capture()) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(Path.Combine(Dir, DateTime.Now.ToString("yyyyMMdd") + ".txt"), lines);
                var files = new List<string>(Directory.GetFiles(Dir, "????????.txt"));
                files.Sort(StringComparer.Ordinal);
                while (files.Count > 30) { try { File.Delete(files[0]); } catch { } files.RemoveAt(0); }
            }
            catch { }
        }

        private static Dictionary<string, string> Load(string path)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (var l in File.ReadAllLines(path))
                {
                    int i = l.IndexOf('=');
                    if (i > 0) d[l.Substring(0, i)] = l.Substring(i + 1);
                }
            }
            catch { }
            return d;
        }

        /// <summary>Différences ANCIEN → ACTUEL, en phrases lisibles. Pur et testable.</summary>
        public static List<string> Diff(Dictionary<string, string> old, Dictionary<string, string> now)
        {
            var outp = new List<string>();
            string a, b;
            if (old.TryGetValue("pilote_gpu", out a) && now.TryGetValue("pilote_gpu", out b) && a != b)
                outp.Add("🎞 Pilote GPU CHANGÉ : « " + a + " » → « " + b + " » (un nouveau pilote explique souvent un changement de comportement en jeu).");
            if (old.TryGetValue("windows", out a) && now.TryGetValue("windows", out b) && a != b)
                outp.Add("🪟 Windows mis à jour : " + a + " → " + b + ".");
            if (old.TryGetValue("demarrage", out a) && now.TryGetValue("demarrage", out b) && a != b)
            {
                var olds = new HashSet<string>(a.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
                var news = new HashSet<string>(b.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
                var added = new List<string>(); var gone = new List<string>();
                foreach (var x in news) if (!olds.Contains(x)) added.Add(x);
                foreach (var x in olds) if (!news.Contains(x)) gone.Add(x);
                if (added.Count > 0) outp.Add("🚀 NOUVEAU au démarrage : " + string.Join(", ", added.ToArray()) + " — chaque ajout ralentit le boot et pèse en fond.");
                if (gone.Count > 0) outp.Add("Retiré du démarrage : " + string.Join(", ", gone.ToArray()) + ".");
            }
            int ga, gb;
            if (old.TryGetValue("disque_libre_go", out a) && now.TryGetValue("disque_libre_go", out b)
                && int.TryParse(a, out ga) && int.TryParse(b, out gb) && Math.Abs(gb - ga) >= 5)
                outp.Add("💽 Espace disque : " + ga + " → " + gb + " Go libres (" + (gb > ga ? "+" : "") + (gb - ga) + " Go).");
            return outp;
        }

        /// <summary>Texte pour le chat : compare l'instant présent à la photo la plus ancienne
        /// d'il y a au moins 1 jour (jusqu'à 30 j). Honnête si l'historique manque encore.</summary>
        public static string DiffText()
        {
            try
            {
                if (!Directory.Exists(Dir)) return NoHistory();
                var files = new List<string>(Directory.GetFiles(Dir, "????????.txt"));
                files.Sort(StringComparer.Ordinal);
                string today = DateTime.Now.ToString("yyyyMMdd");
                string baseline = null;
                foreach (var f in files)
                    if (string.CompareOrdinal(Path.GetFileNameWithoutExtension(f), today) < 0) baseline = f;   // la plus récente AVANT aujourd'hui
                if (baseline == null) return NoHistory();
                var diffs = Diff(Load(baseline), Capture());
                string when = Path.GetFileNameWithoutExtension(baseline);
                string dateFr = when.Substring(6, 2) + "/" + when.Substring(4, 2);
                if (diffs.Count == 0)
                    return "🔍 Comparé à ma photo du " + dateFr + " : RIEN n'a changé de notable (pilote GPU, Windows, démarrage, disque identiques).\n"
                         + "Si un jeu se comporte différemment, la cause est ailleurs — dis « ça crash » ou « ça rame » et j'enquête.";
                var sb = new System.Text.StringBuilder();
                sb.Append("🔍 Ce qui a CHANGÉ sur ton PC depuis le ").Append(dateFr).Append(" :\n");
                foreach (var x in diffs) sb.Append("• ").Append(x).Append('\n');
                sb.Append("(Photo quotidienne prise par le Gardien — moi, je n'ai rien touché sans ton clic : « journal » pour MES actions.)");
                return sb.ToString().TrimEnd();
            }
            catch { return NoHistory(); }
        }

        private static string NoHistory()
        {
            return "🔍 Pas encore de photo de référence — le Gardien photographie l'état du PC une fois par jour "
                 + "(pilote GPU, Windows, démarrage, disque). Dès demain, je saurai te dire ce qui a changé.";
        }
    }
}
