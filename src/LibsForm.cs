using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Bibliothèques & applis de jeu : détecte en local (fichiers/registre, instantané)
    /// les runtimes que les jeux réclament (« vcruntime140.dll manquant »,
    /// « d3dx9_43.dll introuvable »…) et installe ce qui manque via WINGET, le
    /// gestionnaire de paquets OFFICIEL Microsoft — aucun téléchargement douteux.
    /// </summary>
    internal static class LibScan
    {
        internal class LibItem
        {
            public string Name;           // libellé court
            public string Why;            // à quoi ça sert côté jeux
            public string WingetId;       // identifiant winget exact
            public bool Essential;        // bibliothèque (pré-cochée si absente) vs appli optionnelle
            public Func<bool> Installed;  // détection locale rapide
        }

        private static string Sys32(string file) { return Path.Combine(Environment.SystemDirectory, file); }
        private static string WowDir(string file)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SysWOW64\" + file);
        }
        private static bool VcRuntimes(string arch)
        {
            return Sys.IntEquals(Sys.GetMachine(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\" + arch, "Installed"), 1);
        }

        public static List<LibItem> Items()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return new List<LibItem>
            {
                // ---- Bibliothèques indispensables aux jeux ----
                new LibItem { Name = "Visual C++ 2015-2022 (64 bits)", Essential = true, WingetId = "Microsoft.VCRedist.2015+.x64",
                    Why = "réclamé par la majorité des jeux récents (vcruntime140.dll)",
                    Installed = () => VcRuntimes("x64") },
                new LibItem { Name = "Visual C++ 2015-2022 (32 bits)", Essential = true, WingetId = "Microsoft.VCRedist.2015+.x86",
                    Why = "jeux/lanceurs 32 bits (vcruntime140.dll en SysWOW64)",
                    Installed = () => VcRuntimes("x86") },
                new LibItem { Name = "Visual C++ 2013 (64 + 32 bits)", Essential = true, WingetId = "Microsoft.VCRedist.2013.x64",
                    Why = "jeux 2013-2016 (msvcr120.dll)",
                    Installed = () => File.Exists(Sys32("msvcr120.dll")) },
                new LibItem { Name = "Visual C++ 2012 (64 + 32 bits)", Essential = true, WingetId = "Microsoft.VCRedist.2012.x64",
                    Why = "jeux 2012-2015 (msvcr110.dll)",
                    Installed = () => File.Exists(Sys32("msvcr110.dll")) },
                new LibItem { Name = "Visual C++ 2010 (64 + 32 bits)", Essential = true, WingetId = "Microsoft.VCRedist.2010.x64",
                    Why = "vieux jeux et mods (msvcr100.dll)",
                    Installed = () => File.Exists(Sys32("msvcr100.dll")) },
                new LibItem { Name = "DirectX runtime juin 2010 (d3dx9 / XAudio2)", Essential = true, WingetId = "Microsoft.DirectX",
                    Why = "TOUS les jeux DX9-DX11 d'avant 2015 (d3dx9_43.dll, xaudio2_7.dll, xinput1_3.dll)",
                    Installed = () => File.Exists(Sys32("d3dx9_43.dll")) && File.Exists(Sys32("xaudio2_7.dll")) },
                new LibItem { Name = ".NET Desktop Runtime 8", Essential = true, WingetId = "Microsoft.DotNet.DesktopRuntime.8",
                    Why = "lanceurs et outils de jeux écrits en .NET",
                    Installed = () =>
                    {
                        try
                        {
                            string d = Path.Combine(pf, @"dotnet\shared\Microsoft.WindowsDesktop.App");
                            return Directory.Exists(d) && Directory.GetDirectories(d, "8.*").Length > 0;
                        }
                        catch { return false; }
                    } },
                new LibItem { Name = "OpenAL (audio 3D)", Essential = true, WingetId = "CreativeTechnology.OpenAL",
                    Why = "audio de nombreux jeux (OpenAL32.dll)",
                    Installed = () => File.Exists(Sys32("OpenAL32.dll")) || File.Exists(WowDir("OpenAL32.dll")) },
                new LibItem { Name = ".NET Desktop Runtime 6", Essential = true, WingetId = "Microsoft.DotNet.DesktopRuntime.6",
                    Why = "beaucoup de lanceurs et d'outils de jeu (encore très répandu)",
                    Installed = () => DotNetDesktop(pf, "6.") },

                // ---- Runtimes optionnels (selon tes jeux) ----
                new LibItem { Name = ".NET Desktop Runtime 9", WingetId = "Microsoft.DotNet.DesktopRuntime.9",
                    Why = "jeux/outils tout récents en .NET 9",
                    Installed = () => DotNetDesktop(pf, "9.") },
                new LibItem { Name = "Java (Temurin 21 JRE)", WingetId = "EclipseAdoptium.Temurin.21.JRE",
                    Why = "Minecraft Java et jeux/mods en Java",
                    Installed = () => Directory.Exists(Path.Combine(pf, "Eclipse Adoptium")) },

                // ---- Applis utiles (jamais pré-cochées : ton choix) ----
                new LibItem { Name = "7-Zip (archives / mods)", WingetId = "7zip.7zip",
                    Why = "décompresser mods et packs (7z/rar) — libre et léger",
                    Installed = () => File.Exists(Path.Combine(pf, @"7-Zip\7z.exe")) },
                new LibItem { Name = "OBS Studio (clips / stream)", WingetId = "OBSProject.OBSStudio",
                    Why = "remplace Game DVR (coupé par l'optimiseur) pour clipper sans perte de FPS notable",
                    Installed = () => File.Exists(Path.Combine(pf, @"obs-studio\bin\64bit\obs64.exe")) },
                new LibItem { Name = "Discord", WingetId = "Discord.Discord",
                    Why = "vocal d'équipe",
                    Installed = () => Directory.Exists(Path.Combine(local, "Discord")) },
                new LibItem { Name = "Steam", WingetId = "Valve.Steam",
                    Why = "la plateforme de jeux PC de référence",
                    Installed = () => Sys.GetUser(@"Software\Valve\Steam", "SteamPath") != null || File.Exists(Path.Combine(pf86, @"Steam\steam.exe")) },
                new LibItem { Name = "Epic Games Launcher", WingetId = "EpicGames.EpicGamesLauncher",
                    Why = "Fortnite, Rocket League et les jeux gratuits Epic",
                    Installed = () => Directory.Exists(Path.Combine(pf86, @"Epic Games")) || Directory.Exists(Path.Combine(pf, @"Epic Games")) },
                new LibItem { Name = "MSI Afterburner (OC + overlay FPS)", WingetId = "Guru3D.Afterburner",
                    Why = "réglage GPU + overlay FPS/temp en jeu (avec RivaTuner)",
                    Installed = () => File.Exists(Path.Combine(pf86, @"MSI Afterburner\MSIAfterburner.exe")) },
                new LibItem { Name = "HWiNFO (surveillance matérielle)", WingetId = "REALiX.HWiNFO",
                    Why = "capteurs détaillés (températures, tensions, horloges)",
                    Installed = () => File.Exists(Path.Combine(pf, @"HWiNFO64\HWiNFO64.exe")) },
                new LibItem { Name = "CapFrameX (capture de frametimes)", WingetId = "CXWorld.CapFrameX",
                    Why = "mesure 1%/0,1% low façon labo pour comparer tes réglages",
                    Installed = () => Directory.Exists(Path.Combine(local, "CapFrameX")) },
                new LibItem { Name = "CrystalDiskInfo (santé SSD/HDD)", WingetId = "CrystalDewWorld.CrystalDiskInfo",
                    Why = "état S.M.A.R.T. de tes disques (usure, température)",
                    Installed = () => Directory.Exists(Path.Combine(pf, "CrystalDiskInfo")) || Directory.Exists(Path.Combine(pf86, "CrystalDiskInfo")) },
                new LibItem { Name = "Display Driver Uninstaller (DDU)", WingetId = "Wagnardsoft.DisplayDriverUninstaller",
                    Why = "désinstalle proprement un pilote GPU (crashs après mise à jour de pilote)",
                    Installed = () => false },
                new LibItem { Name = "PowerToys (utilitaires Windows)", WingetId = "Microsoft.PowerToys",
                    Why = "outils avancés (FancyZones, Awake pour éviter la veille en jeu…)",
                    Installed = () => Directory.Exists(Path.Combine(local, @"Microsoft\PowerToys")) || Directory.Exists(Path.Combine(pf, "PowerToys")) },
                new LibItem { Name = "Playnite (bibliothèque de jeux unifiée)", WingetId = "Playnite.Playnite",
                    Why = "regroupe Steam/Epic/GOG/Xbox dans une seule bibliothèque",
                    Installed = () => Directory.Exists(Path.Combine(local, "Playnite")) },

                // ---- Diagnostic, thermiques & test de stabilité (surveille et éprouve ton PC) ----
                new LibItem { Name = "Fan Control (courbes de ventilation)", WingetId = "Rem0o.FanControl",
                    Why = "pilote les ventilos selon la température CPU/GPU — le meilleur outil GRATUIT contre le throttling thermique et le bruit",
                    Installed = () => Uninstall("Fan Control") || WingetPkg("Rem0o.FanControl") || Directory.Exists(Path.Combine(local, "FanControl")) },
                new LibItem { Name = "CPU-Z (infos CPU / RAM / carte mère)", WingetId = "CPUID.CPU-Z",
                    Why = "vérifie la vitesse RAM RÉELLE (profil XMP/EXPO actif ?) et le modèle exact de tes composants",
                    Installed = () => File.Exists(Path.Combine(pf, @"CPUID\CPU-Z\cpuz.exe")) || Uninstall("CPU-Z") },
                new LibItem { Name = "GPU-Z (capteurs GPU + lien PCIe)", WingetId = "TechPowerUp.GPU-Z",
                    Why = "température/charge GPU AMD/Intel (que Windows n'expose pas) et contrôle du lien PCIe (x16 Gen4 vs bridé)",
                    Installed = () => Uninstall("GPU-Z") || WingetPkg("TechPowerUp.GPU-Z") },
                new LibItem { Name = "LatencyMon (latence DPC / micro-coupures)", WingetId = "Resplendence.LatencyMon",
                    Why = "identifie le pilote qui provoque grésillements audio et micro-freezes (DPC trop élevés)",
                    Installed = () => Directory.Exists(Path.Combine(pf, "Resplendence")) || Uninstall("LatencyMon") },
                new LibItem { Name = "HWMonitor (capteurs, léger)", WingetId = "CPUID.HWMonitor",
                    Why = "températures / tensions / vitesses de ventilos en un coup d'œil — plus léger que HWiNFO",
                    Installed = () => File.Exists(Path.Combine(pf, @"CPUID\HWMonitor\HWMonitor_x64.exe")) || Uninstall("HWMonitor") || WingetPkg("CPUID.HWMonitor") },
                new LibItem { Name = "ThrottleStop (Intel : throttling / undervolt)", WingetId = "TechPowerUp.ThrottleStop",
                    Why = "diagnostique et lève le bridage thermique des CPU Intel (undervolt, limites de puissance) — surtout sur portable",
                    Installed = () => Uninstall("ThrottleStop") || WingetPkg("TechPowerUp.ThrottleStop") },
                new LibItem { Name = "FurMark 2 (stress-test GPU)", WingetId = "Geeks3D.FurMark.2",
                    Why = "pousse le GPU à fond pour révéler surchauffe/instabilité (crashs « dispositif de rendu perdu »)",
                    Installed = () => Uninstall("FurMark 2") || WingetPkg("Geeks3D.FurMark.2") },
                new LibItem { Name = "OCCT (stress CPU/GPU/RAM/alim)", WingetId = "OCBase.OCCT.Personal",
                    Why = "test de stabilité complet : démasque une alim (PSU) faiblarde, une RAM instable ou un OC bancal",
                    Installed = () => Uninstall("OCCT") || WingetPkg("OCBase.OCCT.Personal") },
                new LibItem { Name = "WizTree (analyse l'espace disque)", WingetId = "AntibodySoftware.WizTree",
                    Why = "trouve en 2 s ce qui remplit ton SSD (jeux, caches de shaders, captures) — bien plus rapide que l'Explorateur",
                    Installed = () => File.Exists(Path.Combine(pf, @"WizTree\WizTree.exe")) || File.Exists(Path.Combine(pf86, @"WizTree\WizTree.exe")) || Uninstall("WizTree") },
                new LibItem { Name = "CrystalDiskMark (benchmark disque)", WingetId = "CrystalDewWorld.CrystalDiskMark",
                    Why = "mesure la vitesse RÉELLE de ton SSD/HDD (lecture/écriture) — complément de CrystalDiskInfo qui, lui, surveille la santé",
                    Installed = () => Uninstall("CrystalDiskMark") || WingetPkg("CrystalDewWorld.CrystalDiskMark") },

                // ---- Optimisation avancée (experts, à la main) ----
                new LibItem { Name = "Process Lasso (priorité/affinité CPU auto)", WingetId = "BitSum.ProcessLasso",
                    Why = "ProBalance empêche un process de fond de faire saccader ton jeu ; gère priorité, affinité et core parking automatiquement",
                    Installed = () => Uninstall("Process Lasso") || WingetPkg("BitSum.ProcessLasso") },
                new LibItem { Name = "ISLC — nettoyeur de liste de veille mémoire", WingetId = "Wagnardsoft.ISLC",
                    Why = "vide la « standby list » RAM automatiquement : remède connu aux micro-saccades (stutter) en session de jeu prolongée",
                    Installed = () => Uninstall("Intelligent standby") || WingetPkg("Wagnardsoft.ISLC") },

                // ---- Lanceurs de jeux (regroupe toute ta bibliothèque) ----
                new LibItem { Name = "GOG Galaxy", WingetId = "GOG.Galaxy",
                    Why = "jeux GOG (sans DRM) + regroupe Steam/Epic/Xbox dans une seule interface",
                    Installed = () => Directory.Exists(Path.Combine(pf86, "GOG Galaxy")) || Uninstall("GOG Galaxy") },
                new LibItem { Name = "EA app", WingetId = "ElectronicArts.EADesktop",
                    Why = "jeux EA (Battlefield, Apex Legends, EA Sports FC, Les Sims)",
                    Installed = () => Directory.Exists(Path.Combine(pf, "Electronic Arts")) || Uninstall("EA app") || Uninstall("EA Desktop") },
                new LibItem { Name = "Ubisoft Connect", WingetId = "Ubisoft.Connect",
                    Why = "jeux Ubisoft (Rainbow Six Siege, Assassin's Creed, Far Cry)",
                    Installed = () => Directory.Exists(Path.Combine(pf86, @"Ubisoft\Ubisoft Game Launcher")) || Uninstall("Ubisoft Connect") },
                new LibItem { Name = "Battle.net (Blizzard)", WingetId = "Blizzard.BattleNet",
                    Why = "jeux Blizzard/Activision (Overwatch 2, Diablo, Call of Duty, WoW)",
                    Installed = () => File.Exists(Path.Combine(pf86, @"Battle.net\Battle.net.exe")) || Uninstall("Battle.net") },
            };
        }

        /// <summary>Vrai si un programme dont le nom d'affichage contient <paramref name="namePart"/>
        /// figure dans les clés de désinstallation (HKLM/HKCU, 64 et 32 bits). Détection fiable
        /// quel que soit le dossier d'installation.</summary>
        private static bool Uninstall(string namePart)
        {
            string[] roots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (string root in roots)
            {
                if (UninstallIn(Registry.LocalMachine, root, namePart)) return true;
                if (UninstallIn(Registry.CurrentUser, root, namePart)) return true;
            }
            return false;
        }

        private static bool UninstallIn(RegistryKey hive, string root, string namePart)
        {
            try
            {
                using (RegistryKey k = hive.OpenSubKey(root))
                {
                    if (k == null) return false;
                    foreach (string sub in k.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey s = k.OpenSubKey(sub))
                            {
                                string n = s == null ? null : Convert.ToString(s.GetValue("DisplayName"));
                                if (!string.IsNullOrEmpty(n) && n.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0)
                                    return true;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return false;
        }

        // Vrai si winget a installé ce paquet en mode PORTABLE / archive (aucune clé de
        // désinstallation créée : Fan Control, GPU-Z, OCCT, FurMark…). winget dépose alors
        // le dossier dans %LOCALAPPDATA%\Microsoft\WinGet\Packages\<Id>_...
        private static string _wingetPkgDir;
        private static bool WingetPkg(string wingetId)
        {
            try
            {
                if (_wingetPkgDir == null)
                    _wingetPkgDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        @"Microsoft\WinGet\Packages");
                return Directory.Exists(_wingetPkgDir)
                    && Directory.GetDirectories(_wingetPkgDir, wingetId + "_*").Length > 0;
            }
            catch { return false; }
        }

        // ---- Lancement des outils installés (cycle natif : installer PUIS ouvrir depuis l'app) ----

        // Nom court d'un outil (avant " (", " —", " :") pour retrouver son entrée de désinstallation.
        private static string ShortName(LibItem it)
        {
            string n = it.Name ?? "";
            int cut = n.Length;
            foreach (string sep in new[] { " (", " —", " -", " :" })
            {
                int i = n.IndexOf(sep, StringComparison.Ordinal);
                if (i > 0 && i < cut) cut = i;
            }
            return n.Substring(0, cut).Trim();
        }

        // Exe principal via le DisplayIcon de la base de désinstallation ("C:\...\app.exe,0").
        private static string UninstallDisplayIcon(string namePart)
        {
            string[] roots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            RegistryKey[] hives = { Registry.LocalMachine, Registry.CurrentUser };
            foreach (string root in roots)
                foreach (RegistryKey hive in hives)
                {
                    try
                    {
                        using (RegistryKey k = hive.OpenSubKey(root))
                        {
                            if (k == null) continue;
                            foreach (string sub in k.GetSubKeyNames())
                            {
                                try
                                {
                                    using (RegistryKey s = k.OpenSubKey(sub))
                                    {
                                        if (s == null) continue;
                                        string n = Convert.ToString(s.GetValue("DisplayName"));
                                        if (string.IsNullOrEmpty(n) || n.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) < 0) continue;
                                        string icon = Convert.ToString(s.GetValue("DisplayIcon"));
                                        if (string.IsNullOrEmpty(icon)) continue;
                                        icon = icon.Trim().Trim('"');
                                        int comma = icon.LastIndexOf(',');
                                        if (comma > 2) icon = icon.Substring(0, comma).Trim().Trim('"');   // retire ",0"
                                        if (icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(icon)) return icon;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            return null;
        }

        // Résout l'exe à lancer : DisplayIcon (installés classiques) puis dossier winget (portables).
        private static string ResolveExe(LibItem it)
        {
            try { string byIcon = UninstallDisplayIcon(ShortName(it)); if (byIcon != null) return byIcon; }
            catch { }
            try
            {
                string pkg = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\WinGet\Packages");
                if (Directory.Exists(pkg))
                    foreach (string dir in Directory.GetDirectories(pkg, it.WingetId + "_*"))
                        foreach (string ex in Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories))
                        {
                            string b = Path.GetFileName(ex).ToLowerInvariant();
                            if (b.Contains("unins") || b.Contains("setup") || b.Contains("install")) continue;
                            return ex;
                        }
            }
            catch { }
            return null;
        }

        /// <summary>Ouvre un outil installé depuis l'app (cycle natif : install puis lancement).
        /// Vrai si lancé.</summary>
        public static bool TryLaunch(LibItem it)
        {
            string exe = ResolveExe(it);
            if (exe == null) return false;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
                return true;
            }
            catch { return false; }
        }

        // Vrai si un runtime .NET Desktop de la version demandée (ex. "6.", "9.") est présent.
        private static bool DotNetDesktop(string programFiles, string prefix)
        {
            try
            {
                string d = Path.Combine(programFiles, @"dotnet\shared\Microsoft.WindowsDesktop.App");
                return Directory.Exists(d) && Directory.GetDirectories(d, prefix + "*").Length > 0;
            }
            catch { return false; }
        }

        /// <summary>Vrai si l'appli d'ID winget donné est détectée installée localement.
        /// Permet à un panneau de diagnostic de savoir si l'outil qu'il conseille est déjà là.</summary>
        public static bool InstalledById(string wingetId)
        {
            try
            {
                foreach (LibItem it in Items())
                    if (string.Equals(it.WingetId, wingetId, StringComparison.OrdinalIgnoreCase))
                    { try { return it.Installed(); } catch { return false; } }
            }
            catch { }
            return false;
        }

        /// <summary>Ouvre le panneau Bibliothèques focalisé sur des outils conseillés par un autre
        /// panneau (met en avant + pré-coche s'ils manquent) : le lien fonction → outil.</summary>
        public static void OpenTools(Form owner, Action<string, int> log, string[] wingetIds)
        {
            try { using (var f = new LibsForm(log, wingetIds)) f.ShowDialog(owner); } catch { }
        }

        /// <summary>Installe UN outil directement (sans ouvrir la liste) : l'app s'en charge en un
        /// clic via winget, en arrière-plan, avec confirmation et compte-rendu. « Installe les apps
        /// pour toi » = intégration transparente depuis le panneau qui en a besoin.</summary>
        public static void QuickInstall(Form owner, Action<string, int> log, string wingetId)
        {
            LibItem item = null;
            try { foreach (LibItem it in Items()) if (string.Equals(it.WingetId, wingetId, StringComparison.OrdinalIgnoreCase)) { item = it; break; } }
            catch { }
            if (item == null) { OpenTools(owner, log, new[] { wingetId }); return; }

            bool installed = false; try { installed = item.Installed(); } catch { }
            if (installed)
            {
                if (MessageBox.Show(owner, item.Name + " est déjà installé. ✔\n\nL'ouvrir maintenant ?",
                        "DesTinGOOD", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes && !TryLaunch(item))
                    MessageBox.Show(owner, "Impossible de le localiser automatiquement — ouvre-le depuis le menu Démarrer.",
                        "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string winget = WingetPath();
            if (winget == null)
            {
                MessageBox.Show(owner,
                    "winget est introuvable.\n\nInstalle « App Installer » (gratuit, Microsoft) depuis le Microsoft Store, puis réessaie.",
                    "winget requis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(owner,
                    "Installer « " + item.Name + " » maintenant ?\n\n" + item.Why + "\n\n"
                    + "L'app s'en occupe : installation via winget (Microsoft), en arrière-plan.",
                    "Installer l'outil", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            if (log != null) log("Installation de " + item.Name + " (winget)...", 0);
            System.Threading.Tasks.Task.Run(() =>
            {
                bool ok = false;
                try { ok = Install(winget, item, log); } catch { }
                bool res = ok;
                try
                {
                    owner.BeginInvoke((Action)(() =>
                    {
                        if (res)
                        {
                            if (MessageBox.Show(owner, item.Name + " installé. ✔\n\nL'ouvrir maintenant ?",
                                    "DesTinGOOD", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes && !TryLaunch(item))
                                MessageBox.Show(owner, "Installé — ouvre-le depuis le menu Démarrer.",
                                    "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                            MessageBox.Show(owner,
                                item.Name + " : l'installation a échoué (voir le journal). Réessaie, ou installe-le depuis le site de l'éditeur.",
                                "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }));
                }
                catch { }
            });
        }

        /// <summary>Nombre d'outils (parmi wingetIds) NON installés. Construit le catalogue une
        /// seule fois. À appeler en arrière-plan (accès registre/disque).</summary>
        public static int MissingCount(string[] wingetIds)
        {
            int missing = 0;
            try
            {
                List<LibItem> items = Items();
                foreach (string id in wingetIds)
                {
                    bool inst = false;
                    foreach (LibItem it in items)
                        if (string.Equals(it.WingetId, id, StringComparison.OrdinalIgnoreCase))
                        { try { inst = it.Installed(); } catch { inst = false; } break; }
                    if (!inst) missing++;
                }
            }
            catch { }
            return missing;
        }

        /// <summary>Câble un bouton « outils conseillés » : clic → ouverture ciblée des Bibliothèques ;
        /// et en fond, ajoute une pastille d'état (✔ tous là / ○ N à installer). Réutilisable.</summary>
        public static void WireToolButton(Button btn, Form owner, Action<string, int> log, string baseText, string[] wingetIds)
        {
            btn.Text = baseText;
            // Un seul outil -> l'app l'installe DIRECTEMENT (transparent, natif). Plusieurs -> la
            // liste ciblée, pour choisir.
            btn.Click += (s, e) =>
            {
                if (wingetIds != null && wingetIds.Length == 1) QuickInstall(owner, log, wingetIds[0]);
                else OpenTools(owner, log, wingetIds);
            };
            // Différé à l'affichage de la fenêtre : garantit que le handle du bouton EXISTE avant
            // le BeginInvoke (pas de course), et le calcul (accès registre/disque) reste en fond.
            owner.Shown += (s, e) => System.Threading.Tasks.Task.Run(() =>
            {
                int missing = MissingCount(wingetIds);
                string txt = baseText + (missing == 0 ? "  ✔" : "  ○ " + missing);
                try { btn.BeginInvoke((Action)(() => { try { btn.Text = txt; } catch { } })); }
                catch { }   // fenêtre fermée entre-temps : sans conséquence
            });
        }

        /// <summary>Nombre de bibliothèques ESSENTIELLES (runtimes réclamés par les jeux : VC++,
        /// DirectX, .NET…) absentes du PC. Sert à unifier les applis avec ⚡ TOUT OPTIMISER :
        /// un PC « optimisé » doit aussi avoir ses runtimes. À appeler en arrière-plan (I/O).</summary>
        public static int MissingEssentialCount()
        {
            int n = 0;
            try
            {
                foreach (LibItem it in Items())
                {
                    if (!it.Essential) continue;
                    bool here;
                    try { here = it.Installed(); } catch { here = true; }   // en cas de doute : ne pas alerter
                    if (!here) n++;
                }
            }
            catch { }
            return n;
        }

        /// <summary>Chemin de winget, ou null s'il est absent (App Installer non présent).</summary>
        public static string WingetPath()
        {
            string alias = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WindowsApps\winget.exe");
            if (File.Exists(alias)) return alias;
            try { return Sys.Run("winget.exe", "--version").ExitCode == 0 ? "winget.exe" : null; }
            catch { return null; }
        }

        /// <summary>Rafraîchit l'index de la source winget (une fois par série ; échec toléré).</summary>
        public static void RefreshSource(string winget, Action<string, int> log)
        {
            log("Mise à jour de l'index winget...", 0);
            Sys.Run(winget, "source update --disable-interactivity");
        }

        private static string InstallArgs(string id, string scope)
        {
            return "install --id " + id + " -e --silent --source winget"
                 + " --accept-source-agreements --accept-package-agreements --disable-interactivity"
                 + (scope == null ? "" : " --scope " + scope);
        }

        /// <summary>Raison d'échec lisible : code winget décodé + dernières lignes de sa sortie.</summary>
        private static string WingetReason(NativeResult r)
        {
            uint code = unchecked((uint)r.ExitCode);
            string known = code == 0x8A150014 ? "paquet introuvable dans la source"
                         : code == 0x8A15002B ? "aucun installeur applicable dans ce contexte"
                         : null;
            string tail = "";
            try
            {
                string[] lines = (r.Output ?? "").Split('\n');
                for (int i = lines.Length - 1; i >= 0 && tail.Length < 160; i--)
                {
                    string l = lines[i].Trim().Trim('\b', '-', '\\', '|', '/');
                    if (l.Length < 4) continue;
                    tail = (tail.Length == 0) ? l : l + " | " + tail;
                    if (tail.Length > 40) break;
                }
            }
            catch { }
            return " (code 0x" + code.ToString("X8")
                 + (known != null ? " — " + known : "")
                 + (tail.Length > 0 ? " ; winget : " + tail : "") + ")";
        }

        /// <summary>
        /// Plan B pour les Visual C++ : téléchargement de l'installeur OFFICIEL via
        /// « winget download » (hash vérifié par winget) puis exécution directe en
        /// silencieux — contourne les refus d'installation de winget en contexte élevé.
        /// </summary>
        private static bool DownloadAndRunRedist(string winget, LibItem item, Action<string, int> log)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), @"destingood-libs\" + item.WingetId.Replace('+', '_'));
                Directory.CreateDirectory(dir);
                log(item.Name + " : plan B — téléchargement vérifié (winget download) puis installation directe...", 0);
                NativeResult d = Sys.Run(winget,
                    "download --id " + item.WingetId + " -e --accept-source-agreements --accept-package-agreements -d \"" + dir + "\"");
                if (d.ExitCode != 0) { log(item.Name + " : téléchargement impossible" + WingetReason(d), 2); return false; }

                string exe = null;
                foreach (string f in Directory.GetFiles(dir, "*.exe")) exe = f;
                if (exe == null) { log(item.Name + " : installeur téléchargé introuvable.", 2); return false; }

                NativeResult inst = Sys.Run(exe, "/install /quiet /norestart");
                bool ok = inst.ExitCode == 0 || inst.ExitCode == 3010 || item.Installed();
                if (!ok) log(item.Name + " : l'installeur a retourné le code " + inst.ExitCode + ".", 2);
                return ok;
            }
            catch (Exception ex) { log(item.Name + " : plan B impossible — " + ex.Message, 2); return false; }
        }

        /// <summary>Installe un paquet ; vrai si OK (ou déjà présent). Journalise la vraie raison en cas d'échec.</summary>
        public static bool Install(string winget, LibItem item, Action<string, int> log)
        {
            log("Installation de " + item.Name + " (winget " + item.WingetId + ")...", 0);
            NativeResult r = Sys.Run(winget, InstallArgs(item.WingetId, null));

            // Nouvel essai en portée machine — pour TOUTE appli, pas seulement les bibliothèques :
            // en contexte élevé, l'installeur « utilisateur » est souvent refusé (0x8A15002B) alors
            // que la portée machine passe. Sans risque : on ne réessaie que si le 1er essai a échoué.
            if (r.ExitCode != 0 && !item.Installed())
            {
                log(item.Name + " : premier essai refusé" + WingetReason(r) + " — nouvel essai portée machine...", 0);
                r = Sys.Run(winget, InstallArgs(item.WingetId, "machine"));
            }

            // 3010 = installé mais redémarrage requis (souvent VC++/DirectX) : c'est un SUCCÈS,
            // pas un échec — cohérent avec le plan B ci-dessous qui traite déjà 3010 ainsi.
            bool ok = r.ExitCode == 0 || r.ExitCode == 3010 || item.Installed();

            // Visual C++ : plan B téléchargement vérifié + exécution directe (l'app est déjà admin).
            if (!ok && item.WingetId.StartsWith("Microsoft.VCRedist", StringComparison.OrdinalIgnoreCase))
                ok = DownloadAndRunRedist(winget, item, log);

            log(ok ? item.Name + " : installé. ✔"
                   : item.Name + " : échec" + WingetReason(r) + " — installe-le depuis le site officiel de l'éditeur.",
                ok ? 1 : 2);
            return ok;
        }
    }

    /// <summary>Panneau « Bibliothèques & applis de jeu » : détection locale + installation winget.</summary>
    internal class LibsForm : Form
    {
        private readonly Action<string, int> _log;
        private CheckedListBox _list;
        private Label _summary;
        private Button _btnScan, _btnInstall, _btnClose;
        private List<LibScan.LibItem> _items = new List<LibScan.LibItem>();
        private string _winget;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        private readonly string[] _highlight;

        public LibsForm(Action<string, int> log) : this(log, null) { }

        /// <summary>Ouvre le panneau en mettant en avant (et pré-cochant si absents) des outils
        /// conseillés par un autre panneau — le « lien » entre une fonction et son outil.</summary>
        public LibsForm(Action<string, int> log, string[] highlightWingetIds)
        {
            _log = log;
            _highlight = highlightWingetIds;
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Bibliothèques & applis de jeu";
            ClientSize = new Size(680, 500);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Bibliothèques & applis de jeu — « vcruntime140.dll manquant », plus jamais",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Un jeu qui refuse de se lancer, c'est presque toujours une bibliothèque manquante. "
                     + "Détection locale instantanée ; installation par WINGET (gestionnaire officiel Microsoft) — "
                     + "aucun téléchargement douteux. Manquantes = pré-cochées · double-clic sur un outil ✔ pour l'OUVRIR.",
                Location = new Point(18, 58), Size = new Size(644, 46), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(intro);

            _list = new CheckedListBox
            {
                Location = new Point(18, 108), Size = new Size(644, 288), CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9.5f), IntegralHeight = false,
                HorizontalScrollbar = true
            };
            _list.DoubleClick += OnListDoubleClick;   // double-clic sur un outil installé -> l'ouvrir depuis l'app
            Controls.Add(_list);

            _summary = new Label
            {
                Location = new Point(18, 402), Size = new Size(644, 22),
                Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_summary);

            _btnScan = MakeBtn("Analyser à nouveau", 18, 434, 150, 38, false);
            _btnScan.Click += (s, e) => Scan();
            _btnInstall = MakeBtn("INSTALLER LA SÉLECTION (winget)", 178, 434, 280, 38, true);
            _btnInstall.Click += OnInstall;
            _btnClose = MakeBtn("Fermer", 572, 434, 90, 38, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnScan); Controls.Add(_btnInstall); Controls.Add(_btnClose);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 9.5f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnScan.Enabled = !busy; _btnInstall.Enabled = !busy; _list.Enabled = !busy;
        }

        private void Scan()
        {
            SetBusy(true);
            _summary.Text = "Détection en cours...";
            Task.Run(() =>
            {
                List<LibScan.LibItem> items = LibScan.Items();
                var installed = new List<bool>();
                foreach (LibScan.LibItem it in items)
                {
                    bool ok;
                    try { ok = it.Installed(); } catch { ok = false; }
                    installed.Add(ok);
                }
                string winget = LibScan.WingetPath();
                try { BeginInvoke((Action)(() => Populate(items, installed, winget))); } catch { }
            });
        }

        private void Populate(List<LibScan.LibItem> items, List<bool> installed, string winget)
        {
            _items = items;
            _winget = winget;
            _list.Items.Clear();
            int missing = 0, firstHi = -1;
            for (int i = 0; i < items.Count; i++)
            {
                LibScan.LibItem it = items[i];
                bool here = installed[i];
                if (!here && it.Essential) missing++;
                bool hi = _highlight != null && Array.IndexOf(_highlight, it.WingetId) >= 0;
                if (hi && firstHi < 0) firstHi = i;
                string prefix = here ? "✔  " : (hi ? "➡  " : (it.Essential ? "⚠  " : "•  "));
                string state = here ? "installé" : "absent";
                // Pré-coché si bibliothèque essentielle absente OU outil conseillé absent.
                _list.Items.Add(prefix + it.Name + "   —   " + state + " · " + it.Why,
                    !here && (it.Essential || hi));
            }
            _summary.Text = (_highlight != null
                ? "Outils conseillés (➡) mis en avant et pré-cochés s'ils manquent. "
                : "")
                + (missing == 0
                ? "Toutes les bibliothèques de jeu essentielles sont présentes. ✔"
                : missing + " bibliothèque(s) essentielle(s) MANQUANTE(S) — cause classique des jeux qui ne se lancent pas.")
                + (winget == null ? "   (winget ABSENT : installe « App Installer » depuis le Microsoft Store)" : "");
            if (firstHi >= 0 && firstHi < _list.Items.Count)
                try { _list.TopIndex = firstHi; } catch { }
            SetBusy(false);
        }

        // Double-clic sur un outil DÉJÀ installé -> l'ouvrir depuis l'app (cycle natif).
        private void OnListDoubleClick(object sender, EventArgs e)
        {
            int i = _list.SelectedIndex;
            if (i < 0 || i >= _items.Count) return;
            LibScan.LibItem it = _items[i];
            bool here = false; try { here = it.Installed(); } catch { }
            if (!here) return;   // pas installé : le double-clic sert juste à cocher pour installer
            if (!LibScan.TryLaunch(it))
                MessageBox.Show(this, it.Name + " est installé mais introuvable automatiquement — ouvre-le depuis le menu Démarrer.",
                    "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnInstall(object sender, EventArgs e)
        {
            var sel = new List<LibScan.LibItem>();
            for (int i = 0; i < _list.Items.Count && i < _items.Count; i++)
                if (_list.GetItemChecked(i)) sel.Add(_items[i]);
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "Coche au moins un élément à installer.", "DesTinGOOD",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_winget == null)
            {
                MessageBox.Show(this,
                    "winget est introuvable sur ce PC.\n\nInstalle « App Installer » (gratuit, Microsoft) depuis le "
                    + "Microsoft Store, puis relance l'analyse.",
                    "winget requis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var names = new List<string>();
            foreach (LibScan.LibItem it in sel) names.Add("  • " + it.Name);
            if (MessageBox.Show(this,
                    "Installer " + sel.Count + " élément(s) via winget (Microsoft) ?\n\n" + string.Join("\n", names.ToArray())
                    + "\n\nConnexion internet requise ; quelques minutes selon la taille.",
                    "Bibliothèques & applis", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            SetBusy(true);
            _summary.Text = "Installation en cours (voir le journal)...";
            string winget = _winget;
            Task.Run(() =>
            {
                LibScan.RefreshSource(winget, _log);   // index à jour (échec toléré)
                int ok = 0;
                foreach (LibScan.LibItem it in sel)
                    if (LibScan.Install(winget, it, _log)) ok++;
                _log("Bibliothèques & applis : " + ok + "/" + sel.Count + " installée(s).", ok == sel.Count ? 1 : 2);
                try { BeginInvoke((Action)(() => Scan())); } catch { }
            });
        }
    }
}
