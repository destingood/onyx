using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// L'INSTALLATION DU PILOTE GRAPHIQUE EST-ELLE INTACTE ?
    ///
    /// Ce module existe parce qu'une enquête de latence peut désigner correctement le coupable —
    /// « le GPU coûte 15,5 µs par DPC au lieu de 5 » — sans que le moindre levier n'y change quoi
    /// que ce soit. Quand le pilote lui-même est mal installé, plafonner les FPS ou répartir les
    /// interruptions ne fait que déplacer un problème qui n'est pas là.
    ///
    /// CE QU'ON CHERCHE : les traces d'une installation qui s'est faite en DEUX temps.
    ///
    ///   Une restauration système est le cas d'école. Elle ramène en arrière le registre et les
    ///   fichiers de System32, mais le DriverStore, lui, est re-provisionné derrière. Le pilote
    ///   redémarre, Windows n'affiche aucune erreur, le Gestionnaire de périphériques est vert —
    ///   et la pile graphique tourne avec des morceaux issus de deux installations différentes.
    ///   Une mise à jour interrompue ou un installeur constructeur qui échoue à mi-course
    ///   laissent exactement la même signature.
    ///
    ///   Constaté sur la machine de référence : SIX INF NVIDIA provisionnés dont cinq en
    ///   43 secondes, deux paquets DriverStore, l'INF lié au GPU plus ANCIEN qu'un autre déposé
    ///   20 secondes après, et une couche mode-utilisateur datée de onze jours plus tôt que les
    ///   paquets. Aucun de ces quatre faits n'est visible dans Windows.
    ///
    /// CE QUE CE MODULE REFUSE DE FAIRE :
    ///
    ///   Conclure seul. Une installation bousculée qui ne coûte RIEN à la mesure n'est pas un
    ///   problème — c'est un historique. Le diagnostic n'est présenté que si le GPU domine
    ///   réellement le temps noyau du relevé (voir PertinentPour). Sortir cette alerte sur une
    ///   machine dont la latence vient d'ailleurs enverrait l'utilisateur réinstaller un pilote
    ///   parfaitement sain.
    ///
    ///   Affirmer la cause. Les dates disent qu'il y a eu deux temps ; elles ne disent pas
    ///   lequel. Le texte propose une réinstallation propre — geste sans risque et réversible —
    ///   et laisse la mesure d'après trancher.
    /// </summary>
    internal static class InstallGraphique
    {
        // ==================================================================
        //  Seuils — chacun justifié, aucun choisi au hasard
        // ==================================================================

        /// <summary>
        /// Nombre normal de pilotes D'AFFICHAGE provisionnés : celui en service, plus la
        /// génération précédente que Windows garde pour permettre le retour arrière.
        ///
        /// Ce seuil ne s'applique QU'AUX INF de classe Display. Une install NVIDIA saine dépose
        /// aussi nvhda (audio HDMI), nvppc (USB-C), nvpcf, nvvad : compter tout INF portant le nom
        /// du fabricant faisait voir six « pilotes graphiques » là où il n'y en a qu'un, et
        /// déclenchait une alerte sur une machine parfaitement normale.
        /// </summary>
        public const int InfAffichageNormal = 2;

        /// <summary>Coût d'un DPC graphique au-delà duquel la pile est anormalement chère. Un
        /// pilote sain tient entre 3 et 8 µs ; on ne parle qu'au-dessus du haut de cette plage.</summary>
        public const double MicrosParEvenementGpuNormal = 8.0;

        /// <summary>Non mesuré — à ne jamais confondre avec zéro.</summary>
        public const int Inconnu = -1;

        // ==================================================================
        //  Ce qu'on relève sur la machine
        // ==================================================================

        public sealed class Etat
        {
            public bool Lu;                      // la lecture a-t-elle abouti ?
            public string Marque = "";           // « NVIDIA », « AMD », « Intel »
            public string Gpu = "";              // libellé de la carte

            public string InfLie = "";           // l'INF d'affichage lié au GPU (oem69.inf)
            public DateTime DateInfLie;
            public string InfPlusRecent = "";    // le plus récent INF D'AFFICHAGE, lié ou non
            public DateTime DateInfPlusRecent;
            /// <summary>Compte les INF de classe Display UNIQUEMENT (voir InfAffichageNormal).</summary>
            public int InfAffichage = Inconnu;

            public int PaquetsDriverStore = Inconnu;
            public DateTime DatePaquetPlusRecent;

            /// <summary>Version du pilote telle que déclarée par le périphérique (32.0.15.9649).</summary>
            public string VersionPilote = "";

            public string ModeUtilisateur = "";  // nvapi64.dll, atiadlxx.dll…
            public DateTime DateModeUtilisateur;
            /// <summary>Version du fichier mode-utilisateur : c'est ELLE qui se compare à
            /// VersionPilote. Les dates de fichier, non — voir Analyse().</summary>
            public string VersionModeUtilisateur = "";
        }

        public sealed class Indice
        {
            /// <summary>2 = signe franc, 1 = à surveiller.</summary>
            public int Gravite;
            public string Constat = "";
            public string Pourquoi = "";
        }

        // ==================================================================
        //  Analyse — PURE, donc testable sans machine
        // ==================================================================

        /// <summary>PUR : les anomalies que porte cet état. Liste vide = rien à signaler.</summary>
        public static List<Indice> Analyse(Etat e)
        {
            var l = new List<Indice>();
            if (e == null || !e.Lu) return l;

            if (e.InfAffichage > InfAffichageNormal)
                l.Add(new Indice
                {
                    Gravite = 2,
                    Constat = e.InfAffichage + " pilotes D'AFFICHAGE " + e.Marque
                            + " provisionnés (un ou deux attendus)",
                    Pourquoi = "Windows garde le pilote en service plus la génération précédente, "
                             + "pour permettre le retour arrière. Au-delà, ce sont des installations "
                             + "rapprochées qui se sont empilées. Ne compte QUE la classe Display : "
                             + "l'audio HDMI, l'USB-C et les périphériques logiciels du GPU ont leurs "
                             + "propres INF, et leur présence est normale."
                });

            // Même seuil que les INF, et pour la même raison : le magasin garde le paquet en
            // service PLUS le précédent, qui permet le retour arrière. Crier à partir de deux
            // revenait à déclarer bancale toute machine ayant reçu une mise à jour de pilote.
            if (e.PaquetsDriverStore > InfAffichageNormal)
                l.Add(new Indice
                {
                    Gravite = 2,
                    Constat = e.PaquetsDriverStore + " paquets d'affichage empilés dans le DriverStore",
                    Pourquoi = "Le magasin garde le paquet en service et le précédent — deux, c'est "
                             + "normal. Au-delà, des installations se sont succédé sans que les "
                             + "anciennes soient retirées."
                });

            if (e.InfLie.Length > 0 && e.InfPlusRecent.Length > 0
                && !string.Equals(e.InfLie, e.InfPlusRecent, StringComparison.OrdinalIgnoreCase)
                && e.DateInfPlusRecent > e.DateInfLie)
                l.Add(new Indice
                {
                    Gravite = 2,
                    Constat = "la carte utilise le pilote d'affichage " + e.InfLie + ", alors que "
                            + e.InfPlusRecent + " — d'affichage lui aussi — est plus récent ("
                            + Ecart(e.DateInfLie, e.DateInfPlusRecent) + " d'écart)",
                    Pourquoi = "Un paquet d'affichage plus récent a été déposé sans que la carte y "
                             + "soit rattachée. Le système est resté accroché à l'ancien."
                });

            // VERSIONS, pas dates. Les horodatages de fichier bougent pour des raisons qui n'ont
            // rien à voir avec une installation — resignature, réparation de composants, simple
            // recopie. S'y fier faisait crier « deux installations différentes » sur une machine
            // dont le noyau et le mode-utilisateur portaient la MÊME version. La version, elle,
            // ne change que lorsque le composant change réellement.
            if (e.VersionPilote.Length > 0 && e.VersionModeUtilisateur.Length > 0
                && !MemeVersion(e.VersionPilote, e.VersionModeUtilisateur))
                l.Add(new Indice
                {
                    Gravite = 2,
                    Constat = "le pilote est en " + e.VersionPilote + " mais " + e.ModeUtilisateur
                            + " est en " + e.VersionModeUtilisateur,
                    Pourquoi = "Le noyau et la couche mode-utilisateur viennent de deux installations "
                             + "différentes. C'est la signature d'une restauration système : elle ramène "
                             + "System32 en arrière pendant que le DriverStore, lui, est re-provisionné."
                });

            return l;
        }

        /// <summary>
        /// PUR : deux versions désignent-elles la même livraison ?
        ///
        /// On compare les trois premiers champs. Le quatrième bouge sur des recompilations sans
        /// changement fonctionnel, et le faire compter produirait des alertes sur du bruit.
        /// </summary>
        public static bool MemeVersion(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return true;
            string[] x = a.Trim().Split('.'), y = b.Trim().Split('.');
            int n = Math.Min(3, Math.Min(x.Length, y.Length));
            for (int i = 0; i < n; i++)
                if (!string.Equals(x[i].Trim(), y[i].Trim(), StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>PUR : écart entre deux dates, dit en français lisible.</summary>
        public static string Ecart(DateTime a, DateTime b)
        {
            TimeSpan t = b > a ? b - a : a - b;
            if (t.TotalDays >= 1.0) return ((int)t.TotalDays) + " jour" + (t.TotalDays >= 2 ? "s" : "");
            if (t.TotalHours >= 1.0) return ((int)t.TotalHours) + " h";
            if (t.TotalMinutes >= 1.0) return ((int)t.TotalMinutes) + " min";
            return ((int)t.TotalSeconds) + " s";
        }

        /// <summary>PUR : part du temps noyau prise par la famille GPU dans ce relevé.</summary>
        public static double PartGpu(EnqueteLatence.Releve r)
        {
            if (r == null || r.TotalMs <= 0) return 0;
            double t = 0;
            foreach (EnqueteLatence.Pilote p in r.Pilotes)
                if (LeviersLatence.Famille(p.Nom) == "gpu") t += p.TotalMs;
            return t / r.TotalMs;
        }

        /// <summary>PUR : coût moyen d'un événement graphique, en µs. 0 si la famille est absente.</summary>
        public static double MicrosParEvenementGpu(EnqueteLatence.Releve r)
        {
            if (r == null) return 0;
            double ms = 0; long ev = 0;
            foreach (EnqueteLatence.Pilote p in r.Pilotes)
                if (LeviersLatence.Famille(p.Nom) == "gpu") { ms += p.TotalMs; ev += p.Evenements; }
            return ev > 0 ? ms / ev * 1000.0 : 0;
        }

        /// <summary>
        /// PUR : faut-il montrer ce diagnostic pour CE relevé ?
        ///
        /// Deux conditions, et les deux comptent. Le GPU doit peser assez pour que le sujet soit
        /// le bon, ET son coût par événement doit être hors plage saine — une installation
        /// bousculée qui ne coûte rien reste un historique, pas une panne.
        /// </summary>
        public static bool PertinentPour(EnqueteLatence.Releve r, double partMini)
        {
            return PartGpu(r) >= partMini
                && MicrosParEvenementGpu(r) > MicrosParEvenementGpuNormal;
        }

        /// <summary>
        /// PUR : le rapport, ou une chaîne vide s'il n'y a rien d'honnête à dire.
        ///
        /// Vide dans trois cas : lecture impossible, aucune anomalie, ou GPU non concerné par ce
        /// relevé. Le silence est ici la bonne réponse — pas un message rassurant de plus.
        /// </summary>
        public static string Rapport(Etat e, EnqueteLatence.Releve r, double partMini)
        {
            if (e == null || !e.Lu) return "";
            if (!PertinentPour(r, partMini)) return "";
            List<Indice> ind = Analyse(e);
            if (ind.Count == 0) return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ÉTAT DE L'INSTALLATION GRAPHIQUE");
            sb.AppendLine("   " + (e.Gpu.Length > 0 ? e.Gpu : e.Marque)
                + " — le GPU pèse " + (PartGpu(r) * 100).ToString("0") + " % du temps noyau à "
                + MicrosParEvenementGpu(r).ToString("0.0") + " µs/évt (sain : 3 à "
                + MicrosParEvenementGpuNormal.ToString("0") + ").");
            sb.AppendLine();
            foreach (Indice i in ind)
            {
                sb.AppendLine("   " + (i.Gravite >= 2 ? "⚠" : "•") + " " + i.Constat);
                sb.AppendLine("        " + i.Pourquoi);
            }
            sb.AppendLine();
            sb.AppendLine("   Ces écarts disent que l'installation s'est faite en DEUX temps — ils ne");
            sb.AppendLine("   disent pas lequel a gagné. Tant qu'elle est dans cet état, aucun réglage");
            sb.AppendLine("   ne peut être évalué : tu mesurerais un levier par-dessus un pilote bancal.");
            sb.AppendLine();
            sb.AppendLine("   À FAIRE D'ABORD — réinstallation propre du pilote " + e.Marque + " :");
            sb.AppendLine("   installeur constructeur → « Installation personnalisée » → cocher");
            sb.AppendLine("   « Effectuer une nouvelle installation ». Sans risque, et c'est la");
            sb.AppendLine("   procédure normale après TOUTE restauration système.");
            sb.AppendLine("   Puis refais un relevé de MÊME DURÉE et compare.");
            return sb.ToString();
        }

        // ==================================================================
        //  Lecture machine — IMPURE, et qui n'a pas le droit de lever
        // ==================================================================

        private const string ClasseAffichage =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        /// <summary>Par marque : préfixe des dossiers DriverStore, DLL mode-utilisateur témoin.</summary>
        private static readonly string[][] Marques =
        {
            //  marque     préfixe DriverStore   témoin mode-utilisateur
            new[] { "NVIDIA", "nv_disp",         "nvapi64.dll"   },
            new[] { "AMD",    "u0",              "atiadlxx.dll"  },
            new[] { "Intel",  "iigd",            "igdumdim64.dll" }
        };

        /// <summary>
        /// L'état réel de la machine. Ne lève jamais : un état non lu (Lu = false) vaut mieux
        /// qu'un état partiel, qui produirait un diagnostic faux plutôt qu'aucun diagnostic.
        /// </summary>
        public static Etat Lire()
        {
            var e = new Etat();
            try
            {
                if (!LitCarteLiee(e)) return e;
                string[] m = MarqueConnue(e.Marque);
                LitInfProvisionnes(e);
                if (m != null) LitDriverStore(e, m[1]);
                if (m != null) LitModeUtilisateur(e, m[2]);
                e.Lu = true;
            }
            catch { e.Lu = false; }
            return e;
        }

        private static string[] MarqueConnue(string marque)
        {
            foreach (string[] m in Marques)
                if (string.Equals(m[0], marque, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        /// <summary>La carte réellement liée. Les sous-clés ne sont PAS toujours « 0000 » —
        /// sur la machine de référence c'est « 0001 » — donc on énumère.</summary>
        private static bool LitCarteLiee(Etat e)
        {
            using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(ClasseAffichage))
            {
                if (cls == null) return false;
                foreach (string nom in cls.GetSubKeyNames())
                {
                    using (RegistryKey k = cls.OpenSubKey(nom))
                    {
                        if (k == null) continue;
                        string prov = k.GetValue("ProviderName") as string;
                        string inf = k.GetValue("InfPath") as string;
                        if (string.IsNullOrEmpty(prov) || string.IsNullOrEmpty(inf)) continue;
                        if (MarqueConnue(prov) == null) continue;

                        e.Marque = prov;
                        e.InfLie = inf;
                        e.Gpu = (k.GetValue("DriverDesc") as string) ?? "";
                        e.VersionPilote = (k.GetValue("DriverVersion") as string) ?? "";
                        string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF", inf);
                        if (File.Exists(p)) e.DateInfLie = File.GetLastWriteTime(p);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Compte les INF de cette marque QUI SONT DES PILOTES D'AFFICHAGE, et retient
        /// le plus récent. On ne lit que le début du fichier : marque et classe sont toutes deux
        /// dans la section [Version], pas au bout des 300 Ko.</summary>
        private static void LitInfProvisionnes(Etat e)
        {
            string inf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
            if (!Directory.Exists(inf)) return;
            int n = 0;
            foreach (string f in Directory.GetFiles(inf, "oem*.inf"))
            {
                if (!EstPiloteDAffichage(f, e.Marque)) continue;
                n++;
                DateTime d = File.GetLastWriteTime(f);
                if (d > e.DateInfPlusRecent)
                {
                    e.DateInfPlusRecent = d;
                    e.InfPlusRecent = Path.GetFileName(f);
                }
            }
            e.InfAffichage = n;
        }

        private const int EnteteInfOctets = 8192;

        /// <summary>Classe Display. Sans ce filtre, nvhda (audio HDMI), nvppc (USB-C), nvpcf et
        /// nvvad étaient comptés comme des pilotes graphiques.</summary>
        private const string ClasseDisplayGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";

        private static bool EstPiloteDAffichage(string fichier, string marque)
        {
            try
            {
                var buf = new char[EnteteInfOctets];
                using (var sr = new StreamReader(fichier))
                {
                    int lus = sr.Read(buf, 0, buf.Length);
                    if (lus <= 0) return false;
                    string tete = new string(buf, 0, lus);
                    return tete.IndexOf(marque, StringComparison.OrdinalIgnoreCase) >= 0
                        && tete.IndexOf(ClasseDisplayGuid, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        private static void LitDriverStore(Etat e, string prefixe)
        {
            string repo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                                       "DriverStore", "FileRepository");
            if (!Directory.Exists(repo)) return;
            int n = 0;
            foreach (string d in Directory.GetDirectories(repo, prefixe + "*"))
            {
                n++;
                DateTime t = Directory.GetLastWriteTime(d);
                if (t > e.DatePaquetPlusRecent) e.DatePaquetPlusRecent = t;
            }
            e.PaquetsDriverStore = n;
        }

        private static void LitModeUtilisateur(Etat e, string dll)
        {
            string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), dll);
            if (!File.Exists(p)) return;
            e.ModeUtilisateur = dll;
            e.DateModeUtilisateur = File.GetLastWriteTime(p);
            try
            {
                System.Diagnostics.FileVersionInfo fv =
                    System.Diagnostics.FileVersionInfo.GetVersionInfo(p);
                e.VersionModeUtilisateur = fv.FileVersion ?? "";
            }
            catch { e.VersionModeUtilisateur = ""; }
        }
    }
}
