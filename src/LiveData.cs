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
        public static string NextHolidays()
        {
            try
            {
                string json = Get("https://date.nager.at/api/v3/NextPublicHolidays/FR");
                if (string.IsNullOrEmpty(json)) return null;
                using (var d = JsonDocument.Parse(json))
                {
                    var sb = new System.Text.StringBuilder();
                    int n = 0;
                    foreach (var h in d.RootElement.EnumerateArray())
                    {
                        if (n++ >= 5) break;
                        string iso = h.GetProperty("date").GetString();
                        string name = h.TryGetProperty("localName", out var ln) ? ln.GetString() : h.GetProperty("name").GetString();
                        sb.Append("• ").Append(FrDate(iso)).Append(" — ").Append(name).Append('\n');
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

        // « 2026-08-15 » → « 15 août 2026 »
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
