using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;

namespace BTOptimizer
{
    /// <summary>
    /// Parcours disque rapide pour la page Stockage. La taille, les attributs et la date sont lus
    /// directement dans la réponse de l'énumération (FindNextFile) : aucun appel système de plus
    /// par fichier, contrairement à <c>new FileInfo(f).Length</c>. Jonctions jamais suivies
    /// (déjà comptées ailleurs, et risque de boucle) ; dossiers illisibles ignorés.
    /// </summary>
    internal static class DiskWalk
    {
        public sealed class Fichier { public string Path, Dir, Name, Ext; public long Bytes; public DateTime Modified; }

        // Fichier OneDrive « en ligne seulement » : sa taille annoncée ne vit pas sur le disque.
        private const FileAttributes CloudRecall = (FileAttributes)0x400000;   // RECALL_ON_DATA_ACCESS
        private const FileAttributes Ecartes = FileAttributes.Hidden | FileAttributes.System
                                               | FileAttributes.Offline | CloudRecall;

        private static EnumerationOptions Options(int maxDepth)
        {
            return new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = Math.Max(0, maxDepth),
                IgnoreInaccessible = true,
                AttributesToSkip = 0,            // cachés et système comptent dans une taille
                ReturnSpecialDirectories = false,
                BufferSize = 64 * 1024
            };
        }

        /// <summary>Octets d'un dossier, sous-dossiers jusqu'à <paramref name="maxDepth"/> niveaux
        /// (0 = le dossier seul). <paramref name="capMs"/> ≤ 0 : sans limite de temps ; sinon le
        /// parcours s'arrête au plafond et <paramref name="partial"/> passe à vrai.</summary>
        public static long Bytes(string dir, int maxDepth, int capMs, out bool partial)
        {
            partial = false;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            var sw = Stopwatch.StartNew();
            bool coupe = false;
            int n = 0;
            long total = 0;
            try
            {
                var en = new FileSystemEnumerable<long>(dir, (ref FileSystemEntry e) => e.Length, Options(maxDepth))
                {
                    ShouldIncludePredicate = (ref FileSystemEntry e) =>
                    {
                        if (capMs > 0 && (++n & 511) == 0 && sw.ElapsedMilliseconds > capMs) coupe = true;
                        return coupe || !e.IsDirectory;
                    },
                    ShouldRecursePredicate = (ref FileSystemEntry e) => (e.Attributes & FileAttributes.ReparsePoint) == 0
                };
                foreach (long b in en)
                {
                    if (coupe) { partial = true; break; }
                    total += b;
                }
            }
            catch { partial = true; }   // erreur imprévue : résultat incomplet, on le dit
            return total;
        }

        /// <summary>Fichiers d'au moins <paramref name="minBytes"/> sous <paramref name="root"/>,
        /// en largeur d'abord. Fichiers cachés, système, hors ligne ou cloud écartés ; dossiers non
        /// parcourus : jonctions, noms commençant par « . », noms de <paramref name="skipNames"/>,
        /// chemins sous <paramref name="excludeRoots"/>. Plafonds de temps et de nombre →
        /// <paramref name="partial"/>.</summary>
        public static List<Fichier> GrosFichiers(string root, long minBytes, ICollection<string> skipNames,
            List<string> excludeRoots, int capMs, int maxCount, out bool partial)
        {
            partial = false;
            var list = new List<Fichier>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return list;

            var exclus = new List<string>();
            if (excludeRoots != null)
                foreach (string r in excludeRoots)
                    if (!string.IsNullOrEmpty(r)) exclus.Add(r.TrimEnd('\\') + "\\");

            var sw = Stopwatch.StartNew();
            bool coupe = false;
            int n = 0;
            try
            {
                var en = new FileSystemEnumerable<Fichier>(root,
                    (ref FileSystemEntry e) => coupe ? null : Nouveau(ref e), Options(int.MaxValue))
                {
                    ShouldIncludePredicate = (ref FileSystemEntry e) =>
                    {
                        if (capMs > 0 && (++n & 511) == 0 && sw.ElapsedMilliseconds > capMs) coupe = true;
                        return coupe || (!e.IsDirectory && e.Length >= minBytes && (e.Attributes & Ecartes) == 0);
                    },
                    ShouldRecursePredicate = (ref FileSystemEntry e) =>
                    {
                        if ((e.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                        ReadOnlySpan<char> nm = e.FileName;
                        if (nm.Length == 0 || nm[0] == '.') return false;
                        if (skipNames != null && skipNames.Contains(nm.ToString())) return false;
                        return exclus.Count == 0 || !SousUn(e.ToFullPath(), exclus);
                    }
                };
                foreach (Fichier f in en)
                {
                    if (f == null) { partial = true; break; }
                    list.Add(f);
                    if (list.Count >= maxCount) { partial = true; break; }
                }
            }
            catch { partial = true; }   // erreur imprévue : résultat incomplet, on le dit
            return list;
        }

        private static Fichier Nouveau(ref FileSystemEntry e)
        {
            string name = e.FileName.ToString();
            return new Fichier
            {
                Path = e.ToFullPath(),
                Dir = e.Directory.ToString().TrimEnd('\\'),
                Name = name,
                Ext = Path.GetExtension(name),
                Bytes = e.Length,
                Modified = e.LastWriteTimeUtc.LocalDateTime
            };
        }

        private static bool SousUn(string dir, List<string> roots)
        {
            string probe = dir.TrimEnd('\\') + "\\";
            foreach (string r in roots)
                if (probe.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
