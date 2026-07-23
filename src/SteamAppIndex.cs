using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BTOptimizer
{
    /// <summary>
    /// Résout « nom de jeu → AppID Steam » pour afficher la VRAIE jaquette des jeux installés
    /// HORS Steam (EA app, Battle.net, GOG, Epic, jeux hors launcher) : la quasi-totalité des
    /// jeux PC existent sur Steam même quand on les installe ailleurs. Une fois l'AppID connu,
    /// GameArt fait le reste (cache local → cache disque → CDN).
    ///
    /// Méthode : une RECHERCHE CIBLÉE par jeu (≈300 octets) plutôt qu'un index global de 10 Mo.
    /// L'API api.steampowered.com est indisponible/filtrée sur certaines machines (404) alors que
    /// steamcommunity.com répond — c'est donc celle-ci qu'on utilise.
    ///
    /// Chaque réponse est mémorisée sur disque (bt-gamecache\steam-names.txt), y compris les
    /// ÉCHECS (appid 0), pour ne jamais réinterroger deux fois le même titre.
    /// </summary>
    internal static class SteamAppIndex
    {
        private const string SearchUrl = "https://steamcommunity.com/actions/SearchApps/";

        private static readonly Dictionary<string, int> _cache = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly HashSet<string> _pending = new HashSet<string>(StringComparer.Ordinal);
        private static readonly object _lock = new object();
        private static bool _loaded;

        private static string CachePath
        {
            get { return Path.Combine(AppContext.BaseDirectory, "bt-gamecache", "steam-names.txt"); }
        }

        /// <summary>
        /// AppID Steam pour ce nom, ou 0 (pas encore résolu, ou jeu absent de Steam).
        /// <paramref name="onReady"/> est rappelé si une correspondance finit par être trouvée.
        /// </summary>
        public static int Resolve(string name, Action onReady)
        {
            string key = Norm(name);
            if (key.Length < 3) return 0;

            lock (_lock)
            {
                if (!_loaded) { _loaded = true; LoadCache(); }
                int id;
                if (_cache.TryGetValue(key, out id)) return id;   // 0 = échec déjà mémorisé
                if (_pending.Contains(key)) return 0;
                _pending.Add(key);
            }

            string query = name;
            Task.Run(() => SearchAsync(key, query, onReady));
            return 0;
        }

        private static async Task SearchAsync(string key, string name, Action onReady)
        {
            int found = 0;
            try
            {
                using (var h = new HttpClient())
                {
                    h.Timeout = TimeSpan.FromSeconds(12);
                    string json = await h.GetStringAsync(SearchUrl + Uri.EscapeDataString(name)).ConfigureAwait(false);
                    found = PickBest(json, key);
                }
            }
            catch { }   // hors ligne / filtré : on retombe sur l'icône du jeu

            lock (_lock) { _cache[key] = found; SaveCache(); }
            if (found > 0 && onReady != null) { try { onReady(); } catch { } }
        }

        /// <summary>Meilleur candidat : correspondance exacte prioritaire, sinon préfixe commun.</summary>
        private static int PickBest(string json, string key)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) return 0;
                    int near = 0;
                    foreach (JsonElement e in doc.RootElement.EnumerateArray())
                    {
                        JsonElement idEl, nEl;
                        if (!e.TryGetProperty("appid", out idEl) || !e.TryGetProperty("name", out nEl)) continue;

                        // « appid » arrive en chaîne dans cette API.
                        string raw = idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : idEl.ToString();
                        int id;
                        if (!int.TryParse(raw, out id) || id <= 0) continue;

                        string cand = Norm(nEl.GetString());
                        if (cand == key) return id;                                   // exact → on prend

                        // Sinon on accepte l'INCLUSION dans un sens ou l'autre : Steam préfixe ou
                        // suffixe souvent ses titres (« NTE: Neverness to Everness », « Solo Leveling:
                        // ARISE OVERDRIVE »). Garde anti-faux-positif : au moins 5 caractères communs,
                        // et on ne regarde que les résultats d'une recherche SUR CE NOM.
                        if (near == 0 && Math.Min(cand.Length, key.Length) >= 5
                            && (cand.Contains(key) || key.Contains(cand)))
                            near = id;                                                // proche → en réserve
                    }
                    return near;
                }
            }
            catch { return 0; }
        }

        // ------------------------------------------------------------ cache disque
        private static void LoadCache()
        {
            try
            {
                string p = CachePath;
                if (!File.Exists(p)) return;
                foreach (string line in File.ReadAllLines(p))
                {
                    int eq = line.LastIndexOf('=');
                    if (eq <= 0) continue;
                    int id;
                    if (int.TryParse(line.Substring(eq + 1).Trim(), out id))
                        _cache[line.Substring(0, eq)] = id;
                }
            }
            catch { }
        }

        private static void SaveCache()
        {
            try
            {
                string p = CachePath;
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                var sb = new StringBuilder();
                foreach (KeyValuePair<string, int> kv in _cache) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
                File.WriteAllText(p, sb.ToString());
            }
            catch { }
        }

        // Normalisation tolérante : minuscules + alphanumérique (gère ™ ® : - espaces).
        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
    }
}
