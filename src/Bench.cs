using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DTGOptimizer
{
    public class BenchResult
    {
        public DateTime When;
        public double TimerMs;      // résolution timer système
        public double SleepAvgMs;   // durée réelle moyenne d'un Sleep(1)
        public double SleepMaxMs;   // pire cas
        public double DpcAvg = -1, DpcMax = -1;       // % temps DPC (_Total)
        public double IrqAvg = -1, IrqMax = -1;       // % temps interruption
        public double DpcRateAvg = -1;                // DPC mis en file / s
        public double CtxAvg = -1;                    // changements de contexte / s

        public string ToText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== MESURE " + When.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
            sb.AppendLine("Timer systeme          : " + TimerMs.ToString("0.0") + " ms");
            sb.AppendLine("Sleep(1) reel          : moy " + SleepAvgMs.ToString("0.00") + " ms / max " + SleepMaxMs.ToString("0.00") + " ms");
            sb.AppendLine("% temps DPC (_Total)   : " + Fmt(DpcAvg) + " moy / " + Fmt(DpcMax) + " max");
            sb.AppendLine("% temps interruptions  : " + Fmt(IrqAvg) + " moy / " + Fmt(IrqMax) + " max");
            sb.AppendLine("DPC en file / s        : " + Fmt(DpcRateAvg) + " moy");
            sb.AppendLine("Chgts de contexte / s  : " + Fmt(CtxAvg) + " moy");
            return sb.ToString();
        }

        private static string Fmt(double v)
        {
            return v < 0 ? "n/d" : v.ToString("0.00");
        }
    }

    /// <summary>
    /// Mesures de latence : compteurs PDH (ajoutés par nom ANGLAIS, donc
    /// indépendants de la langue de Windows), gigue réelle de Sleep(1), et
    /// capture ETW DPC/ISR optionnelle via WPR + xperf (même méthodologie que
    /// les scripts de mesure existants du dossier).
    /// </summary>
    internal static class Bench
    {
        // ---------------------------- PDH ----------------------------
        [DllImport("pdh.dll")]
        private static extern uint PdhOpenQuery(IntPtr dataSource, IntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        private static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll")]
        private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr reserved, out PdhValue value);

        [DllImport("pdh.dll")]
        private static extern uint PdhCloseQuery(IntPtr query);

        [StructLayout(LayoutKind.Sequential)]
        private struct PdhValue
        {
            public uint CStatus;
            public double Value;
        }

        private const uint PDH_FMT_DOUBLE = 0x00000200;

        private class Meter
        {
            public IntPtr Handle;
            public bool Ok;
            public double Sum;
            public double Max;
            public int N;
            public double Avg { get { return N > 0 ? Sum / N : -1; } }
            public double MaxOr { get { return N > 0 ? Max : -1; } }
        }

        public static BenchResult Run(int seconds, Action<string, int> log)
        {
            var res = new BenchResult();
            res.When = DateTime.Now;
            res.TimerMs = Native.CurrentTimerMs();

            // 1) Compteurs système pendant N secondes
            log("Mesure des compteurs système (" + seconds + " s)...", 0);
            string[] paths =
            {
                @"\Processor(_Total)\% DPC Time",
                @"\Processor(_Total)\% Interrupt Time",
                @"\Processor(_Total)\DPCs Queued/sec",
                @"\System\Context Switches/sec"
            };
            var meters = new Meter[paths.Length];
            IntPtr query;
            bool pdhOk = PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out query) == 0;
            if (pdhOk)
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    meters[i] = new Meter();
                    IntPtr h;
                    meters[i].Ok = PdhAddEnglishCounter(query, paths[i], IntPtr.Zero, out h) == 0;
                    meters[i].Handle = h;
                }
                PdhCollectQueryData(query); // échantillon d'amorçage
                int ticks = seconds * 4;
                for (int t = 0; t < ticks; t++)
                {
                    Thread.Sleep(250);
                    if (PdhCollectQueryData(query) != 0) continue;
                    foreach (Meter m in meters)
                    {
                        if (!m.Ok) continue;
                        PdhValue v;
                        if (PdhGetFormattedCounterValue(m.Handle, PDH_FMT_DOUBLE, IntPtr.Zero, out v) == 0 && v.CStatus == 0)
                        {
                            m.Sum += v.Value;
                            if (v.Value > m.Max) m.Max = v.Value;
                            m.N++;
                        }
                    }
                }
                PdhCloseQuery(query);
                res.DpcAvg = meters[0].Avg; res.DpcMax = meters[0].MaxOr;
                res.IrqAvg = meters[1].Avg; res.IrqMax = meters[1].MaxOr;
                res.DpcRateAvg = meters[2].Avg;
                res.CtxAvg = meters[3].Avg;
            }
            else
            {
                log("Compteurs PDH indisponibles : mesure limitée au timer et à la gigue.", 2);
            }

            // 2) Gigue réelle de Sleep(1) — reflète timer + planificateur tels que configurés
            log("Test de précision du sommeil (Sleep 1 ms x 300)...", 0);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++) Thread.Sleep(1); // échauffement
            double sum = 0, max = 0;
            const int n = 300;
            for (int i = 0; i < n; i++)
            {
                long t0 = sw.ElapsedTicks;
                Thread.Sleep(1);
                double dt = (sw.ElapsedTicks - t0) * 1000.0 / Stopwatch.Frequency;
                sum += dt;
                if (dt > max) max = dt;
            }
            res.SleepAvgMs = sum / n;
            res.SleepMaxMs = max;
            return res;
        }

        // ------------------- Capture ETW (WPR + xperf) -------------------
        public static string XperfPath()
        {
            return @"C:\Program Files (x86)\Windows Kits\10\Windows Performance Toolkit\xperf.exe";
        }

        public static bool EtwAvailable(string appDir)
        {
            return File.Exists(XperfPath()) && File.Exists(Path.Combine(appDir, @"tools\dpc-trace.wprp"));
        }

        /// <summary>Capture DPC/ISR 30 s (méthodologie identique aux scripts du dossier). Retourne le chemin du rapport ou null.</summary>
        public static string RunEtw(string appDir, Action<string, int> log)
        {
            string wprp = Path.Combine(appDir, @"tools\dpc-trace.wprp");
            if (!EtwAvailable(appDir))
            {
                log("Capture ETW indisponible (xperf ou tools\\dpc-trace.wprp introuvable).", 2);
                return null;
            }
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string etl = Path.Combine(appDir, @"tools\trace-" + stamp + ".etl");
            string outTxt = Path.Combine(appDir, @"tools\dpcisr-" + stamp + ".txt");

            Sys.Run(Sys.Sys32("wpr.exe"), "-cancel"); // purge une session résiduelle
            log("Capture ETW DPC/ISR en cours (30 secondes)...", 0);
            NativeResult r = Sys.Run(Sys.Sys32("wpr.exe"), "-start \"" + wprp + "!DPC\" -filemode");
            if (r.ExitCode != 0) { log("wpr -start a échoué (code " + r.ExitCode + ").", 3); return null; }

            Thread.Sleep(30000);

            r = Sys.Run(Sys.Sys32("wpr.exe"), "-stop \"" + etl + "\"");
            if (r.ExitCode != 0)
            {
                log("wpr -stop a échoué (code " + r.ExitCode + ").", 3);
                Sys.Run(Sys.Sys32("wpr.exe"), "-cancel");
                return null;
            }
            log("Analyse xperf (dpcisr)...", 0);
            r = Sys.Run(XperfPath(), "-i \"" + etl + "\" -o \"" + outTxt + "\" -a dpcisr");
            if (r.ExitCode != 0) { log("xperf a échoué (code " + r.ExitCode + ").", 3); return null; }
            return outTxt;
        }
    }
}
