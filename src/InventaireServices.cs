using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Management;

namespace BTOptimizer
{
    /// <summary>
    /// LES SERVICES QUI TOURNENT VRAIMENT SUR CETTE MACHINE — PAS CEUX D'UNE LISTE RECOPIÉE.
    ///
    /// ONYX savait déjà couper seize services Windows connus (fenêtre « Services Windows ») et en
    /// suspendre sept pendant une partie (Mode Jeu). Les deux listes sont ÉCRITES EN DUR, et c'est
    /// leur défaut : une machine de joueur porte entre 180 et 260 services, dont trente à soixante
    /// installés par des logiciels tiers — suites constructeur, mises à jour de navigateurs,
    /// lanceurs de jeux, pilotes de périphériques RGB. Ce sont exactement ceux qui consomment, et
    /// aucune liste figée ne peut les connaître : ils dépendent de ce que la personne a installé.
    ///
    /// Ce module part donc dans l'autre sens : il ÉNUMÈRE ce qui existe ici, mesure ce que chacun
    /// coûte, et ne classe qu'ensuite.
    ///
    /// TROIS RÈGLES QU'IL NE VIOLE JAMAIS — chacune corrige une erreur que font les « optimiseurs »
    /// grand public, et deux d'entre elles cassent réellement des machines :
    ///
    ///   1. NE JAMAIS TOUCHER À UN ANTICHEAT. vgc (Vanguard), EasyAntiCheat, BEService (BattlEye),
    ///      FACEIT : arrêter ces services empêche le jeu de démarrer, et la manipulation d'un
    ///      anticheat pendant qu'il tourne est précisément ce que ces logiciels surveillent. Un
    ///      « boost » qui les arrête peut coûter un compte. Ils sont marqués VITAL, donc jamais
    ///      proposés, et le filtre est appliqué une SECONDE fois au moment d'agir.
    ///
    ///   2. NE RIEN PROPOSER QUI NE RAPPORTE RIEN. Un service en démarrage MANUEL et arrêté
    ///      n'occupe ni mémoire ni processeur : le « désactiver » ne libère rien du tout. C'est
    ///      pourtant la moitié du contenu des tutoriels. Ici le gain est MESURÉ (mémoire réelle,
    ///      processeur sur une fenêtre de mesure) et le verdict le dit quand il est nul.
    ///
    ///   3. PRÉFÉRER SUSPENDRE À DÉSACTIVER. Arrêter un service le temps d'une partie rend le même
    ///      gain qu'une désactivation permanente — sans laisser la machine amputée trois semaines
    ///      plus tard devant une page de Paramètres vide (voir <see cref="ServiceGuard"/>, qui
    ///      répare déjà ce dégât). La désactivation n'est proposée que là où le service ne rend
    ///      objectivement plus rien sur un PC de jeu.
    ///
    /// CE QU'IL NE SAIT PAS FAIRE, ET LE DIT :
    ///
    ///   La mémoire d'un service hébergé dans un svchost partagé n'est pas mesurable séparément.
    ///   Le processus est commun à plusieurs services ; leur attribuer à chacun la totalité
    ///   afficherait quatre fois 90 Mo pour 90 Mo réels. On divise donc par le nombre de
    ///   colocataires et le champ <see cref="Service.Partage"/> le signale — c'est une estimation,
    ///   pas une mesure, et le tableau l'écrit.
    /// </summary>
    internal static class InventaireServices
    {
        /// <summary>Ce qu'on peut dire d'un service, du plus intouchable au plus superflu.</summary>
        public enum Verdict
        {
            /// <summary>Jamais proposé, jamais touché : Windows ou le jeu en dépendent.</summary>
            Vital,
            /// <summary>Ça sert. On le laisse, et on explique pourquoi les listes se trompent.</summary>
            Utile,
            /// <summary>Arrêtable pendant une partie, relancé après. Rien de permanent.</summary>
            Suspendable,
            /// <summary>Ne rend plus rien sur un PC de jeu : désactivation défendable.</summary>
            Inutile,
            /// <summary>Service tiers non catalogué. On montre l'éditeur et le coût ; la décision
            /// revient à qui a installé le logiciel — ONYX ne devine pas à sa place.</summary>
            Inconnu
        }

        /// <summary>Un service tel qu'il est SUR CETTE MACHINE, avec son coût réel.</summary>
        public sealed class Service
        {
            public string Nom = "";          // nom court (celui de sc.exe)
            public string Libelle = "";      // nom affiché par Windows
            public string Chemin = "";       // ligne de commande du binaire
            public string Editeur = "";      // société, lue dans le binaire
            public int Start = 3;            // 2 = automatique, 3 = manuel, 4 = désactivé
            public bool Retarde;             // automatique (démarrage différé)
            public bool EnCours;
            public int Pid;
            public double RamMo;             // 0 si arrêté — un service arrêté ne coûte rien
            public double Cpu;               // % du processeur TOTAL de la machine
            public int Partage = 1;          // nombre de services dans le même processus
            public Verdict Verdict = Verdict.Inconnu;
            public string Motif = "";        // pourquoi ce verdict, en français
        }

