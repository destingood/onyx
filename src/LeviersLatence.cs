using System;
using System.Collections.Generic;

namespace BTOptimizer
{
    /// <summary>
    /// QUEL RÉGLAGE PEUT AGIR SUR QUEL PILOTE.
    ///
    /// Sans cette table, une enquête de latence se termine sur « c'est nvlddmkm » — un nom de
    /// fichier, pas une action. Elle relie chaque gros consommateur de temps noyau aux leviers
    /// qu'ONYX sait actionner, avec ce que chacun coûte et ce qu'on peut en espérer.
    ///
    /// TROIS RÈGLES QUE CETTE TABLE S'IMPOSE :
    ///
    /// 1. Aucun levier n'est présenté comme un gain acquis. Chacun porte une ESPÉRANCE, et c'est
    ///    la mesure avant/après qui tranche — jamais cette table.
    ///
    /// 2. Les leviers qui COÛTENT quelque chose le disent. Couper l'hyperviseur fait perdre WSL2
    ///    et Docker ; réactiver le MPO peut ramener les scintillements qu'on avait supprimés.
    ///    Un utilisateur ne peut pas arbitrer ce qu'on lui cache.
    ///
    /// 3. Un réglage d'ONYX qui COÛTE de la latence y figure comme les autres. « mpo_off » est
    ///    dans le préréglage eSport, et il force le compositeur à passer par le chemin de rendu
    ///    du GPU : sur une machine à trois écrans, c'est une charge réelle. Le taire parce que
    ///    c'est notre réglage serait le pire des biais.
    /// </summary>
    internal static class LeviersLatence
    {
        public sealed class Levier
        {
            public string Titre = "";
            /// <summary>Identifiant du tweak ONYX quand il en existe un, sinon vide.</summary>
            public string TweakId = "";
            /// <summary>Ce qu'on attend, honnêtement formulé.</summary>
            public string Esperance = "";
            /// <summary>Ce que ça coûte. Vide = rien à perdre.</summary>
            public string Cout = "";
            /// <summary>Vrai si annulable sans redémarrer — donc testable tout de suite.</summary>
            public bool Reversible = true;
            /// <summary>0 = à essayer en premier.</summary>
            public int Ordre;
        }

        /// <summary>
        /// PUR : le nom de pilote ramené à la famille qu'il représente.
        /// Les noms varient (majuscules, extension), la famille non.
        /// </summary>
        public static string Famille(string pilote)
        {
            if (string.IsNullOrEmpty(pilote)) return "";
            string n = pilote.ToLowerInvariant();
            int p = n.LastIndexOf('.');
            if (p > 0) n = n.Substring(0, p);

            if (n == "nvlddmkm" || n == "dxgkrnl" || n == "dxgmms2" || n == "amdkmdag" || n == "igdkmd64")
                return "gpu";
            if (n == "wdf01000" || n == "usbxhci" || n == "ucx01000" || n == "usbhub3" || n == "hidclass")
                return "usb";
            if (n == "storport" || n == "stornvme" || n == "storahci" || n == "classpnp" || n == "disk" || n == "ntfs")
                return "stockage";
            if (n == "tcpip" || n == "ndis" || n == "netio" || n == "afd" || n == "netadaptercx" || n == "wificx")
                return "reseau";
            if (n == "winhvr" || n == "vmbusr" || n == "vmswitch" || n == "hvservice" || n == "storvsp"
                || n == "vhdmp" || n == "vid" || n == "vpcivsp")
                return "hyperviseur";
            if (n == "hdaudbus" || n == "acx01000" || n == "portcls" || n == "ksthunk")
                return "audio";
            if (n == "ntoskrnl")
                return "noyau";
            if (n == "rspilll64" || n == "rsplll64")
                return "mesure";
            return "";
        }

        /// <summary>PUR : ce pilote est-il l'outil de mesure lui-même ?</summary>
        public static bool EstOutilDeMesure(string pilote)
        {
            return Famille(pilote) == "mesure";
        }

        /// <summary>PUR : les leviers connus pour un NOM DE PILOTE (nvlddmkm.sys, tcpip.sys…).</summary>
        public static List<Levier> Pour(string pilote)
        {
            return PourFamille(Famille(pilote));
        }

