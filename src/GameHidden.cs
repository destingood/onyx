using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// Jeux masqués de la bibliothèque. Action PUREMENT COSMÉTIQUE : rien n'est touché sur le
    /// disque, et c'est réversible à tout moment. Sert surtout aux faux positifs du scanner
    /// (démos, utilitaires, outils détectés comme jeux) et aux titres qu'on ne veut plus voir.
    ///
    /// Stocké dans bt-games-hidden.txt : le préfixe « bt- » le fait survivre aux recompilations
    /// et aux mises à jour de l'installateur, comme la licence et les caches.
    /// </summary>
    internal static class GameHidden
    {
        private static HashSet<string> _set;
        private static readonly object _lock = new object();

        private static string StorePath
        {
            get { return AppPaths.File("bt-games-hidden.txt"); }
        }

        private static HashSet<string> Set()
        {
            if (_set != null) return _set;
            _set = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(StorePath))
                    foreach (string line in File.ReadAllLines(StorePath))
                    {
                        string k = Key(line);
                        if (k.Length > 0) _set.Add(k);
                    }
            }
            catch { }
            return _set;
        }

        public static bool IsHidden(string name)
        {
            string k = Key(name);
            if (k.Length == 0) return false;
            lock (_lock) { return Set().Contains(k); }
        }

        public static void SetHidden(string name, bool hidden)
        {
            string k = Key(name);
            if (k.Length == 0) return;
            lock (_lock)
            {
                if (hidden) Set().Add(k); else Set().Remove(k);
                Save();
            }
        }

        public static int Count { get { lock (_lock) { return Set().Count; } } }

        /// <summary>Réaffiche tous les jeux masqués.</summary>
        public static void ClearAll()
        {
            lock (_lock) { Set().Clear(); Save(); }
        }

        private static void Save()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (string k in _set) sb.Append(k).Append('\n');
                File.WriteAllText(StorePath, sb.ToString());
            }
            catch { }
        }

        // Clé normalisée (minuscules, alphanumérique) : un jeu masqué le RESTE même si le
        // scanner le redétecte sous une graphie légèrement différente (™, ®, ponctuation).
        private static string Key(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
    }
}
