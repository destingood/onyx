using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Ce que le Copilote sait FAIRE, et pas seulement dire. Deux familles :
    ///   • MESURES  (IsChange = false, AutoRun = true)  : lecture seule, lancées toutes seules.
    ///   • CHANGEMENTS (IsChange = true)                : jamais sans un clic explicite, avec
    ///     l'annonce de ce qui va changer — c'est la promesse fondatrice de Fluide.
    /// Chaque action renvoie une <see cref="DocAssistant.Reply"/> : elle peut donc enchaîner
    /// d'elle-même sur la correction qui découle de la mesure. Tout tourne en tâche de fond.
    /// </summary>
    internal static class ChatActions
    {
        private static DocAssistant.Reply Say(string text) { return new DocAssistant.Reply { Text = text }; }

        // ------------------------------------------------------------------
        //  Écran — le piège classique du 144 Hz resté à 60
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MeasureScreen()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Lecture de tes écrans"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                List<DisplayInfo.DisplayMode> list = DisplayInfo.Query();
                if (list == null || list.Count == 0) return Say("Je n'arrive pas à lire tes écrans sur ce PC.");
                var sb = new StringBuilder();
                int below = 0;
                foreach (var d in list)
                {
                    sb.Append("• ").Append(d.Name).Append(" : ").Append(d.CurrentHz).Append(" Hz");
                    if (d.BelowMax) { sb.Append("  ⚠ il peut monter à ").Append(d.MaxHz).Append(" Hz"); below++; }
                    else sb.Append("  ✅ c'est déjà son maximum");
                    sb.Append('\n');
                }
                sb.Append(below > 0
                    ? "\nTu perds de la fluidité sans le savoir — je peux corriger ça tout de suite."
                    : "\nRien à corriger de ce côté.");
                var r = Say(sb.ToString().TrimEnd());
                r.Action = FixScreen();          // null s'il n'y a rien à corriger
                return r;
            };
            return a;
        }

        /// <summary>Action de correction — renvoie null si aucun écran n'est réellement bridé.</summary>
        public static DocAssistant.ChatAction FixScreen()
        {
            List<DisplayInfo.DisplayMode> list;
            try { list = DisplayInfo.Query(); } catch { return null; }
            if (list == null) return null;
            var todo = new List<DisplayInfo.DisplayMode>();
            foreach (var d in list) if (d.BelowMax) todo.Add(d);
            if (todo.Count == 0) return null;

            var a = new DocAssistant.ChatAction();
            a.Label = todo.Count == 1
                ? "Passer l'écran à " + todo[0].MaxHz + " Hz"
                : "Passer les " + todo.Count + " écrans à leur maximum";
            a.IsChange = true;
            a.Warning = "Change la fréquence de rafraîchissement. Réversible, et Windows revient seul en arrière si l'écran ne suit pas.";
            a.Run = delegate (Action<string, int> log)
            {
                int ok = 0;
                var sb = new StringBuilder();
                foreach (var d in todo)
                {
                    bool done = false;
                    try { done = DisplayInfo.SetHz(d.Device, d.MaxHz); } catch { }
                    if (done) { ok++; sb.Append("✅ ").Append(d.Name).Append(" : ").Append(d.CurrentHz).Append(" → ").Append(d.MaxHz).Append(" Hz\n"); }
                    else sb.Append("⚠ ").Append(d.Name).Append(" : refusé par le pilote\n");
                }
                sb.Append(ok > 0 ? "\nRelance ton jeu pour en profiter." : "\nAucun changement appliqué.");
                return Say(sb.ToString().TrimEnd());
            };
            return a;
        }

        // ------------------------------------------------------------------
        //  Capteurs — charge et températures en direct
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MeasureSensors()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Mesure en direct"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log) { return Say(SensorsText()); };
            return a;
        }

        public static string SensorsText()
        {
            var sb = new StringBuilder();
            try
            {
                using (var mon = new HwMonitor())
                {
                    HwSample s = mon.Sample();
                    sb.Append("• Processeur : ").Append(s.CpuLoad < 0 ? "n/d" : s.CpuLoad.ToString("0") + " % de charge");
                    if (!double.IsNaN(s.CpuTempC)) sb.Append(" · ").Append(s.CpuTempC.ToString("0")).Append(" °C");
                    sb.Append('\n');
                    sb.Append("• Mémoire : ").Append(s.RamLoad.ToString("0")).Append(" % utilisée\n");
                    if (s.Gpu != null && s.Gpu.Ok)
                    {
                        sb.Append("• Carte graphique : ").Append(s.Gpu.Util.ToString("0")).Append(" % de charge");
                        if (s.Gpu.TempC > 0) sb.Append(" · ").Append(s.Gpu.TempC.ToString("0")).Append(" °C");
                        sb.Append('\n');
                        if (s.Gpu.TempC >= 85) sb.Append("\n⚠ Ton GPU est très chaud : c'est la cause n°1 des chutes de FPS soudaines.");
                        else if (s.Gpu.TempC > 0) sb.Append("\nTempératures sous contrôle.");
                    }
                    else sb.Append("• Carte graphique : capteurs non lisibles ici\n");
                }
            }
            catch { return "Je n'ai pas réussi à lire les capteurs à l'instant."; }
            return sb.ToString().TrimEnd();
        }

        // ------------------------------------------------------------------
        //  Disque — espace libre et espace récupérable
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MeasureDisk()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Analyse du disque"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                var r = Say(DiskText());
                r.Action = FixDisk();
                return r;
            };
            return a;
        }

        public static string DiskText()
        {
            var sb = new StringBuilder();
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                var di = new DriveInfo(root);
                double freeGb = di.AvailableFreeSpace / 1073741824.0;
                double totGb = di.TotalSize / 1073741824.0;
                int pct = totGb > 0 ? (int)Math.Round(freeGb / totGb * 100) : 0;
                sb.Append("• Disque système ").Append(root.TrimEnd('\\')).Append(" : ")
                  .Append(freeGb.ToString("0")).Append(" Go libres sur ").Append(totGb.ToString("0"))
                  .Append(" Go (").Append(pct).Append(" %)\n");
                if (pct < 10) sb.Append("⚠ Sous 10 % de libre, Windows ralentit franchement.\n");
            }
            catch { }
            try
            {
                sb.Append("• Récupérable sans risque : ~").Append(Human(RecoverableMb()))
                  .Append(" (temporaires, caches — ça se régénère)");
            }
            catch { }
            string txt = sb.ToString().TrimEnd();
            return txt.Length == 0 ? "Je n'ai pas pu analyser le disque." : txt;
        }

        public static long RecoverableMb()
        {
            long mb = 0;
            try { foreach (var t in Sys.CleanTargets()) mb += t.SizeMB; } catch { }
            return mb;
        }

        private static string Human(long mb)
        {
            return mb >= 1024 ? (mb / 1024.0).ToString("0.0") + " Go" : mb + " Mo";
        }

        public static DocAssistant.ChatAction FixDisk()
        {
            long mb = RecoverableMb();
            if (mb < 200) return null;             // rien de significatif à récupérer
            var a = new DocAssistant.ChatAction();
            a.Label = "Libérer l'espace (~" + Human(mb) + ")";
            a.IsChange = true;
            a.Warning = "Supprime des fichiers temporaires et des caches qui se régénèrent. Tes fichiers personnels, jeux et sauvegardes ne sont pas touchés.";
            a.Run = delegate (Action<string, int> log)
            {
                long before = 0, after = 0;
                int n = 0;
                try
                {
                    List<Sys.CleanTarget> targets = Sys.CleanTargets();
                    foreach (var t in targets) before += t.SizeMB;
                    foreach (var t in targets) { try { n += Sys.CleanTargetNow(t, log); } catch { } }
                    foreach (var t in Sys.CleanTargets()) after += t.SizeMB;
                }
                catch { return Say("Le nettoyage n'a pas pu aller au bout (fichiers verrouillés par Windows)."); }
                long freed = Math.Max(0, before - after);
                return Say("✅ Nettoyage terminé — " + Human(freed) + " libérés (" + n + " élément(s)).");
            };
            return a;
        }

        // ------------------------------------------------------------------
        //  Bibliothèques de jeu manquantes
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MeasureLibs()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Vérification des bibliothèques"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log) { return Say(LibsText()); };
            return a;
        }

        public static string LibsText()
        {
            int missing;
            try { missing = LibScan.MissingEssentialCount(); }
            catch { return "Je n'ai pas pu vérifier les bibliothèques."; }
            if (missing <= 0)
                return "✅ Toutes les bibliothèques essentielles sont là (Visual C++, DirectX, .NET).\nSi un jeu refuse quand même de démarrer, le problème est ailleurs — dis-le-moi.";
            return "⚠ " + missing + " bibliothèque(s) essentielle(s) manquante(s) (Visual C++, DirectX, .NET…).\n"
                 + "C'est LA cause classique d'un jeu qui ne se lance pas ou qui plante au démarrage.\n"
                 + "Le panneau Bibliothèques les installe en un clic, elles sont déjà pré-cochées.";
        }

        // ------------------------------------------------------------------
        //  Réseau — ping réel, gigue et perte (triage rapide dans le chat)
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MeasurePing()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Test de ta connexion"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                double avg, jit; int loss;
                if (!PingSample(6, 900, out avg, out jit, out loss))
                    return Say("Impossible de joindre internet à l'instant (hors-ligne, ou échos ICMP bloqués).\n"
                             + "Si tu es bien connecté, le panneau « Qualité réseau » fera le test complet box vs internet.");
                bool wifi = false; try { wifi = OnWifi(); } catch { }
                var sb = new StringBuilder();
                sb.Append("• Ping moyen : ").Append(avg.ToString("0")).Append(" ms\n");
                sb.Append("• Gigue (variation) : ").Append(jit.ToString("0.#")).Append(" ms\n");
                sb.Append("• Perte de paquets : ").Append(loss).Append(" %\n");
                sb.Append("• Connexion : ").Append(wifi ? "Wi-Fi (un câble ferait mieux pour jouer)" : "filaire ✅").Append("\n\n");
                if (loss > 0) sb.Append("⚠ De la perte de paquets — c'est elle qui « téléporte » les joueurs et annule des tirs.");
                else if (jit >= 15) sb.Append("⚠ Gigue élevée : le ping bouge trop d'une seconde à l'autre, sensation de jeu irrégulière.");
                else if (avg >= 80) sb.Append("⚠ Ping élevé sur ce test : joue sur des serveurs proches, et vérifie le Wi-Fi vs câble.");
                else sb.Append("✅ Connexion saine sur ce test rapide. Si ça lag quand même, c'est ponctuel ou côté serveur.");
                sb.Append("\nPour départager ta box d'internet, le test complet est dans « Qualité réseau ».");
                return Say(sb.ToString().TrimEnd());
            };
            return a;
        }

        /// <summary>Échos ICMP vers 1.1.1.1 : moyenne, gigue (variation moyenne entre échos
        /// successifs) et perte. Renvoie faux si AUCUNE réponse (hors-ligne ou ICMP filtré) —
        /// dans ce cas on ne conclut RIEN plutôt que d'inventer un problème réseau.</summary>
        public static bool PingSample(int count, int timeoutMs, out double avg, out double jitter, out int lossPct)
        {
            avg = 0; jitter = 0; lossPct = 100;
            var times = new List<long>();
            int sent = 0;
            try
            {
                using (var p = new System.Net.NetworkInformation.Ping())
                    for (int i = 0; i < count; i++)
                    {
                        sent++;
                        try
                        {
                            var r = p.Send("1.1.1.1", timeoutMs);
                            if (r != null && r.Status == System.Net.NetworkInformation.IPStatus.Success)
                                times.Add(r.RoundtripTime);
                        }
                        catch { }
                    }
            }
            catch { return false; }
            if (times.Count == 0) return false;
            long sum = 0; foreach (long t in times) sum += t;
            avg = (double)sum / times.Count;
            double dsum = 0;
            for (int i = 1; i < times.Count; i++) dsum += Math.Abs(times[i] - times[i - 1]);
            jitter = times.Count > 1 ? dsum / (times.Count - 1) : 0;
            lossPct = (sent - times.Count) * 100 / Math.Max(1, sent);
            return true;
        }

        // ------------------------------------------------------------------
        //  Processus gourmands — qui consomme le CPU, là, tout de suite
        // ------------------------------------------------------------------
        public sealed class Hog { public string Name; public double CpuPct; public long RamMb; public int Count; }

        public static DocAssistant.ChatAction MeasureHogs()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Mesure des processus (1 s)"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                List<Hog> top = TopHogs(1100, 3);
                if (top == null || top.Count == 0)
                    return Say("Rien ne consomme de CPU notable en ce moment (hors Windows). Si ça rame quand même, dis « fais un bilan » et je passe tout au crible.");
                var sb = new StringBuilder("Voilà ce qui travaille en ce moment (hors Windows et hors jeu) :\n");
                foreach (var h in top)
                {
                    sb.Append("• ").Append(h.Name);
                    if (h.Count > 1) sb.Append(" (×").Append(h.Count).Append(')');
                    sb.Append(" : ").Append(h.CpuPct.ToString("0")).Append(" % CPU · ").Append(h.RamMb).Append(" Mo\n");
                }
                sb.Append(top[0].CpuPct >= 25
                    ? "\n⚠ « " + top[0].Name + " » pèse lourd — le panneau « Qui ralentit mon PC » permet de le fermer proprement (jamais un processus système)."
                    : "\nRien d'alarmant : aucun ne pèse vraiment sur les performances.");
                return Say(sb.ToString().TrimEnd());
            };
            return a;
        }

        /// <summary>Le plus gros consommateur CPU du moment (hors système / hors jeu), ou null.</summary>
        public static Hog TopHog(int intervalMs)
        {
            List<Hog> l = TopHogs(intervalMs, 1);
            return l != null && l.Count > 0 ? l[0] : null;
        }

        // Processus qu'on ne liste JAMAIS comme « gourmands » : cœur de Windows (intouchable)
        // — les fermer est impossible ou dangereux, donc les montrer n'aiderait personne.
        private static readonly string[] CoreProc =
        {
            "idle", "system", "registry", "memory compression", "secure system", "vmmem",
            "csrss", "smss", "wininit", "winlogon", "services", "lsass", "svchost",
            "dwm", "fontdrvhost", "sihost", "audiodg", "wudfhost", "conhost"
        };

        /// <summary>Top des processus par CPU mesuré sur 'intervalMs', agrégés PAR NOM (les 20
        /// processus d'un navigateur comptent ensemble), RAM incluse. Jeux connus et cœur de
        /// Windows exclus : on cherche ce qui vole des ressources, pas ce que tu utilises.</summary>
        public static List<Hog> TopHogs(int intervalMs, int take)
        {
            var result = new List<Hog>();
            try
            {
                var games = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try { foreach (string exe in GameScan.PriorityExes()) games.Add(Path.GetFileNameWithoutExtension(exe)); } catch { }
                int self = System.Diagnostics.Process.GetCurrentProcess().Id;

                var t0 = new Dictionary<int, KeyValuePair<string, TimeSpan>>();
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    try { t0[p.Id] = new KeyValuePair<string, TimeSpan>(p.ProcessName, p.TotalProcessorTime); }
                    catch { }   // accès refusé (processus protégé) : ignoré
                    finally { try { p.Dispose(); } catch { } }
                }
                System.Threading.Thread.Sleep(Math.Max(300, intervalMs));

                var byName = new Dictionary<string, Hog>(StringComparer.OrdinalIgnoreCase);
                double denom = Math.Max(300, intervalMs) * Environment.ProcessorCount * 10000.0; // ticks CPU disponibles
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    try
                    {
                        KeyValuePair<string, TimeSpan> prev;
                        if (!t0.TryGetValue(p.Id, out prev) || p.Id == self) continue;
                        string name = p.ProcessName;
                        if (IsCore(name) || games.Contains(name)) continue;
                        double pct = (p.TotalProcessorTime - prev.Value).Ticks / denom * 100.0;
                        if (pct < 0) pct = 0;
                        Hog h;
                        if (!byName.TryGetValue(name, out h)) { h = new Hog { Name = name }; byName[name] = h; }
                        h.CpuPct += pct; h.RamMb += p.WorkingSet64 / 1048576; h.Count++;
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
                foreach (var h in byName.Values) if (h.CpuPct >= 2 || h.RamMb >= 300) result.Add(h);
                result.Sort(delegate (Hog a, Hog b) { return b.CpuPct.CompareTo(a.CpuPct); });
                if (result.Count > take) result.RemoveRange(take, result.Count - take);
            }
            catch { }
            return result;
        }

        private static bool IsCore(string name)
        {
            foreach (string c in CoreProc) if (string.Equals(name, c, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ------------------------------------------------------------------
        //  Faits système transverses — réutilisés par le chat ET l'enquête
        // ------------------------------------------------------------------
        /// <summary>Jours écoulés depuis le dernier VRAI démarrage (le « démarrage rapide »
        /// de Windows endort au lieu d'éteindre : l'uptime révèle la vérité).</summary>
        public static double UptimeDays()
        {
            try { return Environment.TickCount64 / 86400000.0; } catch { return -1; }
        }

        /// <summary>Vrai si la connexion ACTIVE passe par le Wi-Fi (aucune carte filaire avec
        /// passerelle) — l'info qui change les conseils réseau.</summary>
        public static bool OnWifi()
        {
            bool wifi = false, wired = false;
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    System.Net.NetworkInformation.IPInterfaceProperties p;
                    try { p = ni.GetIPProperties(); } catch { continue; }
                    if (p == null || p.GatewayAddresses == null || p.GatewayAddresses.Count == 0) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211) wifi = true;
                    else wired = true;
                }
            }
            catch { }
            return wifi && !wired;
        }

        /// <summary>Premier serveur DNS IPv4 de la connexion active, ou null.</summary>
        public static string CurrentDns()
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    System.Net.NetworkInformation.IPInterfaceProperties p;
                    try { p = ni.GetIPProperties(); } catch { continue; }
                    if (p == null || p.GatewayAddresses == null || p.GatewayAddresses.Count == 0) continue;
                    foreach (var d in p.DnsAddresses)
                        if (d.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return d.ToString();
                }
            }
            catch { }
            return null;
        }

        /// <summary>Compare le DNS ACTUEL aux références gratuites (Cloudflare/Google).
        /// Faux si rien n'est mesurable (hors-ligne) — on ne conclut rien.</summary>
        public static bool DnsCompare(out string cur, out double curMs, out double bestMs, out string bestName)
        {
            cur = CurrentDns(); curMs = -1; bestMs = -1; bestName = null;
            try
            {
                if (cur != null) curMs = DnsBench.QueryMs(cur, "www.google.com", 900, 3);
                double cf = DnsBench.QueryMs("1.1.1.1", "www.google.com", 900, 3);
                double gg = DnsBench.QueryMs("8.8.8.8", "www.google.com", 900, 3);
                if (cf >= 0 && (gg < 0 || cf <= gg)) { bestMs = cf; bestName = "Cloudflare (1.1.1.1)"; }
                else if (gg >= 0) { bestMs = gg; bestName = "Google (8.8.8.8)"; }
            }
            catch { }
            return curMs >= 0 || bestMs >= 0;
        }

        // ------------------------------------------------------------------
        //  DNS — le serveur qui traduit les noms en adresses (souvent négligé)
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MeasureDns()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Test de ton DNS"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                string cur; double curMs, bestMs; string bestName;
                if (!DnsCompare(out cur, out curMs, out bestMs, out bestName))
                    return Say("Impossible de tester le DNS à l'instant (hors-ligne ?). Réessaie une fois connecté.");
                var sb = new StringBuilder();
                sb.Append("• Ton DNS actuel").Append(cur != null ? " (" + cur + ")" : "").Append(" : ")
                  .Append(curMs >= 0 ? curMs.ToString("0") + " ms" : "ne répond pas").Append('\n');
                if (bestMs >= 0) sb.Append("• ").Append(bestName).Append(" : ").Append(bestMs.ToString("0")).Append(" ms (gratuit)\n\n");
                if (curMs < 0 && bestMs >= 0)
                    sb.Append("⚠ Ton DNS ne répond pas alors qu'internet marche : change-le, c'est gratuit et immédiat.");
                else if (curMs >= 0 && bestMs >= 0 && curMs > bestMs * 2 && curMs - bestMs >= 15)
                    sb.Append("⚠ Ton DNS traîne : chaque site, chaque boutique en jeu attend cette traduction. "
                            + "Le panneau DNS rapide bascule vers le plus rapide — gratuit et réversible.");
                else
                    sb.Append("✅ Ton DNS répond bien — rien à gagner de ce côté.");
                return Say(sb.ToString().TrimEnd());
            };
            return a;
        }

        // ------------------------------------------------------------------
        //  Démarrage — ce qui se lance à chaque allumage
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MeasureStartup()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Inventaire du démarrage"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                List<Sys.StartupEntry> list;
                try { list = Sys.ListStartup(); }
                catch { return Say("Je n'ai pas pu lire la liste de démarrage."); }
                var on = new List<Sys.StartupEntry>();
                foreach (var e in list) if (e.Enabled) on.Add(e);
                var sb = new StringBuilder();
                sb.Append(on.Count).Append(" programme(s) se lancent à CHAQUE allumage :\n");
                for (int i = 0; i < on.Count && i < 6; i++)
                    sb.Append("• ").Append(on[i].Name).Append(on[i].Machine ? "  (tous les utilisateurs)" : "").Append('\n');
                if (on.Count > 6) sb.Append("• … et ").Append(on.Count - 6).Append(" autres\n");
                sb.Append('\n');
                if (on.Count >= 8) sb.Append("⚠ C'est beaucoup : chacun ralentit l'allumage ET reste souvent en fond ensuite. "
                                           + "Le panneau « Programmes au démarrage » permet d'en couper — gratuit, réversible, sans rien désinstaller.");
                else if (on.Count <= 3) sb.Append("✅ Démarrage léger — rien à couper d'urgence.");
                else sb.Append("Raisonnable. Tu peux quand même couper ceux que tu n'utilises pas tous les jours (réversible).");
                return Say(sb.ToString().TrimEnd());
            };
            return a;
        }

        // ------------------------------------------------------------------
        //  Crashs — le relevé des 14 derniers jours (GPU + applications)
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MeasureCrashes()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Relevé des crashs (14 j)"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                int gpu = -1; List<CrashEvent> evs = null;
                try { gpu = CrashScan.GpuDriverErrors(14); } catch { }
                try { evs = CrashScan.Recent(14); } catch { }
                var sb = new StringBuilder("Relevé des 14 derniers jours (journaux Windows) :\n");
                sb.Append("• Pilote graphique : ").Append(gpu < 0 ? "illisible" : gpu + " erreur(s)").Append('\n');
                sb.Append("• Applications : ").Append(evs == null ? "illisible" : evs.Count + " crash(s)/blocage(s)").Append('\n');
                if (evs != null)
                    for (int i = 0; i < evs.Count && i < 3; i++)
                        sb.Append("   – ").Append(evs[i].Time.ToString("dd/MM HH:mm")).Append("  ").Append(evs[i].Kind)
                          .Append(" : ").Append(evs[i].Detail).Append(evs[i].IsGame ? "  🎮" : "").Append('\n');
                sb.Append('\n');
                bool gameCrash = false; if (evs != null) foreach (var e in evs) if (e.IsGame) gameCrash = true;
                if (gpu > 0)
                    sb.Append("⚠ Des erreurs du pilote GPU : c'est la piste n°1 (« dispositif de rendu perdu »). "
                            + "Gratuit : surveille la température (panneau Températures), et pilote réinstallé proprement avec DDU si ça persiste.");
                else if (gameCrash)
                    sb.Append("⚠ Des jeux crashent alors que le pilote GPU est propre : pense bibliothèques manquantes "
                            + "(je peux vérifier : dis « vérifie mes bibliothèques ») et stress-test gratuit (OCCT) pour tester la stabilité.");
                else if (evs != null && evs.Count == 0 && gpu == 0)
                    sb.Append("✅ Aucun crash relevé — ta machine est stable sur la période.");
                else
                    sb.Append("Le panneau Stabilité date chaque événement précisément et repère les motifs.");
                return Say(sb.ToString().TrimEnd());
            };
            return a;
        }

        // ------------------------------------------------------------------
        //  Latence — micro-mesure réelle (timer, gigue de Sleep, pics DPC)
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MeasureLatency()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Mesure de latence (~5 s)"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                BenchResult r;
                try { r = Bench.Run(4, log); }
                catch { return Say("La mesure de latence n'a pas pu se faire à l'instant."); }
                var sb = new StringBuilder();
                sb.Append("• Timer système : ").Append(r.TimerMs.ToString("0.0")).Append(" ms")
                  .Append(r.TimerMs <= 1.05 ? "  ✅ (1 ms = idéal jeu)" : "  ⚠ (1 ms attendu en jeu)").Append('\n');
                sb.Append("• Régularité (Sleep 1 ms réel) : moy ").Append(r.SleepAvgMs.ToString("0.00"))
                  .Append(" ms · pire ").Append(r.SleepMaxMs.ToString("0.00")).Append(" ms\n");
                if (r.DpcMax >= 0) sb.Append("• Pics DPC (pilotes) : ").Append(r.DpcMax.ToString("0.0")).Append(" % max\n");
                sb.Append('\n');
                if (r.DpcMax >= 8)
                    sb.Append("⚠ Un pilote monopolise le processeur par à-coups (DPC élevés) — c'est LA cause des "
                            + "micro-coupures de son et de souris. Le panneau « Latence en direct » identifie lequel, gratuitement.");
                else if (r.TimerMs > 1.5)
                    sb.Append("⚠ Le timer système est lent : l'optimisation « Timer 1 ms » de l'app corrige ça gratuitement.");
                else if (r.SleepMaxMs >= 6)
                    sb.Append("⚠ De grosses irrégularités ponctuelles : regarde ce qui tourne en fond (dis « qui ralentit mon pc »).");
                else
                    sb.Append("✅ Machine réactive et régulière — l'input lag ne vient pas de là.");
                return Say(sb.ToString().TrimEnd());
            };
            return a;
        }

        // ------------------------------------------------------------------
        //  Réglages néfastes d'anciens « optimiseurs » — réparation groupée
        // ------------------------------------------------------------------
        /// <summary>Répare d'un coup les réglages néfastes détectés par <see cref="Checkup.Analyze"/> :
        /// remet les valeurs PAR DÉFAUT de Windows (gratuit, documenté, réversible via le panneau).</summary>
        public static DocAssistant.ChatAction FixCheckup(List<Checkup.Item> bad)
        {
            if (bad == null || bad.Count == 0) return null;
            var a = new DocAssistant.ChatAction();
            a.Label = bad.Count == 1 ? "Réparer : " + bad[0].Name : "Réparer les " + bad.Count + " réglages néfastes";
            a.IsChange = true;
            bool reboot = false; foreach (var it in bad) if (it.NeedReboot) reboot = true;
            a.Warning = "Remet les valeurs saines de Windows (gratuit, réversible depuis le panneau Réglages néfastes)."
                      + (reboot ? " Un redémarrage sera nécessaire pour une partie des réglages." : "");
            a.Run = delegate (Action<string, int> log)
            {
                var sb = new StringBuilder();
                int okN = 0;
                foreach (var it in bad)
                {
                    try
                    {
                        if (it.Fix != null) { it.Fix(log); okN++; sb.Append("✅ ").Append(it.Name).Append('\n'); }
                    }
                    catch { sb.Append("⚠ ").Append(it.Name).Append(" : la réparation a échoué\n"); }
                }
                sb.Append('\n').Append(okN).Append(" réglage(s) remis aux valeurs saines de Windows — sans rien payer.");
                if (reboot) sb.Append("\nRedémarre le PC pour que tout prenne effet.");
                return Say(sb.ToString().TrimEnd());
            };
            return a;
        }

        // ------------------------------------------------------------------
        //  Filet de sécurité
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MakeRestorePoint()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Créer un point de restauration";
            a.IsChange = true;
            a.Warning = "Ajoute une photo du système Windows. N'efface rien et ne touche pas à tes fichiers. Peut prendre une minute.";
            a.Run = delegate (Action<string, int> log)
            {
                try { Sys.CreateRestorePoint("Fluide — avant modification", log); }
                catch { return Say("Le point de restauration n'a pas pu être créé (la restauration système est peut-être désactivée)."); }
                return Say("✅ Point de restauration créé. Tu peux manipuler l'esprit tranquille : Windows sait revenir ici.");
            };
            return a;
        }
    }
}
