using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// JOURNAL DE BORD : chaque action qui CHANGE quelque chose (bouton IsChange du Copilote) est
    /// tracée — date, heure, libellé — dans bt-journal.txt à côté de l'exe. L'utilisateur peut
    /// demander « qu'est-ce que tu as changé ? » et obtenir l'historique daté. La confiance par
    /// la transparence : ONYX n'a rien à cacher de ce qu'il fait.
    /// </summary>
    internal static class Journal
    {
        private static readonly object Gate = new object();
        private static string PathFile
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-journal.txt"); }
        }

        /// <summary>Trace une action effectuée (appelé après un clic « changement » réussi).</summary>
        public static void Add(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return;
            try
            {
                lock (Gate)
                    File.AppendAllText(PathFile, DateTime.Now.ToString("dd/MM/yyyy HH:mm") + " — " + label.Trim() + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>Les n dernières entrées (les plus récentes en premier), ou liste vide.</summary>
        public static List<string> Tail(int n)
        {
            var outp = new List<string>();
            try
            {
                if (!File.Exists(PathFile)) return outp;
                string[] all = File.ReadAllLines(PathFile);
                for (int i = all.Length - 1; i >= 0 && outp.Count < n; i--)
                    if (!string.IsNullOrWhiteSpace(all[i])) outp.Add(all[i].Trim());
            }
            catch { }
            return outp;
        }

        /// <summary>Texte prêt pour le chat : l'historique, ou l'aveu honnête qu'il est vide.</summary>
        public static string TailText(int n)
        {
            var t = Tail(n);
            if (t.Count == 0)
                return "📓 Journal de bord : encore VIDE — je n'ai appliqué aucun changement depuis son ouverture.\n"
                     + "(Chaque bouton « changement » que tu cliques sera tracé ici, daté. Les mesures, elles, ne changent rien.)";
            var sb = new System.Text.StringBuilder();
            sb.Append("📓 Journal de bord — mes ").Append(t.Count).Append(" dernière(s) action(s) sur ce PC :\n");
            foreach (var l in t) sb.Append("• ").Append(l).Append('\n');
            sb.Append("(Tout est réversible — dis-moi ce que tu veux annuler.)");
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// TENDANCE SANTÉ : le score du cockpit (% d'optimisations actives), historisé à raison d'UNE
    /// mesure par jour (enregistrée par le Gardien à l'ouverture) dans bt-sante.csv. Le Copilote
    /// peut alors montrer l'ÉVOLUTION (« ↗ +6 pts en 7 jours », mini-graphe) — la preuve dans le
    /// temps, pas juste un chiffre du moment.
    /// </summary>
    internal static class HealthTrend
    {
        private static string PathFile
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-sante.csv"); }
        }

        /// <summary>Enregistre le score du jour (1 ligne max par jour ; re-mesure = remplace).</summary>
        public static void RecordToday(int score) { RecordAt(DateTime.Now.Date, score); }

        /// <summary>Testable : enregistre à une date donnée (déduplique par date, garde ~13 mois).</summary>
        public static void RecordAt(DateTime day, int score)
        {
            try
            {
                var lines = new List<string>();
                string key = day.ToString("yyyy-MM-dd");
                if (File.Exists(PathFile))
                    foreach (var l in File.ReadAllLines(PathFile))
                        if (!l.StartsWith(key) && l.Contains(";")) lines.Add(l);
                lines.Add(key + ";" + score);
                lines.Sort(StringComparer.Ordinal);
                if (lines.Count > 400) lines.RemoveRange(0, lines.Count - 400);
                File.WriteAllLines(PathFile, lines);
            }
            catch { }
        }

        private static List<(DateTime Day, int Score)> All()
        {
            var o = new List<(DateTime, int)>();
            try
            {
                if (!File.Exists(PathFile)) return o;
                foreach (var l in File.ReadAllLines(PathFile))
                {
                    var p = l.Split(';');
                    DateTime d; int sc;
                    if (p.Length == 2 && DateTime.TryParseExact(p[0], "yyyy-MM-dd",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out d) && int.TryParse(p[1], out sc))
                        o.Add((d, sc));
                }
                o.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            }
            catch { }
            return o;
        }

        private static readonly char[] Bars = { '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█' };

        /// <summary>Texte pour le chat : score du jour, deltas 7/30 jours, mini-graphe (14 points).</summary>
        public static string TrendText()
        {
            var all = All();
            if (all.Count == 0)
                return "📈 Pas encore d'historique de santé — le Gardien enregistre UNE mesure par jour à "
                     + "l'ouverture d'ONYX. Reviens dans quelques jours pour voir ta tendance.";
            var last = all[all.Count - 1];
            var sb = new System.Text.StringBuilder();
            sb.Append("📈 Santé du PC : ").Append(last.Item2).Append(" %");
            int? d7 = DeltaSince(all, 7), d30 = DeltaSince(all, 30);
            if (d7 != null) sb.Append("  ·  sur 7 j : ").Append(FmtDelta(d7.Value));
            if (d30 != null) sb.Append("  ·  sur 30 j : ").Append(FmtDelta(d30.Value));
            sb.Append('\n');
            int from = Math.Max(0, all.Count - 14);
            int min = int.MaxValue, max = int.MinValue;
            for (int i = from; i < all.Count; i++) { min = Math.Min(min, all[i].Item2); max = Math.Max(max, all[i].Item2); }
            sb.Append("Tendance : ");
            for (int i = from; i < all.Count; i++)
            {
                int idx = max == min ? 3 : (int)Math.Round((double)(all[i].Item2 - min) / (max - min) * 7);
                sb.Append(Bars[Math.Max(0, Math.Min(7, idx))]);
            }
            sb.Append("  (").Append(all.Count - from).Append(" jour(s), échelle ").Append(min).Append('-').Append(max).Append(" %)\n");
            sb.Append(d7 != null && d7.Value < 0
                ? "En baisse : dis « bilan complet » et je trouve ce qui a changé."
                : "Le score = % des optimisations ONYX actives. « TOUT optimiser » le monte d'un coup.");
            return sb.ToString();
        }
        private static string FmtDelta(int d) { return d > 0 ? "↗ +" + d + " pt(s)" : d < 0 ? "↘ " + d + " pt(s)" : "→ stable"; }
        private static int? DeltaSince(List<(DateTime Day, int Score)> all, int days)
        {
            var last = all[all.Count - 1];
            int? baseline = null;
            foreach (var e in all) if (e.Item1 <= last.Item1.AddDays(-days)) baseline = e.Item2;   // le plus récent ≤ J-N
            if (baseline == null) return null;
            return last.Item2 - baseline.Value;
        }
    }

    /// <summary>
    /// LE GARDIEN : au lancement d'ONYX (1 fois par jour max), vérifie EN SILENCE les signaux
    /// vitaux — disque presque plein, santé SMART des disques, redémarrage Windows en attente,
    /// uptime excessif — et ne se manifeste QUE s'il y a une alerte (notification discrète).
    /// Zéro réseau, zéro pop-up si tout va bien : un gardien, pas une alarme de voiture.
    /// </summary>
    internal static class Guardian
    {
        private static string StampPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-gardien.txt"); }
        }

        private static string SnoozePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-gardien-veille.txt"); }
        }

        /// <summary>Met le Gardien en veille N jours (il continue d'enregistrer photo d'état et
        /// tendance santé — il ne PRÉVIENT simplement plus). Zéro harcèlement.</summary>
        public static void Snooze(int days)
        {
            try { File.WriteAllText(SnoozePath, DateTime.Now.Date.AddDays(days).ToString("yyyyMMdd")); } catch { }
            try { Journal.Add("Gardien mis en veille " + days + " jour(s)"); } catch { }
        }

        /// <summary>Date de fin de veille (null si actif). Testable.</summary>
        public static DateTime? SnoozedUntil()
        {
            try
            {
                if (!File.Exists(SnoozePath)) return null;
                DateTime d;
                if (!DateTime.TryParseExact(File.ReadAllText(SnoozePath).Trim(), "yyyyMMdd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out d)) return null;
                return d.Date >= DateTime.Now.Date ? (DateTime?)d : null;
            }
            catch { return null; }
        }

        /// <summary>Réveille le Gardien immédiatement.</summary>
        public static void Wake() { try { if (File.Exists(SnoozePath)) File.Delete(SnoozePath); } catch { } }

        /// <summary>true si les ALERTES sont coupées (veille). Les mesures, elles, continuent.</summary>
        public static bool AlertsMuted() { return SnoozedUntil() != null; }

        /// <summary>true si le passage quotidien n'a pas encore eu lieu aujourd'hui (indépendant de
        /// la veille : les mesures — photo d'état, tendance santé — doivent continuer).</summary>
        public static bool DueToday()
        {
            try
            {
                string today = DateTime.Now.ToString("yyyyMMdd");
                if (File.Exists(StampPath) && File.ReadAllText(StampPath).Trim() == today) return false;
                File.WriteAllText(StampPath, today);
                return true;
            }
            catch { return false; }
        }

        /// <summary>SOS post-crash : une application a planté il y a MOINS de 30 minutes ?
        /// (l'utilisateur relance souvent ONYX juste après un crash de jeu — on le remarque POUR lui).
        /// Renvoie « bo6.exe a crashé il y a 4 min », ou null si rien de frais.</summary>
        public static string FreshCrash()
        {
            try
            {
                foreach (var c in CrashScan.RecentDetailed(1))
                {
                    if (string.IsNullOrEmpty(c.Exe)) continue;
                    if (c.Exe.ToLowerInvariant().Contains("btoptimizer")) continue;   // pas nous-mêmes
                    double min = (DateTime.Now - c.Time).TotalMinutes;
                    if (min >= 0 && min <= 30)
                        return c.Exe + " a crashé il y a " + Math.Max(1, (int)min) + " min";
                }
            }
            catch { }
            return null;
        }

        /// <summary>Les alertes du moment (liste vide = tout va bien, le Gardien se tait).</summary>
        public static List<string> Alerts()
        {
            var a = new List<string>();
            try
            {
                var di = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                double freeGb = di.AvailableFreeSpace / 1073741824.0;
                if (freeGb < 10) a.Add("Disque système : " + freeGb.ToString("0") + " Go libres — Windows va ralentir (dis « libère de la place »).");
            }
            catch { }
            try
            {
                foreach (var d in Diagnostics.DiskHealth())
                    if (d.Status != 0)
                        a.Add("⚠️ SANTÉ DISQUE : « " + d.Name + " » signale « " + Diagnostics.DiskHealthLabel(d.Status)
                            + " » — SAUVEGARDE tes données importantes MAINTENANT.");
            }
            catch { }
            try { if (ChatActions.RebootPendingPublic()) a.Add("Windows attend un redémarrage pour finaliser des mises à jour."); } catch { }
            try
            {
                var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
                if (up.Days >= 14) a.Add("PC allumé depuis " + up.Days + " jours sans vrai redémarrage — redémarre à l'occasion.");
            }
            catch { }
            try
            {
                // On alerte sur les 2 DERNIERS JOURS : une crise passée (chiffres d'il y a une semaine)
                // ne doit pas déclencher une alarme ni pousser à des manips devenues inutiles.
                int g2 = CrashScan.GpuDriverErrors(2);
                if (g2 >= 15) a.Add("Pilote GPU : " + g2 + " erreurs sur les 2 derniers jours — instable EN CE MOMENT. "
                    + "Ouvre Check Up+ → « Mon pilote GPU est-il instable ? ».");
            }
            catch { }
            try
            {
                var cr = CrashScan.RecentDetailed(2);
                if (cr != null && cr.Count >= 2) a.Add(cr.Count + " crashs d'applications ces 48 h — dis « ça crash » au Copilote pour la cause exacte.");
            }
            catch { }
            return a;
        }
    }
}
