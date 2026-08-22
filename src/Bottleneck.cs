using System;

namespace BTOptimizer
{
    /// <summary>
    /// « C'EST MON CPU OU MON GPU QUI ME LIMITE ? » — LA question de tout joueur, et personne n'y
    /// répond clairement. On échantillonne la charge CPU et l'utilisation GPU pendant que le jeu
    /// tourne, puis on tranche. Règle admise : en jeu, un GPU à ~97-100 % = c'est LUI qui travaille
    /// à fond (situation NORMALE et souhaitable) ; un GPU qui traîne pendant que le CPU sature =
    /// le processeur bride la carte graphique.
    /// </summary>
    internal static class Bottleneck
    {
        public sealed class Result
        {
            public string Title;
            public string Detail;
            public string Advice;
            public double CpuAvg, GpuAvg;
            public int Samples;
        }

        /// <summary>Verdict PUR à partir des moyennes (testable sans matériel). −1 = mesure absente.</summary>
        public static Result Verdict(double cpuAvg, double gpuAvg, int samples, bool gameRunning)
        {
            var r = new Result { CpuAvg = cpuAvg, GpuAvg = gpuAvg, Samples = samples };

            if (samples <= 0 || (cpuAvg < 0 && gpuAvg < 0))
            {
                r.Title = "Mesure impossible";
                r.Detail = "Ni la charge CPU ni l'utilisation GPU n'ont pu être lues.";
                r.Advice = "Vérifie que le pilote graphique est bien installé (nvidia-smi pour NVIDIA), puis réessaie.";
                return r;
            }

            if (!gameRunning)
            {
                r.Title = "Aucun jeu détecté pendant la mesure";
                r.Detail = "CPU ≈ " + Pct(cpuAvg) + ", GPU ≈ " + Pct(gpuAvg) + " — au bureau, ces chiffres ne veulent rien dire.";
                r.Advice = "LANCE TON JEU, mets-toi en pleine action (pas dans un menu), puis relance cette mesure. "
                         + "C'est la seule façon de savoir qui limite qui.";
                return r;
            }

            if (gpuAvg >= 93)
            {
                r.Title = "✅ C'est ton GPU qui travaille à fond — situation NORMALE";
                r.Detail = "GPU ≈ " + Pct(gpuAvg) + " d'utilisation, CPU ≈ " + Pct(cpuAvg) + ".";
                r.Advice = "Ta carte graphique est le facteur limitant, et c'est ce qu'on veut en jeu : rien ne se perd. "
                         + "Pour gagner des FPS : baisse les réglages les plus coûteux (ombres, réflexions, résolution "
                         + "d'échelle / DLSS-FSR en Qualité). Changer de processeur n'apporterait presque RIEN ici.";
                return r;
            }

            if (cpuAvg >= 70 && gpuAvg < 85)
            {
                r.Title = "⚠️ Ton PROCESSEUR bride ta carte graphique";
                r.Detail = "CPU ≈ " + Pct(cpuAvg) + " alors que le GPU n'est qu'à " + Pct(gpuAvg) + " : la carte attend le processeur.";
                r.Advice = "À faire, du gratuit vers le payant :\n"
                         + "1. Ferme ce qui tourne en fond (« qui bouffe mon CPU ? » dans le Copilote) — souvent 10-20 % récupérés ;\n"
                         + "2. Active XMP/EXPO dans le BIOS si ta RAM tourne sous sa vitesse : sur un CPU bridé, c'est LE gain gratuit ;\n"
                         + "3. Monte la résolution ou les réglages graphiques : contre-intuitif, mais ça redonne le travail au GPU et lisse le jeu ;\n"
                         + "4. Baisse les réglages qui pèsent sur le CPU : distance d'affichage, foule/PNJ, ombres dynamiques, ray tracing ;\n"
                         + "5. En dernier recours seulement : un processeur plus récent.";
                return r;
            }

            if (gpuAvg < 85 && cpuAvg < 70)
            {
                r.Title = "🔎 Ni le CPU ni le GPU ne sont saturés — quelque chose d'autre limite";
                r.Detail = "CPU ≈ " + Pct(cpuAvg) + ", GPU ≈ " + Pct(gpuAvg) + " : les deux ont de la marge.";
                r.Advice = "Causes classiques dans cet ordre :\n"
                         + "1. Une LIMITE d'images est active (V-Sync, limite FPS dans le jeu ou le panneau du pilote) — la cause n°1 ;\n"
                         + "2. Le jeu est bridé par sa propre boucle (moteur, mode fenêtré) ;\n"
                         + "3. Chargements disque (HDD, ou disque presque plein) ;\n"
                         + "4. Bridage thermique : vérifie le panneau Températures pendant une partie.";
                return r;
            }

            r.Title = "⚖️ Équilibré — pas de goulot d'étranglement net";
            r.Detail = "CPU ≈ " + Pct(cpuAvg) + ", GPU ≈ " + Pct(gpuAvg) + ".";
            r.Advice = "Aucun des deux ne bride franchement l'autre : ta configuration est cohérente. "
                     + "Pour plus de FPS, les réglages du jeu restent le levier gratuit le plus efficace.";
            return r;
        }

