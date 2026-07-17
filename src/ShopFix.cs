using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Boutiques &amp; contenu en jeu qui chargent à l'infini (Steam, Game Pass, boutiques
    /// intégrées) : diagnostic des causes connues côté Windows et réparations ciblées,
    /// idempotentes et sans danger. Chaque point est indépendant et journalisé.
    /// </summary>
    internal static class ShopFix
    {
        /// <summary>Un point de contrôle : état détecté + réparation associée.</summary>
        internal class Item
        {
            public string Id;
            public string Name;                        // libellé court (liste)
            public string Status;                      // état constaté à l'analyse
            public bool Problem;                       // ⚠ cause probable détectée
            public bool DefaultCheck;                  // pré-coché dans la liste
            public bool NeedReboot;                    // la réparation demande un redémarrage
            public bool ClosesSteam;                   // la réparation ferme Steam (à confirmer)
            public Action<Action<string, int>> Repair; // action de réparation (log niveau 0..3)
        }

        // Résolveurs DNS filtrants connus (anti-pub / anti-malware / familial) : un domaine
        // de boutique ou de CDN bloqué par le résolveur = page qui tourne à l'infini.
        private static readonly string[] FilteringDns =
        {
            "94.140.14.14", "94.140.15.15",            // AdGuard anti-pub
            "94.140.14.15", "94.140.15.16",            // AdGuard famille
            "1.1.1.2", "1.0.0.2", "1.1.1.3", "1.0.0.3",// Cloudflare anti-malware / famille
            "9.9.9.9", "149.112.112.112", "9.9.9.11"   // Quad9 (liste de blocage)
        };

        // Services nécessaires aux boutiques / licences / téléchargements du Microsoft Store
        // et de Windows (un seul désactivé peut suffire à bloquer une boutique en jeu).
        private static readonly string[][] StoreServices =
        {
            // nom, type de démarrage Windows par défaut
            new[] { "ClipSVC",        "demand" },       // licences des applis/jeux du Store
            new[] { "LicenseManager", "demand" },       // gestion des licences Windows
            new[] { "wlidsvc",        "demand" },       // connexion compte Microsoft
            new[] { "TokenBroker",    "demand" },       // jetons d'authentification web
            new[] { "InstallService", "demand" },       // installation des applis du Store
            new[] { "AppXSvc",        "demand" },       // déploiement des paquets AppX
            new[] { "BITS",           "demand" },       // transferts en arrière-plan
            new[] { "wuauserv",       "demand" },       // Windows Update (dépendance Store)
            new[] { "DoSvc",          "delayed-auto" }  // Optimisation de livraison (téléchargements Store/Game Pass)
        };

        private static readonly string[] XboxServices =
        {
            "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc"
        };

        // Domaines de boutiques/lanceurs : une entrée hosts qui les envoie vers
        // 0.0.0.0 / 127.0.0.1 (reste d'un guide « anti-pub/anti-télémétrie ») bloque tout.
        private static readonly string[] StoreDomainTokens =
        {
            "steampowered", "steamstatic", "steamcommunity", "steamcontent", "steamserver",
            "epicgames", "unrealengine", "xboxlive", "xbox.com", "microsoft.com", "msftconnecttest",
            "windowsupdate", "akamai", "riotgames", "rockstargames", "ea.com", "origin.com",
            "battle.net", "blizzard", "ubisoft", "ubi.com", "playstation", "nintendo"
        };

        // ------------------------------------------------------------------
        //  Analyse
        // ------------------------------------------------------------------
        public static List<Item> Analyze()
        {
            var items = new List<Item>();
            items.Add(CheckDnsFilter());
            items.Add(CheckHosts());
            items.Add(CheckStoreServices());
            items.Add(CheckXboxServices());
            items.Add(CheckBackgroundApps());
            items.Add(CheckDeliveryPolicy());
            items.Add(CheckStorePolicy());
            items.Add(CheckIpv6());
            items.Add(CheckProxy());
            items.Add(CheckTime());
            items.Add(CheckSteamCache());
            items.Add(FlushDnsItem());
            return items;
        }

        // --- 1. DNS filtrant -------------------------------------------------
        private static Item CheckDnsFilter()
        {
            var found = new List<string>();
            foreach (string s in CurrentDnsServers())
                if (Array.IndexOf(FilteringDns, s) >= 0 && !found.Contains(s)) found.Add(s);

            bool bad = found.Count > 0;
            return new Item
            {
                Id = "dns_filter",
                Name = "DNS filtrant (anti-pub / anti-malware)",
                Problem = bad, DefaultCheck = bad,
                Status = bad
                    ? "détecté : " + string.Join(", ", found.ToArray()) + " — peut bloquer boutiques et CDN"
                    : "aucun résolveur filtrant détecté",
                Repair = delegate(Action<string, int> log)
                {
                    Sys.SetDns(null, log); // retour automatique (DHCP) + cache vidé
                }
            };
        }

        // --- 2. Fichier hosts ------------------------------------------------
        private static string HostsPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"drivers\etc\hosts");
        }

        private static bool IsBlockingHostsLine(string line)
        {
            string t = line.Trim();
            if (t.Length == 0 || t.StartsWith("#")) return false;
            string low = t.ToLowerInvariant();
            if (!(low.StartsWith("0.0.0.0") || low.StartsWith("127.0.0.1") || low.StartsWith("::1 ") || low.StartsWith(":: ")))
                return false;
            // 127.0.0.1 localhost & co : jamais considérés bloquants
            foreach (string token in StoreDomainTokens)
                if (low.IndexOf(token, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static Item CheckHosts()
        {
            int count = 0; string sample = null;
            try
            {
                foreach (string line in File.ReadAllLines(HostsPath()))
                    if (IsBlockingHostsLine(line)) { count++; if (sample == null) sample = line.Trim(); }
            }
            catch { }

            bool bad = count > 0;
            return new Item
            {
                Id = "hosts",
                Name = "Fichier hosts : domaines de boutiques bloqués",
                Problem = bad, DefaultCheck = bad,
                Status = bad
                    ? count + " ligne(s) bloquante(s), ex. « " + sample + " »"
                    : "aucun blocage de boutique dans hosts",
                Repair = delegate(Action<string, int> log)
                {
                    string path = HostsPath();
                    string[] lines;
                    try { lines = File.ReadAllLines(path); }
                    catch (Exception ex) { log("hosts illisible : " + ex.Message, 3); return; }

                    try { File.Copy(path, path + ".destingood.bak", true); }
                    catch (Exception ex) { log("Sauvegarde hosts impossible : " + ex.Message, 3); return; }

                    int fixedCount = 0;
                    for (int i = 0; i < lines.Length; i++)
                        if (IsBlockingHostsLine(lines[i]))
                        {
                            lines[i] = "# [DesTinGOOD boutiques] " + lines[i];
                            fixedCount++;
                        }
                    if (fixedCount == 0) { log("hosts : rien à corriger.", 0); return; }
                    try
                    {
                        File.WriteAllLines(path, lines);
                        Sys.FlushDns();
                        log("hosts : " + fixedCount + " ligne(s) neutralisée(s) (sauvegarde hosts.destingood.bak), cache DNS vidé.", 1);
                    }
                    catch (Exception ex) { log("Écriture hosts impossible : " + ex.Message, 3); }
                }
            };
        }

        // --- 3. Services boutique / licences --------------------------------
        private static Item CheckStoreServices()
        {
            var off = new List<string>();
            foreach (string[] svc in StoreServices)
                if (Sys.GetServiceStart(svc[0]) == 4) off.Add(svc[0]);

            bool bad = off.Count > 0;
            return new Item
            {
                Id = "svc_store",
                Name = "Services Boutique / licences désactivés",
                Problem = bad, DefaultCheck = bad,
                Status = bad
                    ? "désactivé(s) : " + string.Join(", ", off.ToArray())
                    : "tous en démarrage normal",
                Repair = delegate(Action<string, int> log)
                {
                    int n = 0;
                    foreach (string[] svc in StoreServices)
                    {
                        if (Sys.GetServiceStart(svc[0]) != 4) continue;
                        try { Sys.ConfigureService(svc[0], svc[1], false, false); n++; log("Service " + svc[0] + " rétabli (" + svc[1] + ").", 1); }
                        catch (Exception ex) { log("Service " + svc[0] + " : " + ex.Message, 2); }
                    }
                    if (n == 0) log("Services Boutique : rien à rétablir.", 0);
                }
            };
        }

        // --- 4. Services Xbox (Game Pass / Microsoft Store) ------------------
        private static Item CheckXboxServices()
        {
            var off = new List<string>();
            foreach (string svc in XboxServices)
                if (Sys.GetServiceStart(svc) == 4) off.Add(svc);

            bool bad = off.Count > 0;
            return new Item
            {
                Id = "svc_xbox",
                Name = "Services Xbox désactivés (connexion des jeux Store/Game Pass)",
                Problem = bad, DefaultCheck = bad,
                Status = bad
                    ? "désactivé(s) : " + string.Join(", ", off.ToArray())
                    : "tous en démarrage normal",
                Repair = delegate(Action<string, int> log)
                {
                    int n = 0;
                    foreach (string svc in XboxServices)
                    {
                        if (Sys.GetServiceStart(svc) != 4) continue;
                        try { Sys.ConfigureService(svc, "demand", false, false); n++; log("Service " + svc + " rétabli (manuel).", 1); }
                        catch (Exception ex) { log("Service " + svc + " : " + ex.Message, 2); }
                    }
                    if (n == 0) log("Services Xbox : rien à rétablir.", 0);
                }
            };
        }

        // --- 5. Applications UWP en arrière-plan -----------------------------
        private static Item CheckBackgroundApps()
        {
            bool bad = Sys.IntEquals(Sys.GetUser(
                @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled"), 1);
            return new Item
            {
                Id = "bg_apps",
                Name = "Applications en arrière-plan (UWP) coupées",
                Problem = bad, DefaultCheck = bad,
                Status = bad
                    ? "coupées globalement — les jeux Store/Game Pass peuvent mal se connecter"
                    : "autorisées (réglage Windows par défaut)",
                Repair = delegate(Action<string, int> log)
                {
                    Sys.DelUser(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled");
                    log("Applications en arrière-plan (UWP) réautorisées.", 1);
                }
            };
        }

        // --- 6. Politique Optimisation de livraison --------------------------
        private static Item CheckDeliveryPolicy()
        {
            object v = Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode");
            bool bad = (v is int);
            return new Item
            {
                Id = "do_policy",
                Name = "Téléchargements Store/Game Pass bridés (politique DODownloadMode)",
                Problem = bad, DefaultCheck = bad,
                Status = bad
                    ? "politique forcée (DODownloadMode=" + v + ") — installations/boutique Store parfois bloquées"
                    : "aucune politique forcée",
                Repair = delegate(Action<string, int> log)
                {
                    Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode");
                    log("Politique DODownloadMode retirée (comportement Windows par défaut).", 1);
                }
            };
        }

        // --- 7. Politique Microsoft Store ------------------------------------
        private static Item CheckStorePolicy()
        {
            object v = Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\WindowsStore", "AutoDownload");
            bool bad = (v is int);
            return new Item
            {
                Id = "store_policy",
                Name = "Mises à jour du Microsoft Store bloquées (politique AutoDownload)",
                Problem = bad, DefaultCheck = bad,
                Status = bad
                    ? "politique forcée (AutoDownload=" + v + ") — jeux Store jamais mis à jour = contenu en jeu qui refuse de charger"
                    : "aucune politique forcée",
                Repair = delegate(Action<string, int> log)
                {
                    Sys.DelMachine(@"SOFTWARE\Policies\Microsoft\WindowsStore", "AutoDownload");
                    log("Politique AutoDownload du Store retirée.", 1);
                }
            };
        }

        // --- 8. IPv6 bridé ----------------------------------------------------
        private static Item CheckIpv6()
        {
            object v = Sys.GetMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents");
            int val = (v is int) ? (int)v : 0;
            bool bad = val != 0;
            string detail = (val & 0x20) != 0 || (val & 0x10) != 0 || val == 0xFF
                ? "IPv6 largement désactivé (0x" + val.ToString("X") + ")"
                : "tunnels IPv6 coupés (0x" + val.ToString("X") + ")";
            return new Item
            {
                Id = "ipv6",
                Name = "IPv6 bridé (DisabledComponents)",
                Problem = bad, DefaultCheck = bad, NeedReboot = true,
                Status = bad
                    ? detail + " — certains jeux/boutiques en dépendent"
                    : "réglage Windows par défaut",
                Repair = delegate(Action<string, int> log)
                {
                    Sys.DelMachine(@"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters", "DisabledComponents");
                    log("IPv6 : valeur DisabledComponents retirée (défaut Windows). Redémarrage nécessaire.", 2);
                }
            };
        }

        // --- 9. Proxy système -------------------------------------------------
        private static Item CheckProxy()
        {
            object en = Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyEnable");
            object pac = Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "AutoConfigURL");
            bool bad = Sys.IntEquals(en, 1) || (pac is string && ((string)pac).Length > 0);
            return new Item
            {
                Id = "proxy",
                Name = "Proxy système actif (reste d'un VPN/outil)",
                Problem = bad, DefaultCheck = bad,
                Status = bad
                    ? "proxy ou script PAC configuré — les boutiques passent par un intermédiaire peut-être mort"
                    : "accès direct (aucun proxy)",
                Repair = delegate(Action<string, int> log)
                {
                    Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyEnable", 0, RegistryValueKind.DWord);
                    Sys.DelUser(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "AutoConfigURL");
                    Sys.Run(Sys.Sys32("netsh.exe"), "winhttp reset proxy");
                    log("Proxy système désactivé (WinINET + WinHTTP remis en accès direct).", 1);
                }
            };
        }

        // --- 10. Heure Windows (TLS) ------------------------------------------
        private static Item CheckTime()
        {
            bool disabled = Sys.GetServiceStart("W32Time") == 4;
            return new Item
            {
                Id = "time",
                Name = "Heure Windows (certificats TLS des boutiques)",
                Problem = disabled, DefaultCheck = disabled,
                Status = disabled
                    ? "service Temps Windows DÉSACTIVÉ — une horloge fausse casse le HTTPS"
                    : "service Temps Windows présent (resynchronisation possible)",
                Repair = delegate(Action<string, int> log)
                {
                    if (Sys.GetServiceStart("W32Time") == 4) Sys.ConfigureService("W32Time", "demand", false, false);
                    Sys.StartService("W32Time");
                    NativeResult r = Sys.Run(Sys.Sys32("w32tm.exe"), "/resync");
                    log(r.ExitCode == 0
                        ? "Heure Windows resynchronisée."
                        : "Resynchronisation de l'heure : code " + r.ExitCode + " (réessaie une fois connecté).",
                        r.ExitCode == 0 ? 1 : 2);
                }
            };
        }

        // --- 11. Cache web Steam ----------------------------------------------
        private static string SteamPath()
        {
            object p = Sys.GetUser(@"Software\Valve\Steam", "SteamPath");
            string s = p as string;
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Replace('/', '\\');
            return Directory.Exists(s) ? s : null;
        }

        private static List<string> SteamCacheDirs()
        {
            var dirs = new List<string>();
            string steam = SteamPath();
            if (steam != null)
            {
                dirs.Add(Path.Combine(steam, @"config\htmlcache"));
                dirs.Add(Path.Combine(steam, @"appcache\httpcache"));
            }
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            dirs.Add(Path.Combine(local, @"Steam\htmlcache"));
            return dirs;
        }

        private static long DirSizeMB(string dir)
        {
            long bytes = 0;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    try { bytes += new FileInfo(f).Length; } catch { }
            }
            catch { }
            return bytes / (1024 * 1024);
        }

        private static Item CheckSteamCache()
        {
            bool installed = SteamPath() != null;
            long mb = 0;
            if (installed)
                foreach (string d in SteamCacheDirs())
                    if (Directory.Exists(d)) mb += DirSizeMB(d);

            return new Item
            {
                Id = "steam_cache",
                Name = "Vider le cache web Steam (boutique / overlay / inventaire)",
                Problem = false,
                DefaultCheck = false,           // opt-in : ferme Steam (confirmé dans le panneau)
                ClosesSteam = true,
                Status = !installed
                    ? "Steam non détecté sur ce PC"
                    : mb + " Mo en cache — LE remède classique quand la boutique Steam/en jeu tourne à l'infini (Steam sera fermé)",
                Repair = delegate(Action<string, int> log)
                {
                    string steam = SteamPath();
                    if (steam == null) { log("Steam non détecté : rien à faire.", 0); return; }

                    if (IsSteamRunning())
                    {
                        log("Fermeture de Steam en douceur (les téléchargements reprendront à la réouverture)...", 0);
                        try { System.Diagnostics.Process.Start(Path.Combine(steam, "steam.exe"), "-shutdown"); }
                        catch (Exception ex) { log("Impossible de demander l'arrêt de Steam : " + ex.Message, 3); return; }
                        for (int i = 0; i < 30 && IsSteamRunning(); i++) System.Threading.Thread.Sleep(500);
                        if (IsSteamRunning()) { log("Steam ne s'est pas fermé : cache non vidé. Ferme Steam puis relance la réparation.", 2); return; }
                    }

                    long freed = 0; int done = 0;
                    foreach (string d in SteamCacheDirs())
                    {
                        if (!Directory.Exists(d)) continue;
                        long size = DirSizeMB(d);
                        try
                        {
                            Directory.Delete(d, true);
                            freed += size; done++;
                        }
                        catch (Exception ex) { log("Cache « " + d + " » : " + ex.Message, 2); }
                    }
                    log(done > 0
                        ? "Cache web Steam vidé (" + freed + " Mo). Steam le reconstruira au prochain lancement."
                        : "Aucun cache Steam à vider.", done > 0 ? 1 : 0);
                }
            };
        }

        private static bool IsSteamRunning()
        {
            try { return System.Diagnostics.Process.GetProcessesByName("steam").Length > 0; }
            catch { return false; }
        }

        // --- 12. Cache DNS ----------------------------------------------------
        private static Item FlushDnsItem()
        {
            return new Item
            {
                Id = "dns_cache",
                Name = "Vider le cache DNS (résolutions mémorisées)",
                Problem = false, DefaultCheck = true,
                Status = "instantané et sans risque — oublie les mauvaises résolutions mémorisées",
                Repair = delegate(Action<string, int> log)
                {
                    Sys.FlushDns();
                    log("Cache DNS vidé.", 1);
                }
            };
        }

        // ------------------------------------------------------------------
        //  Outils
        // ------------------------------------------------------------------
        private static List<string> CurrentDnsServers()
        {
            var servers = new List<string>();
            try
            {
                using (var mos = new ManagementObjectSearcher(
                    "SELECT DNSServerSearchOrder FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=true"))
                {
                    foreach (ManagementObject mo in mos.Get())
                    {
                        string[] dns = mo["DNSServerSearchOrder"] as string[];
                        if (dns == null) continue;
                        foreach (string s in dns)
                            if (!string.IsNullOrEmpty(s) && !servers.Contains(s)) servers.Add(s);
                    }
                }
            }
            catch { }
            return servers;
        }
    }
}
