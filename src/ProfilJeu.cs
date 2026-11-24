using System;
using System.Collections.Generic;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Profil par jeu : quand l'appliquer, quoi appliquer, et l'état à retenir pour le retirer.
    /// Cette moitié est PURE (testée au banc) ; le relevé et l'application vivent dans
    /// ProfilJeuMachine.cs.
    ///
    /// Un seul jeu profilé à la fois. Le profil s'applique UNE fois au lancement et se retire
    /// UNE fois à la sortie : pas de ré-application à chaque tour de minuteur.
    /// </summary>
    internal static partial class ProfilJeu
    {
        public enum Geste { Rien, Appliquer, Retirer }

        public enum Mode { Aucun, CpuSets, Affinite }

        public sealed class Etat
        {
            public string Jeu;          // jeu profilé en cours, null si aucun
            public string PlanOrigine;  // GUID du plan d'avant, null si ONYX n'a pas basculé
            public bool Fond;           // logiciels de fond envoyés sur l'autre CCD, à rendre à Windows
        }

        /// <summary>Plan d'alimentation « Performances élevées » de Windows.</summary>
        public const string PlanPerformances = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        /// <summary>
        /// Jeu profilé en cours contre jeu détecté. Un changement de jeu RETIRE d'abord ; le tour
        /// suivant appliquera le nouveau, depuis un état propre.
        /// </summary>
        public static Geste Decide(string jeuEnCours, string jeuDetecte)
        {
            if (jeuEnCours == null) return jeuDetecte == null ? Geste.Rien : Geste.Appliquer;
            if (jeuDetecte != null && string.Equals(jeuEnCours, jeuDetecte, StringComparison.OrdinalIgnoreCase))
                return Geste.Rien;
            return Geste.Retirer;
        }

        /// <summary>
        /// « Auto » suit la topologie (CPU Sets, préférence souple) et ignore l'ancien masque
        /// enregistré ; « Manuel » garde l'affinité stricte choisie par l'utilisateur.
        /// </summary>
        public static Mode Cible(string mode, long masque, IList<int> preferes, out long masqueAffinite)
        {
            masqueAffinite = 0;
            if (string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase))
                return preferes != null && preferes.Count > 0 ? Mode.CpuSets : Mode.Aucun;
            // GameProfileForm écrit « Manual » ; « Manuel » est accepté pour le cas où le libellé
            // français finirait un jour dans le fichier.
            bool manuel = string.Equals(mode, "Manual", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(mode, "Manuel", StringComparison.OrdinalIgnoreCase);
            if (manuel && masque != 0)
            {
                masqueAffinite = masque;
                return Mode.Affinite;
            }
            return Mode.Aucun;
        }

        /// <summary>Faux si le plan est illisible ou déjà « Performances » : rien à rétablir ensuite.</summary>
        public static bool DoitBasculer(string planActif)
        {
            if (planActif == null || string.Equals(planActif, PlanPerformances, StringComparison.OrdinalIgnoreCase)) return false;
            // « Performances optimales » (modèle caché eb61 et la copie eb62 posée par le réglage
            // eSport power_ultimate) va plus loin que « Performances élevées » : on n'y touche pas.
            return !planActif.StartsWith(PrefixeOptimales, StringComparison.OrdinalIgnoreCase);
        }

        private const string PrefixeOptimales = "e9a42b02-d5df-448d-aa00-03f14749eb6";

        public enum Reprendre { Rien, Adopter, Retablir }

        /// <summary>
        /// Premier tour après le lancement d'ONYX, avec l'état resté sur le disque. Le même jeu
        /// tourne encore (ONYX relancé en pleine partie) : on ADOPTE l'état, plan d'origine
        /// compris — le réécrire ici le perdrait, puisque le plan actif est déjà Performances.
        /// Sinon la partie est finie : on rétablit.
        /// </summary>
        public static Reprendre Reprise(Etat fichier, string jeuDetecte)
        {
            if (fichier == null) return Reprendre.Rien;
            return jeuDetecte != null && string.Equals(fichier.Jeu, jeuDetecte, StringComparison.OrdinalIgnoreCase)
                ? Reprendre.Adopter : Reprendre.Retablir;
        }

        private static readonly System.Text.RegularExpressions.Regex GuidRx = new System.Text.RegularExpressions.Regex(
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

        /// <summary>GUID de la sortie de « powercfg /getactivescheme », quelle que soit la langue
        /// de Windows (« GUID du mode… », « Power Scheme GUID: »). Null si absent.</summary>
        public static string GuidPlan(string sortie)
        {
            if (string.IsNullOrEmpty(sortie)) return null;
            var m = GuidRx.Match(sortie);
            return m.Success ? m.Value.ToLowerInvariant() : null;
        }

        /// <summary>
        /// Premier profil (dans l'ordre du fichier) dont un exécutable figure parmi les noms de
        /// processus relevés. Un seul relevé des processus par tour, au lieu d'un par exécutable.
        /// </summary>
        public static string JeuDetecte(IEnumerable<KeyValuePair<string, string[]>> profilsExes, ISet<string> nomsProcessus)
        {
            foreach (var kv in profilsExes)
            {
                if (kv.Value == null) continue;
                foreach (string exe in kv.Value)
                {
                    if (string.IsNullOrEmpty(exe)) continue;
                    if (nomsProcessus.Contains(System.IO.Path.GetFileNameWithoutExtension(exe))) return kv.Key;
                }
            }
            return null;
        }

        /// <summary>
        /// Logiciels de fond éloignés du CCD du jeu sur un X3D à deux CCD : navigateurs, vocal,
        /// lanceurs, utilitaires de périphériques et de capteurs. Ils restent ouverts (vocal
        /// compris) ; ils tournent seulement sur l'autre CCD, au lieu de partager le cache du jeu.
        /// Hors liste exprès : Windows, l'audio, NVIDIA, le pilote AMD (amd3dvcacheUser).
        /// </summary>
        public static readonly string[] LogicielsDeFond =
        {
            "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi",
            "Discord", "Spotify", "Teams", "ms-teams", "slack", "WhatsApp", "Telegram",
            "Battle.net", "steam", "steamwebhelper", "EpicGamesLauncher", "EpicWebHelper",
            "EADesktop", "UbisoftConnect", "upc", "GalaxyClient", "RiotClientServices",
            "iCUE", "lghub", "lghub_agent", "lghub_system_tray", "Razer Synapse 3", "RazerAppEngine",
            "MSIAfterburner", "RTSS", "FanControl", "HWiNFO64", "SignalRgb", "OpenRGB",
            "OneDrive", "Dropbox", "Docker Desktop", "com.docker.backend",
        };

        /// <summary>Noms (une fois chacun) des logiciels de fond vivants, sans les exécutables du
        /// profil du jeu : un lanceur déclaré dans le profil reste avec le jeu.</summary>
        public static List<string> AReleguer(IEnumerable<string> nomsVivants, IEnumerable<string> exesDuJeu)
        {
            var jeu = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (exesDuJeu != null)
                foreach (string exe in exesDuJeu)
                    if (!string.IsNullOrEmpty(exe)) jeu.Add(System.IO.Path.GetFileNameWithoutExtension(exe));
            var connus = new HashSet<string>(LogicielsDeFond, StringComparer.OrdinalIgnoreCase);
            var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var l = new List<string>();
            if (nomsVivants == null) return l;
            foreach (string n in nomsVivants)
                if (!string.IsNullOrEmpty(n) && connus.Contains(n) && !jeu.Contains(n) && vus.Add(n)) l.Add(n);
            return l;
        }

        public static string EcritEtat(Etat e)
        {
            var sb = new StringBuilder();
            sb.Append("jeu=").Append(e.Jeu ?? "").Append('\n');
            if (e.PlanOrigine != null) sb.Append("plan=").Append(e.PlanOrigine).Append('\n');
            if (e.Fond) sb.Append("fond=1").Append('\n');
            return sb.ToString();
        }

        /// <summary>Null si le texte est vide ou ne nomme aucun jeu.</summary>
        public static Etat LitEtat(string texte)
        {
            if (string.IsNullOrWhiteSpace(texte)) return null;
            var e = new Etat();
            foreach (string brute in texte.Split('\n'))
            {
                string l = brute.Trim();
                int i = l.IndexOf('=');
                if (i <= 0) continue;
                string cle = l.Substring(0, i), val = l.Substring(i + 1).Trim();
                if (val.Length == 0) continue;
                if (cle == "jeu") e.Jeu = val;
                else if (cle == "plan") e.PlanOrigine = val;
                else if (cle == "fond") e.Fond = val == "1";
            }
            return e.Jeu == null ? null : e;
        }
    }
}
