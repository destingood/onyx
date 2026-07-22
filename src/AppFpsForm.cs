using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// ⚡ Applications qui plombent les FPS — repère les logiciels connus pour grignoter
    /// CPU/GPU en arrière-plan (Chrome, Wallpaper Engine, OneDrive) ou via un overlay
    /// (Discord, Steam, GeForce Experience). L'app AUTOMATISE ce qui est sûr et réversible,
    /// et te GUIDE pour les réglages qui vivent à l'intérieur de ces applications.
    /// </summary>
    internal class AppFpsForm : Form
    {
        private readonly Action<string, int> _log;
        private FlowLayoutPanel _flow;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private static readonly Color Bg = Color.FromArgb(245, 246, 248);
        private static readonly Color Ink = Color.FromArgb(40, 44, 52);
        private static readonly Color Sub = Color.FromArgb(96, 100, 108);
        private static readonly Color Warn = Color.FromArgb(190, 120, 0);

        private const string ChromePolicy = @"SOFTWARE\Policies\Google\Chrome";

        public AppFpsForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Rescan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Applications qui plombent les FPS";
            ClientSize = new Size(726, 566);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Bg;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  ⚡ Applications qui plombent tes FPS",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Overlays et applis d'arrière-plan grignotent CPU/GPU pendant que tu joues. "
                     + "Ici on automatise ce qui est sûr, et on te guide pour les réglages internes aux applis.",
                Location = new Point(18, 62), Size = new Size(690, 38), ForeColor = Sub
            };
            Controls.Add(intro);

            _flow = new FlowLayoutPanel
            {
                Location = new Point(18, 104), Size = new Size(690, 406),
                AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_flow);

            var btnRescan = MakeBtn("Analyser à nouveau", 18, 522, 170, 34, false);
            btnRescan.Click += delegate { Rescan(); };
            Controls.Add(btnRescan);

            var btnClose = MakeBtn("Fermer", 626, 522, 82, 34, false);
            btnClose.Click += delegate { Close(); };
            Controls.Add(btnClose);
        }

        // -------------------------------------------------------------- scan
        private void Rescan()
        {
            _flow.SuspendLayout();
            _flow.Controls.Clear();
            int i = 0;

            // --- Chrome : arrière-plan (automatisable, réversible) ---
            if (ChromeInstalled() || IsRunning("chrome"))
            {
                bool blocked = ChromeBgBlocked();
                bool running = IsRunning("chrome");
                AddRow(i++,
                    "🌐 Google Chrome",
                    (running ? "En cours d'exécution. " : "Installé. ")
                        + (blocked ? "Arrière-plan déjà bloqué (bon). ✔" : "Tourne encore en fond une fois fermé."),
                    blocked ? Accent : Warn,
                    "Chrome garde des processus + le GPU actifs même fenêtre fermée : autant de ressources en moins en jeu.",
                    blocked ? "Réautoriser l'arrière-plan" : "Empêcher l'arrière-plan",
                    delegate
                    {
                        try
                        {
                            if (ChromeBgBlocked())
                            {
                                Sys.DelMachine(ChromePolicy, "BackgroundModeEnabled");
                                _log("Chrome : arrière-plan réautorisé (réglage par défaut rétabli).", 0);
                            }
                            else
                            {
                                Sys.SetMachine(ChromePolicy, "BackgroundModeEnabled", 0, RegistryValueKind.DWord);
                                _log("Chrome : arrière-plan bloqué. Ferme puis rouvre Chrome pour l'effet complet. ✔", 1);
                            }
                        }
                        catch (Exception ex) { _log("Chrome : " + ex.Message, 3); }
                        Rescan();
                    });
            }

            // --- Wallpaper Engine : décoratif, lourd (fermeture sûre, relançable) ---
            if (IsRunning("wallpaper32", "wallpaper64", "wallpaperengine"))
            {
                AddRow(i++,
                    "🖼 Wallpaper Engine",
                    "En cours d'exécution — anime ton fond d'écran en continu (CPU/GPU).",
                    Warn,
                    "Un fond d'écran animé rend des FPS en le fermant pendant les parties. Tu peux le relancer quand tu veux.",
                    "Fermer maintenant",
                    delegate
                    {
                        int n = KillByName("wallpaper32", "wallpaper64", "wallpaperengine");
                        _log("Wallpaper Engine fermé (" + n + " processus). Relance-le depuis Steam quand tu veux.", n > 0 ? 1 : 2);
                        Rescan();
                    });
            }

            // --- OneDrive : synchro en fond (fermeture sûre) ---
            if (IsRunning("OneDrive"))
            {
                AddRow(i++,
                    "☁ OneDrive",
                    "En cours d'exécution — synchronise et lit le disque en arrière-plan.",
                    Sub,
                    "La synchro OneDrive peut provoquer des à-coups disque en jeu. Ferme-le le temps d'une session.",
                    "Fermer maintenant",
                    delegate
                    {
                        int n = KillByName("OneDrive");
                        _log("OneDrive fermé (" + n + " processus).", n > 0 ? 1 : 2);
                        Rescan();
                    });
            }

            // --- Discord : overlay + accélération matérielle (réglage interne -> guide) ---
            if (IsRunning("Discord"))
            {
                AddRow(i++,
                    "🎧 Discord",
                    "En cours d'exécution — l'overlay et l'accélération matérielle utilisent le GPU en jeu.",
                    Sub,
                    "Dans Discord : Paramètres → Overlay de jeu → désactive l'overlay ; puis Voix et vidéo / Avancés → désactive « Accélération matérielle ».",
                    "Voir les étapes",
                    delegate
                    {
                        MessageBox.Show(this,
                            "Réduire l'impact de Discord :\n\n"
                            + "1) Paramètres (⚙) → « Overlay de jeu » → désactive « Activer l'overlay en jeu ».\n"
                            + "2) Paramètres → « Voix et vidéo » : désactive l'accélération matérielle si présente.\n"
                            + "3) Paramètres → « Avancés » → désactive « Accélération matérielle ».\n\n"
                            + "Ces réglages vivent dans Discord et ne peuvent pas être changés de l'extérieur en toute fiabilité.",
                            "Discord — étapes", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    });
            }

            // --- Steam : overlay en jeu (réglage interne -> ouvrir les réglages) ---
            if (IsRunning("steam"))
            {
                AddRow(i++,
                    "🎮 Steam",
                    "En cours d'exécution — l'overlay Steam s'injecte dans chaque jeu.",
                    Sub,
                    "L'overlay Steam coûte quelques FPS. Steam → Paramètres → « Dans le jeu » → décoche « Activer l'overlay Steam ».",
                    "Ouvrir les réglages Steam",
                    delegate
                    {
                        try { Process.Start(new ProcessStartInfo("steam://open/settings") { UseShellExecute = true }); }
                        catch (Exception ex) { _log("Steam : " + ex.Message, 2); }
                    });
            }

            // --- NVIDIA GeForce Experience / overlay (réglage interne -> guide) ---
            if (IsRunning("NVIDIA GeForce Experience", "NVIDIA Overlay", "NVIDIA Share"))
            {
                AddRow(i++,
                    "🟩 NVIDIA GeForce Experience",
                    "En cours d'exécution — l'overlay (ShadowPlay) tourne par-dessus tes jeux.",
                    Sub,
                    "GeForce Experience → Paramètres (⚙) → désactive « Superposition en jeu » si tu n'enregistres pas tes parties.",
                    "Voir les étapes",
                    delegate
                    {
                        MessageBox.Show(this,
                            "Désactiver l'overlay NVIDIA :\n\n"
                            + "GeForce Experience → icône Paramètres (⚙) → désactive « Superposition en jeu » "
                            + "(In-Game Overlay / ShadowPlay).\n\n"
                            + "Utile seulement si tu n'utilises pas l'enregistrement instantané.",
                            "NVIDIA — étapes", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    });
            }

            if (_flow.Controls.Count == 0)
            {
                var ok = new Label
                {
                    Text = "✔ Aucune application connue pour plomber les FPS n'a été détectée en cours d'exécution.\n"
                         + "(Chrome, Wallpaper Engine, OneDrive, Discord, Steam, GeForce Experience.)",
                    Size = new Size(650, 60), Margin = new Padding(14, 18, 8, 8),
                    ForeColor = Accent, Font = new Font("Segoe UI Semibold", 10f)
                };
                _flow.Controls.Add(ok);
            }

            _flow.ResumeLayout();
        }

        // -------------------------------------------------------------- row builder
        private void AddRow(int index, string title, string status, Color statusColor, string tip, string actionLabel, Action action)
        {
            var row = new Panel
            {
                Size = new Size(666, 108), Margin = new Padding(0),
                BackColor = index % 2 == 0 ? Color.White : Color.FromArgb(248, 249, 251)
            };
            row.Controls.Add(new Label
            {
                Text = title, Location = new Point(14, 10), Size = new Size(500, 22),
                Font = new Font("Segoe UI Semibold", 10.5f), ForeColor = Ink
            });
            row.Controls.Add(new Label
            {
                Text = status, Location = new Point(14, 33), Size = new Size(508, 18),
                Font = new Font("Segoe UI", 8.75f), ForeColor = statusColor
            });
            row.Controls.Add(new Label
            {
                Text = tip, Location = new Point(14, 54), Size = new Size(508, 46), ForeColor = Sub
            });

            if (!string.IsNullOrEmpty(actionLabel) && action != null)
            {
                Button b = MakeBtn(actionLabel, 528, 36, 122, 42, true);
                b.Click += delegate { action(); };
                row.Controls.Add(b);
            }

            var sep = new Panel { Location = new Point(0, 107), Size = new Size(666, 1), BackColor = Color.FromArgb(232, 234, 238) };
            row.Controls.Add(sep);
            _flow.Controls.Add(row);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Ink,
                Font = primary ? new Font("Segoe UI Semibold", 9f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        // -------------------------------------------------------------- helpers
        private static bool IsRunning(params string[] names)
        {
            foreach (string n in names)
            {
                try
                {
                    Process[] ps = Process.GetProcessesByName(n);
                    bool any = ps.Length > 0;
                    foreach (Process p in ps) p.Dispose();
                    if (any) return true;
                }
                catch { }
            }
            return false;
        }

        private static int KillByName(params string[] names)
        {
            int killed = 0;
            foreach (string n in names)
            {
                Process[] ps;
                try { ps = Process.GetProcessesByName(n); } catch { continue; }
                foreach (Process p in ps)
                {
                    try { p.Kill(); p.WaitForExit(3000); killed++; }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            return killed;
        }

        private static bool ChromeBgBlocked()
        {
            return Sys.IntEquals(Sys.GetMachine(ChromePolicy, "BackgroundModeEnabled"), 0);
        }

        private static bool ChromeInstalled()
        {
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pfx = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] paths =
            {
                Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(pfx, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(local, @"Google\Chrome\Application\chrome.exe"),
            };
            foreach (string p in paths) { try { if (File.Exists(p)) return true; } catch { } }
            return false;
        }
    }
}
