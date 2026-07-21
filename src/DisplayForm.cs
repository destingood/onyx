using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Réglages d'écran gaming : vérifie que chaque écran tourne à sa fréquence MAX
    /// (le piège classique : un 144/240 Hz resté à 60 Hz), avec correction en un clic.
    /// Rappelle aussi les réglages qui se font dans Windows/NVIDIA (VRR/G-Sync, HDR, échelle),
    /// avec les raccourcis pour y aller. Réversible (SetHz teste avant d'appliquer).
    /// </summary>
    internal class DisplayForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private Label _verdict;
        private Button _btnScan, _btnMax, _btnWin, _btnGpu, _btnClose;
        private List<DisplayInfo.DisplayMode> _modes = new List<DisplayInfo.DisplayMode>();

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private static readonly Color Warn = Color.FromArgb(200, 110, 0);

        public DisplayForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Réglages d'écran";
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
                Text = "  Réglages d'écran — ton écran tourne-t-il à sa fréquence max ?",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            _list = new ListView
            {
                Location = new Point(18, 66), Size = new Size(644, 150),
                View = View.Details, FullRowSelect = true, GridLines = false
            };
            _list.Columns.Add("Écran", 250);
            _list.Columns.Add("Résolution", 130);
            _list.Columns.Add("Fréquence", 130);
            _list.Columns.Add("État", 130);
            Controls.Add(_list);

            _btnMax = MakeBtn("⬆ Passer tous les écrans à leur fréquence MAX", 18, 224, 380, 36, true);
            _btnMax.Click += OnMax;
            Controls.Add(_btnMax);

            var check = new Label
            {
                Text = "Ces réglages ne se lisent pas de façon fiable depuis une app — ouvre-les et vérifie :\n"
                     + "   • VRR / G-Sync / FreeSync : activé pour supprimer le tearing sans V-Sync (latence).\n"
                     + "   • HDR : à activer seulement si ton écran le gère VRAIMENT (sinon couleurs délavées).\n"
                     + "   • Mise à l'échelle : 100 % pour des pixels exacts, ou la valeur native de l'écran.\n"
                     + "   • Un seul écran « principal » = celui où tu joues (barre des tâches).",
                Location = new Point(18, 272), Size = new Size(644, 120), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(check);

            _btnWin = MakeBtn("Ouvrir les réglages d'affichage Windows", 18, 392, 300, 34, false);
            _btnWin.Click += (s, e) => OpenUri("ms-settings:display");
            _btnGpu = MakeBtn("Ouvrir le panneau NVIDIA / graphique", 330, 392, 300, 34, false);
            _btnGpu.Click += OnGpuPanel;
            Controls.Add(_btnWin); Controls.Add(_btnGpu);

            _verdict = new Label
            {
                Location = new Point(18, 434), Size = new Size(500, 40), ForeColor = Color.FromArgb(60, 64, 72),
                Font = new Font("Segoe UI Semibold", 9.5f)
            };
            Controls.Add(_verdict);

            _btnScan = MakeBtn("Ré-analyser", 18, 434, 0, 0, false); _btnScan.Visible = false; // (scan auto)
            _btnClose = MakeBtn("Fermer", 572, 456, 90, 34, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnClose);
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

        private void Scan()
        {
            _list.Items.Clear();
            _modes = DisplayInfo.Query();
            int belowMax = 0;
            foreach (DisplayInfo.DisplayMode m in _modes)
            {
                var it = new ListViewItem(m.Name + (m.Primary ? "  (principal)" : ""));
                it.SubItems.Add(m.Width + " × " + m.Height);
                it.SubItems.Add(m.CurrentHz + " Hz  (max " + m.MaxHz + ")");
                if (m.BelowMax) { it.SubItems.Add("⚠ sous le max"); it.ForeColor = Warn; belowMax++; }
                else it.SubItems.Add("✔ au max");
                _list.Items.Add(it);
            }
            _btnMax.Enabled = belowMax > 0;
            if (_modes.Count == 0)
            {
                _verdict.ForeColor = Color.FromArgb(60, 64, 72);
                _verdict.Text = "Aucun écran détecté.";
            }
            else if (belowMax > 0)
            {
                _verdict.ForeColor = Warn;
                _verdict.Text = belowMax + " écran(s) SOUS leur fréquence max — clique sur le bouton bleu.";
            }
            else
            {
                _verdict.ForeColor = Accent;
                _verdict.Text = "✔ Tous tes écrans tournent à leur fréquence maximale.";
            }
        }

        private void OnMax(object sender, EventArgs e)
        {
            int done = 0, fail = 0;
            foreach (DisplayInfo.DisplayMode m in _modes)
            {
                if (!m.BelowMax) continue;
                if (DisplayInfo.SetHz(m.Device, m.MaxHz)) { done++; if (_log != null) _log(m.Name + " → " + m.MaxHz + " Hz.", 1); }
                else { fail++; if (_log != null) _log(m.Name + " : passage à " + m.MaxHz + " Hz refusé.", 2); }
            }
            MessageBox.Show(this,
                done + " écran(s) passé(s) à leur fréquence max" + (fail > 0 ? ", " + fail + " échec(s)" : "") + ".",
                "DesTinGOOD", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Scan();
        }

        private void OnGpuPanel(object sender, EventArgs e)
        {
            // NVIDIA : nvcplui.exe si présent, sinon réglages graphiques Windows.
            string[] cands =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvcplui.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\Control Panel Client\nvcplui.exe"),
            };
            foreach (string c in cands)
                if (File.Exists(c)) { try { Process.Start(new ProcessStartInfo(c) { UseShellExecute = true }); return; } catch { } }
            OpenUri("ms-settings:display-advanced-graphics");
        }

        private void OpenUri(string uri)
        {
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
            catch (Exception ex) { if (_log != null) _log("Ouverture impossible : " + ex.Message, 2); }
        }
    }
}
