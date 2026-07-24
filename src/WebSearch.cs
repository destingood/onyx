using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// Recherche web du Copilote (DuckDuckGo HTML, sans clé) : pour les questions d'ACTUALITÉ ou
    /// de temps réel (résultats de match, météo, prix, news…) que le modèle local ne peut pas
    /// connaître, on récupère quelques extraits du web et on les donne au modèle pour qu'il réponde
    /// EN S'APPUYANT dessus. Seule fonction de l'app qui sort sur internet — annoncée clairement.
    /// </summary>
    internal static class WebSearch
    {
        private static readonly HttpClient Http = BuildClient();

        private static HttpClient BuildClient()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(14) };
            h.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
            return h;
        }

        public sealed class Result { public string Title; public string Snippet; public string Domain; }

        /// <summary>Interroge DuckDuckGo et rend jusqu'à 'max' résultats (titre + extrait + domaine).
        /// Liste vide si hors-ligne ou rien trouvé. BLOQUANT (à appeler en tâche de fond).</summary>
        public static List<Result> Query(string q, int max)
        {
            var list = new List<Result>();
            try
            {
                string url = "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(q);
                string html;
                using (var cts = new System.Threading.CancellationTokenSource(13000))
                using (var r = Http.GetAsync(url, cts.Token).Result)
                    html = r.Content.ReadAsStringAsync().Result;
                if (string.IsNullOrEmpty(html)) return list;

                var titles = Grab(html, "result__a");
                var snips = Grab(html, "result__snippet");
                var domains = GrabDomains(html);
                int n = Math.Max(titles.Count, snips.Count);
                for (int i = 0; i < n && list.Count < max; i++)
                {
                    string t = i < titles.Count ? titles[i] : "";
                    string s = i < snips.Count ? snips[i] : "";
                    if (t.Length == 0 && s.Length == 0) continue;
                    list.Add(new Result { Title = t, Snippet = s, Domain = i < domains.Count ? domains[i] : "" });
                }
            }
            catch { }
            return list;
        }

        // Texte des ancres d'une classe DDG donnée (result__a / result__snippet), nettoyé.
        private static List<string> Grab(string html, string cls)
        {
            var res = new List<string>();
            var rx = new Regex("class=\"" + cls + "\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
            foreach (Match m in rx.Matches(html))
            {
                string t = Clean(m.Groups[1].Value);
                if (t.Length >= 2) res.Add(t);
            }
            return res;
        }

        // Domaine réel derrière les redirections DDG (paramètre uddg=<url encodée>).
        private static List<string> GrabDomains(string html)
        {
            var res = new List<string>();
            var rx = new Regex("class=\"result__a\"[^>]*href=\"([^\"]+)\"", RegexOptions.Singleline);
            foreach (Match m in rx.Matches(html))
            {
                string href = m.Groups[1].Value;
                string dom = "";
                try
                {
                    int k = href.IndexOf("uddg=", StringComparison.Ordinal);
                    if (k >= 0)
                    {
                        string enc = href.Substring(k + 5);
                        int amp = enc.IndexOf('&'); if (amp >= 0) enc = enc.Substring(0, amp);
                        string real = Uri.UnescapeDataString(enc);
                        dom = new Uri(real).Host.Replace("www.", "");
                    }
                }
                catch { }
                res.Add(dom);
            }
            return res;
        }

        private static string Clean(string s)
        {
            s = Regex.Replace(s, "<[^>]+>", "");     // retire le balisage (ex. <b> du terme trouvé)
            s = WebUtility.HtmlDecode(s);            // &#x27; → ', &amp; → & …
            s = Regex.Replace(s, "\\s+", " ").Trim();
            return s;
        }

        /// <summary>Bloc de contexte lisible par le modèle : extraits numérotés + liste des sources.</summary>
        public static string Context(List<Result> results)
        {
            var sb = new StringBuilder("Résultats de recherche web (aujourd'hui) :\n");
            var doms = new List<string>();
            for (int i = 0; i < results.Count; i++)
            {
                sb.Append(i + 1).Append(". ").Append(results[i].Title);
                if (!string.IsNullOrEmpty(results[i].Snippet)) sb.Append(" — ").Append(results[i].Snippet);
                sb.Append('\n');
                if (!string.IsNullOrEmpty(results[i].Domain) && !doms.Contains(results[i].Domain)) doms.Add(results[i].Domain);
            }
            return sb.ToString();
        }

        /// <summary>Domaines sources distincts (pour citer « d'après lemonde.fr, wikipedia.org »).</summary>
        public static string Sources(List<Result> results)
        {
            var doms = new List<string>();
            foreach (var r in results) if (!string.IsNullOrEmpty(r.Domain) && !doms.Contains(r.Domain)) doms.Add(r.Domain);
            return doms.Count > 0 ? string.Join(", ", doms.GetRange(0, Math.Min(3, doms.Count)).ToArray()) : "le web";
        }
    }
}
