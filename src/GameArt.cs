using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BTOptimizer
{
    /// <summary>
    /// Jaquettes officielles Steam (image « header ») par AppID, pour la page Jeux. Ordre :
    ///   1. cache LOCAL de Steam (appcache\librarycache) — instantané, hors-ligne, fichiers de l'utilisateur ;
    ///   2. cache disque de l'app (bt-gamecache\&lt;appid&gt;.jpg) — déjà téléchargé une fois ;
    ///   3. CDN public Steam — téléchargé puis mis en cache disque (désactivé sous les harnais de test).
    /// Chargement asynchrone et non bloquant ; échec silencieux → la carte garde sa tuile générique.
    /// </summary>
    internal static class GameArt
    {
        private static readonly Dictionary<int, Image> _cache = new Dictionary<int, Image>();
        private static readonly HashSet<int> _loading = new HashSet<int>();
        private static readonly object _lock = new object();
        private static readonly SemaphoreSlim _gate = new SemaphoreSlim(6);  // limite les chargements concurrents
        private static HttpClient _http;

        /// <summary>Image prête (cache), ou null. Lance un chargement de fond au premier appel ;
        /// onReady est invoqué (thread de fond) quand l'image devient disponible.</summary>
        public static Image Get(int appId, Action onReady)
        {
            if (appId <= 0) return null;
            lock (_lock)
            {
                Image img;
                if (_cache.TryGetValue(appId, out img)) return img;
                if (_loading.Contains(appId)) return null;
                _loading.Add(appId);
            }
            Task.Run(() =>
            {
                Image loaded = null;
                try
                {
                    _gate.Wait();
                    try { loaded = LoadLocal(appId) ?? LoadDiskCache(appId) ?? Download(appId); }
                    finally { _gate.Release(); }
                }
                catch { }
                lock (_lock) { if (loaded != null) _cache[appId] = loaded; _loading.Remove(appId); }
                if (loaded != null && onReady != null) { try { onReady(); } catch { } }
            });
            return null;
        }

        private static Image LoadLocal(int appId)
        {
            try
            {
                string root = GameScan.SteamRoot();
                if (string.IsNullOrEmpty(root)) return null;
                string lc = Path.Combine(root, @"appcache\librarycache");
                string a = appId.ToString();
                string[] cands =
                {
                    Path.Combine(lc, a, "library_600x900.jpg"),      // Steam récent (sous-dossier) — la vraie jaquette
                    Path.Combine(lc, a + "_library_600x900.jpg"),    // Steam ancien (plat)
                    Path.Combine(lc, a, "header.jpg"),
                    Path.Combine(lc, a + "_header.jpg")
                };
                foreach (var c in cands) if (File.Exists(c)) { Image im = FromFile(c); if (im != null) return im; }
            }
            catch { }
            return null;
        }

        private static string DiskPath(int appId) { return Path.Combine(AppContext.BaseDirectory, "bt-gamecache", appId + ".jpg"); }

        private static Image LoadDiskCache(int appId)
        {
            try { string p = DiskPath(appId); if (File.Exists(p) && new FileInfo(p).Length > 100) return FromFile(p); }
            catch { }
            return null;
        }

        private static Image Download(int appId)
        {
            if (UnderTestHarness()) return null;   // pas de réseau sous les captures/tests
            try
            {
                if (_http == null) { _http = new HttpClient(); _http.Timeout = TimeSpan.FromSeconds(6); }
                string url = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/library_600x900.jpg";
                byte[] data = _http.GetByteArrayAsync(url).GetAwaiter().GetResult();
                if (data == null || data.Length < 100) return null;
                try { string p = DiskPath(appId); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllBytes(p, data); } catch { }
                return FromBytes(data);
            }
            catch { return null; }
        }

        // Décode entièrement dans un Bitmap indépendant (le flux peut se fermer sans casser le dessin GDI+).
        private static Image FromFile(string p)
        {
            try { using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read)) using (var tmp = Image.FromStream(fs)) return new Bitmap(tmp); }
            catch { return null; }
        }

        private static Image FromBytes(byte[] b)
        {
            try { using (var ms = new MemoryStream(b)) using (var tmp = Image.FromStream(ms)) return new Bitmap(tmp); }
            catch { return null; }
        }

        private static bool UnderTestHarness()
        {
            try
            {
                return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BT_UITEST"))
                    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BT_UISHOT"))
                    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BT_FORMSHOT"));
            }
            catch { return false; }
        }
    }
}
