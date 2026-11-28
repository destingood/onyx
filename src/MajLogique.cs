using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// Logique PURE de la mise à jour (sans réseau ni interface), testée hors ligne :
    /// fréquence de vérification, empreinte SHA256, notes lisibles, notification unique par version.
    /// </summary>
    internal static class MajLogique
    {
        /// <summary>
        /// Canal bêta : parmi les releases GitHub (liste /releases), l'index de la plus récente à
        /// proposer, ou -1. Jamais un brouillon ; une préversion seulement si le canal bêta est choisi.
        /// À version égale, la version finale passe devant la préversion.
        /// </summary>
        public static int ChoisitRelease(Version[] versions, bool[] preversions, bool[] brouillons, bool beta)
        {
            int meilleur = -1;
            if (versions == null) return -1;
            for (int i = 0; i < versions.Length; i++)
            {
                if (versions[i] == null) continue;
                bool pre = preversions != null && i < preversions.Length && preversions[i];
                bool brouillon = brouillons != null && i < brouillons.Length && brouillons[i];
                if (brouillon || (pre && !beta)) continue;
                if (meilleur < 0) { meilleur = i; continue; }
                int c = versions[i].CompareTo(versions[meilleur]);
                bool preMeilleur = preversions != null && meilleur < preversions.Length && preversions[meilleur];
                if (c > 0 || (c == 0 && preMeilleur && !pre)) meilleur = i;
            }
            return meilleur;
        }

        /// <summary>ONYX reste ouvert toute la journée dans la zone de notification : une seule
        /// vérification par jour faisait attendre jusqu'au lendemain une version publiée le soir.
        /// 4 requêtes par jour restent très loin de la limite de GitHub (60 par heure).</summary>
        public static readonly TimeSpan Intervalle = TimeSpan.FromHours(6);

        /// <summary>Paramètres de l'installateur Inno Setup lancé par ONYX : pas d'assistant (une
        /// simple barre de progression), pas de question, pas de redémarrage, fermeture des
        /// fichiers encore ouverts, et RELANCE=1 pour que l'installateur rouvre ONYX à la fin.
        /// RELANCE n'est pas passé par winget : une installation silencieuse de winget ne doit
        /// pas ouvrir l'application.</summary>
        public const string ArgsSilencieux = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELANCE=1";

        /// <summary>Installation automatique : aucune fenêtre du tout (ONYX est réduit, l'utilisateur
        /// fait autre chose), puis ONYX est rouvert — réduit lui aussi, voir RetourDiscret.</summary>
        public const string ArgsDiscrets = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELANCE=1";

        /// <summary>Dossier, sous %TEMP%, où l'installateur est téléchargé et où le ménage le retire.</summary>
        public const string DossierMaj = "ONYX-maj";

        private const string Format = "yyyy-MM-dd HH:mm";

        public static string Horodatage(DateTime t) { return t.ToString(Format, CultureInfo.InvariantCulture); }

        /// <summary>Une vérification est-elle due ? Accepte aussi l'ancien tampon « yyyyMMdd ».
        /// Un tampon dans le futur (horloge reculée) ne doit pas bloquer les vérifications.</summary>
        public static bool EstDue(string tampon, DateTime maintenant)
        {
            if (string.IsNullOrWhiteSpace(tampon)) return true;
            string s = tampon.Trim();
            DateTime t;
            if (!DateTime.TryParseExact(s, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)
                && !DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                return true;
            if (t > maintenant.AddHours(1)) return true;
            return maintenant - t >= Intervalle;
        }

        /// <summary>« sha256:&lt;64 hex&gt; » (champ digest de GitHub) ou 64 hex bruts → hex en
        /// minuscules ; null pour tout le reste.</summary>
        public static string Sha256DuDigest(string digest)
        {
            if (string.IsNullOrWhiteSpace(digest)) return null;
            string s = digest.Trim();
            if (s.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7);
            else if (s.Contains(":")) return null;
            if (s.Length != 64) return null;
            foreach (char c in s)
                if (!Uri.IsHexDigit(c)) return null;
            return s.ToLowerInvariant();
        }

        /// <summary>Le fichier a-t-il l'empreinte annoncée ? Sans empreinte annoncée (manifeste
        /// personnel qui n'en donne pas), il n'y a rien à contrôler : true.</summary>
        public static bool EmpreinteOk(string chemin, string attendu)
        {
            if (string.IsNullOrEmpty(attendu)) return true;
            try
            {
                using (var fs = File.OpenRead(chemin))
                {
                    string h = Convert.ToHexString(SHA256.HashData(fs));
                    return string.Equals(h, attendu.Trim(), StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        /// <summary>Notes de version (Markdown de GitHub) → texte brut pour une fenêtre Windows.
        /// La section « Installation » (consignes de téléchargement manuel, nombre de tests) ne
        /// concerne pas quelqu'un qui met à jour depuis ONYX : elle est retirée.</summary>
        public static string NotesLisibles(string md, int max)
        {
            if (string.IsNullOrWhiteSpace(md)) return "";
            var sb = new StringBuilder();
            bool vide = true;   // la dernière ligne écrite est vide (ou rien encore)
            // Les commentaires HTML ne s'affichent pas sur GitHub : ils portent les marques lues par
            // ONYX (mise à jour importante) et n'ont rien à faire dans une fenêtre.
            foreach (string brute in Regex.Replace(md, "<!--.*?-->", "", RegexOptions.Singleline).Replace("\r", "").Split('\n'))
            {
                string l = brute.Trim();
                bool titre = l.StartsWith("#");
                if (titre) l = l.TrimStart('#').Trim();
                if (titre && l.StartsWith("Installation", StringComparison.OrdinalIgnoreCase)) break;
                if (l.StartsWith("- ") || l.StartsWith("* ")) l = "• " + l.Substring(2).Trim();
                l = Regex.Replace(l, @"\[([^\]]*)\]\([^)]*\)", "$1");
                l = l.Replace("**", "").Replace("__", "").Replace("`", "");
                if (l.Length == 0)
                {
                    if (!vide) { sb.Append('\n'); vide = true; }
                    continue;
                }
                sb.Append(l).Append('\n');
                vide = false;
            }
            string s = sb.ToString().TrimEnd('\n');
            if (s.Length <= max) return s;
            int coupe = s.LastIndexOf('\n', Math.Max(0, max - 1));
            if (coupe <= 0) coupe = Math.Max(0, max - 1);
            return s.Substring(0, coupe).TrimEnd() + "\n…";
        }

        /// <summary>La notification Windows se montre UNE fois par version : la vérification
        /// toutes les 6 heures ne doit pas la répéter. Le bandeau dans l'app reste, lui.</summary>
        public static bool ANotifier(string dispo, string dejaNotifiee)
        {
            if (string.IsNullOrWhiteSpace(dispo)) return false;
            return !string.Equals(dispo.Trim(), (dejaNotifiee ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>« 12,3 / 55,3 Mo — 22 % », ou « 5,0 Mo » si la taille totale est inconnue.</summary>
        public static string Progression(long fait, long total)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            string f = (fait / 1048576.0).ToString("0.0", fr);
            if (total <= 0) return f + " Mo";
            int pct = (int)Math.Min(100, fait * 100 / total);
            return f + " / " + (total / 1048576.0).ToString("0.0", fr) + " Mo — " + pct + " %";
        }

        // ------------------------------------------------------------------ fichier à télécharger

        /// <summary>
        /// Lequel des fichiers d'une release est l'installateur à télécharger ? 3 : l'installateur
        /// complet (« ONYX-Setup-16.14.0.0.exe ») ; 2 : un autre programme dont le nom contient
        /// « setup » ; 1 : un autre programme ; 0 : pas un programme. L'installateur léger
        /// (« ONYX-Installation.exe ») ne fait que télécharger le complet : il ne doit jamais être
        /// pris pour la mise à jour, quel que soit l'ordre des fichiers de la release.
        /// </summary>
        public static int RangInstallateur(string nom)
        {
            if (string.IsNullOrEmpty(nom) || !nom.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return 0;
            if (nom.StartsWith("ONYX-Setup-", StringComparison.OrdinalIgnoreCase)) return 3;
            return nom.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 1;
        }

        // ------------------------------------------------------------------ ignorer, importante

        /// <summary>« v16.14 », « 16.14.0.0 », « ONYX 16.14 » → « 16.14 » ; null si illisible.</summary>
        private static string Numero(string version)
        {
            if (string.IsNullOrEmpty(version)) return null;
            Match m = Regex.Match(version, @"(\d{1,6})\.(\d{1,6})");
            return m.Success ? int.Parse(m.Groups[1].Value) + "." + int.Parse(m.Groups[2].Value) : null;
        }

        /// <summary>Même version publiée (Majeur.Mineur), quelle que soit l'écriture.</summary>
        public static bool MemeVersion(string a, string b)
        {
            string x = Numero(a);
            return x != null && x == Numero(b);
        }

        /// <summary>« Ignorer cette version » ne vaut que pour CETTE version : la suivante est de
        /// nouveau proposée.</summary>
        public static bool EstIgnoree(string dispo, string ignoree) { return MemeVersion(dispo, ignoree); }

        /// <summary>Mise à jour importante (correctif de sécurité ou de fiabilité) : les notes de la
        /// release portent le commentaire « &lt;!-- onyx:obligatoire --&gt; », invisible sur GitHub.
        /// Elle ne peut pas être ignorée ; elle n'est pas installée de force pour autant.</summary>
        public static bool EstObligatoire(string notes)
        {
            return !string.IsNullOrEmpty(notes) && Regex.IsMatch(notes, @"<!--\s*onyx:obligatoire\s*-->", RegexOptions.IgnoreCase);
        }

        // ------------------------------------------------------------------ espace disque

        /// <summary>Place à avoir avant de télécharger : l'installateur, l'application qu'il pose
        /// (à peu près deux fois sa taille avec ce qu'elle décompresse au premier lancement), et
        /// 200 Mo de marge — un disque plein à ras bord en pleine installation laisse un ONYX cassé.</summary>
        public static long EspaceRequis(long taille) { return Math.Max(0L, taille) * 3 + 200L * 1048576; }

        /// <summary>Espace libre inconnu (négatif) : on ne bloque pas sur un doute.</summary>
        public static bool EspaceSuffisant(long libre, long taille) { return libre < 0 || libre >= EspaceRequis(taille); }

        public static string TexteEspace(long libre, long taille)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            return "Pas assez de place sur le disque : il faut " + (EspaceRequis(taille) / 1048576.0).ToString("0", fr)
                 + " Mo libres, il en reste " + (Math.Max(0L, libre) / 1048576.0).ToString("0", fr) + ". Rien n'a été téléchargé.";
        }

        // ------------------------------------------------------------------ installation automatique

        /// <summary>L'installation automatique n'a lieu que si l'option est cochée, que le fichier
        /// vient de GitHub et que son empreinte est publiée : sans empreinte, rien ne prouve que le
        /// fichier reçu est celui qui a été publié, et personne n'est devant l'écran pour en juger.</summary>
        public static bool AutoPossible(bool auto, bool lienGitHub, string sha256)
        {
            return auto && lienGitHub && !string.IsNullOrEmpty(sha256);
        }

        /// <summary>Installer maintenant, sans rien demander ? Seulement quand ONYX est réduit dans
        /// la zone de notification, qu'aucun jeu ne tourne et que la fenêtre de mise à jour n'est pas
        /// ouverte : ONYX se ferme une minute, ses services en jeu avec lui.</summary>
        public static bool MomentPropice(bool fenetreVisible, bool jeuEnCours, bool fenetreMajOuverte)
        {
            return !fenetreVisible && !jeuEnCours && !fenetreMajOuverte;
        }

        /// <summary>Marque écrite juste avant une installation automatique : « 16.14|2026-11-28 12:00 ».</summary>
        public static string MarqueRetour(string version, DateTime t) { return (version ?? "").Trim() + "|" + Horodatage(t); }

        /// <summary>
        /// ONYX redémarre-t-il après une installation automatique ? Alors il revient réduit, comme
        /// il l'était. Renvoie la version installée, ou null : marque absente ou illisible, vieille
        /// de plus de 15 minutes (l'installation prend une minute), ou version qui tourne différente
        /// de celle annoncée (l'installation n'a pas eu lieu).
        /// </summary>
        public static string RetourDiscret(string marque, DateTime maintenant, string versionCourante)
        {
            if (string.IsNullOrWhiteSpace(marque)) return null;
            string[] p = marque.Trim().Split('|');
            if (p.Length != 2) return null;
            DateTime t;
            if (!DateTime.TryParseExact(p[1].Trim(), Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return null;
            double minutes = (maintenant - t).TotalMinutes;
            if (minutes < -1 || minutes > 15) return null;
            return MemeVersion(p[0], versionCourante) ? p[0].Trim() : null;
        }
    }
}