        private static string Pct(double v) { return v < 0 ? "n/d" : Math.Round(v) + " %"; }

        /// <summary>Mesure réelle : N échantillons espacés d'une seconde, pendant que le jeu tourne.</summary>
        public static Result Measure(int seconds, Action<string, int> log)
        {
            double cpuSum = 0, gpuSum = 0;
            int cpuN = 0, gpuN = 0, n = 0;
            bool game = false;
            try
            {
                using (var mon = new HwMonitor())
                {
                    for (int i = 0; i < seconds; i++)
                    {
                        HwSample s = null;
                        try { s = mon.Sample(); } catch { }
                        if (s != null)
                        {
                            if (s.CpuLoad >= 0) { cpuSum += s.CpuLoad; cpuN++; }
                            if (s.Gpu != null && s.Gpu.Ok && s.Gpu.Util >= 0) { gpuSum += s.Gpu.Util; gpuN++; }
                            n++;
                        }
                        // « un jeu tourne » = fenêtre plein écran active, ou GPU réellement sollicité
                        // (certains jeux en fenêtré sans bordure ne sont pas vus comme plein écran).
                        try { if (!game && Native.IsGameFullscreen()) game = true; } catch { }
                        try { if (!game && s != null && s.Gpu != null && s.Gpu.Ok && s.Gpu.Util >= 50) game = true; } catch { }
                        if (log != null && i % 3 == 0)
                            log("Mesure en cours… " + (i + 1) + "/" + seconds + " s" + (game ? " (jeu détecté)" : ""), 0);
                        System.Threading.Thread.Sleep(1000);
                    }
                }
            }
            catch { }
            double cpuAvg = cpuN > 0 ? cpuSum / cpuN : -1;
            double gpuAvg = gpuN > 0 ? gpuSum / gpuN : -1;
            // On GARDE la mesure : elle sert ensuite à décider si le profil pilote « Ultra faible
            // latence » est bénéfique ou nocif sur CETTE machine. Sans mémoire, chaque partie de
            // l'app redemanderait à l'utilisateur de relancer un jeu pour trancher.
            if (game) Remember(cpuAvg, gpuAvg);
            return Verdict(cpuAvg, gpuAvg, n, game);
        }

        private static string MemPath { get { return AppPaths.File("bt-goulot.txt"); } }

        /// <summary>Sérialisation PURE : « cpu;gpu;date ».</summary>
        public static string Render(double cpuAvg, double gpuAvg, DateTime when)
        {
            return cpuAvg.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + ";"
                 + gpuAvg.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + ";"
                 + when.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "\n";
        }

        /// <summary>Lecture PURE. false si le contenu n'est pas exploitable.</summary>
        public static bool Parse(string content, out double cpuAvg, out double gpuAvg, out DateTime when)
        {
            cpuAvg = -1; gpuAvg = -1; when = DateTime.MinValue;
            if (string.IsNullOrEmpty(content)) return false;
            string[] p = content.Replace("\r", "").Split('\n')[0].Split(';');
            if (p.Length < 2) return false;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (!double.TryParse(p[0], System.Globalization.NumberStyles.Float, inv, out cpuAvg)) return false;
            if (!double.TryParse(p[1], System.Globalization.NumberStyles.Float, inv, out gpuAvg)) return false;
            if (p.Length >= 3)
                DateTime.TryParseExact(p[2], "yyyy-MM-dd", inv, System.Globalization.DateTimeStyles.None, out when);
            return true;
        }

        private static void Remember(double cpuAvg, double gpuAvg)
        {
            try { System.IO.File.WriteAllText(MemPath, Render(cpuAvg, gpuAvg, DateTime.Now.Date)); }
            catch { }
        }

        /// <summary>Dernière mesure faite EN JEU, ou false si l'utilisateur n'en a jamais lancé.</summary>
        public static bool LastMeasure(out double cpuAvg, out double gpuAvg, out DateTime when)
        {
            cpuAvg = -1; gpuAvg = -1; when = DateTime.MinValue;
            try
            {
                if (!System.IO.File.Exists(MemPath)) return false;
                return Parse(System.IO.File.ReadAllText(MemPath), out cpuAvg, out gpuAvg, out when);
            }
            catch { return false; }
        }

        /// <summary>Texte complet prêt pour une fenêtre ou la console.</summary>
        public static string Text(Result r)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(r.Title).Append("\r\n\r\n").Append(r.Detail).Append("\r\n\r\n").Append(r.Advice.Replace("\n", "\r\n"));
            if (r.Samples > 0) sb.Append("\r\n\r\n(").Append(r.Samples).Append(" mesure(s) — moyenne sur la durée du test.)");
            return sb.ToString();
        }
    }
}