        // ==================================================================
        //  Catalogues — PURS, donc vérifiables sans machine
        // ==================================================================

        /// <summary>
        /// Services sans lesquels Windows, le son, le réseau, la manette ou l'anticheat cessent de
        /// fonctionner. Aucun n'est proposé à l'arrêt, sous aucun préréglage.
        ///
        /// La liste est volontairement plus longue que celle des tutoriels : chaque nom ajouté ici
        /// est un nom qu'une liste populaire propose de couper.
        /// </summary>
        private static readonly string[] Vitaux =
        {
            // Noyau de l'espace utilisateur : plus rien ne démarre sans eux.
            "RpcSs", "RpcEptMapper", "DcomLaunch", "Power", "PlugPlay", "Winmgmt", "EventLog",
            "Schedule", "LSM", "ProfSvc", "gpsvc", "BrokerInfrastructure", "SystemEventsBroker",
            "CoreMessagingRegistrar", "StateRepository", "UserManager", "SamSs", "EventSystem",
            "SENS", "DeviceInstall", "ShellHWDetection", "TimeBrokerSvc",
            // Réseau : couper l'un d'eux, c'est perdre la connexion, pas gagner des images.
            "Dhcp", "Dnscache", "NlaSvc", "netprofm", "nsi", "Netman", "WlanSvc", "LanmanWorkstation",
            // Pare-feu et filtrage : la surface d'attaque n'est pas une option de confort.
            "BFE", "mpssvc",
            // Son : le premier réflexe des listes « latence » est de couper l'audio. Il n'y a
            // aucun gain, et la sortie son disparaît.
            "Audiosrv", "AudioEndpointBuilder",
            // Licence, chiffrement, installation : ce qui casse silencieusement et durablement.
            "CryptSvc", "TrustedInstaller", "msiserver", "sppsvc", "ClipSVC", "LicenseManager",
            "AppXSvc", "DsmSvc",
            // Sécurité. Un optimiseur qui coupe l'antivirus rend la machine plus rapide et la
            // personne plus vulnérable ; ce n'est pas un arbitrage qu'ONYX prend à sa place.
            // Defender ne se limite pas à WinDefend : sa plate-forme vit hors de system32 (sous
            // ProgramData), ce qui la ferait passer pour un « service tiers » à ses propres yeux.
            // Relevé sur la machine de référence : WdNisSvc et MDCoreSvc arrivaient en « inconnu ».
            "WinDefend", "SecurityHealthService", "wscsvc", "Sense", "WdNisSvc", "MDCoreSvc",
            "SgrmBroker", "webthreatdefsvc",
            // Mises à jour : sans elles, plus de correctifs de sécurité. Le Mode Jeu peut les
            // suspendre le temps d'une partie ; les désactiver est un tout autre geste.
            "wuauserv", "BITS", "UsoSvc",
            // Entrées : le clavier multimédia, la manette filaire Xbox, le Bluetooth des casques
            // et manettes sans fil. Trois façons classiques de « gagner » un service et de perdre
            // son périphérique de jeu.
            "hidserv", "XboxGipSvc", "GameInputSvc", "GameInputRedistService", "bthserv", "BthAvctpSvc",
            // Affichage NVIDIA : le conteneur porte le panneau de configuration et les réglages
            // de pilote. À ne pas confondre avec le conteneur de télémétrie, lui suspendable.
            "NVDisplay.ContainerLocalSystem",
            // ANTICHEAT — la règle 1. Voir l'en-tête : arrêter ces services empêche le jeu de
            // démarrer et se voit du côté de l'éditeur.
            "vgc", "vgk", "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "FACEIT",
            "ESEADriver2", "PnkBstrA", "PnkBstrB"
        };

        /// <summary>Services qu'on laisse tourner, avec le motif — c'est-à-dire l'erreur qu'on
        /// évite de reproduire. Ils apparaissent dans le tableau, mais jamais cochés.</summary>
        private static readonly string[][] Utiles =
        {
            new[] { "PcaSvc", "l'assistant de compatibilité : c'est lui qui fait démarrer les vieux jeux. Le couper ne rend rien de mesurable et casse un jour un lancement" },
            new[] { "WerSvc", "les rapports d'erreur : ONYX les lit pour expliquer les plantages. Désactivé, l'analyse de plantage n'a plus rien à lire" },
            new[] { "DPS", "diagnostics système : les dépanneurs Windows et plusieurs relevés d'ONYX passent par lui" },
            new[] { "XblAuthManager", "Xbox Live : nécessaire au Game Pass et aux jeux du Store" },
            new[] { "XblGameSave", "sauvegardes Xbox dans le nuage" },
            new[] { "XboxNetApiSvc", "réseau Xbox Live (multijoueur des jeux du Store)" },
            new[] { "GamingServices", "installation et lancement des jeux du Game Pass" },
            new[] { "GamingServicesNet", "réseau des services de jeu du Store" },
            new[] { "Themes", "sans lui l'interface repasse en apparence classique, pour zéro image gagnée" },
            new[] { "TextInputManagementService", "saisie de texte : le couper rend le clavier inutilisable dans certaines applications" }
        };

