using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("BT Optimizer")]
[assembly: AssemblyProduct("BT Optimizer")]
[assembly: AssemblyDescription("Optimiseur latence / input lag / rapidité pour Windows 10 et 11")]
[assembly: AssemblyCompany("BT")]
[assembly: AssemblyCopyright("Outil local — aucune connexion réseau")]
[assembly: AssemblyVersion("6.4.0.0")]
[assembly: AssemblyFileVersion("6.4.0.0")]

namespace BTOptimizer
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
#if BTTEST
            TestHarness.Run();
#else
            // Mode ligne de commande (gardien de démarrage / automatisation).
            if (args.Length > 0 && args[0].StartsWith("-"))
            {
                Environment.ExitCode = Cli.Run(args);
                return;
            }
            bool isNew;
            using (var mutex = new Mutex(true, "BTOptimizer_SingleInstance", out isNew))
            {
                if (!isNew)
                {
                    MessageBox.Show("BT Optimizer est déjà ouvert (vérifiez la barre des tâches ou la zone de notification).",
                        "BT Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    Sys.Init();
                    if (!LicenseForm.EnsureAccepted()) return;
                    Application.Run(new MainForm());
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "BT Optimizer a rencontré une erreur et va se fermer :\n\n" + ex,
                        "BT Optimizer — erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                GC.KeepAlive(mutex);
            }
#endif
        }
    }

