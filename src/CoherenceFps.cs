using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// COHÉRENCE PLAFOND FPS / RAFRAÎCHISSEMENT / V-SYNC PILOTE / REFLEX — logique pure.
    ///
    /// Le plafond que l'on croit avoir n'est pas toujours celui que l'on a. Cas réel (24/11/2026) :
    /// 600 FPS demandés dans Overwatch, écran 500 Hz, G-SYNC + V-Sync forcé dans le panneau NVIDIA
    /// + Reflex. Dans cette combinaison, Reflex plafonne lui-même sous le rafraîchissement pour
    /// rester dans la plage G-SYNC : environ Hz − Hz²/3600, soit ≈ 430 FPS à 500 Hz. Les 600 FPS
    /// étaient donc impossibles, quels que soient les autres réglages.
    ///
    /// Sources du plafond automatique : forum Blur Busters (t=7522, « 144 Hz ≈ 138 FPS ») et la
    /// formule relevée par la communauté (r/nvidia « The G-Sync - vSync - Reflex Formula »). C'est
    /// une MESURE COMMUNAUTAIRE, pas une valeur publiée par NVIDIA : d'où « environ » dans les
    /// messages. Hz − 3 sans Reflex : recommandation Blur Busters pour rester dans la plage VRR.
    ///
    /// Valeurs V-Sync du pilote : NvApiDriverSettings.h (dépôt NVIDIA/nvapi), VSYNCMODE_ID.
    /// </summary>
    internal static class CoherenceFps
    {
        public enum VSync { Inconnu, Application, ForceOff, ForceOn, Rapide, Autre }

        /// <summary>Ce qu'on sait de la partie. 0 / null = inconnu ou absent.</summary>
        public sealed class Contexte
        {
            public string Jeu;
            public int HzEcran;          // rafraîchissement de l'écran principal
            public int HzJeu;            // rafraîchissement choisi dans le jeu (plein écran)
            public int Plafond;          // plafond FPS du jeu, 0 = aucun plafond personnalisé
            public bool? Reflex;
            public VSync VSync;
            public bool? GSync;          // null = inconnu : on suppose G-SYNC actif
            public int LimiteurPilote;   // « Fréquence d'images max. » du pilote, 0 = coupé
        }

        /// <summary>Réglages relus dans le fichier d'un jeu.</summary>
        public sealed class ReglagesJeu
        {
            public int Plafond;
            public int Hz;
            public bool? Reflex;
        }

        private static readonly CultureInfo Fr = new CultureInfo("fr-FR");

        public static int CapReflex(int hz)
        {
            if (hz <= 0) return 0;
            return (int)Math.Floor(hz - hz * (double)hz / 3600.0);
        }

        public static int CapVrr(int hz) { return hz <= 3 ? 0 : hz - 3; }

        public static VSync DepuisPilote(uint? valeur)
        {
            if (valeur == null) return VSync.Inconnu;
            switch (valeur.Value)
            {
                case 0x60925292u: return VSync.Application;
                case 0x08416747u: return VSync.ForceOff;
                case 0x47814940u: return VSync.ForceOn;
                case 0x18888888u: return VSync.Rapide;
                default: return VSync.Autre;
            }
        }

        private static readonly Regex Cle = new Regex(@"^\s*(\w+)\s*=\s*""?([^""\r\n]*)""?", RegexOptions.Multiline);

        /// <summary>Settings_v0.ini d'Overwatch : FrameRateCap n'est le plafond que si
        /// UseCustomFrameRates = 1 (sinon le jeu cale son plafond sur l'écran).</summary>
        public static ReglagesJeu LitOverwatch(string ini)
        {
            if (ini == null) return null;
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Cle.Matches(ini)) v[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            var r = new ReglagesJeu { Reflex = ReflexJeux.OverwatchReflex(ini) };
            int n;
            string s;
            if (v.TryGetValue("UseCustomFrameRates", out s) && s == "1"
                && v.TryGetValue("FrameRateCap", out s) && int.TryParse(s, out n) && n > 0) r.Plafond = n;
            if (v.TryGetValue("FullScreenRefresh", out s) && int.TryParse(s, out n) && n > 0) r.Hz = n;
            return r;
        }

        public static List<string> Constats(Contexte c)
        {
            var r = new List<string>();
            if (c == null || c.HzEcran <= 0) return r;
            int hz = c.HzEcran;
            string jeu = string.IsNullOrEmpty(c.Jeu) ? "ce jeu" : c.Jeu;
            bool force = c.VSync == VSync.ForceOn;
            int attendu = c.Plafond > 0 ? c.Plafond : int.MaxValue;

            if (c.HzJeu > 0 && hz > c.HzJeu + 4)
                r.Add(jeu + " est réglé sur " + c.HzJeu + " Hz en plein écran alors que ton écran monte à " + hz
                    + " Hz : choisis " + hz + " Hz dans les réglages vidéo du jeu.");

            if (force && c.GSync == false)
            {
                r.Add("V-Sync est forcé dans le panneau NVIDIA sans G-SYNC : " + jeu + " reste bloqué à " + hz
                    + " FPS et V-Sync ajoute de la latence d'affichage. Pour jouer en compétition, règle "
                    + "« Synchronisation verticale » sur « Désactivé » pour ce jeu dans le panneau NVIDIA.");
                attendu = Math.Min(attendu, hz);
            }
            else if (force && c.Reflex == true)
            {
                int cap = CapReflex(hz);
                if (c.Plafond == 0 || c.Plafond > cap)
                {
                    string debut = c.Plafond > 0
                        ? "Dans " + jeu + ", ton plafond de " + c.Plafond + " FPS ne sera jamais atteint"
                        : "Dans " + jeu + ", les FPS ne dépasseront pas environ " + cap;
                    string fin = c.Plafond > 0
                        ? " Pour viser " + c.Plafond + " FPS, règle « Synchronisation verticale » sur « Désactivé » pour ce jeu "
                          + "dans le panneau NVIDIA : environ " + (1000.0 / cap - 1000.0 / c.Plafond).ToString("0.0", Fr)
                          + " ms de moins par image, au prix de déchirures à l'écran."
                        : " Pour aller au-delà, règle « Synchronisation verticale » sur « Désactivé » pour ce jeu dans le "
                          + "panneau NVIDIA, au prix de déchirures à l'écran.";
                    r.Add(debut + " : V-Sync est forcé dans le panneau NVIDIA et Reflex est actif, donc le pilote plafonne "
                        + "tout seul à environ " + cap + " FPS sur ton écran " + hz + " Hz (règle relevée par Blur Busters : "
                        + "Hz − Hz²/3600). C'est voulu : pas de déchirure, et G-SYNC reste actif." + fin);
                }
                attendu = Math.Min(attendu, cap);
            }
            else if (force)
            {
                int cap = CapVrr(hz);
                if (c.Plafond == 0 || c.Plafond > cap)
                    r.Add("Dans " + jeu + ", V-Sync est forcé avec G-SYNC mais sans Reflex, et "
                        + (c.Plafond > 0 ? "le plafond de " + c.Plafond + " FPS est trop haut" : "sans plafond")
                        + " : dès que le jeu atteint " + hz + " FPS, G-SYNC décroche et V-Sync ajoute de la latence. "
                        + "Plafonne le jeu à " + cap + " FPS (Hz − 3, recommandation Blur Busters), ou active Reflex.");
                attendu = Math.Min(attendu, cap);
            }

            if (c.LimiteurPilote > 0 && c.LimiteurPilote < attendu)
                r.Add("Le limiteur d'images du pilote NVIDIA (« Fréquence d'images max. ») est réglé à " + c.LimiteurPilote
                    + " FPS : il passe avant le plafond de " + jeu
                    + (c.Plafond > 0 ? " (" + c.Plafond + " FPS)" : "") + ". Coupe-le ou monte-le dans le panneau NVIDIA.");

            if (!force && c.Plafond > 0 && c.Plafond < hz - 4)
                r.Add(jeu + " est plafonné à " + c.Plafond + " FPS alors que ton écran affiche " + hz + " Hz : monte le "
                    + "plafond à au moins " + CapVrr(hz) + " FPS pour profiter de ton écran.");
            return r;
        }
    }
}
