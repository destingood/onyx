using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Management;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// 💾 Jeux & disques : type (SSD/HDD) et espace libre de chaque disque, et sur QUEL disque
    /// sont installés tes jeux. Un jeu sur disque dur mécanique = chargements lents et saccades
    /// de streaming ; un disque système presque plein = Windows qui rame. Lecture seule.
    /// </summary>
    internal class DiskForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _drives, _games;
        private Label _verdict;
        private Button _btnScan, _btnClose;
        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        public DiskForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Jeux & disques";
            ClientSize = new Size(680, 520);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  💾 Jeux & disques — tes jeux sont-ils sur le bon disque ?",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var l1 = new Label { Text = "Disques", Location = new Point(18, 60), AutoSize = true, Font = new Font("Segoe UI Semibold", 9.5f) };
            Controls.Add(l1);
            _drives = new ListView
            {
                Location = new Point(18, 82), Size = new Size(644, 128),
                View = View.Details, FullRowSelect = true, GridLines = false
            };
            _drives.Columns.Add("Disque", 130);
            _drives.Columns.Add("Type", 120);
            _drives.Columns.Add("Libre", 130, HorizontalAlignment.Right);
            _drives.Columns.Add("Total", 130, HorizontalAlignment.Right);
            _drives.Columns.Add("% libre", 120, HorizontalAlignment.Right);
            Controls.Add(_drives);

            var l2 = new Label { Text = "Jeux détectés", Location = new Point(18, 218), AutoSize = true, Font = new Font("Segoe UI Semibold", 9.5f) };
            Controls.Add(l2);
            _games = new ListView
            {
                Location = new Point(18, 240), Size = new Size(644, 160),
                View = View.Details, FullRowSelect = true, GridLines = false
            };
            _games.Columns.Add("Jeu", 240);
            _games.Columns.Add("Disque", 90);
            _games.Columns.Add("Type", 120);
            _games.Columns.Add("Conseil", 190);
            Controls.Add(_games);

            _verdict = new Label
            {
                Location = new Point(18, 408), Size = new Size(644, 56), ForeColor = Color.FromArgb(60, 64, 72),
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(_verdict);

            _btnScan = MakeBtn("Ré-analyser", 18, 470, 130, 38, false);
            _btnScan.Click += (s, e) => Scan();
            // Lien fonction → outil : santé S.M.A.R.T. + vitesse + occupation de l'espace.
            var btnTools = MakeBtn("💾 Outils disque : CrystalDiskInfo / WizTree", 158, 470, 330, 38, false);
            btnTools.Click += (s, e) => LibScan.OpenTools(this, _log,
                new[] { "CrystalDewWorld.CrystalDiskInfo", "AntibodySoftware.WizTree", "CrystalDewWorld.CrystalDiskMark" });
            _btnClose = MakeBtn("Fermer", 572, 470, 90, 38, true);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnScan); Controls.Add(btnTools); Controls.Add(_btnClose);
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

        // Lettre de lecteur -> "SSD" / "HDD" / "?" (via MSFT_Partition -> MSFT_PhysicalDisk).
        private static Dictionary<char, string> LetterTypes()
        {
            var diskType = new Dictionary<int, string>();   // numéro de disque -> type
            var result = new Dictionary<char, string>();
            try
            {
                using (var pd = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage", "SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk"))
                    foreach (ManagementObject mo in pd.Get())
                    {
                        int num; if (!int.TryParse(Convert.ToString(mo["DeviceId"]), out num)) continue;
                        int mt = mo["MediaType"] == null ? 0 : Convert.ToInt32(mo["MediaType"]);
                        diskType[num] = mt == 4 ? "SSD" : mt == 3 ? "HDD (mécanique)" : mt == 5 ? "SSD (SCM)" : "?";
                    }
                using (var part = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage", "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
                    foreach (ManagementObject mo in part.Get())
                    {
                        object dl = mo["DriveLetter"];
                        if (dl == null) continue;
                        char letter = Convert.ToChar(dl);
                        if (letter == '\0') continue;
                        int dn = Convert.ToInt32(mo["DiskNumber"]);
                        string t; if (diskType.TryGetValue(dn, out t)) result[char.ToUpperInvariant(letter)] = t;
                    }
            }
            catch { }
            return result;
        }

        private void Scan()
        {
            Cursor = Cursors.WaitCursor;
            _btnScan.Enabled = false;
            _verdict.Text = "Analyse des disques et des jeux...";
            Task.Run(() =>
            {
                Dictionary<char, string> types = LetterTypes();
                var games = GameScan.Known();
                GameScan.Detect(games);
                try { BeginInvoke((Action)(() => Populate(types, games))); } catch { }
            });
        }

        private void Populate(Dictionary<char, string> types, List<GameScan.GameInfo> games)
        {
            _drives.Items.Clear();
            bool lowSpace = false;
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    char letter = char.ToUpperInvariant(d.Name[0]);
                    string type; if (!types.TryGetValue(letter, out type)) type = "?";
                    double freeGB = d.AvailableFreeSpace / 1073741824.0;
                    double totGB = d.TotalSize / 1073741824.0;
                    double pct = totGB > 0 ? freeGB / totGB * 100 : 0;

                    var it = new ListViewItem(d.Name + "  " + (string.IsNullOrEmpty(d.VolumeLabel) ? "" : d.VolumeLabel));
                    it.SubItems.Add(type);
                    it.SubItems.Add(freeGB.ToString("N0") + " Go");
                    it.SubItems.Add(totGB.ToString("N0") + " Go");
                    it.SubItems.Add(pct.ToString("0") + " %");
                    if (pct < 8 || freeGB < 15) { it.ForeColor = Color.FromArgb(200, 60, 40); lowSpace = true; }
                    else if (pct < 15) it.ForeColor = Color.FromArgb(200, 110, 0);
                    _drives.Items.Add(it);
                }
                catch { }
            }

            _games.Items.Clear();
            int onHdd = 0, detected = 0;
            foreach (GameScan.GameInfo g in games)
            {
                if (!g.Detected) continue;
                detected++;
                string drive = "?", type = "?";
                if (!string.IsNullOrEmpty(g.InstallPath) && g.InstallPath.Length >= 2 && g.InstallPath[1] == ':')
                {
                    char letter = char.ToUpperInvariant(g.InstallPath[0]);
                    drive = letter + ":";
                    types.TryGetValue(letter, out type);
                    if (type == null) type = "?";
                }
                var it = new ListViewItem(g.Name);
                it.SubItems.Add(drive);
                it.SubItems.Add(type);
                bool hdd = type.StartsWith("HDD");
                it.SubItems.Add(hdd ? "→ déplace-le sur SSD (chargements + rapides)" : (type == "SSD" || type.StartsWith("SSD") ? "bien placé ✔" : ""));
                if (hdd) { it.ForeColor = Color.FromArgb(200, 110, 0); onHdd++; }
                _games.Items.Add(it);
            }

            if (onHdd > 0)
            {
                _verdict.ForeColor = Color.FromArgb(200, 110, 0);
                _verdict.Text = "→ " + onHdd + " jeu(x) sur disque dur mécanique : les déplacer sur un SSD réduit fortement les temps "
                    + "de chargement et les saccades de streaming de textures. (Steam : clic droit sur le jeu → Propriétés → "
                    + "Fichiers installés → Déplacer le dossier d'installation.)";
            }
            else if (lowSpace)
            {
                _verdict.ForeColor = Color.FromArgb(200, 60, 40);
                _verdict.Text = "→ Un disque est presque plein (< 8 % ou < 15 Go) : Windows et les jeux ralentissent, les mises à "
                    + "jour peuvent échouer. Fais le ménage (Nettoyage disque) ou libère de l'espace.";
            }
            else if (detected == 0)
            {
                _verdict.ForeColor = Color.FromArgb(60, 64, 72);
                _verdict.Text = "Aucun jeu connu détecté automatiquement. Les infos disques ci-dessus restent utiles "
                    + "(type SSD/HDD, espace libre).";
            }
            else
            {
                _verdict.ForeColor = Accent;
                _verdict.Text = "✔ Tes jeux détectés sont sur SSD et tes disques ont de l'espace. Rien à déplacer.";
            }

            if (_log != null) _log("Disques : " + detected + " jeu(x) détecté(s), " + onHdd + " sur HDD"
                + (lowSpace ? ", disque presque plein" : "") + ".", (onHdd > 0 || lowSpace) ? 2 : 0);
            _btnScan.Enabled = true;
            Cursor = Cursors.Default;
        }
    }
}