        /// <summary>
        /// Services arrêtables LE TEMPS D'UNE PARTIE. Aucun n'est désactivé : Windows les relance
        /// à la demande, et le Mode Jeu les relance à la sortie.
        /// </summary>
        private static readonly string[][] Suspendables =
        {
            new[] { "SysMain", "préchargement disque : utile au démarrage, inutile pendant une partie déjà chargée. À NE PAS désactiver sur disque mécanique" },
            new[] { "WSearch", "indexation des fichiers : lit le disque en arrière-plan, exactement pendant qu'un jeu le lit aussi" },
            new[] { "Spooler", "spouleur d'impression : rien à imprimer pendant une partie" },
            new[] { "PrintNotify", "notifications d'impression" },
            new[] { "DoSvc", "optimisation de livraison : télécharge et PARTAGE des mises à jour sur le réseau, ce qui se voit sur le ping" },
            new[] { "WMPNetworkSvc", "partage média sur le réseau" },
            new[] { "SSDPSRV", "découverte des appareils du réseau (UPnP)" },
            new[] { "upnphost", "hôte UPnP" },
            new[] { "TabletInputService", "saisie tactile et stylet" },
            new[] { "NvTelemetryContainer", "télémétrie NVIDIA — à ne pas confondre avec le conteneur d'affichage, lui indispensable" },
            new[] { "NvContainerLocalSystem", "conteneur de service NVIDIA (superposition, relevés) : interroge les capteurs pendant la partie" }
        };

        /// <summary>
        /// Services qui, sur un PC de JEU, ne rendent plus rien. La désactivation y est défendable
        /// — elle reste réversible, et le journal garde l'état précédent.
        /// </summary>
        private static readonly string[][] Inutiles =
        {
            new[] { "DiagTrack", "télémétrie Windows : collecte et envoi de diagnostics d'usage" },
            new[] { "dmwappushservice", "acheminement de messages de télémétrie" },
            new[] { "diagnosticshub.standardcollector.service", "collecteur de diagnostics pour développeurs" },
            new[] { "RetailDemo", "mode démonstration de magasin : n'a de sens que sur une borne d'exposition" },
            new[] { "Fax", "envoi de fax" },
            new[] { "RemoteRegistry", "modification du registre depuis le réseau : surface d'attaque pure sur une machine personnelle" },
            new[] { "MapsBroker", "téléchargement de cartes hors ligne en arrière-plan" },
            new[] { "WalletService", "portefeuille de paiement Windows" },
            new[] { "SEMgrSvc", "paiements sans contact (NFC)" },
            new[] { "AJRouter", "AllJoyn : protocole d'objets connectés" },
            new[] { "wisvc", "programme Windows Insider" },
            new[] { "icssvc", "partage de connexion mobile" },
            new[] { "WpcMonSvc", "contrôle parental" },
            new[] { "lfsvc", "géolocalisation : un PC fixe ne se déplace pas" }
        };

        /// <summary>Éditeurs et familles de services TIERS dont l'arrêt pendant une partie est sûr.
        /// Reconnus par motif, parce qu'ils changent de nom à chaque version.</summary>
        private static readonly string[][] MotifsTiers =
        {
            new[] { "razer",     "suite Razer : relevés de périphériques et effets lumineux" },
            new[] { "corsair",   "suite Corsair (iCUE) : interroge les capteurs par un bus lent et bloquant" },
            new[] { "logi",      "suite Logitech : relevés de périphériques" },
            new[] { "armoury",   "ASUS Armoury Crate : relevés de capteurs et effets lumineux" },
            new[] { "asus",      "service ASUS : relevés de capteurs" },
            new[] { "msi",       "service MSI : relevés de capteurs et effets lumineux" },
            new[] { "gigabyte",  "service Gigabyte : relevés de capteurs" },
            new[] { "nzxt",      "service NZXT : relevés de capteurs" },
            new[] { "nahimic",   "Nahimic : traitement audio superposé au pilote" },
            new[] { "alienware", "suite Alienware : relevés et éclairage" },
            new[] { "afterburner", "MSI Afterburner : relevé permanent des capteurs GPU" },
            new[] { "hwinfo",    "HWiNFO : relevé permanent des capteurs" },
            new[] { "aida",      "AIDA64 : relevé permanent des capteurs" },
            new[] { "openrgb",   "OpenRGB : pilotage de l'éclairage" },
            new[] { "update",    "service de mise à jour : il attend, vérifie et télécharge — rien de tout cela n'a lieu d'être pendant une partie" },
            new[] { "updater",   "service de mise à jour" },
            new[] { "adobearm",  "Adobe Updater" },
            new[] { "mozillamaintenance", "maintenance Firefox" },
            new[] { "brave",     "service de mise à jour Brave" },
            new[] { "dropbox",   "synchronisation Dropbox : lit et écrit sur le disque en continu" },
            new[] { "onedrive",  "synchronisation OneDrive" },
            new[] { "googledrive", "synchronisation Google Drive" }
        };

