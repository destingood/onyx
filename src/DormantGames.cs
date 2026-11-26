using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// « Jeux dormants » : croise la TAILLE sur le disque et la DATE DE DERNIÈRE PARTIE pour
    /// montrer où dort l'espace. Un disque système saturé fait ramer Windows entier — et on
    /// garde tous des jeux de 100 Go auxquels on n'a pas touché depuis un an.
    ///
    /// Honnêteté sur les dates : Steam enregistre la dernière partie dans ses appmanifest, c'est
    /// donc EXACT pour lui. Ailleurs (EA, Battle.net, hors launcher) Windows ne journalise rien
    /// de fiable — on affiche « inconnue » plutôt que d'inventer une date fausse.
    ///
    /// Le calcul de taille est coûteux (des milliers de fichiers) : résultats mis en cache dans
    /// bt-gamesize.txt et recalculés seulement si le dossier a changé.
    /// </summary>
    internal static class DormantGames
    {
        internal class Entry
        {
            public GameLibrary.InstalledGame Game;
            public long SizeMB;
            public DateTime LastPlayed;   // MinValue = pas de date
            public int DaysIdle;          // -1 = inconnu
            public bool Never;            // installé mais jamais lancé (Steam LastPlayed=0)

            /// <summary>Jamais joué OU pas touché depuis 3 mois : candidat à récupération d'espace.</summary>
            public bool IsDormant { get { return Never || DaysIdle >= 90; } }

            public string Name { get { return Game != null ? Game.Name : ""; } }
            public string Launcher { get { return Game != null ? Game.Launcher : ""; } }
            public string InstallDir { get { return Game != null ? Game.InstallDir : null; } }

            public string SizeText
            {
                get
                {
                    if (SizeMB <= 0) return "—";
                    return SizeMB >= 1024
                        ? (SizeMB / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " Go"
                        : SizeMB + " Mo";
                }
            }

            public string IdleText
            {
                get
                {
                    if (Never) return "jamais joué";
                    if (DaysIdle < 0) return "inconnue";
                    if (DaysIdle <= 1) return "aujourd'hui";
                    if (DaysIdle < 30) return "il y a " + DaysIdle + " j";
                    if (DaysIdle < 365) return "il y a " + (DaysIdle / 30) + " mois";
                    return "il y a " + (DaysIdle / 365) + " an(s)";
                }
            }
        }

        private static string CachePath
        {
            get { return AppPaths.File("bt-gamesize.txt"); }
        }

        /// <summary>Analyse complète. <paramref name="progress"/> reçoit (fait, total).</summary>
        public static List<Entry> Scan(Action<int, int> progress)
        {
            List<GameLibrary.InstalledGame> games;
            try { games = GameLibrary.ScanAll(); }
            catch { return new List<Entry>(); }

            Dictionary<string, string> cache = LoadCache();
            var list = new List<Entry>();
            List<JournalJeu.Partie> journal = JournalJeu.Parties;

            for (int i = 0; i < games.Count; i++)
            {
                GameLibrary.InstalledGame g = games[i];
                if (progress != null) { try { progress(i + 1, games.Count); } catch { } }
                if (string.IsNullOrEmpty(g.InstallDir)) continue;

                long mb = SizeMB(g.InstallDir, cache);
                if (mb <= 0) continue;                       // dossier illisible/vide : on ignore

                // Hors Steam, pas de date fiable côté launcher : la dernière partie vue par ONYX.
                DateTime last = g.LastPlayed;
                bool never = g.NeverPlayed;
                if (last == DateTime.MinValue)
                {
                    DateTime? vu = JournalJeu.DernierePartie(
                        JournalJeu.Filtre(journal, n => BibliothequeJeux.MemeJeu(g.Name, g.SteamAppId, null, n, 0)), null);
                    if (vu != null) { last = vu.Value; never = false; }
                }
                int days = last == DateTime.MinValue
                    ? -1
                    : Math.Max(0, (int)(DateTime.Now - last).TotalDays);

                list.Add(new Entry { Game = g, SizeMB = mb, LastPlayed = last, DaysIdle = days, Never = never });
            }

            SaveCache(cache);
            list.Sort((a, b) => Score(b).CompareTo(Score(a)));
            return list;
        }

        /// <summary>Priorité = gros ET pas joué depuis longtemps. Une date inconnue reste neutre.</summary>
        private static double Score(Entry e)
        {
            // Jamais joué = signal le plus fort (on l'assimile à ~2 ans d'inactivité). Inconnu =
            // neutre. Sinon, plus c'est ancien, plus ça pèse (plafonné à 2 ans).
            double idle = e.Never ? 730 : (e.DaysIdle < 0 ? 60 : Math.Min(e.DaysIdle, 730));
            return e.SizeMB * (idle / 30.0);
        }

        // ------------------------------------------------------------ taille (avec cache)
        private static long SizeMB(string dir, Dictionary<string, string> cache)
        {
            string stamp;
            try
            {
                if (!Directory.Exists(dir)) return 0;
                stamp = Directory.GetLastWriteTimeUtc(dir).Ticks.ToString(CultureInfo.InvariantCulture);
            }
            catch { return 0; }

            string hit;
            if (cache.TryGetValue(dir, out hit))
            {
                // Format « ticks|Mo » : on ne recalcule que si le dossier a bougé.
                int bar = hit.IndexOf('|');
                if (bar > 0 && hit.Substring(0, bar) == stamp)
                {
                    long cached;
                    if (long.TryParse(hit.Substring(bar + 1), out cached)) return cached;
                }
            }

            bool part;
            long bytes = DiskWalk.Bytes(dir, 12, 0, out part);   // jonctions ignorées, profondeur ≤ 12
            long mb = bytes / 1048576;
            cache[dir] = stamp + "|" + mb.ToString(CultureInfo.InvariantCulture);
            return mb;
        }

        // ------------------------------------------------------------ cache disque
        private static Dictionary<string, string> LoadCache()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(CachePath)) return d;
                foreach (string line in File.ReadAllLines(CachePath))
                {
                    int eq = line.LastIndexOf('=');
                    if (eq > 0) d[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
            }
            catch { }
            return d;
        }

        private static void SaveCache(Dictionary<string, string> cache)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (KeyValuePair<string, string> kv in cache)
                    sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
                File.WriteAllText(CachePath, sb.ToString());
            }
            catch { }
        }
    }
}
