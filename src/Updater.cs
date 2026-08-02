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
        /// <summary>Dépôts essayés dans l'ordre : d'abord le dépôt PUBLIC de distribution (le code peut
        /// rester privé — seules les versions publiées y sont), puis le dépôt principal.</summary>
        private static readonly string[] DefaultRepos = { "destingood/onyx-releases", "destingood/onyx" };

        /// <summary>Dépôt forcé par l'utilisateur (bt-update-repo.txt), ou null.</summary>
        public static string RepoOverride
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
                return null;
            }
        }

        public static string Repo { get { return RepoOverride ?? DefaultRepos[0]; } }

        /// <summary>MANIFESTE PERSONNEL : une URL HTTPS vers un petit JSON hébergé où tu veux
        /// (GitHub Pages, ton site, un stockage objet…). C'est LA solution quand le dépôt de code
        /// doit rester privé : le code reste secret, seule la version publiée est publique.
        /// Format attendu : { "version": "15.60", "notes": "…", "url": "https://…/ONYX-Setup.exe", "size": 123 }</summary>
        public static string ManifestUrl
        {
            get
            {
                try
                {
                    string f = AppPaths.File("bt-update-url.txt");
                    if (File.Exists(f))
                    {
                        string s = File.ReadAllText(f).Trim();
                        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return s;
                    }
                }
                catch { }
                return null;
            }
        }

        /// <summary>Jeton GitHub LOCAL et facultatif (bt-update-token.txt) : permet de lire un dépôt
        /// PRIVÉ depuis TES machines. Il n'est JAMAIS embarqué dans l'application ni distribué —
        /// un jeton livré aux utilisateurs serait extractible du binaire en quelques secondes.</summary>
        public static string LocalToken
        {
            get
            {
                try
                {
                    string f = AppPaths.File("bt-update-token.txt");
                    if (File.Exists(f))
                    {
                        string s = File.ReadAllText(f).Trim();
                        if (s.Length >= 20) return s;
                    }
                }
                catch { }
                return null;
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

        /// <summary>Le lien de téléchargement est-il acceptable ? GitHub, ou l'hôte du manifeste que
        /// TU as toi-même configuré (on ne fait confiance qu'à ce qui a été choisi explicitement). PUR.</summary>
        public static bool IsTrustedUrl(string url, string manifestUrl = null)
        {
            if (string.IsNullOrEmpty(url)) return false;
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            if (u.Scheme != Uri.UriSchemeHttps) return false;
            string h = u.Host.ToLowerInvariant();
            if (h == "github.com" || h.EndsWith(".github.com") || h == "objects.githubusercontent.com"
                || h == "github.io" || h.EndsWith(".github.io")) return true;
            if (!string.IsNullOrEmpty(manifestUrl))
            {
                Uri m;
                if (Uri.TryCreate(manifestUrl, UriKind.Absolute, out m) && m.Scheme == Uri.UriSchemeHttps
                    && string.Equals(m.Host, u.Host, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Lit un MANIFESTE personnel (JSON simple, hébergé où tu veux). PUR → testable.</summary>
        public static Release ParseManifest(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    var r = d.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) return null;
                    JsonElement v;
                    if (!r.TryGetProperty("version", out v) || v.ValueKind != JsonValueKind.String) return null;
                    var rel = new Release { Tag = v.GetString() };
                    rel.Ver = ParseTag(rel.Tag);
                    if (rel.Ver == null) return null;
                    JsonElement n;
                    if (r.TryGetProperty("notes", out n) && n.ValueKind == JsonValueKind.String) rel.Notes = n.GetString();
                    JsonElement u2;
                    if (r.TryGetProperty("url", out u2) && u2.ValueKind == JsonValueKind.String)
                    {
                        rel.AssetUrl = u2.GetString();
                        try { rel.AssetName = Path.GetFileName(new Uri(rel.AssetUrl).LocalPath); } catch { rel.AssetName = "ONYX-Setup.exe"; }
                    }
                    JsonElement sz;
                    if (r.TryGetProperty("size", out sz) && sz.ValueKind == JsonValueKind.Number) rel.Size = sz.GetInt64();
                    return rel;
                }
            }
            catch { return null; }
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
            // Jeton LOCAL uniquement (jamais embarqué dans l'app distribuée) : lecture d'un dépôt privé.
            try
            {
                string t = LocalToken;
                if (!string.IsNullOrEmpty(t)) h.DefaultRequestHeaders.Add("Authorization", "Bearer " + t);
            }
            catch { }
            return h;
        }

        public static Version CurrentVersion()
        {
            try { return typeof(Updater).Assembly.GetName().Version; }
            catch { return null; }
        }

        /// <summary>Cherche une nouvelle version. Ordre : 1) ton MANIFESTE personnel s'il est
        /// configuré (marche même avec un dépôt de code privé) ; 2) le dépôt public de distribution ;
        /// 3) le dépôt principal. 'status' explique toujours ce qui s'est passé.</summary>
        public static Release Check(out string status)
        {
            status = "";

            // 1) Manifeste personnel : la voie recommandée quand le code reste privé.
            string manifest = ManifestUrl;
            if (!string.IsNullOrEmpty(manifest))
            {
                try
                {
                    using (var h = Http())
                    using (var resp = h.GetAsync(manifest).Result)
                    {
                        if (resp.IsSuccessStatusCode)
                        {
                            var rel = ParseManifest(resp.Content.ReadAsStringAsync().Result);
                            if (rel != null) return rel;
                            status = "Ton manifeste de mise à jour est illisible (JSON attendu : version, notes, url, size).";
                            return null;
                        }
                        status = "Ton manifeste de mise à jour a répondu « " + (int)resp.StatusCode + " » (" + manifest + ").";
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    status = "Manifeste de mise à jour injoignable (" + ex.GetType().Name + ").";
                    return null;
                }
            }

            // 2-3) Releases GitHub : dépôt forcé, sinon distribution publique puis dépôt principal.
            string[] repos = RepoOverride != null ? new[] { RepoOverride } : DefaultRepos;
            bool sawPrivate = false, sawRate = false;
            foreach (var repo in repos)
            {
                try
                {
                    using (var h = Http())
                    using (var resp = h.GetAsync("https://api.github.com/repos/" + repo + "/releases/latest").Result)
                    {
                        int code = (int)resp.StatusCode;
                        if (code == 404) { sawPrivate = true; continue; }        // privé, inexistant, ou sans release
                        if (code == 401) { sawPrivate = true; continue; }        // jeton invalide/expiré
                        if (code == 403) { sawRate = true; continue; }
                        if (!resp.IsSuccessStatusCode) continue;
                        var rel = ParseRelease(resp.Content.ReadAsStringAsync().Result);
                        if (rel != null && rel.Ver != null) return rel;
                    }
                }
                catch (Exception ex)
                {
                    status = "Impossible de joindre le serveur des mises à jour (" + ex.GetType().Name + "). "
                           + "Vérifie ta connexion — ONYX continue de fonctionner normalement.";
                    return null;
                }
            }

            if (sawRate) status = "GitHub limite temporairement les requêtes (trop de vérifications). Réessaie dans une heure.";
            else if (sawPrivate)
                status = "Aucune version publiée n'est visible : le dépôt est privé, ou aucune Release n'existe encore.\n"
                       + "Deux façons de faire marcher la mise à jour SANS ouvrir ton code :\n"
                       + "  • publier les versions dans un dépôt public séparé (par défaut : « "
                       + DefaultRepos[0] + " ») — le code reste privé, seule l'installation est publique ;\n"
                       + "  • ou héberger un petit manifeste JSON où tu veux et mettre son adresse dans "
                       + "« bt-update-url.txt » (version, notes, url, size).";
            else status = "Aucune information de mise à jour n'a pu être obtenue.";
            return null;
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
            else if (!IsTrustedUrl(rel.AssetUrl, ManifestUrl))
                sb.Append("\n⚠️ Le lien de téléchargement ne vient pas de GitHub : je REFUSE de le lancer.");
            else
                sb.Append("\nLe bouton télécharge l'installateur officiel (").Append(SteamGames.Human(rel.Size))
                  .Append("), puis le lance. Tes réglages, ta mémoire et ton journal sont CONSERVÉS.");
            return sb.ToString();
        }

        /// <summary>Télécharge l'installateur dans le dossier temporaire. Chemin, ou null.</summary>
        public static string Download(Release rel, Action<string, int> log)
        {
            if (rel == null || !IsTrustedUrl(rel.AssetUrl, ManifestUrl)) return null;
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
