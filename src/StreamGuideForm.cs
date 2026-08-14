using System;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Guide « Streamer sans lag » : codifie la config RTSS / OBS / NVIDIA / écran pour jouer et
    /// streamer des jeux peu optimisés sans saccades (d'après le guide CAPET). Détecte les outils
    /// installés (OBS, Afterburner+RTSS) et rappelle les réglages à faire dans chaque logiciel.
    /// Rien n'est écrit sur le système : c'est un guide + des raccourcis vers les bons endroits.
    /// </summary>
    internal class StreamGuideForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private ToolTip _tip;
        private List<Font> _ownedFonts;
        private Font _bold;

        private Font Own(Font f) { _ownedFonts.Add(f); return f; }

        public StreamGuideForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Load += (s, e) => Reload();
        }

        private void Build()
        {
            Text = "ONYX — Streamer sans lag";
            ClientSize = new Size(680, 580);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(580, 480);
            BackColor = Color.FromArgb(245, 246, 248);
            _ownedFonts = new List<Font>();
            Font = Own(new Font("Segoe UI", 9f));
            _bold = Own(new Font("Segoe UI Semibold", 9.5f));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            _tip = new ToolTip { AutoPopDelay = 25000, InitialDelay = 300 };

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Streamer sans lag", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = Own(new Font("Segoe UI Semibold", 13f)), TextAlign = ContentAlignment.MiddleLeft
            });

            var intro = new Label
            {
                Dock = DockStyle.Top, Height = 34, Padding = new Padding(14, 7, 12, 2),
                ForeColor = Color.FromArgb(90, 95, 105),
                Text = "Config RTSS / OBS / NVIDIA / écran pour jouer ET streamer sans saccades. Vert = prêt, orange = à installer."
            };

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, ShowGroups = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable, Font = Own(new Font("Segoe UI", 9.5f))
            };
            _list.Columns.Add("Étape", 250);
            _list.Columns.Add("Détail / conseil", 400);
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 6) };
            host.Controls.Add(_list);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(12, 7, 12, 7) };
            var tools = MakeBtn("Installer OBS / outils...", 170, DockStyle.Left);
            tools.Click += (s, e) => { try { LibScan.OpenTools(this, _log, new[] { "OBSProject.OBSStudio", "Guru3D.Afterburner" }); } catch { } Reload(); };
            _tip.SetToolTip(tools, "Installe OBS Studio et MSI Afterburner (RTSS est fourni avec) via winget.");
            var gpu = MakeBtn("Panneau NVIDIA...", 150, DockStyle.Left);
            // « nvcpl.cpl » n'existe plus depuis des années : le bouton ne faisait rien, en
            // silence. Voir PanneauNvidia — et on prévient désormais quand rien ne s'ouvre.
            gpu.Click += (s, e) =>
            {
                if (!PanneauNvidia.Ouvrir())
                    MessageBox.Show(this, "Le panneau NVIDIA est introuvable sur cette machine.\n\n"
                        + "Ouvre-le par un clic droit sur le bureau, ou depuis le menu Démarrer.",
                        "ONYX", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            _tip.SetToolTip(gpu, "Panneau NVIDIA : mise à l'échelle plein écran, faible latence, fréquence préférée.");
            var scr = MakeBtn("Réglages écran...", 150, DockStyle.Left);
            scr.Click += (s, e) => Shell("ms-settings:display", null);
            _tip.SetToolTip(scr, "Mise à l'échelle par écran (secondaire 100 %, principal selon ta résolution).");
            var close = MakeBtn("Fermer", 90, DockStyle.Right);
            close.Click += (s, e) => Close();
            bottom.Controls.Add(new Label { Dock = DockStyle.Fill });
            bottom.Controls.Add(scr); bottom.Controls.Add(gpu); bottom.Controls.Add(tools);
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

        // level : 0 OK (vert), 1 à faire/installer (orange), 2 rappel (gris info)
        private void Add(ListViewGroup g, int level, string name, string advice)
        {
            string icon = level == 0 ? "✓ " : (level == 1 ? "! " : "• ");
            Color c = level == 0 ? Color.FromArgb(0, 140, 80)
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

            var outils = new ListViewGroup("Outils (installés automatiquement détectés)") { HeaderAlignment = HorizontalAlignment.Left };
            var rtss = new ListViewGroup("RTSS — limiteur de FPS") { HeaderAlignment = HorizontalAlignment.Left };
            var obs = new ListViewGroup("OBS — capture sans input lag") { HeaderAlignment = HorizontalAlignment.Left };
            var affichage = new ListViewGroup("NVIDIA & écran") { HeaderAlignment = HorizontalAlignment.Left };
            _list.Groups.Add(outils);
            _list.Groups.Add(rtss);
            _list.Groups.Add(obs);
            _list.Groups.Add(affichage);

            // ---- Outils détectés ----
            bool obsOk = false, abOk = false;
            try { obsOk = LibScan.InstalledById("OBSProject.OBSStudio"); } catch { }
            try { abOk = LibScan.InstalledById("Guru3D.Afterburner"); } catch { }
            Add(outils, obsOk ? 0 : 1, "OBS Studio",
                obsOk ? "Installé." : "Absent — bouton « Installer OBS / outils ».");
            Add(outils, abOk ? 0 : 1, "MSI Afterburner + RTSS",
                abOk ? "Installé (RTSS fourni avec Afterburner)." : "Absent — bouton « Installer OBS / outils » (RTSS = limiteur de FPS).");

            // ---- Écran à sa fréquence max (levier partagé avec le guide latence) ----
            try
            {
                var modes = DisplayInfo.Query();
                DisplayInfo.DisplayMode prim = null;
                foreach (var d in modes) if (d.Primary) { prim = d; break; }
                if (prim == null && modes.Count > 0) prim = modes[0];
                if (prim != null)
                    Add(outils, prim.BelowMax ? 1 : 0, "Écran principal à sa fréquence max",
                        prim.BelowMax ? prim.CurrentHz + " Hz alors qu'il gère " + prim.MaxHz + " Hz — bouton « Réglages écran »."
                                      : prim.Name + " à " + prim.CurrentHz + " Hz (max).");
            }
            catch { }

            // ---- RTSS ----
            Add(rtss, 2, "Limiter les FPS dans RTSS",
                "Règle « Framerate limit » (ex. 240 ou 144) pour des frametimes stables. 0 = illimité.");
            Add(rtss, 2, "Activer « Use Microsoft Detours API hooking »",
                "Dans les réglages RTSS : rend l'overlay et la capture OBS plus fiables.");

            // ---- OBS ----
            Add(obs, 2, "Utiliser « Capture de jeu »",
                "Source « Capture de jeu » (Game Capture), PAS « Capture d'écran » : bien moins coûteux.");
            Add(obs, 2, "Désactiver l'aperçu (clic droit → « Activer l'aperçu »)",
                "L'aperçu en direct ajoute de l'input lag : coupe-le pendant que tu joues.");
            Add(obs, 2, "Limiter le FPS de capture",
                "Coche « Limiter la fréquence d'images de la capture » pour ne pas voler des ressources au jeu.");
            Add(obs, 2, "Capturer les surcouches tierces",
                "Coche « Capturer les surcouches tierces » si tu veux voir l'overlay Steam/RTSS à l'écran.");

            // ---- NVIDIA & écran ----
            Add(affichage, 2, "NVIDIA : mise à l'échelle plein écran",
                "Panneau NVIDIA → « Ajuster la taille et la position du bureau » → Mise à l'échelle « Plein écran » : évite les bandes noires en résolution réduite pour gagner des FPS.");
            Add(affichage, 2, "Windows : mise à l'échelle par écran",
                "Écran secondaire à 100 % ; principal 100 % ou 150 % selon ta résolution (ex. 1440p à 150 %). Bouton « Réglages écran ».");

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
