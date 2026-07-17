using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Panneau Audio & enceintes : périphériques (COM), améliorations (panneau natif), service audio.</summary>
    internal class AudioForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private Label _defLabel;
        private Button _btnTest, _btnRestart, _btnEnhance, _btnPanel, _btnMixer, _btnClose;
        private ToolTip _tip;
        private List<Font> _ownedFonts;
        private Font _rowBold;
        private string _defName;

        private Font Own(Font f) { _ownedFonts.Add(f); return f; }

        public AudioForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Load += (s, e) => Reload();
        }

        private void Build()
        {
            Text = "BT Optimizer — Audio & enceintes";
            ClientSize = new Size(660, 500);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(580, 420);
            BackColor = Color.FromArgb(245, 246, 248);
            _ownedFonts = new List<Font>();
            Font = Own(new Font("Segoe UI", 9f));
            _rowBold = Own(new Font("Segoe UI Semibold", 9.5f));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            _tip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Audio & enceintes", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = Own(new Font("Segoe UI Semibold", 13f)), TextAlign = ContentAlignment.MiddleLeft
            });

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable, Font = Own(new Font("Segoe UI", 9.5f))
            };
            _list.Columns.Add("Périphérique de lecture actif", 440);
            _list.Columns.Add("Par défaut", 170);
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 4) };
            host.Controls.Add(_list);

            // ---- Améliorations (panneau natif) ----
            var mid = new Panel { Dock = DockStyle.Bottom, Height = 96, Padding = new Padding(12, 4, 12, 4) };
            _defLabel = new Label
            {
                Dock = DockStyle.Top, Height = 22, Font = Own(new Font("Segoe UI Semibold", 9.75f)),
                ForeColor = Color.FromArgb(50, 70, 130), Text = "Périphérique par défaut : ..."
            };
            _btnEnhance = new Button
            {
                Text = "Régler les améliorations du son (loudness, basses, effets)...",
                Dock = DockStyle.Top, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Color.White,
                Font = Own(new Font("Segoe UI", 9.25f)), Margin = new Padding(0, 4, 0, 4)
            };
            _btnEnhance.FlatAppearance.BorderColor = Color.FromArgb(160, 166, 174);
            _btnEnhance.Click += (s, e) => OpenEnhancements();
            _tip.SetToolTip(_btnEnhance, "Ouvre les propriétés du périphérique par défaut (onglet Améliorations / Audio enhancements).\n"
                + "Là tu peux activer l'égalisation de sonie (son plus « plein » à volume modéré), les basses,\n"
                + "l'ambiophonie, ou tout désactiver pour la latence minimale. Réglages 100 % réversibles et sûrs.");
            var note = new Label
            {
                Dock = DockStyle.Bottom, Height = 30, ForeColor = Color.FromArgb(110, 115, 125),
                Text = "Astuce : « Égalisation de sonie » rend les enceintes/casque plus pleins à volume modéré ; "
                     + "tout désactiver donne la latence la plus basse."
            };
            mid.Controls.Add(_btnEnhance);
            mid.Controls.Add(note);
            mid.Controls.Add(_defLabel);
            mid.Controls.SetChildIndex(_defLabel, 0);
            mid.Controls.SetChildIndex(_btnEnhance, 1);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(12, 7, 12, 7) };
            _btnTest = MakeBtn("Son de test", 100, DockStyle.Left);
            _btnTest.Click += (s, e) => { try { System.Media.SystemSounds.Asterisk.Play(); } catch { } };
            _btnRestart = MakeBtn("Redémarrer l'audio", 140, DockStyle.Left);
            _tip.SetToolTip(_btnRestart, "Relance le service audio Windows : résout la plupart des grésillements/coupures sans redémarrer le PC (son coupé ~2 s).");
            _btnRestart.Click += OnRestartAudio;
            _btnPanel = MakeBtn("Panneau Son...", 120, DockStyle.Left);
            _btnPanel.Click += (s, e) => OpenPlaybackPanel();
            _btnMixer = MakeBtn("Mixeur...", 90, DockStyle.Left);
            _btnMixer.Click += (s, e) => StartShell("sndvol.exe", null);
            _btnClose = MakeBtn("Fermer", 100, DockStyle.Right);
            _btnClose.Click += (s, e) => Close();
            bottom.Controls.Add(new Label { Dock = DockStyle.Fill });
            bottom.Controls.Add(_btnMixer); bottom.Controls.Add(_btnPanel);
            bottom.Controls.Add(_btnRestart); bottom.Controls.Add(_btnTest);
            bottom.Controls.Add(_btnClose);

            Controls.Add(banner);
            Controls.Add(host);
            Controls.Add(mid);
            Controls.Add(bottom);
            Controls.SetChildIndex(banner, 3);
            Controls.SetChildIndex(host, 0);
            Controls.SetChildIndex(mid, 2);
            Controls.SetChildIndex(bottom, 1);

            Theme.Apply(this);
        }

        private static Button MakeBtn(string text, int w, DockStyle dock)
        {
            var b = new Button { Text = text, Width = w, Dock = dock, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(4, 0, 4, 0) };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void StartShell(string file, string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true };
                if (args != null) psi.Arguments = args;
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex) { if (_log != null) _log("Ouverture impossible : " + ex.Message, 3); }
        }

        // Boîte de dialogue classique « Périphériques de lecture » (onglet où sont les améliorations).
        private void OpenPlaybackPanel()
        {
            if (!StartShellRaw("rundll32.exe", "shell32.dll,Control_RunDLL mmsys.cpl,,0"))
                StartShell("control.exe", "mmsys.cpl");
        }

        private void OpenEnhancements()
        {
            // Ouvre les propriétés de lecture ; l'onglet Améliorations y est directement accessible.
            OpenPlaybackPanel();
            if (_log != null) _log("Panneau Son ouvert : onglet « Améliorations » du périphérique par défaut.", 0);
        }

        private bool StartShellRaw(string file, string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(file, args) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
                return true;
            }
            catch { return false; }
        }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            Button[] bs = { _btnTest, _btnRestart, _btnEnhance, _btnPanel, _btnMixer };
            foreach (Button b in bs) b.Enabled = !busy;
        }

        private void Reload()
        {
            Cursor = Cursors.WaitCursor;
            List<AudioTools.AudioDevice> devs = AudioTools.ListRender();
            _list.BeginUpdate();
            _list.Items.Clear();
            _defName = null;
            foreach (AudioTools.AudioDevice d in devs)
            {
                var it = new ListViewItem(d.Name);
                it.SubItems.Add(d.IsDefault ? "★ oui" : "");
                if (d.IsDefault) { it.Font = _rowBold; _defName = d.Name; }
                _list.Items.Add(it);
            }
            _list.EndUpdate();
            _defLabel.Text = _defName != null
                ? "Périphérique par défaut : " + _defName
                : (devs.Count > 0 ? "Périphérique par défaut : (indéterminé)" : "Aucun périphérique de lecture actif détecté.");
            Cursor = Cursors.Default;
        }

        private void OnRestartAudio(object sender, EventArgs e)
        {
            SetBusy(true);
            Task.Run(() =>
            {
                try { AudioTools.RestartAudioService(_log ?? delegate { }); } catch { }
                try
                {
                    if (IsDisposed) return;
                    BeginInvoke((Action)(() => { SetBusy(false); Reload(); }));
                }
                catch { }
            });
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
