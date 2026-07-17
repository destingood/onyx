using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace BTOptimizer
{
    /// <summary>
    /// Compteur de FPS façon PresentMon : session ETW utilisateur abonnée aux événements
    /// que Windows émet à CHAQUE Present() DirectX (fournisseurs Microsoft-Windows-DXGI
    /// et Microsoft-Windows-D3D9). Aucun overlay, aucune injection, zéro impact sur le
    /// jeu : on compte les images réellement présentées et l'écart entre elles
    /// (frametimes exacts, horloge QPC). Droits administrateur requis.
    /// </summary>
    internal sealed class FpsEtw : IDisposable
    {
        private const string SessionName = "BTOptimizer-FPS";

        // Fournisseurs manifestés de Windows (mêmes IDs que PresentMon).
        private static readonly Guid DxgiProvider = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        private static readonly Guid D3D9Provider = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");
        private const int DxgiPresentStart = 42;   // IDXGISwapChain::Present (début)
        private const int D3D9PresentStart = 1;    // IDirect3DDevice9::Present (début)

        private TraceEventSession _session;
        private Thread _thread;
        public bool Running { get; private set; }
        public string LastError { get; private set; }

        /// <summary>Statistiques d'un processus qui présente des images.</summary>
        public class ProcStat
        {
            public int Pid;
            public string Name = "?";
            public double Fps;          // images présentées sur la dernière seconde
            public double AvgMs;        // frametime moyen (1 s)
            public double OnePctLowFps; // 1% low : 1000 / moyenne du pire 1 % des frametimes
            public double WorstMs;      // pire frametime (fenêtre récente)
            public long Total;          // total d'images depuis le début / la remise à zéro
        }

        // Par PID : horodatages (ms relatives) des dernières présentations + frametimes.
        private class Track
        {
            public double LastTs = -1;
            public readonly List<double> Times = new List<double>(4096);  // horodatage de chaque frame
            public readonly List<double> Fts = new List<double>(4096);    // frametime associé (ms)
            public long Total;
        }

        private readonly object _lock = new object();
        private readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();
        private readonly Dictionary<int, string> _names = new Dictionary<int, string>();
        private double _nowMs;   // dernier horodatage vu (base de la fenêtre glissante)

        public bool Start()
        {
            if (Running) return true;
            try
            {
                try
                {
                    var old = TraceEventSession.GetActiveSession(SessionName);
                    if (old != null) old.Stop(true);
                }
                catch { }

                _session = new TraceEventSession(SessionName);
                _session.EnableProvider(DxgiProvider, TraceEventLevel.Informational);
                _session.EnableProvider(D3D9Provider, TraceEventLevel.Informational);

                _session.Source.Dynamic.All += OnEvent;

                _thread = new Thread(() => { try { _session.Source.Process(); } catch { } });
                _thread.IsBackground = true;
                _thread.Name = "BT-FpsEtw";
                _thread.Start();

                Running = true;
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                LastError = "droits administrateur requis pour la session ETW";
                Cleanup();
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Cleanup();
                return false;
            }
        }

        private void OnEvent(TraceEvent data)
        {
            int id = (int)data.ID;
            bool present =
                (data.ProviderGuid == DxgiProvider && id == DxgiPresentStart) ||
                (data.ProviderGuid == D3D9Provider && id == D3D9PresentStart);
            if (!present) return;

            int pid = data.ProcessID;
            if (pid <= 0) return;
            double ts = data.TimeStampRelativeMSec;

            lock (_lock)
            {
                _nowMs = ts;
                Track t;
                if (!_tracks.TryGetValue(pid, out t))
                {
                    t = new Track();
                    _tracks[pid] = t;
                }
                if (t.LastTs >= 0)
                {
                    double ft = ts - t.LastTs;
                    if (ft >= 0 && ft < 10000)
                    {
                        t.Times.Add(ts);
                        t.Fts.Add(ft);
                        // Fenêtre bornée : on garde ~20 s de frames (assez pour le 1% low).
                        if (t.Times.Count > 12000)
                        {
                            t.Times.RemoveRange(0, 4000);
                            t.Fts.RemoveRange(0, 4000);
                        }
                    }
                }
                t.LastTs = ts;
                t.Total++;
            }
        }

        /// <summary>Statistiques par processus (trié FPS décroissant). windowMs : fenêtre du FPS instantané.</summary>
        public List<ProcStat> Snapshot(double windowMs)
        {
            var result = new List<ProcStat>();
            lock (_lock)
            {
                foreach (KeyValuePair<int, Track> kv in _tracks)
                {
                    Track t = kv.Value;
                    if (t.Times.Count == 0) continue;
                    double cutoff = _nowMs - windowMs;
                    if (t.Times[t.Times.Count - 1] < _nowMs - 3000) continue;  // plus rien depuis 3 s : ignorer

                    int first = t.Times.Count;                 // 1re frame dans la fenêtre
                    for (int i = t.Times.Count - 1; i >= 0; i--)
                    {
                        if (t.Times[i] < cutoff) break;
                        first = i;
                    }
                    int n = t.Times.Count - first;
                    if (n <= 0) continue;

                    double sum = 0, worst = 0;
                    for (int i = first; i < t.Times.Count; i++)
                    {
                        sum += t.Fts[i];
                        if (t.Fts[i] > worst) worst = t.Fts[i];
                    }

                    // 1% low sur les ~20 dernières secondes de frames conservées.
                    double onePct = 0;
                    int m = t.Fts.Count;
                    if (m >= 100)
                    {
                        var copy = new List<double>(t.Fts);
                        copy.Sort();
                        int k = Math.Max(1, m / 100);
                        double s = 0;
                        for (int i = m - k; i < m; i++) s += copy[i];   // pire 1 % (frametimes les plus longs)
                        double avgWorst = s / k;
                        if (avgWorst > 0) onePct = 1000.0 / avgWorst;
                    }

                    var st = new ProcStat
                    {
                        Pid = kv.Key,
                        Name = NameOf(kv.Key),
                        Fps = n * 1000.0 / windowMs,
                        AvgMs = sum / n,
                        OnePctLowFps = onePct,
                        WorstMs = worst,
                        Total = t.Total
                    };
                    result.Add(st);
                }
            }
            result.Sort((a, b) => b.Fps.CompareTo(a.Fps));
            return result;
        }

        /// <summary>Frametimes récents d'un PID (copie, pour le graphique).</summary>
        public double[] RecentFrametimes(int pid, int count)
        {
            lock (_lock)
            {
                Track t;
                if (!_tracks.TryGetValue(pid, out t) || t.Fts.Count == 0) return new double[0];
                int n = Math.Min(count, t.Fts.Count);
                var arr = new double[n];
                t.Fts.CopyTo(t.Fts.Count - n, arr, 0, n);
                return arr;
            }
        }

        private string NameOf(int pid)
        {
            string name;
            if (_names.TryGetValue(pid, out name)) return name;
            try { name = Process.GetProcessById(pid).ProcessName + ".exe"; }
            catch { name = "PID " + pid; }
            _names[pid] = name;
            return name;
        }

        public void Reset()
        {
            lock (_lock)
            {
                _tracks.Clear();
                _names.Clear();
            }
        }

        private void Cleanup()
        {
            try { if (_session != null) _session.Dispose(); } catch { }
            _session = null;
        }

        public void Dispose()
        {
            Running = false;
            Cleanup();
            try { if (_thread != null) _thread.Join(2000); } catch { }
            _thread = null;
        }
    }
}
