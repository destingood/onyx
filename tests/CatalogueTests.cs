using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Catalogue des réglages, lu dans le SOURCE (Tweaks.cs dépend du registre et ne se compile
    /// pas dans le banc) : identifiants uniques, et réglages sans gain mesuré annoncés comme tels.
    /// </summary>
    internal static class CatalogueTests
    {
        public static void Tout()
        {
            Banc.Titre("Catalogue des réglages");
            string src = Source();
            Banc.Verifie("src/Tweaks.cs trouvé", true, src != null);
            if (src == null) return;

            var vus = new HashSet<string>();
            var doublons = new List<string>();
            foreach (Match m in Regex.Matches(src, "\\bId = \"([^\"]+)\""))
                if (!vus.Add(m.Groups[1].Value)) doublons.Add(m.Groups[1].Value);
            Banc.Verifie("identifiants uniques" + (doublons.Count > 0 ? " (doublon : " + string.Join(", ", doublons) + ")" : ""),
                true, doublons.Count == 0);

            foreach (string id in new[] { "sysresp", "netthrottle", "tcp_bbr2" })
            {
                Match d = Regex.Match(src, "\\bId = \"" + id + "\"[\\s\\S]*?\\bDesc = \"((?:[^\"\\\\]|\\\\.)*)\"");
                Banc.Verifie(id + " : effet non démontré annoncé", true, d.Success && d.Groups[1].Value.Contains("non démontré"));
            }
        }

        private static string Source()
        {
            for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string f = Path.Combine(d.FullName, "src", "Tweaks.cs");
                if (File.Exists(f)) return File.ReadAllText(f);
            }
            return null;
        }
    }
}
