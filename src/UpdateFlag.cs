using System;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// « UNE MISE À JOUR M'ATTEND » — la mémoire entre deux lancements.
    ///
    /// La notification Windows est fugace : elle passe pendant que l'utilisateur n'est pas devant
    /// l'écran, ou n'apparaît pas du tout si les notifications sont coupées (fréquent chez les
    /// joueurs, justement). La vérification, elle, n'a lieu qu'une fois par jour — sans mémoire,
    /// l'information est perdue jusqu'au lendemain.
    ///
    /// On note donc la version disponible dans un fichier, et l'app l'affiche À CHAQUE ouverture
    /// tant qu'elle n'est pas installée. Le drapeau se retire tout seul dès que la version qui
    /// tourne rattrape celle qui était annoncée.
    /// </summary>
    internal static class UpdateFlag
    {
        private static string Chemin { get { return AppPaths.File("bt-maj-dispo.txt"); } }

        /// <summary>Note qu'une version est disponible (format « 15.63 »).</summary>
        public static void Set(string version)
        {
            try { if (!string.IsNullOrEmpty(version)) File.WriteAllText(Chemin, version.Trim() + "\n"); }
            catch { }
        }

        public static void Clear()
        {
            try { if (File.Exists(Chemin)) File.Delete(Chemin); }
            catch { }
        }

        /// <summary>Lecture PURE : le contenu du fichier est-il une version exploitable ?</summary>
        public static string Parse(string contenu)
        {
            if (string.IsNullOrEmpty(contenu)) return null;
            string s = contenu.Replace("\r", "").Split('\n')[0].Trim();
            if (s.Length == 0 || s.Length > 20) return null;
            return Updater.ParseTag(s) == null ? null : s;
        }

        /// <summary>
        /// Décision PURE : faut-il encore signaler <paramref name="annoncee"/> à quelqu'un qui
        /// tourne en <paramref name="courante"/> ? Non si la mise à jour a été installée entre-temps.
        /// </summary>
        public static bool ADevoirSignaler(string annoncee, Version courante)
        {
            Version v = Updater.ParseTag(annoncee);
            if (v == null) return false;
            return Updater.IsNewer(courante, v);
        }

        /// <summary>Version en attente, ou null. Le drapeau périmé est effacé au passage.</summary>
        public static string EnAttente()
        {
            try
            {
                if (!File.Exists(Chemin)) return null;
                string v = Parse(File.ReadAllText(Chemin));
                if (v == null) { Clear(); return null; }
                if (!ADevoirSignaler(v, Updater.CurrentVersion())) { Clear(); return null; }
                return v;
            }
            catch { return null; }
        }
    }
}
