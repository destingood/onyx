using System;

namespace BTOptimizer
{
    /// <summary>
    /// L'HYPERVISEUR QUI TOURNE POUR RIEN — le coût que personne ne voit.
    ///
    /// Le tweak « vbs_off » écrit les bonnes clés de registre. Mais sur une machine où WSL2, Docker
    /// Desktop, l'émulateur Android ou le Bac à sable Windows ont été installés, ces clés ne
    /// changent RIEN : c'est la « Plateforme de machine virtuelle » qui démarre l'hyperviseur au
    /// boot, et Windows continue de s'exécuter en partition racine, au-dessus de lui.
    ///
    /// Le coût est réel — chaque accès mémoire passe par une couche de traduction supplémentaire —
    /// et il se paie plus cher sur un processeur d'avant 2020, qui n'a pas les optimisations de
    /// virtualisation des générations suivantes. Sur les creux d'images, c'est net.
    ///
    /// Cas constaté : Windows en hyperviseur, VBS actif, et AUCUN service de sécurité en cours. La
    /// machine payait la virtualisation sans la moindre contrepartie de protection. Le constat de
    /// l'app ne se déclenchait pas, puisqu'il ne regardait que HVCI — qui, lui, était éteint.
    ///
    /// ONYX NE COUPE RIEN TOUT SEUL ICI : désactiver l'hyperviseur casse WSL2, Docker et les
    /// machines virtuelles. C'est un arbitrage qui appartient à l'utilisateur, pas à un optimiseur.
    /// </summary>
    internal static class Hyperviseur
    {
        public sealed class Etat
        {
            public bool HyperviseurActif;      // Windows tourne-t-il au-dessus d'un hyperviseur ?
            public int VbsStatus;              // 0 absent, 1 activé non lancé, 2 en cours
            public int ServicesEnCours;        // nombre de services de sécurité réellement rendus
            public bool VirtualisationUtilisee; // WSL / Docker / VM détectés
            public string QuiLUtilise;         // ce qu'on a reconnu, pour le dire à l'utilisateur
        }

        /// <summary>
        /// Décision PURE : l'hyperviseur coûte-t-il sans rien rendre ?
        /// Actif, et aucun service de sécurité rendu = pure perte. On ne se prononce PAS quand des
        /// services tournent : là, l'utilisateur paie pour une protection réelle, et l'arbitrage
        /// n'est plus le nôtre.
        /// </summary>
        public static bool CouteSansRienRendre(bool hyperviseurActif, int servicesEnCours)
        {
            return hyperviseurActif && servicesEnCours <= 0;
        }

        /// <summary>Message PUR, adapté à ce qui utilise la virtualisation.</summary>
        public static string Explique(Etat e)
        {
            if (e == null || !e.HyperviseurActif) return "";
            var sb = new System.Text.StringBuilder();
            sb.Append("Windows s'exécute AU-DESSUS d'un hyperviseur");
            if (e.ServicesEnCours <= 0)
                sb.Append(", et aucun service de sécurité ne tourne dessus — tu paies la virtualisation sans la protection");
            sb.Append(". Chaque accès mémoire passe par une couche de traduction en plus : quelques pour cent de FPS, "
                    + "davantage sur les creux, et le coût est plus lourd sur un processeur d'avant 2020.");
            if (e.VirtualisationUtilisee)
                sb.Append(" Cause : ").Append(e.QuiLUtilise)
                  .Append(" — c'est cette fonctionnalité qui démarre l'hyperviseur au boot, "
                        + "les clés de registre VBS n'y peuvent rien.");
            return sb.ToString();
        }

        /// <summary>Ce qui, sur cette machine, impose l'hyperviseur. Chaîne vide si rien de reconnu.</summary>
        public static string DetecteUsage()
        {
            var trouve = new System.Collections.Generic.List<string>();
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    string n = p.ProcessName.ToLowerInvariant();
                    if (n == "vmmem" || n == "vmmemwsl" || n == "wslservice" || n.StartsWith("wsl"))
                    { if (!trouve.Contains("WSL2")) trouve.Add("WSL2"); }
                    else if (n.StartsWith("docker") || n == "com.docker.backend")
                    { if (!trouve.Contains("Docker Desktop")) trouve.Add("Docker Desktop"); }
                    else if (n == "vmwp" || n == "vmcompute")
                    { if (!trouve.Contains("machines virtuelles Hyper-V")) trouve.Add("machines virtuelles Hyper-V"); }
                    try { p.Dispose(); } catch { }
                }
            }
            catch { }
            return string.Join(" et ", trouve.ToArray());
        }

        /// <summary>État réel de la machine.</summary>
        public static Etat Lire()
        {
            var e = new Etat();
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher("SELECT HypervisorPresent FROM Win32_ComputerSystem"))
                    foreach (System.Management.ManagementObject mo in s.Get())
                        e.HyperviseurActif = Convert.ToBoolean(mo["HypervisorPresent"]);
            }
            catch { }
            try
            {
                var scope = new System.Management.ManagementScope(@"\\.\root\Microsoft\Windows\DeviceGuard");
                scope.Connect();
                using (var s = new System.Management.ManagementObjectSearcher(scope,
                           new System.Management.ObjectQuery("SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard")))
                    foreach (System.Management.ManagementObject mo in s.Get())
                    {
                        try { e.VbsStatus = Convert.ToInt32(mo["VirtualizationBasedSecurityStatus"]); } catch { }
                        try
                        {
                            var arr = mo["SecurityServicesRunning"] as Array;
                            int n = 0;
                            if (arr != null) foreach (object o in arr) { try { if (Convert.ToInt32(o) != 0) n++; } catch { } }
                            e.ServicesEnCours = n;
                        }
                        catch { }
                    }
            }
            catch { }
            e.QuiLUtilise = DetecteUsage();
            e.VirtualisationUtilisee = e.QuiLUtilise.Length > 0;
            return e;
        }
    }
}
