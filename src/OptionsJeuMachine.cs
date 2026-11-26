using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// OPTIONS DE LANCEMENT PAR JEU — branchement : fichier bt-jeux-options.txt (écrit dans un
    /// fichier temporaire puis remplacé) et lancement d'un jeu avec ses options.
    /// </summary>
    internal static partial class OptionsJeu
    {
        private const string Fichier = "bt-jeux-options.txt";
        private static List<Options> _liste;
        private static readonly object _verrou = new object();

        private static List<Options> Liste()
        {
            if (_liste != null) return _liste;
            try
            {
                string p = AppPaths.File(Fichier);
                _liste = Lire(File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : "");
            }
            catch { _liste = new List<Options>(); }
            return _liste;
        }

        /// <summary>Options du jeu (copie), ou null.</summary>
        public static Options De(string nomJeu)
        {
            lock (_verrou)
            {
                Options o = Trouve(Liste(), nomJeu);
                return o == null ? null : new Options { Cle = o.Cle, Nom = o.Nom, Cible = o.Cible, Arguments = o.Arguments, Dossier = o.Dossier };
            }
        }

        /// <summary>Nom affiché du jeu (celui choisi par l'utilisateur, sinon le nom détecté).</summary>
        public static string Nom(string nomJeu)
        {
            lock (_verrou) { return NomAffiche(Liste(), nomJeu); }
        }

        /// <summary>Enregistre les options ; des champs vides les retirent. Faux si l'écriture échoue.</summary>
        public static bool Enregistre(string nomJeu, string nom, string cible, string arguments, string dossier)
        {
            lock (_verrou)
            {
                if (!Pose(Liste(), nomJeu, nom, cible, arguments, dossier)) return false;
                try
                {
                    string p = AppPaths.File(Fichier), tmp = p + ".tmp";
                    File.WriteAllText(tmp, Ecrire(_liste), new UTF8Encoding(false));
                    File.Move(tmp, p, true);
                    return true;
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Lance le jeu selon ses options quand elles changent le lancement : exécutable choisi, ou
        /// arguments d'un jeu Steam (steam.exe -applaunch). Faux : au lanceur habituel de jouer,
        /// avec <see cref="Options.Arguments"/> et <see cref="Options.Dossier"/> pour un exécutable.
        /// </summary>
        public static bool LanceSiChoisi(GameScan.GameInfo g, Options o)
        {
            if (g == null || o == null) return false;
            if (o.Cible.Length > 0)
            {
                if (!File.Exists(o.Cible)) throw new FileNotFoundException("exécutable choisi introuvable : " + o.Cible);
                Process.Start(new ProcessStartInfo(o.Cible, o.Arguments) { UseShellExecute = true, WorkingDirectory = DossierDepart(o.Dossier, o.Cible) });
                return true;
            }
            if (g.SteamId > 0 && o.Arguments.Length > 0)
            {
                string steam = SteamGames.SteamPath();
                string exe = steam == null ? null : Path.Combine(steam, "steam.exe");
                if (exe != null && File.Exists(exe))
                {
                    Process.Start(new ProcessStartInfo(exe, ArgsSteam(g.SteamId, o.Arguments)) { UseShellExecute = true, WorkingDirectory = steam });
                    return true;
                }
            }
            return false;
        }
    }
}
