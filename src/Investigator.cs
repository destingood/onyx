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
            public string Key;                       // identifiant STABLE pour la mémoire (null = piste volatile)
            public DocAssistant.ChatAction Fix;      // correction proposée (peut être null)
        }

        // --- Mémoire d'une enquête à l'autre --------------------------------------------------
        //  On ne mémorise que les pistes STABLES (un ping ou un processus gourmand fluctuent) :
        //  au bilan suivant, le Copilote dit ce qui a été réglé, ce qui reste, ce qui est nouveau.
        private static readonly string[] MemLabels =
        {
            "ecrans|écran sous sa fréquence", "crashgpu|crashs du pilote GPU", "disque|disque à nettoyer",
            "biblio|bibliothèques manquantes", "reglages|réglages néfastes", "opti|optimisations inactives",
            "uptime|PC sans vrai redémarrage", "ramspeed|RAM sous sa vitesse (XMP)",
            "gpupilote|pilote GPU âgé", "demarrage|démarrage chargé"
        };

        private static string MemPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-copilote.txt"); }
        }

        private static string MemLabel(string key)
        {
            foreach (string m in MemLabels) { int i = m.IndexOf('|'); if (m.Substring(0, i) == key) return m.Substring(i + 1); }
            return key;
        }

        // minuscule + accents plats : le focus vient tel que TAPÉ par l'utilisateur.
        private static string DocFocusNorm(string focus)
        {
            string f = (focus ?? "").ToLowerInvariant();
            return f.Replace('é', 'e').Replace('è', 'e').Replace('ê', 'e').Replace('à', 'a').Replace('â', 'a');
        }

        private static bool IsStable(string key)
        {
            foreach (string m in MemLabels) if (m.StartsWith(key + "|", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Clés stables de la dernière enquête, ou null si aucune enquête mémorisée.</summary>
        private static HashSet<string> LoadMem()
        {
            try
            {
                if (!File.Exists(MemPath)) return null;
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (string line in File.ReadAllLines(MemPath))
                {
                    string l = line.Trim();
                    if (l.Length == 0 || l.StartsWith("#", StringComparison.Ordinal)) continue;
                    if (IsStable(l)) set.Add(l);
                }
                return set;
            }
            catch { return null; }
        }

        private static void SaveMem(List<Finding> found)
        {
            try
            {
                var sb = new StringBuilder("# Dernière enquête du Copilote — pistes stables constatées\n");
                foreach (var f in found) if (f.Key != null && IsStable(f.Key)) sb.Append(f.Key).Append('\n');
                File.WriteAllText(MemPath, sb.ToString());
            }
            catch { }
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
                    var f = new Finding { Impact = 95, Key = "ecrans", Fix = ChatActions.FixScreen() };
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
                        Impact = 90, Key = "crashgpu",
                        Text = "Le pilote de ta carte graphique a signalé " + gpuErr + " erreur(s) ces 14 derniers jours. "
                             + "C'est la signature des crashs « dispositif de rendu perdu » : surchauffe, overclock instable "
                             + "ou pilote abîmé. Gratuit : dépoussiérage + panneau Températures pour surveiller, et si ça "
                             + "persiste, pilote réinstallé PROPREMENT avec DDU (gratuit, 1 clic depuis Bibliothèques).",
                        Why = "Crashs GPU — constaté : " + gpuErr + " événement(s) du pilote graphique dans le journal "
                            + "système Windows sur 14 jours ; attendu sur un PC stable : 0. Chaque événement = le pilote "
                            + "s'est réinitialisé (freeze/écran noir possible en jeu). Les remèdes proposés sont tous gratuits.",
                        Fix = ChatActions.InstallTool("Wagnardsoft.DisplayDriverUninstaller", "DDU")
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
                        Impact = 80, Key = "disque",
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
                        Impact = 45, Key = "disque",
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
                        Impact = 75, Key = "biblio",
                        Text = "" + missing + " bibliothèque(s) essentielle(s) manquante(s) (Visual C++, DirectX, .NET). "
                             + "C'est la cause classique d'un jeu qui refuse de démarrer. Installation gratuite "
                             + "(runtimes Microsoft officiels), pré-cochée dans le panneau Bibliothèques.",
                        Why = "Bibliothèques — constaté : " + missing + " runtime(s) essentiel(s) absent(s) de la base de "
                            + "désinstallation Windows. Un jeu compilé contre un runtime absent refuse de démarrer ou plante "
                            + "aussitôt. Ces runtimes sont distribués gratuitement par Microsoft.",
                        Fix = ChatActions.FixLibs()
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
                        Impact = 82, Key = "reglages",
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
                    bool wifi = false; try { wifi = ChatActions.OnWifi(); } catch { }
                    if (loss > 0 || jit >= 15 || avg >= 80)
                        found.Add(new Finding
                        {
                            Impact = 70,
                            Text = "Ta connexion n'est pas nette : ping moyen " + avg.ToString("0") + " ms, gigue "
                                 + jit.ToString("0.#") + " ms" + (loss > 0 ? ", " + loss + " % de paquets perdus" : "") + ". "
                                 + "En jeu, ça fait des à-coups et des tirs non comptés. "
                                 + (wifi ? "Et tu es en Wi-Fi : un câble Ethernet, même temporaire, tranche la question gratuitement. "
                                         : "Gratuit : le panneau Qualité réseau départage ta box d'internet. "),
                            Why = "Connexion — constaté : " + avg.ToString("0") + " ms de ping moyen, " + jit.ToString("0.#")
                                + " ms de gigue, " + loss + " % de perte sur 4 échos réels vers 1.1.1.1"
                                + (wifi ? ", en Wi-Fi" : "") + " ; seuils jeu : ~80 ms "
                                + "de ping, 15 ms de gigue, 0 % de perte. La perte « téléporte », la gigue rend le jeu irrégulier."
                        });
                    else ok.Add("connexion stable (" + avg.ToString("0") + " ms" + (wifi ? ", Wi-Fi" : "") + ")");
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
                            + "de fond qui pèse autant vole des images par seconde à ton jeu ; le fermer est gratuit.",
                        Fix = ChatActions.FixHog(hog.Name)
                    });
                else ok.Add("aucun programme gourmand en fond");
            }
            catch { }

            // --- Redémarrage en retard (le « démarrage rapide » endort au lieu d'éteindre) ---
            try
            {
                double up = ChatActions.UptimeDays();
                if (up >= 7)
                    found.Add(new Finding
                    {
                        Impact = 58, Key = "uptime",
                        Text = "Ton PC n'a pas VRAIMENT redémarré depuis " + up.ToString("0") + " jours (le « démarrage "
                             + "rapide » de Windows endort au lieu d'éteindre). Un vrai redémarrage purge pilotes et fuites "
                             + "mémoire — gratuit, 2 minutes, souvent spectaculaire.",
                        Why = "Redémarrage — constaté : " + up.ToString("0") + " jours d'uptime système ; seuil : 7 jours. "
                            + "Pilotes et services accumulent états bancals et fuites ; un redémarrage complet remet tout à plat, gratuitement.",
                        Fix = ChatActions.RestartAction()
                    });
                else if (up >= 0) ok.Add("redémarré récemment");
            }
            catch { }

            // --- RAM sous sa vitesse vendue (profil XMP/EXPO non activé dans le BIOS) ---
            try
            {
                var ram = Sys.QueryRam();
                if (ram.SpeedRated > 0 && ram.SpeedRunning > 0 && ram.SpeedRated - ram.SpeedRunning >= 400)
                    found.Add(new Finding
                    {
                        Impact = 72, Key = "ramspeed",
                        Text = "Ta RAM tourne à " + ram.SpeedRunning + " MT/s alors qu'elle est vendue pour "
                             + ram.SpeedRated + " MT/s : le profil XMP/EXPO n'est pas activé dans le BIOS. Ce sont des FPS "
                             + "gratuits que tu as déjà payés — 2 minutes dans le BIOS (le guide BIOS de l'app, menu ⋯, t'accompagne).",
                        Why = "RAM — constaté (WMI) : " + ram.SpeedRunning + " MT/s configurés vs " + ram.SpeedRated
                            + " MT/s annoncés par les barrettes. Sans XMP/EXPO, la carte mère applique une vitesse de "
                            + "sécurité très inférieure ; l'activer est gratuit et réversible dans le BIOS."
                    });
                else if (ram.SpeedRunning > 0) ok.Add("RAM à sa vitesse (" + ram.SpeedRunning + " MT/s)");
            }
            catch { }

            // --- Pilote GPU très âgé (correctifs et FPS manqués) ---
            try
            {
                int age = Diagnostics.GpuDriverAgeDays();
                if (age > 540)
                    found.Add(new Finding
                    {
                        Impact = 62, Key = "gpupilote",
                        Text = "Ton pilote graphique date d'environ " + (age / 30) + " mois. Les pilotes récents corrigent "
                             + "des crashs et gagnent des FPS — mise à jour GRATUITE (NVIDIA/AMD/Intel), et DDU (gratuit, "
                             + "1 clic depuis Bibliothèques) si tu veux repartir propre.",
                        Why = "Pilote GPU — constaté (WMI DriverDate) : " + age + " jours ; seuil : ~18 mois. Les jeux "
                            + "récents sont optimisés contre les pilotes récents ; la mise à jour est gratuite chez le constructeur."
                    });
                else if (age >= 0) ok.Add("pilote GPU récent");
            }
            catch { }

            // --- Démarrage chargé (programmes lancés à chaque allumage) ---
            try
            {
                var stl = Sys.ListStartup(); int en = 0;
                foreach (var e in stl) if (e.Enabled) en++;
                if (en >= 8)
                    found.Add(new Finding
                    {
                        Impact = 48, Key = "demarrage",
                        Text = "" + en + " programmes se lancent à CHAQUE allumage : le PC démarre plus lentement et garde "
                             + "des poids en fond. Le panneau « Programmes au démarrage » permet d'en couper — gratuit, "
                             + "réversible, sans rien désinstaller.",
                        Why = "Démarrage — constaté : " + en + " entrées actives (clés Run du registre) ; seuil : 8. "
                            + "Chaque entrée ralentit l'allumage et reste souvent résidente ensuite.",
                        Fix = ChatActions.FixStartup()
                    });
                else ok.Add("démarrage léger (" + en + " au boot)");
            }
            catch { }

            // --- DNS lent — UNIQUEMENT si la plainte est réseau : l'enquête s'adapte au symptôme ---
            try
            {
                string fl = DocFocusNorm(focus);
                if (fl.Contains("ping") || fl.Contains("ligne") || fl.Contains("internet") || fl.Contains("reseau")
                    || fl.Contains("dns") || fl.Contains("lag") || fl.Contains("telecharg"))
                {
                    string cur; double curMs, bestMs; string bestName;
                    if (ChatActions.DnsCompare(out cur, out curMs, out bestMs, out bestName)
                        && curMs >= 0 && bestMs >= 0 && curMs > bestMs * 2 && curMs - bestMs >= 15)
                        found.Add(new Finding
                        {
                            Impact = 40,
                            Text = "Ton DNS" + (cur != null ? " (" + cur + ")" : "") + " répond en " + curMs.ToString("0")
                                 + " ms là où " + bestName + " fait " + bestMs.ToString("0") + " ms. Chaque connexion à un "
                                 + "serveur attend cette traduction. Le panneau DNS rapide bascule gratuitement (réversible).",
                            Why = "DNS — constaté : " + curMs.ToString("0") + " ms (actuel) vs " + bestMs.ToString("0")
                                + " ms (" + bestName + ", gratuit) sur les mêmes requêtes. Mesuré uniquement parce que ta "
                                + "plainte touche au réseau — l'enquête adapte ses mesures au symptôme.",
                            Fix = ChatActions.FixDns(bestName,
                                bestName != null && bestName.StartsWith("Google") ? new[] { "8.8.8.8", "8.8.4.4" }
                                                                                  : new[] { "1.1.1.1", "1.0.0.1" })
                        });
                    else if (curMs >= 0) ok.Add("DNS réactif (" + curMs.ToString("0") + " ms)");
                }
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
                            Impact = 55, Key = "opti",
                            Text = "" + inactive + " optimisations sont encore inactives et ton score de santé est à "
                                 + st.Health + " %. Le preset « Recommandé » couvre l'essentiel sans rien risquer — "
                                 + "gratuit et réversible, comme tout ici.",
                            Why = "Optimisations — constaté : santé " + st.Health + " % et " + inactive + " réglages "
                                + "recommandés inactifs. Chacun est documenté, réversible et gratuit ; le preset "
                                + "« Recommandé » n'active que les sûrs.",
                            Fix = ChatActions.FixOpti()
                        });
                    else if (st.Health >= 70) ok.Add("optimisations bien engagées (" + st.OptiActive + " actives)");
                }
            }
            catch { }

            found.Sort(delegate (Finding a, Finding b) { return b.Impact.CompareTo(a.Impact); });

            // --- Mémoire : ce qui a changé depuis la DERNIÈRE enquête (pistes stables) ---
            HashSet<string> prev = LoadMem();
            string evol = null;
            if (prev != null)
            {
                var cur = new HashSet<string>(StringComparer.Ordinal);
                foreach (var f in found) if (f.Key != null && IsStable(f.Key)) cur.Add(f.Key);
                var done = new List<string>(); var still = new List<string>(); var fresh = new List<string>();
                foreach (var k in prev) { if (cur.Contains(k)) still.Add(MemLabel(k)); else done.Add(MemLabel(k)); }
                foreach (var k in cur) if (!prev.Contains(k)) fresh.Add(MemLabel(k));
                var parts = new List<string>();
                if (done.Count > 0) parts.Add("réglé ✔ : " + string.Join(", ", done.ToArray()));
                if (still.Count > 0) parts.Add("toujours là : " + string.Join(", ", still.ToArray()));
                if (fresh.Count > 0) parts.Add("nouveau : " + string.Join(", ", fresh.ToArray()));
                if (parts.Count > 0) evol = "Depuis la dernière enquête — " + string.Join(" · ", parts.ToArray()) + ".";
            }
            SaveMem(found);

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
                if (evol != null) sb.Append(evol).Append("\n\n");
                sb.Append("J'ai passé ton PC au crible");
                if (!string.IsNullOrEmpty(focus)) sb.Append(" en cherchant ce qui pourrait expliquer « ").Append(focus).Append(" »");
                sb.Append(" : je n'ai rien trouvé d'anormal.\n\n");
                if (ok.Count > 0) sb.Append("Vérifié et sain : ").Append(string.Join(" · ", ok.ToArray())).Append(".\n\n");
                sb.Append("Si le souci persiste, décris-moi précisément quand il arrive (dans un jeu en particulier ? "
                        + "au démarrage ? après un moment ?) et je creuse ailleurs.");
                return new DocAssistant.Reply { Text = sb.ToString(), Explain = explain };
            }

            if (evol != null) sb.Append(evol).Append("\n\n");
            sb.Append("J'ai enquêté sur ton PC");
            if (!string.IsNullOrEmpty(focus)) sb.Append(" (« ").Append(focus).Append(" »)");
            sb.Append(" — ").Append(found.Count).Append(found.Count > 1 ? " causes trouvées" : " cause trouvée")
              .Append(", de la plus lourde à la plus légère. Chaque carte porte sa correction gratuite quand j'en ai une :");

            // Les causes deviennent des CARTES d'impact (le texte reste sobre, la structure parle).
            var cards = new List<DocAssistant.Card>();
            foreach (var f in found) cards.Add(new DocAssistant.Card { Impact = f.Impact, Text = f.Text, Fix = f.Fix });

            var foot = new StringBuilder();
            if (ok.Count > 0) foot.Append("Vérifié et sain : ").Append(string.Join(" · ", ok.ToArray())).Append(".\n");
            foot.Append("Demande-moi « pourquoi ? » pour le raisonnement complet, mesure par mesure.");

            var plan = new List<DocAssistant.ChatAction>();
            foreach (var f in found) if (f.Fix != null) plan.Add(f.Fix);

            // « 🚀 TOUT réparer » : un SEUL clic explicite pour dérouler toutes les corrections
            // enchaînables (le redémarrage, NoChain, garde son propre bouton).
            var chain = new List<DocAssistant.ChatAction>();
            foreach (var p in plan) if (p != null && !p.NoChain) chain.Add(p);
            DocAssistant.ChatAction all = ChatActions.AllFix(chain);

            return new DocAssistant.Reply
            {
                Text = sb.ToString().TrimEnd(),
                Cards = cards,
                Footer = foot.ToString().TrimEnd(),
                Plan = plan.Count > 0 ? plan : null,   // sert au « oui » (re-présentation) — les cartes portent les boutons
                Action = all,                          // le pilote automatique, sous les cartes
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
