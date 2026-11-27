using System;
using System.Collections.Generic;
using BTOptimizer;

namespace BTOptimizer.Tests
{
    /// <summary>Carte postale du bilan : texte, HTML et BBCode à coller.</summary>
    internal static class CartePostaleTests
    {
        public static void Tout()
        {
            Banc.Titre("Carte postale du bilan");
            var maintenant = new DateTime(2026, 11, 28, 22, 0, 0);
            var parties = new List<JournalJeu.Partie>
            {
                new JournalJeu.Partie { Jeu = "Fortnite", Debut = maintenant.AddHours(-3), Secondes = 7200 },
                new JournalJeu.Partie { Jeu = "Fortnite", Debut = maintenant.AddDays(-1), Secondes = 3600 },
                new JournalJeu.Partie { Jeu = "Tom & Jerry <Arena> [Beta]", Debut = maintenant.AddDays(-2), Secondes = 1800 },
            };
            BilanJeu.Resultat r = BilanJeu.Calcule(parties, maintenant, 7);
            CartePostale.Contenu c = CartePostale.Prepare(r, "4 succès Steam débloqués.", new[] { "Hades II", "hades ii", " ", "Cyberpunk 2077" });
            Banc.Egal("jeux les plus joués, du plus au moins joué", "2", c.Jeux.Count.ToString());
            Banc.Verifie("premier : Fortnite avec son détail", true, c.Jeux[0].StartsWith("Fortnite — "));
            Banc.Egal("favoris : vides et doublons écartés", "Hades II|Cyberpunk 2077", string.Join("|", c.Favoris));
            Banc.Verifie("succès ajouté aux faits, sans point final", true, c.Faits.Contains("4 succès Steam débloqués"));

            string html = CartePostale.Html(c);
            Banc.Verifie("HTML : caractères spéciaux échappés", true, html.Contains("Tom &amp; Jerry &lt;Arena&gt;") && !html.Contains("<Arena>"));
            Banc.Verifie("HTML : aucune image ni adresse externe", true, !html.Contains("<img") && !html.Contains("http") && !html.Contains("url("));
            Banc.Verifie("HTML : liste ordonnée des jeux", true, html.Contains("<ol") && html.Contains("</ol>"));

            string bb = CartePostale.BBCode(c);
            Banc.Verifie("BBCode : crochets d'un nom neutralisés", true, bb.Contains("Tom & Jerry <Arena> (Beta)") && !bb.Contains("[Beta]"));
            Banc.Verifie("BBCode : balises équilibrées", true, Compte(bb, "[list") == Compte(bb, "[/list]") && Compte(bb, "[b]") == Compte(bb, "[/b]"));
            Banc.Verifie("BBCode : signature", true, bb.EndsWith("[i]" + CartePostale.Signature + "[/i]"));

            string t = CartePostale.Texte(c);
            Banc.Verifie("texte : titre, total et classement", true, t.StartsWith(BilanJeu.CarteTitre(r)) && t.Contains("1. Fortnite") && t.Contains("Jeux favoris : Hades II, Cyberpunk 2077"));

            CartePostale.Contenu vide = CartePostale.Prepare(BilanJeu.Calcule(new List<JournalJeu.Partie>(), maintenant, 7), null, null);
            Banc.Verifie("période vide : texte lisible, sans liste", true, vide.Vide && !CartePostale.BBCode(vide).Contains("[list") && CartePostale.Texte(vide).Contains("Aucune partie"));
        }

        private static int Compte(string s, string motif)
        {
            int n = 0, i = 0;
            while ((i = s.IndexOf(motif, i, StringComparison.Ordinal)) >= 0) { n++; i += motif.Length; }
            return n;
        }
    }
}
