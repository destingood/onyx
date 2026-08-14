using System;
using System.Collections.Generic;
using System.Management;

namespace BTOptimizer
{
    /// <summary>
    /// TU JOUES EN WI-FI ALORS QU'UNE PRISE ETHERNET DORT DERRIÈRE LA MACHINE.
    ///
    /// C'est le levier de latence le plus efficace, et le plus souvent ignoré — parce qu'il ne se
    /// règle pas dans un menu, il se branche.
    ///
    /// Deux raisons, distinctes :
    ///
    ///  • LA GIGUE. Le Wi-Fi partage un canal avec les voisins et retransmet ce qui se perd. Le
    ///    débit moyen peut être excellent (2,4 Gbit/s annoncés) pendant que le temps d'aller-retour
    ///    varie de quelques millisecondes à quelques dizaines, au hasard. En jeu, ce n'est pas le
    ///    débit qui compte, c'est la RÉGULARITÉ : un pic isolé suffit à faire reculer un personnage.
    ///
    ///  • LES INTERRUPTIONS. Un pilote Wi-Fi traite bien plus d'événements qu'un pilote Ethernet, et
    ///    son travail différé (DPC) monopolise un cœur pendant qu'il s'exécute. C'est visible dans
    ///    la mesure DPC d'ONYX, où les pilotes réseau sans fil figurent régulièrement en tête.
    ///
    /// On ne signale RIEN si aucune carte Ethernet n'existe : reprocher à quelqu'un de ne pas
    /// brancher un câble sur un port absent serait absurde. Et « désactivée » se distingue de
    /// « débranchée » : ce ne sont pas les mêmes gestes.
    /// </summary>
    internal static class LienReseau
    {
        /// <summary>Ce que la machine expose comme cartes physiques (le virtuel est écarté).</summary>
        public sealed class Etat
        {
            public bool SansFilActif;       // une carte sans fil porte la connexion
            public bool FilairePresent;     // une carte Ethernet existe sur la machine
            public bool FilaireActif;       // …et elle est connectée
            public bool FilaireDesactive;   // …elle existe mais est désactivée (≠ câble débranché)
            public string NomFilaire = "";
            public string NomSansFil = "";
        }

        /// <summary>
        /// Verdict PUR. null = rien à dire. Sinon le texte du constat.
        ///
        /// Aucun reproche quand le filaire est déjà actif, ni quand il n'existe pas. Le seul cas
        /// signalé est celui où la solution est à portée de main.
        /// </summary>
        public static string Verdict(Etat e)
        {
            if (e == null) return null;
            if (!e.SansFilActif) return null;      // pas en Wi-Fi : rien à dire
            if (e.FilaireActif) return null;       // le câble est déjà là
            if (!e.FilairePresent) return null;    // pas de port : le reproche serait absurde

            string quoi = string.IsNullOrEmpty(e.NomFilaire) ? "Une carte Ethernet" : "« " + e.NomFilaire + " »";
            return e.FilaireDesactive
                ? "Tu joues en Wi-Fi alors que " + quoi + " est DÉSACTIVÉE (pas seulement débranchée). "
                + "Le filaire supprime la gigue du sans-fil et allège la latence DPC — c'est le gain "
                + "de latence le plus net, et il ne coûte rien."
                : "Tu joues en Wi-Fi alors que " + quoi + " est présente mais sans câble. "
                + "Le filaire supprime la gigue du sans-fil et allège la latence DPC — c'est le gain "
                + "de latence le plus net, et il ne coûte rien.";
        }

        /// <summary>Niveau du constat : 1 (à améliorer) s'il y a quelque chose à dire, sinon 0.</summary>
        public static int Niveau(Etat e) { return Verdict(e) == null ? 0 : 1; }

        /// <summary>PUR : ce nom d'interface désigne-t-il une carte VIRTUELLE ? Hyper-V, WSL, VPN et
        /// boucles logicielles ne disent rien de la façon dont la machine est réellement reliée.</summary>
        public static bool EstVirtuelle(string nom)
        {
            if (string.IsNullOrEmpty(nom)) return true;
            string n = nom.ToLowerInvariant();
            return n.Contains("virtual") || n.Contains("vethernet") || n.Contains("hyper-v")
                || n.Contains("loopback") || n.Contains("tap-") || n.Contains("tunnel")
                || n.Contains("vpn") || n.Contains("wintun") || n.Contains("wireguard")
                || n.Contains("bluetooth") || n.Contains("wan miniport");
        }

        /// <summary>PUR : ce nom désigne-t-il une carte sans fil ?</summary>
        public static bool EstSansFil(string nom)
        {
            if (string.IsNullOrEmpty(nom)) return false;
            string n = nom.ToLowerInvariant();
            return n.Contains("wi-fi") || n.Contains("wifi") || n.Contains("wireless")
                || n.Contains("802.11") || n.Contains("wlan");
        }

        /// <summary>
        /// Lit les cartes physiques de la machine.
        ///
        /// NetConnectionStatus vaut 2 quand la carte porte une connexion. « Présente mais éteinte »
        /// se lit sur NetEnabled : c'est ce qui distingue une carte désactivée dans le Gestionnaire
        /// de périphériques d'un simple câble non branché — deux situations, deux gestes.
        /// </summary>
        public static Etat Lire()
        {
            var e = new Etat();
            try
            {
                using (var s = new ManagementObjectSearcher(
                    "SELECT Name, NetConnectionStatus, NetEnabled, PhysicalAdapter FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        string nom = Convert.ToString(mo["Name"]) ?? "";
                        if (EstVirtuelle(nom)) continue;

                        int etat = 0;
                        try { etat = Convert.ToInt32(mo["NetConnectionStatus"]); } catch { }
                        bool active = etat == 2;
                        bool allumee = true;
                        try { object v = mo["NetEnabled"]; if (v != null) allumee = Convert.ToBoolean(v); } catch { }

                        if (EstSansFil(nom))
                        {
                            if (active) { e.SansFilActif = true; e.NomSansFil = nom; }
                        }
                        else
                        {
                            e.FilairePresent = true;
                            if (e.NomFilaire.Length == 0) e.NomFilaire = nom;
                            if (active) { e.FilaireActif = true; e.NomFilaire = nom; }
                            else if (!allumee) e.FilaireDesactive = true;
                        }
                    }
            }
            catch { }
            return e;
        }
    }
}