        /// <summary>Motifs de nom qui désignent un ANTICHEAT, quel que soit l'éditeur. Doublon
        /// volontaire avec la table des vitaux : un anticheat INCONNU doit être protégé aussi.</summary>
        private static readonly string[] MotifsAnticheat =
        {
            "anticheat", "anti-cheat", "battleye", "beservice", "vanguard", "vgc", "vgk",
            "faceit", "punkbuster", "pnkbstr", "gameguard", "xigncode", "denuvo", "ricochet"
        };

        /// <summary>
        /// Motifs qui désignent un logiciel de SÉCURITÉ, quel que soit l'éditeur.
        ///
        /// Une machine sur deux porte un antivirus qui n'est pas Defender, sous un nom qu'aucune
        /// table ne contient. Se tromper ici n'a pas deux coûts symétriques : protéger à tort un
        /// service, c'est laisser 20 Mo occupés ; l'arrêter à tort, c'est désarmer la machine
        /// pendant que son propriétaire croit avoir gagné des images.
        /// </summary>
        private static readonly string[] MotifsSecurite =
        {
            "defender", "antivirus", "anti-virus", "endpoint protection", "msmpeng",
            "kaspersky", "bitdefender", "eset", "malwarebytes", "avast", "avg secure", "norton"
        };

        /// <summary>Noms protégés en plus du catalogue, fournis par l'appelant. Le garde-fou
        /// <see cref="ServiceGuard"/> vit là-bas ; on ne recopie pas sa table ici — une copie
        /// diverge, et c'est justement ce module qui casserait ce qu'elle protège.</summary>
        private static readonly HashSet<string> _protegesEnPlus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void ProtegeAussi(IEnumerable<string> noms)
        {
            if (noms == null) return;
            foreach (string n in noms) if (!string.IsNullOrEmpty(n)) _protegesEnPlus.Add(n.Trim());
        }

        /// <summary>Services que l'appelant sait déjà suspendables — en pratique les sondes
        /// matérielles que le Mode Jeu arrête depuis toujours. Sans ce raccord, ONYX les
        /// suspendrait d'un côté en les déclarant « inconnus » de l'autre.</summary>
        private static readonly HashSet<string> _suspendablesEnPlus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void SuspendableAussi(IEnumerable<string> noms)
        {
            if (noms == null) return;
            foreach (string n in noms) if (!string.IsNullOrEmpty(n)) _suspendablesEnPlus.Add(n.Trim());
        }

        // ==================================================================
        //  Décisions — PURES
        // ==================================================================

