using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Panneau « État de la licence Windows » : lecture seule, légal. Affiche l'activation,
    /// le type de licence, l'édition, la clé partielle et la clé OEM du firmware, + un bouton vers
    /// l'activation Windows officielle. Ne modifie ni ne contourne rien.</summary>
    internal class WindowsLicenseForm : Form
    {
        private Label _edition, _status, _channel, _partial, _oem, _note;
        private Button _copyOem;
        private string _oemKey;

        public WindowsLicenseForm()
        {
            Text = "ONYX — État de la licence Windows";
            ClientSize = new Size(560, 400);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            Theme.Apply(this);
            LoadAsync();
        }

        private void BuildUi()
        {
            var banner = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(12, 14, 13) };
            banner.Controls.Add(new Label
            {
                Text = "  État de la licence Windows", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 14f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            int y = 78;
            _status = Row("Statut", 20, ref y, big: true);
            _edition = Row("Édition", 20, ref y);
            _channel = Row("Type de licence", 20, ref y);
            _partial = Row("Clé partielle", 20, ref y);
            _oem = Row("Clé OEM (firmware)", 20, ref y);

            _copyOem = new Button
            {
                Text = "Copier la clé OEM", Location = new Point(180, _oem.Top + 22), Size = new Size(160, 26),
                FlatStyle = FlatStyle.Flat, Visible = false
            };
            _copyOem.Click += (s, e) => { try { Clipboard.SetText(_oemKey); _copyOem.Text = "Copié ✓"; } catch { } };
            Controls.Add(_copyOem);
            y += 30;

            _note = new Label
            {
                Location = new Point(20, y + 6), Size = new Size(520, 70), ForeColor = Theme.InkDimColor,
                Text = "Lecture en cours…"
            };
            Controls.Add(_note);

            var openAct = new Button
            {
                Text = "Ouvrir l'activation Windows", Location = new Point(20, 356), Size = new Size(220, 30),
                FlatStyle = FlatStyle.Flat, BackColor = Theme.AccentColor, ForeColor = Color.White
            };
            openAct.FlatAppearance.BorderSize = 0;
            openAct.Click += (s, e) => { try { Process.Start(new ProcessStartInfo("ms-settings:activation") { UseShellExecute = true }); } catch { } };
            Controls.Add(openAct);

            var close = new Button { Text = "Fermer", Location = new Point(440, 356), Size = new Size(100, 30), FlatStyle = FlatStyle.Flat };
            close.Click += (s, e) => Close();
            Controls.Add(close);
        }

        private Label Row(string caption, int x, ref int y, bool big = false)
        {
            var cap = new Label { Text = caption, Location = new Point(x, y), Size = new Size(150, 22), ForeColor = Theme.InkDimColor };
            var val = new Label
            {
                Text = "…", Location = new Point(x + 158, y - (big ? 2 : 0)), Size = new Size(372, big ? 26 : 22),
                ForeColor = Theme.InkColor, Font = big ? new Font("Segoe UI Semibold", 13f, FontStyle.Bold) : Font
            };
            Controls.Add(cap); Controls.Add(val);
            y += big ? 40 : 30;
            return val;
        }

        private void LoadAsync()
        {
            Task.Run(() =>
            {
                WindowsLicense.Info info = null;
                try { info = WindowsLicense.Read(); } catch { }
                try { BeginInvoke((Action)(() => Fill(info))); } catch { }
            });
        }

        private void Fill(WindowsLicense.Info i)
        {
            if (i == null) { _note.Text = "Impossible de lire l'état de licence."; return; }
            _status.Text = i.StatusText;
            _status.ForeColor = i.Activated ? Theme.OkColor : Color.FromArgb(220, 120, 60);
            _edition.Text = i.Edition ?? "Windows";
            _channel.Text = string.IsNullOrEmpty(i.Channel) ? "—" : i.Channel;
            _partial.Text = string.IsNullOrEmpty(i.PartialKey) ? "—" : ("…" + i.PartialKey);
            if (!string.IsNullOrEmpty(i.OemKey))
            {
                _oem.Text = i.OemKey;
                _oem.Font = new Font("Consolas", 10f);
                _oemKey = i.OemKey;
                _copyOem.Visible = true;
            }
            else
            {
                _oem.Text = "aucune (PC sans licence OEM en firmware)";
            }
            _note.Text = i.Note;
        }
    }
}
