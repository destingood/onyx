using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// « LE POIDS MORT QUI ÉTOUFFE UN DISQUE » — les fichiers que PERSONNE ne regarde jamais :
    /// journaux d'applications partis en boucle (un SEUL fichier peut dépasser 70 Go) et restes de
    /// téléchargements Steam abandonnés depuis des mois. Sur un SSD presque plein, ce poids mort ne
    /// coûte pas que de la place : sous ~10 % de libre, le cache d'écriture s'effondre et le disque
    /// tombe à quelques Mo/s — le jeu met alors trois plombes à charger sans qu'on comprenne pourquoi.
    /// Balayage BORNÉ en temps ET en profondeur : l'analyse ne doit jamais figer l'application.
    /// ONYX ne supprime RIEN sans confirmation explicite : cette classe ne fait que TROUVER.
    /// </summary>
    internal static class JunkScan
    {
        /// <summary>En dessous d'1 Go, ça n'intéresse personne.</summary>
        public const long DefaultMinBytes = 1073741824L;
        /// <summary>Un journal encore écrit aujourd'hui appartient à une appli vivante : on n'y touche pas.</summary>
        public const int LogIdleDays = 7;
        /// <summary>Un téléchargement Steam figé depuis un mois est abandonné, pas « en pause ».</summary>
        public const int DownloadIdleDays = 30;
        /// <summary>Profondeur depuis la racine du disque : au-delà, on sort du domaine des gros journaux.</summary>
        private const int MaxDepth = 4;

        public enum Kind { FatLog, SteamLeftover }

        public sealed class Item
        {
            public string Path;
            public long Bytes;
            public Kind Kind;
            public bool IsFolder;
            public DateTime LastWrite;
            public string Reason;
        }

        // Extensions de pur journal/vidage : leur contenu n'a de valeur que le jour d'un incident.
        private static readonly string[] JunkExt = { ".log", ".etl", ".dmp", ".tmp" };

        // Dossiers système : hors-sujet, et dangereux à proposer à la suppression.
        private static readonly string[] Skip =
        {
            "$recycle.bin", "system volume information", "windows", "$windows.~bt", "$windows.~ws",
            "windows.old", "recovery", "boot", "perflogs", "config.msi", "msocache", "onedrivetemp"
        };

        private static bool IsSkipped(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            foreach (var s in Skip) if (n == s) return true;
            return false;
        }

        private static bool IsJunkExt(string path)
        {
            string e = Path.GetExtension(path ?? "").ToLowerInvariant();
            foreach (var x in JunkExt) if (e == x) return true;
            return false;
        }

        /// <summary>Taille d'un dossier, bornée par une échéance absolue (ms depuis le début du scan).</summary>
        private static long SizeWithin(string path, Stopwatch budget, long deadlineMs)
        {
            long bytes = 0;
            var stack = new Stack<string>();
            stack.Push(path);
            while (stack.Count > 0)
            {
                if (budget.ElapsedMilliseconds > deadlineMs) break;
                string d = stack.Pop();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(d))
                    {
                        try { bytes += new FileInfo(f).Length; } catch { }
                        if (budget.ElapsedMilliseconds > deadlineMs) break;
                    }
                    foreach (var sub in Directory.EnumerateDirectories(d)) stack.Push(sub);
                }
                catch { }   // accès refusé : on ignore ce sous-dossier, pas toute la mesure
            }
            return bytes;
        }

        /// <summary>
        /// Cherche le poids mort sur tous les disques fixes. <paramref name="totalBudgetMs"/> est un
        /// plafond DUR : dépassé, on rend ce qu'on a trouvé plutôt que de faire attendre l'utilisateur.
        /// </summary>
        public static List<Item> Scan(long minBytes, int totalBudgetMs, Action<string, int> log)
        {
            var found = new List<Item>();
            var budget = Stopwatch.StartNew();
            if (minBytes <= 0) minBytes = DefaultMinBytes;
            DateTime logCutoff = DateTime.Now.AddDays(-LogIdleDays);
            DateTime dlCutoff = DateTime.Now.AddDays(-DownloadIdleDays);

            // Les bibliothèques Steam sont DÉCLARÉES dans libraryfolders.vdf : on va droit au but au
            // lieu d'espérer que le parcours générique tombe dessus avant d'épuiser son budget. Sans
            // ça, le dossier « common » (des dizaines de milliers de fichiers) avale tout le temps
            // imparti et le reste de téléchargement abandonné passe inaperçu — précisément le cas
            // qu'on cherche à attraper.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var lib in SteamLibraries())
                {
                    string dl = Path.Combine(lib, "downloading");
                    if (!Directory.Exists(dl)) continue;
                    CollectSteamLeftovers(dl, minBytes, dlCutoff, budget, budget.ElapsedMilliseconds + 4000, found);
                }
                foreach (var i in found) seen.Add(i.Path);
            }
            catch { }

            var roots = new List<string>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try { if (d.DriveType == DriveType.Fixed && d.IsReady) roots.Add(d.RootDirectory.FullName); }
                    catch { }
                }
            }
            catch { }
            if (roots.Count == 0) { found.Sort((a, b) => b.Bytes.CompareTo(a.Bytes)); return found; }

            // Budget RÉPARTI par disque : sinon le premier avale tout et les disques de jeux
            // (souvent les plus encombrés) ne sont jamais analysés.
            long perDrive = Math.Max(2000, totalBudgetMs / roots.Count);

            foreach (var root in roots)
            {
                long driveDeadline = Math.Min(totalBudgetMs, budget.ElapsedMilliseconds + perDrive);
                if (log != null) log("Recherche du poids mort : " + root, 0);
                var stack = new Stack<KeyValuePair<string, int>>();
                stack.Push(new KeyValuePair<string, int>(root, 0));

                while (stack.Count > 0)
                {
                    if (budget.ElapsedMilliseconds > driveDeadline) break;
                    var cur = stack.Pop();
                    string dir = cur.Key;
                    int depth = cur.Value;

                    // --- Restes de téléchargement Steam : …\steamapps\downloading\* ---
                    try
                    {
                        if (string.Equals(Path.GetFileName(dir), "downloading", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(Path.GetFileName(Path.GetDirectoryName(dir) ?? ""), "steamapps", StringComparison.OrdinalIgnoreCase))
                        {
                            // Filet pour une bibliothèque absente de libraryfolders.vdf (copiée à la main).
                            int before = found.Count;
                            CollectSteamLeftovers(dir, minBytes, dlCutoff, budget, driveDeadline, found);
                            for (int k = found.Count - 1; k >= before; k--)
                                if (!seen.Add(found[k].Path)) found.RemoveAt(k);   // déjà vu par la passe ciblée
                            continue;   // traité en bloc : inutile de redescendre dedans
                        }
                    }
                    catch { }

                    // --- Journaux obèses ---
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(dir))
                        {
                            if (budget.ElapsedMilliseconds > driveDeadline) break;
                            if (!IsJunkExt(f)) continue;
                            try
                            {
                                var fi = new FileInfo(f);
                                if (fi.Length < minBytes) continue;
                                if (fi.LastWriteTime > logCutoff) continue;   // encore alimenté : appli vivante
                                found.Add(new Item
                                {
                                    Path = fi.FullName,
                                    Bytes = fi.Length,
                                    Kind = Kind.FatLog,
                                    IsFolder = false,
                                    LastWrite = fi.LastWriteTime,
                                    Reason = "journal d'application parti en boucle, plus écrit depuis "
                                             + Math.Max(1, (int)(DateTime.Now - fi.LastWriteTime).TotalDays) + " jours"
                                });
                            }
                            catch { }
                        }
                    }
                    catch { }

                    if (depth >= MaxDepth) continue;
                    try
                    {
                        foreach (var sub in Directory.EnumerateDirectories(dir))
                        {
                            if (depth == 0 && IsSkipped(Path.GetFileName(sub))) continue;
                            stack.Push(new KeyValuePair<string, int>(sub, depth + 1));
                        }
                    }
                    catch { }
                }
            }

            found.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            return found;
        }

        /// <summary>Chemins « steamapps » de toutes les bibliothèques déclarées (Steam éclate souvent
        /// les jeux sur plusieurs disques). Liste vide si Steam n'est pas installé.</summary>
        private static List<string> SteamLibraries()
        {
            var libs = new List<string>();
            try
            {
                string steam = SteamGames.SteamPath();
                if (steam == null) return libs;
                string vdf = null;
                try
                {
                    string f = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                    if (File.Exists(f)) vdf = File.ReadAllText(f);
                }
                catch { }
                foreach (var lib in SteamGames.ParseLibraryPaths(vdf, steam))
                    if (Directory.Exists(lib)) libs.Add(lib);
            }
            catch { }
            return libs;
        }

        /// <summary>Contenu d'un dossier « downloading » de Steam figé depuis trop longtemps.</summary>
        private static void CollectSteamLeftovers(string dir, long minBytes, DateTime cutoff,
                                                  Stopwatch budget, long deadlineMs, List<Item> found)
        {
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (budget.ElapsedMilliseconds > deadlineMs) return;
                    try
                    {
                        var di = new DirectoryInfo(sub);
                        if (di.LastWriteTime > cutoff) continue;    // téléchargement en cours : on n'y touche pas
                        long size = SizeWithin(sub, budget, Math.Min(deadlineMs, budget.ElapsedMilliseconds + 2500));
                        if (size < minBytes) continue;
                        found.Add(new Item
                        {
                            Path = di.FullName,
                            Bytes = size,
                            Kind = Kind.SteamLeftover,
                            IsFolder = true,
                            LastWrite = di.LastWriteTime,
                            Reason = "téléchargement Steam abandonné depuis "
                                     + Math.Max(1, (int)(DateTime.Now - di.LastWriteTime).TotalDays) + " jours"
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static long Total(List<Item> items)
        {
            long t = 0;
            if (items != null) foreach (var i in items) t += i.Bytes;
            return t;
        }

        /// <summary>Mise en forme PURE (testable sans disque) : ce que l'utilisateur lit avant de décider.</summary>
        public static string Format(List<Item> items, int top)
        {
            if (items == null || items.Count == 0)
                return "✅ Aucun poids mort trouvé : pas de journal obèse ni de téléchargement Steam abandonné.";
            var sb = new System.Text.StringBuilder();
            sb.Append("🧹 Poids mort trouvé — ").Append(SteamGames.Human(Total(items)))
              .Append(" récupérables sur ").Append(items.Count).Append(" élément(s) :\n\n");
            int n = 0;
            foreach (var i in items)
            {
                if (n++ >= top) break;
                sb.Append("• ").Append(SteamGames.Human(i.Bytes)).Append("  —  ").Append(i.Path).Append('\n')
                  .Append("   (").Append(i.Reason).Append(")\n");
            }
            if (items.Count > top) sb.Append("…et ").Append(items.Count - top).Append(" autre(s).\n");
            return sb.ToString();
        }

        /// <summary>Suppression EFFECTIVE, après confirmation de l'utilisateur uniquement.
        /// Renvoie les octets réellement libérés ; <paramref name="failed"/> liste ce qui a résisté.</summary>
        public static long Delete(List<Item> items, out List<string> failed, Action<string, int> log)
        {
            failed = new List<string>();
            long freed = 0;
            if (items == null) return 0;
            foreach (var i in items)
            {
                try
                {
                    if (i.IsFolder) Directory.Delete(i.Path, true);
                    else File.Delete(i.Path);
                    freed += i.Bytes;
                    if (log != null) log("Supprimé : " + i.Path + " (" + SteamGames.Human(i.Bytes) + ")", 1);
                }
                catch (Exception ex)
                {
                    failed.Add(i.Path + " — " + ex.Message);
                    if (log != null) log("Suppression impossible : " + i.Path + " (" + ex.Message + ")", 3);
                }
            }
            return freed;
        }
    }
}