        /// <summary>
        /// PUR : les leviers d'une FAMILLE déjà résolue (« gpu », « reseau »…), du plus
        /// prometteur au moins.
        ///
        /// Séparé de Pour() parce que Plan() rend des familles, pas des noms de pilote : les
        /// enchaîner passait « gpu.sys » à Famille(), qui ne reconnaissait rien et rendait une
        /// liste vide. Le rapport affichait alors « ce qu'on peut tenter » sans rien dessous.
        /// </summary>
        public static List<Levier> PourFamille(string famille)
        {
            var l = new List<Levier>();
            switch (famille)
            {
                case "gpu":
                    l.Add(new Levier
                    {
                        Ordre = 0, Titre = "Plafonner les images par seconde", TweakId = "frame_cap",
                        Esperance = "Le temps du pilote graphique est proportionnel au nombre d'images "
                                  + "présentées : moins d'images, moins de travail noyau, dans la même proportion.",
                        Cout = "", Reversible = true
                    });
                    l.Add(new Levier
                    {
                        Ordre = 1, Titre = "Réactiver le MultiPlane Overlay (annuler « mpo_off »)",
                        TweakId = "mpo_off",
                        Esperance = "Désactivé, le compositeur ne peut plus superposer en matériel et fait tout "
                                  + "passer par le chemin de rendu du GPU — d'autant plus lourd qu'il y a d'écrans.",
                        Cout = "Ce réglage existait pour corriger scintillements et micro-saccades sur "
                             + "certains écrans : ils peuvent revenir. À tester, puis à annuler si c'est le cas.",
                        Reversible = true
                    });
                    l.Add(new Levier
                    {
                        Ordre = 2, Titre = "Éteindre les écrans secondaires pendant le jeu", TweakId = "",
                        Esperance = "Des écrans à des fréquences différentes obligent le compositeur à concilier "
                                  + "plusieurs cadences en permanence. Les éteindre retire ce travail entièrement.",
                        Cout = "Tu n'as plus qu'un écran le temps de la partie.", Reversible = true
                    });
                    l.Add(new Levier
                    {
                        Ordre = 3, Titre = "Priorité d'interruption au GPU", TweakId = "irq_priorite_gpu",
                        Esperance = "Les interruptions graphiques passent avant les autres. N'allège pas le "
                                  + "travail, mais le fait attendre moins.",
                        Cout = "", Reversible = true
                    });
                    break;

                case "hyperviseur":
                    l.Add(new Levier
                    {
                        Ordre = 0, Titre = "Couper la virtualisation (VBS / hyperviseur)", TweakId = "vbs_off",
                        Esperance = "Windows s'exécute au-dessus d'un hyperviseur : CHAQUE interruption traverse "
                                  + "une couche de virtualisation. Le coût est faible par événement mais se "
                                  + "multiplie par leur nombre — et il y en a des centaines de milliers.",
                        Cout = "WSL2 et Docker cessent de fonctionner. Redémarrage nécessaire.",
                        Reversible = false
                    });
                    break;

                case "usb":
                    l.Add(new Levier
                    {
                        Ordre = 0, Titre = "Répartir les interruptions sur tous les cœurs",
                        TweakId = "irq_spread_producteurs",
                        Esperance = "Un seul cœur encaissait toutes les interruptions USB. Les répartir ne réduit "
                                  + "pas le travail, mais évite qu'un cœur saturé retarde tout le reste.",
                        Cout = "", Reversible = true
                    });
                    l.Add(new Levier
                    {
                        Ordre = 1, Titre = "Débrancher ce qui ne sert pas pendant le jeu", TweakId = "",
                        Esperance = "Chaque périphérique HID interrompt à sa propre cadence. Une souris à "
                                  + "1000 Hz produit mille interruptions par seconde à elle seule.",
                        Cout = "Ne touche PAS à la souris ni au clavier : leur cadence élevée est "
                             + "précisément ce que tu veux en jeu.",
                        Reversible = true
                    });
                    break;

                case "reseau":
                    l.Add(new Levier
                    {
                        Ordre = 0, Titre = "Réglages de latence de la carte réseau", TweakId = "nic_latency",
                        Esperance = "Regroupement d'interruptions, économies d'énergie et déchargements : chacun "
                                  + "ajoute de l'attente sur le chemin réseau.",
                        Cout = "", Reversible = true
                    });
                    l.Add(new Levier
                    {
                        Ordre = 1, Titre = "Répartir les interruptions réseau", TweakId = "irq_spread_producteurs",
                        Esperance = "Même raison que pour l'USB : éviter qu'un cœur encaisse tout.",
                        Cout = "", Reversible = true
                    });
                    break;

                case "stockage":
                    l.Add(new Levier
                    {
                        Ordre = 0, Titre = "Répartir les interruptions du stockage",
                        TweakId = "irq_spread_producteurs",
                        Esperance = "Les contrôleurs NVMe produisent beaucoup d'interruptions ; les répartir "
                                  + "évite qu'un cœur sature pendant les chargements.",
                        Cout = "", Reversible = true
                    });
                    l.Add(new Levier
                    {
                        Ordre = 1, Titre = "Chercher ce qui lit le disque en fond", TweakId = "",
                        Esperance = "Un temps de stockage élevé signale surtout une ACTIVITÉ : indexation, "
                                  + "antivirus, synchronisation, mise à jour. Le pilote n'y est pour rien.",
                        Cout = "", Reversible = true
                    });
                    break;

                case "audio":
                    l.Add(new Levier
                    {
                        Ordre = 0, Titre = "Répartir les interruptions audio", TweakId = "audio_irq_spread",
                        Esperance = "Le pilote audio a des échéances strictes : le laisser partager un cœur "
                                  + "saturé produit les craquements.",
                        Cout = "", Reversible = true
                    });
                    break;

                case "noyau":
                    l.Add(new Levier
                    {
                        Ordre = 0, Titre = "Répartir l'expiration des minuteurs",
                        TweakId = "distribuer_minuteurs",
                        Esperance = "Les minuteurs expirent tous sur le même cœur par défaut. Les répartir "
                                  + "étale la charge — sans preuve mesurée à charge égale sur cette machine.",
                        Cout = "", Reversible = true
                    });
                    break;

                case "mesure":
                    l.Add(new Levier
                    {
                        Ordre = 0, Titre = "Fermer l'outil de mesure", TweakId = "",
                        Esperance = "Ce pilote est celui de LatencyMon. Il produit des DPC pour en mesurer : "
                                  + "tu ne mesures jamais ta machine au repos, tu la mesures pendant qu'on "
                                  + "la mesure. Sa part n'est pas un défaut à corriger.",
                        Cout = "", Reversible = true
                    });
                    break;
            }
            l.Sort((a, b) => a.Ordre.CompareTo(b.Ordre));
            return l;
        }

