using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BTOptimizer
{
    /// <summary>
    /// FICHE STEAM d'un jeu (logique PURE) : genre, développeur, éditeur, date de sortie et avis des
    /// joueurs, tirés de la réponse publique IStoreBrowseService/GetItems (l'appel qu'ONYX fait déjà
    /// pour reconnaître les logiciels, mise en cache 7 jours). Seuls des AppID partent : aucune
    /// donnée du compte.
    ///
    /// Le genre : Steam rend les étiquettes les plus votées (« Cyberpunk », « Monde ouvert »,
    /// « Nudité », « RPG »…). On garde la première qui est un vrai genre de la table ci-dessous.
    /// </summary>
    internal static class FicheSteam
    {
        public sealed class Fiche
        {
            public int AppId;
            public string Genre;
            public string Developpeur;
            public string Editeur;
            /// <summary>Date de sortie (secondes Unix, UTC) ; 0 = inconnue.</summary>
            public long Sortie;
            public int AvisPourcent = -1;
            public int AvisNombre;
            public string AvisLibelle;
        }

        /// <summary>Étiquettes Steam qui désignent un genre (identifiant → nom affiché), relevées dans
        /// IStoreService/GetTagList en français le 28/11/2026. L'ordre de la table ne compte pas :
        /// c'est le poids donné par les joueurs qui décide.</summary>
        public static readonly Dictionary<int, string> Genres = new Dictionary<int, string>
        {
            { 19, "Action" }, { 21, "Aventure" }, { 122, "RPG" }, { 9, "Stratégie" }, { 599, "Simulation" },
            { 701, "Sport" }, { 699, "Course" }, { 128, "Massivement multijoueur" }, { 1663, "FPS" },
            { 1774, "Tir" }, { 1667, "Horreur" }, { 1662, "Survie" }, { 3993, "Combat" }, { 3810, "Bac à sable" },
            { 1716, "Roguelike" }, { 1718, "MOBA" }, { 176981, "Battle Royale" }, { 1752, "Rythme" },
            { 4328, "Construction de villes" }, { 12472, "Gestion" }, { 1676, "Stratégie en temps réel" },
            { 1708, "Tactique" }, { 1628, "Metroidvania" },
        };

        /// <summary>Adresse de l'appel (lot de 50 AppID au plus) ; null si aucun AppID valide.</summary>
        public static string Url(IEnumerable<int> appids)
        {
            if (appids == null) return null;
            var sb = new StringBuilder("{\"ids\":[");
            int n = 0;
            foreach (int id in appids)
            {
                if (id <= 0) continue;
                if (n == 50) break;
                if (n > 0) sb.Append(',');
                sb.Append("{\"appid\":").Append(id.ToString(CultureInfo.InvariantCulture)).Append('}');
                n++;
            }
            if (n == 0) return null;
            sb.Append("],\"context\":{\"language\":\"french\",\"country_code\":\"FR\"},")
              .Append("\"data_request\":{\"include_basic_info\":true,\"include_tag_count\":20,\"include_release\":true,\"include_reviews\":true}}");
            return "https://api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json=" + Uri.EscapeDataString(sb.ToString());
        }

        /// <summary>Genre : première étiquette (dans l'ordre des poids décroissants) qui est un genre.</summary>
        public static string Genre(IList<int> tagidsParPoids)
        {
            if (tagidsParPoids == null) return null;
            foreach (int t in tagidsParPoids)
            {
                string g;
                if (Genres.TryGetValue(t, out g)) return g;
            }
            return null;
        }

        /// <summary>Lit une réponse GetItems ; les fiches ratées ou illisibles sont sautées.</summary>
        public static List<Fiche> Lit(string json)
        {
            var res = new List<Fiche>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json ?? ""))
                {
                    JsonElement r, items;
                    if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("response", out r)
                        || r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("store_items", out items) || items.ValueKind != JsonValueKind.Array) return res;
                    foreach (JsonElement it in items.EnumerateArray())
                    {
                        try
                        {
                            int id, ok;
                            if (!Entier(it, "success", out ok) || ok != 1 || !Entier(it, "appid", out id) || id <= 0) continue;
                            var f = new Fiche { AppId = id };

                            // Étiquettes par poids décroissant (Steam les rend déjà triées, on ne s'y fie pas).
                            JsonElement tags;
                            if (it.TryGetProperty("tags", out tags) && tags.ValueKind == JsonValueKind.Array)
                            {
                                var l = new List<KeyValuePair<int, int>>();
                                foreach (JsonElement t in tags.EnumerateArray())
                                {
                                    int tid, poids;
                                    if (Entier(t, "tagid", out tid)) l.Add(new KeyValuePair<int, int>(tid, Entier(t, "weight", out poids) ? poids : 0));
                                }
                                l.Sort((a, b) => b.Value.CompareTo(a.Value));
                                var ids = new List<int>();
                                foreach (var kv in l) ids.Add(kv.Key);
                                f.Genre = Genre(ids);
                            }

                            JsonElement bi;
                            if (it.TryGetProperty("basic_info", out bi) && bi.ValueKind == JsonValueKind.Object)
                            {
                                f.Developpeur = Noms(bi, "developers");
                                f.Editeur = Noms(bi, "publishers");
                            }

                            JsonElement rel, d;
                            if (it.TryGetProperty("release", out rel) && rel.ValueKind == JsonValueKind.Object)
                            {
                                long o = 0, s = 0;
                                if (rel.TryGetProperty("original_release_date", out d) && d.ValueKind == JsonValueKind.Number) d.TryGetInt64(out o);
                                if (rel.TryGetProperty("steam_release_date", out d) && d.ValueKind == JsonValueKind.Number) d.TryGetInt64(out s);
                                f.Sortie = o > 0 ? o : s;   // sortie d'origine si le jeu est arrivé plus tard sur Steam
                            }

                            JsonElement rv, sm;
                            if (it.TryGetProperty("reviews", out rv) && rv.ValueKind == JsonValueKind.Object
                                && rv.TryGetProperty("summary_filtered", out sm) && sm.ValueKind == JsonValueKind.Object)
                            {
                                int p, n;
                                if (Entier(sm, "percent_positive", out p) && Entier(sm, "review_count", out n) && n > 0)
                                {
                                    f.AvisPourcent = Math.Max(0, Math.Min(100, p));
                                    f.AvisNombre = n;
                                    JsonElement lb;
                                    if (sm.TryGetProperty("review_score_label", out lb) && lb.ValueKind == JsonValueKind.String) f.AvisLibelle = lb.GetString();
                                }
                            }
                            res.Add(f);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return res;
        }

        private static string Noms(JsonElement bi, string cle)
        {
            JsonElement a;
            if (!bi.TryGetProperty(cle, out a) || a.ValueKind != JsonValueKind.Array) return null;
            var l = new List<string>();
            foreach (JsonElement x in a.EnumerateArray())
            {
                JsonElement n;
                if (x.ValueKind == JsonValueKind.Object && x.TryGetProperty("name", out n) && n.ValueKind == JsonValueKind.String)
                {
                    string s = (n.GetString() ?? "").Trim();
                    if (s.Length > 0 && !l.Contains(s)) l.Add(s);
                }
            }
            return l.Count == 0 ? null : string.Join(", ", l);
        }

        private static bool Entier(JsonElement e, string nom, out int v)
        {
            JsonElement x;
            v = 0;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nom, out x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out v);
        }

        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        /// <summary>« 10 décembre 2020 », ou null si la date est inconnue.</summary>
        public static string TexteSortie(long unix)
        {
            if (unix <= 0) return null;
            DateTime d = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
            return d.Day.ToString(CultureInfo.InvariantCulture) + (d.Day == 1 ? "er " : " ") + d.ToString("MMMM yyyy", Fr);
        }

        /// <summary>« 87 % positives sur 911 882 avis (très positives) », ou null sans avis.</summary>
        public static string TexteAvis(Fiche f)
        {
            if (f == null || f.AvisPourcent < 0 || f.AvisNombre <= 0) return null;
            string s = f.AvisPourcent.ToString(CultureInfo.InvariantCulture) + " % positives sur "
                     + f.AvisNombre.ToString("#,0", Fr).Replace(' ', ' ').Replace(' ', ' ') + " avis";
            if (!string.IsNullOrWhiteSpace(f.AvisLibelle)) s += " (" + f.AvisLibelle.Trim() + ")";
            return s;
        }

        /// <summary>Lignes de la fiche, dans l'ordre d'affichage ; vide si rien n'est connu.</summary>
        public static List<string> Lignes(Fiche f)
        {
            var l = new List<string>();
            if (f == null) return l;
            if (!string.IsNullOrEmpty(f.Genre)) l.Add("Genre : " + f.Genre);
            if (!string.IsNullOrEmpty(f.Developpeur) && string.Equals(f.Developpeur, f.Editeur, StringComparison.OrdinalIgnoreCase))
                l.Add("Développé et édité par " + f.Developpeur);
            else
            {
                if (!string.IsNullOrEmpty(f.Developpeur)) l.Add("Développeur : " + f.Developpeur);
                if (!string.IsNullOrEmpty(f.Editeur)) l.Add("Éditeur : " + f.Editeur);
            }
            string sortie = TexteSortie(f.Sortie);
            if (sortie != null) l.Add("Sortie : " + sortie);
            string avis = TexteAvis(f);
            if (avis != null) l.Add("Avis Steam : " + avis);
            return l;
        }
    }

    /// <summary>Fiches Steam déjà lues pendant cette session (mémoire seulement ; le cache disque
    /// est celui d'OffresReseau). Accès depuis n'importe quel fil.</summary>
    internal static class FichesSteam
    {
        private static readonly Dictionary<int, FicheSteam.Fiche> _parId = new Dictionary<int, FicheSteam.Fiche>();
        private static readonly object _verrou = new object();

        /// <summary>Range les fiches d'une réponse ; rend le nombre de fiches nouvelles ou changées.</summary>
        public static int Memorise(string json)
        {
            int n = 0;
            foreach (FicheSteam.Fiche f in FicheSteam.Lit(json))
                lock (_verrou)
                {
                    if (!_parId.ContainsKey(f.AppId)) n++;
                    _parId[f.AppId] = f;
                }
            return n;
        }

        public static FicheSteam.Fiche De(int appId)
        {
            if (appId <= 0) return null;
            lock (_verrou)
            {
                FicheSteam.Fiche f;
                return _parId.TryGetValue(appId, out f) ? f : null;
            }
        }

        public static bool Connue(int appId) { return De(appId) != null; }
    }
}
