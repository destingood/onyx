using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

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
                        Image = ImageSure(Chaine(g, "image")) ?? ImageSure(Chaine(g, "thumbnail")),
                    });
                }
            }
            return res;
        }

        // ---------------------------------------------------------------- images

        private const string BaseImagesSteam = "https://shared.akamai.steamstatic.com/store_item_assets/";

        /// <summary>
        /// Visuel Steam horizontal (capsule, puis bandeau), portrait seulement en repli. Les jeux récents
        /// ne sont plus servis à l'ancienne adresse /steam/apps/&lt;id&gt;/library_600x900.jpg : seul le chemin
        /// avec empreinte donné par l'API (asset_url_format + nom de fichier) existe.
        /// </summary>
        private static string ImageSteam(JsonElement item)
        {
            JsonElement a;
            if (!Prop(item, "assets", out a)) return null;
            string format = Chaine(a, "asset_url_format");
            if (format == null || !format.StartsWith("steam/", StringComparison.Ordinal) || !format.Contains("${FILENAME}")) return null;
            foreach (string cle in new[] { "main_capsule", "header", "library_capsule" })
            {
                string f = Chaine(a, cle);
                if (string.IsNullOrWhiteSpace(f) || f.Contains("..") || f.Contains("://")) continue;
                return ImageSure(BaseImagesSteam + format.Replace("${FILENAME}", f.Trim()));
            }
            return null;
        }

        /// <summary>Image Epic : horizontale en priorité, sans la réduction en vignette de 240 pixels.</summary>
        private static string ImageEpic(JsonElement el)
        {
            JsonElement imgs;
            if (!Prop(el, "keyImages", out imgs) || imgs.ValueKind != JsonValueKind.Array) return null;
            foreach (string type in new[] { "OfferImageWide", "DieselStoreFrontWide", "OfferImageTall", "DieselStoreFrontTall", "Thumbnail" })
                foreach (var k in imgs.EnumerateArray())
                    if (Chaine(k, "type") == type)
                    {
                        string u = ImageSure(Chaine(k, "url"));
                        // Le CDN fournit une largeur adaptée aux grandes cartes et aux écrans à haute densité.
                        if (u != null && u.StartsWith("https://cdn1.epicgames.com/", StringComparison.Ordinal) && u.IndexOf('?') < 0)
                            u += "?w=1280&resize=1";
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
                    : b.Contains("itch") ? "itch.io"
                    : b.Contains("ubisoft") ? "Ubisoft"
                    : b.Contains("origin") || b == "ea" || b.StartsWith("ea ", StringComparison.Ordinal) ? "EA"
                    : b.Contains("battle.net") ? "Battle.net"
                    : b.Contains("android") ? "Android"
                    : b.Contains("ios") ? "iOS"
                    : b == "pc" ? "PC"
                    : v;
                if (!res.Contains(lib)) res.Add(lib);
            }
            return res.ToArray();
        }
    

        // ---------------------------------------------------------------- stores

        /// <summary>Un store du menu « Stores ». CheapShark : son storeID, 0 s'il est lu à la source.</summary>
        public sealed class Boutique
        {
            public readonly string Cle, Nom;
            public readonly int CheapShark;
            public Boutique(string cle, string nom, int cheapShark) { Cle = cle; Nom = nom; CheapShark = cheapShark; }
        }

        /// <summary>
        /// Steam, GOG et Nintendo sont lus à la source (prix en euros) ; les autres passent par CheapShark
        /// (prix US en dollars), itch.io par son flux RSS. Microsoft Store et PlayStation Store n'offrent
        /// pas d'accès exploitable sans clé (relevé le 21/11/2026) ; leurs jeux gratuits viennent de GamerPower.
        /// </summary>
        public static readonly Boutique[] Boutiques =
        {
            new Boutique("steam", "Steam", 0),
            new Boutique("gog", "GOG", 0),
            new Boutique("nintendo", "Nintendo eShop", 0),
            new Boutique("epic", "Epic Games Store", 25),
            new Boutique("humble", "Humble Store", 11),
            new Boutique("fanatical", "Fanatical", 15),
            new Boutique("gmg", "Green Man Gaming", 3),
            new Boutique("ubisoft", "Ubisoft Store", 13),
            new Boutique("gamesplanet", "Gamesplanet", 27),
            new Boutique("gamersgate", "GamersGate", 2),
            new Boutique("indiegala", "IndieGala", 30),
            new Boutique("gamebillet", "GameBillet", 23),
            new Boutique("wingamestore", "WinGameStore", 21),
            new Boutique("dreamgame", "DreamGame", 35),
            new Boutique("itch", "itch.io", 0),
        };

        /// <summary>Nom affiché d'une source ; inconnue → la clé telle quelle.</summary>
        public static string NomBoutique(string cle)
        {
            if (cle == null) return "";
            foreach (Boutique b in Boutiques) if (b.Cle == cle) return b.Nom;
            return cle == "gamerpower" ? "GamerPower" : cle;
        }

        private const string EnteteChoix = "v1";

        /// <summary>Stores cochés. Fichier absent ou d'un autre format : tous.</summary>
        public static HashSet<string> LitChoixBoutiques(string texte)
        {
            var res = new HashSet<string>();
            string[] lignes = (texte ?? "").Replace("\r", "").Split('\n');
            if (lignes.Length == 0 || lignes[0].Trim() != EnteteChoix)
            {
                foreach (Boutique b in Boutiques) res.Add(b.Cle);
                return res;
            }
            for (int i = 1; i < lignes.Length; i++)
            {
                string c = lignes[i].Trim();
                foreach (Boutique b in Boutiques) if (b.Cle == c) res.Add(c);
            }
            return res;
        }

        public static string EcritChoixBoutiques(HashSet<string> choix)
        {
            var sb = new StringBuilder(EnteteChoix + "\n");
            foreach (Boutique b in Boutiques) if (choix != null && choix.Contains(b.Cle)) sb.Append(b.Cle).Append('\n');
            return sb.ToString();
        }

        /// <summary>storeID CheapShark des stores cochés, triés (« 11,15,25 ») ; aucun → "".</summary>
        public static string IdsCheapShark(HashSet<string> choix)
        {
            var ids = new List<int>();
            foreach (Boutique b in Boutiques) if (b.CheapShark > 0 && choix != null && choix.Contains(b.Cle)) ids.Add(b.CheapShark);
            ids.Sort();
            return string.Join(",", ids.ConvertAll(i => i.ToString(CultureInfo.InvariantCulture)));
        }

        // ---------------------------------------------------------------- GOG (euros)

        public const int ParPageGog = 48;

        public static string UrlGog(string texte, int page)
        {
            string url = "https://catalog.gog.com/v1/catalog?limit=" + ParPageGog.ToString(CultureInfo.InvariantCulture)
                + "&order=desc:trending&discounted=eq:true&productType=in:game,pack&page=" + Math.Max(1, page).ToString(CultureInfo.InvariantCulture)
                + "&countryCode=FR&locale=fr-FR&currencyCode=EUR";
            if (!string.IsNullOrWhiteSpace(texte)) url += "&query=like:" + Uri.EscapeDataString(texte.Trim());
            return url;
        }

        public static List<Offre> PromosGog(string json)
        {
            var res = new List<Offre>();
            JsonDocument doc = Ouvre(json);
            if (doc == null) return res;
            using (doc)
            {
                JsonElement produits;
                if (!Prop(doc.RootElement, "products", out produits) || produits.ValueKind != JsonValueKind.Array) return res;
                foreach (var p in produits.EnumerateArray())
                {
                    JsonElement prix, fin, base_;
                    string id = Chaine(p, "id"), nom = Chaine(p, "title"), url = Chaine(p, "storeLink");
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(nom) || !Offres.LienSur(url)) continue;
                    if (!Prop(p, "price", out prix) || !Prop(prix, "finalMoney", out fin) || !Prop(prix, "baseMoney", out base_)) continue;
                    string devise = Chaine(fin, "currency");
                    if (devise != null && devise != "EUR") continue;
                    int cts = Montant(Chaine(fin, "amount")), origine = Montant(Chaine(base_, "amount"));
                    if (cts < 0) continue;
                    int remise = Pourcent(Chaine(prix, "discount"));
                    if (remise < 0) remise = RemiseDe(cts, origine);
                    DateTime d;
                    string sortie = Chaine(p, "releaseDate");
                    res.Add(new Offre
                    {
                        Source = "gog", Id = id.Trim(), Nom = nom.Trim(), PrixCts = cts, PrixOrigineCts = origine, Remise = remise,
                        Url = url.Trim(), Plateformes = new[] { "GOG" },
                        Image = ImageSure(Chaine(p, "coverHorizontal")) ?? ImageSure(Chaine(p, "coverVertical")),
                        Sortie = sortie != null && DateTime.TryParseExact(sortie, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d) ? d : (DateTime?)null,
                    });
                }
            }
            return res;
        }

        // ---------------------------------------------------------------- CheapShark (dollars)

        public const int ParPageCheapShark = 60;
        private static readonly Regex DealSur = new Regex(@"^[A-Za-z0-9%._-]{1,200}$", RegexOptions.CultureInvariant);

        public static string UrlCheapShark(string idsStores, string texte, int page)
        {
            string url = "https://www.cheapshark.com/api/1.0/deals?storeID=" + idsStores + "&onSale=1&pageSize=" + ParPageCheapShark.ToString(CultureInfo.InvariantCulture)
                + "&pageNumber=" + Math.Max(0, page).ToString(CultureInfo.InvariantCulture) + "&sortBy=Deal%20Rating";
            if (!string.IsNullOrWhiteSpace(texte)) url += "&title=" + Uri.EscapeDataString(texte.Trim());
            return url;
        }

        public static List<Offre> PromosCheapShark(string json)
        {
            var res = new List<Offre>();
            var place = new Dictionary<long, int>(); // gameID -> rang dans res : un seul deal par jeu, le moins cher
            JsonDocument doc = Ouvre(json);
            if (doc == null) return res;
            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return res;
                foreach (var g in doc.RootElement.EnumerateArray())
                {
                    long store = Entier(g, "storeID", 0), jeu = Entier(g, "gameID", 0);
                    Boutique b = Array.Find(Boutiques, x => x.CheapShark > 0 && x.CheapShark == store);
                    string nom = Chaine(g, "title"), deal = Chaine(g, "dealID");
                    if (b == null || jeu <= 0 || string.IsNullOrWhiteSpace(nom) || deal == null || !DealSur.IsMatch(deal)) continue;
                    int cts = Montant(Chaine(g, "salePrice")), origine = Montant(Chaine(g, "normalPrice"));
                    if (cts < 0) continue;
                    decimal eco;
                    int remise = decimal.TryParse(Chaine(g, "savings"), NumberStyles.Number, CultureInfo.InvariantCulture, out eco)
                        ? (int)Math.Max(0, Math.Min(100, Math.Round(eco))) : RemiseDe(cts, origine);
                    long appId = Entier(g, "steamAppID", 0);
                    string image = null;
                    // La vignette d'un jeu Steam est un bandeau large : la jaquette par AppID rend mieux.
                    if (appId <= 0 || appId > int.MaxValue)
                    {
                        appId = 0;
                        image = ImageSure(Chaine(g, "thumb"));
                        if (image != null && image.StartsWith("https://cdn1.epicgames.com/", StringComparison.Ordinal) && image.IndexOf('?') < 0)
                            image += "?w=1280&resize=1";
                    }
                    int deja;
                    bool vu = place.TryGetValue(jeu, out deja);
                    if (vu && res[deja].PrixCts <= cts) continue;
                    var o = new Offre
                    {
                        Source = b.Cle, Id = jeu.ToString(CultureInfo.InvariantCulture), Nom = nom.Trim(), PrixCts = cts, PrixOrigineCts = origine, Remise = remise,
                        Devise = "USD", Url = "https://www.cheapshark.com/redirect?dealID=" + deal, SteamAppId = (int)appId, Image = image,
                        Plateformes = new[] { b.Nom }, Sortie = DepuisUnix(Entier(g, "releaseDate", 0)),
                    };
                    if (vu) res[deja] = o;
                    else { place[jeu] = res.Count; res.Add(o); }
                }
            }
            return res;
        }

        // ---------------------------------------------------------------- comparateur

        /// <summary>Prix d'un jeu dans un store officiel (CheapShark, dollars US).</summary>
        public sealed class PrixCompare { public string Store, Url; public int PrixCts, PrixOrigineCts, Remise; public long Jeu; }

        /// <summary>Tous les stores d'un jeu, du moins cher au plus cher : par AppID Steam, sinon par titre exact.</summary>
        public static string UrlComparaison(int steamAppId, string nom)
        {
            const string fin = "&pageSize=30&sortBy=Price";
            if (steamAppId > 0) return "https://www.cheapshark.com/api/1.0/deals?steamAppID=" + steamAppId.ToString(CultureInfo.InvariantCulture) + fin;
            if (string.IsNullOrWhiteSpace(nom)) return null;
            return "https://www.cheapshark.com/api/1.0/deals?title=" + Uri.EscapeDataString(nom.Trim()) + "&exact=1" + fin;
        }

        /// <summary>Nom d'un store CheapShark : ceux du menu, plus Steam (1) et GOG (7) lus ailleurs à la source.</summary>
        private static string StoreCheapShark(long id)
        {
            if (id == 1) return "Steam";
            if (id == 7) return "GOG";
            Boutique b = Array.Find(Boutiques, x => x.CheapShark > 0 && x.CheapShark == id);
            return b == null ? null : b.Nom;
        }

        /// <summary>Un prix par store connu (le moins cher), triés par prix puis par nom.</summary>
        public static List<PrixCompare> PrixCompares(string json)
        {
            var parStore = new Dictionary<string, PrixCompare>();
            JsonDocument doc = Ouvre(json);
            if (doc == null) return new List<PrixCompare>();
            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return new List<PrixCompare>();
                foreach (var g in doc.RootElement.EnumerateArray())
                {
                    string store = StoreCheapShark(Entier(g, "storeID", 0)), deal = Chaine(g, "dealID");
                    if (store == null || deal == null || !DealSur.IsMatch(deal)) continue;
                    int cts = Montant(Chaine(g, "salePrice")), origine = Montant(Chaine(g, "normalPrice"));
                    if (cts < 0) continue;
                    PrixCompare deja;
                    if (parStore.TryGetValue(store, out deja) && deja.PrixCts <= cts) continue;
                    decimal eco;
                    int remise = decimal.TryParse(Chaine(g, "savings"), NumberStyles.Number, CultureInfo.InvariantCulture, out eco)
                        ? (int)Math.Max(0, Math.Min(100, Math.Round(eco))) : RemiseDe(cts, origine);
                    parStore[store] = new PrixCompare { Store = store, PrixCts = cts, PrixOrigineCts = origine, Remise = remise, Jeu = Entier(g, "gameID", 0), Url = "https://www.cheapshark.com/redirect?dealID=" + deal };
                }
            }
            var res = new List<PrixCompare>(parStore.Values);
            res.Sort((a, b) => a.PrixCts != b.PrixCts ? a.PrixCts.CompareTo(b.PrixCts) : string.CompareOrdinal(a.Store, b.Store));
            return res;
        }

        /// <summary>Plus bas prix jamais relevé par CheapShark (dollars US) ; -1 si inconnu.</summary>
        public sealed class HistoriquePrix { public int PlusBasCts = -1; public DateTime? PlusBasLe; }

        public static string UrlJeuCheapShark(long jeu)
        {
            return jeu > 0 ? "https://www.cheapshark.com/api/1.0/games?id=" + jeu.ToString(CultureInfo.InvariantCulture) : null;
        }

        public static HistoriquePrix Historique(string json)
        {
            var h = new HistoriquePrix();
            JsonDocument doc = Ouvre(json);
            if (doc == null) return h;
            using (doc)
            {
                JsonElement bas;
                if (doc.RootElement.ValueKind != JsonValueKind.Object || !Prop(doc.RootElement, "cheapestPriceEver", out bas) || bas.ValueKind != JsonValueKind.Object) return h;
                h.PlusBasCts = Montant(Chaine(bas, "price"));
                if (h.PlusBasCts >= 0) h.PlusBasLe = DepuisUnix(Entier(bas, "date", 0));
            }
            return h;
        }

        /// <summary>Taux de la BCE : dollars pour 1 euro (0 si absent ou invalide), et le jour de la cotation.</summary>
        public static decimal TauxUsd(string xml, out DateTime jour)
        {
            jour = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(xml)) return 0m;
            try
            {
                var reglages = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                decimal taux = 0m;
                using (var sr = new StringReader(xml))
                using (var r = XmlReader.Create(sr, reglages))
                {
                    while (r.Read())
                    {
                        if (r.NodeType != XmlNodeType.Element || r.LocalName != "Cube") continue;
                        DateTime d;
                        string t = r.GetAttribute("time");
                        if (t != null && DateTime.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) jour = d;
                        decimal v;
                        if (r.GetAttribute("currency") == "USD" && decimal.TryParse(r.GetAttribute("rate"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out v)
                            && v > 0.1m && v < 10m) taux = v;
                    }
                }
                return taux;
            }
            catch { jour = DateTime.MinValue; return 0m; }
        }

        /// <summary>Centimes de dollar → centimes d'euro au taux de la BCE ; -1 si impossible.</summary>
        public static int EnEuros(int centimesUsd, decimal tauxUsd)
        {
            if (centimesUsd < 0 || tauxUsd <= 0m) return -1;
            return (int)Math.Round(centimesUsd / tauxUsd, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Acheter ou attendre, d'après le plus bas historique. niveau : 2 au plus bas, 1 proche (moins de 10 % au-dessus), 0 attendre.
        /// </summary>
        public static string Verdict(int meilleurCts, int plusBasCts, out int niveau)
        {
            niveau = 0;
            if (meilleurCts < 0 || plusBasCts < 0) return null;
            if (meilleurCts <= plusBasCts) { niveau = 2; return "Prix le plus bas jamais vu"; }
            int ecart = (int)Math.Round((meilleurCts - plusBasCts) * 100m / Math.Max(1, plusBasCts));
            if (ecart < 10) { niveau = 1; return "Proche du plus bas historique (+" + Math.Max(1, ecart).ToString(CultureInfo.InvariantCulture) + " %)"; }
            int moins = (int)Math.Round((meilleurCts - plusBasCts) * 100m / meilleurCts);
            return "Déjà vu " + moins.ToString(CultureInfo.InvariantCulture) + " % moins cher : une promo reviendra sans doute";
        }

        /// <summary>Une ligne du comparatif : prix d'un store, sa valeur en euros (-1 si inconnue), et si c'est l'offre de la carte.</summary>
        public sealed class Ligne { public string Store, Devise, Url; public int Cts, OrigineCts, Remise, EurCts; public bool DeLaCarte; }

        /// <summary>
        /// Prix des stores (dollars, convertis au taux BCE) et prix de la carte s'il est en euros (« Steam (France) »),
        /// triés en euros ; sans taux, les prix en euros d'abord, puis les dollars entre eux.
        /// </summary>
        public static List<Ligne> Lignes(Offre carte, List<PrixCompare> prix, decimal tauxUsd)
        {
            var res = new List<Ligne>();
            string storeCarte = carte == null ? null : NomBoutique(carte.Source);
            if (prix != null)
                foreach (PrixCompare p in prix)
                    res.Add(new Ligne { Store = p.Store, Devise = "USD", Url = p.Url, Cts = p.PrixCts, OrigineCts = p.PrixOrigineCts, Remise = p.Remise, EurCts = EnEuros(p.PrixCts, tauxUsd),
                        DeLaCarte = carte != null && carte.Devise == "USD" && (p.Url == carte.Url || p.Store == storeCarte) });
            if (carte != null && carte.Devise == "EUR" && carte.PrixCts >= 0)
                res.Add(new Ligne { Store = storeCarte + " (France)", Devise = "EUR", Url = carte.Url, Cts = carte.PrixCts, OrigineCts = carte.PrixOrigineCts, Remise = carte.Remise, EurCts = carte.PrixCts, DeLaCarte = true });
            res.Sort((a, b) =>
            {
                if ((a.EurCts >= 0) != (b.EurCts >= 0)) return a.EurCts >= 0 ? -1 : 1;
                int c = a.EurCts >= 0 ? a.EurCts.CompareTo(b.EurCts) : a.Cts.CompareTo(b.Cts);
                return c != 0 ? c : string.CompareOrdinal(a.Store, b.Store);
            });
            return res;
        }

        /// <summary>Revendeur de clés : lien de recherche seulement ({0} = nom échappé). Place de marché = vendeurs tiers.</summary>
        public sealed class Revendeur
        {
            public readonly string Nom, Modele;
            public readonly bool PlaceDeMarche, EspacePlus;
            public Revendeur(string nom, string modele, bool placeDeMarche, bool espacePlus = false) { Nom = nom; Modele = modele; PlaceDeMarche = placeDeMarche; EspacePlus = espacePlus; }
        }

        /// <summary>Adresses de recherche vérifiées le 21/11/2026. gg.deals écarté : défi Cloudflare.</summary>
        public static readonly Revendeur[] Revendeurs =
        {
            new Revendeur("Instant Gaming", "https://www.instant-gaming.com/fr/rechercher/?q={0}", false),
            new Revendeur("G2A", "https://www.g2a.com/fr/search?query={0}", true),
            new Revendeur("Eneba", "https://www.eneba.com/fr/store/all?text={0}", true),
            new Revendeur("Kinguin", "https://www.kinguin.net/listing?phrase={0}", true),
            new Revendeur("Gamivo", "https://www.gamivo.com/search/{0}", true),
            new Revendeur("AllKeyShop", "https://www.allkeyshop.com/blog/catalogue/search-{0}/", false, true),
        };

        /// <summary>Historique complet des prix (sites tiers, ouverts dans le navigateur) : IsThereAnyDeal par nom, SteamDB par AppID.</summary>
        public static readonly Revendeur Itad = new Revendeur("IsThereAnyDeal", "https://isthereanydeal.com/search/?q={0}", false);

        public static string UrlSteamDb(int steamAppId)
        {
            return steamAppId > 0 ? "https://steamdb.info/app/" + steamAppId.ToString(CultureInfo.InvariantCulture) + "/" : null;
        }

        /// <summary>
        /// Lien de recherche du jeu chez un revendeur. Le nom ne garde que lettres, chiffres, apostrophes, tirets
        /// et points collés à un mot (™, ®, « : », / … deviennent des espaces) ; vide → null.
        /// </summary>
        public static string UrlRevendeur(Revendeur r, string nom)
        {
            if (r == null || nom == null) return null;
            var sb = new StringBuilder();
            char avant = ' ';
            foreach (char c0 in nom)
            {
                char c = c0 == '’' ? '\'' : c0;
                bool garde = char.IsLetterOrDigit(c) || ((c == '\'' || c == '-' || c == '.') && char.IsLetterOrDigit(avant));
                sb.Append(garde ? c : ' ');
                avant = garde ? c : ' ';
            }
            string[] mots = sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (mots.Length == 0) return null;
            string q = string.Join(r.EspacePlus ? "+" : "%20", Array.ConvertAll(mots, Uri.EscapeDataString));
            return string.Format(CultureInfo.InvariantCulture, r.Modele, q);
        }

        // ---------------------------------------------------------------- Nintendo eShop (euros)

        public const int ParPageNintendo = 48;

        /// <summary>Recherche Nintendo (Solr) : seuls lettres, chiffres et espaces passent dans la requête.</summary>
        public static string UrlNintendo(string texte, int debut)
        {
            var mots = new List<string>();
            var sb = new StringBuilder();
            foreach (char c in (texte ?? "") + " ")
            {
                if (char.IsLetterOrDigit(c)) { sb.Append(c); continue; }
                if (sb.Length > 0) { mots.Add(Uri.EscapeDataString(sb.ToString())); sb.Clear(); }
            }
            string q = mots.Count == 0 ? "*" : string.Join("%20", mots);
            return "https://searching.nintendo-europe.com/fr/select?q=" + q + "&fq=type:GAME%20AND%20price_has_discount_b:true&rows="
                + ParPageNintendo.ToString(CultureInfo.InvariantCulture) + "&start=" + Math.Max(0, debut).ToString(CultureInfo.InvariantCulture)
                + (mots.Count == 0 ? "&sort=change_date%20desc" : "") + "&wt=json";
        }

        public static List<Offre> PromosNintendo(string json)
        {
            var res = new List<Offre>();
            JsonDocument doc = Ouvre(json);
            if (doc == null) return res;
            using (doc)
            {
                JsonElement rep, docs;
                if (!Prop(doc.RootElement, "response", out rep) || !Prop(rep, "docs", out docs) || docs.ValueKind != JsonValueKind.Array) return res;
                foreach (var d in docs.EnumerateArray())
                {
                    string nom = Chaine(d, "title"), chemin = Chaine(d, "url");
                    if (string.IsNullOrWhiteSpace(nom) || chemin == null || !chemin.StartsWith("/", StringComparison.Ordinal) || chemin.StartsWith("//", StringComparison.Ordinal)) continue;
                    string url = "https://www.nintendo.com" + chemin.Trim();
                    if (!Offres.LienSur(url)) continue;
                    int cts = Montant(Chaine(d, "price_discounted_f")), origine = Montant(Chaine(d, "price_regular_f"));
                    if (cts < 0) continue;
                    string nsuid = PremiereChaine(d, "nsuid_txt"), fs = Chaine(d, "fs_id");
                    string id = ChiffresSeuls(nsuid) ? nsuid : ChiffresSeuls(fs) ? "fs" + fs : null;
                    if (id == null) continue;
                    int remise = Pourcent(Chaine(d, "price_discount_percentage_f"));
                    res.Add(new Offre
                    {
                        Source = "nintendo", Id = id, Nom = nom.Trim(), PrixCts = cts, PrixOrigineCts = origine, Remise = remise >= 0 ? remise : RemiseDe(cts, origine),
                        Url = url, Plateformes = new[] { "Switch" },
                        Image = ImageSure(Chaine(d, "image_url_h2x1_s")) ?? ImageSure(Chaine(d, "image_url_sq_s")),
                        Sortie = IsoUtc(PremiereChaine(d, "dates_released_dts")),
                    });
                }
            }
            return res;
        }

        /// <summary>L'index de recherche peut avoir du retard : prix réels des offres ayant un nsuid (50 max).</summary>
        public static string UrlPrixNintendo(List<Offre> offres)
        {
            var ids = new List<string>();
            foreach (Offre o in offres ?? new List<Offre>())
                if (o != null && o.Source == "nintendo" && ChiffresSeuls(o.Id) && !ids.Contains(o.Id) && ids.Count < 50) ids.Add(o.Id);
            return ids.Count == 0 ? null : "https://api.ec.nintendo.com/v1/price?country=FR&lang=fr&ids=" + string.Join(",", ids);
        }

        /// <summary>
        /// Copies des offres avec le prix réel ; une offre dont la promo est finie est retirée.
        /// Réponse illisible ou offre absente de la réponse : prix de l'index gardé.
        /// </summary>
        public static List<Offre> AppliquePrixNintendo(List<Offre> offres, string json)
        {
            var res = new List<Offre>();
            var prix = new Dictionary<string, JsonElement>();
            JsonDocument doc = Ouvre(json);
            try
            {
                JsonElement liste;
                if (doc != null && Prop(doc.RootElement, "prices", out liste) && liste.ValueKind == JsonValueKind.Array)
                    foreach (var p in liste.EnumerateArray())
                    {
                        string id = Chaine(p, "title_id");
                        if (id != null) prix[id] = p;
                    }
                foreach (Offre o in offres ?? new List<Offre>())
                {
                    if (o == null) continue;
                    Offre c = o.Copie();
                    JsonElement p, remise, normal;
                    if (!prix.TryGetValue(o.Id ?? "", out p)) { res.Add(c); continue; }
                    if (Chaine(p, "sales_status") != "onsale" || !Prop(p, "discount_price", out remise)) continue;
                    int cts = Montant(Chaine(remise, "raw_value"));
                    if (cts < 0) continue;
                    c.PrixCts = cts;
                    if (Prop(p, "regular_price", out normal) && Montant(Chaine(normal, "raw_value")) > 0) c.PrixOrigineCts = Montant(Chaine(normal, "raw_value"));
                    c.Remise = RemiseDe(c.PrixCts, c.PrixOrigineCts);
                    c.Fin = IsoUtc(Chaine(remise, "end_datetime"));
                    res.Add(c);
                }
            }
            finally { if (doc != null) doc.Dispose(); }
            return res;
        }

        // ---------------------------------------------------------------- itch.io (flux RSS)

        public const string UrlItch = "https://itch.io/games/on-sale.xml";

        public static List<Offre> PromosItch(string xml)
        {
            var res = new List<Offre>();
            if (string.IsNullOrWhiteSpace(xml)) return res;
            var doc = new XmlDocument { XmlResolver = null };
            try
            {
                var reglages = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                using (var sr = new StringReader(xml))
                using (var r = XmlReader.Create(sr, reglages))
                    doc.Load(r);
            }
            catch { return res; }
            XmlNodeList items = doc.SelectNodes("/rss/channel/item");
            if (items == null) return res;
            foreach (XmlNode it in items)
            {
                string lien = Texte(it, "link"), devise = Texte(it, "currency");
                string nom = Texte(it, "plainTitle");
                if (string.IsNullOrEmpty(nom))
                {
                    nom = Texte(it, "title") ?? "";
                    int i = nom.IndexOf(" [", StringComparison.Ordinal);
                    if (i > 0) nom = nom.Substring(0, i);
                }
                if (string.IsNullOrWhiteSpace(nom) || lien == null || !lien.StartsWith("https://", StringComparison.Ordinal) || !Offres.LienSur(lien)) continue;
                if (devise != "USD" && devise != "EUR") continue;
                int cts = Montant(Texte(it, "price")), origine = Montant(Texte(it, "fullPrice"));
                if (cts < 0) continue;
                int remise;
                if (!int.TryParse(Texte(it, "discountpercent"), NumberStyles.Integer, CultureInfo.InvariantCulture, out remise) || remise < 0 || remise > 100)
                    remise = RemiseDe(cts, origine);
                DateTimeOffset fin;
                string sFin = Texte(it, "saleends");
                res.Add(new Offre
                {
                    Source = "itch", Id = lien.Substring("https://".Length).TrimEnd('/'), Nom = nom.Trim(), PrixCts = cts, PrixOrigineCts = origine, Remise = remise,
                    Devise = devise, Url = lien, Image = ImageSure(Texte(it, "imageurl")), Plateformes = new[] { "itch.io" },
                    Fin = sFin != null && DateTimeOffset.TryParse(sFin, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out fin) ? fin.UtcDateTime : (DateTime?)null,
                });
            }
            return res;
        }

        // ---------------------------------------------------------------- aides des stores

        private static string Texte(XmlNode n, string nom)
        {
            XmlNode e = n.SelectSingleNode(nom);
            if (e == null) return null;
            string t = e.InnerText.Trim();
            return t.Length == 0 ? null : t;
        }

        /// <summary>« 22.19 », « $1.98 », « €2,50 », 9.59 → centimes ; illisible → -1.</summary>
        private static int Montant(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return -1;
            var sb = new StringBuilder();
            foreach (char c in s) if (char.IsDigit(c) || c == '.' || c == ',') sb.Append(c);
            string t = sb.ToString();
            if (t.IndexOf('.') >= 0) t = t.Replace(",", "");
            else t = t.Replace(',', '.');
            decimal d;
            if (t.Length == 0 || !decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out d) || d > 1000000m) return -1;
            return (int)Math.Round(d * 100m);
        }

        /// <summary>« -40% », « 20 », 20.0 → 40, 20 ; illisible → -1.</summary>
        private static int Pourcent(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return -1;
            decimal d;
            string t = s.Replace("-", "").Replace("%", "").Trim();
            if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out d) || d < 0 || d > 100) return -1;
            return (int)Math.Round(d);
        }

        private static int RemiseDe(int cts, int origine)
        {
            if (cts < 0 || origine <= 0 || cts >= origine) return 0;
            return (int)Math.Round((1m - cts / (decimal)origine) * 100m);
        }

        private static string PremiereChaine(JsonElement e, string nom)
        {
            JsonElement v;
            if (!Prop(e, nom, out v)) return null;
            if (v.ValueKind == JsonValueKind.String) return v.GetString();
            if (v.ValueKind != JsonValueKind.Array) return null;
            foreach (var x in v.EnumerateArray()) if (x.ValueKind == JsonValueKind.String) return x.GetString();
            return null;
        }

        private static bool ChiffresSeuls(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 20) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }
    }
}