        /// <summary>
        /// PUR : le plan d'action pour un relevé — les familles à traiter, dans l'ordre du temps
        /// qu'elles consomment, en écartant l'outil de mesure.
        ///
        /// <paramref name="couverture"/> : on s'arrête quand cette part du temps noyau est couverte.
        /// Inutile de proposer dix actions quand deux pilotes font 90 % du travail.
        /// </summary>
        public static List<string> Plan(EnqueteLatence.Releve r, double couverture)
        {
            var fams = new List<string>();
            if (r == null || r.TotalMs <= 0) return fams;
            double cumul = 0;
            foreach (EnqueteLatence.Pilote p in EnqueteLatence.Coupables(r))
            {
                if (EstOutilDeMesure(p.Nom)) continue;
                string f = Famille(p.Nom);
                if (f.Length == 0) continue;

                // Le temps compte TOUJOURS, même quand la famille est déjà retenue. Sans cela,
                // deux pilotes d'une même famille — nvlddmkm et dxgkrnl pèsent 91 % à eux deux —
                // ne comptaient que pour le premier : le cumul restait sous le seuil et le plan
                // proposait toutes les familles de la machine, y compris celles à 0,4 %.
                cumul += p.TotalMs / r.TotalMs;
                if (!fams.Contains(f)) fams.Add(f);
                if (cumul >= couverture) break;
            }
            return fams;
        }
    }
}
