using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// LE MÉDECIN DES JOURNAUX WINDOWS : lit les événements CRITIQUES et ERREURS des journaux Système
    /// et Application, les regroupe, puis les TRADUIT en diagnostic clair — cause probable, gravité,
    /// et quoi faire. Windows enregistre tout ; personne ne sait le lire (« Observateur d'événements »
    /// est illisible pour un joueur).
    ///
    /// Deux principes tenus ici :
    ///  • on DIT ce qui est du BRUIT connu et inoffensif (DistributedCOM 10016, etc.) au lieu de faire
    ///    peur avec des « erreurs » qui n'en sont pas — c'est la moitié du travail d'un vrai diagnostic ;
    ///  • un événement inconnu est présenté comme inconnu, jamais interprété au hasard.
    /// </summary>
    internal static class LogDoctor
    {
        public sealed class EventGroup
        {
            public string Provider;
            public int EventId;
            public int Count;
            public DateTime Last;
            public string Log;         // System / Application
        }

        public sealed class Finding
        {
            public int Severity;       // 0 = bruit, 1 = info, 2 = à surveiller, 3 = grave, 4 = critique
            public string Title;
            public string Cause;
            public string Fix;
            public int Count;
            public DateTime Last;
            public bool Known;
        }

        // ---- BASE DE CONNAISSANCES : (fournisseur, id) → sens en français ----
        private sealed class Known { public int Sev; public string Title; public string Cause; public string Fix; }

        private static readonly Dictionary<string, Known> Kb = Build();
        private static string Key(string provider, int id) { return (provider ?? "").ToLowerInvariant() + "/" + id; }

        private static Dictionary<string, Known> Build()
        {
            var d = new Dictionary<string, Known>(StringComparer.Ordinal);
            void K(string prov, int id, int sev, string title, string cause, string fix)
            { d[Key(prov, id)] = new Known { Sev = sev, Title = title, Cause = cause, Fix = fix }; }

            // --- Arrêts brutaux / écrans bleus ---
            K("Microsoft-Windows-Kernel-Power", 41, 4, "Arrêt BRUTAL du PC (sans extinction propre)",
              "Coupure de courant, alimentation insuffisante ou instable, surchauffe, ou plantage matériel dur.",
              "Vérifie les câbles d'alimentation (12VHPWR sur RTX 40), surveille les températures pendant une partie, et coupe tout overclock.");
            K("Microsoft-Windows-WER-SystemErrorReporting", 1001, 4, "Écran bleu (BSOD)",
              "Le noyau Windows s'est arrêté sur une erreur : pilote, mémoire, ou matériel.",
              "Le panneau « Stabilité du PC » donne le code exact du dernier écran bleu. Pilote GPU propre (DDU) et test mémoire sont les deux premières pistes.");
            K("EventLog", 6008, 3, "Arrêt inattendu du système",
              "Le PC s'est éteint sans passer par l'arrêt normal (souvent le même incident qu'un Kernel-Power 41).",
              "Même piste : alimentation, températures, overclock.");

            // --- Matériel (les plus importants et les moins connus) ---
            K("Microsoft-Windows-WHEA-Logger", 17, 2, "Erreur matérielle CORRIGÉE (WHEA)",
              "Le matériel a rencontré une erreur qu'il a su corriger : souvent un lien PCIe capricieux, de la RAM en limite, ou un overclock un peu trop ambitieux.",
              "Isolé, ce n'est pas grave. Répété : coupe l'overclock (CPU/GPU/RAM), réenfonce la carte graphique, et teste la mémoire.");
            K("Microsoft-Windows-WHEA-Logger", 18, 4, "Erreur matérielle FATALE (WHEA)",
              "Erreur matérielle non corrigible : CPU, mémoire, ou bus PCIe. C'est l'un des signaux les plus sérieux du journal.",
              "Coupe TOUT overclock (y compris XMP/EXPO pour tester), teste la RAM (barrette par barrette), et vérifie les températures et l'alimentation.");
            K("Microsoft-Windows-WHEA-Logger", 19, 2, "Erreur mémoire corrigée (WHEA)",
              "Une erreur de mémoire a été corrigée automatiquement.",
              "Si ça se répète : teste la RAM et désactive XMP/EXPO le temps du test.");

            // --- Disques ---
            K("disk", 7, 3, "Secteur défectueux signalé par le disque",
              "Le disque a signalé un secteur illisible : câble, ou disque qui commence à fatiguer.",
              "Lance « état de mes disques » (SMART) et SAUVEGARDE ce qui compte. Sur un disque interne : change le câble SATA avant de conclure.");
            K("disk", 51, 2, "Erreur de lecture/écriture disque",
              "Le disque a eu du mal à lire ou écrire : câble, alimentation, ou usure.",
              "Vérifie la santé SMART ; si le disque est un SSD, mets à jour son firmware.");
            K("disk", 153, 2, "Commande disque abandonnée (réessai)",
              "Le contrôleur a dû réessayer une commande : souvent un câble SATA ou un disque externe mal alimenté.",
              "Change le câble, évite les rallonges USB pour les disques externes.");
            K("Ntfs", 55, 3, "Corruption du système de fichiers NTFS",
              "La structure du disque est abîmée (coupure pendant une écriture, disque plein, matériel).",
              "Lance « répare Windows » puis un CHKDSK sur le disque concerné.");
            K("volmgr", 162, 1, "Vidage mémoire suite à un plantage",
              "Windows a écrit un fichier de vidage après un écran bleu.",
              "C'est la trace d'un BSOD passé, pas une panne en soi : voir « Stabilité du PC ».");

            // --- Pilotes / GPU ---
            K("Display", 4101, 4, "Pilote graphique RÉINITIALISÉ (dispositif de rendu perdu)",
              "Le pilote GPU a cessé de répondre et Windows l'a relancé : surchauffe, overclock instable, alimentation, ou pilote abîmé.",
              "Voir « Mon pilote GPU est-il instable ? ». Remède classique : réinstallation PROPRE avec DDU, overclock coupé.");
            K("nvlddmkm", 13, 3, "Erreur du pilote NVIDIA",
              "Le pilote NVIDIA a signalé une erreur interne.",
              "Réinstallation propre (DDU) et retour à une version antérieure si le problème est apparu après une mise à jour.");
            K("Microsoft-Windows-Kernel-PnP", 219, 2, "Un pilote n'a pas pu être chargé",
              "Un périphérique a échoué à démarrer : pilote manquant, abîmé, ou matériel mal branché.",
              "Ouvre « Périphériques (erreurs) » : le matériel fautif y est listé avec son code d'erreur.");

            // --- Services / applications ---
            K("Application Error", 1000, 2, "Plantage d'une application",
              "Une application s'est arrêtée sur une erreur (module fautif indiqué dans l'événement).",
              "Si c'est un JEU : vérifie ses fichiers (Steam) et les bibliothèques Visual C++/DirectX.");
            K("Application Hang", 1002, 1, "Application figée",
              "Une application a cessé de répondre puis a été fermée.",
              "Isolé, c'est banal. Répété sur le même logiciel : réinstalle-le.");
            K("Service Control Manager", 7031, 1, "Un service Windows s'est arrêté brutalement",
              "Un service a planté et Windows l'a relancé.",
              "Sans symptôme visible, c'est bénin. Si ça revient sans cesse, note le nom du service.");
            K("Service Control Manager", 7000, 1, "Un service n'a pas pu démarrer",
              "Service désactivé, mal configuré, ou dépendant d'un pilote absent.",
              "Bénin dans la plupart des cas ; à regarder si une fonction Windows ne marche plus.");

            // --- Réseau ---
            K("Microsoft-Windows-DNS-Client", 1014, 1, "Échec de résolution DNS",
              "Le PC n'a pas réussi à traduire un nom de site en adresse : DNS lent ou injoignable.",
              "Essaie « DNS rapide » (panneau Réseau) : c'est gratuit et souvent immédiat.");
            K("Tcpip", 4227, 1, "Connexion TCP réinitialisée",
              "Une connexion réseau a été coupée par l'autre bout ou par le réseau.",
              "Isolé, sans importance. Massif : voir « ma connexion & ma box ».");

            // --- Ajouts issus de MACHINES RÉELLES (les plus fréquents en pratique) ---
            K(".NET Runtime", 1026, 2, "Plantage d'une application .NET",
              "Une application écrite en .NET s'est arrêtée sur une exception non gérée (lanceur de jeu, utilitaire…).",
              "Si c'est un lanceur ou un overlay (Steam, Discord, RGB…) : réinstalle-le. Vérifie aussi que .NET est à jour (panneau Bibliothèques).");
            K("BTHUSB", 5, 1, "Erreur du contrôleur Bluetooth",
              "La radio Bluetooth a signalé une erreur : clé USB capricieuse, pilote ancien, ou interférence.",
              "Si ta souris/manette Bluetooth décroche : passe-la en filaire ou en dongle 2,4 GHz pour jouer — c'est aussi meilleur pour la latence.");
            K("Microsoft-Windows-WindowsUpdateClient", 20, 1, "Échec d'installation d'une mise à jour",
              "Une mise à jour Windows n'a pas pu s'installer : espace disque, fichiers système, ou composant occupé.",
              "Libère de l'espace, redémarre, puis « répare Windows » si ça recommence.");

            // --- BRUIT CONNU : dire que ce n'est RIEN est aussi utile qu'alerter ---
            K("DCOM", 10010, 0, "DCOM 10010 (bruit connu, très fréquent)",
              "Un composant Windows ne s'est pas enregistré dans le délai imparti. Sans conséquence dans l'immense "
              + "majorité des cas — c'est l'erreur la plus courante des journaux Windows.",
              "Rien à faire. Ignore les « correctifs registre » des forums : ils cassent plus qu'ils ne réparent.");
            K("Microsoft-Windows-CAPI2", 513, 0, "CAPI2 513 (bruit connu)",
              "Service de cryptographie : échec de synchronisation de la liste des certificats racines.",
              "Aucun impact sur les jeux ni la sécurité au quotidien.");
            K("Microsoft-Windows-Time-Service", 134, 0, "Synchronisation de l'heure (bruit connu)",
              "Windows n'a pas pu joindre son serveur de temps à cet instant.",
              "Sans conséquence : la synchronisation reprend seule.");
            K("Microsoft-Windows-DistributedCOM", 10016, 0, "DistributedCOM 10016 (bruit connu)",
              "Erreur de permissions COM interne à Windows. Microsoft la documente comme SANS conséquence.",
              "Rien à faire — et surtout PAS les « correctifs registre » qu'on trouve sur les forums : ils cassent plus qu'ils ne réparent.");
            K("Microsoft-Windows-Kernel-EventTracing", 3, 0, "Traçage d'événements (bruit connu)",
              "Message interne de collecte de traces Windows.",
              "Aucun impact, rien à faire.");
            K("Microsoft-Windows-Kernel-EventTracing", 2, 0, "Traçage d'événements (bruit connu)",
              "Message interne de collecte de traces Windows.",
              "Aucun impact, rien à faire.");
            K("Microsoft-Windows-Search", 3036, 0, "Indexation de la recherche (bruit connu)",
              "Le service d'indexation a rencontré un fichier qu'il ne peut pas lire.",
              "Sans impact sur les performances en jeu.");
            K("Microsoft-Windows-Winlogon", 6000, 0, "Ouverture de session (bruit connu)",
              "Message d'attente d'un composant au démarrage de session.",
              "Aucun impact.");
            return d;
        }

        // ---- Lecture des journaux ----
        private static string Query(string logName, int days, int max)
        {
            long ms = (long)days * 24L * 3600L * 1000L;
            string xpath = "*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= " + ms + "]]]";
            try
            {
                var r = Sys.Run(Sys.Sys32("wevtutil.exe"), "qe " + logName + " \"/q:" + xpath + "\" /c:" + max + " /rd:true /f:xml");
                return r.ExitCode == 0 ? r.Output : null;
            }
            catch { return null; }
        }

        /// <summary>Regroupe les événements d'un XML wevtutil par (fournisseur, id). PUR → testable.</summary>
        public static List<EventGroup> Parse(string xml, string logName)
        {
            var map = new Dictionary<string, EventGroup>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(xml)) return new List<EventGroup>();
            foreach (Match m in Regex.Matches(xml, "<Event[ >].*?</Event>", RegexOptions.Singleline))
            {
                string ev = m.Value;
                var pm = Regex.Match(ev, "<Provider[^>]*Name='([^']*)'");
                if (!pm.Success) pm = Regex.Match(ev, "<Provider[^>]*Name=\"([^\"]*)\"");
                var im = Regex.Match(ev, "<EventID[^>]*>(\\d+)</EventID>");
                if (!pm.Success || !im.Success) continue;
                string prov = pm.Groups[1].Value;
                int id; if (!int.TryParse(im.Groups[1].Value, out id)) continue;
                DateTime when = DateTime.MinValue;
                var tm = Regex.Match(ev, "SystemTime='([^']*)'");
                if (!tm.Success) tm = Regex.Match(ev, "SystemTime=\"([^\"]*)\"");
                if (tm.Success) DateTime.TryParse(tm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal, out when);
                string k = Key(prov, id);
                EventGroup g;
                if (!map.TryGetValue(k, out g)) { g = new EventGroup { Provider = prov, EventId = id, Log = logName }; map[k] = g; }
                g.Count++;
                if (when > g.Last) g.Last = when;
            }
            var outp = new List<EventGroup>(map.Values);
            outp.Sort((a, b) => b.Count.CompareTo(a.Count));
            return outp;
        }

        /// <summary>Traduit les groupes en diagnostics classés. PUR → testable.</summary>
        public static List<Finding> Diagnose(List<EventGroup> groups)
        {
            var outp = new List<Finding>();
            if (groups == null) return outp;
            foreach (var g in groups)
            {
                Known k;
                if (Kb.TryGetValue(Key(g.Provider, g.EventId), out k))
                    outp.Add(new Finding { Severity = k.Sev, Title = k.Title, Cause = k.Cause, Fix = k.Fix,
                                           Count = g.Count, Last = g.Last, Known = true });
                else
                    outp.Add(new Finding
                    {
                        Severity = 1, Known = false, Count = g.Count, Last = g.Last,
                        Title = g.Provider + " (événement " + g.EventId + ")",
                        Cause = "Je ne connais pas cet événement : je ne vais donc pas inventer une explication.",
                        Fix = "Si tu as un symptôme précis en même temps, dis-le-moi : je croiserai avec le reste des mesures."
                    });
            }
            // du plus grave au plus fréquent ; le bruit connu passe en dernier
            outp.Sort(delegate (Finding a, Finding b)
            {
                if (a.Severity != b.Severity) return b.Severity.CompareTo(a.Severity);
                return b.Count.CompareTo(a.Count);
            });
            return outp;
        }

        /// <summary>Mise en forme du bilan. PUR → testable.</summary>
        public static string Format(List<Finding> f, int days)
        {
            if (f == null || f.Count == 0)
                return "🩺 Journaux Windows (" + days + " derniers jours) : AUCUNE erreur ni événement critique. "
                     + "C'est le meilleur résultat possible — ton PC ne se plaint de rien.";
            var sb = new System.Text.StringBuilder();
            var serious = new List<Finding>(); var noise = new List<Finding>(); var unknown = new List<Finding>();
            foreach (var x in f)
            {
                if (x.Severity == 0) noise.Add(x);
                else if (!x.Known) unknown.Add(x);
                else serious.Add(x);
            }
            sb.Append("🩺 Diagnostic des journaux Windows (").Append(days).Append(" derniers jours)\n");
            if (serious.Count == 0)
                sb.Append("\n✅ Aucune erreur SÉRIEUSE identifiée.\n");
            else
            {
                sb.Append('\n');
                int n = 0;
                foreach (var x in serious)
                {
                    if (n++ >= 6) break;
                    string icon = x.Severity >= 4 ? "🚨" : x.Severity == 3 ? "⚠️" : "•";
                    sb.Append(icon).Append(' ').Append(x.Title).Append("  ×").Append(x.Count);
                    if (x.Last > DateTime.MinValue) sb.Append("  (dernier : ").Append(x.Last.ToLocalTime().ToString("dd/MM HH:mm")).Append(')');
                    sb.Append('\n');
                    sb.Append("   Cause : ").Append(x.Cause).Append('\n');
                    sb.Append("   À faire : ").Append(x.Fix).Append('\n');
                }
                if (serious.Count > 6) sb.Append("…et ").Append(serious.Count - 6).Append(" autre(s) type(s) d'erreur.\n");
            }
            if (noise.Count > 0)
            {
                sb.Append("\n😌 Bruit connu, SANS conséquence (je te le dis pour que tu ne t'inquiètes pas en ouvrant "
                        + "l'Observateur d'événements) :\n");
                int n = 0;
                foreach (var x in noise) { if (n++ >= 4) break; sb.Append("   • ").Append(x.Title).Append("  ×").Append(x.Count).Append('\n'); }
            }
            if (unknown.Count > 0)
            {
                sb.Append("\n❔ Événements que je ne connais pas (je ne les interprète pas) :\n");
                int n = 0;
                foreach (var x in unknown) { if (n++ >= 4) break; sb.Append("   • ").Append(x.Title).Append("  ×").Append(x.Count).Append('\n'); }
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Analyse réelle des journaux Système + Application.</summary>
        public static string Run(int days, Action<string, int> log)
        {
            var all = new List<EventGroup>();
            if (log != null) log("Lecture du journal Système…", 0);
            all.AddRange(Parse(Query("System", days, 300), "System"));
            if (log != null) log("Lecture du journal Application…", 0);
            all.AddRange(Parse(Query("Application", days, 300), "Application"));
            if (all.Count == 0)
                return "🩺 Je n'ai pas pu lire les journaux Windows (droits administrateur nécessaires, ou service "
                     + "« Journal des événements » arrêté).";
            return Format(Diagnose(all), days);
        }
    }
}
