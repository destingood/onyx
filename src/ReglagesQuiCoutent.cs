using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// LES RÉGLAGES QUI GONFLENT LE CHIFFRE QU'ON EST EN TRAIN DE LIRE.
    ///
    /// InstallGraphique répond à « le pilote est-il sain ». Ce module répond à une question
    /// différente et tout aussi décisive : « la machine était-elle dans un état où ce chiffre veut
    /// dire quelque chose ». Les deux peuvent être vrais en même temps, et se cumulent.
    ///
    /// TROIS CHOSES QU'ON CHERCHE, TOUTES CONSTATÉES SUR LA MACHINE DE RÉFÉRENCE :
    ///
    ///   L'ÉCART DE FRÉQUENCE. Un CPU qui tombe à 5 % de sa fréquence au bureau exécute chaque
    ///   DPC bien plus longtemps — en temps réel, pas en cycles. Le relevé au repos est alors
    ///   PESSIMISTE, et il s'améliore dès que la machine travaille. C'est exactement l'inversion
    ///   documentée dans EnqueteLatence : 0,524 ms au repos, 0,319 ms en jeu, sans qu'une ligne
    ///   n'ait changé. Ce n'est PAS un défaut — c'est le défaut Windows — mais il faut le dire,
    ///   sinon l'utilisateur cherche une panne là où il n'y a qu'un CPU endormi.
    ///
    ///   LA CONTRADICTION MSI. « Répartir les interruptions sur tous les cœurs » suppose qu'il y
    ///   ait plusieurs vecteurs à répartir. Quand le périphérique n'en a qu'UN, la politique ne
    ///   répartit rien : elle fait MIGRER l'unique interruption de cœur en cœur, et chaque DPC
    ///   arrive sur un cache froid. Le réglage censé aider coûte alors du temps, et il coûte
    ///   d'autant plus que le pilote est fréquent.
    ///
    ///   L'HYPERVISEUR SANS CONTREPARTIE. Windows peut tourner au-dessus de l'hyperviseur sans
    ///   qu'aucun service de sécurité ne s'exécute. Le coût est payé, le bénéfice est nul.
    ///   L'utilisateur mérite de savoir qu'il n'échange rien contre rien.
    ///
    /// CE MODULE NE CORRIGE RIEN. Il constate, explique, et donne la commande — le geste reste
    /// à l'utilisateur, parce que deux des trois coûtent quelque chose de réel.
    /// </summary>
    internal static class ReglagesQuiCoutent
    {
        // ==================================================================
        //  Seuils
        // ==================================================================

        /// <summary>Rapport MAX/MIN de l'état processeur au-delà duquel un relevé au repos et un
        /// relevé en charge ne mesurent plus la même machine.</summary>
        public const int RatioFrequenceQuiFausse = 4;

        /// <summary>Politique d'interruption « réparties sur tous les processeurs ».</summary>
        public const int IrqRepartieSurTous = 5;

        /// <summary>Non mesuré — jamais à confondre avec zéro.</summary>
        public const int Inconnu = -1;

        // ==================================================================

        public sealed class Etat
        {
            public bool Lu;

            public int EtatProcMin = Inconnu;      // %
            public int EtatProcMax = Inconnu;      // %

            public string Gpu = "";
            public int MsiSupporte = Inconnu;      // MSISupported
            public int VecteursMsi = Inconnu;      // MessageNumberLimit
            public int PolitiqueIrq = Inconnu;     // DevicePolicy
            public int PrioriteIrq = Inconnu;      // DevicePriority

            public int Hyperviseur = Inconnu;      // VirtualizationBasedSecurityStatus
            public int ServicesSecurite = Inconnu; // nombre de services actifs
        }

        public sealed class Indice
        {
            /// <summary>2 = coûte de la latence, 1 = fausse la lecture ou s'arbitre.</summary>
            public int Gravite;
            public string Constat = "";
            public string Pourquoi = "";
            /// <summary>Le geste exact. Vide s'il n'y en a pas de sûr.</summary>
            public string Correction = "";
            /// <summary>Ce que la correction coûte. Vide = rien à perdre.</summary>
            public string Cout = "";
        }

        // ==================================================================
        //  Analyse — PURE
        // ==================================================================

        /// <summary>PUR : les réglages de cet état qui pèsent sur la latence ou sur sa lecture.</summary>
        public static List<Indice> Analyse(Etat e)
        {
            var l = new List<Indice>();
            if (e == null || !e.Lu) return l;
            EcartDeFrequence(e, l);
            ContradictionMsi(e, l);
            HyperviseurSansContrepartie(e, l);
            return l;
        }

        /// <summary>PUR : le CPU descend-il si bas que le relevé au repos en devient pessimiste ?</summary>
        private static void EcartDeFrequence(Etat e, List<Indice> l)
        {
            if (e.EtatProcMin <= 0 || e.EtatProcMax <= 0) return;
            int ratio = e.EtatProcMax / e.EtatProcMin;
            if (ratio < RatioFrequenceQuiFausse) return;

            l.Add(new Indice
            {
                Gravite = 1,
                Constat = "état processeur MIN " + e.EtatProcMin + " % / MAX " + e.EtatProcMax
                        + " % — jusqu'à " + ratio + "× d'écart de fréquence",
                Pourquoi = "Au bureau, le processeur descend très bas : chaque DPC s'exécute alors "
                         + "plus longtemps EN TEMPS RÉEL, sans que le pilote y soit pour rien. Ton "
                         + "relevé au repos est pessimiste, et il s'améliorera tout seul dès que la "
                         + "machine travaillera. C'est le réglage Windows par défaut — ce n'est pas "
                         + "une panne — mais tant qu'il est là, un relevé au repos ne se compare "
                         + "à AUCUN relevé en charge.",
                Correction = "powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMIN 100"
                           + "  puis  powercfg /setactive SCHEME_CURRENT",
                Cout = "Le processeur ne redescend plus au repos : consommation, chaleur et "
                     + "ventilation en hausse en permanence. Sur portable, l'autonomie chute. "
                     + "Réversible en remettant " + e.EtatProcMin + "."
            });
        }

        /// <summary>PUR : répartir une interruption unique ne la répartit pas, il la fait migrer.</summary>
        private static void ContradictionMsi(Etat e, List<Indice> l)
        {
            if (e.PolitiqueIrq != IrqRepartieSurTous) return;
            bool vecteurUnique = e.VecteursMsi == 1;
            bool sansMsi = e.MsiSupporte == 0;
            if (!vecteurUnique && !sansMsi) return;

            string quoi = vecteurUnique
                ? "un seul vecteur MSI (MessageNumberLimit = 1)"
                : "des interruptions par ligne (MSI désactivé)";

            l.Add(new Indice
            {
                Gravite = 2,
                Constat = (e.Gpu.Length > 0 ? e.Gpu : "le GPU") + " a " + quoi
                        + ", mais sa politique d'interruption est « répartie sur tous les cœurs »",
                Pourquoi = "Répartir suppose qu'il y ait plusieurs vecteurs à distribuer. Avec un "
                         + "seul, la politique ne répartit rien — elle fait MIGRER l'unique "
                         + "interruption d'un cœur à l'autre, et chaque DPC arrive sur un cache "
                         + "froid qu'il faut recharger. Le réglage censé alléger la charge "
                         + "l'alourdit, d'autant plus que le pilote est fréquent.",
                Correction = "Remettre la politique par défaut : supprimer « DevicePolicy » sous "
                           + "Device Parameters\\Interrupt Management\\Affinity Policy du GPU "
                           + "(ou annuler le levier « irq_spread_producteurs »). "
                           + "La priorité haute, elle, peut rester.",
                Cout = "Redémarrage nécessaire pour que la politique reprenne effet."
            });
        }

        /// <summary>PUR : hyperviseur en marche, aucun service de sécurité derrière.</summary>
        private static void HyperviseurSansContrepartie(Etat e, List<Indice> l)
        {
            if (e.Hyperviseur < 2 || e.ServicesSecurite != 0) return;
            l.Add(new Indice
            {
                Gravite = 1,
                Constat = "Windows tourne au-dessus de l'hyperviseur, mais AUCUN service de "
                        + "sécurité ne s'exécute",
                Pourquoi = "Le coût de la virtualisation est payé sur chaque accès mémoire du "
                         + "noyau — la gestion mémoire du GPU en particulier — sans qu'aucune "
                         + "protection (intégrité du code, Credential Guard) ne soit rendue en "
                         + "échange. C'est en général Hyper-V, WSL ou le bac à sable qui "
                         + "l'allument, pas la sécurité.",
                Correction = "Si WSL, Docker, le bac à sable et les machines virtuelles ne te "
                           + "servent pas : désactive ces fonctionnalités Windows, puis "
                           + "bcdedit /set hypervisorlaunchtype off",
                Cout = "WSL2, Docker, le bac à sable Windows et Hyper-V cessent de fonctionner. "
                     + "Redémarrage nécessaire, et l'opération se défait aussi facilement."
            });
        }

        /// <summary>PUR : le rapport, ou vide s'il n'y a rien d'honnête à dire.</summary>
        public static string Rapport(Etat e)
        {
            List<Indice> ind = Analyse(e);
            if (ind.Count == 0) return "";
            ind.Sort((a, b) => b.Gravite.CompareTo(a.Gravite));

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("RÉGLAGES QUI PÈSENT SUR CE CHIFFRE");
            foreach (Indice i in ind)
            {
                sb.AppendLine("   " + (i.Gravite >= 2 ? "⚠" : "•") + " " + i.Constat);
                sb.AppendLine("        " + i.Pourquoi);
                if (i.Correction.Length > 0) sb.AppendLine("        GESTE : " + i.Correction);
                if (i.Cout.Length > 0) sb.AppendLine("        COÛT : " + i.Cout);
            }
            return sb.ToString();
        }

        // ==================================================================
        //  Lecture machine — IMPURE, et qui n'a pas le droit de lever
        // ==================================================================

        public static Etat Lire()
        {
            var e = new Etat();
            try
            {
                LitEtatProcesseur(e);
                LitInterruptionsGpu(e);
                LitHyperviseur(e);
                e.Lu = true;
            }
            catch { e.Lu = false; }
            return e;
        }

        // --- État processeur : par l'API, pas par powercfg -----------------
        // powercfg rend un texte TRADUIT ; le parser oblige à deviner la langue de la machine et
        // casse dès qu'elle change. PowerReadACValueIndex rend l'entier effectif, y compris quand
        // le réglage n'a jamais été écrit dans le registre et vaut le défaut du plan.

        private static readonly Guid SubProcesseur = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        private static readonly Guid ProcMin = new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c");
        private static readonly Guid ProcMax = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec");

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme,
            ref Guid sousGroupe, ref Guid reglage, out uint valeur);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr p);

        private static void LitEtatProcesseur(Etat e)
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                if (PowerGetActiveScheme(IntPtr.Zero, out p) != 0 || p == IntPtr.Zero) return;
                Guid scheme = (Guid)Marshal.PtrToStructure(p, typeof(Guid));
                e.EtatProcMin = LitValeur(ref scheme, ProcMin);
                e.EtatProcMax = LitValeur(ref scheme, ProcMax);
            }
            finally { if (p != IntPtr.Zero) LocalFree(p); }
        }

        private static int LitValeur(ref Guid scheme, Guid reglage)
        {
            Guid sub = SubProcesseur;
            uint v;
            return PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref reglage, out v) == 0
                ? (int)v : Inconnu;
        }

        // --- Interruptions du GPU ------------------------------------------
        // On cherche l'instance PCI dont le service est celui du pilote graphique. Le chemin
        // dépend du matériel : il faut énumérer, aucun raccourci n'est fiable.

        private const string EnumPci = @"SYSTEM\CurrentControlSet\Enum\PCI";

        private static readonly string[] ServicesGraphiques = { "nvlddmkm", "amdkmdag", "igfx", "igdkmd64" };

        private static void LitInterruptionsGpu(Etat e)
        {
            using (RegistryKey pci = Registry.LocalMachine.OpenSubKey(EnumPci))
            {
                if (pci == null) return;
                foreach (string dev in pci.GetSubKeyNames())
                    using (RegistryKey d = pci.OpenSubKey(dev))
                    {
                        if (d == null) continue;
                        foreach (string inst in d.GetSubKeyNames())
                            if (LitInstance(d, inst, e)) return;
                    }
            }
        }

        private static bool LitInstance(RegistryKey parent, string inst, Etat e)
        {
            using (RegistryKey k = parent.OpenSubKey(inst))
            {
                if (k == null) return false;
                string svc = k.GetValue("Service") as string;
                if (string.IsNullOrEmpty(svc) || Array.IndexOf(ServicesGraphiques, svc.ToLowerInvariant()) < 0)
                    return false;

                e.Gpu = (k.GetValue("DeviceDesc") as string) ?? "";
                int coupe = e.Gpu.LastIndexOf(';');
                if (coupe >= 0 && coupe + 1 < e.Gpu.Length) e.Gpu = e.Gpu.Substring(coupe + 1);

                const string Irq = @"Device Parameters\Interrupt Management\";
                using (RegistryKey m = k.OpenSubKey(Irq + "MessageSignaledInterruptProperties"))
                    if (m != null)
                    {
                        e.MsiSupporte = Entier(m, "MSISupported");
                        e.VecteursMsi = Entier(m, "MessageNumberLimit");
                    }
                using (RegistryKey a = k.OpenSubKey(Irq + "Affinity Policy"))
                    if (a != null)
                    {
                        e.PolitiqueIrq = Entier(a, "DevicePolicy");
                        e.PrioriteIrq = Entier(a, "DevicePriority");
                    }
                return true;
            }
        }

        private static int Entier(RegistryKey k, string nom)
        {
            object v = k.GetValue(nom);
            return v is int ? (int)v : Inconnu;
        }

        // --- Hyperviseur ----------------------------------------------------

        private static void LitHyperviseur(Etat e)
        {
            using (var s = new System.Management.ManagementObjectSearcher(
                @"root\Microsoft\Windows\DeviceGuard",
                "SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard"))
            {
                foreach (System.Management.ManagementObject mo in s.Get())
                {
                    object st = mo["VirtualizationBasedSecurityStatus"];
                    if (st != null) e.Hyperviseur = Convert.ToInt32(st);

                    var svc = mo["SecurityServicesRunning"] as uint[];
                    // Windows rend {0} — un service « aucun » — quand rien ne tourne : le tableau
                    // n'est pas vide, il contient un zéro. Compter sa longueur dirait « 1 service ».
                    int n = 0;
                    if (svc != null) foreach (uint v in svc) if (v != 0) n++;
                    e.ServicesSecurite = n;
                    return;
                }
            }
        }
    }
}