        private static bool Contient(string sujet, string motif)
        {
            return !string.IsNullOrEmpty(sujet)
                && sujet.IndexOf(motif, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool DansTable(string[][] table, string nom, out string motif)
        {
            motif = "";
            if (string.IsNullOrEmpty(nom)) return false;
            foreach (string[] e in table)
                if (string.Equals(e[0], nom, StringComparison.OrdinalIgnoreCase)) { motif = e[1]; return true; }
            return false;
        }

        /// <summary>PUR : ce service est-il un anticheat ? Le nom, le libellé ET le chemin sont
        /// examinés — un anticheat se nomme rarement pareil dans les trois.</summary>
        public static bool EstAnticheat(string nom, string libelle, string chemin)
        {
            foreach (string m in MotifsAnticheat)
                if (Contient(nom, m) || Contient(libelle, m) || Contient(chemin, m)) return true;
            return false;
        }

        /// <summary>PUR : ce service appartient-il à un logiciel de sécurité ?</summary>
        public static bool EstSecurite(string nom, string libelle, string chemin)
        {
            foreach (string m in MotifsSecurite)
                if (Contient(nom, m) || Contient(libelle, m) || Contient(chemin, m)) return true;
            return false;
        }

        /// <summary>PUR : intouchable ? Catalogue des vitaux, anticheats, antivirus, et noms
        /// protégés en plus.</summary>
        public static bool EstVital(string nom, string libelle, string chemin)
        {
            if (string.IsNullOrEmpty(nom)) return true;   // sans nom, on ne touche à rien
            if (_protegesEnPlus.Contains(nom.Trim())) return true;
            foreach (string v in Vitaux)
                if (string.Equals(v, nom, StringComparison.OrdinalIgnoreCase)) return true;
            return EstAnticheat(nom, libelle, chemin) || EstSecurite(nom, libelle, chemin);
        }

        /// <summary>PUR : le binaire vit-il hors de Windows ? C'est la définition opérationnelle
        /// d'un service TIERS — celui qu'aucune liste écrite d'avance ne peut connaître.</summary>
        public static bool EstTiers(string chemin)
        {
            if (string.IsNullOrEmpty(chemin)) return false;
            string c = chemin.Trim().Trim('"').ToLowerInvariant();
            return !(c.Contains(@"\windows\system32") || c.Contains(@"\windows\syswow64")
                  || c.Contains(@"\windows\servicing") || c.StartsWith(@"\systemroot")
                  || c.Contains(@"\windows\microsoft.net"));
        }

        /// <summary>
        /// CLASSEMENT PUR d'un service. L'ordre des questions est le fond du module : on protège
        /// AVANT de proposer, et on ne devine pour un tiers que si aucun motif ne colle.
        /// </summary>
        public static void Classe(Service s)
        {
            if (s == null) return;

            if (EstVital(s.Nom, s.Libelle, s.Chemin))
            {
                s.Verdict = Verdict.Vital;
                s.Motif = EstAnticheat(s.Nom, s.Libelle, s.Chemin)
                    ? "service anticheat : l'arrêter empêche le jeu de démarrer, et manipuler un anticheat en cours d'exécution est exactement ce qu'il surveille"
                    : EstSecurite(s.Nom, s.Libelle, s.Chemin)
                    ? "logiciel de sécurité : le couper rend la machine plus rapide et son propriétaire plus vulnérable — ce n'est pas un arbitrage qu'ONYX prend à sa place"
                    : "Windows, le son, le réseau, la sécurité ou la manette en dépendent";
                return;
            }

            string motif;
            if (DansTable(Utiles, s.Nom, out motif)) { s.Verdict = Verdict.Utile; s.Motif = motif; return; }
            if (DansTable(Suspendables, s.Nom, out motif)) { s.Verdict = Verdict.Suspendable; s.Motif = motif; return; }
            if (DansTable(Inutiles, s.Nom, out motif)) { s.Verdict = Verdict.Inutile; s.Motif = motif; return; }

            if (_suspendablesEnPlus.Contains(s.Nom.Trim()))
            {
                s.Verdict = Verdict.Suspendable;
                s.Motif = "sonde matérielle : interroge les capteurs par un bus lent et bloquant. "
                        + "Le Mode Jeu l'arrête déjà pendant les parties";
                return;
            }

            if (EstTiers(s.Chemin))
            {
                foreach (string[] m in MotifsTiers)
                    if (Contient(s.Nom, m[0]) || Contient(s.Libelle, m[0])
                     || Contient(s.Chemin, m[0]) || Contient(s.Editeur, m[0]))
                    {
                        s.Verdict = Verdict.Suspendable; s.Motif = m[1]; return;
                    }

                s.Verdict = Verdict.Inconnu;
                s.Motif = "service installé par un logiciel tiers"
                        + (string.IsNullOrEmpty(s.Editeur) ? "" : " (" + s.Editeur + ")")
                        + " : ONYX ne sait pas ce qu'il fait et ne le propose donc pas de lui-même";
                return;
            }

            s.Verdict = Verdict.Utile;
            s.Motif = "service Windows non catalogué : rien ne dit qu'il coûte quoi que ce soit";
        }

        /// <summary>PUR : ce que l'arrêt de ce service rapporterait RÉELLEMENT, en une phrase.
        /// C'est la règle 2 — un service arrêté ne coûte rien, et le dire évite un geste inutile.</summary>
        public static string Gain(Service s)
        {
            if (s == null) return "";
            if (!s.EnCours)
                return s.Start == 4
                    ? "déjà désactivé : rien à gagner"
                    : "à l'arrêt : il ne consomme rien pour l'instant, le désactiver ne libérerait donc rien";

            var sb = new System.Text.StringBuilder();
            sb.Append(s.RamMo.ToString("0", CultureInfo.InvariantCulture)).Append(" Mo");
            if (s.Partage > 1)
                sb.Append(" (estimé : processus partagé avec ").Append(s.Partage - 1).Append(" autre(s) service(s))");
            if (s.Cpu >= 0.5)
                sb.Append(", ").Append(s.Cpu.ToString("0.#", CultureInfo.InvariantCulture)).Append(" % du processeur");
            return sb.ToString();
        }

        /// <summary>PUR : vaut-il la peine de PROPOSER ce service ? Un service déjà à l'arrêt ou
        /// déjà désactivé ne rapporte rien : le cocher d'office ferait croire à un gain.</summary>
        public static bool Proposable(Service s)
        {
            if (s == null) return false;
            if (s.Verdict != Verdict.Suspendable && s.Verdict != Verdict.Inutile) return false;
            return s.EnCours;
        }

        /// <summary>
        /// PUR : tri d'affichage. Un tableau trié par ordre alphabétique oblige à lire 200 lignes
        /// pour trouver les trois qui comptent.
        ///
        /// L'ORDRE DES CLÉS EST LA RÈGLE 2 APPLIQUÉE À L'AFFICHAGE. Trier d'abord par verdict
        /// remplissait le haut du tableau de services « inutiles ici » DÉJÀ désactivés, dont la
        /// colonne coût répétait « rien à gagner » — le classement disait donc de regarder en
        /// premier ce qui ne rapporte rien. Ce qui est réellement arrêtable passe devant, le reste
        /// suit par verdict puis par coût.
        /// </summary>
        public static int Compare(Service a, Service b)
        {
            if (a == null || b == null) return 0;

            int act = (Proposable(b) ? 1 : 0).CompareTo(Proposable(a) ? 1 : 0);
            if (act != 0) return act;

            int vif = (b.EnCours ? 1 : 0).CompareTo(a.EnCours ? 1 : 0);
            if (vif != 0) return vif;

            int p = Priorite(b.Verdict).CompareTo(Priorite(a.Verdict));
            if (p != 0) return p;

            double ca = a.RamMo + a.Cpu * 50, cb = b.RamMo + b.Cpu * 50;
            int c = cb.CompareTo(ca);
            return c != 0 ? c : string.Compare(a.Nom, b.Nom, StringComparison.OrdinalIgnoreCase);
        }

        private static int Priorite(Verdict v)
        {
            switch (v)
            {
                case Verdict.Inutile: return 4;
                case Verdict.Suspendable: return 3;
                case Verdict.Inconnu: return 2;
                case Verdict.Utile: return 1;
                default: return 0;
            }
        }

        /// <summary>PUR : le constat d'ensemble, pour le rapport et le journal.</summary>
        public static string Constat(List<Service> tous)
        {
            if (tous == null || tous.Count == 0) return "Aucun service énuméré.";
            int total = tous.Count, actifs = 0, tiers = 0, proposables = 0;
            double ram = 0;
            foreach (Service s in tous)
            {
                if (s == null) continue;
                if (s.EnCours) actifs++;
                if (EstTiers(s.Chemin)) tiers++;
                if (Proposable(s)) { proposables++; ram += s.RamMo; }
            }
            return total + " services installés, " + actifs + " en cours, dont " + tiers
                 + " venus de logiciels tiers. " + proposables + " sont arrêtables sans rien casser, "
                 + "pour environ " + ram.ToString("0", CultureInfo.InvariantCulture) + " Mo de mémoire.";
        }

        // ==================================================================
        //  Journal de ce qu'on a changé — PUR (format), pour pouvoir revenir
        // ==================================================================

        /// <summary>Une ligne du journal : de quoi RESTAURER exactement l'état d'avant.</summary>
        public sealed class Trace
        {
            public string Nom = "";
            public int StartAvant = 3;
            public bool TournaitAvant;
            public string Action = "";     // suspendu | desactive
            public DateTime Quand = DateTime.Now;
        }

        /// <summary>PUR : mise en ligne. Format tabulé, lisible à l'œil dans le bloc-notes.</summary>
        public static string Ligne(Trace t)
        {
            if (t == null) return "";
            return string.Join("\t", new[]
            {
                t.Nom, t.StartAvant.ToString(CultureInfo.InvariantCulture),
                t.TournaitAvant ? "1" : "0", t.Action,
                t.Quand.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            });
        }

        /// <summary>PUR : relecture. Une ligne abîmée est ignorée, jamais fatale — un journal
        /// tronqué doit rendre les lignes qui restent, pas empêcher toute restauration.</summary>
        public static Trace Relit(string ligne)
        {
            if (string.IsNullOrEmpty(ligne)) return null;
            string[] p = ligne.Split('\t');
            if (p.Length < 4 || p[0].Trim().Length == 0) return null;
            var t = new Trace { Nom = p[0].Trim(), Action = p[3].Trim() };
            int st;
            t.StartAvant = int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out st) ? st : 3;
            t.TournaitAvant = p[2].Trim() == "1";
            DateTime d;
            if (p.Length > 4 && DateTime.TryParse(p[4], CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                t.Quand = d;
            return t;
        }

        /// <summary>PUR : le type de démarrage à réécrire pour revenir à l'état d'avant.</summary>
        public static string TypeDemarrage(int start)
        {
            switch (start)
            {
                case 2: return "auto";
                case 4: return "disabled";
                default: return "demand";
            }
        }

        /// <summary>
        /// Où s'écrit le journal. Redirigeable, et uniquement pour le banc d'essai : un test qui
        /// écrirait dans le vrai journal effacerait la marche arrière d'une vraie machine — et
        /// c'est précisément la promesse que ce module est censé tenir.
        /// </summary>
        public static string CheminJournal { get; set; }

        private static string Chemin
        {
            get
            {
                return string.IsNullOrEmpty(CheminJournal)
                    ? AppPaths.File("bt-services-journal.txt")
                    : CheminJournal;
            }
        }

        public static List<Trace> Journal()
        {
            var l = new List<Trace>();
            try
            {
                if (!System.IO.File.Exists(Chemin)) return l;
                foreach (string ligne in System.IO.File.ReadAllLines(Chemin))
                {
                    Trace t = Relit(ligne);
                    if (t != null) l.Add(t);
                }
            }
            catch (Exception ex) { JournalTechnique.Echec("InventaireServices.Journal", ex); }
            return l;
        }

        private static void Ajoute(Trace t)
        {
            try { System.IO.File.AppendAllText(Chemin, Ligne(t) + Environment.NewLine); }
            catch (Exception ex) { JournalTechnique.Echec("InventaireServices.Ajoute", ex); }
        }

        private static void VideJournal()
        {
            try { if (System.IO.File.Exists(Chemin)) System.IO.File.Delete(Chemin); }
            catch (Exception ex) { JournalTechnique.Echec("InventaireServices.VideJournal", ex); }
        }

        // ==================================================================
        //  Agir — par une interface injectée
        // ==================================================================

        /// <summary>
        /// Les gestes réels sur un service. Injectés plutôt qu'appelés en dur pour deux raisons :
        /// l'action véritable reste au seul endroit qui la porte (<c>Sys</c>), et ce module se
        /// vérifie au banc d'essai sans toucher à une machine.
        /// </summary>
        public interface IActions
        {
            void Configure(string service, string typeDemarrage, bool arrete, bool demarre);
            void Arrete(string service);
            void Demarre(string service);
            /// <summary>Second filet : un service que l'appelant interdit de toucher, quoi qu'en
            /// dise le catalogue. C'est par là que passe <see cref="ServiceGuard"/>.</summary>
            bool Interdit(string service);
        }

        public enum Geste { Suspendre, Desactiver }

        /// <summary>
        /// Applique le plan. Renvoie le nombre de services effectivement touchés.
        ///
        /// Chaque service est isolé : un service récalcitrant ne doit ni faire échouer les autres,
        /// ni laisser un état à moitié appliqué. Et rien n'est fait sans avoir d'abord écrit dans
        /// le journal comment revenir en arrière — un geste dont on ne sait pas revenir n'est pas
        /// une optimisation, c'est une modification définitive.
        /// </summary>
        public static int Applique(List<Service> choisis, Geste geste, IActions actions, Action<string, int> log)
        {
            if (choisis == null || actions == null) return 0;
            int n = 0;
            foreach (Service s in choisis)
            {
                if (s == null) continue;
                if (EstVital(s.Nom, s.Libelle, s.Chemin) || actions.Interdit(s.Nom))
                {
                    if (log != null) log("Service « " + s.Nom + " » ignoré : protégé.", 2);
                    continue;
                }
                try
                {
                    Ajoute(new Trace
                    {
                        Nom = s.Nom,
                        StartAvant = s.Start,
                        TournaitAvant = s.EnCours,
                        Action = geste == Geste.Suspendre ? "suspendu" : "desactive"
                    });

                    if (geste == Geste.Suspendre) actions.Arrete(s.Nom);
                    else actions.Configure(s.Nom, "disabled", true, false);
                    n++;
                }
                catch (Exception ex)
                {
                    JournalTechnique.Echec("InventaireServices.Applique/" + s.Nom, ex);
                    if (log != null) log("Service " + s.Nom + " : " + ex.Message, 3);
                }
            }
            if (log != null && n > 0)
                log(geste == Geste.Suspendre
                    ? n + " service(s) arrêté(s) — ils redémarreront à la demande, rien n'est désactivé."
                    : n + " service(s) désactivé(s) — réversible par « Tout restaurer ».", 1);
            return n;
        }

        /// <summary>
        /// Remet TOUT dans l'état d'avant, en partant du journal. C'est la promesse du CHANGELOG
        /// (« toutes les optimisations sont réversibles ») tenue pour ce module-ci.
        /// </summary>
        public static int Restaure(IActions actions, Action<string, int> log)
        {
            if (actions == null) return 0;
            List<Trace> j = Journal();
            int n = 0;
            foreach (Trace t in j)
            {
                try
                {
                    actions.Configure(t.Nom, TypeDemarrage(t.StartAvant), false, false);
                    if (t.TournaitAvant) actions.Demarre(t.Nom);
                    n++;
                }
                catch (Exception ex) { JournalTechnique.Echec("InventaireServices.Restaure/" + t.Nom, ex); }
            }
            if (n > 0) VideJournal();
            if (log != null) log(n + " service(s) remis dans leur état d'origine.", 0);
            return n;
        }

        // ==================================================================
        //  Mesure — la seule partie qui a besoin d'une machine
        // ==================================================================

        /// <summary>
        /// Énumère les services, leur état, leur binaire, leur éditeur, et ce qu'ils coûtent.
        ///
        /// <paramref name="fenetreMs"/> : durée de la fenêtre de mesure du processeur. Un relevé
        /// instantané ne veut rien dire — c'est l'écart entre deux relevés de temps processeur qui
        /// donne un pourcentage. Zéro pour n'obtenir que la mémoire (affichage immédiat).
        /// </summary>
        public static List<Service> Analyse(int fenetreMs)
        {
            var res = new List<Service>();
            var parPid = new Dictionary<int, List<Service>>();

            try
            {
                using (var chercheur = new ManagementObjectSearcher(
                    "SELECT Name, DisplayName, PathName, ProcessId, State, StartMode, DelayedAutoStart FROM Win32_Service"))
                    foreach (ManagementObject mo in chercheur.Get())
                    {
                        var s = new Service();
                        try
                        {
                            s.Nom = Convert.ToString(mo["Name"]) ?? "";
                            s.Libelle = Convert.ToString(mo["DisplayName"]) ?? "";
                            s.Chemin = Convert.ToString(mo["PathName"]) ?? "";
                            s.EnCours = string.Equals(Convert.ToString(mo["State"]), "Running", StringComparison.OrdinalIgnoreCase);
                            try { s.Pid = Convert.ToInt32(mo["ProcessId"]); } catch { s.Pid = 0; }
                            try { s.Retarde = Convert.ToBoolean(mo["DelayedAutoStart"]); } catch { s.Retarde = false; }

                            string mode = Convert.ToString(mo["StartMode"]) ?? "";
                            s.Start = string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase) ? 2
                                    : string.Equals(mode, "Disabled", StringComparison.OrdinalIgnoreCase) ? 4 : 3;
                        }
                        catch { continue; }
                        if (s.Nom.Length == 0) continue;

                        s.Editeur = Editeur(s.Chemin);
                        Classe(s);
                        res.Add(s);

                        if (s.EnCours && s.Pid > 0)
                        {
                            if (!parPid.ContainsKey(s.Pid)) parPid[s.Pid] = new List<Service>();
                            parPid[s.Pid].Add(s);
                        }
                    }
            }
            catch (Exception ex) { JournalTechnique.Echec("InventaireServices.Analyse", ex); }

            Coute(parPid, fenetreMs);
            res.Sort(Compare);
            return res;
        }

        /// <summary>
        /// Mémoire et processeur, par PROCESSUS, répartis entre les services qui l'habitent.
        /// C'est l'aveu de l'en-tête : un svchost partagé ne se découpe pas, on divise et on le dit.
        /// </summary>
        private static void Coute(Dictionary<int, List<Service>> parPid, int fenetreMs)
        {
            if (parPid.Count == 0) return;

            var proc = new Dictionary<int, Process>();
            var avant = new Dictionary<int, TimeSpan>();
            foreach (int pid in parPid.Keys)
            {
                try
                {
                    Process p = Process.GetProcessById(pid);
                    proc[pid] = p;
                    avant[pid] = p.TotalProcessorTime;
                }
                catch { }   // processus disparu entre l'énumération et la mesure : normal
            }

            Stopwatch chrono = null;
            if (fenetreMs > 0)
            {
                chrono = Stopwatch.StartNew();
                try { System.Threading.Thread.Sleep(fenetreMs); } catch { }
                chrono.Stop();
            }

            int coeurs = Environment.ProcessorCount;
            foreach (KeyValuePair<int, List<Service>> e in parPid)
            {
                Process p;
                if (!proc.TryGetValue(e.Key, out p)) continue;
                int colocataires = e.Value.Count;
                try
                {
                    p.Refresh();
                    double ramMo = p.WorkingSet64 / 1024.0 / 1024.0 / colocataires;
                    double cpu = 0;
                    if (chrono != null)
                    {
                        TimeSpan debut;
                        if (avant.TryGetValue(e.Key, out debut))
                            cpu = SvcHost.Pourcent((p.TotalProcessorTime - debut).TotalMilliseconds,
                                                   chrono.Elapsed.TotalMilliseconds, coeurs) / colocataires;
                    }
                    foreach (Service s in e.Value) { s.RamMo = ramMo; s.Cpu = cpu; s.Partage = colocataires; }
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
        }

        /// <summary>Société qui signe le binaire. C'est ce qui rend un service tiers identifiable :
        /// « CorsairService » ne dit rien à qui n'a pas monté sa machine ; « Corsair Memory, Inc. » si.</summary>
        private static string Editeur(string ligneCommande)
        {
            try
            {
                string exe = BinaireSeul(ligneCommande);
                if (exe.Length == 0 || !System.IO.File.Exists(exe)) return "";
                FileVersionInfo v = FileVersionInfo.GetVersionInfo(exe);
                return (v.CompanyName ?? "").Trim();
            }
            catch { return ""; }
        }

        /// <summary>PUR : extrait le chemin de l'exécutable d'une ligne de commande de service.
        /// Les arguments (« -k netsvcs ») ne sont pas un fichier, et les guillemets non plus.</summary>
        public static string BinaireSeul(string ligneCommande)
        {
            if (string.IsNullOrEmpty(ligneCommande)) return "";
            string c = ligneCommande.Trim();
            if (c.StartsWith("\""))
            {
                int f = c.IndexOf('"', 1);
                return f > 1 ? c.Substring(1, f - 1) : c.Trim('"');
            }
            int ext = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return ext > 0 ? c.Substring(0, ext + 4) : c;
        }
    }
}
