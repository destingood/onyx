using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace BTOptimizer
{
    /// <summary>
    /// MISE À JOUR INTÉGRÉE : plus besoin de retélécharger et réinstaller l'app à la main. ONYX
    /// interroge les Releases GitHub du projet, compare la version, et propose la mise à jour —
    /// téléchargement du programme d'installation officiel, puis lancement. L'installateur écrase
    /// l'ancienne version en gardant les données (elles vivent à côté, voir AppPaths).
    ///
    /// Règles tenues :
    ///  • RIEN n'est téléchargé ni installé sans un clic explicite ;
    ///  • le fichier ne peut venir QUE de github.com (une réponse détournée ne peut pas faire
    ///    exécuter n'importe quoi) ;
    ///  • si aucune version n'est publiée, l'app le dit franchement au lieu d'inventer.
    /// </summary>
    internal static class Updater
    {
        /// <summary>Dépôt par défaut ; remplaçable dans bt-update-repo.txt (« proprio/depot »).</summary>
        private const string DefaultRepo = "destingood/onyx";

        public static string Repo
        {
            get
            {
                try
                {
                    string f = AppPaths.File("bt-update-repo.txt");
                    if (File.Exists(f))
                    {
                        string s = File.ReadAllText(f).Trim();
                        if (s.Length > 3 && s.Contains("/")) return s;
                    }
                }
                catch { }
                return DefaultRepo;
            }
        }

        public sealed class Release
        {
            public Version Ver;
            public string Tag;
            public string Notes;
            public string AssetUrl;
            public string AssetName;
            public long Size;
        }

        /// <summary>« v15.58 », « 15.58.0.0 », « ONYX 15.58 » → Version. null si illisible. PUR.</summary>
        public static Version ParseTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(tag, "(\\d+)\\.(\\d+)(?:\\.(\\d+))?(?:\\.(\\d+))?");
            if (!m.Success) return null;
            try
            {
                int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value);
                int c = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
                int d = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0;
                return new Version(a, b, c, d);
            }
            catch { return null; }
        }

        /// <summary>Compare en ignorant la révision (on publie en Majeur.Mineur). PUR.</summary>
        public static bool IsNewer(Version current, Version remote)
        {
            if (remote == null) return false;
            if (current == null) return true;
            if (remote.Major != current.Major) return remote.Major > current.Major;
            return remote.Minor > current.Minor;
        }

        /// <summary>Le lien de téléchargement est-il acceptable ? (github.com uniquement) PUR.</summary>
        public static bool IsTrustedUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            if (u.Scheme != Uri.UriSchemeHttps) return false;
            string h = u.Host.ToLowerInvariant();
            return h == "github.com" || h.EndsWith(".github.com") || h == "objects.githubusercontent.com";
        }

        /// <summary>Lit la réponse JSON d'une Release GitHub. PUR → testable hors ligne.</summary>
        public static Release ParseRelease(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    var r = d.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) return null;
                    JsonElement tagEl;
                    if (!r.TryGetProperty("tag_name", out tagEl) || tagEl.ValueKind != JsonValueKind.String) return null;
                    var rel = new Release { Tag = tagEl.GetString() };
                    rel.Ver = ParseTag(rel.Tag);
                    JsonElement body;
                    if (r.TryGetProperty("body", out body) && body.ValueKind == JsonValueKind.String) rel.Notes = body.GetString();
                    JsonElement assets;
                    if (r.TryGetProperty("assets", out assets) && assets.ValueKind == JsonValueKind.Array)
                        foreach (var a in assets.EnumerateArray())
                        {
                            JsonElement nm, url, sz;
                            if (!a.TryGetProperty("name", out nm) || nm.ValueKind != JsonValueKind.String) continue;
                            if (!a.TryGetProperty("browser_download_url", out url) || url.ValueKind != JsonValueKind.String) continue;
                            string name = nm.GetString();
                            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                            // on privilégie l'installateur officiel
                            bool better = rel.AssetUrl == null || name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!better) continue;
                            rel.AssetName = name;
                            rel.AssetUrl = url.GetString();
                            rel.Size = a.TryGetProperty("size", out sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetInt64() : 0;
                        }
                    return rel;
                }
            }
            catch { return null; }
        }

        private static HttpClient Http()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            h.DefaultRequestHeaders.Add("User-Agent", "ONYX-Updater");
            h.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            return h;
        }

        public static Version CurrentVersion()
        {
            try { return typeof(Updater).Assembly.GetName().Version; }
            catch { return null; }
        }

        /// <summary>Interroge GitHub. 'status' explique toujours ce qui s'est passé.</summary>
        public static Release Check(out string status)
        {
            status = "";
            try
            {
                using (var h = Http())
                using (var resp = h.GetAsync("https://api.github.com/repos/" + Repo + "/releases/latest").Result)
                {
                    if ((int)resp.StatusCode == 404)
                    {
                        status = "Aucune version n'est publiée pour l'instant (ou le dépôt est privé). "
                               + "ONYX vérifiera de nouveau plus tard — rien à faire de ton côté.";
                        return null;
                    }
                    if ((int)resp.StatusCode == 403)
                    {
                        status = "GitHub limite temporairement les requêtes (trop de vérifications). Réessaie dans une heure.";
                        return null;
                    }
                    if (!resp.IsSuccessStatusCode)
                    {
                        status = "Le serveur des mises à jour a répondu « " + (int)resp.StatusCode + " ». Réessaie plus tard.";
                        return null;
                    }
                    var rel = ParseRelease(resp.Content.ReadAsStringAsync().Result);
                    if (rel == null || rel.Ver == null) { status = "Réponse du serveur illisible — rien n'a été téléchargé."; return null; }
                    return rel;
                }
            }
            catch (Exception ex)
            {
                status = "Impossible de joindre le serveur des mises à jour (" + ex.GetType().Name + "). "
                       + "Vérifie ta connexion — ONYX continue de fonctionner normalement.";
                return null;
            }
        }

        /// <summary>Texte affiché à l'utilisateur. PUR → testable.</summary>
        public static string Describe(Version current, Release rel, string status)
        {
            if (rel == null) return "🔄 " + (string.IsNullOrEmpty(status) ? "Rien à signaler." : status);
            string cur = current == null ? "?" : current.Major + "." + current.Minor.ToString("00");
            string neu = rel.Ver.Major + "." + rel.Ver.Minor.ToString("00");
            if (!IsNewer(current, rel.Ver))
                return "✅ ONYX est à jour (version " + cur + "). La dernière version publiée est la " + neu + ".";
            var sb = new System.Text.StringBuilder();
            sb.Append("🔄 Une nouvelle version est disponible : ").Append(neu).Append("  (tu as la ").Append(cur).Append(")\n");
            if (!string.IsNullOrEmpty(rel.Notes))
            {
                string n = rel.Notes.Replace("\r", "");
                if (n.Length > 900) n = n.Substring(0, 900) + "…";
                sb.Append('\n').Append(n).Append('\n');
            }
            if (rel.AssetUrl == null)
                sb.Append("\n⚠️ Cette version n'a pas de programme d'installation attaché : télécharge-la depuis GitHub.");
            else if (!IsTrustedUrl(rel.AssetUrl))
                sb.Append("\n⚠️ Le lien de téléchargement ne vient pas de GitHub : je REFUSE de le lancer.");
            else
                sb.Append("\nLe bouton télécharge l'installateur officiel (").Append(SteamGames.Human(rel.Size))
                  .Append("), puis le lance. Tes réglages, ta mémoire et ton journal sont CONSERVÉS.");
            return sb.ToString();
        }

        /// <summary>Télécharge l'installateur dans le dossier temporaire. Chemin, ou null.</summary>
        public static string Download(Release rel, Action<string, int> log)
        {
            if (rel == null || !IsTrustedUrl(rel.AssetUrl)) return null;
            try
            {
                string dest = Path.Combine(Path.GetTempPath(), rel.AssetName ?? "ONYX-Setup.exe");
                using (var h = Http())
                using (var resp = h.GetAsync(rel.AssetUrl, HttpCompletionOption.ResponseHeadersRead).Result)
                {
                    if (!resp.IsSuccessStatusCode) return null;
                    using (var src = resp.Content.ReadAsStreamAsync().Result)
                    using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buf = new byte[131072];
                        long done = 0; int n; int lastPct = -1;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            dst.Write(buf, 0, n);
                            done += n;
                            if (log != null && rel.Size > 0)
                            {
                                int pct = (int)(done * 100 / rel.Size);
                                if (pct != lastPct && pct % 5 == 0) { lastPct = pct; log("Téléchargement… " + pct + " %", 0); }
                            }
                        }
                    }
                }
                var fi = new FileInfo(dest);
                if (!fi.Exists || fi.Length < 1024) return null;
                if (rel.Size > 0 && Math.Abs(fi.Length - rel.Size) > 4096) return null;   // taille annoncée non respectée
                return dest;
            }
            catch { return null; }
        }

        // Vérification quotidienne discrète (jamais plus d'une fois par jour).
        private static string StampPath { get { return AppPaths.File("bt-update-check.txt"); } }

        public static bool DueToday()
        {
            try
            {
                string today = DateTime.Now.ToString("yyyyMMdd");
                if (File.Exists(StampPath) && File.ReadAllText(StampPath).Trim() == today) return false;
                File.WriteAllText(StampPath, today);
                return true;
            }
            catch { return false; }
        }
    }
}
