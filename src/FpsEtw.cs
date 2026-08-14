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
            public double Fps;            // images présentées sur la dernière seconde
            public double AvgMs;          // frametime moyen (1 s)
            public double OnePctLowFps;   // 1% low : 1000 / moyenne du pire 1 % des frametimes
            public double TenthPctLowFps; // 0.1% low (les pires micro-saccades) — 0 tant que < 1000 frames
            public double WorstMs;        // pire frametime (fenêtre récente)
            /// <summary>Durée RÉELLEMENT couverte par ces chiffres, en ms. Peut être inférieure à
            /// la fenêtre demandée : l'historique de frametimes est borné en temps. Un appelant qui
            /// annonce « benchmark d'une minute » doit dire ce qu'il a vraiment mesuré.</summary>
            public double FenetreMs;
            public long Total;            // total d'images depuis le début / la remise à zéro
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

        /// <summary>Durée d'historique de frametimes conservée, en millisecondes. BORNÉE EN TEMPS,
        /// pas en nombre d'images : à 60 fps, 12 000 images font plus de trois minutes — un « 1 %
        /// low » calculé là-dessus décrit ce qui s'est passé il y a deux minutes, pas maintenant.</summary>
        private const double MemoireMs = 20000;

        // « Maintenant » NE PEUT PAS être l'horodatage du dernier événement : quand plus rien ne
        // présente (jeu fermé, minimisé, figé), cet horodatage se fige aussi — la fenêtre glissante
        // reste alors calée sur les dernières images vues et le compteur affiche indéfiniment le
        // dernier FPS connu. Une horloge murale sert donc de référence, et l'heure ETW est
        // extrapolée à partir du dernier événement reçu.
        private readonly Stopwatch _horloge = Stopwatch.StartNew();
        private double _tsDernierEvt = -1;   // horodatage ETW du dernier événement
        private double _murAuDernierEvt;     // horloge murale au même instant

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
                _tsDernierEvt = ts;
                _murAuDernierEvt = _horloge.Elapsed.TotalMilliseconds;
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
                        Elague(t, ts);
                    }
                }
                t.LastTs = ts;
                t.Total++;
            }
        }

        /// <summary>Jette les images plus vieilles que <see cref="MemoireMs"/>. Le retrait se fait
        /// par blocs (jamais image par image) : retirer en tête d'une liste coûte cher, et ce code
        /// tourne sur CHAQUE image présentée — jusqu'à plusieurs centaines par seconde.</summary>
        private static void Elague(Track t, double maintenant)
        {
            if (t.Times.Count < 256) return;
            double limite = maintenant - MemoireMs;
            if (t.Times[0] >= limite) return;
            int coupe = PremierDansFenetre(t.Times, limite);
            if (coupe <= 0) return;
            t.Times.RemoveRange(0, coupe);
            t.Fts.RemoveRange(0, coupe);
        }

        /// <summary>PUR : index de la première image dont l'horodatage dépasse <paramref name="borne"/>.
        /// Rend Count si aucune ne la dépasse (fenêtre vide) — les horodatages sont croissants.</summary>
        public static int PremierDansFenetre(List<double> times, double borne)
        {
            if (times == null || times.Count == 0) return 0;
            int lo = 0, hi = times.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (times[mid] > borne) hi = mid; else lo = mid + 1;
            }
            return lo;
        }

        /// <summary>PUR : images comptées sur une fenêtre de durée connue. La durée est celle qui
        /// S'EST ÉCOULÉE, pas celle qui sépare la première et la dernière image — sans quoi un jeu
        /// qui s'arrête de présenter continuerait d'afficher son dernier FPS.</summary>
        public static double CalculeFps(int images, double fenetreMs)
        {
            if (images <= 0 || fenetreMs <= 0) return 0;
            return images * 1000.0 / fenetreMs;
        }

        /// <summary>Heure ETW estimée à l'instant présent (appeler sous verrou).</summary>
        private double Maintenant()
        {
            if (_tsDernierEvt < 0) return 0;
            return _tsDernierEvt + (_horloge.Elapsed.TotalMilliseconds - _murAuDernierEvt);
        }

        /// <summary>Statistiques par processus (trié FPS décroissant). windowMs : fenêtre du FPS instantané.</summary>
        public List<ProcStat> Snapshot(double windowMs)
        {
            var result = new List<ProcStat>();

            // On ne peut pas compter sur des images qui n'existent plus. L'historique est borné à
            // MemoireMs ; demander une fenêtre plus large ne fait pas apparaître d'images, mais le
            // FPS était quand même divisé par la durée DEMANDÉE.
            //
            // Conséquence mesurée : un benchmark d'une minute annonçait le tiers du vrai FPS, deux
            // minutes le sixième. Le compteur en direct (fenêtre de 2 s) était juste, si bien que
            // l'utilisateur voyait 300 FPS pendant toute la capture puis lisait un résumé à 100.
            //
            // On borne donc la fenêtre à ce qui est réellement couvert. L'horloge murale reste la
            // référence : un jeu qui cesse de présenter voit toujours son FPS retomber, puisque ses
            // images vieillissent hors de la fenêtre au lieu d'être recomptées.
            double fenetre = Math.Min(windowMs, MemoireMs);

            lock (_lock)
            {
                double maintenant = Maintenant();
                foreach (KeyValuePair<int, Track> kv in _tracks)
                {
                    Track t = kv.Value;
                    if (t.Times.Count == 0) continue;
                    double cutoff = maintenant - fenetre;
                    if (t.Times[t.Times.Count - 1] < maintenant - 3000) continue;  // plus rien depuis 3 s : ignorer

                    int first = PremierDansFenetre(t.Times, cutoff);
                    int n = t.Times.Count - first;
                    if (n <= 0) continue;

                    double sum = 0, worst = 0;
                    for (int i = first; i < t.Times.Count; i++)
                    {
                        sum += t.Fts[i];
                        if (t.Fts[i] > worst) worst = t.Fts[i];
                    }

                    // 1% low / 0.1% low sur les ~20 dernières secondes de frames conservées.
                    double onePct = 0, tenthPct = 0;
                    int m = t.Fts.Count;
                    if (m >= 100)
                    {
                        var copy = new List<double>(t.Fts);
                        copy.Sort();
                        onePct = LowAvgFps(copy, m / 100);
                        if (m >= 1000) tenthPct = LowAvgFps(copy, m / 1000);
                    }

                    var st = new ProcStat
                    {
                        Pid = kv.Key,
                        Name = NameOf(kv.Key),
                        Fps = CalculeFps(n, fenetre),
                        FenetreMs = fenetre,
                        AvgMs = sum / n,
                        OnePctLowFps = onePct,
                        TenthPctLowFps = tenthPct,
                        WorstMs = worst,
                        Total = t.Total
                    };
                    result.Add(st);
                }
            }
            result.Sort((a, b) => b.Fps.CompareTo(a.Fps));
            return result;
        }

        // ------------------------------------------------------------------ quel processus est LE JEU

        /// <summary>
        /// Applications qui présentent des images sans être un jeu. Un navigateur avec
        /// l'accélération matérielle présente en continu, souvent PLUS VITE qu'un jeu synchronisé à
        /// 60 Hz — prendre « celui qui a le plus de FPS » désigne alors Chrome, pas le jeu.
        /// </summary>
        private static readonly string[] PasDesJeux =
        {
            "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore",
            "discord", "spotify", "steam", "steamwebhelper", "epicgameslauncher", "battle.net",
            "explorer", "dwm", "searchhost", "startmenuexperiencehost", "shellexperiencehost",
            "textinputhost", "widgets", "widgetboard", "applicationframehost", "systemsettings",
            "teams", "slack", "code", "devenv", "windowsterminal", "powershell", "cmd",
            "vlc", "mpc-hc64", "mpc-be64", "obs64", "obs32", "nvcontainer", "nvidia share",
            "btoptimizer", "onyx"
        };

        /// <summary>PUR : ce nom de processus est-il connu pour ne PAS être un jeu ?</summary>
        public static bool EstIgnore(string nom)
        {
            if (string.IsNullOrEmpty(nom)) return false;
            string n = nom.ToLowerInvariant();
            if (n.EndsWith(".exe")) n = n.Substring(0, n.Length - 4);
            foreach (string s in PasDesJeux)
                if (n == s) return true;
            return false;
        }

        /// <summary>
        /// PUR : lequel de ces processus est le jeu ?
        ///
        /// L'ANCIENNE RÈGLE ÉTAIT « celui qui a le plus de FPS », et elle est fausse par
        /// construction : un navigateur accéléré dépasse sans peine un jeu bridé à 60 Hz. Le
        /// compteur affichait alors le débit d'images de Chrome pendant une partie.
        ///
        /// La règle est maintenant : ce que l'utilisateur REGARDE, c'est-à-dire la fenêtre au
        /// premier plan. À défaut (premier plan inconnu, ou qui ne présente rien), on retombe sur le
        /// plus rapide en excluant ce qui n'est pas un jeu. Et si tout est exclu, on rend null
        /// plutôt qu'un mauvais candidat : « — » est une réponse honnête, un chiffre faux non.
        /// </summary>
        public static ProcStat ChoisirJeu(List<ProcStat> stats, int pidPremierPlan)
        {
            if (stats == null || stats.Count == 0) return null;

            if (pidPremierPlan > 0)
                foreach (ProcStat p in stats)
                    if (p.Pid == pidPremierPlan && p.Fps > 0) return p;

            ProcStat meilleur = null;
            foreach (ProcStat p in stats)
            {
                if (p.Fps <= 0 || EstIgnore(p.Name)) continue;
                if (meilleur == null || p.Fps > meilleur.Fps) meilleur = p;
            }
            return meilleur;
        }

        /// <summary>PID de la fenêtre au premier plan, 0 si indéterminé.</summary>
        public static int PidPremierPlan()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return 0;
                int pid;
                GetWindowThreadProcessId(h, out pid);
                return pid;
            }
            catch { return 0; }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);

        /// <summary>Moyenne du pire k-ième des frametimes triés, convertie en FPS.</summary>
        private static double LowAvgFps(List<double> sorted, int k)
        {
            if (k < 1) k = 1;
            double s = 0;
            for (int i = sorted.Count - k; i < sorted.Count; i++) s += sorted[i];
            double avgWorst = s / k;
            return avgWorst > 0 ? 1000.0 / avgWorst : 0;
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
