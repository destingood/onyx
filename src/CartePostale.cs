using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// CARTE POSTALE DU BILAN (logique PURE) : le bilan de jeu de la période en texte à coller
    /// (texte simple, code HTML pour un site, BBCode pour un forum). Temps total, faits, jeux les
    /// plus joués, succès et jeux favoris. Aucune image ni adresse externe : rien ne sort du PC
    /// tant que l'utilisateur ne colle pas le texte lui-même.
    /// </summary>
    internal static class CartePostale
    {
        public const int MaxJeux = 5, MaxFavoris = 8;
        public const string Signature = "Fait avec ONYX";

        public sealed class Contenu
        {
            public string Titre, Duree, Sous;
            public List<string> Faits = new List<string>();
            public List<string> Jeux = new List<string>();
            public List<string> Favoris = new List<string>();
            public bool Vide;
        }

        public static Contenu Prepare(BilanJeu.Resultat r, string ligneSucces, IEnumerable<string> favoris)
        {
            var c = new Contenu { Titre = BilanJeu.CarteTitre(r), Sous = BilanJeu.CarteSous(r), Vide = r == null || r.Parties == 0 };
            c.Duree = c.Vide ? "" : JournalJeu.Duree(r.Secondes);
            c.Faits.AddRange(BilanJeu.CarteFaits(r));
            if (!string.IsNullOrWhiteSpace(ligneSucces)) c.Faits.Add(ligneSucces.Trim().TrimEnd('.'));
            if (r != null)
                foreach (BilanJeu.Ligne g in r.Jeux)
                {
                    if (c.Jeux.Count == MaxJeux) break;
                    if (g != null && !string.IsNullOrWhiteSpace(g.Jeu)) c.Jeux.Add(g.Jeu.Trim() + " — " + BilanJeu.Detail(g));
                }
            if (favoris != null)
            {
                var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string f in favoris)
                {
                    if (c.Favoris.Count == MaxFavoris) break;
                    if (!string.IsNullOrWhiteSpace(f) && vus.Add(f.Trim())) c.Favoris.Add(f.Trim());
                }
            }
            return c;
        }

        private static string Total(Contenu c)
        {
            return c.Vide ? char.ToUpperInvariant(c.Sous[0]) + c.Sous.Substring(1) : c.Duree + " " + c.Sous;
        }

        public static string Texte(Contenu c)
        {
            var sb = new StringBuilder();
            sb.Append(c.Titre).Append("\r\n").Append(Total(c)).Append("\r\n");
            foreach (string f in c.Faits) sb.Append("- ").Append(f).Append("\r\n");
            if (c.Jeux.Count > 0)
            {
                sb.Append("\r\nJeux les plus joués :\r\n");
                for (int i = 0; i < c.Jeux.Count; i++) sb.Append(i + 1).Append(". ").Append(c.Jeux[i]).Append("\r\n");
            }
            if (c.Favoris.Count > 0) sb.Append("\r\nJeux favoris : ").Append(string.Join(", ", c.Favoris)).Append("\r\n");
            sb.Append("\r\n").Append(Signature);
            return sb.ToString();
        }

        public static string Html(Contenu c)
        {
            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:'Segoe UI',Arial,sans-serif;background:#141414;color:#eeeeee;padding:16px 20px;border-radius:10px;max-width:560px\">\n");
            sb.Append("<div style=\"color:#e8b44a;font-weight:600\">").Append(H(c.Titre)).Append("</div>\n");
            sb.Append("<p style=\"margin:6px 0 10px;font-size:18px\">");
            if (c.Vide) sb.Append(H(Total(c)));
            else sb.Append("<b>").Append(H(c.Duree)).Append("</b> ").Append(H(c.Sous));
            sb.Append("</p>\n");
            if (c.Faits.Count > 0)
            {
                sb.Append("<ul style=\"margin:0 0 10px;padding-left:20px\">\n");
                foreach (string f in c.Faits) sb.Append("<li>").Append(H(f)).Append("</li>\n");
                sb.Append("</ul>\n");
            }
            if (c.Jeux.Count > 0)
            {
                sb.Append("<div style=\"font-weight:600\">Jeux les plus joués</div>\n<ol style=\"margin:4px 0 10px;padding-left:22px\">\n");
                foreach (string j in c.Jeux) sb.Append("<li>").Append(H(j)).Append("</li>\n");
                sb.Append("</ol>\n");
            }
            if (c.Favoris.Count > 0)
                sb.Append("<p style=\"margin:0 0 10px\"><b>Jeux favoris :</b> ").Append(H(string.Join(", ", c.Favoris))).Append("</p>\n");
            sb.Append("<div style=\"font-size:12px;color:#999999\">").Append(H(Signature)).Append("</div>\n</div>");
            return sb.ToString();
        }

        public static string BBCode(Contenu c)
        {
            var sb = new StringBuilder();
            sb.Append("[b]").Append(B(c.Titre)).Append("[/b]\n");
            if (c.Vide) sb.Append(B(Total(c))).Append('\n');
            else sb.Append("[b]").Append(B(c.Duree)).Append("[/b] ").Append(B(c.Sous)).Append('\n');
            if (c.Faits.Count > 0)
            {
                sb.Append("[list]\n");
                foreach (string f in c.Faits) sb.Append("[*]").Append(B(f)).Append('\n');
                sb.Append("[/list]\n");
            }
            if (c.Jeux.Count > 0)
            {
                sb.Append("[b]Jeux les plus joués[/b]\n[list=1]\n");
                foreach (string j in c.Jeux) sb.Append("[*]").Append(B(j)).Append('\n');
                sb.Append("[/list]\n");
            }
            if (c.Favoris.Count > 0) sb.Append("[b]Jeux favoris :[/b] ").Append(B(string.Join(", ", c.Favoris))).Append('\n');
            sb.Append("[i]").Append(Signature).Append("[/i]");
            return sb.ToString();
        }

        private static string H(string s) { return WebUtility.HtmlEncode(s ?? ""); }

        /// <summary>Un crochet dans un nom de jeu ouvrirait une balise : remplacé par une parenthèse.</summary>
        private static string B(string s) { return (s ?? "").Replace('[', '(').Replace(']', ')'); }
    }
}
