using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// SUCCÈS STEAM — branchement : compte le plus récent de loginusers.vdf, puis les deux fichiers
    /// du cache de Steam dans appcache\stats. Lecture seule, aucun réseau. Bloquant (disque) :
    /// hors du fil d'interface.
    /// </summary>
    internal static partial class SuccesSteam
    {
        /// <summary>
        /// Tous les succès datés du compte Steam courant, tous jeux confondus (pour le bilan).
        /// Bloquant (une lecture par jeu ayant des succès) : hors du fil d'interface. Vide si Steam est absent.
        /// </summary>
        public static List<Deblocage> ChargeTous()
        {
            var tous = new List<Deblocage>();
            try
            {
                string steam = SteamGames.SteamPath();
                if (steam == null) return tous;
                string dossier = Path.Combine(steam, "appcache", "stats");
                string login = Path.Combine(steam, "config", "loginusers.vdf");
                if (!Directory.Exists(dossier) || !File.Exists(login)) return tous;
                string compte = JeuxCompteSteam.Compte(ListeSouhaits.SteamId64(File.ReadAllText(login, Encoding.UTF8)));
                if (compte == null) return tous;
                string prefixe = "UserGameStats_" + compte + "_";
                foreach (string joueur in Directory.GetFiles(dossier, prefixe + "*.bin"))
                {
                    string app = Path.GetFileNameWithoutExtension(joueur).Substring(prefixe.Length);
                    string schema = Path.Combine(dossier, "UserGameStatsSchema_" + app + ".bin");
                    if (!File.Exists(schema)) continue;
                    try { tous.AddRange(Deblocages(Lit(File.ReadAllBytes(schema), File.ReadAllBytes(joueur)), null)); }
                    catch { }   // un fichier en cours d'écriture par Steam : ce jeu manque cette fois
                }
            }
            catch (Exception ex) { JournalTechnique.Echec("SuccesSteam.ChargeTous", ex); }
            return tous;
        }

        /// <summary>Succès du jeu, ou null + raison lisible. releve = date du dernier relevé de Steam.</summary>
        public static Bilan Charge(int appId, out string raison, out DateTime? releve)
        {
            raison = null; releve = null;
            if (appId <= 0) { raison = "Pas un jeu Steam."; return null; }
            try
            {
                string steam = SteamGames.SteamPath();
                if (steam == null) { raison = "Steam introuvable."; return null; }
                string dossier = Path.Combine(steam, "appcache", "stats");
                string schema = Path.Combine(dossier, "UserGameStatsSchema_" + appId + ".bin");
                if (!File.Exists(schema)) { raison = "Steam n'a pas encore la liste des succès de ce jeu : ouvre sa page Succès dans Steam ou lance-le une fois."; return null; }

                string joueur = null;
                string login = Path.Combine(steam, "config", "loginusers.vdf");
                string compte = File.Exists(login) ? JeuxCompteSteam.Compte(ListeSouhaits.SteamId64(File.ReadAllText(login, Encoding.UTF8))) : null;
                if (compte != null)
                {
                    string p = Path.Combine(dossier, "UserGameStats_" + compte + "_" + appId + ".bin");
                    if (File.Exists(p)) { joueur = p; releve = File.GetLastWriteTime(p); }
                }
                Bilan b = Lit(File.ReadAllBytes(schema), joueur != null ? File.ReadAllBytes(joueur) : null);
                if (b == null) raison = "Ce jeu n'a pas de succès Steam.";
                return b;
            }
            catch (Exception ex)
            {
                JournalTechnique.Echec("SuccesSteam.Charge", ex);
                raison = "Lecture impossible : " + ex.Message;
                return null;
            }
        }
    }
}
