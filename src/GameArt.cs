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
    /// Jaquettes officielles Steam (image « library_600x900 ») par AppID, pour la page Jeux. Ordre :
    ///   1. cache LOCAL de Steam (appcache\librarycache) — instantané, hors-ligne ;
    ///   2. cache disque de l'app (bt-gamecache\&lt;appid&gt;.jpg) — déjà téléchargé une fois ;
    ///   3. CDN public Steam — téléchargé puis mis en cache disque (désactivé sous les harnais de test).
    /// Chargement ASYNCHRONE et non bloquant (aucun thread bloqué sur le sémaphore), plusieurs cartes
    /// peuvent attendre la même image, et un échec est mémorisé pour ne pas réessayer en boucle.
    /// </summary>
    internal static class GameArt
    {
        private static readonly Dictionary<int, Image> _cache = new Dictionary<int, Image>();
        private static readonly Dictionary<int, List<Action>> _waiters = new Dictionary<int, List<Action>>();
        private static readonly HashSet<int> _loading = new HashSet<int>();
        private static readonly HashSet<int> _failed = new HashSet<int>();
        private static readonly object _lock = new object();
        private static readonly SemaphoreSlim _gate = new SemaphoreSlim(8);  // limite les TÉLÉCHARGEMENTS concurrents
        private static HttpClient _http;

        /// <summary>Jaquette d'un jeu, MÊME s'il ne vient pas de Steam (EA, Battle.net, Epic…) :
        /// sans AppID on résout d'abord le nom via l'index Steam, puis on charge l'image
        /// normalement. Beaucoup de jeux non-Steam existent aussi sur Steam et ont donc une
        /// jaquette officielle ; ceux qui n'y sont pas (WoW, Hearthstone…) retombent sur
        /// l'icône du jeu, gérée par l'appelant.</summary>
        public static Image ForGame(string name, int steamId, Action onReady)
        {
            int id = steamId;
            if (id <= 0 && !string.IsNullOrEmpty(name)) id = SteamAppIndex.Resolve(name, onReady);
            return id > 0 ? Get(id, onReady) : null;
        }

        /// <summary>Image prête (cache mémoire), ou null. Lance un chargement de fond au premier appel ;
        /// chaque onReady non nul est rappelé (thread de fond) quand l'image devient disponible.</summary>
        public static Image Get(int appId, Action onReady)
        {
            if (appId <= 0) return null;
            bool start = false;
            lock (_lock)
            {
                Image img;
                if (_cache.TryGetValue(appId, out img)) return img;
                if (_failed.Contains(appId)) return null;
                if (onReady != null)
                {
                    List<Action> list;
                    if (!_waiters.TryGetValue(appId, out list)) { list = new List<Action>(); _waiters[appId] = list; }
                    list.Add(onReady);
                }
                if (!_loading.Contains(appId)) { _loading.Add(appId); start = true; }
            }
            if (start) { var _ignore = LoadAsync(appId); }
            return null;
        }

        private static async Task LoadAsync(int appId)
        {
            Image loaded = null;
            try
            {
                // Rapide et hors-ligne (cache local Steam + cache disque) : hors du gate réseau.
                loaded = await Task.Run(() => LoadLocal(appId) ?? LoadDiskCache(appId)).ConfigureAwait(false);
                if (loaded == null && !UnderTestHarness())
                {
                    await _gate.WaitAsync().ConfigureAwait(false);
                    try { loaded = await DownloadAsync(appId).ConfigureAwait(false); }
                    finally { _gate.Release(); }
                }
            }
            catch { }

            Action[] cbs;
            lock (_lock)
            {
                if (loaded != null) _cache[appId] = loaded; else _failed.Add(appId);
                _loading.Remove(appId);
                List<Action> list;
                cbs = _waiters.TryGetValue(appId, out list) ? list.ToArray() : new Action[0];
                _waiters.Remove(appId);
            }
            if (loaded != null) foreach (var cb in cbs) { try { cb(); } catch { } }
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

        private static async Task<Image> DownloadAsync(int appId)
        {
            try
            {
                if (_http == null)
                    lock (_lock) { if (_http == null) { var h = new HttpClient(); h.Timeout = TimeSpan.FromSeconds(8); _http = h; } }
                string url = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/library_600x900.jpg";
                byte[] data = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
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
