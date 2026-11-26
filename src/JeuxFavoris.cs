using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Jeux favoris ★ de la bibliothèque : ils passent en tête de la page Jeux. Purement cosmétique,
    /// réversible d'un clic droit. Fichier bt-jeux-favoris.txt, une clé par ligne
    /// (<see cref="BibliothequeJeux.Cle"/> : un favori le reste malgré ™ ou une mention d'édition).
    /// </summary>
    internal static class JeuxFavoris
    {
        private const string Fichier = "bt-jeux-favoris.txt";
        private static HashSet<string> _set;
        private static readonly object _lock = new object();

        private static HashSet<string> Set()
        {
            if (_set != null) return _set;
            _set = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                string p = AppPaths.File(Fichier);
                if (File.Exists(p))
                    foreach (string line in File.ReadAllLines(p, Encoding.UTF8))
                        if (line.Trim().Length > 0) _set.Add(line.Trim());
            }
            catch { }
            return _set;
        }

        public static bool Est(string nom)
        {
            string k = BibliothequeJeux.Cle(nom);
            if (k.Length == 0) return false;
            lock (_lock) { return Set().Contains(k); }
        }

        public static void Bascule(string nom)
        {
            string k = BibliothequeJeux.Cle(nom);
            if (k.Length == 0) return;
            lock (_lock)
            {
                if (!Set().Remove(k)) Set().Add(k);
                try
                {
                    string p = AppPaths.File(Fichier), tmp = p + ".tmp";
                    File.WriteAllText(tmp, string.Join("\n", _set) + "\n", new UTF8Encoding(false));
                    File.Move(tmp, p, true);
                }
                catch { }
            }
        }
    }
}
