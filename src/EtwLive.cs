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
                    | KernelTraceEventParser.Keywords.Interrupt
                    | KernelTraceEventParser.Keywords.MemoryHardFaults);

                _session.Source.Kernel.PerfInfoDPC += OnDpc;
                _session.Source.Kernel.PerfInfoThreadedDPC += OnDpc;
                _session.Source.Kernel.PerfInfoTimerDPC += OnDpc;
                _session.Source.Kernel.PerfInfoISR += OnIsr;
                _session.Source.Kernel.MemoryHardFault += OnHardFault;

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
            // > 20 ms : invraisemblable pour un ISR réel (artefact de bord de session, ex.
            // interruption commencée avant l'activation de la trace) — on l'écarte.
            if (us < 0 || us > 20000) return;
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

        // ------------------------------------------------------------------
        //  Défauts de page durs (hard pagefaults) : une app lit le DISQUE en
        //  pleine exécution — cause classique de stutter (comme LatencyMon).
        // ------------------------------------------------------------------
        public class HardFaultInfo
        {
            public long Count;
            public double WorstMs;
            public string WorstProcess = "-";
            public string Top = "";        // « proc (n), proc (n)... » les 3 plus gourmands
        }

        private long _hfCount;
        private double _hfWorstMs;
        private string _hfWorstProc = "-";
        private readonly Dictionary<int, long> _hfByPid = new Dictionary<int, long>();
        private readonly Dictionary<int, string> _hfNames = new Dictionary<int, string>();

        private void OnHardFault(Microsoft.Diagnostics.Tracing.Parsers.Kernel.MemoryHardFaultTraceData d)
        {
            double ms = d.ElapsedTimeMSec;
            if (ms < 0 || ms > 60000) return;
            int pid = d.ProcessID;
            string name = d.ProcessName;
            lock (_lock)
            {
                _hfCount++;
                long n;
                _hfByPid.TryGetValue(pid, out n);
                _hfByPid[pid] = n + 1;
                if (!string.IsNullOrEmpty(name) && !_hfNames.ContainsKey(pid)) _hfNames[pid] = name;
                // NOM EN CACHE UNIQUEMENT. Cette méthode est un RAPPEL ETW : tout ce qu'elle fait
                // retarde le traitement des événements suivants, et elle tient le verrou que
                // chaque DPC et chaque interruption doivent prendre.
                if (ms > _hfWorstMs) { _hfWorstMs = ms; _hfWorstProc = NomEnCache(pid); }
            }
        }

        /// <summary>Nom du processus SANS appel système : simple lecture du cache alimenté par la
        /// trace elle-même. À utiliser partout où le verrou est tenu.</summary>
        private string NomEnCache(int pid)
        {
            string name;
            if (_hfNames.TryGetValue(pid, out name) && !string.IsNullOrEmpty(name)) return name;
            return "PID " + pid;
        }

        public HardFaultInfo HardFaults()
        {
            var info = new HardFaultInfo();
            List<KeyValuePair<int, long>> top;
            var connus = new Dictionary<int, string>();

            // SOUS LE VERROU : uniquement de la recopie. Rien qui puisse bloquer.
            //
            // L'ancien code appelait Process.GetProcessById ICI, verrou tenu — mesuré sur cette
            // machine à 2,8 ms en moyenne, jusqu'à 6,7 ms. Or ce verrou est celui que prend CHAQUE
            // événement DPC et CHAQUE interruption.
            //
            // HONNÊTETÉ SUR L'AMPLEUR : l'interface n'interroge qu'une fois par seconde, donc ce
            // blocage représente environ 0,3 % du temps. Mesuré sur un banc d'essai forçant le
            // trait (20 interrogations par seconde), le rappel n'absorbait que ~3 % d'événements
            // en moins. Ce n'est donc PAS la cause des pertes de tampon affichées en pied de page,
            // contrairement à ce qu'on pourrait croire.
            //
            // On le corrige quand même, pour une raison de principe : un appel système sous un
            // verrou partagé avec un rappel à haute fréquence est une dette qui se paie mal le
            // jour où la machine rame — et GetProcessById peut dépasser 6 ms sur un système
            // chargé. Le coût du correctif est nul ; celui du pari ne l'est pas.
            lock (_lock)
            {
                info.Count = _hfCount;
                info.WorstMs = _hfWorstMs;
                info.WorstProcess = _hfWorstProc;
                top = new List<KeyValuePair<int, long>>(_hfByPid);
                foreach (KeyValuePair<int, string> kv in _hfNames) connus[kv.Key] = kv.Value;
            }

            // HORS VERROU : tri, résolution des noms manquants, mise en forme.
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            var parts = new List<string>();
            var appris = new Dictionary<int, string>();
            for (int i = 0; i < top.Count && i < 3; i++)
            {
                int pid = top[i].Key;
                string nom;
                if (!connus.TryGetValue(pid, out nom) || string.IsNullOrEmpty(nom))
                {
                    try { nom = System.Diagnostics.Process.GetProcessById(pid).ProcessName; }
                    catch { nom = "PID " + pid; }
                    appris[pid] = nom;
                }
                parts.Add(nom + " (" + top[i].Value + ")");
            }
            info.Top = string.Join(", ", parts.ToArray());

            // On range ce qu'on vient d'apprendre, pour ne pas le redemander à chaque seconde.
            if (appris.Count > 0)
                lock (_lock)
                    foreach (KeyValuePair<int, string> kv in appris)
                        if (!_hfNames.ContainsKey(kv.Key)) _hfNames[kv.Key] = kv.Value;

            return info;
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
                _hfCount = 0; _hfWorstMs = 0; _hfWorstProc = "-";
                _hfByPid.Clear();
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

        /// <summary>Table des modules noyau, remplacée en bloc par Refresh() et lue SANS VERROU
        /// par Lookup — qui s'exécute dans le rappel ETW, à chaque DPC et chaque interruption.
        /// « volatile » garantit que le rappel voit bien la table publiée par le thread qui l'a
        /// reconstruite, au lieu de rester sur une référence périmée.</summary>
        private static volatile Mod[] _mods = new Mod[0];
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
        private readonly double[] _ring = new double[8192];   // derniers retards, pour le percentile 99

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
                    _ring[_count % _ring.Length] = us;
                    _count++;
                    _sumUs += us;
                    if (us > _maxUs) _maxUs = us;
                }
            }
        }

        public void Read(out double maxUs, out double avgUs)
        {
            double p99;
            Read(out maxUs, out avgUs, out p99);
        }

        public void Read(out double maxUs, out double avgUs, out double p99Us)
        {
            lock (_lock)
            {
                maxUs = _maxUs;
                avgUs = _count > 0 ? _sumUs / _count : 0;
                p99Us = 0;
                int n = (int)Math.Min(_count, _ring.Length);
                if (n >= 100)
                {
                    var copy = new double[n];
                    Array.Copy(_ring, copy, n);
                    Array.Sort(copy);
                    p99Us = copy[(int)(n * 0.99)];   // 99 % des réveils sont plus rapides que ça
                }
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
