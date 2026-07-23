using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    internal class GameProfileForm : Form
    {
        private readonly Action<string, int> _log;
        private ListBox _listGames;
        private ComboBox _cboMode;
        private CheckedListBox _listCores;
        private Button _btnApply, _btnClose, _btnAdd;
        
        private List<Game> _games = new List<Game>();
        private Dictionary<string, Tuple<string, long>> _affinityDict;
        private Game _selectedGame;
        private int _cpuCores;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        private class Game
        {
            public string Name; public string[] Exes; public bool Detected;
            public Game(string name, params string[] exes) { Name = name; Exes = exes; }
        }

        private static List<Game> Catalog()
        {
            return new List<Game>
            {
                new Game("Counter-Strike 2", "cs2.exe"),
                new Game("VALORANT", "VALORANT-Win64-Shipping.exe"),
                new Game("Fortnite", "FortniteClient-Win64-Shipping.exe"),
                new Game("Overwatch 2", "Overwatch.exe"),
                new Game("Apex Legends", "r5apex.exe"),
                new Game("Call of Duty", "cod.exe", "cod22-cod.exe", "cod23-cod.exe"),
                new Game("League of Legends", "League of Legends.exe"),
                new Game("Rocket League", "RocketLeague.exe"),
                new Game("Rainbow Six Siege", "RainbowSix.exe", "RainbowSix_Vulkan.exe"),
                new Game("Marvel Rivals", "MarvelRivals.exe"),
                new Game("The Finals", "Discovery.exe"),
                new Game("Dota 2", "dota2.exe"),
                new Game("PUBG", "TslGame.exe"),
            };
        }

        private static string ManualPath { get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-manual-games.txt"); } }

        private static List<Game> ManualGames()
        {
            var list = new List<Game>();
            try
            {
                if (System.IO.File.Exists(ManualPath))
                    foreach (string line in System.IO.File.ReadAllLines(ManualPath))
                    {
                        string[] p = line.Split('|');
                        if (p.Length >= 2 && p[0].Trim().Length > 0 && p[1].Trim().Length > 0)
                            list.Add(new Game(p[0].Trim(), p[1].Trim()));
                    }
            }
            catch { }
            return list;
        }

        public GameProfileForm(Action<string, int> log)
        {
            _log = log;
            _cpuCores = Environment.ProcessorCount;
            _affinityDict = Sys.LoadGameAffinity();
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Booster de jeu (Priorité & Affinité)";
            ClientSize = new Size(680, 480);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Booster Dynamique : Priorité CPU & Affinité (Core Parking)",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Configure l'affinité CPU (cœurs utilisés) et la priorité dynamique pour chaque jeu. Le Gardien en fond (s'il est activé) détectera le lancement du jeu, lui attribuera la priorité Haute et appliquera le masque d'affinité choisi, tout en désactivant temporairement le stationnement des cœurs (Core Parking).",
                Location = new Point(18, 58), Size = new Size(644, 46), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(intro);

            _listGames = new ListBox
            {
                Location = new Point(18, 110), Size = new Size(300, 268),
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9.5f), IntegralHeight = false
            };
            _listGames.SelectedIndexChanged += OnGameSelected;
            Controls.Add(_listGames);

            var pnlSettings = new Panel { Location = new Point(330, 110), Size = new Size(330, 268), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
            
            pnlSettings.Controls.Add(new Label { Text = "Mode d'affinité CPU :", Location = new Point(10, 10), AutoSize = true, Font = new Font("Segoe UI Semibold", 9f) });
            _cboMode = new ComboBox { Location = new Point(10, 30), Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
            _cboMode.Items.AddRange(new object[] { "Désactivé (Par défaut Windows)", "Automatique (Évite Core 0 / E-Cores)", "Manuel (Choix personnalisé)" });
            _cboMode.SelectedIndex = 0;
            _cboMode.SelectedIndexChanged += OnModeChanged;
            pnlSettings.Controls.Add(_cboMode);

            pnlSettings.Controls.Add(new Label { Text = "Cœurs sélectionnés (Manuel) :", Location = new Point(10, 65), AutoSize = true });
            _listCores = new CheckedListBox { Location = new Point(10, 85), Size = new Size(300, 170), CheckOnClick = true, IntegralHeight = false };
            for (int i = 0; i < _cpuCores; i++) _listCores.Items.Add("CPU " + i);
            pnlSettings.Controls.Add(_listCores);
            
            Controls.Add(pnlSettings);

            _btnAdd = MakeBtn("＋  Ajouter un jeu…", 18, 388, 150, 30, false);
            _btnAdd.Click += OnAddManual;
            Controls.Add(_btnAdd);

            _btnApply = MakeBtn("SAUVEGARDER", 430, 414, 150, 36, true);
            _btnApply.Click += OnApply;
            _btnClose = MakeBtn("Fermer", 588, 414, 74, 36, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnApply); Controls.Add(_btnClose);
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
            _btnApply.Enabled = !busy; _listGames.Enabled = !busy; _cboMode.Enabled = !busy; _listCores.Enabled = !busy;
        }

        private void Scan()
        {
            SetBusy(true);
            Task.Run(() =>
            {
                _games = Catalog();
                var known = GameScan.Known();
                GameScan.Detect(known);
                var detectedNames = new HashSet<string>(known.Where(g => g.Detected).Select(g => g.Name), StringComparer.OrdinalIgnoreCase);

                foreach (Game g in _games) g.Detected = detectedNames.Contains(g.Name);
                foreach (Game g in ManualGames()) { g.Detected = true; _games.Add(g); }
                try { BeginInvoke((Action)Populate); } catch { }
            });
        }

        private void Populate()
        {
            _listGames.Items.Clear();
            foreach (Game g in _games)
            {
                string status = _affinityDict.ContainsKey(g.Name) ? " [" + _affinityDict[g.Name].Item1 + "]" : "";
                _listGames.Items.Add((g.Detected ? "" : "     ") + g.Name + status);
            }
            if (_listGames.Items.Count > 0) _listGames.SelectedIndex = 0;
            SetBusy(false);
        }

        private void OnGameSelected(object sender, EventArgs e)
        {
            int idx = _listGames.SelectedIndex;
            if (idx < 0 || idx >= _games.Count) return;
            // Sauvegarder la configuration de l'ancien jeu si modifié ? Pour simplifier, on applique au changement ou au bouton Apply global.
            // On va le faire en mémoire.
            if (_selectedGame != null) SaveCurrentToMemory();

            _selectedGame = _games[idx];
            string mode = "Disabled";
            long mask = 0;
            if (_affinityDict.ContainsKey(_selectedGame.Name))
            {
                mode = _affinityDict[_selectedGame.Name].Item1;
                mask = _affinityDict[_selectedGame.Name].Item2;
            }

            if (mode == "Auto") _cboMode.SelectedIndex = 1;
            else if (mode == "Manual") _cboMode.SelectedIndex = 2;
            else _cboMode.SelectedIndex = 0;

            for (int i = 0; i < _cpuCores; i++)
            {
                _listCores.SetItemChecked(i, (mask & (1L << i)) != 0);
            }
            OnModeChanged(null, null);
        }

        private void SaveCurrentToMemory()
        {
            if (_selectedGame == null) return;
            string mode = "Disabled";
            if (_cboMode.SelectedIndex == 1) mode = "Auto";
            else if (_cboMode.SelectedIndex == 2) mode = "Manual";

            if (mode == "Disabled")
            {
                _affinityDict.Remove(_selectedGame.Name);
            }
            else
            {
                long mask = 0;
                if (mode == "Manual")
                {
                    for (int i = 0; i < _cpuCores; i++)
                        if (_listCores.GetItemChecked(i)) mask |= (1L << i);
                }
                else
                {
                    // Auto mask : All cores except Core 0
                    mask = ((1L << _cpuCores) - 1) & ~1L;
                }
                _affinityDict[_selectedGame.Name] = Tuple.Create(mode, mask);
            }
            // Update l'affichage de la liste
            int idx = _games.IndexOf(_selectedGame);
            if (idx >= 0)
            {
                string status = _affinityDict.ContainsKey(_selectedGame.Name) ? " [" + _affinityDict[_selectedGame.Name].Item1 + "]" : "";
                _listGames.Items[idx] = (_selectedGame.Detected ? "" : "     ") + _selectedGame.Name + status;
            }
        }

        private void OnModeChanged(object sender, EventArgs e)
        {
            _listCores.Enabled = (_cboMode.SelectedIndex == 2);
        }

        private void OnApply(object sender, EventArgs e)
        {
            SaveCurrentToMemory();
            SetBusy(true);
            Task.Run(() =>
            {
                Sys.SaveGameAffinity(_affinityDict);
                // IFEO cleanup (remove legacy Priority setting)
                foreach (Game g in _games)
                {
                    foreach (string exe in g.Exes)
                    {
                        try { Sys.DelMachine(GameScan.IfeoKey + "\\" + exe + "\\PerfOptions", "CpuPriorityClass"); } catch { }
                    }
                }
                if (_log != null) _log("Règles d'affinité sauvegardées.", 1);
                try { BeginInvoke((Action)(() => { SetBusy(false); Close(); })); } catch { }
            });
        }

        private void OnAddManual(object sender, EventArgs e)
        {
            string exe;
            using (var dlg = new OpenFileDialog { Filter = "Exécutable du jeu (*.exe)|*.exe", Title = "Sélectionne l'exécutable du jeu" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                exe = System.IO.Path.GetFileName(dlg.FileName);
            }
            string name = Prompt("Nom du jeu à afficher :", System.IO.Path.GetFileNameWithoutExtension(exe));
            if (string.IsNullOrWhiteSpace(name)) return;
            try { System.IO.File.AppendAllText(ManualPath, name.Replace("|", " ") + "|" + exe.Replace("|", " ") + Environment.NewLine); } catch { }
            if (_log != null) _log("Jeu ajouté : " + name.Trim() + " (" + exe + ").", 0);
            Scan();
        }

        private string Prompt(string label, string def)
        {
            using (var f = new Form
            {
                Text = "DesTinGOOD — Ajouter un jeu", ClientSize = new Size(380, 132), FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = new Font("Segoe UI", 9f)
            })
            {
                var l = new Label { Text = label, Location = new Point(16, 16), AutoSize = true };
                var tb = new TextBox { Text = def, Location = new Point(16, 42), Width = 348 };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(200, 86), Width = 76, FlatStyle = FlatStyle.Flat };
                var ca = new Button { Text = "Annuler", DialogResult = DialogResult.Cancel, Location = new Point(286, 86), Width = 78, FlatStyle = FlatStyle.Flat };
                f.Controls.Add(l); f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(ca);
                f.AcceptButton = ok; f.CancelButton = ca;
                try { Theme.Apply(f); } catch { }
                return f.ShowDialog(this) == DialogResult.OK ? tb.Text : null;
            }
        }
    }
}
