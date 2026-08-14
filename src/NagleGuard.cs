using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// LE RÉGLAGE ANTI-NAGLE NE TIENT PAS TOUT SEUL DANS LE TEMPS.
    ///
    /// TcpAckFrequency et TCPNoDelay ne vivent pas dans une clé globale : la pile TCP les lit dans
    /// UNE SOUS-CLÉ PAR CARTE RÉSEAU. Le réglage est donc appliqué aux cartes présentes le jour où
    /// on clique — et à elles seules.
    ///
    /// Tout ce qui crée une carte plus tard passe à travers : installer WSL, Docker ou Hyper-V, un
    /// VPN, un adaptateur USB-Ethernet, une nouvelle carte Wi-Fi, ou simplement un pilote réseau
    /// réinstallé (Windows recrée alors une sous-clé neuve, sans les valeurs). Constaté sur une
    /// machine réelle : 14 interfaces réglées, une non — l'adaptateur virtuel Hyper-V, apparu après.
    ///
    /// Ce garde recolle les manquantes au démarrage. Une règle stricte le gouverne : IL N'ACTIVE
    /// JAMAIS LE RÉGLAGE DE SA PROPRE INITIATIVE. S'il ne trouve aucune interface déjà réglée, il ne
    /// fait rien — sinon un réglage volontairement retiré par l'utilisateur reviendrait tout seul au
    /// lancement suivant, ce qui est exactement le comportement qu'on reproche aux « optimiseurs ».
    /// Il ne fait que propager un choix DÉJÀ pris.
    /// </summary>
    internal static class NagleGuard
    {
        private const string Racine = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

        /// <summary>Photo des interfaces : combien portent le réglage, combien ne l'ont pas.</summary>
        public sealed class Etat
        {
            public int Total;
            public int Reglees;
            public List<string> Manquantes = new List<string>();
        }

        /// <summary>
        /// Décision PURE. On ne soigne QUE si le réglage est déjà en usage (au moins une interface
        /// réglée) ET qu'il en manque au moins une. Zéro réglée = l'utilisateur n'en veut pas, ou ne
        /// l'a jamais posé : on n'y touche pas.
        /// </summary>
        public static bool DoitSoigner(int reglees, int manquantes)
        {
            return reglees > 0 && manquantes > 0;
        }

        /// <summary>Texte PUR du constat, pour le journal et le diagnostic.</summary>
        public static string Texte(Etat e)
        {
            if (e == null || e.Total == 0) return null;
            if (!DoitSoigner(e.Reglees, e.Manquantes.Count)) return null;
            return e.Manquantes.Count + " carte(s) réseau sur " + e.Total + " n'avaient pas le réglage "
                 + "anti-Nagle (apparues après son application). Il a été recollé.";
        }

        /// <summary>Lit l'état réel des interfaces. Total = 0 si la clé est illisible.</summary>
        public static Etat Inventaire()
        {
            var e = new Etat();
            try
            {
                using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(Racine, false))
                {
                    if (rk == null) return e;
                    foreach (string enfant in rk.GetSubKeyNames())
                    {
                        using (RegistryKey ik = rk.OpenSubKey(enfant, false))
                        {
                            if (ik == null) continue;
                            e.Total++;
                            if (Sys.IntEquals(ik.GetValue("TcpAckFrequency"), 1)
                                && Sys.IntEquals(ik.GetValue("TCPNoDelay"), 1))
                                e.Reglees++;
                            else
                                e.Manquantes.Add(enfant);
                        }
                    }
                }
            }
            catch { }
            return e;
        }

        /// <summary>
        /// Recolle le réglage sur les interfaces qui l'ont perdu. Rend le nombre d'interfaces
        /// effectivement traitées — 0 si rien n'était à faire, ou si l'écriture a été refusée.
        /// </summary>
        public static int Soigne(Action<string, int> log)
        {
            Etat e = Inventaire();
            if (!DoitSoigner(e.Reglees, e.Manquantes.Count)) return 0;

            int faites = 0;
            try
            {
                using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(Racine, true))
                {
                    if (rk == null) return 0;
                    foreach (string enfant in e.Manquantes)
                    {
                        try
                        {
                            using (RegistryKey ik = rk.OpenSubKey(enfant, true))
                            {
                                if (ik == null) continue;
                                ik.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                                ik.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                                faites++;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { return 0; }

            if (faites > 0 && log != null)
                log(faites + " carte(s) réseau ont retrouvé le réglage anti-Nagle (elles sont "
                  + "apparues après son application — WSL, VPN, pilote réinstallé…).", 1);
            return faites;
        }
    }
}
