using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// NOTE ET LIENS PAR JEU — branchement : fichier bt-jeux-notes.txt (écrit dans un fichier
    /// temporaire puis remplacé) et ouverture d'un lien dans le navigateur par défaut.
    /// </summary>
    internal static partial class NotesJeu
    {
        private const string Fichier = "bt-jeux-notes.txt";
        private static List<Fiche> _liste;
        private static readonly object _verrou = new object();

        private static List<Fiche> Liste()
        {
            if (_liste != null) return _liste;
            try
            {
                string p = AppPaths.File(Fichier);
                _liste = Lire(File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : "");
            }
            catch { _liste = new List<Fiche>(); }
            return _liste;
        }

        /// <summary>Note et liens du jeu (copie) ; une fiche vide s'il n'en a pas.</summary>
        public static Fiche De(string nomJeu)
        {
            lock (_verrou)
            {
                Fiche f = Trouve(Liste(), nomJeu);
                var copie = new Fiche();
                if (f == null) return copie;
                copie.Cle = f.Cle; copie.Note = f.Note; copie.Etoiles = f.Etoiles;
                foreach (Lien k in f.Liens) copie.Liens.Add(new Lien { Titre = k.Titre, Url = k.Url });
                return copie;
            }
        }

        /// <summary>Enregistre l'appréciation, la note et les liens ; tout vide les retire. Faux si l'écriture échoue.</summary>
        public static bool Enregistre(string nomJeu, int etoiles, string note, IEnumerable<Lien> liens)
        {
            lock (_verrou)
            {
                if (!Pose(Liste(), nomJeu, etoiles, note, liens)) return false;
                try
                {
                    string p = AppPaths.File(Fichier), tmp = p + ".tmp";
                    File.WriteAllText(tmp, Ecrire(_liste), new UTF8Encoding(false));
                    File.Move(tmp, p, true);
                    return true;
                }
                catch { _liste = null; return false; }   // relu du disque au prochain accès
            }
        }

        /// <summary>
        /// Ouvre le lien dans le navigateur par défaut. L'adresse repasse par <see cref="UrlPropre"/> :
        /// même si le fichier a été modifié à la main, seul du http/https part vers le système.
        /// </summary>
        public static bool Ouvre(string url, out string erreur)
        {
            string propre = UrlPropre(url, out erreur);
            if (propre == null) return false;
            try { Process.Start(new ProcessStartInfo(propre) { UseShellExecute = true }); return true; }
            catch (Exception ex) { erreur = ex.Message; return false; }
        }
    }
}
