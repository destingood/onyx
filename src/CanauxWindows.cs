using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;

namespace BTOptimizer
{
    /// <summary>
    /// LES 139 AUTRES JOURNAUX DE WINDOWS.
    ///
    /// LogDoctor lit « Système » et « Application ». Ce sont les deux que tout le monde connaît,
    /// et ce sont aussi les deux où les pannes modernes ne s'écrivent PLUS. Windows tient sur
    /// cette machine 141 journaux non vides ; 139 sont ailleurs, dans des canaux « Operational »
    /// et « Admin » que l'Observateur d'événements n'ouvre jamais de lui-même.
    ///
    /// CE QUI A MOTIVÉ CE MODULE :
    ///
    ///   Un pilote refusé par l'intégrité du code produisait une boîte de dialogue et rien
    ///   d'autre. Le refus était journalisé — dans CodeIntegrity/Operational. LogDoctor, qui est
    ///   pourtant fait pour ça, ne pouvait pas le voir : il ne regardait pas ce canal, et il n'y
    ///   avait aucune raison qu'il le regarde plutôt qu'un autre. Écrire un module par canal ne
    ///   passe pas à l'échelle. Il fallait les balayer TOUS.
    ///
    ///   Mesuré ici : 25 canaux portent des erreurs sur sept jours, et le balayage complet coûte
    ///   2,3 secondes. Le coût n'était donc jamais la raison de ne pas le faire.
    ///
    /// CE QUE CE MODULE REFUSE DE FAIRE :
    ///
    ///   Interpréter ce qu'il ne connaît pas. Un canal inconnu est présenté comme inconnu, avec
    ///   son compte et sa date, et rien de plus. Inventer une cause plausible pour chacune des
    ///   centaines de sources possibles produirait un diagnostic faux la plupart du temps — et
    ///   un diagnostic faux coûte plus cher que pas de diagnostic.
    ///
    ///   Tout remonter. La majorité de ces canaux crachent en permanence des erreurs sans
    ///   conséquence : synchronisation de tuiles, déploiement de paquets Store, notifications.
    ///   Les afficher au même rang qu'un pilote refusé noierait le seul qui compte. Le bruit
    ///   connu est compté et résumé en une ligne, jamais détaillé.
    ///
    ///   Doubler LogDoctor. « Système » et « Application » sont exclus explicitement : ils ont
    ///   déjà leur base de connaissances, leur chronologie et leur corrélation aux changements.
    /// </summary>
    internal static class CanauxWindows
    {
        // ==================================================================
        //  Ce qu'on lit
        // ==================================================================

        /// <summary>Critique.</summary>
        public const byte NiveauCritique = 1;
        /// <summary>Erreur.</summary>
        public const byte NiveauErreur = 2;

        /// <summary>Déjà traités par LogDoctor, avec une base de connaissances dédiée.</summary>
        private static readonly string[] DejaCouverts = { "System", "Application", "Security" };

        /// <summary>Plafond par canal. Au-delà, on sait déjà que ça crie : le compte exact
        /// n'apprend rien de plus et la lecture s'allonge pour rien.</summary>
        public const int MaxParCanal = 60;

        public sealed class Groupe
        {
            public string Canal = "";
            public string Fournisseur = "";
            public int Id;
            public byte Niveau;
            public int Nombre;
            /// <summary>Nombre d'identifiants d'événement DISTINCTS réunis dans ce groupe.
            /// Vaut 1 à la lecture ; seule la fusion par canal le fait monter.</summary>
            public int Types = 1;
            public DateTime Dernier;
            /// <summary>Premier message rencontré, tronqué. Sert à montrer, pas à conclure.</summary>
            public string Exemple = "";
        }

        /// <summary>Ce qu'on sait dire d'un groupe. « Inconnu » est un verdict à part entière.</summary>
        public enum Classe
        {
            /// <summary>Bruit documenté, sans conséquence.</summary>
            Bruit,
            /// <summary>Canal où une panne est INVISIBLE ailleurs : ça mérite d'être lu.</summary>
            Serieux,
            /// <summary>Ni l'un ni l'autre : montré tel quel, sans interprétation.</summary>
            Inconnu
        }

