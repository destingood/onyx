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
    ///     l'annonce de ce qui va changer — c'est la promesse fondatrice de ONYX.
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
            a.Run = delegate (Action<string, int> log)
            {
                var r = Say(LibsText());
                r.Action = FixLibs();          // null si rien ne manque
                return r;
            };
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
                    ? "\n⚠ « " + top[0].Name + " » pèse lourd — je peux le fermer proprement (jamais un processus système)."
                    : "\nRien d'alarmant : aucun ne pèse vraiment sur les performances.");
                var r = Say(sb.ToString().TrimEnd());
                if (top[0].CpuPct >= 25) r.Action = FixHog(top[0].Name);
                return r;
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

        // ==================================================================
        //  RÉPARATIONS — ce que le Copilote sait corriger LUI-MÊME (sur clic)
        // ==================================================================

        /// <summary>« 🚀 TOUT réparer » : enchaîne toutes les corrections du plan, dans l'ordre
        /// d'impact, après UN SEUL clic explicite. Point de restauration d'abord (filet), puis
        /// chaque étape isolément — une qui échoue n'arrête pas les autres.</summary>
        public static DocAssistant.ChatAction AllFix(List<DocAssistant.ChatAction> steps)
        {
            if (steps == null || steps.Count < 2) return null;
            var a = new DocAssistant.ChatAction();
            a.Label = "🚀 TOUT réparer (" + steps.Count + " corrections)";
            a.IsChange = true;
            a.Warning = "Enchaîne les " + steps.Count + " corrections ci-dessus dans l'ordre d'impact, après un "
                      + "point de restauration (filet). Tout reste réversible. Peut prendre plusieurs minutes.";
            a.Run = delegate (Action<string, int> log)
            {
                var sb = new StringBuilder();
                try { Sys.CreateRestorePoint("ONYX — TOUT réparer (Copilote)", log); sb.Append("🛟 Point de restauration créé.\n\n"); }
                catch { sb.Append("⚠ Point de restauration impossible (restauration système coupée ?) — je continue, chaque étape reste réversible.\n\n"); }
                int okN = 0;
                foreach (var st in steps)
                {
                    if (st == null || st.Run == null) continue;
                    sb.Append("• ").Append(st.Label).Append(" : ");
                    try
                    {
                        DocAssistant.Reply r = st.Run(log);
                        sb.Append(r != null && !string.IsNullOrEmpty(r.Text) ? r.Text.Split('\n')[0] : "fait").Append('\n');
                        okN++;
                    }
                    catch (Exception ex) { sb.Append("échec — ").Append(ex.Message).Append('\n'); }
                }
                sb.Append('\n').Append("Terminé : ").Append(okN).Append('/').Append(steps.Count)
                  .Append(" corrections passées — le tout gratuitement. Je relance une vérification complète dans la foulée…");
                var res = Say(sb.ToString().TrimEnd());
                // Boucle FERMÉE : contrôle automatique après réparations (mesure : elle part seule).
                // Avec la mémoire d'enquête, le « réglé ✔ » s'affiche noir sur blanc.
                res.Action = Investigator.Action("vérification après réparations", null);
                return res;
            };
            return a;
        }

        /// <summary>Installe toutes les bibliothèques ESSENTIELLES manquantes (runtimes Microsoft,
        /// winget, gratuit) — null s'il ne manque rien.</summary>
        public static DocAssistant.ChatAction FixLibs()
        {
            int missing; try { missing = LibScan.MissingEssentialCount(); } catch { return null; }
            if (missing <= 0) return null;
            var a = new DocAssistant.ChatAction();
            a.Label = "Installer les " + missing + " bibliothèque(s) manquante(s)";
            a.IsChange = true;
            a.Warning = "Installe les runtimes officiels Microsoft manquants via winget (gratuit). Peut prendre quelques minutes.";
            a.Run = delegate (Action<string, int> log)
            {
                string winget = LibScan.WingetPath();
                if (winget == null) return Say("winget est introuvable : installe « App Installer » (gratuit, Microsoft Store), puis redemande-moi.");
                var sb = new StringBuilder();
                int okN = 0, n = 0;
                try
                {
                    foreach (var it in LibScan.Items())
                    {
                        if (!it.Essential) continue;
                        bool here = true; try { here = it.Installed(); } catch { }
                        if (here) continue;
                        n++;
                        bool done = false; try { done = LibScan.Install(winget, it, log); } catch { }
                        if (done) { okN++; sb.Append("✅ ").Append(it.Name).Append('\n'); }
                        else sb.Append("⚠ ").Append(it.Name).Append(" : échec (voir journal)\n");
                    }
                }
                catch { }
                if (n == 0) return Say("Toutes les bibliothèques essentielles sont déjà là. ✅");
                sb.Append('\n').Append(okN).Append('/').Append(n).Append(" installée(s), sans rien payer.")
                  .Append(okN == n ? " Tes jeux ont tout ce qu'il leur faut." : " Réessaie plus tard pour le reste.");
                return Say(sb.ToString().TrimEnd());
            };
            return a;
        }

        /// <summary>Bascule le DNS IPv4 vers un résolveur GRATUIT plus rapide — avec le même filet
        /// que le panneau : test réel après bascule, retour arrière automatique s'il ne répond pas.</summary>
        public static DocAssistant.ChatAction FixDns(string bestName, string[] servers)
        {
            if (servers == null || servers.Length == 0) return null;
            var a = new DocAssistant.ChatAction();
            a.Label = "Basculer le DNS vers " + bestName;
            a.IsChange = true;
            a.Warning = "Change les serveurs DNS IPv4 — gratuit, réversible (panneau DNS rapide → « Automatique »). "
                      + "Si le résolveur ne répond pas d'ici, retour arrière AUTOMATIQUE.";
            a.Run = delegate (Action<string, int> log)
            {
                try
                {
                    Dictionary<string, string[]> snap = Sys.SnapshotDns();
                    Sys.SetDns(servers, log);
                    if (DnsBench.QueryMs(servers[0], "www.google.com", 900, 2) < 0)
                    {
                        Sys.RestoreDnsSnapshot(snap, log);
                        return Say("⚠ " + bestName + " ne répond pas depuis ton réseau — j'ai tout remis comme avant. Rien n'est cassé.");
                    }
                }
                catch (Exception ex) { return Say("La bascule DNS a échoué : " + ex.Message); }
                return Say("✅ DNS basculé vers " + bestName + " et vérifié en vrai — gratuit, et réversible à tout moment.");
            };
            return a;
        }

        /// <summary>Applique le preset « Recommandé » (réglages sûrs, réversibles) via le MOTEUR
        /// complet de l'app : sauvegarde du registre + point de restauration AVANT, application
        /// isolée ensuite. Null si tout le preset est déjà actif.</summary>
        public static DocAssistant.ChatAction FixOpti()
        {
            List<Tweak> todo = RecommendedTodo();
            if (todo == null || todo.Count == 0) return null;
            var a = new DocAssistant.ChatAction();
            a.Label = "Appliquer le preset Recommandé (" + todo.Count + " réglages)";
            a.IsChange = true;
            a.Warning = "Applique les réglages sûrs du preset « Recommandé » (tous réversibles depuis Optimisations). "
                      + "Sauvegarde du registre + point de restauration créés AVANT. Peut prendre une minute.";
            a.Run = delegate (Action<string, int> log)
            {
                List<Tweak> sel = RecommendedTodo();   // re-mesuré au moment du clic
                if (sel == null || sel.Count == 0) return Say("Tout le preset Recommandé est déjà en place. ✅");
                EngineResult r;
                try { r = Engine.Run(sel, true, true, true, log); }
                catch (Exception ex) { return Say("L'application a échoué : " + ex.Message); }
                if (r.PrepFailed) return Say("Je n'ai RIEN appliqué : la préparation (sauvegarde) a échoué — " + r.PrepError);
                return Say("✅ " + r.Ok + "/" + sel.Count + " réglage(s) du preset Recommandé appliqués, sauvegarde créée."
                         + (r.RebootNeeded ? "\nUn redémarrage finalisera certains d'entre eux." : "")
                         + "\nRéversible à tout moment depuis Optimisations.");
            };
            return a;
        }

        private static List<Tweak> RecommendedTodo()
        {
            try
            {
                var sel = new List<Tweak>();
                foreach (Tweak t in Catalog.All())
                {
                    if (!t.Recommended) continue;
                    bool? c = null; try { c = t.Check != null ? t.Check() : null; } catch { }
                    if (c != true) sel.Add(t);
                }
                return sel;
            }
            catch { return null; }
        }

        /// <summary>Ferme tous les processus d'un nom : douce d'abord (CloseMainWindow), forcée
        /// sinon. Renvoie le nombre fermé ; 'forced' = ceux qu'il a fallu tuer.</summary>
        private static int CloseByName(string name, out int forced)
        {
            int soft = 0; forced = 0;
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                {
                    try
                    {
                        bool polite = false;
                        try { polite = p.CloseMainWindow(); } catch { }
                        if (polite && p.WaitForExit(3000)) { soft++; continue; }
                        p.Kill(true); p.WaitForExit(3000); forced++;
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { }
            return soft + forced;
        }

        /// <summary>Ferme proprement tous les processus d'un nom donné (jamais le cœur de Windows) :
        /// fermeture douce d'abord, forcée sinon. L'appli reste installée et relançable.</summary>
        public static DocAssistant.ChatAction FixHog(string name)
        {
            if (string.IsNullOrEmpty(name) || IsCore(name)) return null;
            var a = new DocAssistant.ChatAction();
            a.Label = "Fermer « " + name + " »";
            a.IsChange = true;
            a.Warning = "Ferme cette application (fermeture douce d'abord, forcée sinon). Sauvegarde ton travail dedans avant — tu peux la relancer quand tu veux.";
            a.Run = delegate (Action<string, int> log)
            {
                int forced; int n = CloseByName(name, out forced);
                if (n == 0) return Say("« " + name + " » ne tourne plus (déjà fermé ?).");
                return Say("✅ « " + name + " » fermé (" + n + " processus" + (forced > 0 ? ", dont " + forced + " forcé(s)" : "")
                         + "). Ton CPU respire — relance-le quand tu veux.");
            };
            return a;
        }

        // ------------------------------------------------------------------
        //  « Prépare ma partie » — nettoie le fond AVANT une session de jeu
        // ------------------------------------------------------------------
        //  Catégories fermées d'un clic. Comms (Discord) et audio (Spotify) restent
        //  VOLONTAIREMENT ouverts, et Riot est nécessaire pour LoL/Valorant.
        private static readonly string[] PrepCats = { "RGB", "lanceur", "fond animé", "overlay", "capture", "stream", "cloud", "navigateur" };

        public static DocAssistant.ChatAction PrepGame()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Repérage avant-partie"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                List<string[]> run = BloatForm.RunningBloat();
                if (run == null || run.Count == 0)
                    return Say("Rien de connu ne traîne en fond — ta session est déjà propre. 🎮 Bonne partie !");
                var close = new List<string>();
                foreach (var r in run)
                {
                    if (r[0].ToLowerInvariant().Contains("riot")) continue;   // nécessaire pour LoL/Valorant
                    foreach (var c in PrepCats)
                        if (string.Equals(r[1], c, StringComparison.OrdinalIgnoreCase)) { close.Add(r[0]); break; }
                }
                var sb = new StringBuilder("Repérage avant-partie — applis de fond détectées :\n");
                foreach (var r in run) sb.Append("• ").Append(r[0]).Append("  (").Append(r[1]).Append(")\n");
                sb.Append('\n');
                sb.Append(close.Count == 0
                    ? "Rien à fermer d'office : ce qui tourne (comms/musique) reste ton choix."
                    : "Je peux fermer les " + close.Count + " non essentielles d'un clic — je laisse exprès Discord/Spotify (comms/musique) et Riot.");
                var rep = Say(sb.ToString().TrimEnd());
                if (close.Count > 0)
                {
                    var fix = new DocAssistant.ChatAction();
                    fix.Label = "Fermer " + close.Count + " appli(s) de fond";
                    fix.IsChange = true;
                    fix.Warning = "Ferme : " + string.Join(", ", close.ToArray()) + ". Fermeture douce d'abord ; tout se "
                                + "relance à la main. Enregistre ce que tu veux garder (onglets, projets) avant.";
                    fix.Run = delegate (Action<string, int> log2)
                    {
                        int total = 0, forcedTot = 0;
                        var sb2 = new StringBuilder();
                        foreach (var n in close)
                        {
                            int f; int c = CloseByName(n, out f);
                            total += c; forcedTot += f;
                            if (c > 0) sb2.Append("✅ ").Append(n).Append('\n');
                        }
                        if (total == 0) return Say("Tout était déjà fermé. 🎮 Bonne partie !");
                        sb2.Append('\n').Append(total).Append(" processus fermé(s)")
                           .Append(forcedTot > 0 ? " (dont " + forcedTot + " forcé(s))" : "")
                           .Append(" — le fond est propre. 🎮 Bonne partie !");
                        return Say(sb2.ToString().TrimEnd());
                    };
                    rep.Action = fix;
                }
                return rep;
            };
            return a;
        }

        // ==================================================================
        //  DÉPANNAGE PC UNIVERSEL — les grandes réparations, gratuites
        // ==================================================================

        /// <summary>Répare l'intégrité de Windows : DISM /RestoreHealth puis SFC /scannow. C'est LE
        /// remède universel aux corruptions système (crashs, plantages, MAJ qui échoue, apps qui
        /// ne s'ouvrent plus). Long (10-20 min), gratuit, officiel Microsoft, sans risque.</summary>
        public static DocAssistant.ChatAction RepairWindows()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Réparer Windows (DISM + SFC, 10-20 min)";
            a.IsChange = true;
            a.Warning = "Lance les réparateurs officiels de Windows (DISM puis SFC). Gratuit, sans risque, mais LONG "
                      + "(10-20 min) — tu peux continuer à utiliser le PC. Un redémarrage peut être demandé ensuite.";
            a.Run = delegate (Action<string, int> log)
            {
                if (log != null) log("Réparation d'intégrité Windows (DISM /RestoreHealth puis SFC /scannow)…", 0);
                try { Sys.RepairWindows(log); }
                catch (Exception ex) { return Say("La réparation n'a pas pu aller au bout : " + ex.Message); }
                return Say("✅ Réparation Windows terminée (DISM + SFC). Si des fichiers ont été réparés, redémarre le PC "
                         + "pour finaliser. Beaucoup de problèmes « impossibles à régler » partent après ça.");
            };
            return a;
        }

        /// <summary>Réinitialise la PILE réseau : Winsock + TCP/IP + cache DNS/ARP + IP renouvelée.
        /// LE remède au « plus d'internet » quand tout semble branché (souvent laissé par un VPN,
        /// un antivirus ou un mauvais « optimiseur »). Gratuit ; un redémarrage finalise.</summary>
        public static DocAssistant.ChatAction RepairNetwork()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Réinitialiser la connexion réseau";
            a.IsChange = true; a.NoChain = true;
            a.Warning = "Remet à zéro Winsock, la pile TCP/IP et les caches DNS/ARP (commandes officielles Windows). "
                      + "Répare la plupart des « plus d'internet ». Un REDÉMARRAGE est nécessaire ensuite pour finaliser.";
            a.Run = delegate (Action<string, int> log)
            {
                string sh = Sys.Sys32("netsh.exe"), ip = Sys.Sys32("ipconfig.exe");
                try
                {
                    if (log != null) log("Réinitialisation réseau : Winsock…", 0);
                    Sys.Run(sh, "winsock reset");
                    if (log != null) log("Réinitialisation réseau : pile TCP/IP…", 0);
                    Sys.Run(sh, "int ip reset");
                    Sys.Run(sh, "int ipv6 reset");
                    Sys.Run(sh, "winhttp reset proxy");
                    Sys.Run(ip, "/flushdns");
                    Sys.Run(ip, "/release");
                    Sys.Run(ip, "/renew");
                }
                catch (Exception ex) { return Say("La réinitialisation réseau a échoué : " + ex.Message); }
                var r = Say("✅ Pile réseau réinitialisée (Winsock, TCP/IP, DNS/ARP). ⚠ REDÉMARRE le PC pour finaliser — "
                          + "c'est après le redémarrage que la connexion revient. Je peux le programmer :");
                r.Action = RestartAction();
                return r;
            };
            return a;
        }

        /// <summary>Relance le moteur audio de Windows (services Audiosrv + AudioEndpointBuilder).
        /// Corrige la majorité des « plus de son » sans redémarrer. Gratuit, immédiat.</summary>
        public static DocAssistant.ChatAction RepairAudio()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Relancer le son de Windows";
            a.IsChange = true;
            a.Warning = "Redémarre les services audio de Windows. Le son se coupe une seconde puis revient. Sans risque.";
            a.Run = delegate (Action<string, int> log)
            {
                try
                {
                    if (log != null) log("Redémarrage du moteur audio (AudioEndpointBuilder + Audiosrv)…", 0);
                    Sys.RestartService("AudioEndpointBuilder");   // porte Audiosrv (dépendance) : le relance aussi
                }
                catch (Exception ex) { return Say("Je n'ai pas pu relancer l'audio : " + ex.Message); }
                return Say("✅ Moteur audio relancé. Si le son ne revient pas : vérifie le bon périphérique de sortie "
                         + "(clic sur l'icône 🔊 près de l'horloge) et le volume de l'appli. Sinon, dis-moi et je creuse.");
            };
            return a;
        }

        // ------------------------------------------------------------------
        //  Cerveau IA LOCAL (optionnel, gratuit) — « il répond à tout »
        // ------------------------------------------------------------------
        /// <summary>Inspecte l'état (Ollama installé ? serveur ? modèle ?) et propose la BONNE
        /// prochaine étape — active le cerveau dès que tout est prêt.</summary>
        public static DocAssistant.ChatAction SetupBrain()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "État de l'IA locale"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                LocalBrain.ClearOptOut();   // demande explicite : elle annule un « désactive » passé
                if (LocalBrain.ServerUp(1500))
                {
                    string m = LocalBrain.BestModel();
                    if (m != null)
                    {
                        LocalBrain.SetEnabled(true);
                        return Say("🧠 IA locale ACTIVÉE (" + m + ") — gratuite, 100 % sur ta machine, rien ne sort du PC.\n"
                                 + "Désormais, tout ce que mes règles ne comprennent pas, je le lui demande — pose-moi "
                                 + "n'importe quelle question ! (« désactive l'ia » pour couper.)");
                    }
                    LocalBrain.ModelPick pk = LocalBrain.ChooseModel();
                    var r0 = Say("Ollama tourne, mais aucun modèle n'est téléchargé. J'ai choisi celui qui convient le "
                               + "mieux à ta machine : " + pk.Human + " — gratuit, une seule fois, il tourne ensuite hors-ligne :");
                    r0.Action = PullModel();
                    return r0;
                }
                if (LocalBrain.Installed)
                    return Say("✅ Ollama est bien installé, mais son moteur ne tourne pas à l'instant. Lance « Ollama » "
                             + "depuis le menu Démarrer (il se met dans la zone de notification), puis redis « active l'ia » — "
                             + "je m'occupe du reste (modèle adapté à ta machine + configuration).");
                var r = Say("Ollama n'est pas installé sur ce PC. Je peux l'installer pour toi — gratuit, open source, "
                          + "100 % sur ta machine, aucune donnée envoyée, aucun abonnement — puis je le configure et "
                          + "télécharge le modèle adapté :");
                r.Action = InstallTool("Ollama.Ollama", "Ollama (IA locale)");
                return r;
            };
            return a;
        }

        /// <summary>Télécharge le modèle ADAPTÉ à la machine (une seule fois).</summary>
        public static DocAssistant.ChatAction PullModel()
        {
            LocalBrain.ModelPick pick = LocalBrain.ChooseModel();
            var a = new DocAssistant.ChatAction();
            a.Label = "Télécharger le modèle (" + pick.Human.Split('—')[0].Trim() + ", gratuit)";
            a.IsChange = true;
            a.Warning = "Télécharge " + pick.Human + " via Ollama (une seule fois). Modèle choisi pour TA config. "
                      + "Il tourne ensuite 100 % hors-ligne. Peut prendre plusieurs minutes selon ta connexion.";
            a.Run = delegate (Action<string, int> log)
            {
                string exe = LocalBrain.OllamaExe(); if (exe == null) exe = "ollama";
                if (log != null) log("Téléchargement du modèle " + pick.Tag + " (Ollama)…", 0);
                // 60 min : un modèle de plusieurs Go sur une connexion normale dépasse le délai par défaut (10 min).
                try { Sys.Run(exe, "pull " + pick.Tag, Sys.LongRunTimeoutMs); }
                catch (Exception ex) { return Say("Le téléchargement a échoué : " + ex.Message); }
                if (LocalBrain.BestModel() != null)
                {
                    LocalBrain.SetEnabled(true);
                    return Say("🧠 Modèle prêt (" + pick.Tag + ") — IA locale ACTIVÉE. Pose-moi n'importe quelle question !");
                }
                return Say("Le modèle ne s'est pas installé (connexion ? espace disque ?). Réessaie dans un moment.");
            };
            return a;
        }

        /// <summary>Télécharge bge-m3 (embeddings plus précis, français) et ré-indexe la base.</summary>
        public static DocAssistant.ChatAction UpgradeEmbed()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Installer bge-m3 (~1,2 Go) + ré-indexer";
            a.IsChange = true;
            a.Warning = "Télécharge un modèle de recherche plus précis (bge-m3) via Ollama, puis reconstruit l'index de "
                      + "ta base de connaissances avec. Une seule fois ; ~1,2 Go.";
            a.Run = delegate (Action<string, int> log)
            {
                if (!LocalBrain.ServerUp(1500)) return Say("Ollama ne répond pas — lance-le (« active l'ia »), puis réessaie.");
                string exe = LocalBrain.OllamaExe(); if (exe == null) exe = "ollama";
                if (log != null) log("Téléchargement du modèle de recherche bge-m3 (~1,2 Go)…", 0);
                try { Sys.Run(exe, "pull " + LocalBrain.EmbedModelPro, Sys.LongRunTimeoutMs); }
                catch (Exception ex) { return Say("Le téléchargement a échoué : " + ex.Message); }
                if (LocalBrain.EmbedModelName() != LocalBrain.EmbedModelPro)
                    return Say("Le modèle ne s'est pas installé (connexion ? espace ?). Réessaie plus tard.");
                if (log != null) log("Ré-indexation de la base avec bge-m3…", 0);
                try { KnowledgeBase.Invalidate(); KnowledgeBase.EnsureIndex(); } catch { }
                return Say("✅ bge-m3 installé et base ré-indexée — la recherche dans tes documents est maintenant plus précise (surtout en français).");
            };
            return a;
        }

        /// <summary>Passe la question au modèle LOCAL (lecture seule : ça ne modifie rien).</summary>
        public static DocAssistant.ChatAction AskBrain(string q, BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Je réfléchis (IA locale)"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                if (!LocalBrain.ServerUp(1500))
                    return Say("Mon cerveau IA local ne répond pas : lance Ollama (menu Démarrer), puis repose ta "
                             + "question. (« active l'ia » refait le point si besoin.)");
                string model = LocalBrain.BestModel();
                if (model == null)
                {
                    var r0 = Say("Ollama tourne mais aucun modèle n'est téléchargé — je peux m'en charger :");
                    r0.Action = PullModel();
                    return r0;
                }
                if (log != null) log("IA locale (" + model + ") réfléchit…", 0);
                string ans;
                // RAG : on récupère les extraits pertinents de la base de connaissances (intégrée +
                // tes documents) et on les ajoute au contexte → réponses ancrées et précises.
                string sysCtx = BrainContext(st);
                bool grounded = false;   // la réponse s'appuiera-t-elle sur des preuves (base) ?
                try
                {
                    string kb = KnowledgeBase.Search(q, 5);
                    if (!string.IsNullOrEmpty(kb))
                    {
                        sysCtx += "\n\n" + kb + "Sers-toi de ces extraits pour tout fait qu'ils couvrent (cite « base de connaissances »). Si une source est marquée « ⚠ peut-être daté », préviens que l'info n'est peut-être plus à jour. S'ils ne couvrent pas la question, ne force pas et n'invente rien.";
                        grounded = true;
                    }
                }
                catch { }
                // Température DYNAMIQUE (technique reconnue anti-hallucination) : quasi nulle pour
                // une question FACTUELLE (moins de « créativité » = moins d'invention), normale pour
                // le bavardage / les conseils où un peu de naturel est bienvenu.
                bool factual = IsFactualLookup(q);
                // Mémoire de conversation : la question rejoint le fil, l'IA répond EN CONTEXTE
                // (« et pourquoi ? », « développe »… gardent leur sens).
                LocalBrain.PushUser(q);
                try { ans = LocalBrain.AskChat(sysCtx, model, factual ? FactualTemp : NormalTemp, factual ? FactualTopP : NormalTopP); }
                catch (Exception ex) { return Say("L'IA locale a calé : " + ex.Message); }
                if (string.IsNullOrEmpty(ans))
                    return Say("Là, honnêtement, je sèche — reformule, ou pose-moi un souci PC : c'est mon terrain, j'y suis imbattable.");

                // RAISONNEMENT AUTOMATIQUE : valide la réponse contre les règles déterministes du
                // domaine PC (mythes / conseils dangereux). Si invalide, on y adjoint la correction.
                ans = ReasonCheck.Enhance(ans.Trim());

                // ══ ANTI-HALLUCINATION ══════════════════════════════════════════════════════
                // Le vrai piège n'est PAS seulement le doute avoué : c'est l'hallucination
                // CONFIANTE (inventer une bio, une fiche technique, une date, un chiffre sans la
                // moindre hésitation — le bug « Clio Williams »). Donc on vérifie sur le web dès
                // que la question est FACTUELLE (personne, marque, produit, lieu, date, chiffre,
                // définition d'entité…), MÊME si la réponse a l'air parfaitement sûre.
                bool webOk   = LocalBrain.Enabled && !LocalBrain.WebOff();   // factual déjà calculé plus haut
                // Garde côté RÉPONSE : une réponse NON ancrée (ni base, ni web) qui assène un fait
                // daté (« né en 1997 », « fondée en 2010 ») est typiquement une invention confiante,
                // même quand la QUESTION n'avait rien de factuel → on la traite pareil.
                bool riskyFact   = !grounded && AnswerHasHardFact(ans);
                bool needVerify  = factual || riskyFact || LooksUnsure(ans);
                bool needHonesty = factual || riskyFact;   // ne doit jamais passer pour une certitude

                if (needVerify && webOk && !PrivacyGuard.HasPII(q))   // jamais de PII vers le web
                {
                    if (log != null) log(needHonesty ? "Vérification factuelle sur le web…"
                                                     : "Réponse incertaine → vérification sur le web…", 0);
                    List<WebSearch.Result> res = null;
                    try { res = WebSearch.Query(q, 5); } catch { }
                    if (res != null && res.Count > 0)
                    {
                        string better = null;
                        try { better = LocalBrain.AskWeb(q, WebSearch.Context(res), model); } catch { }
                        if (!string.IsNullOrEmpty(better))
                        {
                            string bt = better.Trim();
                            LocalBrain.PushAssistant(bt);
                            // Si le web est HORS-SUJET (le modèle le dit lui-même), NE PAS afficher
                            // « Fiabilité élevée » ni mémoriser une non-réponse (bug vu en test météo).
                            bool weak = WebLooksOffTopic(bt);
                            if (!weak) LocalBrain.RememberFact(q, bt);
                            string tag = weak ? "🌐 Fiabilité faible · le web n'a pas répondu clairement là-dessus"
                                              : Reliability(true, false) + " (" + WebSearch.Sources(res) + ")";
                            return Say(ReasonCheck.Enhance(bt) + "\n\n— " + tag + ".");
                        }
                    }
                    // Fait à vérifier mais web injoignable / rien trouvé : ne JAMAIS faire passer une
                    // réponse peut-être inventée pour une certitude → on prévient honnêtement.
                    if (needHonesty)
                    {
                        LocalBrain.PushAssistant(ans.Trim());
                        return Say(ans.Trim() + "\n\n⚠️ Je n'ai pas pu vérifier ça en ligne (web injoignable ou rien "
                                 + "trouvé) — à prendre avec des pincettes, je peux me tromper sur ce point précis.");
                    }
                }
                // Fait à affirmer SANS internet : le modèle local seul invente facilement des faits
                // précis → on l'assume clairement et on propose la vérification.
                else if (needHonesty && !webOk)
                {
                    LocalBrain.PushAssistant(ans.Trim());
                    return Say(ans.Trim() + "\n\n⚠️ Réponse de mémoire, NON vérifiée en ligne (sur les faits précis "
                             + "je peux me tromper). Dis « active internet » et je confirme sur le web.");
                }

                LocalBrain.PushAssistant(ans.Trim());
                return Say(ans.Trim() + "\n\n— " + Reliability(false, grounded) + " · IA locale (" + model + "), 100 % sur ta machine.");
            };
            return a;
        }

        // ------------------------------------------------------------------
        //  RECHERCHE WEB — le Copilote va chercher l'info d'actualité en ligne
        // ------------------------------------------------------------------
        /// <summary>Télécharge une page web donnée et la fait RÉSUMER par le modèle local.</summary>
        public static DocAssistant.ChatAction ReadUrl(string url, string q, BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Lecture de la page"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                if (LocalBrain.WebOff())
                    return Say("La recherche internet est coupée. Dis « active internet » pour que je puisse lire des pages web.");
                if (log != null) log("Lecture de la page " + url + "…", 0);
                string txt;
                try { txt = WebSearch.FetchPage(url, 6000); }
                catch { txt = ""; }
                if (string.IsNullOrEmpty(txt) || txt.Length < 60)
                    return Say("Je n'ai pas réussi à lire cette page (injoignable, protégée, ou vide).");
                if (!LocalBrain.ServerUp(1500) || LocalBrain.BestModel() == null)
                    return Say("Page lue, mais l'IA locale n'est pas là pour la résumer. Active-la (« active l'ia »).");
                string demande = string.IsNullOrEmpty(q) ? "Résume cette page en français, points clés." : q;
                string ans;
                try { ans = LocalBrain.AskWeb(demande, "Contenu de la page " + url + " :\n" + txt, LocalBrain.BestModel()); }
                catch (Exception ex) { return Say("Le résumé a échoué : " + ex.Message); }
                if (string.IsNullOrEmpty(ans)) return Say("Je n'ai pas pu résumer cette page.");
                return Say(ans.Trim() + "\n\n— 🌐 lu sur " + url + ", synthétisé par l'IA locale.");
            };
            return a;
        }

        // Ville mentionnée dans une question météo (« météo à Lyon » → « lyon »), ou "" → géoloc IP.
        private static string ExtractCity(string q)
        {
            if (string.IsNullOrEmpty(q)) return "";
            string low = Deaccent(q.ToLowerInvariant());
            string[] marks = { "meteo a ", "meteo de ", "meteo sur ", "temps a ", "meteo ", " a " };
            int best = -1, len = 0;
            foreach (string m in marks) { int k = low.IndexOf(m, StringComparison.Ordinal); if (k >= 0 && (best < 0 || k < best)) { best = k; len = m.Length; } }
            if (best < 0) return "";
            string tail = low.Substring(best + len).Trim().TrimEnd('?', '!', '.', ' ');
            // retire les mots de liaison résiduels et borne à un nom de ville court
            foreach (string junk in new[] { "fait il", "fait-il", "aujourd'hui", "demain", "maintenant", "dehors", "il fait" })
                tail = tail.Replace(junk, "").Trim();
            if (tail.Length > 40) tail = tail.Substring(0, 40).Trim();
            return tail;
        }

        /// <summary>Météo actuelle via Open-Meteo (gratuit, sans clé). Ville extraite de la question,
        /// sinon estimée par l'IP. Action asynchrone (appels réseau hors du fil de l'UI).</summary>
        public static DocAssistant.ChatAction WeatherAction(string q, BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Météo (Open-Meteo)"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                if (log != null) log("Météo en direct (Open-Meteo, gratuit)…", 0);
                LiveData.Meteo m = null;
                try { m = LiveData.Current(ExtractCity(q)); } catch { }
                if (m == null)
                    return Say("Je n'ai pas réussi à récupérer la météo (ville introuvable ou connexion coupée). "
                             + "Précise ta ville : « météo à <ta ville> ».");
                string src = m.FromIp ? " (ville estimée d'après ta connexion)" : "";
                string t = m.Temp.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
                string w = m.Wind.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                return Say("🌦️ À " + m.City + src + " : " + m.Desc + ", " + t + " °C, vent " + w + " km/h.\n"
                         + "— Open-Meteo (gratuit), relevé du moment.");
            };
            return a;
        }

        /// <summary>Conversion de devises via Frankfurter (BCE, gratuit). Action asynchrone.</summary>
        public static DocAssistant.ChatAction CurrencyAction(double amount, string from, string to, BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Change de devise"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                if (log != null) log("Taux de change (Frankfurter / BCE)…", 0);
                double? r = null;
                try { r = LiveData.Currency(amount, from, to); } catch { }
                if (r == null) return Say("Je n'ai pas réussi à récupérer le taux de change (devise inconnue ou hors-ligne).");
                return Say("💱 " + UtilityTools.Fmt(amount) + " " + from + " = " + UtilityTools.Fmt(r.Value) + " " + to
                         + ".\n— taux BCE du jour (Frankfurter, gratuit). Indicatif, hors frais bancaires.");
            };
            return a;
        }

        /// <summary>Prochains jours fériés en France via Nager.Date (gratuit). Action asynchrone.</summary>
        public static DocAssistant.ChatAction HolidaysAction(BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Jours fériés"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                if (log != null) log("Jours fériés (Nager.Date)…", 0);
                string list = null;
                try { list = LiveData.NextHolidays(); } catch { }
                if (string.IsNullOrEmpty(list)) return Say("Je n'ai pas réussi à récupérer les jours fériés (hors-ligne ?).");
                return Say("📅 Prochains jours fériés en France :\n" + list + "\n— source : Nager.Date (gratuit).");
            };
            return a;
        }

        /// <summary>Prix d'une cryptomonnaie (en euros) via CoinGecko (gratuit). Action asynchrone.</summary>
        public static DocAssistant.ChatAction CryptoAction(string coinId, BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Prix crypto"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                if (log != null) log("Cours (CoinGecko)…", 0);
                double? p = null;
                try { p = LiveData.Crypto(coinId, "eur"); } catch { }
                if (p == null) return Say("Je n'ai pas réussi à récupérer le cours (hors-ligne ?).");
                return Say("🪙 " + coinId + " ≈ " + UtilityTools.Fmt(p.Value) + " € (cours du moment, CoinGecko).\n"
                         + "Info seulement — je ne donne pas de conseil d'investissement.");
            };
            return a;
        }

        /// <summary>Traduction via MyMemory (gratuit). Action asynchrone.</summary>
        public static DocAssistant.ChatAction TranslateAction(string text, string from, string to, BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Traduction"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                if (log != null) log("Traduction (MyMemory)…", 0);
                string r = null;
                try { r = LiveData.Translate(text, from, to); } catch { }
                if (string.IsNullOrEmpty(r)) return Say("Je n'ai pas réussi à traduire (hors-ligne ?). L'IA locale peut essayer si tu reformules.");
                return Say("🌍 " + r + "\n— traduction " + from + "→" + to + " (MyMemory, gratuit).");
            };
            return a;
        }

        /// <summary>IP publique + FAI via ipwho.is (gratuit). Action asynchrone.</summary>
        public static DocAssistant.ChatAction MyIpAction(BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Mon IP publique"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                if (log != null) log("Adresse IP (ipwho.is)…", 0);
                string r = null;
                try { r = LiveData.MyIp(); } catch { }
                return Say(string.IsNullOrEmpty(r) ? "Je n'ai pas pu récupérer ton IP publique (hors-ligne ?)." : r);
            };
            return a;
        }

        /// <summary>Lever/coucher du soleil via Open-Meteo (gratuit). Action asynchrone.</summary>
        public static DocAssistant.ChatAction SunAction(string q, BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Soleil"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                if (log != null) log("Lever/coucher du soleil (Open-Meteo)…", 0);
                LiveData.Sun s = null;
                try { s = LiveData.SunTimes(ExtractCity(q)); } catch { }
                if (s == null) return Say("Je n'ai pas pu récupérer les horaires du soleil (ville introuvable ou hors-ligne).");
                string src = s.FromIp ? " (ville estimée d'après ta connexion)" : "";
                return Say("☀️ À " + s.City + src + " : lever du soleil à " + s.Rise + ", coucher à " + s.Set + ".\n— Open-Meteo (gratuit).");
            };
            return a;
        }

        /// <summary>Fiche produit / nutrition via Open Food Facts (gratuit, sans clé). Action asynchrone.</summary>
        public static DocAssistant.ChatAction FoodAction(string product, BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Produit / nutrition"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                if (log != null) log("Produit (Open Food Facts)…", 0);
                string r = null;
                try { r = LiveData.Food(product); } catch { }
                if (string.IsNullOrEmpty(r)) return Say("Je n'ai pas trouvé « " + product + " » dans Open Food Facts (ou hors-ligne). Essaie un nom précis, ex. « nutriscore du nutella ».");
                return Say(r);
            };
            return a;
        }

        /// <summary>Ville d'un code postal français via Zippopotam (gratuit). Action asynchrone.</summary>
        public static DocAssistant.ChatAction PostalAction(string code, BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Code postal"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                if (log != null) log("Code postal (Zippopotam)…", 0);
                string r = null;
                try { r = LiveData.Postal(code); } catch { }
                if (string.IsNullOrEmpty(r)) return Say("Je n'ai pas trouvé le code postal « " + code + " » (code français à 5 chiffres, ou hors-ligne ?).");
                return Say(r);
            };
            return a;
        }

        /// <summary>Photo astronomique du jour (NASA APOD, clé de démo). Action asynchrone.</summary>
        public static DocAssistant.ChatAction ApodAction(BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Photo astro (NASA)"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                if (log != null) log("Photo du jour (NASA APOD)…", 0);
                string r = null;
                try { r = LiveData.Apod(); } catch { }
                return Say(string.IsNullOrEmpty(r) ? "Je n'ai pas pu récupérer la photo astro du jour (hors-ligne ou quota NASA atteint)." : r);
            };
            return a;
        }

        /// <summary>Avions en vol autour de toi via OpenSky (gratuit, sans clé). Action asynchrone.</summary>
        public static DocAssistant.ChatAction FlightsAction(BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Avions en vol"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                if (log != null) log("Avions en vol (OpenSky)…", 0);
                string r = null;
                try { r = LiveData.FlightsNearby(); } catch { }
                return Say(string.IsNullOrEmpty(r) ? "Je n'ai pas pu récupérer le trafic aérien (hors-ligne, position inconnue, ou OpenSky saturé)." : r);
            };
            return a;
        }

        /// <summary>Cherche sur le web puis fait répondre le modèle LOCAL à partir des résultats.
        /// Pour les questions d'actualité/temps réel (match, météo, prix, news…). Lecture seule.</summary>
        public static DocAssistant.ChatAction WebAnswer(string q, BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Recherche web"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                if (LocalBrain.WebOff())
                    return Say("La recherche internet est coupée. Dis « active internet » pour que je puisse chercher l'actualité en ligne.");
                // Garde-fou VIE PRIVÉE : ne jamais envoyer une info perso à un moteur externe.
                var pii = PrivacyGuard.Detect(q);
                if (pii.Count > 0)
                    return Say("⚠️ Ta demande contient une info personnelle (" + string.Join(", ", pii.ToArray()) + "). "
                             + "Je ne l'envoie PAS à un moteur de recherche externe (vie privée). Retire l'info sensible et "
                             + "redemande, ou pose-moi ça autrement — les mesures et réparations restent 100 % sur ta machine.");
                // BASE UNIVERSELLE : Wikipédia d'abord (encyclopédie sourcée) pour les entités/concepts —
                // bien plus fiable qu'un scraping. On ancre la réponse dessus et on cite l'article.
                if (LocalBrain.ServerUp(1500) && LocalBrain.BestModel() != null)
                {
                    Wikipedia.Page wp = null;
                    try { wp = Wikipedia.Lookup(q); } catch { }
                    if (wp != null)
                    {
                        if (log != null) log("Wikipédia (« " + wp.Title + " »)…", 0);
                        string wa = null;
                        try { wa = LocalBrain.AskWeb(q, Wikipedia.Context(wp), LocalBrain.BestModel()); } catch { }
                        if (!string.IsNullOrEmpty(wa) && !WebLooksOffTopic(wa))
                        {
                            LocalBrain.PushAssistant(wa.Trim());
                            LocalBrain.RememberFact(q, wa.Trim());
                            return Say(ReasonCheck.Enhance(wa.Trim()) + "\n\n— 📚 " + Reliability(true, false)
                                     + " (Wikipédia : " + wp.Url + ").");
                        }
                    }
                }
                if (log != null) log("Recherche web (DuckDuckGo)…", 0);
                List<WebSearch.Result> res;
                try { res = WebSearch.Query(q, 5); }
                catch { res = null; }
                if (res == null || res.Count == 0)
                    return Say("Je n'ai pas réussi à joindre le web (hors-ligne ?) ou rien trouvé. Réessaie, ou reformule ta recherche.");
                if (!LocalBrain.ServerUp(1500) || LocalBrain.BestModel() == null)
                {
                    // Pas d'IA pour résumer : on donne quand même les meilleurs résultats bruts.
                    var sb = new StringBuilder("🌐 Voici ce que j'ai trouvé sur le web :\n\n");
                    foreach (var r in res) { sb.Append("• ").Append(r.Title); if (!string.IsNullOrEmpty(r.Domain)) sb.Append("  (").Append(r.Domain).Append(')'); sb.Append('\n'); }
                    return Say(sb.ToString().TrimEnd());
                }
                string ans;
                try { ans = LocalBrain.AskWeb(q, WebSearch.Context(res), LocalBrain.BestModel()); }
                catch (Exception ex) { return Say("La synthèse a échoué : " + ex.Message); }
                if (string.IsNullOrEmpty(ans))
                    return Say("Je n'ai pas pu résumer les résultats. Sources : " + WebSearch.Sources(res) + ".");
                return Say(ans.Trim() + "\n\n— 🌐 recherché sur le web (" + WebSearch.Sources(res) + "), synthétisé par l'IA locale.");
            };
            return a;
        }

        // La réponse trahit-elle une incertitude / un manque d'info ? → déclenche l'auto-vérification web.
        private static bool LooksUnsure(string ans)
        {
            string a = (ans ?? "").ToLowerInvariant();
            string[] cues =
            {
                // doute
                "je ne suis pas sur", "je ne suis pas certain", "je ne suis pas sûr", "pas totalement sur",
                "je ne sais pas", "je n'ai pas l'info", "je n'ai pas d'info", "je n'ai pas acces",
                "je n'ai pas accès", "a verifier", "à vérifier", "reste a confirmer", "je pense que",
                "il me semble", "peut-etre", "peut etre", "sous reserve", "je ne connais pas",
                "je n'en suis pas sur", "difficile a dire", "il faudrait verifier",
                // refus / limite de connaissances (souvent une info RÉCENTE prise pour du futur) → à vérifier sur le web
                "je ne peux pas fournir", "je ne peux pas répondre", "je ne peux pas repondre",
                "je ne peux pas te dire", "je ne peux pas confirmer", "je ne peux pas savoir",
                "je ne dispose pas", "je ne suis pas en mesure", "je n'ai pas la possibilite",
                "au-dela de mes connaissances", "au-delà de mes connaissances", "ma base de connaissances",
                "date de coupure", "ma derniere mise a jour", "ma dernière mise à jour", "apres ma formation",
                "pas encore realise", "pas encore réalisé", "n'est pas encore", "futur lointain",
                "en temps reel", "en temps réel", "informations en direct",
                "je n'ai pas cette information", "je ne dispose pas de", "je ne saurais",
                "sans certitude", "de memoire", "de mémoire", "je crois que", "il se peut"
            };
            foreach (string c in cues) if (a.Contains(c)) return true;
            return false;
        }

        // Températures + top-p de génération. Proche de 0 / restreint pour le factuel (moins
        // d'invention), plus souple pour le bavardage/conseils. Techniques reconnues anti-hallucination.
        private const double FactualTemp = 0.15;
        private const double NormalTemp  = 0.4;
        private const double FactualTopP = 0.5;
        private const double NormalTopP  = 0.9;

        /// <summary>Température à utiliser pour cette question : quasi nulle si factuelle, normale sinon.</summary>
        internal static double ChatTemperature(string q) { return IsFactualLookup(q) ? FactualTemp : NormalTemp; }

        /// <summary>top-p à utiliser : restreint si factuelle, large sinon.</summary>
        internal static double ChatTopP(string q) { return IsFactualLookup(q) ? FactualTopP : NormalTopP; }

        /// <summary>Indicateur de fiabilité AFFICHÉ à l'utilisateur (recommandé par l'état de l'art :
        /// « établir un score de certitude et l'afficher »). Élevée = ancré (web ou base), sinon moyenne.</summary>
        internal static string Reliability(bool webVerified, bool grounded)
        {
            if (webVerified) return "✅ Fiabilité élevée · vérifié en ligne";
            if (grounded)    return "✅ Fiabilité élevée · source : ta base de connaissances";
            return "🧠 Fiabilité moyenne · connaissances générales";
        }

        // La synthèse web indique-t-elle elle-même que les résultats sont HORS-SUJET / sans info ?
        // → on ne doit pas la présenter comme « vérifiée / fiable » (fausse confiance).
        internal static bool WebLooksOffTopic(string ans)
        {
            string a = Deaccent((ans ?? "").ToLowerInvariant());
            string[] cues =
            {
                "ne contiennent pas", "ne contient pas", "pas d'information", "pas d'info", "hors-sujet",
                "hors sujet", "je n'ai pas trouve", "aucune information", "ne mentionnent pas", "ne precisent pas",
                "pas de resultat", "ne repondent pas", "pas trouve d'info", "ne fournissent pas", "sans rapport"
            };
            foreach (string c in cues) if (a.Contains(c)) return true;
            return false;
        }

        private static string DiagLine(bool ok, string label) { return "• " + (ok ? "✅" : "❌") + " " + label + "\n"; }

        /// <summary>Auto-diagnostic AFFICHÉ : mesure en direct que les garde-fous anti-hallucination
        /// fonctionnent (recommandation « mesurer le succès / contrôle continu »). Rend le système
        /// observable dans l'app, pas seulement dans le harnais de test développeur.</summary>
        internal static string SelfDiagnostic()
        {
            int ok = 0, n = 0;
            var sb = new StringBuilder("🩺 Auto-diagnostic des garde-fous anti-hallucination :\n");
            n++; bool c1 = IsFactualLookup("qui est untel") && !IsFactualLookup("salut ca va");
            if (c1) ok++; sb.Append(DiagLine(c1, "Détection des questions factuelles → vérification web"));
            n++; bool c2 = AnswerHasHardFact("il est ne en 1990") && !AnswerHasHardFact("ta souris est a 144 hz");
            if (c2) ok++; sb.Append(DiagLine(c2, "Détection des faits datés non vérifiés"));
            n++; bool c3 = ReasonCheck.Check("desactive ton pagefile").Count > 0 && ReasonCheck.Check("active le dlss").Count == 0;
            if (c3) ok++; sb.Append(DiagLine(c3, "Raisonnement automatique (règles de domaine)"));
            n++; bool c4 = ChatTemperature("qui est untel") < ChatTemperature("raconte une blague");
            if (c4) ok++; sb.Append(DiagLine(c4, "Température & top-p dynamiques"));
            n++; bool c5 = !string.IsNullOrEmpty(LocalBrain.ExtractKey("c'est qui clio williams"));
            if (c5) ok++; sb.Append(DiagLine(c5, "Mémoire des faits vérifiés (anti flip-flop)"));
            n++; bool c6 = DocAssistant.IsForget("nouveau sujet");
            if (c6) ok++; sb.Append(DiagLine(c6, "Oubli contextuel (« nouveau sujet »)"));
            sb.Append("\nBilan : ").Append(ok).Append('/').Append(n).Append(ok == n ? " garde-fous actifs. ✅ Système sain." : " — ⚠️ anomalie détectée.");
            sb.Append("\nAutres couches toujours en place : RAG (ta base de connaissances), vérification web, prompt structuré + exemples, citations, indicateur de fiabilité.");
            return sb.ToString();
        }

        // La question demande-t-elle un FAIT vérifiable (personne, marque, produit, lieu, date,
        // chiffre, définition d'entité…) ? Ce sont les sujets où un petit modèle local invente
        // avec aplomb → on force la vérification web, même si la réponse a l'air sûre. On reste
        // PRUDENT (on exclut bavardage, aide PC, opinions, créatif) pour ne pas web-chercher à tort.
        internal static bool IsFactualLookup(string q)
        {
            string n = Deaccent((q ?? "").ToLowerInvariant());
            if (n.Length == 0) return false;

            // Exclusions : créatif / opinion / how-to → aucune vérification factuelle utile.
            string[] notFactual =
            {
                "raconte", "blague", "invente", "imagine", "ecris", "redige", "traduis", "corrige",
                "ton avis", "tu penses", "tu preferes", "que penses tu", "donne moi une idee",
                "propose moi", "comment faire", "comment je", "comment optimiser", "comment ameliorer",
                "comment regler", "comment booster"
            };
            foreach (string x in notFactual) if (n.Contains(x)) return false;

            // Marqueurs de RECHERCHE DE FAIT / D'ENTITÉ.
            string[] cues =
            {
                "qui est", "c'est qui", "cest qui", "qui sont", "qui etait", "qui a invente",
                "qui a cree", "qui a ecrit", "qui a realise", "qui a fonde", "qui joue dans",
                "info sur", "infos sur", "renseigne", "biographie", "bio de", "parle moi de",
                "parle-moi de", "presente moi", "presente-moi", "c'est quoi que", "qu'est ce que c'est que",
                "date de", "quand est", "quand a ", "quand sort", "quand sortira", "en quelle annee",
                "quelle annee", "population de", "capitale de", "capitale du", "combien coute",
                "combien mesure", "combien d'habitants", "quel est le prix", "quelle est la hauteur",
                "quelle est la taille", "record du", "record de", "auteur de", "createur de",
                "createur du", "fondateur de", "specifications de", "caracteristiques de",
                "fiche technique", "ou se trouve", "ou est situe", "de quel pays", "nationalite de",
                "quel age a", "age de "
            };
            foreach (string c in cues) if (n.Contains(c)) return true;

            // Deux mots (ou plus) qui se suivent en Majuscule dans la question ORIGINALE = très
            // probablement un nom propre (personne, marque, jeu) → recherche d'entité, quel que
            // soit le phrasé. Ex. « Clio Williams », « Grand Theft Auto ». (≥3 lettres → écarte « Je Suis ».)
            try
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(q ?? "",
                    @"\b\p{Lu}\p{Ll}{2,}\s+\p{Lu}\p{Ll}{2,}"))
                    return true;
            }
            catch { }
            return false;
        }

        // La RÉPONSE assène-t-elle un fait daté précis (« né en 1997 », « fondée en 2010 »,
        // « sorti en 2013 ») ? Sur une réponse NON ancrée, c'est le marqueur d'une invention
        // confiante — même quand la question semblait anodine. Volontairement TRÈS étroit (une
        // ANNÉE explicite « en 18xx/19xx/20xx ») pour ne jamais s'alarmer des chiffres techniques
        // légitimes d'un dépannage PC (« 1000 Hz », « 16 Go », « 30 ms », « 144 Hz »).
        internal static bool AnswerHasHardFact(string ans)
        {
            string n = Deaccent((ans ?? "").ToLowerInvariant());
            if (n.Length == 0) return false;
            try
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\ben\s+(1[89]\d{2}|20\d{2})\b"))
                    return true;   // « en 1971 », « en 2013 », « en 2024 » = affirmation historique/bio
            }
            catch { }
            return false;
        }

        // Minuscules sans accents — pour comparer du texte tapé « à la va-vite ».
        private static string Deaccent(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            try
            {
                string f = s.Normalize(System.Text.NormalizationForm.FormD);
                var sb = new StringBuilder(f.Length);
                foreach (char c in f)
                    if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
                        sb.Append(c);
                return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
            }
            catch { return s; }
        }

        // Le contexte donné au modèle : rôle, HONNÊTETÉ (dire ses doutes), capacités de l'app, état du PC.
        // Prompt système STRUCTURÉ (technique « structured prompting ») : sections claires plutôt
        // qu'un mur de phrases — un contexte bien organisé réduit le « lost in the middle ». Il se
        // termine par des EXEMPLES (few-shot) qui MONTRENT le bon comportement (admettre l'incertitude,
        // agir sur un souci PC, corriger un mythe) : plus efficace que de seulement l'expliquer.
        internal static string PromptSkeleton()
        {
            return
"## RÔLE\n" +
"Tu es « le Copilote » d'ONYX, un assistant polyvalent qui tourne 100 % en local sur le PC de l'utilisateur. Tu réponds à N'IMPORTE QUELLE question (PC, jeux, culture générale, aide, conseils).\n\n" +
"## RÈGLES D'OR (par ordre de priorité)\n" +
"1. HONNÊTETÉ : si tu n'es pas sûr, DIS-LE (« Je ne suis pas certain, mais… »). Une réponse « je ne sais pas » vaut mille fois mieux qu'un faux dit avec assurance.\n" +
"2. N'INVENTE JAMAIS un fait, un chiffre, une date, une biographie ou une mesure du PC. Si on te demande qui est une personne / une marque / un groupe que tu ne connais pas PRÉCISÉMENT, ne devine pas : dis-le et propose une recherche web.\n" +
"3. Évite les formules d'absolue certitude (« c'est sûr à 100 % », « sans aucun doute ») sur un fait non vérifié.\n" +
"4. COHÉRENCE : ne te contredis pas d'un message à l'autre.\n" +
"5. PREUVES : quand une base de connaissances ou une page web te sont fournies, tes affirmations factuelles viennent UNIQUEMENT d'elles — nomme la source. Si c'est de ta mémoire, dis-le.\n" +
"6. ACTUALITÉ/temps réel (match, météo, prix, news du jour) : tu ne la connais pas de tête → une recherche web sera lancée.\n" +
"7. Ne recommande JAMAIS de logiciel PAYANT : tout gratuit. Pour un OUTIL, dis (1) qu'il est gratuit, (2) ses risques/précautions, (3) le site officiel. Déconseille les « driver updaters » et « PC boosters » (arnaques).\n\n" +
"## MÉTHODE\n" +
"Avant de répondre, distingue en toi-même ce que tu SAIS de source sûre de ce que tu SUPPOSES ; n'affirme que le certain, présente le reste comme hypothèse (« probablement », « il me semble »).\n\n" +
"## FORMAT\n" +
"FRANÇAIS, ton direct et amical (tutoiement), 130 mots MAXIMUM. Pour un VRAI souci PC, rappelle que tu peux AGIR : « fais un bilan complet », « mesure mon ping », « qui bouffe mon cpu », « mesure ma latence », « prépare ma partie », « génère le rapport », « libère de l'espace ».\n\n" +
"## EXEMPLES (imite exactement ce comportement)\n" +
"Q : c'est qui Jordan Kessler ?\n" +
"R : Franchement, je ne suis pas sûr de qui il s'agit — je préfère ne pas inventer une bio. Je vérifie sur le web ? Dis « cherche sur internet Jordan Kessler ».\n" +
"Q : mon jeu rame depuis hier\n" +
"R : On regarde ça direct. Dis « fais un bilan complet » : je mesure FPS, température, réseau et process gourmands, puis je te propose les réparations en 1 clic.\n" +
"Q : je désactive le pagefile pour gagner des FPS ?\n" +
"R : Non, surtout pas : désactiver le fichier d'échange fait planter les jeux gourmands (« out of memory »). Laisse-le géré par Windows — le gain FPS est un mythe.\n";
        }

        private static string BrainContext(BadgeCatalog.Stats st)
        {
            try { Memory.SeedHardwareOnce(); } catch { }   // renseigne CPU/RAM/GPU une fois → conseils adaptés
            var sb = new StringBuilder(PromptSkeleton());
            if (st != null)
                sb.Append("\n## ÉTAT RÉEL DU PC\nSanté ").Append(st.Health).Append(" %, ").Append(st.OptiActive).Append('/').Append(st.OptiTotal)
                  .Append(" optimisations actives, ").Append(st.GamesDet).Append(" jeu(x) détecté(s).\n");
            string mem = Memory.ForPrompt();
            if (!string.IsNullOrEmpty(mem)) sb.Append("\n").Append(mem).Append("Utilise ces infos pour personnaliser tes réponses (sans les répéter inutilement).\n");
            string vf = LocalBrain.VerifiedFactsBlock();   // faits sourcés déjà établis cette session → cohérence
            if (!string.IsNullOrEmpty(vf)) sb.Append("\n").Append(vf);
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        //  Rapport HTML — l'audit complet, généré depuis la conversation
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MakeReport()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Générer le rapport HTML (Bureau)";
            a.IsChange = true;   // écrit un fichier : action explicite, jamais automatique
            a.Warning = "Écrit l'audit complet (matériel, toutes les optimisations, diagnostic santé) en HTML sur le "
                      + "Bureau, puis l'ouvre. Ne modifie RIEN au système. ~10-20 secondes.";
            a.Run = delegate (Action<string, int> log)
            {
                try
                {
                    string html = Report.BuildHtml(Catalog.All(), Hardware.Detect());
                    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        "ONYX-rapport-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".html");
                    File.WriteAllText(path, html, new System.Text.UTF8Encoding(false));
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
                    return Say("📄 Rapport écrit sur le Bureau : " + Path.GetFileName(path)
                             + "\nOuvrable dans le navigateur, imprimable en PDF — le livrable avant/après idéal.");
                }
                catch (Exception ex) { return Say("Le rapport n'a pas pu être généré : " + ex.Message); }
            };
            return a;
        }


        // Lancements auto non essentiels connus (gaming/fond) — coupables SANS risque : l'appli
        // reste installée, se lance à la main, et le panneau Démarrage peut tout remettre.
        private static readonly string[] StartupSafeCut =
        {
            "steam", "epic", "discord", "spotify", "riot", "gog", "galaxy", "ubisoft", "ea desktop",
            "battle.net", "battlenet", "wallpaper", "lively", "medal", "overwolf", "megasync"
        };

        /// <summary>Coupe au démarrage les lanceurs/apps de fond CONNUS et sans risque (liste
        /// blanche stricte) — null si aucun ne se lance au boot.</summary>
        public static DocAssistant.ChatAction FixStartup()
        {
            List<Sys.StartupEntry> hit = StartupCuttable();
            if (hit == null || hit.Count == 0) return null;
            var names = new List<string>(); foreach (var e in hit) names.Add(e.Name);
            var a = new DocAssistant.ChatAction();
            a.Label = "Couper " + hit.Count + " lancement(s) auto non essentiel(s)";
            a.IsChange = true;
            a.Warning = "Désactive au démarrage : " + string.Join(", ", names.ToArray()) + ". Les applis restent "
                      + "installées et lançables à la main — réversible dans « Programmes au démarrage ».";
            a.Run = delegate (Action<string, int> log)
            {
                int okN = 0;
                var sb = new StringBuilder();
                foreach (var e in StartupCuttable())   // re-listé au moment du clic
                {
                    try { Sys.SetStartupEnabled(e, false); okN++; sb.Append("✅ ").Append(e.Name).Append('\n'); }
                    catch { sb.Append("⚠ ").Append(e.Name).Append(" : refusé\n"); }
                }
                if (okN == 0) return Say("Rien n'a pu être coupé (déjà fait ?).");
                sb.Append('\n').Append(okN).Append(" lancement(s) auto coupé(s) — allumage plus rapide, moins de poids en fond. Réversible à tout moment.");
                return Say(sb.ToString().TrimEnd());
            };
            return a;
        }

        private static List<Sys.StartupEntry> StartupCuttable()
        {
            var hit = new List<Sys.StartupEntry>();
            try
            {
                foreach (var e in Sys.ListStartup())
                {
                    if (!e.Enabled) continue;
                    string hay = ((e.Name ?? "") + " " + (e.Command ?? "")).ToLowerInvariant();
                    foreach (string k in StartupSafeCut) if (hay.Contains(k)) { hit.Add(e); break; }
                }
            }
            catch { }
            return hit;
        }

        /// <summary>Programme un VRAI redémarrage dans 60 s (annulable) — purge pilotes et fuites.
        /// Jamais enchaîné dans « TOUT réparer » (NoChain).</summary>
        public static DocAssistant.ChatAction RestartAction()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Redémarrer le PC (dans 60 s, annulable)";
            a.IsChange = true; a.NoChain = true;
            a.Warning = "Programme un redémarrage COMPLET dans 60 secondes — enregistre ton travail. Un bouton d'annulation apparaîtra.";
            a.Run = delegate (Action<string, int> log)
            {
                try { Sys.Run(Sys.Sys32("shutdown.exe"), "/r /t 60 /c \"ONYX : vrai redemarrage demande au Copilote (annulable)\""); }
                catch (Exception ex) { return Say("Impossible de programmer le redémarrage : " + ex.Message); }
                var r = Say("⏳ Redémarrage complet dans 60 secondes — enregistre ton travail.\nPour annuler, clique ci-dessous.");
                var cancel = new DocAssistant.ChatAction();
                cancel.Label = "Annuler le redémarrage"; cancel.IsChange = true; cancel.NoChain = true;
                cancel.Warning = "Annule le redémarrage programmé — rien d'autre ne change.";
                cancel.Run = delegate (Action<string, int> log2)
                {
                    try { Sys.Run(Sys.Sys32("shutdown.exe"), "/a"); } catch { }
                    return Say("Redémarrage annulé. Pense à le faire à l'occasion — c'est gratuit et ça purge beaucoup de soucis.");
                };
                r.Action = cancel;
                return r;
            };
            return a;
        }

        /// <summary>Installe UN outil gratuit du catalogue par winget, depuis le chat.
        /// Null s'il est déjà installé.</summary>
        public static DocAssistant.ChatAction InstallTool(string wingetId, string label)
        {
            try { if (LibScan.InstalledById(wingetId)) return null; } catch { }
            var a = new DocAssistant.ChatAction();
            a.Label = "Installer " + label + " (gratuit)";
            a.IsChange = true;
            a.Warning = "Installe " + label + " via winget (éditeur officiel, gratuit). Quelques minutes.";
            a.Run = delegate (Action<string, int> log)
            {
                string winget = LibScan.WingetPath();
                if (winget == null) return Say("winget est introuvable : installe « App Installer » (gratuit, Microsoft Store) puis reviens.");
                LibScan.LibItem item = null;
                try { foreach (var it in LibScan.Items()) if (string.Equals(it.WingetId, wingetId, StringComparison.OrdinalIgnoreCase)) { item = it; break; } }
                catch { }
                if (item == null) return Say("Je ne connais pas cet outil dans le catalogue.");
                bool done = false; try { done = LibScan.Install(winget, item, log); } catch { }
                return Say(done
                    ? "✅ " + item.Name + " installé — double-clic dessus dans 📦 Bibliothèques pour l'ouvrir."
                    : "⚠ L'installation de " + item.Name + " a échoué (voir journal). Réessaie plus tard.");
            };
            return a;
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
                bool bad = (curMs < 0 && bestMs >= 0) || (curMs >= 0 && bestMs >= 0 && curMs > bestMs * 2 && curMs - bestMs >= 15);
                if (curMs < 0 && bestMs >= 0)
                    sb.Append("⚠ Ton DNS ne répond pas alors qu'internet marche : je peux le basculer, c'est gratuit et immédiat.");
                else if (bad)
                    sb.Append("⚠ Ton DNS traîne : chaque site, chaque boutique en jeu attend cette traduction. "
                            + "Je peux le basculer vers le plus rapide — gratuit et réversible.");
                else
                    sb.Append("✅ Ton DNS répond bien — rien à gagner de ce côté.");
                var r = Say(sb.ToString().TrimEnd());
                if (bad && bestName != null)
                    r.Action = FixDns(bestName, bestName.StartsWith("Google") ? new[] { "8.8.8.8", "8.8.4.4" }
                                                                              : new[] { "1.1.1.1", "1.0.0.1" });
                return r;
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
                                           + "Je peux couper les non essentiels connus — gratuit, réversible, sans rien désinstaller.");
                else if (on.Count <= 3) sb.Append("✅ Démarrage léger — rien à couper d'urgence.");
                else sb.Append("Raisonnable. Tu peux quand même couper ceux que tu n'utilises pas tous les jours (réversible).");
                var r = Say(sb.ToString().TrimEnd());
                if (on.Count >= 8) r.Action = FixStartup();    // null si aucun connu à couper
                return r;
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
                var r = Say(sb.ToString().TrimEnd());
                if (gpu > 0) r.Action = InstallTool("Wagnardsoft.DisplayDriverUninstaller", "DDU");   // null si déjà là
                return r;
            };
            return a;
        }

        // ------------------------------------------------------------------
        //  CAUSE EXACTE d'un crash — quel que soit l'app/jeu
        // ------------------------------------------------------------------
        /// <summary>Analyse les crashs récents et donne la CAUSE EXACTE (module fautif + code
        /// d'exception → diagnostic) pour l'app nommée, ou pour l'app qui plante le plus.
        /// Attache le bon correctif gratuit, et peut enrichir un module inconnu via le web.</summary>
        public static DocAssistant.ChatAction AnalyseCrash(string appHint, BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Analyse des crashs"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                if (log != null) log("Analyse des crashs (journal Windows, 14 j)…", 0);
                List<CrashInfo> all;
                try { all = CrashScan.RecentDetailed(14); } catch { all = null; }
                int gpuErr = 0; try { gpuErr = CrashScan.GpuDriverErrors(14); } catch { }

                if ((all == null || all.Count == 0))
                {
                    if (gpuErr > 0)
                        return Say("Aucun crash d'application relevé sur 14 jours, MAIS ton pilote graphique a signalé "
                                 + gpuErr + " erreur(s) — c'est la piste des freezes/écrans noirs. Répare le pilote proprement "
                                 + "(DDU, gratuit) et surveille la température.");
                    return Say("Bonne nouvelle : aucun crash d'application relevé dans le journal Windows sur 14 jours. "
                             + "Si un jeu a planté sans laisser de trace, dis-moi précisément lequel et quand.");
                }

                // Choix de l'app à analyser : celle nommée, sinon celle qui plante le plus (récente en cas d'égalité).
                var byExe = new Dictionary<string, List<CrashInfo>>(StringComparer.OrdinalIgnoreCase);
                foreach (var ci in all)
                {
                    string key = ci.Exe;
                    if (!byExe.ContainsKey(key)) byExe[key] = new List<CrashInfo>();
                    byExe[key].Add(ci);
                }
                List<CrashInfo> target = null; string exeName = null;
                if (!string.IsNullOrEmpty(appHint))
                {
                    string hint = appHint.ToLowerInvariant();
                    foreach (var kv in byExe)
                        if (kv.Key.ToLowerInvariant().Contains(hint)) { target = kv.Value; exeName = kv.Key; break; }
                    if (target == null)
                        return Say("Je ne trouve aucun crash de « " + appHint + " » dans le journal des 14 derniers jours. "
                                 + "Il a peut-être planté sans erreur enregistrée, ou sous un autre nom d'exe. Dis « analyse mes crashs » pour voir tout ce qui a planté.");
                }
                else
                {
                    int best = 0;
                    foreach (var kv in byExe) if (kv.Value.Count > best) { best = kv.Value.Count; target = kv.Value; exeName = kv.Key; }
                }

                // Crash le plus récent de cette app = représentatif.
                CrashInfo top = target[0];
                foreach (var ci in target) if (ci.Time > top.Time) top = ci;

                var diag = CrashAnalyzer.FromModule(top.Exe, top.Module, top.Code);
                string meaning = CrashAnalyzer.CodeMeaning(top.Code);

                var sb = new StringBuilder();
                sb.Append("🔎 « ").Append(exeName).Append(" » a planté ").Append(target.Count)
                  .Append(target.Count > 1 ? " fois" : " fois").Append(" ces 14 jours (dernier : ")
                  .Append(top.Time.ToString("dd/MM à HH:mm")).Append(").\n\n");
                if (top.Hang)
                    sb.Append("C'était un BLOCAGE (appli figée) : souvent une attente sur le réseau, le disque ou un périphérique.\n");
                else
                {
                    if (!string.IsNullOrEmpty(top.Module)) sb.Append("• Module fautif : ").Append(top.Module).Append('\n');
                    if (!string.IsNullOrEmpty(top.Code)) sb.Append("• Code : ").Append(top.Code).Append(string.IsNullOrEmpty(meaning) ? "" : " — " + meaning).Append('\n');
                    sb.Append("\n➡ Cause probable : ").Append(diag.Cause).Append(".\n");
                }
                sb.Append("💡 Remède gratuit : ").Append(diag.Remedy);

                var r = Say(sb.ToString());
                r.Action = FixForCrash(diag.Fix);   // bouton adapté (DDU, VC++, DISM/SFC…) ou null

                // Module inconnu + IA + web → on va chercher ce module/ce code sur le web pour préciser.
                if (!diag.Known && !top.Hang && !string.IsNullOrEmpty(top.Module) && LocalBrain.Enabled && !LocalBrain.WebOff())
                {
                    var web = new DocAssistant.ChatAction();
                    web.Label = "Chercher « " + top.Module + " » sur le web"; web.AutoRun = false; web.IsChange = false;
                    string query = top.Exe + " " + top.Module + " " + top.Code + " crash fix";
                    web.Run = delegate (Action<string, int> log2) { return WebAnswer(query, st).Run(log2); };
                    r.Action = web;   // remplace le fix générique par la recherche ciblée
                }
                return r;
            };
            return a;
        }

        /// <summary>Correctif câblé selon la cause du crash (clé de CrashAnalyzer.Diag.Fix).
        /// « ram / game / anticheat » n'ont pas de correctif 1 clic — le remède est dans le texte.</summary>
        private static DocAssistant.ChatAction FixForCrash(string kind)
        {
            switch (kind)
            {
                case "libs": return FixLibs();                                          // runtimes VC++/DirectX/.NET manquants
                case "gpu": return InstallTool("Wagnardsoft.DisplayDriverUninstaller", "DDU");
                case "system": return RepairWindows();                                  // DISM + SFC
                default: return null;
            }
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
                try { Sys.CreateRestorePoint("ONYX — avant modification", log); }
                catch { return Say("Le point de restauration n'a pas pu être créé (la restauration système est peut-être désactivée)."); }
                return Say("✅ Point de restauration créé. Tu peux manipuler l'esprit tranquille : Windows sait revenir ici.");
            };
            return a;
        }
    }
}
