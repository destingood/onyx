using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// Bons plans (logique PURE) : URL des sources et lecture de leurs réponses JSON.
    /// Formes relevées par appel réel le 21/11/2026 (voir le plan 2026-11-21-bons-plans-et-actus).
    /// Toute entrée vide, non JSON ou de forme inattendue donne une liste vide, jamais une exception.
    /// </summary>
    internal static class OffresSources
    {
        /// <summary>Route non officielle d'Epic : peut changer sans préavis (d'où la lecture défensive).</summary>
        /// <summary>
        /// Vrai pour un corps JSON ou XML (RSS, Atom). Une page HTML, du texte brut ou un corps vide
        /// répondus en 200 (portail captif d'un Wi-Fi public, page d'erreur) ne sont ni lus ni mis en cache.
        /// </summary>
        public static bool ReponsePlausible(string texte)
        {
            if (string.IsNullOrEmpty(texte)) return false;
            string t = texte.TrimStart((char)0xFEFF, ' ', '\t', '\r', '\n');
            if (t.Length == 0) return false;
            if (t[0] == '{' || t[0] == '[') return true;
            if (t[0] != '<') return false;
            string debut = (t.Length > 20 ? t.Substring(0, 20) : t).ToLowerInvariant();
            return !(debut.StartsWith("<!doctype html", StringComparison.Ordinal) || debut.StartsWith("<html", StringComparison.Ordinal) || debut.StartsWith("<head", StringComparison.Ordinal) || debut.StartsWith("<body", StringComparison.Ordinal));
        }

        public const string UrlEpic = "https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale=fr&country=FR&allowCountries=FR";
        public const string UrlGamerPower = "https://www.gamerpower.com/api/giveaways?type=game";
        public const int LotSteam = 50;

        private const string BaseRechercheSteam = "https://store.steampowered.com/search/results/?json=1&cc=fr&l=french&category1=998&count=50&start=";
        private static readonly Regex AppDansLogo = new Regex(@"/steam/apps/(\d+)/", RegexOptions.CultureInvariant);

        /// <summary>
        /// Steam ne respecte pas maxprice=1 et n'a pas de « gratuit en promo » : le plafond envoyé
        /// n'est qu'un dégrossissage, le filtre exact est refait par Offres.Filtrer.
        /// </summary>
        public static string UrlRechercheSteam(string texte, int tag, int plafondCts, bool promos, bool nouveautes, int debut)
        {
            var sb = new StringBuilder(BaseRechercheSteam);
            sb.Append(Math.Max(0, debut).ToString(CultureInfo.InvariantCulture));
            if (promos) sb.Append("&specials=1");
            if (nouveautes) sb.Append("&sort_by=Released_DESC");
            if (tag > 0) sb.Append("&tags=").Append(tag.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(texte)) sb.Append("&term=").Append(Uri.EscapeDataString(texte.Trim()));
            if (plafondCts == 100 || plafondCts == 500) sb.Append("&maxprice=5");
            else if (plafondCts == 1000) sb.Append("&maxprice=10");
            else if (plafondCts == 2000) sb.Append("&maxprice=20");
            return sb.ToString();
        }

        /// <summary>La recherche ne donne ni identifiant ni prix : l'identifiant se lit dans l'URL de la vignette.</summary>
        public static List<int> IdsRechercheSteam(string json)
        {
            var res = new List<int>();
            JsonDocument doc = Ouvre(json);
            if (doc == null) return res;
            using (doc)
            {
                JsonElement items;
                if (!Prop(doc.RootElement, "items", out items) || items.ValueKind != JsonValueKind.Array) return res;
                foreach (var it in items.EnumerateArray())
                {
                    string logo = Chaine(it, "logo");
                    if (logo == null) continue;
                    var m = AppDansLogo.Match(logo);
                    int id;
                    if (!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out id) || id <= 0) continue;
                    if (!res.Contains(id)) res.Add(id);
                }
            }
            return res;
        }

        public static string UrlFichesSteam(IEnumerable<int> appids)
        {
            if (appids == null) return null;
            var sb = new StringBuilder("{\"ids\":[");
            int n = 0;
            foreach (int id in appids)
            {
                if (id <= 0) continue;
                if (n == LotSteam) break;
                if (n > 0) sb.Append(',');
                sb.Append("{\"appid\":").Append(id.ToString(CultureInfo.InvariantCulture)).Append('}');
                n++;
            }
            if (n == 0) return null;
            sb.Append("],\"context\":{\"language\":\"french\",\"country_code\":\"FR\"},")
              .Append("\"data_request\":{\"include_basic_info\":true,\"include_tag_count\":5,\"include_release\":true,\"include_platforms\":true,\"include_assets\":true}}");
            return "https://api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json=" + Uri.EscapeDataString(sb.ToString());
        }

        public static List<Offre> FichesSteam(string json)
        {
            var res = new List<Offre>();
            JsonDocument doc = Ouvre(json);
            if (doc == null) return res;
            using (doc)
            {
                JsonElement r, items;
                if (!Prop(doc.RootElement, "response", out r) || !Prop(r, "store_items", out items) || items.ValueKind != JsonValueKind.Array)
                    return res;
                foreach (var it in items.EnumerateArray())
                {
                    long appid = Entier(it, "appid", 0);
                    string nom = Chaine(it, "name");
                    if (Entier(it, "success", 0) != 1 || appid <= 0 || appid > int.MaxValue || string.IsNullOrWhiteSpace(nom)) continue;
                    var o = new Offre
                    {
                        Source = "steam",
                        Id = appid.ToString(CultureInfo.InvariantCulture),
                        SteamAppId = (int)appid,
                        Nom = nom.Trim(),
                        Url = "https://store.steampowered.com/app/" + appid.ToString(CultureInfo.InvariantCulture) + "/",
                        Plateformes = new[] { "Steam" },
                        Tags = Entiers(it, "tagids"),
                        Image = ImageSteam(it),
                    };
                    JsonElement rel;
                    if (Prop(it, "release", out rel)) o.Sortie = DepuisUnix(Entier(rel, "steam_release_date", 0));

                    JsonElement gratuit;
                    if (Prop(it, "is_free", out gratuit) && gratuit.ValueKind == JsonValueKind.True)
                    {
                        o.PrixCts = 0; o.PrixOrigineCts = 0; o.Remise = 0;
                    }
                    else
                    {
                        JsonElement bpo;
                        bool a = Prop(it, "best_purchase_option", out bpo);
                        o.PrixCts = a ? (int)Borne(Entier(bpo, "final_price_in_cents", -1)) : -1;
                        o.PrixOrigineCts = a ? (int)Borne(Entier(bpo, "original_price_in_cents", o.PrixCts)) : o.PrixCts;
                        o.Remise = a && o.PrixCts >= 0 ? (int)Math.Max(0, Math.Min(100, Entier(bpo, "discount_pct", 0))) : 0;
                        JsonElement remises;
                        if (a && Prop(bpo, "active_discounts", out remises) && remises.ValueKind == JsonValueKind.Array && remises.GetArrayLength() > 0)
                            o.Fin = DepuisUnix(Entier(remises[0], "discount_end_date", 0));
                    }
                    res.Add(o);
                }
            }
            return res;
        }

        /// <summary>Seuls les jeux devenus gratuits par une promotion EN COURS (pas les « bientôt », pas les gratuits permanents).</summary>
        public static List<Offre> GratuitsEpic(string json, DateTime maintenantUtc)
        {
            var res = new List<Offre>();
            JsonDocument doc = Ouvre(json);
            if (doc == null) return res;
            using (doc)
            {
                JsonElement d, cat, ss, elements;
                if (!Prop(doc.RootElement, "data", out d) || !Prop(d, "Catalog", out cat) || !Prop(cat, "searchStore", out ss)
                    || !Prop(ss, "elements", out elements) || elements.ValueKind != JsonValueKind.Array)
                    return res;
                foreach (var el in elements.EnumerateArray())
                {
                    string titre = Chaine(el, "title");
                    if (string.IsNullOrWhiteSpace(titre)) continue;
                    JsonElement price, total;
                    if (!Prop(el, "price", out price) || !Prop(price, "totalPrice", out total)) continue;
                    long remisePrix = Entier(total, "discountPrice", -1), origine = Entier(total, "originalPrice", -1);
                    if (remisePrix != 0 || origine <= 0) continue;
                    DateTime? fin = PromoEnCours(el, maintenantUtc);
                    if (fin == null) continue;
                    string slug = PremierSlug(el);
                    res.Add(new Offre
                    {
                        Source = "epic",
                        Id = Chaine(el, "id") ?? NomCle(titre),
                        Nom = titre.Trim(),
                        PrixCts = 0,
                        PrixOrigineCts = (int)Borne(origine),
                        Remise = 100,
                        Fin = fin,
                        Plateformes = new[] { "Epic Games" },
                        Image = ImageEpic(el),
                        Url = slug != null ? "https://store.epicgames.com/fr/p/" + slug : "https://store.epicgames.com/fr/free-games",
                    });
                }
            }
            return res;
        }

        /// <summary>Sans jeu à offrir, GamerPower répond un OBJET (HTTP 201) au lieu d'un tableau.</summary>
        public static List<Offre> GratuitsGamerPower(string json)
        {
            var res = new List<Offre>();
            JsonDocument doc = Ouvre(json);
            if (doc == null) return res;
            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return res;
                foreach (var g in doc.RootElement.EnumerateArray())
                {
                    if (g.ValueKind != JsonValueKind.Object) continue;
                    string url = Chaine(g, "open_giveaway_url");
                    string nom = NomGamerPower(Chaine(g, "title"));
                    long id = Entier(g, "id", 0);
                    if (!Offres.LienSur(url) || nom.Length == 0 || id <= 0) continue;
                    res.Add(new Offre
                    {
                        Source = "gamerpower",
                        Id = id.ToString(CultureInfo.InvariantCulture),
                        Nom = nom,
                        PrixCts = 0,
                        Remise = 100,
                        Url = url.Trim(),
                        Sortie = DateGamerPower(Chaine(g, "published_date")),
                        Fin = DateGamerPower(Chaine(g, "end_date")),
                        Plateformes = PlateformesGamerPower(Chaine(g, "platforms")),
                        Image = ImageSure(Chaine(g, "thumbnail")) ?? ImageSure(Chaine(g, "image")),
                    });
                }
            }
            return res;
        }

        // ---------------------------------------------------------------- images

        private const string BaseImagesSteam = "https://shared.akamai.steamstatic.com/store_item_assets/";

        /// <summary>
        /// Jaquette Steam (library_capsule, portrait), sinon le bandeau, sinon la capsule. Les jeux récents
        /// ne sont plus servis à l'ancienne adresse /steam/apps/&lt;id&gt;/library_600x900.jpg : seul le chemin
        /// avec empreinte donné par l'API (asset_url_format + nom de fichier) existe.
        /// </summary>
        private static string ImageSteam(JsonElement item)
        {
            JsonElement a;
            if (!Prop(item, "assets", out a)) return null;
            string format = Chaine(a, "asset_url_format");
            if (format == null || !format.StartsWith("steam/", StringComparison.Ordinal) || !format.Contains("${FILENAME}")) return null;
            foreach (string cle in new[] { "library_capsule", "header", "main_capsule" })
            {
                string f = Chaine(a, cle);
                if (string.IsNullOrWhiteSpace(f) || f.Contains("..") || f.Contains("://")) continue;
                return ImageSure(BaseImagesSteam + format.Replace("${FILENAME}", f.Trim()));
            }
            return null;
        }

        /// <summary>Image Epic : verticale d'abord (comme la jaquette Steam), puis vignette, puis large.</summary>
        private static string ImageEpic(JsonElement el)
        {
            JsonElement imgs;
            if (!Prop(el, "keyImages", out imgs) || imgs.ValueKind != JsonValueKind.Array) return null;
            foreach (string type in new[] { "OfferImageTall", "DieselStoreFrontTall", "Thumbnail", "OfferImageWide", "DieselStoreFrontWide" })
                foreach (var k in imgs.EnumerateArray())
                    if (Chaine(k, "type") == type)
                    {
                        string u = ImageSure(Chaine(k, "url"));
                        // Le CDN d'Epic sert l'original (souvent 2 Mo) ; il redimensionne sur demande (≈ 30 Ko).
                        if (u != null && u.StartsWith("https://cdn1.epicgames.com/", StringComparison.Ordinal) && u.IndexOf('?') < 0)
                            u += "?h=360&w=240&resize=1";
                        if (u != null) return u;
                    }
            return null;
        }

        /// <summary>URL d'image acceptée : https seulement (jamais javascript:, file:, http en clair).</summary>
        private static string ImageSure(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            Uri u;
            return Uri.TryCreate(url.Trim(), UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps ? url.Trim() : null;
        }

        // ---------------------------------------------------------------- aides

        private static JsonDocument Ouvre(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonDocument.Parse(json); }
            catch (JsonException) { return null; }
        }

        private static bool Prop(JsonElement e, string nom, out JsonElement v)
        {
            v = default(JsonElement);
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nom, out v) && v.ValueKind != JsonValueKind.Null;
        }

        private static string Chaine(JsonElement e, string nom)
        {
            JsonElement v;
            if (!Prop(e, nom, out v)) return null;
            if (v.ValueKind == JsonValueKind.String) return v.GetString();
            if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
            return null;
        }

        /// <summary>Entier lu en nombre ou en chaîne (Steam donne les prix en chaînes) ; illisible → défaut.</summary>
        private static long Entier(JsonElement e, string nom, long defaut)
        {
            JsonElement v;
            if (!Prop(e, nom, out v)) return defaut;
            long n;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out n)) return n;
            if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return defaut;
        }

        private static int[] Entiers(JsonElement e, string nom)
        {
            JsonElement v;
            if (!Prop(e, nom, out v) || v.ValueKind != JsonValueKind.Array) return new int[0];
            var l = new List<int>();
            foreach (var x in v.EnumerateArray())
            {
                int n;
                if (x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out n)) l.Add(n);
            }
            return l.ToArray();
        }

        private static long Borne(long cts) { return cts < -1 ? -1 : Math.Min(cts, int.MaxValue); }

        private static DateTime? DepuisUnix(long s)
        {
            if (s <= 0 || s > 253402300799L) return null;
            return DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime;
        }

        private static DateTime? IsoUtc(string s)
        {
            DateTimeOffset d;
            if (s != null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d)) return d.UtcDateTime;
            return null;
        }

        /// <summary>Fin de la promotion en cours (startDate ≤ maintenant &lt; endDate), sinon null.</summary>
        private static DateTime? PromoEnCours(JsonElement el, DateTime maintenantUtc)
        {
            JsonElement promos, offres;
            if (!Prop(el, "promotions", out promos) || !Prop(promos, "promotionalOffers", out offres) || offres.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var groupe in offres.EnumerateArray())
            {
                JsonElement liste;
                if (!Prop(groupe, "promotionalOffers", out liste) || liste.ValueKind != JsonValueKind.Array) continue;
                foreach (var p in liste.EnumerateArray())
                {
                    DateTime? debut = IsoUtc(Chaine(p, "startDate")), fin = IsoUtc(Chaine(p, "endDate"));
                    if (debut != null && fin != null && debut.Value <= maintenantUtc && maintenantUtc < fin.Value) return fin;
                }
            }
            return null;
        }

        private static string PremierSlug(JsonElement el)
        {
            JsonElement ns, maps;
            if (Prop(el, "catalogNs", out ns) && Prop(ns, "mappings", out maps))
            {
                string s = SlugDe(maps);
                if (s != null) return s;
            }
            if (Prop(el, "offerMappings", out maps))
            {
                string s = SlugDe(maps);
                if (s != null) return s;
            }
            return SlugPropre(Chaine(el, "productSlug"));
        }

        private static string SlugDe(JsonElement maps)
        {
            if (maps.ValueKind != JsonValueKind.Array || maps.GetArrayLength() == 0) return null;
            return SlugPropre(Chaine(maps[0], "pageSlug"));
        }

        /// <summary>Un slug ne sert qu'à composer un chemin : lettres, chiffres, tirets et barres seulement.</summary>
        private static string SlugPropre(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            foreach (char c in s)
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '/')) return null;
            return s;
        }

        private static string NomCle(string titre) { return Offres.NomNormalise(titre); }

        /// <summary>« Mark of the Deep (Epic Games) Giveaway » → « Mark of the Deep ».</summary>
        private static string NomGamerPower(string titre)
        {
            if (string.IsNullOrWhiteSpace(titre)) return "";
            string s = titre.Trim();
            if (s.EndsWith("Giveaway", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - "Giveaway".Length).TrimEnd();
            if (s.EndsWith(")"))
            {
                int i = s.LastIndexOf('(');
                if (i > 0) s = s.Substring(0, i).TrimEnd();
            }
            return s;
        }

        private static DateTime? DateGamerPower(string s)
        {
            DateTime d;
            if (s != null && DateTime.TryParseExact(s.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
            return null;
        }

        private static string[] PlateformesGamerPower(string s)
        {
            var res = new List<string>();
            if (string.IsNullOrWhiteSpace(s)) return res.ToArray();
            foreach (string brut in s.Split(','))
            {
                string v = brut.Trim();
                if (v.Length == 0) continue;
                string b = v.ToLowerInvariant();
                string lib = b.Contains("playstation") ? "PlayStation"
                    : b.Contains("xbox") ? "Xbox"
                    : b.Contains("switch") ? "Switch"
                    : b.Contains("epic") ? "Epic Games"
                    : b.Contains("steam") ? "Steam"
                    : b.Contains("gog") ? "GOG"
                    : b.Contains("android") ? "Android"
                    : b.Contains("ios") ? "iOS"
                    : b == "pc" ? "PC"
                    : v;
                if (!res.Contains(lib)) res.Add(lib);
            }
            return res.ToArray();
        }
    }
}