        // ==================================================================
        //  Base de connaissances — par CANAL, pas par identifiant
        // ==================================================================

        /// <summary>
        /// Pourquoi par canal et non par (fournisseur, identifiant) : LogDoctor fait déjà le
        /// second sur les deux journaux classiques, et ça n'y tient que parce que leurs sources
        /// sont peu nombreuses et stables. Ici il y a des centaines de sources, renommées à
        /// chaque version de Windows. Ce qui reste stable, c'est le SUJET du canal — et c'est
        /// suffisant pour trier ce qui mérite un regard de ce qui n'en mérite aucun.
        /// </summary>
        private sealed class Sujet
        {
            public string Motif;
            public Classe Classe;
            public string Quoi;
            public string Pourquoi;
        }

        private static readonly Sujet[] Sujets =
        {
            // ---- Canaux où une panne ne se voit NULLE PART ailleurs ----
            new Sujet {
                Motif = "CodeIntegrity", Classe = Classe.Serieux,
                Quoi = "Windows a refusé de charger un pilote ou une bibliothèque",
                Pourquoi = "L'outil concerné s'installe et se lance sans rien dire — il ne fait "
                         + "simplement rien. Voir le rapport « pilotes refusés » pour le détail."
            },
            new Sujet {
                Motif = "Kernel-PnP", Classe = Classe.Serieux,
                Quoi = "un périphérique n'a pas pu démarrer",
                Pourquoi = "Le matériel apparaît pourtant dans le Gestionnaire de périphériques. "
                         + "Cause fréquente d'un port, d'un micro ou d'une manette qui « existe » "
                         + "mais ne répond pas."
            },
            new Sujet {
                Motif = "Kernel-EventTracing", Classe = Classe.Serieux,
                Quoi = "une session de mesure ETW a échoué",
                Pourquoi = "C'est le mécanisme qu'ONYX utilise pour mesurer la latence. Des échecs "
                         + "ici expliquent un relevé vide ou interrompu."
            },
            new Sujet {
                Motif = "StorPort", Classe = Classe.Serieux,
                Quoi = "le contrôleur de stockage a signalé un incident",
                Pourquoi = "Précède souvent, de plusieurs semaines, une panne de disque visible."
            },
            new Sujet {
                Motif = "Storage-ClassPnP", Classe = Classe.Serieux,
                Quoi = "un disque a mis trop longtemps à répondre",
                Pourquoi = "Micro-blocages du système entier pendant que Windows attend le disque."
            },
            new Sujet {
                Motif = "DeviceSetupManager", Classe = Classe.Serieux,
                Quoi = "l'installation d'un pilote a échoué",
                Pourquoi = "Un pilote resté à moitié installé donne un périphérique qui fonctionne "
                         + "mal sans jamais être signalé en erreur."
            },
            new Sujet {
                Motif = "WHEA", Classe = Classe.Serieux,
                Quoi = "erreur matérielle corrigée par le processeur",
                Pourquoi = "Corrigée ne veut pas dire sans importance : c'est le signal le plus "
                         + "précoce d'une mémoire, d'une alimentation ou d'un overclock instable."
            },
            new Sujet {
                Motif = "Resource-Exhaustion", Classe = Classe.Serieux,
                Quoi = "mémoire épuisée",
                Pourquoi = "Windows a dû fermer ou brider des programmes. Explique des fermetures "
                         + "de jeu qui n'apparaissent nulle part ailleurs."
            },
            new Sujet {
                Motif = "DriverFrameworks", Classe = Classe.Serieux,
                Quoi = "un pilote en mode utilisateur a échoué",
                Pourquoi = "Touche surtout les périphériques USB : manettes, casques, cartes son."
            },

            // ---- Bruit documenté : compté, jamais détaillé ----
            new Sujet { Motif = "CloudStore",        Classe = Classe.Bruit,
                        Quoi = "synchronisation des tuiles et réglages du menu Démarrer" },
            new Sujet { Motif = "CloudRestore",      Classe = Classe.Bruit,
                        Quoi = "restauration depuis le nuage" },
            new Sujet { Motif = "AppXDeployment",    Classe = Classe.Bruit,
                        Quoi = "déploiement d'applications du Store" },
            new Sujet { Motif = "AppxPackaging",     Classe = Classe.Bruit,
                        Quoi = "empaquetage d'applications du Store" },
            new Sujet { Motif = "AppReadiness",      Classe = Classe.Bruit,
                        Quoi = "préparation des applications à la première ouverture de session" },
            new Sujet { Motif = "AppModel-Runtime",  Classe = Classe.Bruit,
                        Quoi = "exécution des applications empaquetées" },
            new Sujet { Motif = "PushNotification",  Classe = Classe.Bruit,
                        Quoi = "notifications poussées" },
            new Sujet { Motif = "Store",             Classe = Classe.Bruit,
                        Quoi = "Microsoft Store" },
            new Sujet { Motif = "HelloForBusiness",  Classe = Classe.Bruit,
                        Quoi = "Windows Hello entreprise (inutilisé sur un poste personnel)" },
            new Sujet { Motif = "UserSettingsBackup",Classe = Classe.Bruit,
                        Quoi = "sauvegarde des réglages utilisateur" },
            new Sujet { Motif = "WinRM",             Classe = Classe.Bruit,
                        Quoi = "administration à distance (inutilisée sur un poste personnel)" },
            new Sujet { Motif = "UAC",               Classe = Classe.Bruit,
                        Quoi = "contrôle de compte d'utilisateur" },
            new Sujet { Motif = "TWinUI",            Classe = Classe.Bruit,
                        Quoi = "interface des applications modernes" },
            new Sujet { Motif = "Diagnosis-Scheduled", Classe = Classe.Bruit,
                        Quoi = "diagnostics planifiés de Windows" },
            new Sujet { Motif = "LessPrivilegedAppContainer", Classe = Classe.Bruit,
                        Quoi = "bac à sable des applications" }
        };

