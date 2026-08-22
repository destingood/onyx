using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// Suivi PERSISTANT des succès : les badges déjà gagnés le restent (même si l'état du PC
    /// change ensuite), plus des compteurs d'événements (Check Up réalisés, Mode Jeu déjà utilisé).
    /// Fichier bt-badges.txt à côté de l'exécutable.
    /// </summary>
    internal static class BadgeStore
    {
        private static readonly object _lock = new object();
        private static readonly HashSet<string> _earned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static int Checkups { get; private set; }
        public static bool BoostUsed { get; private set; }

        /// <summary>Déclenché quand un badge est gagné pour la PREMIÈRE fois (id du badge). Peut
        /// être levé depuis un thread de fond : les abonnés doivent marshaler vers l'UI.</summary>
        public static event Action<string> OnNewBadge;

        private static string StorePath { get { return AppPaths.File("bt-badges.txt"); } }

        static BadgeStore() { Load(); }

        private static void Load()
        {
            try
            {
                if (!File.Exists(StorePath)) return;
                foreach (string line in File.ReadAllLines(StorePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    if (k == "checkups") { int n; if (int.TryParse(v, out n)) Checkups = n; }
                    else if (k == "boost") BoostUsed = v == "1";
                    else if (k == "earned")
                        foreach (string id in v.Split(',')) { string t = id.Trim(); if (t.Length > 0) _earned.Add(t); }
                }
            }
            catch { }
        }

        private static void Save()
        {
            try
            {
                File.WriteAllLines(StorePath, new List<string>
                {
                    "checkups=" + Checkups,
                    "boost=" + (BoostUsed ? "1" : "0"),
                    "earned=" + string.Join(",", new List<string>(_earned).ToArray())
                });
            }
            catch { }
        }

        public static void IncCheckups(int n) { lock (_lock) { Checkups += n; Save(); } }
        public static void MarkBoostUsed() { lock (_lock) { if (!BoostUsed) { BoostUsed = true; Save(); } } }
        public static bool IsEarned(string id) { lock (_lock) { return _earned.Contains(id); } }

        /// <summary>Enregistre un badge comme gagné. Renvoie true si c'est un NOUVEAU badge.</summary>
        public static bool MarkEarned(string id)
        {
            bool added;
            lock (_lock) { added = _earned.Add(id); if (added) Save(); }
            if (added) { var h = OnNewBadge; if (h != null) try { h(id); } catch { } }
            return added;
        }
    }
}
