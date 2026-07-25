using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace BTOptimizer
{
    /// <summary>
    /// Données « simples » du quotidien : l'HEURE/DATE (100 % locale, hors-ligne, exacte) et la MÉTÉO
    /// dehors (API gratuite Open-Meteo, sans clé + géolocalisation approximative par IP). Tout est
    /// factuel/mesuré → anti-hallucination. La météo nécessite le web ; l'heure non.
    /// </summary>
    internal static class LiveData
    {
        private static readonly HttpClient Http = Build();
        private static HttpClient Build()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            h.DefaultRequestHeaders.Add("User-Agent", "ONYX-Copilote/1.0 (assistant PC local)");
            return h;
        }

        // ---- HEURE / DATE (local, hors-ligne) ----
        private static readonly string[] Jours = { "dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi" };
        private static readonly string[] Mois = { "", "janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre" };

        /// <summary>Date et heure locales en français (sans dépendance culture/ICU).</summary>
        public static string TimeNow()
        {
            DateTime n = DateTime.Now;
            string date = Jours[(int)n.DayOfWeek] + " " + n.Day + " " + Mois[n.Month] + " " + n.Year;
            string heure = n.ToString("HH") + "h" + n.ToString("mm");
            return "🕐 Il est " + heure + ", nous sommes le " + date + ". (horloge de ton PC)";
        }

        // ---- MÉTÉO (Open-Meteo, gratuit, sans clé) ----
        public sealed class Meteo { public string City; public double Temp; public double Wind; public string Desc; public bool FromIp; }

        /// <summary>Météo actuelle pour une ville (ou, si vide, la ville devinée par l'IP). null si échec.</summary>
        public static Meteo Current(string city)
        {
            try
            {
                double lat, lon; string name; bool fromIp = false;
                if (!string.IsNullOrWhiteSpace(city))
                {
                    if (!Geocode(city.Trim(), out lat, out lon, out name)) return null;
                }
                else
                {
                    if (!GeoByIp(out lat, out lon, out name)) return null;
                    fromIp = true;
                }
                double temp, wind; int code;
                if (!Forecast(lat, lon, out temp, out wind, out code)) return null;
                return new Meteo { City = name, Temp = temp, Wind = wind, Desc = Describe(code), FromIp = fromIp };
            }
            catch { return null; }
        }

        // Ville -> coordonnées (API de géocodage Open-Meteo, gratuite).
        private static bool Geocode(string city, out double lat, out double lon, out string name)
        {
            lat = lon = 0; name = city;
            string json = Get("https://geocoding-api.open-meteo.com/v1/search?count=1&language=fr&format=json&name=" + Uri.EscapeDataString(city));
            if (string.IsNullOrEmpty(json)) return false;
            using (var d = JsonDocument.Parse(json))
            {
                if (!d.RootElement.TryGetProperty("results", out var res) || res.ValueKind != JsonValueKind.Array || res.GetArrayLength() == 0) return false;
                var r0 = res[0];
                lat = r0.GetProperty("latitude").GetDouble();
                lon = r0.GetProperty("longitude").GetDouble();
                if (r0.TryGetProperty("name", out var nm)) name = nm.GetString();
                return true;
            }
        }

        // IP -> ville/coordonnées approximatives (ipwho.is, gratuit, https, sans clé).
        private static bool GeoByIp(out double lat, out double lon, out string name)
        {
            lat = lon = 0; name = "";
            string json = Get("https://ipwho.is/");
            if (string.IsNullOrEmpty(json)) return false;
            using (var d = JsonDocument.Parse(json))
            {
                var root = d.RootElement;
                if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) return false;
                if (!root.TryGetProperty("latitude", out var la) || !root.TryGetProperty("longitude", out var lo)) return false;
                lat = la.GetDouble(); lon = lo.GetDouble();
                name = root.TryGetProperty("city", out var c) ? c.GetString() : "ta position";
                return true;
            }
        }

        // Coordonnées -> météo actuelle.
        private static bool Forecast(double lat, double lon, out double temp, out double wind, out int code)
        {
            temp = wind = 0; code = -1;
            string url = "https://api.open-meteo.com/v1/forecast?current=temperature_2m,weather_code,wind_speed_10m&timezone=auto"
                       + "&latitude=" + lat.ToString(System.Globalization.CultureInfo.InvariantCulture)
                       + "&longitude=" + lon.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string json = Get(url);
            if (string.IsNullOrEmpty(json)) return false;
            using (var d = JsonDocument.Parse(json))
            {
                if (!d.RootElement.TryGetProperty("current", out var cur)) return false;
                if (cur.TryGetProperty("temperature_2m", out var t)) temp = t.GetDouble();
                if (cur.TryGetProperty("wind_speed_10m", out var w)) wind = w.GetDouble();
                if (cur.TryGetProperty("weather_code", out var c)) code = c.GetInt32();
                return true;
            }
        }

        // Code météo WMO -> description française.
        internal static string Describe(int c)
        {
            switch (c)
            {
                case 0: return "ciel dégagé";
                case 1: return "plutôt dégagé";
                case 2: return "partiellement nuageux";
                case 3: return "couvert";
                case 45: case 48: return "brouillard";
                case 51: case 53: case 55: return "bruine";
                case 56: case 57: return "bruine verglaçante";
                case 61: case 63: case 65: return "pluie";
                case 66: case 67: return "pluie verglaçante";
                case 71: case 73: case 75: return "neige";
                case 77: return "grains de neige";
                case 80: case 81: case 82: return "averses";
                case 85: case 86: return "averses de neige";
                case 95: return "orage";
                case 96: case 99: return "orage avec grêle";
                default: return "temps variable";
            }
        }

        // ---- DEVISES (Frankfurter / BCE, gratuit sans clé) ----
        /// <summary>Convertit un montant de 'from' vers 'to' (codes ISO). null si échec.</summary>
        public static double? Currency(double amount, string from, string to)
        {
            try
            {
                string json = Get("https://api.frankfurter.dev/v1/latest?base=" + Uri.EscapeDataString(from) + "&symbols=" + Uri.EscapeDataString(to));
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                    if (d.RootElement.TryGetProperty("rates", out var rates) && rates.TryGetProperty(to, out var rr))
                        return amount * rr.GetDouble();
            }
            catch { }
            return null;
        }

        // ---- JOURS FÉRIÉS (Nager.Date, gratuit) ----
        public static string NextHolidays() { return NextHolidaysFor("FR"); }
        /// <summary>Prochains jours fériés d'un pays (code ISO2). null si pays non couvert / hors-ligne.</summary>
        public static string NextHolidaysFor(string iso)
        {
            try
            {
                string json = Get("https://date.nager.at/api/v3/NextPublicHolidays/" + Uri.EscapeDataString(iso));
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    var sb = new System.Text.StringBuilder();
                    int n = 0;
                    foreach (var h in d.RootElement.EnumerateArray())
                    {
                        if (n++ >= 5) break;
                        string dateIso = h.GetProperty("date").GetString();
                        string name = h.TryGetProperty("localName", out var ln) ? ln.GetString() : h.GetProperty("name").GetString();
                        sb.Append("• ").Append(FrDate(dateIso)).Append(" — ").Append(name).Append('\n');
                    }
                    return sb.ToString().TrimEnd();
                }
            }
            catch { return null; }
        }

        // ---- CRYPTO (CoinGecko, gratuit) ----
        public static double? Crypto(string id, string vs)
        {
            try
            {
                string json = Get("https://api.coingecko.com/api/v3/simple/price?ids=" + id + "&vs_currencies=" + vs);
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                    if (d.RootElement.TryGetProperty(id, out var c) && c.TryGetProperty(vs, out var v))
                        return v.GetDouble();
            }
            catch { }
            return null;
        }

        // ---- TRADUCTION (MyMemory, gratuit sans clé) ----
        public static string Translate(string text, string from, string to)
        {
            try
            {
                string json = Get("https://api.mymemory.translated.net/get?langpair=" + from + "|" + to + "&q=" + Uri.EscapeDataString(text));
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                    if (d.RootElement.TryGetProperty("responseData", out var rd) && rd.TryGetProperty("translatedText", out var tt))
                    {
                        string s = tt.GetString();
                        return string.IsNullOrEmpty(s) ? null : System.Net.WebUtility.HtmlDecode(s);
                    }
            }
            catch { }
            return null;
        }

        // ---- MON IP PUBLIQUE (ipwho.is, gratuit) ----
        public static string MyIp()
        {
            try
            {
                string json = Get("https://ipwho.is/");
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    var r = d.RootElement;
                    string ip = r.TryGetProperty("ip", out var i) ? i.GetString() : "?";
                    string city = r.TryGetProperty("city", out var c) ? c.GetString() : "";
                    string isp = "";
                    if (r.TryGetProperty("connection", out var con) && con.TryGetProperty("isp", out var ii)) isp = ii.GetString();
                    return "🌐 Ton IP PUBLIQUE : " + ip + (string.IsNullOrEmpty(city) ? "" : " — " + city)
                         + (string.IsNullOrEmpty(isp) ? "" : " (" + isp + ")") + ".\n(Ton IP LOCALE, elle, se voit avec « ipconfig ».)";
                }
            }
            catch { return null; }
        }

        // ---- LEVER / COUCHER DU SOLEIL (Open-Meteo daily) ----
        public sealed class Sun { public string City; public string Rise; public string Set; public bool FromIp; }
        public static Sun SunTimes(string city)
        {
            try
            {
                double lat, lon; string nm; bool fromIp = false;
                if (!string.IsNullOrWhiteSpace(city)) { if (!Geocode(city.Trim(), out lat, out lon, out nm)) return null; }
                else { if (!GeoByIp(out lat, out lon, out nm)) return null; fromIp = true; }
                string url = "https://api.open-meteo.com/v1/forecast?daily=sunrise,sunset&timezone=auto"
                           + "&latitude=" + lat.ToString(System.Globalization.CultureInfo.InvariantCulture)
                           + "&longitude=" + lon.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string json = Get(url);
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    if (!d.RootElement.TryGetProperty("daily", out var dd)) return null;
                    string rise = dd.GetProperty("sunrise")[0].GetString();
                    string set = dd.GetProperty("sunset")[0].GetString();
                    return new Sun { City = nm, Rise = Hm(rise), Set = Hm(set), FromIp = fromIp };
                }
            }
            catch { return null; }
        }
        private static string Hm(string iso)
        {
            int t = iso.IndexOf('T'); if (t < 0) return iso;
            string hm = iso.Substring(t + 1); if (hm.Length >= 5) hm = hm.Substring(0, 5);
            return hm.Replace(':', 'h');
        }

        // ---- PRODUIT / NUTRITION (Open Food Facts, gratuit sans clé) ----
        /// <summary>Fiche nutrition d'un produit alimentaire (Nutri-Score, NOVA, valeurs /100 g). null si rien.</summary>
        public static string Food(string query)
        {
            try
            {
                // API de recherche moderne (« search-a-licious ») : rapide + pertinente et renvoie du JSON
                // (l'ancien cgi/search.pl répond souvent une page HTML « temporarily unavailable »).
                string url = "https://search.openfoodfacts.org/search?page_size=1"
                           + "&fields=product_name,product_name_fr,brands,nutriscore_grade,nova_group,nutriments"
                           + "&q=" + Uri.EscapeDataString(query);
                string json = Get(url);
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    if (!d.RootElement.TryGetProperty("hits", out var ps) || ps.ValueKind != JsonValueKind.Array || ps.GetArrayLength() == 0) return null;
                    var p = ps[0];
                    string name = Str(p, "product_name_fr"); if (string.IsNullOrEmpty(name)) name = Str(p, "product_name");
                    if (string.IsNullOrEmpty(name)) return null;

                    var sb = new System.Text.StringBuilder();
                    sb.Append("🍽️ ").Append(name);
                    string brand = FirstBrand(p);
                    if (!string.IsNullOrEmpty(brand)) sb.Append(" (").Append(brand).Append(')');
                    sb.Append('\n');

                    string nutri = Str(p, "nutriscore_grade");
                    bool hasNutri = nutri.Length == 1 && nutri[0] >= 'a' && nutri[0] <= 'e';
                    if (hasNutri) sb.Append("Nutri-Score : ").Append(nutri.ToUpperInvariant());
                    double? novaN = Num(p, "nova_group");
                    if (novaN.HasValue) sb.Append(hasNutri ? " · groupe NOVA " : "Groupe NOVA ").Append((int)novaN.Value).Append(NovaHint((int)novaN.Value));

                    if (p.TryGetProperty("nutriments", out var nut) && nut.ValueKind == JsonValueKind.Object)
                    {
                        string per100 = Per100(nut);
                        if (!string.IsNullOrEmpty(per100)) sb.Append(hasNutri || novaN.HasValue ? "\n" : "").Append("Pour 100 g/ml : ").Append(per100);
                    }
                    sb.Append("\n— source : Open Food Facts (base communautaire, gratuite).");
                    return sb.ToString();
                }
            }
            catch { }
            return null;
        }

        private static string NovaHint(int nova)
        {
            switch (nova) { case 1: return " (peu ou pas transformé)"; case 4: return " (ultra-transformé)"; default: return ""; }
        }
        // « brands » peut être un tableau (search-a-licious) ou une chaîne « a,b » (ancienne API).
        private static string FirstBrand(JsonElement p)
        {
            if (!p.TryGetProperty("brands", out var b)) return "";
            if (b.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in b.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String) { string s = x.GetString(); if (!string.IsNullOrWhiteSpace(s)) return s.Trim(); }
                return "";
            }
            if (b.ValueKind == JsonValueKind.String) { string s = b.GetString(); return string.IsNullOrEmpty(s) ? "" : s.Split(',')[0].Trim(); }
            return "";
        }
        private static string Per100(JsonElement nut)
        {
            var parts = new System.Collections.Generic.List<string>();
            double? kcal = Num(nut, "energy-kcal_100g"); if (kcal == null) kcal = Num(nut, "energy-kcal");
            if (kcal != null) parts.Add(G(kcal.Value) + " kcal");
            double? sug = Num(nut, "sugars_100g"); if (sug != null) parts.Add(G(sug.Value) + " g de sucres");
            double? fat = Num(nut, "fat_100g"); if (fat != null) parts.Add(G(fat.Value) + " g de matières grasses");
            double? sel = Num(nut, "salt_100g"); if (sel != null) parts.Add(G(sel.Value) + " g de sel");
            double? prot = Num(nut, "proteins_100g"); if (prot != null) parts.Add(G(prot.Value) + " g de protéines");
            return string.Join(" · ", parts);
        }

        // ---- CODE POSTAL → VILLE (Zippopotam, gratuit sans clé) ----
        /// <summary>Ville(s) d'un code postal français via Zippopotam. null si introuvable.</summary>
        public static string Postal(string code)
        {
            try
            {
                string json = Get("https://api.zippopotam.us/fr/" + Uri.EscapeDataString(code));
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    var root = d.RootElement;
                    if (!root.TryGetProperty("places", out var pl) || pl.ValueKind != JsonValueKind.Array || pl.GetArrayLength() == 0) return null;
                    var p0 = pl[0];
                    string place = p0.TryGetProperty("place name", out var pn) ? pn.GetString() : "";
                    if (string.IsNullOrEmpty(place)) return null;
                    string state = p0.TryGetProperty("state", out var stt) ? stt.GetString() : "";
                    string extra = string.IsNullOrEmpty(state) ? "" : " (" + state + ")";
                    string more = pl.GetArrayLength() > 1 ? " — et " + (pl.GetArrayLength() - 1) + " autre(s) commune(s)" : "";
                    return "📮 " + code + " → " + place + extra + more + ", France.\n— source : Zippopotam (gratuit).";
                }
            }
            catch { }
            return null;
        }

        // helpers JSON/format partagés
        private static string Str(JsonElement e, string prop)
        {
            if (e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String) { string s = v.GetString(); return s == null ? "" : s.Trim(); }
            return "";
        }
        private static double? Num(JsonElement e, string prop)
        {
            if (!e.TryGetProperty(prop, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double r)) return r;
            return null;
        }
        private static string G(double v)  // grammes/kcal : 57.5 → « 57,5 »
        {
            return Math.Round(v, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
        }

        // ---- PHOTO ASTRO DU JOUR (NASA APOD, clé de démo publique) ----
        /// <summary>Photo/vidéo astronomique du jour de la NASA. null si échec.</summary>
        public static string Apod()
        {
            try
            {
                string json = Get("https://api.nasa.gov/planetary/apod?api_key=DEMO_KEY");
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    var r = d.RootElement;
                    string title = r.TryGetProperty("title", out var t) ? t.GetString() : null;
                    if (string.IsNullOrEmpty(title)) return null;
                    string date = r.TryGetProperty("date", out var dt) ? dt.GetString() : "";
                    string media = r.TryGetProperty("media_type", out var m) ? m.GetString() : "image";
                    string link = r.TryGetProperty("url", out var u) ? u.GetString() : "";
                    var sb = new System.Text.StringBuilder();
                    sb.Append(media == "video" ? "🎬 Vidéo astro du jour (NASA)" : "🔭 Photo astro du jour (NASA)");
                    if (!string.IsNullOrEmpty(date)) sb.Append(" — ").Append(FrDate(date));
                    sb.Append('\n').Append(title);
                    if (!string.IsNullOrEmpty(link)) sb.Append('\n').Append(link);
                    sb.Append("\n(APOD, clé de démo NASA ; description détaillée en anglais sur le lien.)");
                    return sb.ToString();
                }
            }
            catch { }
            return null;
        }

        // ---- AVIONS EN VOL AUTOUR DE TOI (OpenSky, gratuit sans clé) ----
        /// <summary>Avions actuellement en vol dans une zone autour de ta position (estimée par IP). null si échec.</summary>
        public static string FlightsNearby()
        {
            try
            {
                double lat, lon; string city;
                if (!GeoByIp(out lat, out lon, out city)) return null;
                if (string.IsNullOrEmpty(city)) city = "ta position";
                const double box = 0.6;   // ~ ±60 km
                string url = "https://opensky-network.org/api/states/all?lamin=" + Inv(lat - box) + "&lomin=" + Inv(lon - box)
                           + "&lamax=" + Inv(lat + box) + "&lomax=" + Inv(lon + box);
                string json = Get(url);
                if (string.IsNullOrEmpty(json)) return null;
                using (var doc = JsonDocument.Parse(json))
                {
                    JsonElement st;
                    bool has = doc.RootElement.TryGetProperty("states", out st) && st.ValueKind == JsonValueKind.Array;
                    int total = has ? st.GetArrayLength() : 0;
                    if (total == 0) return "✈️ Aucun avion détecté juste au-dessus de toi (≈ " + city + ") à l'instant.\n— source : OpenSky Network (gratuit).";
                    var sb = new System.Text.StringBuilder();
                    sb.Append("✈️ ").Append(total).Append(total > 1 ? " avions en vol" : " avion en vol").Append(" autour de toi (≈ ").Append(city).Append(") :\n");
                    int n = 0;
                    foreach (var a in st.EnumerateArray())
                    {
                        if (n++ >= 6) break;
                        string call = a.GetArrayLength() > 1 && a[1].ValueKind == JsonValueKind.String ? a[1].GetString().Trim() : "";
                        string ctry = a.GetArrayLength() > 2 && a[2].ValueKind == JsonValueKind.String ? a[2].GetString() : "";
                        string alt = a.GetArrayLength() > 7 && a[7].ValueKind == JsonValueKind.Number ? " à " + (int)Math.Round(a[7].GetDouble()) + " m" : "";
                        sb.Append("• ").Append(string.IsNullOrEmpty(call) ? "(sans indicatif)" : call);
                        if (!string.IsNullOrEmpty(ctry)) sb.Append(" — ").Append(ctry);
                        sb.Append(alt).Append('\n');
                    }
                    if (total > 6) sb.Append("…et ").Append(total - 6).Append(" autre(s).\n");
                    sb.Append("— source : OpenSky Network (gratuit).");
                    return sb.ToString();
                }
            }
            catch { }
            return null;
        }
        private static string Inv(double v) { return v.ToString(System.Globalization.CultureInfo.InvariantCulture); }

        // ---- DETTE PUBLIQUE DE LA FRANCE EN DIRECT (dettedelafrance.fr, gratuit sans clé) ----
        /// <summary>Dette publique française en temps réel (montant, ratio PIB, par habitant, cadence). null si échec.</summary>
        public static string Debt()
        {
            try
            {
                string json = Get("https://dettedelafrance.fr/api/v1/debt");
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    var r = d.RootElement;
                    if (r.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False) return null;
                    double? billions = Num(r, "debt_eur_billions");
                    if (billions == null) return null;
                    double? ratio = Num(r, "debt_to_gdp_ratio_pct");
                    double? perCap = Num(r, "debt_per_capita_eur");
                    double? perSec = Num(r, "rate_eur_per_second");

                    var sb = new System.Text.StringBuilder();
                    sb.Append("🇫🇷 Dette publique de la France : ").Append(FrNum(billions.Value, 2)).Append(" milliards d'euros");
                    if (ratio != null) sb.Append(" (").Append(FrNum(ratio.Value, 1)).Append(" % du PIB)");
                    sb.Append(".\n");
                    bool any = false;
                    if (perCap != null) { sb.Append("Soit ≈ ").Append(FrNum(perCap.Value, 0)).Append(" € par habitant"); any = true; }
                    if (perSec != null) { sb.Append(any ? " · elle grimpe d'environ " : "Elle grimpe d'environ ").Append(FrNum(perSec.Value, 0)).Append(" €/seconde"); any = true; }
                    if (any) sb.Append(".\n");
                    sb.Append("— source : dettedelafrance.fr (données INSEE/AFT/Banque de France, CC BY 4.0).");
                    return sb.ToString();
                }
            }
            catch { }
            return null;
        }
        // 3546.28 → « 3 546,28 » (milliers = espace insécable, décimale = virgule ; sans dépendance ICU).
        private static string FrNum(double v, int dec)
        {
            string fmt = dec > 0 ? "#,##0." + new string('#', dec) : "#,##0";
            string s = v.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
            return s.Replace(",", " ").Replace(".", ",");
        }

        // « 2026-08-15 » → « 15 août 2026 »
        // ---- QUALITE DE L'AIR (Open-Meteo, gratuit sans cle) ----
        /// <summary>Qualite de l'air pour une ville (ou position par IP) : indice EAQI + particules. null si echec.</summary>
        public static string AirNow(string city)
        {
            try
            {
                double lat, lon; string nm; bool fromIp = false;
                if (!string.IsNullOrWhiteSpace(city)) { if (!Geocode(city.Trim(), out lat, out lon, out nm)) return null; }
                else { if (!GeoByIp(out lat, out lon, out nm)) return null; fromIp = true; }
                if (string.IsNullOrEmpty(nm)) nm = "ta position";
                string url = "https://air-quality-api.open-meteo.com/v1/air-quality?current=european_aqi,pm2_5,pm10&timezone=auto"
                           + "&latitude=" + Inv(lat) + "&longitude=" + Inv(lon);
                string json = Get(url);
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    if (!d.RootElement.TryGetProperty("current", out var cur)) return null;
                    int aqi = cur.TryGetProperty("european_aqi", out var aq) && aq.ValueKind == JsonValueKind.Number ? aq.GetInt32() : -1;
                    double? pm25 = Num(cur, "pm2_5");
                    double? pm10 = Num(cur, "pm10");
                    var sb = new System.Text.StringBuilder();
                    sb.Append("🌬️ Qualité de l'air à ").Append(nm).Append(fromIp ? " (estimée d'après ta connexion)" : "").Append(" : ");
                    if (aqi >= 0) { string lab = AqiLabel(aqi); sb.Append("indice européen ").Append(aqi).Append(lab.Length > 0 ? " (" + lab + ")" : ""); }
                    else sb.Append("indice indisponible");
                    sb.Append('.');
                    if (pm25 != null || pm10 != null)
                    {
                        sb.Append("\nParticules fines :");
                        if (pm25 != null) sb.Append(" PM2.5 ").Append(G(pm25.Value));
                        if (pm10 != null) sb.Append(pm25 != null ? " · PM10 " : " PM10 ").Append(G(pm10.Value));
                        sb.Append(" µg/m³");
                    }
                    sb.Append("\n— source : Open-Meteo (gratuit).");
                    return sb.ToString();
                }
            }
            catch { }
            return null;
        }
        // Indice europeen de qualite de l'air (EAQI 0->100+) -> mot francais.
        private static string AqiLabel(int aqi)
        {
            if (aqi < 0) return "";
            if (aqi <= 20) return "très bon";
            if (aqi <= 40) return "bon";
            if (aqi <= 60) return "moyen";
            if (aqi <= 80) return "mauvais";
            if (aqi <= 100) return "très mauvais";
            return "extrêmement mauvais";
        }

        // ---- DISTANCE ENTRE 2 VILLES (géocodage Open-Meteo + calcul LOCAL) ----
        /// <summary>Distance à vol d'oiseau entre deux villes. null si une ville est introuvable.</summary>
        public static string Distance(string a, string b)
        {
            try
            {
                double la, lo, lb, ob; string na, nb;
                if (!Geocode(a, out la, out lo, out na)) return null;
                if (!Geocode(b, out lb, out ob, out nb)) return null;
                double km = Haversine(la, lo, lb, ob);
                return "📏 Distance à vol d'oiseau entre " + na + " et " + nb + " : " + FrNum(km, 0) + " km."
                     + "\n(distance géodésique, calcul local ; par la route c'est un peu plus long.)";
            }
            catch { }
            return null;
        }
        private static double Haversine(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            double dLat = Rad(lat2 - lat1), dLon = Rad(lon2 - lon1);
            double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * R * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
        }
        private static double Rad(double deg) { return deg * Math.PI / 180.0; }

        // ---- PHASE DE LA LUNE (calcul LOCAL, hors-ligne, déterministe) ----
        /// <summary>Phase lunaire du moment, calculée localement (mois synodique). Toujours disponible.</summary>
        public static string MoonPhase()
        {
            DateTime knownNew = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);  // nouvelle lune de référence
            const double syn = 29.530588853;                                            // mois synodique (jours)
            double frac = (DateTime.UtcNow - knownNew).TotalDays / syn;
            frac -= Math.Floor(frac);
            if (frac < 0) frac += 1;
            double age = frac * syn;
            int illum = (int)Math.Round((1 - Math.Cos(2 * Math.PI * frac)) / 2 * 100);
            return "🌙 Lune : " + MoonName(frac) + " — âge ≈ " + FrNum(age, 1) + " jour(s), éclairée à ≈ " + illum + " %."
                 + "\n(calcul local, hors-ligne.)";
        }
        private static string MoonName(double f)
        {
            if (f < 0.03 || f >= 0.97) return "nouvelle lune";
            if (f < 0.22) return "premier croissant";
            if (f < 0.28) return "premier quartier";
            if (f < 0.47) return "gibbeuse croissante";
            if (f < 0.53) return "pleine lune";
            if (f < 0.72) return "gibbeuse décroissante";
            if (f < 0.78) return "dernier quartier";
            return "dernier croissant";
        }

        // ---- SÉISMES RÉCENTS (USGS, gratuit sans clé) ----
        /// <summary>5 derniers séismes de magnitude ≥ 4 dans le monde. null si échec.</summary>
        public static string Quakes()
        {
            try
            {
                string json = Get("https://earthquake.usgs.gov/fdsnws/event/1/query?format=geojson&limit=5&orderby=time&minmagnitude=4");
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    if (!d.RootElement.TryGetProperty("features", out var fs) || fs.ValueKind != JsonValueKind.Array || fs.GetArrayLength() == 0) return null;
                    var sb = new System.Text.StringBuilder();
                    sb.Append("🌍 Derniers séismes (magnitude ≥ 4) dans le monde :\n");
                    int n = 0;
                    foreach (var f in fs.EnumerateArray())
                    {
                        if (n++ >= 5) break;
                        if (!f.TryGetProperty("properties", out var p)) continue;
                        double mag = Num(p, "mag") ?? 0;
                        string place = p.TryGetProperty("place", out var pl) && pl.ValueKind == JsonValueKind.String ? pl.GetString() : "";
                        string when = "";
                        if (p.TryGetProperty("time", out var t) && t.ValueKind == JsonValueKind.Number)
                        {
                            var dto = DateTimeOffset.FromUnixTimeMilliseconds(t.GetInt64()).ToLocalTime();
                            when = " (" + dto.ToString("dd/MM") + " " + dto.ToString("HH") + "h" + dto.ToString("mm") + ")";
                        }
                        sb.Append("• M ").Append(G(mag)).Append(" — ").Append(place).Append(when).Append('\n');
                    }
                    sb.Append("— source : USGS (gratuit). Lieux en anglais.");
                    return sb.ToString();
                }
            }
            catch { }
            return null;
        }

        // ---- POSITION DE L'ISS (wheretheiss.at, gratuit sans clé) ----
        /// <summary>Position actuelle de la Station spatiale internationale. null si échec.</summary>
        public static string Iss()
        {
            try
            {
                string json = Get("https://api.wheretheiss.at/v1/satellites/25544");
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    var r = d.RootElement;
                    double? lat = Num(r, "latitude"), lon = Num(r, "longitude"), alt = Num(r, "altitude"), vel = Num(r, "velocity");
                    if (lat == null || lon == null) return null;
                    var sb = new System.Text.StringBuilder();
                    sb.Append("🛰️ La Station spatiale internationale (ISS) survole ")
                      .Append(FrNum(Math.Abs(lat.Value), 1)).Append("° ").Append(lat.Value >= 0 ? "N" : "S").Append(", ")
                      .Append(FrNum(Math.Abs(lon.Value), 1)).Append("° ").Append(lon.Value >= 0 ? "E" : "O").Append('.');
                    if (alt != null) sb.Append("\nAltitude ≈ ").Append(FrNum(alt.Value, 0)).Append(" km");
                    if (vel != null) sb.Append(alt != null ? " · vitesse ≈ " : "\nVitesse ≈ ").Append(FrNum(vel.Value, 0)).Append(" km/h");
                    sb.Append(".\n— source : wheretheiss.at (gratuit).");
                    return sb.ToString();
                }
            }
            catch { }
            return null;
        }

        // ---- ACTUALITÉS (Google Actualités RSS FR, gratuit sans clé) ----
        /// <summary>Titres de presse : à la une (topic vide) ou recherche sur un sujet. null si échec.</summary>
        public static string News(string topic)
        {
            try
            {
                string url = string.IsNullOrWhiteSpace(topic)
                    ? "https://news.google.com/rss?hl=fr&gl=FR&ceid=FR:fr"
                    : "https://news.google.com/rss/search?hl=fr&gl=FR&ceid=FR:fr&q=" + Uri.EscapeDataString(topic);
                string xml = Get(url);
                if (string.IsNullOrEmpty(xml)) return null;
                var items = System.Text.RegularExpressions.Regex.Matches(xml, "<item>(.*?)</item>", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (items.Count == 0) return null;
                var sb = new System.Text.StringBuilder();
                sb.Append(string.IsNullOrWhiteSpace(topic) ? "📰 À la une en ce moment :\n" : "📰 Actus « " + topic.Trim() + " » :\n");
                int n = 0;
                foreach (System.Text.RegularExpressions.Match it in items)
                {
                    if (n >= 5) break;
                    var tm = System.Text.RegularExpressions.Regex.Match(it.Groups[1].Value, "<title>(.*?)</title>", System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (!tm.Success) continue;
                    string title = CleanXml(tm.Groups[1].Value);
                    if (string.IsNullOrEmpty(title)) continue;
                    sb.Append("• ").Append(title).Append('\n');
                    n++;
                }
                if (n == 0) return null;
                sb.Append("— source : Google Actualités (gratuit). Ce sont des TITRES de presse, pas des faits que j'affirme.");
                return sb.ToString();
            }
            catch { }
            return null;
        }
        private static string CleanXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("<![CDATA[", "").Replace("]]>", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");
            return System.Net.WebUtility.HtmlDecode(s).Trim();
        }

        // ---- JEUX GRATUITS PC (FreeToGame, gratuit sans clé) ----
        /// <summary>Liste de jeux free-to-play PC (option : genre FreeToGame). null si échec.</summary>
        public static string FreeGames(string genre)
        {
            try
            {
                string url = "https://www.freetogame.com/api/games?platform=pc";
                if (!string.IsNullOrWhiteSpace(genre)) url += "&category=" + Uri.EscapeDataString(genre);
                string json = Get(url);
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    if (d.RootElement.ValueKind != JsonValueKind.Array || d.RootElement.GetArrayLength() == 0) return null;
                    int total = d.RootElement.GetArrayLength();
                    var sb = new System.Text.StringBuilder();
                    sb.Append("🎮 Jeux gratuits (free-to-play) sur PC");
                    if (!string.IsNullOrWhiteSpace(genre)) sb.Append(" — genre « ").Append(genre).Append(" »");
                    sb.Append(" :\n");
                    int n = 0;
                    foreach (var g in d.RootElement.EnumerateArray())
                    {
                        if (n >= 7) break;
                        string title = g.TryGetProperty("title", out var t) ? t.GetString() : null;
                        if (string.IsNullOrEmpty(title)) continue;
                        string gen = g.TryGetProperty("genre", out var ge) ? ge.GetString() : "";
                        sb.Append("• ").Append(title);
                        if (!string.IsNullOrEmpty(gen)) sb.Append(" — ").Append(gen);
                        sb.Append('\n');
                        n++;
                    }
                    if (n == 0) return null;
                    sb.Append("— source : FreeToGame (gratuit). ").Append(total).Append(" jeu(x) au total.");
                    return sb.ToString();
                }
            }
            catch { }
            return null;
        }

        // ---- LIVRES (Open Library, gratuit sans clé) ----
        /// <summary>Recherche de livres (titre, auteur, année). null si rien.</summary>
        public static string Book(string query)
        {
            try
            {
                string json = Get("https://openlibrary.org/search.json?limit=3&fields=title,author_name,first_publish_year&q=" + Uri.EscapeDataString(query));
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    if (!d.RootElement.TryGetProperty("docs", out var docs) || docs.ValueKind != JsonValueKind.Array || docs.GetArrayLength() == 0) return null;
                    var sb = new System.Text.StringBuilder();
                    sb.Append("📚 Livres trouvés pour « ").Append(query.Trim()).Append(" » :\n");
                    int n = 0;
                    foreach (var b in docs.EnumerateArray())
                    {
                        if (n >= 3) break;
                        string title = b.TryGetProperty("title", out var t) ? t.GetString() : null;
                        if (string.IsNullOrEmpty(title)) continue;
                        string author = "";
                        if (b.TryGetProperty("author_name", out var au) && au.ValueKind == JsonValueKind.Array && au.GetArrayLength() > 0) author = au[0].GetString();
                        string year = b.TryGetProperty("first_publish_year", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32().ToString() : "";
                        sb.Append("• ").Append(title);
                        if (!string.IsNullOrEmpty(author)) sb.Append(" — ").Append(author);
                        if (!string.IsNullOrEmpty(year)) sb.Append(" (").Append(year).Append(')');
                        sb.Append('\n');
                        n++;
                    }
                    if (n == 0) return null;
                    sb.Append("— source : Open Library (gratuit).");
                    return sb.ToString();
                }
            }
            catch { }
            return null;
        }

        internal static string FrDate(string iso)
        {
            try
            {
                var parts = iso.Split('-');
                int y = int.Parse(parts[0]), mo = int.Parse(parts[1]), day = int.Parse(parts[2]);
                return day + " " + Mois[mo] + " " + y;
            }
            catch { return iso; }
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
    }
}