        // ==================================================================
        //  Analyse — PURE, donc testable sans machine
        // ==================================================================

        private static Sujet SujetDe(string canal)
        {
            if (string.IsNullOrEmpty(canal)) return null;
            foreach (Sujet s in Sujets)
                if (canal.IndexOf(s.Motif, StringComparison.OrdinalIgnoreCase) >= 0) return s;
            return null;
        }

        /// <summary>PUR : ce qu'on sait dire de ce groupe.</summary>
        public static Classe ClasseDe(Groupe g)
        {
            if (g == null) return Classe.Inconnu;
            Sujet s = SujetDe(g.Canal);
            return s == null ? Classe.Inconnu : s.Classe;
        }

        /// <summary>PUR : ce que ce canal signifie, ou vide si on ne le connaît pas.</summary>
        public static string Signification(Groupe g)
        {
            Sujet s = g == null ? null : SujetDe(g.Canal);
            return s == null ? "" : s.Quoi;
        }

        /// <summary>PUR : pourquoi ça compte, ou vide. Seuls les canaux sérieux en ont un.</summary>
        public static string Pourquoi(Groupe g)
        {
            Sujet s = g == null ? null : SujetDe(g.Canal);
            return s == null || s.Pourquoi == null ? "" : s.Pourquoi;
        }

        /// <summary>PUR : nom court du canal — « CodeIntegrity/Operational » plutôt que le
        /// chemin complet « Microsoft-Windows-CodeIntegrity/Operational », qui ne tient pas
        /// sur une ligne et dont le préfixe est le même partout.</summary>
        public static string NomCourt(string canal)
        {
            if (string.IsNullOrEmpty(canal)) return "";
            const string prefixe = "Microsoft-Windows-";
            return canal.StartsWith(prefixe, StringComparison.OrdinalIgnoreCase)
                 ? canal.Substring(prefixe.Length) : canal;
        }

