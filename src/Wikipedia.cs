using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace BTOptimizer
{
    /// <summary>
    /// Connecteur WIKIPÉDIA : la base de connaissances universelle, en direct et SOURCÉE, pour
    /// « tout et n'importe quoi ». On récupère l'INTRODUCTION complète de l'article (riche) pour
    /// ANCRER la réponse du modèle (anti-hallucination), avec repli FR → EN (l'anglais couvre les
    /// termes tech/gaming/mondiaux absents du FR, ex. « RTX 4090 »). Gratuit, sans clé, lecture seule.
    /// </summary>
    internal static class Wikipedia
    {
        private static readonly HttpClient Http = Build();
        private static readonly string[] Langs = { "fr", "en" };   // FR d'abord, repli EN
        private const int MaxExtract = 1500;                       // borne le prompt

        private static HttpClient Build()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            h.DefaultRequestHeaders.Add("User-Agent", "ONYX-Copilote/1.0 (assistant PC local; contact via app)");
            h.DefaultRequestHeaders.Add("Accept", "application/json");
            return h;
        }

        public sealed class Page { public string Title; public string Extract; public string Url; public string Lang; }

        /// <summary>Meilleur article (FR puis EN) + son introduction. null si rien de net.</summary>
        public static Page Lookup(string query)
        {
            foreach (string lang in Langs)
            {
                try
                {
                    string title = BestTitle(query, lang);
                    if (string.IsNullOrEmpty(title)) continue;
                    string extract = IntroExtract(title, lang);
                    if (string.IsNullOrEmpty(extract) || extract.Length < 40) continue;
                    string low = extract.ToLowerInvariant();
                    if (low.Contains("peut faire référence à") || low.Contains("peut désigner") || low.Contains("may refer to"))
                        continue;   // page d'homonymie
                    if (extract.Length > MaxExtract) extract = extract.Substring(0, MaxExtract).TrimEnd() + "…";
                    string url = "https://" + lang + ".wikipedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_'));
                    return new Page { Title = title, Extract = extract.Trim(), Url = url, Lang = lang };
                }
                catch { }
            }
            return null;
        }

        // opensearch → premier titre correspondant (namespace article uniquement).
        private static string BestTitle(string query, string lang)
        {
            string url = "https://" + lang + ".wikipedia.org/w/api.php?action=opensearch&limit=1&namespace=0&format=json&search="
                       + Uri.EscapeDataString(query);
            string json = Get(url);
            if (string.IsNullOrEmpty(json)) return null;
            using (var d = JsonDocument.Parse(json))
            {
                var arr = d.RootElement;   // [ "requête", [titres], [descriptions], [urls] ]
                if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() >= 2)
                {
                    var titles = arr[1];
                    if (titles.ValueKind == JsonValueKind.Array && titles.GetArrayLength() >= 1)
                        return titles[0].GetString();
                }
            }
            return null;
        }

        // Introduction en texte brut (plus riche que le résumé REST) via prop=extracts&exintro.
        private static string IntroExtract(string title, string lang)
        {
            string url = "https://" + lang + ".wikipedia.org/w/api.php?action=query&prop=extracts&exintro&explaintext"
                       + "&redirects=1&format=json&titles=" + Uri.EscapeDataString(title);
            string json = Get(url);
            if (string.IsNullOrEmpty(json)) return null;
            using (var d = JsonDocument.Parse(json))
            {
                if (!d.RootElement.TryGetProperty("query", out var q)) return null;
                if (!q.TryGetProperty("pages", out var pages)) return null;
                foreach (var p in pages.EnumerateObject())   // clé = pageid (ou -1 si absent)
                {
                    if (p.Name == "-1") continue;
                    if (p.Value.TryGetProperty("extract", out var ex))
                    {
                        string s = ex.GetString();
                        if (!string.IsNullOrEmpty(s)) return s;
                    }
                }
            }
            return null;
        }

        private static string Get(string url)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (var r = Http.GetAsync(url, cts.Token).Result)
            {
                if (!r.IsSuccessStatusCode) return null;
                return r.Content.ReadAsStringAsync().Result;
            }
        }

        /// <summary>Contexte prêt à injecter dans le prompt (intro + titre + langue).</summary>
        public static string Context(Page p)
        {
            if (p == null) return "";
            string src = "Wikipédia" + (p.Lang == "en" ? " (article en anglais)" : "");
            return "Extrait de " + src + " — article « " + p.Title + " » :\n" + p.Extract + "\n";
        }
    }
}
