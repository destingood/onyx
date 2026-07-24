using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// L'enquêteur du Copilote : au lieu d'ouvrir un panneau, il lance TOUTES les mesures
    /// utiles (écrans, crashs pilote GPU, capteurs, disque, bibliothèques, réglages
    /// néfastes, connexion, processus en fond, optimisations), croise les résultats,
    /// explique son raisonnement (« pourquoi ? ») et classe les causes par
    /// impact réel et rend un plan d'action ordonné. Chaque cause n'apparaît que si elle
    /// est vraiment constatée — jamais de faux problème pour se rendre intéressant.
    /// 100 % local : rien ne quitte la machine (le test de connexion se limite à des
    /// échos ICMP vers 1.1.1.1 — aucune donnée transmise).
    /// </summary>
    internal static class Investigator
    {
        private sealed class Finding
        {
            public int Impact;                       // 0-100, sert au tri
            public string Text;                      // « Ton écran est à 60 Hz… »
            public string Why;                       // constaté / seuil / conséquence — servi sur « pourquoi ? »
            public DocAssistant.ChatAction Fix;      // correction proposée (peut être null)
        }

        /// <summary>Enquête complète. 'focus' oriente le vocabulaire de la conclusion
        /// (« ça rame », « ça crash »…) sans changer les mesures effectuées.</summary>
        public static DocAssistant.Reply Investigate(string focus, BadgeCatalog.Stats st, Action<string, int> log)
        {
            var found = new List<Finding>();
            var ok = new List<string>();

            // --- 1. Écrans sous leur fréquence maximale (le gain le plus spectaculaire) ---
            try
            {
                var screens = DisplayInfo.Query();
                var below = new List<DisplayInfo.DisplayMode>();
                if (screens != null) foreach (var d in screens) if (d.BelowMax) below.Add(d);
                if (below.Count > 0)
                {
                    var f = new Finding { Impact = 95, Fix = ChatActions.FixScreen() };
                    f.Text = below.Count == 1
                        ? "Ton écran tourne à " + below[0].CurrentHz + " Hz alors qu'il peut faire " + below[0].MaxHz + " Hz. "
                          + "C'est de la fluidité que tu as payée et que tu n'utilises pas — correction gratuite et immédiate."
                        : below.Count + " écrans tournent sous leur fréquence maximale — correction gratuite et immédiate.";
                    f.Why = "Écrans — constaté via l'API d'affichage Windows : "
                          + below[0].CurrentHz + " Hz alors que le mode " + below[0].MaxHz + " Hz existe sur le même écran. "
                          + "Conséquence : chaque image s'affiche plus tard que possible, la fluidité perçue chute. Le passage au maximum est gratuit et réversible.";
                    found.Add(f);
                }
                else if (screens != null && screens.Count > 0) ok.Add("écrans à leur fréquence maximale");
            }
            catch { }

            // --- 1bis. Crashs du pilote GPU sur 14 jours (LE signal de l'instabilité en jeu) ---
            try
            {
                int gpuErr = CrashScan.GpuDriverErrors(14);
                if (gpuErr > 0)
                    found.Add(new Finding
                    {
                        Impact = 90,
                        Text = "Le pilote de ta carte graphique a signalé " + gpuErr + " erreur(s) ces 14 derniers jours. "
                             + "C'est la signature des crashs « dispositif de rendu perdu » : surchauffe, overclock instable "
                             + "ou pilote abîmé. Gratuit : dépoussiérage + panneau Températures pour surveiller, et si ça "
                             + "persiste, pilote réinstallé PROPREMENT avec DDU (gratuit, 1 clic depuis Bibliothèques).",
                        Why = "Crashs GPU — constaté : " + gpuErr + " événement(s) du pilote graphique dans le journal "
                            + "système Windows sur 14 jours ; attendu sur un PC stable : 0. Chaque événement = le pilote "
                            + "s'est réinitialisé (freeze/écran noir possible en jeu). Les remèdes proposés sont tous gratuits."
                    });
                else ok.Add("aucun crash du pilote GPU (14 j)");
            }
            catch { }

            // --- 2. Température GPU (cause n°1 des chutes de FPS soudaines) ---
            try
            {
                using (var mon = new HwMonitor())
                {
                    HwSample s = mon.Sample();
                    if (s.Gpu != null && s.Gpu.Ok && s.Gpu.TempC >= 85)
                        found.Add(new Finding
                        {
                            Impact = 85,
                            Text = "Ton GPU monte à " + s.Gpu.TempC.ToString("0") + " °C. Au-delà de 85 °C il se bride tout seul : "
                                 + "les FPS s'effondrent en pleine partie. Gratuit : dépoussiérage, flux d'air, et courbe de "
                                 + "ventilation avec Fan Control (gratuit, installable en 1 clic).",
                            Why = "Température GPU — constaté : " + s.Gpu.TempC.ToString("0") + " °C (capteur constructeur, "
                                + "lecture en direct) ; seuil : 85 °C = zone où le GPU réduit seul ses fréquences pour se "
                                + "protéger. Les remèdes efficaces sont d'abord gratuits (poussière, flux d'air, ventilation)."
                        });
                    else if (s.Gpu != null && s.Gpu.Ok && s.Gpu.TempC > 0) ok.Add("températures GPU sous contrôle");

                    if (s.RamLoad >= 90)
                        found.Add(new Finding
                        {
                            Impact = 65,
                            Text = "Ta mémoire est occupée à " + s.RamLoad.ToString("0") + " %. Quand la RAM sature, "
                                 + "Windows échange sur le disque et le jeu saccade. Gratuit : ferme les gourmands "
                                 + "(panneau « Qui ralentit mon PC ») avant même de penser à acheter des barrettes.",
                            Why = "Mémoire — constaté : " + s.RamLoad.ToString("0") + " % de RAM occupée ; seuil : 90 % = "
                                + "Windows commence à échanger sur le disque (bien plus lent), d'où les saccades. Fermer "
                                + "les programmes de fond est gratuit et se teste immédiatement."
                        });
                    else if (s.RamLoad > 0) ok.Add("mémoire disponible");
                }
            }
            catch { }

            // --- 3. Disque système ---
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                var di = new DriveInfo(root);
                double freeGb = di.AvailableFreeSpace / 1073741824.0;
                double totGb = di.TotalSize / 1073741824.0;
                int pct = totGb > 0 ? (int)Math.Round(freeGb / totGb * 100) : 100;
                long recov = ChatActions.RecoverableMb();
                if (pct < 12)
                    found.Add(new Finding
                    {
                        Impact = 80,
                        Text = "Ton disque système est plein à " + (100 - pct) + " % (" + freeGb.ToString("0") + " Go libres). "
                             + "Sous 10 % de libre, Windows ralentit franchement. Le nettoyage est gratuit : uniquement des "
                             + "fichiers qui se régénèrent (temporaires, caches).",
                        Why = "Disque système — constaté : " + freeGb.ToString("0") + " Go libres (" + pct + " %) ; seuil : "
                            + "sous ~10-12 % de libre, Windows manque de place pour ses caches, ses mises à jour et le "
                            + "fichier d'échange → tout ralentit. Le nettoyage ne touche que du régénérable, gratuit.",
                        Fix = ChatActions.FixDisk()
                    });
                else if (recov >= 3072)
                    found.Add(new Finding
                    {
                        Impact = 45,
                        Text = "" + (recov / 1024.0).ToString("0.0") + " Go de temporaires et de caches traînent sur ton disque "
                             + "— récupérables gratuitement, sans toucher à tes fichiers.",
                        Why = "Espace récupérable — constaté : " + (recov / 1024.0).ToString("0.0") + " Go de temporaires et "
                            + "caches (mesure locale des dossiers) ; ils se régénèrent seuls, leur suppression est sans risque et gratuite.",
                        Fix = ChatActions.FixDisk()
                    });
                else ok.Add("espace disque suffisant");
            }
            catch { }

            // --- 4. Bibliothèques de jeu (cause n°1 d'un jeu qui ne démarre pas) ---
            try
            {
                int missing = LibScan.MissingEssentialCount();
                if (missing > 0)
                    found.Add(new Finding
                    {
                        Impact = 75,
                        Text = "" + missing + " bibliothèque(s) essentielle(s) manquante(s) (Visual C++, DirectX, .NET). "
                             + "C'est la cause classique d'un jeu qui refuse de démarrer. Installation gratuite "
                             + "(runtimes Microsoft officiels), pré-cochée dans le panneau Bibliothèques.",
                        Why = "Bibliothèques — constaté : " + missing + " runtime(s) essentiel(s) absent(s) de la base de "
                            + "désinstallation Windows. Un jeu compilé contre un runtime absent refuse de démarrer ou plante "
                            + "aussitôt. Ces runtimes sont distribués gratuitement par Microsoft."
                    });
                else ok.Add("bibliothèques de jeu complètes");
            }
            catch { }

            // --- 4bis. Réglages néfastes laissés par d'anciens « optimiseurs » (lecture locale) ---
            try
            {
                List<Checkup.Item> items = Checkup.Analyze();
                var bad = new List<Checkup.Item>();
                if (items != null) foreach (var it in items) if (it.Problem) bad.Add(it);
                if (bad.Count > 0)
                {
                    var names = new List<string>(); foreach (var it in bad) names.Add(it.Name);
                    found.Add(new Finding
                    {
                        Impact = 82,
                        Text = "" + bad.Count + " réglage(s) néfaste(s) laissé(s) par un ancien « optimiseur » ou un mauvais "
                             + "guide : " + string.Join(" · ", names.ToArray()) + ". Je peux remettre les valeurs saines de "
                             + "Windows — gratuit et réversible.",
                        Why = "Réglages néfastes — constaté (lecture locale registre/bcdedit/tâches) : "
                            + string.Join(" · ", names.ToArray()) + ". Ces réglages sont documentés comme sources de latence, "
                            + "de crashs ou de risque (données/sécurité) ; la réparation remet la valeur PAR DÉFAUT de Windows, gratuitement.",
                        Fix = ChatActions.FixCheckup(bad)
                    });
                }
                else ok.Add("aucun réglage néfaste d'ancien optimiseur");
            }
            catch { }

            // --- 4ter. Connexion : ping et stabilité (échos réels vers 1.1.1.1). Si AUCUNE
            //     réponse (hors-ligne / ICMP filtré), on ne conclut RIEN — jamais de faux problème. ---
            try
            {
                double avg, jit; int loss;
                if (ChatActions.PingSample(4, 600, out avg, out jit, out loss))
                {
                    if (loss > 0 || jit >= 15 || avg >= 80)
                        found.Add(new Finding
                        {
                            Impact = 70,
                            Text = "Ta connexion n'est pas nette : ping moyen " + avg.ToString("0") + " ms, gigue "
                                 + jit.ToString("0.#") + " ms" + (loss > 0 ? ", " + loss + " % de paquets perdus" : "") + ". "
                                 + "En jeu, ça fait des à-coups et des tirs non comptés. Gratuit : câble Ethernet plutôt que "
                                 + "Wi-Fi quand c'est possible, et le panneau Qualité réseau départage ta box d'internet.",
                            Why = "Connexion — constaté : " + avg.ToString("0") + " ms de ping moyen, " + jit.ToString("0.#")
                                + " ms de gigue, " + loss + " % de perte sur 4 échos réels vers 1.1.1.1 ; seuils jeu : ~80 ms "
                                + "de ping, 15 ms de gigue, 0 % de perte. La perte « téléporte », la gigue rend le jeu irrégulier."
                        });
                    else ok.Add("connexion stable (" + avg.ToString("0") + " ms)");
                }
            }
            catch { }

            // --- 4quater. Un programme gourmand en fond (les jeux en cours sont exclus du suspect) ---
            try
            {
                ChatActions.Hog hog = ChatActions.TopHog(900);
                if (hog != null && hog.CpuPct >= 25)
                    found.Add(new Finding
                    {
                        Impact = 60,
                        Text = "« " + hog.Name + " »" + (hog.Count > 1 ? " (×" + hog.Count + ")" : "") + " consomme "
                             + hog.CpuPct.ToString("0") + " % de ton processeur en ce moment"
                             + (hog.RamMb >= 500 ? " et " + hog.RamMb + " Mo de mémoire" : "") + ". "
                             + "Gratuit : le panneau « Qui ralentit mon PC » permet de le fermer proprement.",
                        Why = "Processus — constaté : « " + hog.Name + " » à " + hog.CpuPct.ToString("0") + " % CPU pendant "
                            + "~1 s de mesure des temps processeur (cœur de Windows et jeux en cours exclus). Un programme "
                            + "de fond qui pèse autant vole des images par seconde à ton jeu ; le fermer est gratuit."
                    });
                else ok.Add("aucun programme gourmand en fond");
            }
            catch { }

            // --- 5. Optimisations recommandées non appliquées ---
            try
            {
                if (st != null && st.OptiTotal > 0)
                {
                    int inactive = st.OptiTotal - st.OptiActive;
                    if (st.Health < 70 && inactive > 20)
                        found.Add(new Finding
                        {
                            Impact = 55,
                            Text = "" + inactive + " optimisations sont encore inactives et ton score de santé est à "
                                 + st.Health + " %. Le preset « Recommandé » couvre l'essentiel sans rien risquer — "
                                 + "gratuit et réversible, comme tout ici.",
                            Why = "Optimisations — constaté : santé " + st.Health + " % et " + inactive + " réglages "
                                + "recommandés inactifs. Chacun est documenté, réversible et gratuit ; le preset "
                                + "« Recommandé » n'active que les sûrs."
                        });
                    else if (st.Health >= 70) ok.Add("optimisations bien engagées (" + st.OptiActive + " actives)");
                }
            }
            catch { }

            found.Sort(delegate (Finding a, Finding b) { return b.Impact.CompareTo(a.Impact); });

            // --- Raisonnement complet, servi si l'utilisateur demande « pourquoi ? » ---
            var why = new StringBuilder("Mon raisonnement, mesure par mesure :\n\n");
            foreach (var f in found)
                if (!string.IsNullOrEmpty(f.Why)) why.Append("• ").Append(f.Why).Append("\n\n");
            if (ok.Count > 0)
                why.Append("Mesuré aussi, sans rien trouver d'anormal : ").Append(string.Join(" · ", ok.ToArray())).Append(".\n\n");
            why.Append("Et la règle de la maison : toutes les corrections que je propose sont gratuites.");
            string explain = why.ToString().TrimEnd();

            // --- Rédaction du compte-rendu ---
            var sb = new StringBuilder();
            if (found.Count == 0)
            {
                sb.Append("J'ai passé ton PC au crible");
                if (!string.IsNullOrEmpty(focus)) sb.Append(" en cherchant ce qui pourrait expliquer « ").Append(focus).Append(" »");
                sb.Append(" : je n'ai rien trouvé d'anormal.\n\n");
                if (ok.Count > 0) sb.Append("Vérifié et sain : ").Append(string.Join(" · ", ok.ToArray())).Append(".\n\n");
                sb.Append("Si le souci persiste, décris-moi précisément quand il arrive (dans un jeu en particulier ? "
                        + "au démarrage ? après un moment ?) et je creuse ailleurs.");
                return new DocAssistant.Reply { Text = sb.ToString(), Explain = explain };
            }

            sb.Append("J'ai enquêté sur ton PC");
            if (!string.IsNullOrEmpty(focus)) sb.Append(" (« ").Append(focus).Append(" »)");
            sb.Append(" — ").Append(found.Count).Append(found.Count > 1 ? " causes trouvées" : " cause trouvée")
              .Append(", de la plus lourde à la plus légère :\n\n");
            for (int i = 0; i < found.Count; i++)
                sb.Append(i + 1).Append(". ").Append(found[i].Text).Append("\n\n");
            if (ok.Count > 0)
                sb.Append("Vérifié et sain : ").Append(string.Join(" · ", ok.ToArray())).Append(".");
            sb.Append("\n\nDemande-moi « pourquoi ? » pour le raisonnement complet, mesure par mesure.");

            var plan = new List<DocAssistant.ChatAction>();
            foreach (var f in found) if (f.Fix != null) plan.Add(f.Fix);

            if (plan.Count > 0)
                sb.Append("\nCe que je peux corriger tout de suite — gratuit, et tu gardes la main :");

            return new DocAssistant.Reply
            {
                Text = sb.ToString().TrimEnd(),
                Plan = plan.Count > 0 ? plan : null,
                Explain = explain
            };
        }

        /// <summary>L'action « enquête » telle que le chat la déclenche (mesure : elle part seule).</summary>
        public static DocAssistant.ChatAction Action(string focus, BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Enquête complète"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log) { return Investigate(focus, st, log); };
            return a;
        }
    }
}