        /// <summary>
        /// PUR : le rapport, ou une chaîne vide s'il n'y a rien à dire.
        ///
        /// L'ordre n'est pas cosmétique. Les canaux sérieux d'abord, parce que ce sont les seuls
        /// où une panne est invisible ailleurs. Les inconnus ensuite, sans interprétation. Le
        /// bruit en dernier et en UNE ligne : le nommer évite qu'on s'en inquiète en ouvrant
        /// l'Observateur d'événements, le détailler noierait le reste.
        /// </summary>
        public static string Rapport(List<Groupe> l, int jours)
        {
            if (l == null || l.Count == 0) return "";

            var serieux = new List<Groupe>();
            var inconnus = new List<Groupe>();
            int bruitTypes = 0, bruitTotal = 0;

            foreach (Groupe g in l)
                switch (ClasseDe(g))
                {
                    case Classe.Serieux: serieux.Add(g); break;
                    case Classe.Bruit: bruitTypes++; bruitTotal += g.Nombre; break;
                    default: inconnus.Add(g); break;
                }

            // Du bruit et RIEN d'autre ne justifie pas un bloc. LogDoctor a déjà sa section
            // « bruit connu » pour Système et Application ; en ajouter une seconde qui ne dit
            // que « tout va bien » reviendrait à faire soi-même le bruit qu'on prétend trier.
            // La ligne de bruit ne paraît donc qu'en contexte, sous du contenu réel.
            if (serieux.Count == 0 && inconnus.Count == 0) return "";

            // Les sérieux sont fusionnés par canal (le niveau auquel on prétend savoir lire) ;
            // les inconnus gardent leur identifiant, seule information utile dont on dispose.
            serieux = FusionneParCanal(serieux);
            serieux.Sort(ParImportance);
            inconnus.Sort(ParImportance);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("LES AUTRES JOURNAUX DE WINDOWS  —  " + jours + " derniers jours");
            sb.AppendLine("   Au-delà de « Système » et « Application » : les canaux Operational");
            sb.AppendLine("   et Admin, que l'Observateur d'événements n'ouvre jamais tout seul.");
            sb.AppendLine("   C'est là que s'écrivent les pannes qui n'affichent rien.");
            sb.AppendLine();

            if (serieux.Count > 0)
            {
                sb.AppendLine("   CE QUI MÉRITE D'ÊTRE LU");
                foreach (Groupe g in serieux)
                {
                    sb.AppendLine("   ⚠ " + NomCourt(g.Canal) + "   ×" + g.Nombre
                        + (g.Types > 1 ? " sur " + g.Types + " types" : "")
                        + "   (dernier : " + g.Dernier.ToString("dd/MM à HH:mm") + ")");
                    sb.Append(PilotesRefuses.Plie(Signification(g), 66, "        "));
                    string pq = Pourquoi(g);
                    if (pq.Length > 0) sb.Append(PilotesRefuses.Plie(pq, 66, "        "));
                }
                sb.AppendLine();
            }

            if (inconnus.Count > 0)
            {
                sb.AppendLine("   CE QUE JE NE SAIS PAS INTERPRÉTER");
                sb.AppendLine("   (montré tel quel : inventer une cause serait pire que se taire)");
                int n = 0;
                foreach (Groupe g in inconnus)
                {
                    if (n++ >= 8) break;
                    sb.AppendLine("   • " + LigneInconnu(g));
                }
                if (inconnus.Count > n)
                    sb.AppendLine("   … et " + (inconnus.Count - n) + " autre(s).");
                sb.AppendLine();
            }

            if (bruitTypes > 0)
            {
                sb.Append(PilotesRefuses.Plie("BRUIT CONNU, SANS CONSÉQUENCE : " + bruitTotal
                    + " événement(s) sur " + bruitTypes + " type(s) — synchronisation de tuiles, "
                    + "déploiement d'applications du Store, notifications. Dit ici pour que tu ne "
                    + "t'en inquiètes pas en ouvrant l'Observateur d'événements.", 69, "   "));
            }

            return sb.ToString();
        }

