using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace BTOptimizer
{
    /// <summary>
    /// Connecteur WIKIPÉDIA (français) : la base de connaissances universelle, en direct et SOURCÉE.
    /// Pour « tout et n'importe quoi » (personnes, marques, lieux, concepts…), on récupère le résumé
    /// encyclopédique et on l'utilise pour ANCRER la réponse du modèle local (anti-hallucination) —
    /// plutôt que de deviner ou de scraper des snippets. Gratuit, sans clé, 100 % en lecture.
    /// API REST publique : /api/rest_v1/page/summary + opensearch pour trouver le bon titre.
    /// </summary>
    internal static class Wikipedia
    {
        private static readonly HttpClient Http = Build();

        private static HttpClient Build()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            // Wikimedia demande un User-Agent identifiant l'app.
            h.DefaultRequestHeaders.Add("User-Agent", "ONYX-Copilote/1.0 (assistant PC local; contact via app)");
            h.DefaultRequestHeaders.Add("Accept", "application/json");
            return h;
        }

        public sealed class Page { public string Title; public string Extract; public string Url; }

        /// <summary>Cherche le meilleur article et renvoie son résumé (ou null si rien de net /
        /// page d'homonymie / extrait trop court).</summary>
        public static Page Lookup(string query)
        {
            try
            {
                string title = BestTitle(query);
                if (string.IsNullOrEmpty(title)) return null;
                string url = "https://fr.wikipedia.org/api/rest_v1/page/summary/" + Uri.EscapeDataString(title.Replace(' ', '_'));
                string json = Get(url);
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    var root = d.RootElement;
                    if (root.TryGetProperty("type", out var t) && t.GetString() == "disambiguation") return null;
                    string extract = root.TryGetProperty("extract", out var e) ? e.GetString() : "";
                    if (string.IsNullOrEmpty(extract) || extract.Length < 40) return null;
                    string realTitle = root.TryGetProperty("title", out var rt) ? rt.GetString() : title;
                    string page = "https://fr.wikipedia.org/wiki/" + Uri.EscapeDataString(realTitle.Replace(' ', '_'));
                    if (root.TryGetProperty("content_urls", out var cu) && cu.TryGetProperty("desktop", out var dk)
                        && dk.TryGetProperty("page", out var pg))
                    {
                        string u = pg.GetString(); if (!string.IsNullOrEmpty(u)) page = u;
                    }
                    return new Page { Title = realTitle, Extract = extract.Trim(), Url = page };
                }
            }
            catch { return null; }
        }

        // opensearch → premier titre correspondant (namespace article uniquement).
        private static string BestTitle(string query)
        {
            try
            {
                string url = "https://fr.wikipedia.org/w/api.php?action=opensearch&limit=1&namespace=0&format=json&search="
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
            }
            catch { }
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

        /// <summary>Contexte prêt à injecter dans le prompt (résumé + titre).</summary>
        public static string Context(Page p)
        {
            return p == null ? "" : "Extrait encyclopédique de Wikipédia — article « " + p.Title + " » :\n" + p.Extract + "\n";
        }
    }
}
