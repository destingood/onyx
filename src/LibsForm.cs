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
                new LibItem { Name = "FurMark 2 (stress-test GPU)", WingetId = "Geeks3D.FurMark.2",
                    Why = "pousse le GPU à fond pour révéler surchauffe/instabilité (crashs « dispositif de rendu perdu »)",
                    Installed = () => Uninstall("FurMark") || WingetPkg("Geeks3D.FurMark.2") },
                new LibItem { Name = "OCCT (stress CPU/GPU/RAM/alim)", WingetId = "OCBase.OCCT.Personal",
                    Why = "test de stabilité complet : démasque une alim (PSU) faiblarde, une RAM instable ou un OC bancal",
                    Installed = () => Uninstall("OCCT") || WingetPkg("OCBase.OCCT.Personal") },
                new LibItem { Name = "WizTree (analyse l'espace disque)", WingetId = "AntibodySoftware.WizTree",
                    Why = "trouve en 2 s ce qui remplit ton SSD (jeux, caches de shaders, captures) — bien plus rapide que l'Explorateur",
                    Installed = () => File.Exists(Path.Combine(pf, @"WizTree\WizTree.exe")) || File.Exists(Path.Combine(pf86, @"WizTree\WizTree.exe")) || Uninstall("WizTree") },

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

            bool ok = r.ExitCode == 0 || item.Installed();

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

        public LibsForm(Action<string, int> log)
        {
            _log = log;
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
                Text = "  📦 Bibliothèques & applis de jeu — « vcruntime140.dll manquant », plus jamais",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Un jeu qui refuse de se lancer, c'est presque toujours une bibliothèque manquante. "
                     + "Détection locale instantanée ; installation par WINGET (gestionnaire officiel Microsoft) — "
                     + "aucun téléchargement douteux. Les bibliothèques manquantes sont pré-cochées.",
                Location = new Point(18, 58), Size = new Size(644, 46), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(intro);

            _list = new CheckedListBox
            {
                Location = new Point(18, 108), Size = new Size(644, 288), CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9.5f), IntegralHeight = false,
                HorizontalScrollbar = true
            };
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
            int missing = 0;
            for (int i = 0; i < items.Count; i++)
            {
                LibScan.LibItem it = items[i];
                bool here = installed[i];
                if (!here && it.Essential) missing++;
                string prefix = here ? "✔  " : (it.Essential ? "⚠  " : "•  ");
                string state = here ? "installé" : "absent";
                _list.Items.Add(prefix + it.Name + "   —   " + state + " · " + it.Why,
                    !here && it.Essential);
            }
            _summary.Text = (missing == 0
                ? "Toutes les bibliothèques de jeu essentielles sont présentes. ✔"
                : missing + " bibliothèque(s) essentielle(s) MANQUANTE(S) — cause classique des jeux qui ne se lancent pas.")
                + (winget == null ? "   (winget ABSENT : installe « App Installer » depuis le Microsoft Store)" : "");
            SetBusy(false);
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