        /// <summary>
        /// PUR : la ligne d'un canal qu'on n'interprète pas.
        ///
        /// Le fournisseur répète presque toujours le canal — « Microsoft-Windows-WMI-Activity »
        /// pour « WMI-Activity/Operational ». Le réécrire poussait la ligne à 95 colonnes, au-delà
        /// de la zone d'affichage, pour une information déjà présente. Il n'est donc montré que
        /// lorsqu'il apporte quelque chose.
        /// </summary>
        public static string LigneInconnu(Groupe g)
        {
            if (g == null) return "";
            string canal = NomCourt(g.Canal);
            string f = NomCourt(g.Fournisseur);
            bool redondant = f.Length == 0
                || canal.StartsWith(f, StringComparison.OrdinalIgnoreCase);
            return canal + (redondant ? "" : "  " + f)
                 + "  id " + g.Id + "  ×" + g.Nombre
                 + "  (" + g.Dernier.ToString("dd/MM à HH:mm") + ")";
        }

        /// <summary>
        /// PUR : réunit en un seul groupe tous les identifiants d'un même canal.
        ///
        /// La lecture regroupe par (canal, fournisseur, identifiant) — c'est la bonne finesse
        /// pour compter. Mais ce module ne prétend savoir interpréter qu'au niveau du CANAL :
        /// afficher trois lignes « Storage-ClassPnP » portant la même phrase et trois comptes
        /// différents donne à lire trois incidents là où il y en a un, et ressemble surtout à un
        /// défaut d'affichage. On somme, on garde la date la plus récente, et on dit combien
        /// d'identifiants distincts sont réunis.
        ///
        /// Ne s'applique QU'aux canaux connus. Pour un canal inconnu, l'identifiant est la seule
        /// information utile qu'on ait : la fusionner reviendrait à la jeter.
        /// </summary>
        public static List<Groupe> FusionneParCanal(List<Groupe> l)
        {
            var res = new List<Groupe>();
            if (l == null) return res;

            var parCanal = new Dictionary<string, Groupe>(StringComparer.OrdinalIgnoreCase);
            foreach (Groupe g in l)
            {
                if (g == null) continue;
                Groupe f;
                if (!parCanal.TryGetValue(g.Canal, out f))
                {
                    // Copie : on ne modifie jamais le groupe rendu par la lecture, qui est mis
                    // en cache et relu par d'autres appelants.
                    f = new Groupe
                    {
                        Canal = g.Canal, Fournisseur = g.Fournisseur, Id = g.Id,
                        Niveau = g.Niveau, Nombre = 0, Types = 0,
                        Dernier = g.Dernier, Exemple = g.Exemple
                    };
                    parCanal[g.Canal] = f;
                    res.Add(f);
                }
                f.Nombre += g.Nombre;
                f.Types++;
                if (g.Dernier > f.Dernier) f.Dernier = g.Dernier;
            }
            return res;
        }

        /// <summary>PUR : le plus récent d'abord, et à date égale le plus fréquent. Un incident
        /// vieux de six jours compte moins qu'un incident de ce matin, même répété.</summary>
        private static int ParImportance(Groupe a, Groupe b)
        {
            int d = b.Dernier.CompareTo(a.Dernier);
            return d != 0 ? d : b.Nombre.CompareTo(a.Nombre);
        }

        // ==================================================================
        //  Lecture machine — IMPURE, et qui n'a pas le droit de lever
        // ==================================================================

        private const int MaxLongueurExemple = 160;

        private static List<Groupe> _cache;
        private static int _cacheJours = -1;
        private static readonly object _verrou = new object();

        /// <summary>Le balayage, calculé une fois par durée demandée. Coûte quelques secondes :
        /// à appeler en arrière-plan, jamais sur le fil de l'interface.</summary>
        public static List<Groupe> LireEnCache(int jours)
        {
            lock (_verrou)
            {
                if (_cache == null || _cacheJours != jours)
                {
                    _cache = Lire(jours);
                    _cacheJours = jours;
                }
                return _cache;
            }
        }

