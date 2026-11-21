using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;

namespace BTOptimizer
{
    /// <summary>Un titre d'actualité. Date en UTC ; DateTime.MinValue = inconnue.</summary>
    internal sealed class Actu { public string Titre, Lien, Source; public DateTime Date; }

    /// <summary>
    /// Actus (logique PURE) : lecture des flux RSS 2.0, fusion, filtre et âge affiché.
    /// Seuls le titre, le lien et la date sont repris — jamais le contenu des articles
    /// (description, content:encoded). La DTD est ignorée et aucune ressource externe n'est résolue.
    /// </summary>
    internal static class Actus
    {
        public sealed class SourceActu { public string Nom, Url; }

        public static readonly SourceActu[] Sources =
        {
            new SourceActu { Nom = "jeuxvideo.com", Url = "https://www.jeuxvideo.com/rss/rss.xml" },
            new SourceActu { Nom = "Gamekult", Url = "https://www.gamekult.com/feed.xml" },
            new SourceActu { Nom = "ActuGaming", Url = "https://www.actugaming.net/feed/" },
            new SourceActu { Nom = "PC Gamer (EN)", Url = "https://www.pcgamer.com/rss/" },
        };

        public const int MaxAffiche = 120;
        private const int MaxTitre = 200;
        private static readonly Regex Espaces = new Regex(@"\s+", RegexOptions.CultureInvariant);
        private static readonly Regex FuseauCompact = new Regex(@"([+-])(\d{2})(\d{2})$", RegexOptions.CultureInvariant);

        public static string UrlFluxSteam(int appId)
        {
            return "https://store.steampowered.com/feeds/news/app/" + appId.ToString(CultureInfo.InvariantCulture) + "/?l=french";
        }

        public static List<Actu> LitRss(string xml, string source)
        {
            var res = new List<Actu>();
            if (string.IsNullOrWhiteSpace(xml)) return res;
            var doc = new XmlDocument { XmlResolver = null };
            try
            {
                var reglages = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                using (var sr = new StringReader(xml))
                using (var r = XmlReader.Create(sr, reglages))
                    doc.Load(r);
            }
            catch (XmlException) { return res; }
            catch (InvalidOperationException) { return res; }

            var racine = doc.DocumentElement;
            if (racine == null || racine.LocalName != "rss") return res;
            foreach (XmlNode canal in racine.ChildNodes)
            {
                if (canal.NodeType != XmlNodeType.Element || canal.LocalName != "channel") continue;
                foreach (XmlNode item in canal.ChildNodes)
                {
                    if (item.NodeType != XmlNodeType.Element || item.LocalName != "item") continue;
                    string titre = Titre(Enfant(item, "title"));
                    string lien = (Enfant(item, "link") ?? "").Trim();
                    if (titre.Length == 0 || !Offres.LienSur(lien)) continue;
                    res.Add(new Actu { Titre = titre, Lien = lien, Source = source, Date = DateRss(Enfant(item, "pubDate")) });
                }
            }
            return res;
        }

        /// <summary>Sans doublon de lien (première occurrence, dans l'ordre des flux), du plus récent au plus ancien.</summary>
        public static List<Actu> Fusionne(IEnumerable<List<Actu>> flux, int max)
        {
            var tous = new List<Actu>();
            var liens = new HashSet<string>();
            if (flux != null)
                foreach (var l in flux)
                {
                    if (l == null) continue;
                    foreach (var a in l)
                        if (a != null && a.Lien != null && liens.Add(a.Lien)) tous.Add(a);
                }
            return tous.OrderByDescending(a => a.Date).Take(Math.Max(0, max)).ToList();
        }

        public static List<Actu> Filtre(List<Actu> actus, string texte, string source)
        {
            var res = new List<Actu>();
            if (actus == null) return res;
            string t = Offres.NomNormalise(texte);
            foreach (var a in actus)
            {
                if (t.Length > 0 && !Offres.NomNormalise(a.Titre).Contains(t)) continue;
                if (!string.IsNullOrEmpty(source) && !string.Equals(a.Source, source, StringComparison.OrdinalIgnoreCase)) continue;
                res.Add(a);
            }
            return res;
        }

        /// <summary>« il y a 12 min », « il y a 3 h », « hier », « 12/11 » ; date inconnue ou future → vide.</summary>
        public static string Age(DateTime dateUtc, DateTime maintenantUtc)
        {
            if (dateUtc == DateTime.MinValue || dateUtc > maintenantUtc) return "";
            TimeSpan d = maintenantUtc - dateUtc;
            if (d.TotalHours < 1) return "il y a " + Math.Max(1, (int)d.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " min";
            if (d.TotalHours < 24) return "il y a " + ((int)d.TotalHours).ToString(CultureInfo.InvariantCulture) + " h";
            if (d.TotalHours < 48) return "hier";
            return dateUtc.ToString("dd/MM", CultureInfo.InvariantCulture);
        }

        public static Favori EnFavori(Actu a)
        {
            return new Favori { Cle = a.Lien, Source = a.Source, Url = a.Lien, Titre = a.Titre, Date = a.Date };
        }

        // ---------------------------------------------------------------- aides

        private static string Enfant(XmlNode item, string nom)
        {
            foreach (XmlNode c in item.ChildNodes)
                if (c.NodeType == XmlNodeType.Element && c.LocalName == nom && string.IsNullOrEmpty(c.NamespaceURI)) return c.InnerText;
            return null;
        }

        private static string Titre(string brut)
        {
            if (string.IsNullOrEmpty(brut)) return "";
            string t = Espaces.Replace(WebUtility.HtmlDecode(brut), " ").Trim();
            return t.Length > MaxTitre ? t.Substring(0, MaxTitre - 1).TrimEnd() + "…" : t;
        }

        /// <summary>Date RFC 822 (« Sat, 21 Nov 2026 10:30:00 +0000 ») convertie en UTC ; illisible → MinValue.</summary>
        private static DateTime DateRss(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return DateTime.MinValue;
            s = s.Trim();
            if (s.EndsWith(" GMT") || s.EndsWith(" UTC")) s = s.Substring(0, s.Length - 4) + " +00:00";
            else if (s.EndsWith(" UT") || s.EndsWith(" Z")) s = s.Substring(0, s.LastIndexOf(' ')) + " +00:00";
            else s = FuseauCompact.Replace(s, "$1$2:$3");
            DateTimeOffset d;
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d)) return d.UtcDateTime;
            // Jour de semaine faux (« Fri, 21 Nov ») : on retente sans lui.
            int v = s.IndexOf(',');
            if (v > 0 && DateTimeOffset.TryParse(s.Substring(v + 1).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d)) return d.UtcDateTime;
            return DateTime.MinValue;
        }
    }
}
