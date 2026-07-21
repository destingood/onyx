using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Guide latence & perf : vérifie en direct les leviers clés + rappelle les étapes hors-app (Reflex, polling, plein écran).</summary>
    internal class LatencyGuideForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private ToolTip _tip;
        private List<Font> _ownedFonts;
        private Font _bold;

        private Font Own(Font f) { _ownedFonts.Add(f); return f; }

        // Plans d'alimentation « haute perf » / « ultimate »
        private const string HighPerf = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        private const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb62";

        public LatencyGuideForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Load += (s, e) => Reload();
        }

        private void Build()
        {
            Text = "DesTinGOOD — Guide latence & perf";
            ClientSize = new Size(660, 560);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 460);
            BackColor = Color.FromArgb(245, 246, 248);
            _ownedFonts = new List<Font>();
            Font = Own(new Font("Segoe UI", 9f));
            _bold = Own(new Font("Segoe UI Semibold", 9.5f));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            _tip = new ToolTip { AutoPopDelay = 25000, InitialDelay = 300 };

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Guide latence & perf", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = Own(new Font("Segoe UI Semibold", 13f)), TextAlign = ContentAlignment.MiddleLeft
            });

            var intro = new Label
            {
                Dock = DockStyle.Top, Height = 34, Padding = new Padding(14, 7, 12, 2),
                ForeColor = Color.FromArgb(90, 95, 105),
                Text = "Les leviers de l'input lag, du plus fort au plus fin. Vert = OK, orange = à régler."
            };

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, ShowGroups = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable, Font = Own(new Font("Segoe UI", 9.5f))
            };
            _list.Columns.Add("Levier", 250);
            _list.Columns.Add("État / conseil", 380);
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 6) };
            host.Controls.Add(_list);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(12, 7, 12, 7) };
            var scr = MakeBtn("Réglages écran...", 150, DockStyle.Left);
            scr.Click += (s, e) => Shell("ms-settings:display-advanced", "ms-settings:display");
            _tip.SetToolTip(scr, "Ouvre l'Affichage avancé : choisis l'écran et sa fréquence de rafraîchissement maximale.");
            var gpu = MakeBtn("Panneau NVIDIA...", 150, DockStyle.Left);
            gpu.Click += (s, e) => Shell("nvcpl.cpl", null);
            _tip.SetToolTip(gpu, "Ouvre le panneau NVIDIA (G-Sync, faible latence, fréquence préférée).");
            var refresh = MakeBtn("Rafraîchir", 100, DockStyle.Left);
            refresh.Click += (s, e) => Reload();
            var close = MakeBtn("Fermer", 90, DockStyle.Right);
            close.Click += (s, e) => Close();
            bottom.Controls.Add(new Label { Dock = DockStyle.Fill });
            bottom.Controls.Add(refresh); bottom.Controls.Add(gpu); bottom.Controls.Add(scr);
            bottom.Controls.Add(close);

            Controls.Add(banner);
            Controls.Add(intro);
            Controls.Add(host);
            Controls.Add(bottom);
            Controls.SetChildIndex(banner, 3);
            Controls.SetChildIndex(intro, 2);
            Controls.SetChildIndex(host, 0);
            Controls.SetChildIndex(bottom, 1);

            Theme.Apply(this);
        }

        private static Button MakeBtn(string text, int w, DockStyle dock)
        {
            var b = new Button { Text = text, Width = w, Dock = dock, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(4, 0, 4, 0) };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void Shell(string file, string fallback)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true }); }
            catch { if (fallback != null) try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fallback) { UseShellExecute = true }); } catch { } }
        }

        // level : 0 OK (vert), 1 à régler (orange), 2 rappel (gris info)
        private void Add(ListViewGroup g, int level, string name, string advice)
        {
            string icon = level == 0 ? "✓ " : (level == 1 ? "! " : "• ");
            Color c = level == 0 ? Theme.OkColor
                    : (level == 1 ? Color.FromArgb(190, 120, 0) : Color.FromArgb(90, 95, 105));
            var it = new ListViewItem(icon + name) { Group = g, UseItemStyleForSubItems = false, ForeColor = c };
            if (level != 2) it.Font = _bold;
            var sub = it.SubItems.Add(advice);
            sub.ForeColor = c;
            _list.Items.Add(it);
        }

        private void Reload()
        {
            Cursor = Cursors.WaitCursor;
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Groups.Clear();

            var auto = new ListViewGroup("Vérifié automatiquement (réglages Windows)") { HeaderAlignment = HorizontalAlignment.Left };
            var todo = new ListViewGroup("À faire toi-même — le plus gros de l'input lag est ici") { HeaderAlignment = HorizontalAlignment.Left };
            _list.Groups.Add(auto);
            _list.Groups.Add(todo);

            // ---- Écran à sa fréquence max (levier n°1) ----
            try
            {
                var modes = DisplayInfo.Query();
                DisplayInfo.DisplayMode prim = null;
                foreach (var d in modes) if (d.Primary) { prim = d; break; }
                if (prim == null && modes.Count > 0) prim = modes[0];
                if (prim != null)
                {
                    if (prim.BelowMax)
                        Add(auto, 1, "Écran principal à sa fréquence max",
                            prim.CurrentHz + " Hz alors qu'il gère " + prim.MaxHz + " Hz — bouton « Réglages écran ».");
                    else
                        Add(auto, 0, "Écran principal à sa fréquence max", prim.Name + " à " + prim.CurrentHz + " Hz (max).");
                }
            }
            catch { }

            // ---- Timer 1 ms ----
            try
            {
                double ms = Native.CurrentTimerMs();
                if (ms > 1.2)
                    Add(auto, 1, "Timer système à 1 ms", "Actuel " + ms.ToString("0.0") + " ms — active MODE JEU ou la case Timer.");
                else
                    Add(auto, 0, "Timer système à 1 ms", "Actuel " + ms.ToString("0.0") + " ms.");
            }
            catch { }

            // ---- Accélération souris ----
            try
            {
                object v = Sys.GetUser(@"Control Panel\Mouse", "MouseSpeed");
                bool off = v != null && Convert.ToString(v) == "0";
                if (off) Add(auto, 0, "Accélération souris désactivée", "Prise directe (0).");
                else Add(auto, 1, "Accélération souris désactivée", "Encore active — coche « Désactiver l'accélération de la souris ».");
            }
            catch { }

            // ---- MSI USB ----
            try
            {
                bool? msi = Sys.MsiActiveForClass(Sys.MsiUsbClass);
                if (msi == true) Add(auto, 0, "Mode MSI sur les contrôleurs USB", "Interruptions directes pour souris/clavier.");
                else Add(auto, 1, "Mode MSI sur les contrôleurs USB", "Non actif — coche « Mode MSI sur les contrôleurs USB ».");
            }
            catch { }

            // ---- HAGS ----
            try
            {
                bool hags = Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode"), 2);
                Add(auto, hags ? 0 : 1, "Planification GPU matérielle (HAGS)",
                    hags ? "Active." : "Inactive — coche « HAGS » (redémarrage).");
            }
            catch { }

            // ---- Plan d'alimentation ----
            try
            {
                string sch = Convert.ToString(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", "ActivePowerScheme"));
                bool perf = sch != null && (sch.IndexOf(HighPerf, StringComparison.OrdinalIgnoreCase) >= 0
                                         || sch.IndexOf(Ultimate, StringComparison.OrdinalIgnoreCase) >= 0);
                Add(auto, perf ? 0 : 1, "Plan d'alimentation performance",
                    perf ? "Performances élevées / ultimes." : "Plan équilibré — active « Performances ultimes ».");
            }
            catch { }

            // ---- Rappels non détectables (le plus gros levier réel) ----
            Add(todo, 2, "NVIDIA Reflex : ON en jeu",
                "Le réducteur de latence n°1 aujourd'hui. Active « Reflex / Faible latence » dans chaque jeu (Valorant, CS2, Fortnite, OW2, COD...).");
            Add(todo, 2, "Souris à 1000 Hz+ (polling)",
                "Règle la fréquence d'interrogation au maximum dans le logiciel de ta souris (G HUB, etc.).");
            Add(todo, 2, "Plein écran EXCLUSIF",
                "Joue en plein écran exclusif, pas en fenêtré sans bordure, quand tu veux la latence la plus basse.");
            Add(todo, 2, "Écran : câble DisplayPort + OSD",
                "La fréquence max exige souvent le câble DisplayPort fourni et le bon mode dans le menu (OSD) de l'écran.");
            Add(todo, 2, "VRR / G-Sync activé",
                "Active G-Sync/FreeSync sur l'écran de jeu (panneau NVIDIA) : moins de déchirure et de latence de présentation.");

            _list.EndUpdate();
            Cursor = Cursors.Default;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_tip != null) { try { _tip.RemoveAll(); _tip.Dispose(); } catch { } _tip = null; }
                if (_ownedFonts != null)
                {
                    foreach (Font f in _ownedFonts) { try { f.Dispose(); } catch { } }
                    _ownedFonts.Clear();
                }
            }
            base.Dispose(disposing);
        }
    }
}