        /// <summary>
        /// Balaie tous les journaux, sauf ceux que LogDoctor couvre déjà.
        ///
        /// Ne lève jamais. Un canal peut être désactivé, vide, ou refusé faute de droits — c'est
        /// le cas courant, pas l'exception : on passe au suivant. Rendre une liste partielle est
        /// ici correct, parce que chaque canal est indépendant des autres ; ce serait faux dans
        /// un module qui construit un état unique à partir de plusieurs lectures.
        /// </summary>
        public static List<Groupe> Lire(int jours)
        {
            var parCle = new Dictionary<string, Groupe>(StringComparer.OrdinalIgnoreCase);
            if (jours <= 0) return new List<Groupe>();

            try
            {
                var session = EventLogSession.GlobalSession;
                foreach (string canal in session.GetLogNames())
                {
                    if (EstDejaCouvert(canal)) continue;
                    LitCanal(canal, jours, parCle);
                }
            }
            catch (Exception ex)
            {
                // Échec de l'ÉNUMÉRATION : on ne balaie alors rien du tout, et le silence qui
                // suit ressemble à s'y méprendre à une machine sans erreur.
                JournalTechnique.Echec("CanauxWindows.Lire", ex);
            }

            return new List<Groupe>(parCle.Values);
        }

        private static bool EstDejaCouvert(string canal)
        {
            foreach (string c in DejaCouverts)
                if (string.Equals(canal, c, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void LitCanal(string canal, int jours, Dictionary<string, Groupe> parCle)
        {
            try
            {
                // Filtrage côté journal : niveau ET fenêtre de temps. Lire puis trier côté
                // application coûterait des secondes par canal, sur 139 canaux.
                long ms = (long)jours * 24L * 3600L * 1000L;
                var requete = new EventLogQuery(canal, PathType.LogName,
                    "*[System[(Level=" + NiveauCritique + " or Level=" + NiveauErreur + ")"
                    + " and TimeCreated[timediff(@SystemTime) <= " + ms + "]]]");
                requete.ReverseDirection = true;
                requete.TolerateQueryErrors = true;

                using (var lecteur = new EventLogReader(requete))
                {
                    int lus = 0;
                    for (EventRecord ev = lecteur.ReadEvent(); ev != null; ev = lecteur.ReadEvent())
                    {
                        using (ev)
                        {
                            if (++lus > MaxParCanal) break;
                            Absorbe(parCle, canal, ev);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Canal désactivé, absent ou refusé : les autres restent lisibles. Noté quand
                // même — un canal SÉRIEUX systématiquement illisible est une information, et la
                // répétition est comptée, pas recopiée.
                JournalTechnique.Echec("CanauxWindows.LitCanal " + canal, ex);
            }
        }

        private static void Absorbe(Dictionary<string, Groupe> parCle, string canal, EventRecord ev)
        {
            try
            {
                string fournisseur = ev.ProviderName ?? "";
                string cle = canal + "|" + fournisseur + "|" + ev.Id;

                Groupe g;
                if (!parCle.TryGetValue(cle, out g))
                {
                    g = new Groupe
                    {
                        Canal = canal, Fournisseur = fournisseur, Id = ev.Id,
                        Niveau = ev.Level.HasValue ? ev.Level.Value : NiveauErreur,
                        Exemple = Exemple(ev)
                    };
                    parCle[cle] = g;
                }
                g.Nombre++;

                DateTime t = ev.TimeCreated.HasValue ? ev.TimeCreated.Value : default(DateTime);
                if (t != default(DateTime) && t > g.Dernier) g.Dernier = t;
            }
            catch { }
        }

        /// <summary>Le message du premier événement du groupe, tronqué. Formater coûte cher —
        /// on ne le fait donc qu'une fois par groupe, pas une fois par événement.</summary>
        private static string Exemple(EventRecord ev)
        {
            try
            {
                string m = ev.FormatDescription();
                if (string.IsNullOrEmpty(m)) return "";
                m = m.Replace('\r', ' ').Replace('\n', ' ').Trim();
                while (m.Contains("  ")) m = m.Replace("  ", " ");
                return m.Length > MaxLongueurExemple ? m.Substring(0, MaxLongueurExemple) + "…" : m;
            }
            catch { return ""; }
        }
    }
}
