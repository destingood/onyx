using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Actus : lecture RSS 2.0 (titre, lien, date ; jamais le contenu), liens sûrs, DTD ignorée,
    /// fusion, filtre, âge affiché et favoris.
    /// </summary>
    internal static class ActusTests
    {
        const string Rss = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><rss version=\"2.0\"><channel><title>Flux</title>" +
          "<item><title>Mise à jour de Counter-Strike 2</title><link>https://store.steampowered.com/news/app/730/view/1</link><pubDate>Sat, 21 Nov 2026 10:30:00 +0000</pubDate><description>&lt;p&gt;texte long&lt;/p&gt;</description></item>" +
          "<item><title><![CDATA[Test &amp; avis : l&#039;été   du jeu]]></title><link>https://www.gamekult.com/a/2</link><pubDate>Fri, 20 Nov 2026 08:00:00 +0100</pubDate></item>" +
          "<item><title>Lien piégé</title><link>javascript:alert(1)</link><pubDate>Sat, 21 Nov 2026 09:00:00 +0000</pubDate></item>" +
          "<item><title></title><link>https://x.fr/vide</link></item>" +
          "<item><title>Sans date</title><link>https://x.fr/sansdate</link><pubDate>n'importe quoi</pubDate></item>" +
          "</channel></rss>";

        public static void Tout()
        {
            Banc.Titre("Actus : flux RSS");

            var l = Actus.LitRss(Rss, "Test");
            Banc.Verifie("3 items valides", true, l.Count == 3);
            Banc.Verifie("titre, lien, source, date", true, l[0].Titre == "Mise à jour de Counter-Strike 2" && l[0].Lien == "https://store.steampowered.com/news/app/730/view/1" && l[0].Source == "Test" && l[0].Date == new DateTime(2026, 11, 21, 10, 30, 0, DateTimeKind.Utc));
            Banc.Verifie("entités décodées, espaces repliés, fuseau converti", true, l[1].Titre == "Test & avis : l'été du jeu" && l[1].Date == new DateTime(2026, 11, 20, 7, 0, 0, DateTimeKind.Utc));
            Banc.Verifie("date illisible", true, l[2].Date == DateTime.MinValue);
            Banc.Verifie("XML invalide", true, Actus.LitRss("<rss><channel><item>", "T").Count == 0 && Actus.LitRss(null, "T").Count == 0 && Actus.LitRss("<html><body>404</body></html>", "T").Count == 0);
            const string M = "xmlns:media=\"http://search.yahoo.com/mrss/\"";
            var im = Actus.LitRss("<rss version=\"2.0\" " + M + "><channel>" +
                "<item><title>a</title><link>https://x.fr/1</link><enclosure url=\"https://img.x.fr/1.jpg\" type=\"image/jpeg\" /></item>" +
                "<item><title>b</title><link>https://x.fr/2</link><media:content url=\"https://img.x.fr/2.jpg\" medium=\"image\" /></item>" +
                "<item><title>c</title><link>https://x.fr/3</link><enclosure url=\"https://x.fr/3.mp3\" type=\"audio/mpeg\" /><media:thumbnail url=\"https://img.x.fr/3.jpg\" /></item>" +
                "<item><title>d</title><link>https://x.fr/4</link><enclosure url=\"http://img.x.fr/4.jpg\" type=\"image/jpeg\" /><media:content url=\"javascript:alert(1)\" medium=\"image\" /></item>" +
                "<item><title>e</title><link>https://x.fr/5</link></item>" +
                "</channel></rss>", "T");
            Banc.Verifie("image : enclosure", true, im.Count == 5 && im[0].Image == "https://img.x.fr/1.jpg");
            Banc.Verifie("image : media:content", true, im[1].Image == "https://img.x.fr/2.jpg");
            Banc.Verifie("image : audio ignoré, media:thumbnail en repli", true, im[2].Image == "https://img.x.fr/3.jpg");
            Banc.Verifie("image : http et javascript refusés", true, im[3].Image == null);
            Banc.Verifie("image : absente → null", true, im[4].Image == null && l[0].Image == null);
            Banc.Verifie("DOCTYPE avec entité : aucune expansion", true, !Actus.LitRss("<?xml version=\"1.0\"?><!DOCTYPE rss [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]><rss><channel><item><title>a&x;b</title><link>https://x.fr/1</link></item></channel></rss>", "T").Exists(a => a.Titre.Contains("fonts") || a.Titre.Length > 10));
            var autre = new List<Actu> { new Actu { Titre = "Doublon", Lien = "https://www.gamekult.com/a/2", Source = "B", Date = new DateTime(2026, 11, 21, 11, 0, 0, DateTimeKind.Utc) }, new Actu { Titre = "Récent", Lien = "https://x.fr/r", Source = "B", Date = new DateTime(2026, 11, 21, 11, 30, 0, DateTimeKind.Utc) } };
            var fus = Actus.Fusionne(new[] { l, autre }, 10);
            Banc.Verifie("fusion : sans doublon, récent d'abord, sans date en dernier", true, fus.Count == 4 && fus[0].Titre == "Récent" && fus[3].Titre == "Sans date");
            Banc.Verifie("fusion : plafond", true, Actus.Fusionne(new[] { l, autre }, 2).Count == 2);
            Banc.Verifie("filtre texte sans accent", true, Actus.Filtre(fus, "ete", null).Count == 1);
            Banc.Verifie("filtre source", true, Actus.Filtre(fus, null, "B").Count == 1 && Actus.Filtre(fus, "", "").Count == 4);
            DateTime n = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
            Banc.Verifie("âge", true, Actus.Age(n.AddSeconds(-20), n) == "il y a 1 min" && Actus.Age(n.AddMinutes(-12), n) == "il y a 12 min" && Actus.Age(n.AddHours(-3), n) == "il y a 3 h" && Actus.Age(n.AddHours(-30), n) == "hier" && Actus.Age(n.AddDays(-9), n) == "12/11" && Actus.Age(DateTime.MinValue, n) == "" && Actus.Age(n.AddHours(2), n) == "");
            Banc.Verifie("flux Steam", true, Actus.UrlFluxSteam(730) == "https://store.steampowered.com/feeds/news/app/730/?l=french");
            Banc.Verifie("4 sources, toutes en https", true, Actus.Sources.Length == 4 && Array.TrueForAll(Actus.Sources, s => s.Url.StartsWith("https://")));
            Banc.Verifie("favori d'actu", true, Actus.EnFavori(l[0]).Cle == l[0].Lien && Actus.EnFavori(l[0]).Titre == l[0].Titre);
            // Revue finale : le plafond vaut pour les sites ; les jeux suivis gardent leurs titres même plus anciens.
            var sites = new List<Actu>();
            for (int i = 0; i < 30; i++) sites.Add(new Actu { Titre = "S" + i, Lien = "https://s.fr/" + i, Source = "jeuxvideo.com", Date = new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc).AddMinutes(-i) });
            var jeu = new List<Actu>();
            for (int i = 0; i < 15; i++) jeu.Add(new Actu { Titre = "J" + i, Lien = "https://j.fr/" + i, Source = "CS2", Date = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-i) });
            var mix = Actus.FusionneAvecJeux(new[] { sites }, new[] { jeu }, 20, 10);
            Banc.Verifie("fusion : sites plafonnés, 10 titres par jeu suivi", true, mix.FindAll(a => a.Source == "jeuxvideo.com").Count == 20 && mix.FindAll(a => a.Source == "CS2").Count == 10);
            Banc.Verifie("fusion : du plus récent au plus ancien", true, mix[0].Titre == "S0" && mix[mix.Count - 1].Titre == "J9");
        }
    }
}