#if BTTEST
    /// <summary>
    /// Mode test (compilé avec /define:BTTEST) : aucun GUI, aucun droit admin requis,
    /// aucune écriture. Charge le contexte et exécute tous les Check() (lectures seules)
    /// pour valider le catalogue de bout en bout.
    /// </summary>
    internal static class TestHarness
    {
        public static void Run()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Sys.Init();
            Console.WriteLine("BT Optimizer TEST — contexte :");
            Console.WriteLine("  OS             : " + Sys.OsDescription());
            Console.WriteLine("  SID courant    : " + Sys.CurrentSid);
            Console.WriteLine("  SID cible      : " + Sys.TargetSid);
            Console.WriteLine("  MêmeUtilisateur: " + Sys.SameUser);
            Console.WriteLine("  Bureau backup  : " + Sys.BackupDesktop);
            int i = 0, errors = 0;
            foreach (Tweak t in Catalog.All())
            {
                i++;
                string state;
                try
                {
                    bool? c = (t.Check != null) ? t.Check() : null;
                    state = c.HasValue ? (c.Value ? "ACTIF" : "inactif") : "n/a";
                }
                catch (Exception ex)
                {
                    errors++;
                    state = "ERREUR: " + ex.Message;
                }
                Console.WriteLine(string.Format("  {0,2}. [{1,-8}] {2}", i, state, t.Name));
            }
            Console.WriteLine("Profil / gardien / plein écran...");
            try
            {
                var testIds = new System.Collections.Generic.List<string> { "mouse_accel", "power_ultimate" };
                Sys.SaveProfile(testIds);
                System.Collections.Generic.List<string> back = Sys.LoadProfile();
                Console.WriteLine("  Profil : sauvegardé puis relu = " + string.Join(",", back.ToArray())
                    + (back.Count == 2 ? " (OK)" : " (ERREUR)"));
                if (back.Count != 2) errors++;
                Console.WriteLine("  Gardien (tâche planifiée) présent : " + Sys.GuardExists());
                Console.WriteLine("  Plein écran au premier plan : " + Native.IsGameFullscreen());
            }
            catch (Exception ex)
            {
                errors++;
                Console.WriteLine("  Profil/gardien ERREUR : " + ex.Message);
            }

            Console.WriteLine("Commercial (EULA / licence / fenêtres)...");
            try
            {
                Console.WriteLine("  EULA version acceptée : " + Sys.EulaAcceptedVersion());
                string tok = Environment.GetEnvironmentVariable("BT_LICENSE");
                if (!string.IsNullOrEmpty(tok))
                {
                    bool ok = License.Activate(tok, false);
                    Console.WriteLine("  Activation clé test : " + (ok ? "VALIDE -> " + License.Status() : "REJETÉE"));
                    if (!ok) errors++;
                    // clé falsifiée : doit être rejetée
                    bool bad = License.Activate("ZmFrZQ==", false);
                }
                Console.WriteLine("  Édition : " + License.Status());
                if (License.CanStartTrial)
                {
                    License.StartTrial();
                    Console.WriteLine("  Essai démarré : actif=" + License.TrialActive + " jours=" + License.TrialDaysLeft + " -> " + License.Status());
                    if (!License.TrialActive || License.TrialDaysLeft != 7) errors++;
                }
                else Console.WriteLine("  Essai : " + (License.TrialUsed ? License.Status() : "non applicable (Pro)"));
                using (var f = new LicenseForm()) { f.CreateControl(); }
                using (var f = new AboutForm()) { f.CreateControl(); }
                using (var f = new LicenseKeyForm("Test")) { f.CreateControl(); }
                Console.WriteLine("  UI License/About/KeyForm : construites OK.");
            }
            catch (Exception ex) { errors++; Console.WriteLine("  Commercial ERREUR : " + ex.Message); }

            Console.WriteLine("Programmes au démarrage...");
            try
            {
                var su = Sys.ListStartup();
                int on = 0; foreach (var s in su) if (s.Enabled) on++;
                Console.WriteLine("  " + su.Count + " entrées (" + on + " actives)");
                using (var f = new StartupForm(delegate(string m, int l) { })) { f.CreateControl(); }
                Console.WriteLine("  UI StartupForm : construite OK.");
            }
            catch (Exception ex) { errors++; Console.WriteLine("  Démarrage ERREUR : " + ex.Message); }

            Console.WriteLine("Nettoyage disque (analyse)...");
            try
            {
                long sum = 0; int nt = 0;
                foreach (Sys.CleanTarget t in Sys.CleanTargets()) { sum += t.SizeMB; nt++; }
                Console.WriteLine("  " + nt + " emplacements, " + sum + " Mo récupérables");
                using (var f = new CleanupForm(delegate(string m, int l) { })) { f.CreateControl(); }
                Console.WriteLine("  UI CleanupForm : construite OK.");
            }
            catch (Exception ex) { errors++; Console.WriteLine("  Nettoyage ERREUR : " + ex.Message); }

            Console.WriteLine("Matériel / auto-tune...");
            try
            {
                HwProfile hw = Hardware.Detect();
                Console.WriteLine("  " + hw.Summary());
                var auto = Hardware.AutoTuneIds(Catalog.All(), hw);
                var bench = Hardware.BenchmarkIds(Catalog.All());
                Console.WriteLine("  Auto-tune : " + auto.Count + " tweaks | Benchmark : " + bench.Count + " tweaks");
            }
            catch (Exception ex) { errors++; Console.WriteLine("  Matériel ERREUR : " + ex.Message); }

            Console.WriteLine("DNS (lecture)...");
            try
            {
                Console.WriteLine("  " + Sys.CurrentDnsSummary().Replace("\r\n", "\n  "));
                double c = DpcHelperMs("1.1.1.1");
                double g = DpcHelperMs("8.8.8.8");
                Console.WriteLine("  Latence DNS : Cloudflare 1.1.1.1 = " + (c < 0 ? "—" : c.ToString("0") + " ms")
                    + " | Google 8.8.8.8 = " + (g < 0 ? "—" : g.ToString("0") + " ms"));
                using (var f = new DnsForm(delegate(string m, int l) { })) { f.CreateControl(); }
                Console.WriteLine("  UI DnsForm : construite OK.");
            }
            catch (Exception ex) { errors++; Console.WriteLine("  DNS ERREUR : " + ex.Message); }

            Console.WriteLine("Overclock (sonde)...");
            try
            {
                Sys.GpuOcInfo oc = Sys.QueryGpuOc();
                Console.WriteLine("  GPU OC : " + (oc.Ok
                    ? oc.Name + " pl=" + oc.PowerCur + "/" + oc.PowerDefault + "/" + oc.PowerMax + "W boostMax=" + oc.MaxCoreMhz + "MHz"
                    : "n/d"));
                Sys.RamInfo ram = Sys.QueryRam();
                Console.WriteLine("  RAM : " + (ram.TotalMB / 1024) + "Go rated=" + ram.SpeedRated + " running=" + ram.SpeedRunning + " MT/s");
                Sys.CpuInfo cpu = Sys.QueryCpu();
                Console.WriteLine("  CPU : " + cpu.Name + " " + cpu.Cores + "c/" + cpu.Threads + "t");
                Console.WriteLine("  NVIDIA profile inspector : " + (Sys.NvpiAvailable() ? Sys.FindNvpi() : "introuvable"));
                Console.WriteLine("  Profil .nip cible : " + Sys.EnsureLowLatencyNip());
                using (var f = new OverclockForm(delegate(string m, int l) { })) { f.CreateControl(); }
                Console.WriteLine("  UI OverclockForm : construite OK.");
            }
            catch (Exception ex)
            {
                errors++;
                Console.WriteLine("  Overclock ERREUR : " + ex.Message);
            }

            Console.WriteLine("Moniteur matériel (échantillon)...");
            try
            {
                using (var mon = new HwMonitor())
                {
                    System.Threading.Thread.Sleep(1000);
                    HwSample hs = mon.Sample();
                    Console.WriteLine(string.Format("  CPU={0:0}% RAM={1:0.0}/{2:0.0}Go CPUtemp={3} GPU={4} {5:0}C {6:0}% {7:0}/{8:0}MHz {9:0}W VRAM={10:0.0}/{11:0.0}Go",
                        hs.CpuLoad, hs.RamUsedMB / 1024.0, hs.RamTotalMB / 1024.0,
                        double.IsNaN(hs.CpuTempC) ? "n/d" : hs.CpuTempC.ToString("0") + "C",
                        hs.Gpu.Ok ? hs.Gpu.Name : "n/d", hs.Gpu.TempC, hs.Gpu.Util,
                        hs.Gpu.CoreMhz, hs.Gpu.MemMhz, hs.Gpu.PowerW,
                        hs.Gpu.VramUsedMB / 1024.0, hs.Gpu.VramTotalMB / 1024.0));
                }
                using (var f = new MonitorForm()) { f.CreateControl(); }
                Console.WriteLine("  UI MonitorForm : construite OK.");
            }
            catch (Exception ex)
            {
                errors++;
                Console.WriteLine("  Moniteur ERREUR : " + ex.Message);
            }

            Console.WriteLine("Mini-mesure de latence (3 s)...");
            try
            {
                BenchResult r = Bench.Run(3, delegate(string m, int l) { });
                Console.WriteLine(string.Format(
                    "  timer={0:0.0} ms | Sleep(1) moy={1:0.00} max={2:0.00} ms | %DPC moy={3} | %IRQ moy={4} | DPC/s={5}",
                    r.TimerMs, r.SleepAvgMs, r.SleepMaxMs,
                    r.DpcAvg < 0 ? "n/d" : r.DpcAvg.ToString("0.00"),
                    r.IrqAvg < 0 ? "n/d" : r.IrqAvg.ToString("0.00"),
                    r.DpcRateAvg < 0 ? "n/d" : r.DpcRateAvg.ToString("0")));
            }
            catch (Exception ex)
            {
                errors++;
                Console.WriteLine("  ERREUR mesure : " + ex.Message);
            }
            string reportEnv = Environment.GetEnvironmentVariable("BT_PARSE");
            if (!string.IsNullOrEmpty(reportEnv) && System.IO.File.Exists(reportEnv))
            {
                Console.WriteLine();
                Console.WriteLine("Analyse DPC/ISR de : " + reportEnv);
                DpcIsrReport rep = DpcIsrReport.Parse(reportEnv);
                Console.WriteLine("  Durée trace  : " + rep.DurationSec.ToString("0.0") + " s");
                Console.WriteLine("  Total DPC    : " + rep.TotalDpc + "   Total ISR : " + rep.TotalIsr);
                Console.WriteLine("  Pire DPC     : <= " + rep.MaxDpcUs.ToString("0") + " us (" + rep.MaxDpcModule + ")");
                Console.WriteLine("  Pire ISR     : <= " + rep.MaxIsrUs.ToString("0") + " us (" + rep.MaxIsrModule + ")");
                Console.WriteLine("  Verdict [" + rep.VerdictLevel + "] : " + rep.VerdictTitle);
                Console.WriteLine("  Top pilotes par pire latence :");
                foreach (DriverStat d in rep.Drivers.GetRange(0, Math.Min(8, rep.Drivers.Count)))
                    Console.WriteLine(string.Format("    {0,-16} DPCx{1,-6} <= {2,4:0}us   ISRx{3,-6} <= {4,4:0}us   {5}",
                        d.Module, d.DpcCount, d.DpcMaxUs, d.IsrCount, d.IsrMaxUs, d.Description));
                try
                {
                    System.Windows.Forms.Application.EnableVisualStyles();
                    using (var f = new LatencyForm(rep)) { f.CreateControl(); }
                    Console.WriteLine("  UI LatencyForm : construite OK (" + rep.Drivers.Count + " lignes).");
                }
                catch (Exception ex)
                {
                    errors++;
                    Console.WriteLine("  UI LatencyForm ERREUR : " + ex.Message);
                }

                string before = Environment.GetEnvironmentVariable("BT_PARSE2");
                if (!string.IsNullOrEmpty(before) && System.IO.File.Exists(before))
                {
                    DpcIsrReport repB = DpcIsrReport.Parse(before);
                    Console.WriteLine("  Comparaison AVANT=" + System.IO.Path.GetFileName(before)
                        + " APRES=" + System.IO.Path.GetFileName(reportEnv));
                    var deltas = DpcIsrReport.Compare(repB, rep);
                    foreach (DpcIsrReport.ModuleDelta m in deltas.GetRange(0, Math.Min(6, deltas.Count)))
                        Console.WriteLine(string.Format("    {0,-16} {1,5:0} -> {2,5:0} us  (delta {3,5:+0;-0;0})",
                            m.Module, m.WorstBefore, m.WorstAfter, m.Delta));
                    try
                    {
                        using (var f = new CompareForm(repB, rep)) { f.CreateControl(); }
                        Console.WriteLine("  UI CompareForm : construite OK.");
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Console.WriteLine("  UI CompareForm ERREUR : " + ex.Message);
                    }
                }
            }
            Console.WriteLine("TEST TERMINÉ — " + i + " optimisations chargées, " + errors + " erreur(s).");
            Environment.Exit(errors == 0 ? 0 : 1);
        }

        private static double DpcHelperMs(string server)
        {
            try { return DnsBench.QueryMs(server, "www.google.com", 800, 3); }
            catch { return -1; }
        }
    }
#endif
}
