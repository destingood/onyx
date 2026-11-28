using System;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// « UNE MISE À JOUR M'ATTEND » — la mémoire entre deux lancements.
    ///
    /// La notification Windows est fugace : elle passe pendant que l'utilisateur n'est pas devant
    /// l'écran, ou n'apparaît pas du tout si les notifications sont coupées (fréquent chez les
    /// joueurs, justement). La vérification, elle, n'a lieu qu'une fois par jour — sans mémoire,
    /// l'information est perdue jusqu'au lendemain.
    ///
    /// On note donc la version disponible dans un fichier, et l'app l'affiche À CHAQUE ouverture
    /// tant qu'elle n'est pas installée. Le drapeau se retire tout seul dès que la version qui
    /// tourne rattrape celle qui était annoncée.
    /// </summary>
    internal static class UpdateFlag
    {
        private static string Chemin { get { return AppPaths.File("bt-maj-dispo.txt"); } }

        /// <summary>
        /// Note qu'une version est disponible (format « 15.63 »). Si c'est la MÊME version qu'avant,
        /// on conserve la marque « déjà annoncée » : sans ça, la vérification quotidienne
        /// réafficherait la fenêtre chaque jour pour une mise à jour que l'utilisateur a vue et
        /// choisi de reporter. Une nouvelle version, elle, remet le compteur à zéro.
        /// </summary>
        public static void Set(string version)
        {
            try
            {
                if (string.IsNullOrEmpty(version)) return;
                string v = version.Trim();
                bool garderMarque = false;
                try
                {
                    if (File.Exists(Chemin))
                    {
                        string[] l = File.ReadAllText(Chemin).Replace("\r", "").Split('\n');
                        garderMarque = l.Length > 0 && string.Equals(l[0].Trim(), v, StringComparison.OrdinalIgnoreCase)
                                    && l.Length > 1 && l[1].Trim() == "vu";
                    }
                }
                catch { }
                File.WriteAllText(Chemin, v + "\n" + (garderMarque ? "vu\n" : ""));
            }
            catch { }
        }

        /// <summary>Version à ANNONCER en grand : disponible, et pas encore montrée à l'utilisateur.
        /// null s'il n'y a rien, ou si on la lui a déjà présentée.</summary>
        public static string AAnnoncer()
        {
            try
            {
                string v = EnAttente();
                if (v == null) return null;
                string[] l = File.ReadAllText(Chemin).Replace("\r", "").Split('\n');
                if (l.Length > 1 && l[1].Trim() == "vu") return null;
                return v;
            }
            catch { return null; }
        }

        /// <summary>L'utilisateur a vu l'annonce : on ne la lui remet plus pour cette version.
        /// Le rappel discret (titre de la fenêtre) reste, lui.</summary>
        public static void MarqueAnnonce()
        {
            try
            {
                string v = EnAttente();
                if (v == null) return;
                File.WriteAllText(Chemin, v + "\nvu\n");
            }
            catch { }
        }

        private static string CheminNotif { get { return AppPaths.File("bt-maj-notifiee.txt"); } }

        /// <summary>La notification Windows pour cette version a-t-elle déjà été montrée ?
        /// La vérification a lieu toutes les 6 heures : sans cette mémoire, la même notification
        /// reviendrait 4 fois par jour.</summary>
        public static bool DoitNotifier(string version)
        {
            string d = null;
            try { if (File.Exists(CheminNotif)) d = File.ReadAllText(CheminNotif); } catch { }
            return MajLogique.ANotifier(version, d);
        }

        public static void MarqueNotifiee(string version)
        {
            try { if (!string.IsNullOrWhiteSpace(version)) File.WriteAllText(CheminNotif, version.Trim()); } catch { }
        }

        private static string CheminIgnoree { get { return AppPaths.File("bt-maj-ignoree.txt"); } }

        /// <summary>« Ignorer cette version » : plus de bandeau, d'annonce ni de notification pour
        /// elle, et pas d'installation automatique. La version suivante est de nouveau proposée ;
        /// la vérification à la demande (menu ⋯) montre toujours la version ignorée.</summary>
        public static void Ignore(string version)
        {
            try { if (!string.IsNullOrWhiteSpace(version)) File.WriteAllText(CheminIgnoree, version.Trim()); } catch { }
        }

        /// <summary>Une mise à jour importante ne s'ignore pas : la marque est retirée.</summary>
        public static void OublieIgnoree()
        {
            try { if (File.Exists(CheminIgnoree)) File.Delete(CheminIgnoree); } catch { }
        }

        public static bool EstIgnoree(string version)
        {
            try { return File.Exists(CheminIgnoree) && MajLogique.EstIgnoree(version, File.ReadAllText(CheminIgnoree)); }
            catch { return false; }
        }

        public static void Clear()
        {
            try { if (File.Exists(Chemin)) File.Delete(Chemin); }
            catch { }
        }

        /// <summary>Lecture PURE : le contenu du fichier est-il une version exploitable ?</summary>
        public static string Parse(string contenu)
        {
            if (string.IsNullOrEmpty(contenu)) return null;
            string s = contenu.Replace("\r", "").Split('\n')[0].Trim();
            if (s.Length == 0 || s.Length > 20) return null;
            return Updater.ParseTag(s) == null ? null : s;
        }

        /// <summary>Lecture PURE de la marque « déjà annoncée » (2ᵉ ligne du fichier).</summary>
        public static bool ParseDejaVu(string contenu)
        {
            if (string.IsNullOrEmpty(contenu)) return false;
            string[] l = contenu.Replace("\r", "").Split('\n');
            return l.Length > 1 && l[1].Trim() == "vu";
        }

        /// <summary>
        /// Décision PURE : faut-il encore signaler <paramref name="annoncee"/> à quelqu'un qui
        /// tourne en <paramref name="courante"/> ? Non si la mise à jour a été installée entre-temps.
        /// </summary>
        public static bool ADevoirSignaler(string annoncee, Version courante)
        {
            Version v = Updater.ParseTag(annoncee);
            if (v == null) return false;
            return Updater.IsNewer(courante, v);
        }

        /// <summary>Version en attente, ou null. Le drapeau périmé est effacé au passage.</summary>
        public static string EnAttente()
        {
            try
            {
                if (!File.Exists(Chemin)) return null;
                string v = Parse(File.ReadAllText(Chemin));
                if (v == null) { Clear(); return null; }
                if (!ADevoirSignaler(v, Updater.CurrentVersion())) { Clear(); return null; }
                if (EstIgnoree(v)) return null;   // version ignorée : ni bandeau, ni annonce
                return v;
            }
            catch { return null; }
        }
    }
}
