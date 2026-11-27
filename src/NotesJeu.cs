using System;
using System.Collections.Generic;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// NOTE ET LIENS PAR JEU (logique PURE) : une appréciation sur 5, une note personnelle et quelques
    /// liens web (wiki, carte, guide…) que l'utilisateur attache à un jeu, et « je recommande ». Une ligne
    /// par jeu dans bt-jeux-notes.txt : « clé TAB étoiles TAB note TAB url titre TAB url titre… » ; un jeu
    /// recommandé porte « ,r » après ses étoiles (« 4,r »), si bien qu'un fichier sans recommandation
    /// reste celui des versions précédentes. La note garde
    /// ses retours à la ligne (codés
    /// « \n »). Seuls les liens http et https sont acceptés : un lien ne lance jamais un programme.
    /// Rien ne sort du PC : ONYX n'ouvre un lien que quand l'utilisateur clique dessus.
    /// </summary>
    internal static partial class NotesJeu
    {
        public const int MaxJeux = 500, MaxNote = 2000, MaxLiens = 12, MaxTitre = 60, MaxUrl = 500, MaxEtoiles = 5;

        public sealed class Lien { public string Titre = "", Url = ""; }

        public sealed class Fiche
        {
            public string Cle = "", Note = "";
            /// <summary>Appréciation personnelle : 1 à 5, 0 = pas noté.</summary>
            public int Etoiles;
            /// <summary>« Je recommande ce jeu ».</summary>
            public bool Recommande;
            public List<Lien> Liens = new List<Lien>();
            public bool Vide { get { return Etoiles == 0 && !Recommande && Note.Length == 0 && Liens.Count == 0; } }
            /// <summary>Ce que le bouton de la fiche annonce : la note compte pour un, chaque lien pour un.</summary>
            public int Nombre { get { return (Note.Length > 0 ? 1 : 0) + Liens.Count; } }
        }

        /// <summary>Appréciation bornée : 1 à 5, tout le reste vaut 0 (pas noté).</summary>
        public static int EtoilesPropres(int n) { return n >= 1 && n <= MaxEtoiles ? n : 0; }

        /// <summary>« 4/5 », ou vide si le jeu n'est pas noté.</summary>
        public static string TexteEtoiles(int n) { n = EtoilesPropres(n); return n == 0 ? "" : n + "/" + MaxEtoiles; }

        /// <summary>
        /// Adresse web propre, ou null avec la raison. « wiki.gg/x » devient « https://wiki.gg/x ».
        /// Refusés : tout ce qui n'est pas http/https (fichier, programme, protocole d'appli), les
        /// adresses sans hôte, et celles qui portent un identifiant (« nom:motdepasse@ »).
        /// </summary>
        public static string UrlPropre(string saisi, out string erreur)
        {
            erreur = null;
            string u = (saisi ?? "").Trim();
            if (u.Length == 0) { erreur = "L'adresse est vide."; return null; }
            foreach (char c in u) if (c <= 32 || c == '"' || c == '<' || c == '>') { erreur = "L'adresse contient une espace ou un caractère interdit."; return null; }
            if (u.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                // « C:\jeu.exe », « mailto:x », « steam:run » : pas des adresses web.
                int deux = u.IndexOf(':');
                int barre = u.IndexOf('/');
                if (deux >= 0 && (barre < 0 || deux < barre) && !SuiviDUnPort(u, deux)) { erreur = "Seules les adresses web (http ou https) sont acceptées."; return null; }
                u = "https://" + u;
            }
            if (u.Length > MaxUrl) { erreur = "L'adresse dépasse " + MaxUrl + " caractères."; return null; }
            Uri uri;
            if (!Uri.TryCreate(u, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            { erreur = "Seules les adresses web (http ou https) sont acceptées."; return null; }
            if (string.IsNullOrEmpty(uri.Host) || uri.Host.IndexOf('.') < 0) { erreur = "L'adresse n'a pas de nom de site."; return null; }
            if (!string.IsNullOrEmpty(uri.UserInfo)) { erreur = "Une adresse qui contient un identifiant n'est pas enregistrée."; return null; }
            return u;
        }

        /// <summary>« site.fr:8080/x » : les deux-points annoncent un port, pas un protocole.</summary>
        private static bool SuiviDUnPort(string u, int deux)
        {
            int i = deux + 1, n = 0;
            while (i < u.Length && u[i] >= '0' && u[i] <= '9') { i++; n++; }
            return n > 0 && (i == u.Length || u[i] == '/') && u.Substring(0, deux).IndexOf('.') > 0;
        }

        /// <summary>Titre proposé pour un lien : le nom du site sans « www. ».</summary>
        public static string TitreParDefaut(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url ?? "", UriKind.Absolute, out uri)) return "";
            string h = uri.Host;
            return h.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? h.Substring(4) : h;
        }

        public static string TitrePropre(string s)
        {
            string t = (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
            return t.Length > MaxTitre ? t.Substring(0, MaxTitre).Trim() : t;
        }

        /// <summary>Note prête à être gardée : fins de ligne unifiées, tabulations retirées, longueur bornée.</summary>
        public static string NotePropre(string s)
        {
            string n = (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ').Trim();
            return n.Length > MaxNote ? n.Substring(0, MaxNote).Trim() : n;
        }

        /// <summary>Note sur une seule ligne de fichier : « \ » devient « \\ », le retour à la ligne « \n ».</summary>
        public static string Code(string note)
        {
            return (note ?? "").Replace("\\", "\\\\").Replace("\n", "\\n");
        }

        public static string Decode(string code)
        {
            var sb = new StringBuilder();
            string c = code ?? "";
            for (int i = 0; i < c.Length; i++)
            {
                if (c[i] == '\\' && i + 1 < c.Length)
                {
                    i++;
                    sb.Append(c[i] == 'n' ? '\n' : c[i]);
                }
                else sb.Append(c[i]);
            }
            return sb.ToString();
        }

        public static List<Fiche> Lire(string texte)
        {
            var res = new List<Fiche>();
            if (string.IsNullOrEmpty(texte)) return res;
            var vues = new HashSet<string>();
            foreach (string brute in texte.Split('\n'))
            {
                string[] c = brute.TrimEnd('\r').Split('\t');
                if (c.Length < 3 || c[0].Length == 0 || !vues.Add(c[0])) continue;
                int etoiles, virgule = c[1].IndexOf(',');
                int.TryParse(virgule < 0 ? c[1] : c[1].Substring(0, virgule), out etoiles);
                var f = new Fiche { Cle = c[0], Etoiles = EtoilesPropres(etoiles), Note = NotePropre(Decode(c[2])) };
                f.Recommande = virgule >= 0 && c[1].Substring(virgule + 1).Trim() == "r";
                for (int i = 3; i < c.Length && f.Liens.Count < MaxLiens; i++)
                {
                    int esp = c[i].IndexOf(' ');
                    string erreur, url = UrlPropre(esp < 0 ? c[i] : c[i].Substring(0, esp), out erreur);
                    if (url == null) continue;   // lien modifié à la main et devenu invalide : ignoré
                    string titre = TitrePropre(esp < 0 ? "" : c[i].Substring(esp + 1));
                    f.Liens.Add(new Lien { Url = url, Titre = titre.Length > 0 ? titre : TitreParDefaut(url) });
                }
                if (!f.Vide && res.Count < MaxJeux) res.Add(f);
            }
            return res;
        }

        public static string Ecrire(IEnumerable<Fiche> l)
        {
            var sb = new StringBuilder();
            if (l == null) return "";
            foreach (Fiche f in l)
            {
                if (f == null || f.Cle.Length == 0 || f.Vide) continue;
                sb.Append(f.Cle).Append('\t').Append(EtoilesPropres(f.Etoiles)).Append(f.Recommande ? ",r" : "").Append('\t').Append(Code(NotePropre(f.Note)));
                foreach (Lien k in f.Liens) sb.Append('\t').Append(k.Url).Append(' ').Append(TitrePropre(k.Titre));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Note et liens du jeu, ou null s'il n'en a pas.</summary>
        public static Fiche Trouve(List<Fiche> l, string nomJeu)
        {
            string cle = BibliothequeJeux.Cle(nomJeu);
            if (l == null || cle.Length == 0) return null;
            return l.Find(x => x.Cle == cle);
        }

        /// <summary>
        /// Remplace l'appréciation, la recommandation, la note et les liens du jeu ; tout vide les retire. Les
        /// liens invalides ou en double sont écartés. Faux si la liste des jeux est pleine.
        /// </summary>
        public static bool Pose(List<Fiche> l, string nomJeu, int etoiles, string note, IEnumerable<Lien> liens, bool recommande = false)
        {
            string cle = BibliothequeJeux.Cle(nomJeu);
            if (l == null || cle.Length == 0) return false;
            var f = new Fiche { Cle = cle, Etoiles = EtoilesPropres(etoiles), Recommande = recommande, Note = NotePropre(note) };
            if (liens != null)
                foreach (Lien k in liens)
                {
                    if (k == null || f.Liens.Count >= MaxLiens) continue;
                    string erreur, url = UrlPropre(k.Url, out erreur);
                    if (url == null || f.Liens.Exists(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase))) continue;
                    string titre = TitrePropre(k.Titre);
                    f.Liens.Add(new Lien { Url = url, Titre = titre.Length > 0 ? titre : TitreParDefaut(url) });
                }
            int i = l.FindIndex(x => x.Cle == cle);
            if (f.Vide) { if (i >= 0) l.RemoveAt(i); return true; }
            if (i >= 0) { l[i] = f; return true; }
            if (l.Count >= MaxJeux) return false;
            l.Add(f);
            return true;
        }
    }
}
