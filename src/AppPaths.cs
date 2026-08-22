using System;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// OÙ VIVENT TES DONNÉES. Historiquement, ONYX écrivait tout à côté de son exécutable — ce qui ne
    /// fonctionne que si le dossier est accessible en écriture (installé dans « Program Files », cela
    /// ne marche QUE grâce à l'élévation administrateur). Si l'app est lancée sans droits, ou copiée
    /// dans un dossier protégé, la mémoire du Copilote, le journal, la tendance santé et les photos
    /// du système seraient perdus SILENCIEUSEMENT — le pire cas.
    ///
    /// Règle désormais : on garde le dossier de l'exe quand il est inscriptible (comportement
    /// d'origine, aucune migration inutile) ; sinon on bascule vers %LOCALAPPDATA%\ONYX en RECOPIANT
    /// les données existantes. Le choix est fait une fois, en mémoire.
    /// </summary>
    internal static class AppPaths
    {
        private static string _dir;
        private static readonly object Gate = new object();

        /// <summary>Dossier de données réellement utilisable en écriture.</summary>
        public static string DataDir
        {
            get
            {
                if (_dir != null) return _dir;
                lock (Gate)
                {
                    if (_dir != null) return _dir;
                    string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                    if (IsWritable(exeDir)) { _dir = exeDir; return _dir; }

                    string fallback = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ONYX");
                    try { Directory.CreateDirectory(fallback); } catch { }
                    if (!IsWritable(fallback)) { _dir = exeDir; return _dir; }   // dernier recours : on n'aggrave pas
                    Migrate(exeDir, fallback);
                    _dir = fallback;
                    return _dir;
                }
            }
        }

        /// <summary>Chemin complet d'un fichier de données.</summary>
        public static string File(string name) { return Path.Combine(DataDir, name); }

        /// <summary>true si le dossier accepte réellement une écriture (le seul test qui vaille).</summary>
        public static bool IsWritable(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                string probe = Path.Combine(dir, "bt-write-probe.tmp");
                System.IO.File.WriteAllText(probe, "x");
                System.IO.File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Recopie les données existantes vers le nouveau dossier (jamais d'écrasement).</summary>
        private static void Migrate(string from, string to)
        {
            try
            {
                foreach (var pattern in new[] { "bt-*.txt", "bt-*.csv", "bt-*.md" })
                    foreach (var f in Directory.EnumerateFiles(from, pattern))
                    {
                        try
                        {
                            string dest = Path.Combine(to, Path.GetFileName(f));
                            if (!System.IO.File.Exists(dest)) System.IO.File.Copy(f, dest);
                        }
                        catch { }
                    }
                foreach (var sub in new[] { "bt-etat", "bt-savoir" })
                {
                    try
                    {
                        string src = Path.Combine(from, sub);
                        if (!Directory.Exists(src)) continue;
                        string dst = Path.Combine(to, sub);
                        Directory.CreateDirectory(dst);
                        foreach (var f in Directory.EnumerateFiles(src))
                        {
                            string dest = Path.Combine(dst, Path.GetFileName(f));
                            if (!System.IO.File.Exists(dest)) System.IO.File.Copy(f, dest);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>Phrase d'explication pour l'auto-diagnostic (où sont les données, et pourquoi).</summary>
        public static string Explain()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            bool moved = !string.Equals(DataDir.TrimEnd(Path.DirectorySeparatorChar),
                                        exeDir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            return moved
                ? "à côté de l'application, ce n'était pas possible en écriture → tes données sont dans « " + DataDir + " »"
                : "dans le dossier de l'application (« " + DataDir + " »)";
        }
    }
}
