using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace BTOptimizer
{
    /// <summary>
    /// « POPULAIRE EN CE MOMENT » (logique PURE) : classement public de Steam par joueurs connectés,
    /// ISteamChartsService/GetGamesByConcurrentPlayers/v1, sans clé. Forme vérifiée le 28/11/2026 :
    /// {"response":{"last_update":…,"ranks":[{"rank":1,"appid":730,"concurrent_in_game":693285,
    /// "peak_in_game":1206781},…]}}. Les noms, prix et images viennent ensuite des fiches Steam.
    /// </summary>
    internal static class Populaires
    {
        public const string Url = "https://api.steampowered.com/ISteamChartsService/GetGamesByConcurrentPlayers/v1/";

        /// <summary>Nombre de jeux montrés : un seul lot de fiches Steam.</summary>
        public const int Max = 48;

        public sealed class Rang { public int AppId, Joueurs, Pic; }

        /// <summary>Jeux dans l'ordre du classement, au plus <paramref name="max"/>, sans doublon ; réponse illisible → vide.</summary>
        public static List<Rang> Lit(string json, int max)
        {
            var res = new List<Rang>();
            if (string.IsNullOrWhiteSpace(json) || max <= 0) return res;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement r, ranks;
                    if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("response", out r)
                        || r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("ranks", out ranks) || ranks.ValueKind != JsonValueKind.Array)
                        return res;
                    var lignes = new List<KeyValuePair<long, Rang>>();
                    var vus = new HashSet<int>();
                    foreach (JsonElement e in ranks.EnumerateArray())
                    {
                        long id = Entier(e, "appid"), rang = Entier(e, "rank");
                        if (id <= 0 || id > int.MaxValue || !vus.Add((int)id)) continue;
                        lignes.Add(new KeyValuePair<long, Rang>(rang > 0 ? rang : long.MaxValue, new Rang
                        {
                            AppId = (int)id,
                            Joueurs = (int)Math.Min(int.MaxValue, Math.Max(0, Entier(e, "concurrent_in_game"))),
                            Pic = (int)Math.Min(int.MaxValue, Math.Max(0, Entier(e, "peak_in_game"))),
                        }));
                    }
                    // Tri stable par rang : à rang égal ou absent, l'ordre de la réponse.
                    var ordre = new List<int>();
                    for (int i = 0; i < lignes.Count; i++) ordre.Add(i);
                    ordre.Sort((a, b) => { int c = lignes[a].Key.CompareTo(lignes[b].Key); return c != 0 ? c : a.CompareTo(b); });
                    foreach (int i in ordre) { if (res.Count == max) break; res.Add(lignes[i].Value); }
                }
            }
            catch (JsonException) { res.Clear(); }
            return res;
        }

        /// <summary>« 693 285 joueurs en ce moment » (espace fine insécable entre milliers), vide si inconnu.</summary>
        public static string TexteJoueurs(int joueurs)
        {
            if (joueurs <= 0) return "";
            return Milliers(joueurs) + (joueurs > 1 ? " joueurs en ce moment" : " joueur en ce moment");
        }

        public static string Milliers(long n)
        {
            var nf = new NumberFormatInfo { NumberGroupSeparator = " ", NumberGroupSizes = new[] { 3 } };
            return n.ToString("#,0", nf);
        }

        private static long Entier(JsonElement e, string nom)
        {
            JsonElement v;
            if (!e.TryGetProperty(nom, out v)) return 0;
            long n;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out n)) return n;
            if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out n)) return n;
            return 0;
        }
    }
}
