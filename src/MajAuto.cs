using System;
using System.IO;
using System.Threading;

namespace BTOptimizer
{
    /// <summary>
    /// INSTALLATION AUTOMATIQUE DES MISES À JOUR — option du menu ⋯, coupée par défaut.
    ///
    /// Sans elle, ONYX prévient et attend un clic. Avec elle, une version trouvée par la
    /// vérification périodique est :
    ///  1. téléchargée en fond, jamais pendant une partie (58 Mo sur une connexion en jeu) ;
    ///  2. contrôlée (taille et empreinte SHA256 publiée par GitHub) ;
    ///  3. installée sans aucune fenêtre quand ONYX est réduit dans la zone de notification et
    ///     qu'aucun jeu ne tourne ; ONYX se rouvre seul, réduit comme il l'était.
    /// Tant que la fenêtre d'ONYX est ouverte, rien ne s'installe tout seul : le bandeau reste, et
    /// son bouton installe aussitôt puisque le fichier est déjà là.
    ///
    /// Ici : l'état (version proposée, fichier prêt) et la marque de retour. Les décisions sont
    /// dans MajLogique (pures, testées au banc) ; le moment est choisi par le tableau de bord.
    /// Une version ignorée, un fichier sans empreinte ou venu d'ailleurs que GitHub ne passent
    /// jamais par ici.
    /// </summary>
    internal static class MajAuto
    {
        private static readonly object Verrou = new object();
        private static Updater.Release _proposee;
        private static string _fichier, _derniereErreur;

        /// <summary>Version à installer automatiquement. La même version reproposée ne relance rien.</summary>
        public static void Propose(Updater.Release rel)
        {
            if (rel == null || rel.Ver == null) return;
            lock (Verrou)
            {
                if (_proposee != null && _proposee.Ver == rel.Ver && _proposee.Sha256 == rel.Sha256) return;
                _proposee = rel; _fichier = null; _derniereErreur = null;
            }
        }

        public static Updater.Release Proposee { get { lock (Verrou) return _proposee; } }

        /// <summary>Installateur téléchargé et contrôlé, ou null.</summary>
        public static string Fichier { get { lock (Verrou) return _fichier; } }

        public static void Oublie()
        {
            lock (Verrou) { _proposee = null; _fichier = null; _derniereErreur = null; }
        }

        /// <summary>
        /// Télécharge la version proposée (bloquant : à appeler hors du fil d'interface). Un fichier
        /// déjà présent et intact n'est pas retéléchargé. La même erreur n'est rendue qu'une fois :
        /// l'appel revient toutes les 15 minutes, le journal ne doit pas se remplir d'une panne de réseau.
        /// </summary>
        public static bool Telecharge(out string erreur)
        {
            erreur = "";
            Updater.Release rel = Proposee;
            if (rel == null) return false;
            string e;
            string f = Updater.Download(rel, null, CancellationToken.None, out e);
            lock (Verrou)
            {
                if (_proposee != rel) return false;   // remplacée pendant le téléchargement
                if (f != null) { _fichier = f; _derniereErreur = null; return true; }
                if (!string.Equals(e, _derniereErreur)) { _derniereErreur = e; erreur = e ?? ""; }
                return false;
            }
        }

        // ------------------------------------------------------------------ retour après installation

        private static string CheminRetour { get { return AppPaths.File("bt-maj-retour.txt"); } }

        /// <summary>À écrire juste avant de lancer l'installateur : ONYX reviendra réduit.</summary>
        public static void MarqueRetour(string version)
        {
            try { File.WriteAllText(CheminRetour, MajLogique.MarqueRetour(version, DateTime.Now)); } catch { }
        }

        public static void EffaceRetour()
        {
            try { if (File.Exists(CheminRetour)) File.Delete(CheminRetour); } catch { }
        }

        /// <summary>Au lancement : version qui vient d'être installée automatiquement, ou null. La
        /// marque est lue une seule fois.</summary>
        public static string RetourDiscret()
        {
            try
            {
                if (!File.Exists(CheminRetour)) return null;
                string marque = File.ReadAllText(CheminRetour);
                EffaceRetour();
                Version v = Updater.CurrentVersion();
                return MajLogique.RetourDiscret(marque, DateTime.Now, v == null ? null : v.Major + "." + v.Minor);
            }
            catch { return null; }
        }
    }
}
