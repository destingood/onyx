using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace BTOptimizer
{
    /// <summary>
    /// Mesure de latence temps réel « précision LatencyMon » : session ETW noyau
    /// (NT Kernel Logger) consommée en direct. Chaque DPC et chaque ISR est reçu avec sa
    /// durée EXACTE (horloge QPC) et attribué au pilote responsable via la table des
    /// modules noyau — pas d'échantillonnage, pas d'approximation par compteurs.
    /// Nécessite les droits administrateur (l'application est déjà élevée).
    /// </summary>
    internal sealed class EtwLive : IDisposable
    {
        private TraceEventSession _session;
        private Thread _thread;
        private readonly object _lock = new object();
        private readonly Dictionary<string, DriverStat> _map = new Dictionary<string, DriverStat>(StringComparer.OrdinalIgnoreCase);
        private long _totalDpc, _totalIsr;
        private double _maxDpcUs, _maxIsrUs;
        private string _maxDpcModule = "-", _maxIsrModule = "-";
        private DateTime _startedUtc;

        public bool Running { get; private set; }
        public string LastError { get; private set; }

        /// <summary>Démarre la session noyau (DPC + Interrupt). false si refusé (admin requis) ou en échec.</summary>
        public bool Start()
        {
            if (Running) return true;
            try
            {
                KernelModules.Refresh();

                // Une seule session « NT Kernel Logger » existe par machine : on reprend la main
                // si une trace précédente (WPR/xperf) est restée ouverte.
                try
                {
                    var old = TraceEventSession.GetActiveSession(KernelTraceEventParser.KernelSessionName);
                    if (old != null) old.Stop(true);
                }
                catch { }

                _session = new TraceEventSession(KernelTraceEventParser.KernelSessionName);
                _session.EnableKernelProvider(
                    KernelTraceEventParser.Keywords.DeferedProcedureCalls
                    | KernelTraceEventParser.Keywords.Interrupt);

                _session.Source.Kernel.PerfInfoDPC += OnDpc;
                _session.Source.Kernel.PerfInfoThreadedDPC += OnDpc;
                _session.Source.Kernel.PerfInfoTimerDPC += OnDpc;
                _session.Source.Kernel.PerfInfoISR += OnIsr;

                _startedUtc = DateTime.UtcNow;
                _thread = new Thread(() => { try { _session.Source.Process(); } catch { } });
                _thread.IsBackground = true;
                _thread.Name = "BT-EtwLive";
                _thread.Start();

                Running = true;
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                LastError = "droits administrateur requis pour la session noyau ETW";
                CleanupSession();
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                CleanupSession();
                return false;
            }
        }

        private void OnDpc(DPCTraceData d)
        {
            double us = d.ElapsedTimeMSec * 1000.0;
            if (us < 0 || us > 4e6) return;    // garde-fou (horloge incohérente)
            string module = KernelModules.Lookup(d.Routine);
            lock (_lock)
            {
                DriverStat s = Get(module);
                s.DpcCount++;
                s.DpcTotalUs += (long)us;
                if (us > s.DpcMaxUs) s.DpcMaxUs = us;
                _totalDpc++;
                if (us > _maxDpcUs) { _maxDpcUs = us; _maxDpcModule = module; }
            }
        }

        private void OnIsr(ISRTraceData d)
        {
            double us = d.ElapsedTimeMSec * 1000.0;
            if (us < 0 || us > 4e6) return;
            string module = KernelModules.Lookup(d.Routine);
            lock (_lock)
            {
                DriverStat s = Get(module);
                s.IsrCount++;
                s.IsrTotalUs += (long)us;
                if (us > s.IsrMaxUs) s.IsrMaxUs = us;
                _totalIsr++;
                if (us > _maxIsrUs) { _maxIsrUs = us; _maxIsrModule = module; }
            }
        }

        private DriverStat Get(string module)
        {
            DriverStat s;
            if (!_map.TryGetValue(module, out s))
            {
                s = new DriverStat { Module = module, Description = DpcIsrReport.DescribeDriver(module) };
                _map[module] = s;
            }
            return s;
        }

        /// <summary>Photographie thread-safe de l'état courant, sous la même forme que l'analyse xperf.</summary>
        public DpcIsrReport Snapshot()
        {
            var rep = new DpcIsrReport();
            rep.SourceFile = "session ETW noyau en direct";
            lock (_lock)
            {
                rep.TotalDpc = _totalDpc;
                rep.TotalIsr = _totalIsr;
                rep.MaxDpcUs = _maxDpcUs; rep.MaxDpcModule = _maxDpcModule;
                rep.MaxIsrUs = _maxIsrUs; rep.MaxIsrModule = _maxIsrModule;
                rep.DurationSec = (DateTime.UtcNow - _startedUtc).TotalSeconds;
                foreach (DriverStat s in _map.Values)
                    rep.Drivers.Add(new DriverStat
                    {
                        Module = s.Module, Description = s.Description,
                        DpcCount = s.DpcCount, DpcTotalUs = s.DpcTotalUs, DpcMaxUs = s.DpcMaxUs,
                        IsrCount = s.IsrCount, IsrTotalUs = s.IsrTotalUs, IsrMaxUs = s.IsrMaxUs
                    });
            }
            rep.Drivers = rep.Drivers
                .OrderByDescending(d => d.WorstUs)
                .ThenByDescending(d => d.DpcTotalUs + d.IsrTotalUs)
                .ToList();
            return rep;
        }

        /// <summary>Nombre d'événements perdus par la session (tampons pleins) — 0 en usage normal.</summary>
        public int EventsLost
        {
            get { try { return _session != null ? _session.EventsLost : 0; } catch { return 0; } }
        }

        /// <summary>Remet les compteurs à zéro sans interrompre la session.</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _map.Clear();
                _totalDpc = _totalIsr = 0;
                _maxDpcUs = _maxIsrUs = 0;
                _maxDpcModule = _maxIsrModule = "-";
                _startedUtc = DateTime.UtcNow;
            }
        }

        private void CleanupSession()
        {
            try { if (_session != null) _session.Dispose(); } catch { }
            _session = null;
        }

        public void Dispose()
        {
            Running = false;
            CleanupSession();                       // stoppe la session -> Process() rend la main
            try { if (_thread != null && !_thread.Join(2000)) { /* thread d'arrière-plan, il mourra avec le process */ } } catch { }
            _thread = null;
        }
    }

    /// <summary>
    /// Table des modules noyau chargés (NtQuerySystemInformation / SystemModuleInformation) :
    /// permet d'attribuer l'adresse d'une routine DPC/ISR au pilote (.sys) qui la contient.
    /// </summary>
    internal static class KernelModules
    {
        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);

        private const int SystemModuleInformation = 11;

        private class Mod { public ulong Base, End; public string Name; }

        private static Mod[] _mods = new Mod[0];
        private static DateTime _lastRefresh = DateTime.MinValue;
        private static readonly object _lock = new object();

        public static int Count { get { return _mods.Length; } }

        /// <summary>Recharge la liste des modules noyau (au démarrage puis au plus toutes les 5 s si adresse inconnue).</summary>
        public static void Refresh()
        {
            lock (_lock)
            {
                _lastRefresh = DateTime.UtcNow;
                try
                {
                    int len = 256 * 1024, need;
                    IntPtr buf = Marshal.AllocHGlobal(len);
                    try
                    {
                        int status = NtQuerySystemInformation(SystemModuleInformation, buf, len, out need);
                        if (status != 0 && need > len)
                        {
                            Marshal.FreeHGlobal(buf);
                            len = need + 4096;
                            buf = Marshal.AllocHGlobal(len);
                            status = NtQuerySystemInformation(SystemModuleInformation, buf, len, out need);
                        }
                        if (status != 0) return;

                        int count = Marshal.ReadInt32(buf, 0);
                        int entrySize = IntPtr.Size == 8 ? 296 : 284;   // RTL_PROCESS_MODULE_INFORMATION
                        int first = IntPtr.Size;                        // NumberOfModules + alignement pointeur
                        // Décalages dans l'entrée : Section, MappedBase, ImageBase (3 pointeurs),
                        // puis ImageSize (4), Flags (4), LoadOrderIndex/InitOrderIndex/LoadCount (3 x 2),
                        // OffsetToFileName (2), FullPathName (256, ANSI).
                        int offImageBase = 2 * IntPtr.Size;
                        int offImageSize = 3 * IntPtr.Size;
                        int offNameOff = offImageSize + 4 + 4 + 2 + 2 + 2;
                        int offPath = offNameOff + 2;
                        var list = new List<Mod>(count);
                        for (int i = 0; i < count; i++)
                        {
                            int off = first + i * entrySize;
                            if (off + entrySize > len) break;
                            ulong imageBase = (ulong)Marshal.ReadIntPtr(buf, off + offImageBase).ToInt64();
                            uint imageSize = (uint)Marshal.ReadInt32(buf, off + offImageSize);
                            short nameOff = Marshal.ReadInt16(buf, off + offNameOff);
                            if (nameOff < 0 || nameOff > 255) nameOff = 0;
                            string path = Marshal.PtrToStringAnsi(IntPtr.Add(buf, off + offPath + nameOff));
                            if (string.IsNullOrEmpty(path)) continue;
                            list.Add(new Mod { Base = imageBase, End = imageBase + imageSize, Name = path });
                        }
                        list.Sort((a, b) => a.Base.CompareTo(b.Base));
                        _mods = list.ToArray();
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
                catch { }
            }
        }

        /// <summary>Module contenant l'adresse noyau donnée, ou « inconnu ».</summary>
        public static string Lookup(ulong address)
        {
            Mod[] mods = _mods;
            int lo = 0, hi = mods.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (address < mods[mid].Base) hi = mid - 1;
                else if (address >= mods[mid].End) lo = mid + 1;
                else return mods[mid].Name;
            }
            // Adresse hors table (pilote chargé après coup ?) : on retente un refresh espacé.
            if ((DateTime.UtcNow - _lastRefresh).TotalSeconds > 5)
            {
                Refresh();
                mods = _mods;
                for (int i = 0; i < mods.Length; i++)
                    if (address >= mods[i].Base && address < mods[i].End) return mods[i].Name;
            }
            return "inconnu";
        }
    }

    /// <summary>
    /// Sonde de réveil : un thread haute priorité dort 1 ms en boucle et mesure (QPC) de
    /// combien Windows le réveille en retard. C'est l'équivalent utilisateur de la
    /// « interrupt to process latency » de LatencyMon : ce que subit réellement un jeu.
    /// </summary>
    internal sealed class WakeupProbe : IDisposable
    {
        private Thread _thread;
        private volatile bool _stop;
        private readonly object _lock = new object();
        private double _maxUs, _sumUs;
        private long _count;

        public void Start()
        {
            if (_thread != null) return;
            _stop = false;
            _thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "BT-WakeupProbe" };
            _thread.Start();
        }

        private void Loop()
        {
            double tickUs = 1e6 / System.Diagnostics.Stopwatch.Frequency;
            long prev = System.Diagnostics.Stopwatch.GetTimestamp();
            while (!_stop)
            {
                Thread.Sleep(1);
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                double us = (now - prev) * tickUs - 1000.0;   // retard au-delà de la milliseconde demandée
                prev = now;
                if (us < 0) us = 0;
                if (us > 1e6) continue;                        // veille/suspension : ignorer
                lock (_lock)
                {
                    _count++;
                    _sumUs += us;
                    if (us > _maxUs) _maxUs = us;
                }
            }
        }

        public void Read(out double maxUs, out double avgUs)
        {
            lock (_lock)
            {
                maxUs = _maxUs;
                avgUs = _count > 0 ? _sumUs / _count : 0;
            }
        }

        public void Reset()
        {
            lock (_lock) { _maxUs = 0; _sumUs = 0; _count = 0; }
        }

        public void Dispose()
        {
            _stop = true;
            try { if (_thread != null) _thread.Join(500); } catch { }
            _thread = null;
        }
    }
}
