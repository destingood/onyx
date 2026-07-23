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
    /// Jeux & disques : type (SSD/HDD) et espace libre de chaque disque, et sur QUEL disque
    /// sont installés tes jeux. Un jeu sur disque dur mécanique = chargements lents et saccades
    /// de streaming ; un disque système presque plein = Windows qui rame. Lecture seule.
    /// </summary>
    internal class DiskForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _drives, _games;
        private Label _verdict;
        private Button _btnScan, _btnClose;
        private static readonly Color Accent = Color.FromArgb(79, 70, 229);

        public DiskForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "Fluide — Jeux & disques";
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
                Text = "  Jeux & disques — tes jeux sont-ils sur le bon disque ?",
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
            _drives.Columns.Add("Disque", 116);
            _drives.Columns.Add("Type", 94);
            _drives.Columns.Add("Santé (S.M.A.R.T.)", 156);
            _drives.Columns.Add("Libre", 96, HorizontalAlignment.Right);
            _drives.Columns.Add("Total", 96, HorizontalAlignment.Right);
            _drives.Columns.Add("% libre", 72, HorizontalAlignment.Right);
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
            // La santé S.M.A.R.T. est désormais affichée NATIVEMENT ci-dessus. Le bouton n'est
            // qu'un « aller plus loin » : WizTree (carte de l'espace), CrystalDiskMark (vitesse).
            var btnTools = MakeBtn("Aller plus loin : WizTree / benchmark", 158, 470, 330, 38, false);
            LibScan.WireToolButton(btnTools, this, _log, "Aller plus loin : WizTree / benchmark",
                new[] { "AntibodySoftware.WizTree", "CrystalDewWorld.CrystalDiskMark", "CrystalDewWorld.CrystalDiskInfo" });
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

        /// <summary>Santé S.M.A.R.T. native d'un disque (comme CrystalDiskInfo, mais via WMI —
        /// aucun pilote noyau) : type, état, température et usure SSD si le disque les expose.</summary>
        internal class DiskMeta
        {
            public string Type = "?";
            public string Health;   // "Sain" / "⚠ Attention" / "✗ Défaillant" / null (inconnu)
            public int TempC = -1;
            public int Wear = -1;   // % d'usure (SSD) quand disponible
        }

        private static string HealthLabel(object h)
        {
            if (h == null) return null;
            try
            {
                switch (Convert.ToInt32(h))
                {
                    case 0: return "Sain";
                    case 1: return "⚠ Attention";
                    case 2: return "✗ Défaillant";
                }
            }
            catch { }
            return null;
        }

        /// <summary>Nombre de disques physiques dont l'état S.M.A.R.T. est DÉGRADÉ (Attention ou
        /// Défaillant), avec un libellé. 0 = tous sains. Source native MSFT_PhysicalDisk (aucun
        /// pilote noyau). Réutilisé par le bilan Santé /100. À appeler en arrière-plan (WMI).</summary>
        public static int UnhealthyDisks(out string detail)
        {
            detail = "";
            int bad = 0;
            var names = new List<string>();
            try
            {
                using (var pd = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage", "SELECT FriendlyName, HealthStatus FROM MSFT_PhysicalDisk"))
                    foreach (ManagementObject mo in pd.Get())
                    {
                        int hs; try { hs = Convert.ToInt32(mo["HealthStatus"]); } catch { continue; }
                        if (hs != 1 && hs != 2) continue;
                        bad++;
                        string nm = Convert.ToString(mo["FriendlyName"]);
                        names.Add((string.IsNullOrEmpty(nm) ? "disque" : nm) + (hs == 2 ? " (défaillant)" : " (attention)"));
                    }
            }
            catch { }
            detail = string.Join(", ", names.ToArray());
            return bad;
        }

        // Lettre de lecteur -> métadonnées disque (type + santé S.M.A.R.T.), via l'espace Storage.
        private static Dictionary<char, DiskMeta> LetterInfo()
        {
            var byNum = new Dictionary<int, DiskMeta>();     // numéro de disque -> méta
            var result = new Dictionary<char, DiskMeta>();
            try
            {
                using (var pd = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage", "SELECT DeviceId, MediaType, HealthStatus FROM MSFT_PhysicalDisk"))
                    foreach (ManagementObject mo in pd.Get())
                    {
                        int num; if (!int.TryParse(Convert.ToString(mo["DeviceId"]), out num)) continue;
                        var m = new DiskMeta();
                        int mt = mo["MediaType"] == null ? 0 : Convert.ToInt32(mo["MediaType"]);
                        m.Type = mt == 4 ? "SSD" : mt == 3 ? "HDD (mécanique)" : mt == 5 ? "SSD (SCM)" : "?";
                        m.Health = HealthLabel(mo["HealthStatus"]);
                        byNum[num] = m;
                    }

                // Compteurs de fiabilité (S.M.A.R.T. moderne) : température + usure, best-effort.
                try
                {
                    using (var rc = new ManagementObjectSearcher(
                        @"root\Microsoft\Windows\Storage", "SELECT DeviceId, Temperature, Wear FROM MSFT_StorageReliabilityCounter"))
                        foreach (ManagementObject mo in rc.Get())
                        {
                            int num; if (!int.TryParse(Convert.ToString(mo["DeviceId"]), out num)) continue;
                            DiskMeta m; if (!byNum.TryGetValue(num, out m)) continue;
                            try { if (mo["Temperature"] != null) { int t = Convert.ToInt32(mo["Temperature"]); if (t > 0 && t < 120) m.TempC = t; } } catch { }
                            try { if (mo["Wear"] != null) { int w = Convert.ToInt32(mo["Wear"]); if (w >= 0 && w <= 100) m.Wear = w; } } catch { }
                        }
                }
                catch { }

                using (var part = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Storage", "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
                    foreach (ManagementObject mo in part.Get())
                    {
                        object dl = mo["DriveLetter"];
                        if (dl == null) continue;
                        char letter = Convert.ToChar(dl);
                        if (letter == '\0') continue;
                        int dn = Convert.ToInt32(mo["DiskNumber"]);
                        DiskMeta m; if (byNum.TryGetValue(dn, out m)) result[char.ToUpperInvariant(letter)] = m;
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
                Dictionary<char, DiskMeta> metas = LetterInfo();
                var games = GameScan.Known();
                GameScan.Detect(games);
                try { BeginInvoke((Action)(() => Populate(metas, games))); } catch { }
            });
        }

        private void Populate(Dictionary<char, DiskMeta> metas, List<GameScan.GameInfo> games)
        {
            _drives.Items.Clear();
            bool lowSpace = false, badHealth = false;
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    char letter = char.ToUpperInvariant(d.Name[0]);
                    DiskMeta m; if (!metas.TryGetValue(letter, out m)) m = new DiskMeta();
                    double freeGB = d.AvailableFreeSpace / 1073741824.0;
                    double totGB = d.TotalSize / 1073741824.0;
                    double pct = totGB > 0 ? freeGB / totGB * 100 : 0;

                    // Colonne Santé native (S.M.A.R.T.) : état + température + usure quand exposés.
                    string health = m.Health ?? "n/d";
                    if (m.TempC >= 0) health += "  " + m.TempC + "°C";
                    if (m.Wear >= 0) health += "  usure " + m.Wear + "%";
                    bool unhealthy = m.Health != null && m.Health != "Sain";

                    var it = new ListViewItem(d.Name + "  " + (string.IsNullOrEmpty(d.VolumeLabel) ? "" : d.VolumeLabel));
                    it.SubItems.Add(m.Type);
                    it.SubItems.Add(health);
                    it.SubItems.Add(freeGB.ToString("N0") + " Go");
                    it.SubItems.Add(totGB.ToString("N0") + " Go");
                    it.SubItems.Add(pct.ToString("0") + " %");
                    if (unhealthy) { it.ForeColor = Color.FromArgb(200, 60, 40); badHealth = true; }
                    else if (pct < 8 || freeGB < 15) { it.ForeColor = Color.FromArgb(200, 60, 40); lowSpace = true; }
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
                    DiskMeta gm; if (metas.TryGetValue(letter, out gm)) type = gm.Type;
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

            if (badHealth)
            {
                _verdict.ForeColor = Color.FromArgb(200, 60, 40);
                _verdict.Text = "⚠ Un disque signale un état S.M.A.R.T. dégradé (« Attention » ou « Défaillant ») : SAUVEGARDE "
                    + "tes données maintenant et prévois son remplacement. C'est le disque, pas un réglage.";
            }
            else if (onHdd > 0)
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
                + (lowSpace ? ", disque presque plein" : "") + (badHealth ? ", SANTÉ S.M.A.R.T. dégradée" : "") + ".",
                (badHealth || onHdd > 0 || lowSpace) ? 2 : 0);
            _btnScan.Enabled = true;
            Cursor = Cursors.Default;
        }
    }
}
