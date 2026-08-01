using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// « OÙ SONT PASSÉS MES GO ? » — le classement des plus gros dossiers, tous disques confondus.
    /// Beaucoup de jeux ne sont PAS dans Steam (Battle.net, EA, Epic, installations à la racine d'un
    /// disque) et échappent donc au scan des bibliothèques : cette mesure-là les voit tous, puisqu'elle
    /// regarde le disque lui-même. Budget de temps STRICT (l'analyse ne doit jamais bloquer l'app) et
    /// résultat annoncé comme PARTIEL si le budget est épuisé — jamais de chiffre présenté comme sûr
    /// alors qu'il est incomplet.
    /// </summary>
    internal static class BigFolders
    {
        public sealed class Folder { public string Path; public string Name; public long Bytes; public bool Partial; }

        // Dossiers système : hors-sujet (et dangereux à suggérer de supprimer).
        private static readonly string[] Skip =
        {
            "$recycle.bin", "system volume information", "windows", "$windows.~bt", "$windows.~ws",
            "recovery", "boot", "perflogs", "config.msi", "documents and settings", "onedrivetemp"
        };

        private static bool IsSkipped(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            foreach (var s in Skip) if (n == s) return true;
            return false;
        }

        /// <summary>Taille d'un dossier, bornée par une ÉCHÉANCE absolue (ms depuis le début).</summary>
        private static long SizeWithin(string path, Stopwatch budget, long deadlineMs, out bool partial)
        {
            long bytes = 0;
            partial = false;
            var stack = new Stack<string>();
            stack.Push(path);
            while (stack.Count > 0)
            {
                if (budget.ElapsedMilliseconds > deadlineMs) { partial = true; break; }
                string d = stack.Pop();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(d))
                    {
                        try { bytes += new FileInfo(f).Length; } catch { }
                        if (budget.ElapsedMilliseconds > deadlineMs) { partial = true; break; }
                    }
                    foreach (var sub in Directory.EnumerateDirectories(d)) stack.Push(sub);
                }
                catch { }   // accès refusé : on ignore ce sous-dossier, pas toute la mesure
            }
            return bytes;
        }

        /// <summary>Les plus gros dossiers de premier niveau de tous les disques fixes.</summary>
        public static List<Folder> Scan(int totalBudgetMs, Action<string, int> log)
        {
            var outp = new List<Folder>();
            var budget = Stopwatch.StartNew();
            try
            {
                var roots = new List<string>();
                foreach (var d in DriveInfo.GetDrives())
                {
                    try { if (d.DriveType == DriveType.Fixed && d.IsReady) roots.Add(d.RootDirectory.FullName); }
                    catch { }
                }
                // Budget RÉPARTI par disque : sinon le premier disque consomme tout et les autres
                // (souvent ceux qui portent les jeux !) ne sont jamais analysés.
                if (roots.Count == 0) return Finish(outp);
                long perDrive = Math.Max(3000, totalBudgetMs / roots.Count);
                foreach (var root in roots)
                {
                    long driveDeadline = budget.ElapsedMilliseconds + perDrive;
                    string[] dirs;
                    try { dirs = Directory.GetDirectories(root); } catch { continue; }
                    // Part égale du temps du disque pour chaque dossier de premier niveau.
                    long perFolder = Math.Max(600, perDrive / Math.Max(1, dirs.Length));
                    foreach (var dir in dirs)
                    {
                        if (budget.ElapsedMilliseconds > driveDeadline) break;         // au disque suivant
                        if (budget.ElapsedMilliseconds > totalBudgetMs) return Finish(outp);
                        string name = Path.GetFileName(dir);
                        if (IsSkipped(name)) continue;
                        if (log != null) log("Analyse : " + dir, 0);
                        bool partial;
                        long folderDeadline = Math.Min(driveDeadline, budget.ElapsedMilliseconds + perFolder);
                        long size = SizeWithin(dir, budget, folderDeadline, out partial);
                        if (size >= 5L * 1073741824L)   // en dessous de 5 Go, ça n'intéresse personne
                            outp.Add(new Folder { Path = dir, Name = name, Bytes = size, Partial = partial });
                    }
                }
            }
            catch { }
            return Finish(outp);
        }

        private static List<Folder> Finish(List<Folder> l)
        {
            l.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            return l;
        }

        /// <summary>Mise en forme PURE (testable) : le classement en texte.</summary>
        public static string Format(List<Folder> folders, int top)
        {
            if (folders == null || folders.Count == 0)
                return "📂 Aucun dossier de plus de 5 Go trouvé (ou analyse interrompue).";
            var sb = new System.Text.StringBuilder();
            long total = 0;
            foreach (var f in folders) total += f.Bytes;
            sb.Append("📂 Où sont passés tes Go — les plus gros dossiers (").Append(SteamGames.Human(total)).Append(" au total) :\n");
            int n = 0;
            bool anyPartial = false;
            foreach (var f in folders)
            {
                if (n++ >= top) break;
                sb.Append("• ").Append(f.Path).Append("  —  ").Append(SteamGames.Human(f.Bytes));
                if (f.Partial) { sb.Append("  (mesure partielle)"); anyPartial = true; }
                sb.Append('\n');
            }
            if (folders.Count > top) sb.Append("…et ").Append(folders.Count - top).Append(" autre(s) dossier(s) de plus de 5 Go.\n");
            if (anyPartial) sb.Append("(« mesure partielle » = le temps imparti a été atteint : le vrai poids est PLUS élevé.)\n");
            sb.Append("Je ne supprime rien : à toi de voir ce qui mérite de rester. Les jeux hors Steam "
                    + "(Battle.net, EA, Epic, installations manuelles) apparaissent ici alors qu'ils échappent au scan Steam.");
            return sb.ToString();
        }
    }
}
