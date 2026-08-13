using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// 🛠 Réparation des installations de jeux (launchers) : diagnostique et corrige les causes
    /// connues des échecs d'installation / mise à jour — EA app (INST-3-xxxx), Steam (« erreur
    /// d'écriture disque »), Epic, Battle.net. Chaque point est indépendant, idempotent et sans
    /// danger : on ne vide que des CACHES régénérés automatiquement, jamais tes jeux.
    /// </summary>
    internal static class LauncherFix
    {
        /// <summary>Un point de contrôle : état détecté + réparation associée.</summary>
        internal class Item
        {
            public string Id;
            public string Name;
            public string Status;
            public bool Problem;        // ⚠ cause probable détectée
            public bool DefaultCheck;   // pré-coché
            public Action<Action<string, int>> Fix;
        }

        // Marge conseillée sur le disque système : les launchers y déposent des fichiers
        // temporaires même quand le jeu s'installe ailleurs.
        private const double SystemFreeWarnGB = 25.0;

        public static List<Item> Analyze()
        {
            var list = new List<Item>();
            list.Add(SystemDiskSpace());
            list.Add(TargetDisksSpace());
            list.Add(LauncherServices());
            list.Add(EaCache());
            list.Add(EpicCache());
            list.Add(SteamCache());
            list.Add(BattleNetCache());
            return list;
        }

        // ------------------------------------------------------------ 1. espace disque système
        private static Item SystemDiskSpace()
        {
            double freeGB = FreeGB(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
            bool bad = freeGB >= 0 && freeGB < SystemFreeWarnGB;
            return new Item
            {
                Id = "sys_space",
                Name = "Espace libre sur le disque système",
                Problem = bad,
                DefaultCheck = bad,
                Status = freeGB < 0 ? "indéterminé"
                    : bad ? "CRITIQUE — " + freeGB.ToString("0.0") + " Go libres (< " + SystemFreeWarnGB + " Go). "
                          + "Les launchers échouent souvent à installer quand le disque système est saturé, "
                          + "même si le jeu va sur un autre disque."
                        : freeGB.ToString("0.0") + " Go libres (bon)",
                Fix = delegate(Action<string, int> log)
                {
                    long mb = 0;
                    // Faire de la place pour un launcher ne justifie pas d'effacer l'historique
                    // Copilot/Recall (genre "ia") : ça se coche à la main dans Nettoyage disque.
                    foreach (Sys.CleanTarget t in Sys.CleanTargets())
                        if (t.Kind != "ia") mb += Sys.CleanTargetNow(t, log);
                    double after = FreeGB(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
                    log("Nettoyage : ~" + Math.Max(0, mb) + " Mo récupérés — désormais "
                        + after.ToString("0.0") + " Go libres.", 1);
                    if (after < SystemFreeWarnGB)
                        log("Toujours sous " + SystemFreeWarnGB + " Go : les temporaires ne suffisent pas. "
                          + "Désinstalle des jeux/logiciels du disque système ou déplace-les.", 2);
                }
            };
        }

        // ------------------------------------------------------------ 2. espace des autres disques
        private static Item TargetDisksSpace()
        {
            var tight = new List<string>();
            var ok = new List<string>();
            foreach (DriveInfo d in SafeDrives())
            {
                double f = d.TotalFreeSpace / 1073741824.0;
                string s = d.Name.TrimEnd('\\') + " " + f.ToString("0") + " Go";
                if (f < 15) tight.Add(s); else ok.Add(s);
            }
            return new Item
            {
                Id = "target_space",
                Name = "Espace libre sur les disques de jeux",
                Problem = tight.Count > 0,
                DefaultCheck = false,
                Status = tight.Count > 0
                    ? "serré sur : " + string.Join(" · ", tight.ToArray()) + "   (ok : " + string.Join(" · ", ok.ToArray()) + ")"
                    : "ok — " + string.Join(" · ", ok.ToArray()),
                Fix = delegate(Action<string, int> log)
                {
                    log("Choisis un disque avec assez de place dans ton launcher (rien à réparer automatiquement ici).", 0);
                }
            };
        }

        // ------------------------------------------------------------ 3. services des launchers
        private static readonly string[] LauncherSvc = { "EABackgroundService", "Steam Client Service", "BcmSvc", "Battle.net Update Agent" };

        private static Item LauncherServices()
        {
            var disabled = new List<string>();
            var present = new List<string>();
            foreach (string s in LauncherSvc)
            {
                int st = SafeServiceStart(s);
                if (st < 0) continue;               // service absent
                present.Add(s);
                if (st == 4) disabled.Add(s);       // 4 = désactivé
            }
            return new Item
            {
                Id = "svc",
                Name = "Services des launchers (EA, Steam, Battle.net)",
                Problem = disabled.Count > 0,
                DefaultCheck = disabled.Count > 0,
                Status = present.Count == 0 ? "aucun service launcher installé"
                    : disabled.Count > 0
                        ? "DÉSACTIVÉ(S) : " + string.Join(", ", disabled.ToArray()) + " — le launcher ne pourra pas installer."
                        : "présents et non désactivés (bon) : " + string.Join(", ", present.ToArray()),
                Fix = delegate(Action<string, int> log)
                {
                    foreach (string s in disabled)
                    {
                        try
                        {
                            Sys.Run(Sys.Sys32("sc.exe"), "config \"" + s + "\" start= demand");
                            log("Service « " + s + " » remis en démarrage manuel (le launcher le lancera au besoin).", 1);
                        }
                        catch (Exception ex) { log("Service « " + s + " » : " + ex.Message, 3); }
                    }
                }
            };
        }

        // ------------------------------------------------------------ 4. cache EA app
        // LA cause classique des INST-3-xxxx : cache/session EA corrompus. Régénérés au lancement.
        private static string[] EaCacheDirs()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string ea = Path.Combine(local, @"Electronic Arts\EA Desktop");
            return new[]
            {
                Path.Combine(ea, "CEF"), Path.Combine(ea, "IGOCache"), Path.Combine(ea, "OfflineCache"),
                Path.Combine(ea, "cloudsync"), Path.Combine(ea, "UIC"), Path.Combine(ea, "Logs")
            };
        }

        private static Item EaCache()
        {
            bool any = false;
            foreach (string d in EaCacheDirs()) if (Directory.Exists(d)) { any = true; break; }
            return new Item
            {
                Id = "ea_cache",
                Name = "Cache EA app (erreurs INST-3-xxxx)",
                Problem = false,
                DefaultCheck = false,
                Status = any ? "présent — à vider si l'EA app échoue à installer/réparer (régénéré tout seul)"
                             : "EA app non installée",
                Fix = delegate(Action<string, int> log)
                {
                    int n = 0;
                    foreach (string d in EaCacheDirs()) n += WipeDir(d);
                    log("Cache EA app vidé (" + n + " éléments). Relance l'EA app : elle se reconstruit "
                      + "et te redemandera peut-être ta connexion.", 1);
                }
            };
        }

        // ------------------------------------------------------------ 5. cache Epic
        private static Item EpicCache()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(local, @"EpicGamesLauncher\Saved");
            bool exists = Directory.Exists(dir);
            return new Item
            {
                Id = "epic_cache",
                Name = "Cache Epic Games Launcher",
                Problem = false,
                DefaultCheck = false,
                Status = exists ? "présent — à vider en cas d'erreur d'installation Epic" : "Epic non installé",
                Fix = delegate(Action<string, int> log)
                {
                    int n = WipeDir(Path.Combine(dir, "webcache")) + WipeDir(Path.Combine(dir, "webcache_4147"))
                          + WipeDir(Path.Combine(dir, "webcache_4430")) + WipeDir(Path.Combine(dir, "Logs"));
                    log("Cache Epic vidé (" + n + " éléments).", 1);
                }
            };
        }

        // ------------------------------------------------------------ 6. cache Steam
        private static Item SteamCache()
        {
            string root = null;
            try { root = GameScan.SteamRoot(); } catch { }
            bool exists = root != null && Directory.Exists(root);
            return new Item
            {
                Id = "steam_cache",
                Name = "Cache Steam (« erreur d'écriture disque »)",
                Problem = false,
                DefaultCheck = false,
                Status = exists ? "présent — à vider si Steam refuse d'installer/mettre à jour" : "Steam non détecté",
                Fix = delegate(Action<string, int> log)
                {
                    if (!exists) { log("Steam non détecté.", 2); return; }
                    int n = WipeDir(Path.Combine(root, @"appcache\httpcache")) + WipeDir(Path.Combine(root, "htmlcache"));
                    log("Cache Steam vidé (" + n + " éléments). Ferme puis rouvre Steam.", 1);
                }
            };
        }

        // ------------------------------------------------------------ 7. cache Battle.net
        private static Item BattleNetCache()
        {
            string data = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dir = Path.Combine(data, @"Battle.net\Cache");
            bool exists = Directory.Exists(Path.Combine(data, "Battle.net"));
            return new Item
            {
                Id = "bnet_cache",
                Name = "Cache Battle.net (mises à jour bloquées)",
                Problem = false,
                DefaultCheck = false,
                Status = exists ? "présent — à vider si Battle.net boucle sur une mise à jour" : "Battle.net non installé",
                Fix = delegate(Action<string, int> log)
                {
                    int n = WipeDir(dir);
                    log("Cache Battle.net vidé (" + n + " éléments). Relance Battle.net.", 1);
                }
            };
        }

        // ------------------------------------------------------------ utilitaires
        private static double FreeGB(string root)
        {
            try { return new DriveInfo(root).TotalFreeSpace / 1073741824.0; }
            catch { return -1; }
        }

        private static IEnumerable<DriveInfo> SafeDrives()
        {
            DriveInfo[] all;
            try { all = DriveInfo.GetDrives(); }
            catch { yield break; }
            foreach (DriveInfo d in all)
            {
                bool ok = false;
                try { ok = d.DriveType == DriveType.Fixed && d.IsReady; } catch { }
                if (ok) yield return d;
            }
        }

        private static int SafeServiceStart(string name)
        {
            try { return Sys.GetServiceStart(name); }
            catch { return -1; }
        }

        /// <summary>Vide le contenu d'un dossier (best-effort). Renvoie le nombre d'éléments supprimés.</summary>
        private static int WipeDir(string dir)
        {
            int n = 0;
            try
            {
                if (!Directory.Exists(dir)) return 0;
                foreach (string f in Directory.GetFiles(dir))
                    try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); n++; } catch { }
                foreach (string sub in Directory.GetDirectories(dir))
                {
                    n += WipeDir(sub);
                    try { Directory.Delete(sub, false); } catch { }
                }
            }
            catch { }
            return n;
        }
    }
}
