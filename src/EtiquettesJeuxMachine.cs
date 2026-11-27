using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// ÉTIQUETTES DE JEUX — branchement : fichier bt-jeux-etiquettes.txt, écrit dans un fichier
    /// temporaire puis remplacé. Lu une fois, gardé en mémoire.
    /// </summary>
    internal static partial class EtiquettesJeux
    {
        private const string Fichier = "bt-jeux-etiquettes.txt";
        private static Dictionary<string, List<string>> _d;
        private static readonly object _verrou = new object();

        private static Dictionary<string, List<string>> Table()
        {
            if (_d != null) return _d;
            try
            {
                string p = AppPaths.File(Fichier);
                _d = Lire(File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : "");
            }
            catch { _d = new Dictionary<string, List<string>>(); }
            return _d;
        }

        public static List<string> Pour(string nomJeu)
        {
            lock (_verrou) { return De(Table(), nomJeu); }
        }

        public static List<string> Toutes()
        {
            lock (_verrou) { return Toutes(Table()); }
        }

        /// <summary>Enregistre la saisie (vide : retire les étiquettes). Faux si l'écriture échoue.</summary>
        public static bool Enregistre(string nomJeu, string saisie)
        {
            lock (_verrou)
            {
                Dictionary<string, List<string>> d = Table();
                var avant = new Dictionary<string, List<string>>(d, d.Comparer);
                if (!Pose(d, nomJeu, saisie)) return false;
                try
                {
                    string p = AppPaths.File(Fichier), tmp = p + ".tmp";
                    File.WriteAllText(tmp, Ecrire(d), new UTF8Encoding(false));
                    File.Move(tmp, p, true);
                    return true;
                }
                catch { _d = avant; return false; }
            }
        }
    }
}
