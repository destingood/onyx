using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// LA QUALITÉ DU LIEN SANS FIL — ce qui décide de la régularité du ping, et qu'aucun réglage
    /// Windows ne peut corriger.
    ///
    /// Dire « tu es en Wi-Fi » ne suffit pas. Un lien à 93 % de signal sur 5 GHz et un lien à 35 %
    /// sur un canal 2,4 GHz saturé par les voisins n'ont rien à voir : le premier est excellent, le
    /// second produit des pics de ping que ni les tweaks TCP ni le pilote ne rattraperont.
    ///
    /// Ce module lit l'état réel de la radio et le dit franchement, dans les DEUX SENS. Quand le
    /// lien est mauvais, il nomme la cause. Quand il est bon, il le dit AUSSI — parce qu'à ce
    /// moment-là il faut arrêter de chercher du côté du Wi-Fi, et que continuer à conseiller « passe
    /// en Ethernet » sans le préciser serait paresseux : le gain restant tient alors à la gigue et
    /// aux interruptions du pilote sans fil, pas à la qualité du signal.
    /// </summary>
    internal static class WifiLink
    {
        public sealed class Etat
        {
            public bool Connecte;
            public string Ssid = "";
            public string Bande = "";      // « 5 GHz », « 2,4 GHz »…
            public int Canal;
            public string Radio = "";      // 802.11be, 802.11ax…
            public int DebitMbps;          // débit négocié en réception
            public int SignalPct = -1;     // -1 = non lu
            public int Rssi;               // dBm, 0 = non lu
        }

        // ------------------------------------------------------------------ pur

        /// <summary>Lecture PURE de « netsh wlan show interfaces » (FR et EN).</summary>
        public static Etat Analyse(string sortie)
        {
            var e = new Etat();
            if (string.IsNullOrEmpty(sortie)) return e;

            // « connecté » / « connected », mais PAS « déconnecté » / « disconnected ».
            e.Connecte = Regex.IsMatch(sortie, @"(?<![édis])\b(connect(é|ed))\b", RegexOptions.IgnoreCase)
                      && !Regex.IsMatch(sortie, @"(déconnect|disconnect)", RegexOptions.IgnoreCase);

            e.Ssid = Texte(sortie, @"^\s*SSID\s*:\s*(.+?)\s*$");
            e.Bande = Texte(sortie, @"^\s*(?:Bande|Band)\s*:\s*(.+?)\s*$");
            e.Radio = Texte(sortie, @"^\s*(?:Type de radio|Radio type)\s*:\s*(.+?)\s*$");
            e.Canal = Nombre(sortie, @"^\s*(?:Canal|Channel)\s*:\s*(\d+)", 1, 400);
            e.DebitMbps = Nombre(sortie, @"^\s*(?:R[ée]ception|Receive rate)\s*\(Mbit?s?/s\)\s*:\s*(\d+)", 1, 100000);
            e.SignalPct = Nombre(sortie, @"^\s*(?:Signal)\s*:\s*(\d+)\s*%", 0, 100);
            if (e.SignalPct == 0 && !Regex.IsMatch(sortie, @"Signal\s*:\s*0\s*%")) e.SignalPct = -1;
            e.Rssi = -Nombre(sortie, @"^\s*Rssi\s*:\s*-(\d+)", 1, 200);
            return e;
        }

        /// <summary>
        /// Extraction PURE d'un champ, avec NORMALISATION DES ESPACES INSÉCABLES.
        ///
        /// netsh écrit « 5 GHz » avec une espace INSÉCABLE (U+00A0) entre le nombre et l'unité,
        /// invisible à l'œil et à l'écran. Une comparaison sur « 5 GHz » écrit au clavier échoue
        /// alors sans que rien ne le laisse deviner. Constaté en testant le lecteur contre la vraie
        /// sortie de la machine — jamais en relisant le code.
        /// </summary>
        private static string Texte(string s, string motif)
        {
            Match m = Regex.Match(s, motif, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (!m.Success) return "";
            return m.Groups[1].Value.Replace(' ', ' ').Replace(' ', ' ').Trim();
        }

        private static int Nombre(string s, string motif, int min, int max)
        {
            Match m = Regex.Match(s, motif, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            int v;
            if (!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                return 0;
            return v >= min && v <= max ? v : 0;
        }

        /// <summary>PUR : le lien travaille-t-il sur la bande encombrée ?</summary>
        public static bool Est24Ghz(Etat e)
        {
            if (e == null || string.IsNullOrEmpty(e.Bande)) return false;
            return e.Bande.Replace(",", ".").IndexOf("2.4", StringComparison.Ordinal) >= 0;
        }

        /// <summary>PUR : signal faible ? On croise le pourcentage ET le RSSI quand les deux sont
        /// lus — le pourcentage est une interprétation du pilote, le RSSI est la mesure.</summary>
        public static bool SignalFaible(Etat e)
        {
            if (e == null) return false;
            if (e.Rssi < 0 && e.Rssi <= -70) return true;
            return e.SignalPct >= 0 && e.SignalPct < 45;
        }

        /// <summary>
        /// Verdict PUR. null si on n'est pas en Wi-Fi ou si rien n'a pu être lu.
        /// </summary>
        public static string Verdict(Etat e)
        {
            if (e == null || !e.Connecte) return null;
            if (e.SignalPct < 0 && e.Rssi == 0) return null;   // rien de mesurable : on se tait

            string ou = e.SignalPct >= 0 ? e.SignalPct + " % de signal" : "signal non lu";
            if (e.Rssi < 0) ou += " (" + e.Rssi + " dBm)";
            string lien = e.Bande.Length > 0 ? ", " + e.Bande : "";
            if (e.Canal > 0) lien += " canal " + e.Canal;
            if (e.DebitMbps > 0) lien += ", " + e.DebitMbps + " Mbit/s négociés";

            if (SignalFaible(e))
                return "Lien Wi-Fi FAIBLE : " + ou + lien + ". À ce niveau, la carte retransmet "
                     + "beaucoup, et ça se voit en pics de ping — aucun réglage de Windows ne "
                     + "corrige ça. Rapproche-toi de la box, ou passe en Ethernet.";

            if (Est24Ghz(e))
                return "Lien Wi-Fi sur la bande 2,4 GHz (" + ou + lien + "). C'est la bande que se "
                     + "partagent tous les voisins, les micro-ondes et le Bluetooth : le signal peut "
                     + "être bon et le ping instable quand même. Bascule sur 5 GHz si ta box la "
                     + "propose — c'est le gain le plus net sans toucher au PC.";

            return "Lien Wi-Fi solide : " + ou + lien + ". Le signal n'est PAS ton problème — "
                 + "inutile de chercher de ce côté. Ce qu'un câble apporterait encore, c'est la "
                 + "régularité (gigue) et moins de travail différé du pilote sans fil, pas du débit.";
        }

        /// <summary>Niveau du constat : 1 quand il y a quelque chose à corriger, 0 sinon.</summary>
        public static int Niveau(Etat e)
        {
            if (e == null || !e.Connecte) return 0;
            return SignalFaible(e) || Est24Ghz(e) ? 1 : 0;
        }

        // ------------------------------------------------------------------ matériel

        public static Etat Lire()
        {
            try
            {
                NativeResult r = Sys.Run(Sys.Sys32("netsh.exe"), "wlan show interfaces");
                return Analyse(r.Output);
            }
            catch { return new Etat(); }
        }
    }
}
