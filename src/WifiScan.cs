using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// LE PIC DE PING TOUTES LES MINUTES — Windows qui cherche un meilleur réseau.
    ///
    /// Même connectée, la carte Wi-Fi interrompt régulièrement le trafic pour balayer les canaux et
    /// voir si un point d'accès plus intéressant existe. Ce balayage dure quelques dizaines à
    /// quelques centaines de millisecondes, pendant lesquelles rien ne passe. En navigation ça ne se
    /// voit pas ; en jeu, c'est le pic de ping périodique que rien n'explique.
    ///
    /// Windows sait geler cette recherche : la logique de configuration automatique se coupe par
    /// interface. C'est efficace, et c'est DANGEREUX pour une raison simple — pendant ce temps, la
    /// carte ne voit plus aucun réseau et ne se reconnecte plus toute seule si le lien tombe.
    ///
    /// C'EST POURQUOI CE N'EST PAS UN RÉGLAGE, MAIS UN INTERRUPTEUR DE SÉANCE. Les guides qui
    /// proposent cette astuce concluent tous par « fais-toi deux fichiers .bat, un Mode Jeu et un
    /// Mode Normal ». Autrement dit : la réparation repose sur ta mémoire, et le jour où tu oublies,
    /// tu passes une heure à te demander pourquoi ton Wi-Fi ne se reconnecte plus.
    ///
    /// ONYX pose donc un marqueur en gelant, le retire en dégelant, et RÉTABLIT AU LANCEMENT SUIVANT
    /// si le marqueur est encore là — c'est-à-dire si l'app a été fermée brutalement, si elle a
    /// planté, ou si la machine s'est éteinte pendant la partie. Un outil de latence n'a pas le
    /// droit de laisser une machine incapable de se reconnecter.
    ///
    /// Et il ne touche JAMAIS un gel qu'il n'a pas posé : sans marqueur, il se contente de le
    /// signaler. Peut-être que l'utilisateur l'a voulu.
    /// </summary>
    internal static class WifiScan
    {
        public sealed class Carte
        {
            public string Nom = "";
            public bool RechercheActive;   // true = Windows continue de balayer
        }

        private static string Marqueur { get { return AppPaths.File("bt-wifi-scan.txt"); } }

        // ------------------------------------------------------------------ pur

        /// <summary>
        /// Lecture PURE de « netsh wlan show settings » (FR et EN).
        ///
        /// Le français emploie des guillemets typographiques « » et une apostrophe courbe dans
        /// « l’interface » : les motifs acceptent les deux formes, sinon la lecture échoue sur une
        /// machine française sans que rien ne l'explique.
        /// </summary>
        public static List<Carte> Analyse(string sortie)
        {
            var l = new List<Carte>();
            if (string.IsNullOrEmpty(sortie)) return l;

            const string fr = @"configuration automatique est (activ|désactiv)[ée]*e?\s+sur\s+l['’]interface\s*[«""]?\s*(.+?)\s*[»""]?\s*\.?\s*$";
            const string en = @"[Aa]uto configuration logic is (enabled|disabled) on interface\s*[""«]?\s*(.+?)\s*[""»]?\s*\.?\s*$";

            foreach (string ligne in sortie.Replace("\r", "").Split('\n'))
            {
                Match m = Regex.Match(ligne, fr, RegexOptions.IgnoreCase);
                bool actif;
                if (m.Success) actif = !m.Groups[1].Value.StartsWith("désactiv", StringComparison.OrdinalIgnoreCase);
                else
                {
                    m = Regex.Match(ligne, en, RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    actif = m.Groups[1].Value.Equals("enabled", StringComparison.OrdinalIgnoreCase);
                }
                string nom = m.Groups[2].Value.Trim().Trim('«', '»', '"', '.', ' ');
                if (nom.Length == 0) continue;
                l.Add(new Carte { Nom = nom, RechercheActive = actif });
            }
            return l;
        }

        /// <summary>PUR : cartes dont la recherche est gelée.</summary>
        public static List<Carte> Gelees(List<Carte> l)
        {
            var r = new List<Carte>();
            if (l == null) return r;
            foreach (Carte c in l) if (!c.RechercheActive) r.Add(c);
            return r;
        }

        /// <summary>Constat PUR pour le diagnostic. null s'il n'y a rien à dire.</summary>
        public static string Texte(List<Carte> l, bool parNous)
        {
            List<Carte> g = Gelees(l);
            if (g.Count == 0) return null;
            string noms = "";
            foreach (Carte c in g) noms += (noms.Length > 0 ? ", " : "") + "« " + c.Nom + " »";
            return parNous
                ? "Recherche de réseaux gelée sur " + noms + " par ONYX. Elle sera rétablie à la "
                + "fermeture de l'app — ou au prochain lancement si elle se ferme mal."
                : "La recherche de réseaux Wi-Fi est gelée sur " + noms + ", et ce n'est pas ONYX "
                + "qui l'a fait. Ça supprime les pics de ping périodiques, mais la carte ne se "
                + "reconnectera pas toute seule si le lien tombe. Si tu ne l'as pas voulu : "
                + "netsh wlan set autoconfig enabled=yes interface=\"" + g[0].Nom + "\"";
        }

        // ------------------------------------------------------------------ matériel

        public static List<Carte> Lire()
        {
            try
            {
                NativeResult r = Sys.Run(Sys.Sys32("netsh.exe"), "wlan show settings");
                return Analyse(r.Output);
            }
            catch { return new List<Carte>(); }
        }

        /// <summary>Nom de l'interface gelée par ONYX, ou null.</summary>
        public static string GeleeParNous()
        {
            try
            {
                if (!File.Exists(Marqueur)) return null;
                string s = File.ReadAllText(Marqueur).Trim();
                return s.Length == 0 ? null : s;
            }
            catch { return null; }
        }

        private static bool Netsh(string nom, bool actif, Action<string, int> log)
        {
            try
            {
                NativeResult r = Sys.Run(Sys.Sys32("netsh.exe"),
                    "wlan set autoconfig enabled=" + (actif ? "yes" : "no") + " interface=\"" + nom + "\"");
                string o = (r.Output ?? "").ToLowerInvariant();
                if (r.ExitCode != 0 || o.Contains("introuvable") || o.Contains("not found"))
                {
                    if (log != null) log("La carte « " + nom + " » n'a pas accepté la commande.", 3);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                if (log != null) log("Recherche Wi-Fi : " + ex.Message, 3);
                return false;
            }
        }

        /// <summary>Gèle la recherche pendant la partie. Le marqueur est écrit AVANT la commande :
        /// si l'app meurt entre les deux, on rétablira une carte qui n'était pas gelée — sans effet.
        /// L'inverse (geler sans marqueur) laisserait un Wi-Fi muet sans que rien ne le sache.</summary>
        public static bool Geler(Action<string, int> log)
        {
            List<Carte> l = Lire();
            if (l.Count == 0)
            {
                if (log != null) log("Aucune carte Wi-Fi : rien à geler.", 1);
                return false;
            }
            string nom = l[0].Nom;
            try { File.WriteAllText(Marqueur, nom + "\n"); } catch { }
            if (!Netsh(nom, false, log)) { try { File.Delete(Marqueur); } catch { } return false; }
            if (log != null)
                log("Recherche de réseaux gelée sur « " + nom + " » : plus de pic de ping périodique. "
                  + "ATTENTION : la carte ne verra plus aucun autre réseau et ne se reconnectera pas "
                  + "seule si le lien tombe. ONYX rétablit à la fermeture.", 2);
            return true;
        }

        /// <summary>Rend la recherche à Windows.</summary>
        public static bool Degeler(Action<string, int> log)
        {
            string nom = GeleeParNous();
            if (nom == null)
            {
                List<Carte> g = Gelees(Lire());
                if (g.Count == 0) return false;
                nom = g[0].Nom;
            }
            bool ok = Netsh(nom, true, log);
            try { if (File.Exists(Marqueur)) File.Delete(Marqueur); } catch { }
            if (ok && log != null) log("Recherche de réseaux rendue à Windows sur « " + nom + " ».", 1);
            return ok;
        }

        /// <summary>
        /// Au lancement : si un marqueur traîne, c'est qu'ONYX a été fermé sans dégeler. On répare.
        /// Rend true si une réparation a eu lieu.
        /// </summary>
        public static bool Soigne(Action<string, int> log)
        {
            string nom = GeleeParNous();
            if (nom == null) return false;
            bool ok = Netsh(nom, true, null);
            try { File.Delete(Marqueur); } catch { }
            if (log != null)
                log(ok
                    ? "Recherche de réseaux Wi-Fi rétablie sur « " + nom + " » : elle était restée "
                      + "gelée depuis la dernière session (fermeture brutale ou plantage)."
                    : "La recherche Wi-Fi était marquée gelée sur « " + nom + " » mais n'a pas pu "
                      + "être rétablie. Commande : netsh wlan set autoconfig enabled=yes interface=\""
                      + nom + "\"", ok ? 1 : 3);
            return true;
        }
    }
}
