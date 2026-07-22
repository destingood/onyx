using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// 🎮 Bibliothèque de jeux — liste TOUS les jeux installés, toutes plateformes confondues
    /// (via GameLibrary), avec pastille du launcher, conseils FPS pour les jeux connus, et des
    /// actions strictement OS-level (ouvrir le dossier, priorité CPU). Ligne rouge anti-triche :
    /// pour les jeux protégés, on ne propose QUE des conseils — jamais d'action sur le jeu.
    /// </summary>
    internal class GameLibraryForm : Form
    {
        private readonly Action<string, int> _log;
        private ListView _list;
        private Label _count;
        private Label _detailTitle, _detailMeta, _detailAdvice;
        private Button _btnFolder, _btnCpu, _btnCopy, _btnRescan, _btnClose;
        private List<GameLibrary.InstalledGame> _games = new List<GameLibrary.InstalledGame>();
        private Dictionary<string, GameScan.GameInfo> _known;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private static readonly Color Bg = Color.FromArgb(245, 246, 248);
        private static readonly Color Ink = Color.FromArgb(40, 44, 52);
        private static readonly Color Sub = Color.FromArgb(96, 100, 108);
        private static readonly Color Warn = Color.FromArgb(190, 120, 0);

        // Jeux à anti-triche noyau : on n'agit jamais dessus, conseils uniquement.
        private static readonly string[] AntiCheat =
        {
            "valorant", "fortnite", "counter-strike", "cs2", "rainbow six", "apex", "call of duty",
            "pubg", "destiny 2", "escape from tarkov", "the finals", "rust", "battlefield", "delta force",
            "fc 24", "fc 25", "ea sports fc", "marvel rivals", "r6", "warzone"
        };

        public GameLibraryForm(Action<string, int> log)
        {
            _log = log;
            BuildKnownIndex();
            Build();
            Theme.Apply(this);
            Rescan();
        }

        private void BuildKnownIndex()
        {
            _known = new Dictionary<string, GameScan.GameInfo>();
            try
            {
                foreach (GameScan.GameInfo g in GameScan.Known())
                {
                    string k = Norm(g.Name);
                    if (!string.IsNullOrEmpty(k) && !_known.ContainsKey(k)) _known[k] = g;
                }
            }
            catch { }
        }

        private void Build()
        {
            Text = "DesTinGOOD — Bibliothèque de jeux (toutes plateformes)";
            ClientSize = new Size(760, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(680, 480);
            BackColor = Bg;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  🎮 Bibliothèque — tous tes jeux, toutes plateformes",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            // Détails + actions (bas)
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 176, BackColor = Color.White, Padding = new Padding(14, 10, 14, 10) };
            _detailTitle = new Label { Location = new Point(14, 8), AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), ForeColor = Ink, Text = "Sélectionne un jeu" };
            _detailMeta = new Label { Location = new Point(14, 34), Size = new Size(720, 18), ForeColor = Sub };
            _detailAdvice = new Label { Location = new Point(14, 56), Size = new Size(730, 62), ForeColor = Ink };
            bottom.Controls.Add(_detailTitle); bottom.Controls.Add(_detailMeta); bottom.Controls.Add(_detailAdvice);

            _btnFolder = MakeBtn("Ouvrir le dossier", 14, 128, 150, 34, false);
            _btnFolder.Click += OnOpenFolder;
            _btnCpu = MakeBtn("Priorité CPU…", 174, 128, 150, 34, false);
            _btnCpu.Click += OnCpuPriority;
            _btnCopy = MakeBtn("Copier le conseil", 334, 128, 160, 34, false);
            _btnCopy.Click += OnCopyAdvice;
            bottom.Controls.Add(_btnFolder); bottom.Controls.Add(_btnCpu); bottom.Controls.Add(_btnCopy);
            Controls.Add(bottom);

            // Barre d'actions (bas de tout)
            var actions = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Bg };
            _count = new Label { Location = new Point(16, 14), Size = new Size(360, 22), ForeColor = Sub, Font = new Font("Segoe UI Semibold", 9.5f) };
            _btnRescan = MakeBtn("Actualiser", 470, 8, 130, 32, false);
            _btnRescan.Click += (s, e) => Rescan();
            _btnClose = MakeBtn("Fermer", 640, 8, 100, 32, true);
            _btnClose.Click += (s, e) => Close();
            actions.Controls.Add(_count); actions.Controls.Add(_btnRescan); actions.Controls.Add(_btnClose);
            Controls.Add(actions);

            // Liste (remplit le reste)
            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                HideSelection = false, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 9.5f)
            };
            _list.Columns.Add("Jeu", 250);
            _list.Columns.Add("Plateforme", 120);
            _list.Columns.Add("Dossier d'installation", 360);
            _list.SelectedIndexChanged += (s, e) => UpdateDetails();
            _list.DoubleClick += OnOpenFolder;
            Controls.Add(_list);
            _list.BringToFront();

            SetActionsEnabled(false);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink,
                Font = primary ? new Font("Segoe UI Semibold", 9f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void SetActionsEnabled(bool on)
        {
            _btnFolder.Enabled = on; _btnCpu.Enabled = on; _btnCopy.Enabled = on;
        }

        // ------------------------------------------------------------ scan
        private void Rescan()
        {
            _btnRescan.Enabled = false;
            _count.Text = "Analyse des plateformes en cours…";
            _list.Items.Clear();
            SetActionsEnabled(false);
            Cursor = Cursors.WaitCursor;

            Task.Run(() =>
            {
                List<GameLibrary.InstalledGame> found = GameLibrary.ScanAll();
                try { BeginInvoke((Action)(() => Populate(found))); } catch { }
            });
        }

        private void Populate(List<GameLibrary.InstalledGame> found)
        {
            _games = found ?? new List<GameLibrary.InstalledGame>();
            _list.BeginUpdate();
            _list.Items.Clear();
            var byPlatform = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (GameLibrary.InstalledGame g in _games)
            {
                var it = new ListViewItem(g.Name);
                it.SubItems.Add(g.Launcher ?? "?");
                it.SubItems.Add(g.InstallDir ?? "—");
                it.Tag = g;
                _list.Items.Add(it);
                int n; byPlatform.TryGetValue(g.Launcher ?? "?", out n); byPlatform[g.Launcher ?? "?"] = n + 1;
            }
            _list.EndUpdate();

            var sb = new StringBuilder();
            sb.Append(_games.Count + " jeu(x) détecté(s)");
            if (byPlatform.Count > 0)
            {
                sb.Append("  —  ");
                var parts = new List<string>();
                foreach (KeyValuePair<string, int> kv in byPlatform) parts.Add(kv.Key + " " + kv.Value);
                sb.Append(string.Join(" · ", parts.ToArray()));
            }
            _count.Text = sb.ToString();
            _btnRescan.Enabled = true;
            Cursor = Cursors.Default;
            if (_games.Count == 0)
                _count.Text = "Aucun jeu détecté (Steam/Epic/GOG/Ubisoft/Riot/Battle.net/EA/Xbox). Les launchers non installés sont normaux.";
        }

        // ------------------------------------------------------------ détails
        private GameLibrary.InstalledGame Selected()
        {
            if (_list.SelectedItems.Count == 0) return null;
            return _list.SelectedItems[0].Tag as GameLibrary.InstalledGame;
        }

        private void UpdateDetails()
        {
            GameLibrary.InstalledGame g = Selected();
            if (g == null) { SetActionsEnabled(false); return; }

            _detailTitle.Text = g.Name;
            _detailMeta.Text = "Plateforme : " + (g.Launcher ?? "?")
                + (string.IsNullOrEmpty(g.InstallDir) ? "" : "     •     " + g.InstallDir);

            bool anti = IsAntiCheat(g.Name);
            GameScan.GameInfo known = LookupKnown(g.Name);
            string advice = known != null ? known.Uncap : null;

            if (anti)
            {
                _detailAdvice.ForeColor = Warn;
                _detailAdvice.Text = "⚠ Jeu à anti-triche : conseils uniquement. On ne modifie jamais ses fichiers/process (risque de bannissement)."
                    + (advice != null ? "\r\n" + advice : "");
            }
            else if (advice != null)
            {
                _detailAdvice.ForeColor = Ink;
                _detailAdvice.Text = "💡 Pour plus de FPS : " + advice;
            }
            else
            {
                _detailAdvice.ForeColor = Sub;
                _detailAdvice.Text = "Pas de conseil spécifique connu. Règle générique : limite d'images → illimitée/500, V-Sync off, Reflex/Anti-Lag activé.";
            }

            _btnFolder.Enabled = !string.IsNullOrEmpty(g.InstallDir) && DirExists(g.InstallDir);
            _btnCpu.Enabled = true;   // priorité CPU = OS-level, sûr même pour anti-triche
            _btnCopy.Enabled = true;
        }

        // ------------------------------------------------------------ actions (OS-level only)
        private void OnOpenFolder(object sender, EventArgs e)
        {
            GameLibrary.InstalledGame g = Selected();
            if (g == null || string.IsNullOrEmpty(g.InstallDir) || !DirExists(g.InstallDir)) return;
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + g.InstallDir + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { _log("Ouverture du dossier : " + ex.Message, 2); }
        }

        private void OnCpuPriority(object sender, EventArgs e)
        {
            // Priorité CPU par jeu = réglage Windows (affinité/priorité), neutre pour les anti-triche.
            try { using (var f = new GameProfileForm(_log)) f.ShowDialog(this); }
            catch (Exception ex) { _log("Priorité CPU : " + ex.Message, 2); }
        }

        private void OnCopyAdvice(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_detailAdvice.Text)) return;
            try { Clipboard.SetText(_detailAdvice.Text); _log("Conseil copié dans le presse-papiers.", 0); }
            catch { }
        }

        // ------------------------------------------------------------ helpers
        private static bool DirExists(string p) { try { return Directory.Exists(p); } catch { return false; } }

        private GameScan.GameInfo LookupKnown(string name)
        {
            string k = Norm(name);
            if (string.IsNullOrEmpty(k)) return null;
            GameScan.GameInfo exact;
            if (_known.TryGetValue(k, out exact)) return exact;
            // Rapprochement souple : l'un contient l'autre (normalisés).
            foreach (KeyValuePair<string, GameScan.GameInfo> kv in _known)
                if (k.Contains(kv.Key) || kv.Key.Contains(k)) return kv.Value;
            return null;
        }

        private static bool IsAntiCheat(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            foreach (string a in AntiCheat) if (n.Contains(a)) return true;
            return false;
        }

        // Normalise un nom de jeu pour comparaison (minuscules, alphanumérique seulement).
        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
    }
}
