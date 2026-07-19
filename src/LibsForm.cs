using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

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
                new LibItem { Name = "OpenAL (audio 3D)", Essential = true, WingetId = "OpenAL.OpenAL",
                    Why = "audio de nombreux jeux (OpenAL32.dll)",
                    Installed = () => File.Exists(Sys32("OpenAL32.dll")) || File.Exists(WowDir("OpenAL32.dll")) },

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
            };
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

        /// <summary>Installe un paquet winget en silencieux ; vrai si OK (ou déjà présent).</summary>
        public static bool Install(string winget, LibItem item, Action<string, int> log)
        {
            log("Installation de " + item.Name + " (winget " + item.WingetId + ")...", 0);
            NativeResult r = Sys.Run(winget,
                "install --id " + item.WingetId + " -e --silent --accept-source-agreements --accept-package-agreements --disable-interactivity");
            bool ok = r.ExitCode == 0 || item.Installed();
            log(ok ? item.Name + " : installé. ✔"
                   : item.Name + " : échec winget (code " + r.ExitCode + ") — réessaie ou installe-le depuis le site officiel.",
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
                int ok = 0;
                foreach (LibScan.LibItem it in sel)
                    if (LibScan.Install(winget, it, _log)) ok++;
                _log("Bibliothèques & applis : " + ok + "/" + sel.Count + " installée(s).", ok == sel.Count ? 1 : 2);
                try { BeginInvoke((Action)(() => Scan())); } catch { }
            });
        }
    }
}
