using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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
                // Même filtre anti-placeholder que pour le réseau : le cache Steam contient
                // aussi des vignettes d'attente de 1 Ko pour les jeux sans jaquette publiée.
                foreach (var c in cands)
                    if (File.Exists(c) && new FileInfo(c).Length >= MinArtBytes)
                    { Image im = FromFile(c); if (im != null) return im; }
            }
            catch { }
            return null;
        }

        private static string DiskPath(int appId) { return Path.Combine(AppContext.BaseDirectory, "bt-gamecache", appId + ".jpg"); }

        private static Image LoadDiskCache(int appId)
        {
            try { string p = DiskPath(appId); if (File.Exists(p) && new FileInfo(p).Length >= MinArtBytes) return FromFile(p); }
            catch { }
            return null;
        }

        /// <summary>Formats Steam essayés dans l'ordre. Tous les jeux n'ont PAS la jaquette
        /// portrait (library_600x900) : sans repli, un jeu récent ou un portage sortait avec
        /// une tuile vide alors que son bandeau existe. On dégrade donc proprement.</summary>
        /// <summary>Seuil anti-placeholder. Steam sert un JPEG GRIS UNI (~1,5 Ko) pour les jeux
        /// sans jaquette publiée : accepté, il donnait une tuile grise, moche et muette. Une vraie
        /// jaquette 600x900 pèse 30-200 Ko, un bandeau 15-40 Ko — sous 6 Ko, ce n'est pas une image
        /// de jeu, et on préfère retomber sur l'icône + le nom (bien plus lisible).</summary>
        private const int MinArtBytes = 6000;

        private static readonly string[] ArtFiles =
        {
            "library_600x900.jpg",   // vraie jaquette verticale
            "library_600x900_2x.jpg",
            "portrait.png",
            "header.jpg",            // bandeau 460x215 — présent sur quasiment tous les jeux
            "capsule_616x353.jpg"
        };

        private static async Task<Image> DownloadAsync(int appId)
        {
            if (_http == null)
                lock (_lock) { if (_http == null) { var h = new HttpClient(); h.Timeout = TimeSpan.FromSeconds(8); _http = h; } }

            foreach (string file in ArtFiles)
            {
                try
                {
                    string url = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/" + file;
                    byte[] data = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
                    if (data == null || data.Length < MinArtBytes) continue;   // placeholder gris → format suivant
                    try { string p = DiskPath(appId); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllBytes(p, data); } catch { }
                    Image img = FromBytes(data);
                    if (img != null) return img;
                }
                catch { }   // 404 sur ce format : on tente le suivant
            }

            // Jeux récents : plus rien à l'ancienne adresse. L'API Steam donne le chemin avec empreinte.
            try
            {
                string json = await OffresReseau.Texte(OffresSources.UrlFichesSteam(new[] { appId }), 7 * 24 * 60).ConfigureAwait(false);
                var fiches = OffresSources.FichesSteam(json);
                string u = fiches.Count > 0 ? fiches[0].Image : null;
                byte[] data = u == null ? null : await TelechargeImage(u).ConfigureAwait(false);
                if (data != null)
                {
                    try { string p = DiskPath(appId); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllBytes(p, data); } catch { }
                    return FromBytes(data);
                }
            }
            catch { }
            return null;
        }

        // ------------------------------------------------ images par URL (Bons plans : Steam, Epic, GamerPower)

        private static readonly Dictionary<string, Image> _urlCache = new Dictionary<string, Image>();
        private static readonly Dictionary<string, List<Action>> _urlWaiters = new Dictionary<string, List<Action>>();
        private static readonly HashSet<string> _urlLoading = new HashSet<string>();
        private static readonly HashSet<string> _urlFailed = new HashSet<string>();
        private const int MaxImageBytes = 4 * 1024 * 1024;
        private const int HauteurMax = 360;   // réduit à l'affichage : une carte n'en montre que 112 px
        private static HttpClient _httpOnyx;

        /// <summary>Image d'une URL https (cache mémoire puis disque), ou null tant qu'elle n'est pas prête.
        /// onReady est rappelé (thread de fond) quand elle est prête ou a échoué.</summary>
        public static Image GetUrl(string url, Action onReady, bool highResolution = false)
        {
            if (!UrlImageSure(url)) return null;
            string key = highResolution ? "hd:" + url : url;
            bool start = false;
            lock (_lock)
            {
                Image img;
                if (_urlCache.TryGetValue(key, out img)) return img;
                if (_urlFailed.Contains(key)) return null;
                if (onReady != null)
                {
                    List<Action> list;
                    if (!_urlWaiters.TryGetValue(key, out list)) { list = new List<Action>(); _urlWaiters[key] = list; }
                    list.Add(onReady);
                }
                if (!_urlLoading.Contains(key)) { _urlLoading.Add(key); start = true; }
            }
            if (start) { var _ignore = LoadUrlAsync(url, key, highResolution); }
            return null;
        }

        // Les offres utilisent un cache mémoire distinct : une vignette d'Actus déjà chargée
        // ne doit pas remplacer leur visuel HD. Le cache disque conserve les octets originaux.
        public static Image ForDeal(Offre offer, Action ready)
        {
            Image image = offer.Image == null ? null : GetUrl(offer.Image, ready, true);
            if (image != null || offer.SteamAppId <= 0 || (offer.Image != null && !EchecUrl(offer.Image, true))) return image;
            string root = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + offer.SteamAppId + "/";
            string capsule = root + "capsule_616x353.jpg", header = root + "header.jpg";
            image = GetUrl(capsule, ready, true);
            if (image == null && EchecUrl(capsule, true)) image = GetUrl(header, ready, true);
            if (image == null && EchecUrl(capsule, true) && EchecUrl(header, true)) image = Get(offer.SteamAppId, ready);
            return image;
        }

        /// <summary>Vrai si l'image de cette URL n'a pas pu être chargée (l'appelant passe à son repli).</summary>
        public static bool EchecUrl(string url, bool highResolution = false)
        {
            if (url == null) return false;
            lock (_lock) return _urlFailed.Contains(highResolution ? "hd:" + url : url);
        }

        private static bool UrlImageSure(string url)
        {
            return Offres.LienSur(url) && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task LoadUrlAsync(string url, string key, bool highResolution)
        {
            Image loaded = null;
            try
            {
                string p = UrlDiskPath(url);
                loaded = await Task.Run(() => File.Exists(p) && new FileInfo(p).Length >= MinArtBytes ? Reduit(FromFile(p), highResolution) : null).ConfigureAwait(false);
                if (loaded == null && !UnderTestHarness())
                {
                    await _gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        byte[] data = await TelechargeImage(url).ConfigureAwait(false);
                        if (data != null)
                        {
                            loaded = Reduit(FromBytes(data), highResolution);
                            if (loaded != null) { try { Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllBytes(p, data); } catch { } }
                        }
                    }
                    finally { _gate.Release(); }
                }
            }
            catch { }

            Action[] cbs;
            lock (_lock)
            {
                if (loaded != null) _urlCache[key] = loaded; else _urlFailed.Add(key);
                _urlLoading.Remove(key);
                List<Action> list;
                cbs = _urlWaiters.TryGetValue(key, out list) ? list.ToArray() : new Action[0];
                _urlWaiters.Remove(key);
            }
            // Rappel aussi en cas d'échec : la carte bascule alors sur son repli.
            foreach (var cb in cbs) { try { cb(); } catch { } }
        }

        /// <summary>Téléchargement borné (https, 4 Mo, 10 s, User-Agent « ONYX ») ; null si absent ou image d'attente.</summary>
        private static async Task<byte[]> TelechargeImage(string url)
        {
            if (!UrlImageSure(url)) return null;
            if (_httpOnyx == null)
                lock (_lock)
                {
                    if (_httpOnyx == null)
                    {
                        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = MaxImageBytes };
                        h.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ONYX");
                        _httpOnyx = h;
                    }
                }
            try
            {
                byte[] data = await _httpOnyx.GetByteArrayAsync(url).ConfigureAwait(false);
                return data != null && data.Length >= MinArtBytes ? data : null;
            }
            catch { return null; }
        }

        private static string UrlDiskPath(string url)
        {
            byte[] h;
            using (var sha = SHA256.Create()) h = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
            var sb = new StringBuilder("u-");
            for (int i = 0; i < 12; i++) sb.Append(h[i].ToString("x2"));
            return Path.Combine(AppContext.BaseDirectory, "bt-gamecache", sb.Append(".img").ToString());
        }

        /// <summary>Copie réduite à HauteurMax (une jaquette Epic d'origine dépasse 2 000 px) ; libère l'original.</summary>
        private static Image Reduit(Image img, bool highResolution = false)
        {
            if (img == null) return null;
            Size size = highResolution ? DealArtwork.CacheSize(img.Size) : new Size(Math.Max(1, img.Width * Math.Min(img.Height, HauteurMax) / img.Height), Math.Min(img.Height, HauteurMax));
            if (size == img.Size) return img;
            try
            {
                var b = new Bitmap(size.Width, size.Height);
                using (var g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, 0, 0, size.Width, size.Height);
                }
                img.Dispose();
                return b;
            }
            catch { return img; }
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
#if BTTEST
            // Capture des bons plans avec les vraies jaquettes, sur demande explicite du harnais.
            if (Environment.GetEnvironmentVariable("BT_ART_NETWORK") == "1") return false;
#endif
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
