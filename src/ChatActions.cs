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
                try { Sys.CreateRestorePoint("Fluide — TOUT réparer (Copilote)", log); sb.Append("🛟 Point de restauration créé.\n\n"); }
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
                try { Sys.Run(exe, "pull " + pick.Tag); }
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
                try { ans = LocalBrain.Ask(q, BrainContext(st), model); }
                catch (Exception ex) { return Say("L'IA locale a calé : " + ex.Message); }
                if (string.IsNullOrEmpty(ans))
                    return Say("Là, honnêtement, je sèche — reformule, ou pose-moi un souci PC : c'est mon terrain, j'y suis imbattable.");
                return Say(ans.Trim() + "\n\n— 🧠 IA locale (" + model + "), 100 % sur ta machine, gratuit.");
            };
            return a;
        }

        // Le contexte donné au modèle : rôle, HONNÊTETÉ (dire ses doutes), capacités de l'app, état du PC.
        private static string BrainContext(BadgeCatalog.Stats st)
        {
            var sb = new StringBuilder();
            sb.Append("Tu es « le Copilote » de Fluide, un assistant polyvalent qui tourne 100 % en local sur le PC de l'utilisateur. ");
            sb.Append("Réponds à N'IMPORTE QUELLE question (PC, jeux, culture générale, aide, conseils…), en FRANÇAIS, ton direct et amical (tutoiement), 130 mots MAXIMUM. ");
            sb.Append("HONNÊTETÉ AVANT TOUT : si tu n'es pas sûr, DIS-LE clairement (« Je ne suis pas certain, mais… », « À vérifier »). ");
            sb.Append("N'invente JAMAIS un fait, un chiffre, une date ou une mesure du PC : mieux vaut admettre « je ne sais pas » qu'affirmer du faux. ");
            sb.Append("Tu n'as PAS accès à internet ni à l'heure réelle, la météo ou l'actualité du jour : dis-le si on te le demande, et propose ce que tu peux faire à la place. ");
            sb.Append("Tes connaissances peuvent être incomplètes ou datées — signale-le sur les sujets pointus ou récents. ");
            sb.Append("Ne recommande JAMAIS de logiciel payant : tout doit rester gratuit. ");
            sb.Append("Pour un VRAI souci PC, rappelle que tu peux AGIR via ces phrases : « fais un bilan complet » (enquête + réparations 1 clic), ");
            sb.Append("« mesure mon ping », « qui bouffe mon cpu », « mesure ma latence », « prépare ma partie », « génère le rapport », « libère de l'espace ». ");
            if (st != null)
                sb.Append("État réel du PC de l'utilisateur : santé " + st.Health + " %, " + st.OptiActive + "/" + st.OptiTotal
                        + " optimisations actives, " + st.GamesDet + " jeu(x) détecté(s). ");
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
                        "Fluide-rapport-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".html");
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
                try { Sys.Run(Sys.Sys32("shutdown.exe"), "/r /t 60 /c \"Fluide : vrai redemarrage demande au Copilote (annulable)\""); }
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
