using System;
using System.Collections.Generic;

namespace BTOptimizer
{
    /// <summary>
    /// AFFICHAGE DE LA BIBLIOTHÈQUE (logique PURE) : grille de jaquettes ou liste, et, en liste,
    /// regroupement par plateforme, par étiquette, par dernière partie, par genre ou par éditeur. Le choix est gardé dans
    /// bt-jeux-vue.txt (« liste;plateforme »). L'ordre DANS un groupe reste celui de la bibliothèque
    /// (favoris d'abord, puis le tri choisi) : le regroupement ne fait que découper.
    /// </summary>
    internal static class VueJeux
    {
        public enum Groupe { Aucun, Plateforme, Etiquette, DernierePartie, Genre, Editeur }

        public sealed class Choix
        {
            public bool Liste;
            public Groupe Par = Groupe.Aucun;
        }

        /// <summary>Ce qu'il faut savoir d'un jeu pour le ranger.</summary>
        public sealed class Jeu
        {
            public string Plateforme = "";
            public List<string> Etiquettes = new List<string>();
            public DateTime? Derniere;
            /// <summary>Genre et éditeur de la fiche Steam ; null si le jeu n'en a pas (ou pas encore).</summary>
            public string Genre, Editeur;
        }

        public sealed class Bloc
        {
            public string Titre = "";
            /// <summary>Positions des jeux dans la liste d'entrée, dans leur ordre d'origine.</summary>
            public List<int> Jeux = new List<int>();
        }

        private static readonly string[] Codes = { "aucun", "plateforme", "etiquette", "derniere", "genre", "editeur" };

        public static readonly string[] Libelles = { "Aucun", "Plateforme", "Étiquette", "Dernière partie", "Genre", "Éditeur" };

        public static Choix Lit(string texte)
        {
            var c = new Choix();
            string[] p = (texte ?? "").Trim().Split(';');
            c.Liste = string.Equals(p[0].Trim(), "liste", StringComparison.OrdinalIgnoreCase);
            if (p.Length > 1)
            {
                int i = Array.IndexOf(Codes, p[1].Trim().ToLowerInvariant());
                if (i > 0) c.Par = (Groupe)i;
            }
            return c;
        }

        public static string Ecrit(Choix c)
        {
            if (c == null) return "grille;aucun";
            return (c.Liste ? "liste" : "grille") + ";" + Codes[(int)c.Par];
        }

        /// <summary>Texte du bouton : « Grille », « Liste », « Liste · Plateforme ».</summary>
        public static string Bouton(Choix c)
        {
            if (c == null || !c.Liste) return "Grille";
            return c.Par == Groupe.Aucun ? "Liste" : "Liste · " + Libelles[(int)c.Par];
        }

        /// <summary>Tranche « dernière partie » d'un jeu, dans l'ordre d'affichage (0 = la plus récente).</summary>
        public static int Tranche(DateTime? derniere, DateTime maintenant)
        {
            if (derniere == null) return 4;
            int jours = (int)(maintenant.Date - derniere.Value.Date).TotalDays;
            return jours <= 0 ? 0 : jours < 7 ? 1 : jours < 30 ? 2 : 3;
        }

        public static readonly string[] Tranches = { "Aujourd'hui", "7 derniers jours", "30 derniers jours", "Plus ancien", "Jamais lancé avec ONYX ouvert" };

        /// <summary>
        /// Découpe la liste (déjà triée) en blocs titrés « Steam · 12 ». Sans regroupement : un seul
        /// bloc sans titre. Par étiquette, un jeu paraît sous chacune de ses étiquettes, et ceux qui
        /// n'en ont pas finissent dans « Sans étiquette ». Les blocs vides ne sont pas rendus.
        /// </summary>
        public static List<Bloc> Range(List<Jeu> jeux, Groupe par, DateTime maintenant)
        {
            var res = new List<Bloc>();
            if (jeux == null) return res;
            if (par == Groupe.Aucun)
            {
                var b = new Bloc();
                for (int i = 0; i < jeux.Count; i++) b.Jeux.Add(i);
                if (b.Jeux.Count > 0) res.Add(b);
                return res;
            }

            var parCle = new Dictionary<string, Bloc>(StringComparer.CurrentCultureIgnoreCase);
            var ordre = new List<string>();
            Bloc sans = null;
            var tranches = new Bloc[Tranches.Length];
            for (int i = 0; i < jeux.Count; i++)
            {
                Jeu j = jeux[i] ?? new Jeu();
                if (par == Groupe.DernierePartie)
                {
                    int t = Tranche(j.Derniere, maintenant);
                    if (tranches[t] == null) tranches[t] = new Bloc { Titre = Tranches[t] };
                    tranches[t].Jeux.Add(i);
                    continue;
                }
                var cles = new List<string>();
                if (par == Groupe.Plateforme) cles.Add(string.IsNullOrWhiteSpace(j.Plateforme) ? "Autres" : j.Plateforme.Trim());
                else if (par == Groupe.Genre || par == Groupe.Editeur)
                {
                    string v = par == Groupe.Genre ? j.Genre : j.Editeur;
                    if (!string.IsNullOrWhiteSpace(v)) cles.Add(v.Trim());
                }
                else if (j.Etiquettes != null)
                    foreach (string e in j.Etiquettes)
                        if (!string.IsNullOrWhiteSpace(e) && !cles.Exists(x => string.Equals(x, e.Trim(), StringComparison.CurrentCultureIgnoreCase))) cles.Add(e.Trim());
                if (cles.Count == 0)
                {
                    if (sans == null) sans = new Bloc { Titre = par == Groupe.Genre ? "Genre inconnu" : par == Groupe.Editeur ? "Éditeur inconnu" : "Sans étiquette" };
                    sans.Jeux.Add(i);
                    continue;
                }
                foreach (string cle in cles)
                {
                    Bloc b;
                    if (!parCle.TryGetValue(cle, out b)) { b = new Bloc { Titre = (par == Groupe.Etiquette ? "# " : "") + cle }; parCle[cle] = b; ordre.Add(cle); }
                    b.Jeux.Add(i);
                }
            }

            if (par == Groupe.DernierePartie)
            {
                foreach (Bloc b in tranches) if (b != null) res.Add(b);
            }
            else
            {
                ordre.Sort(StringComparer.CurrentCultureIgnoreCase);
                foreach (string cle in ordre) res.Add(parCle[cle]);
                if (sans != null) res.Add(sans);
            }
            foreach (Bloc b in res) b.Titre += "  ·  " + b.Jeux.Count;
            return res;
        }
    }
}
