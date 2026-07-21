using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Profils de priorité par jeu : donne à chaque jeu (individuellement) la priorité
    /// processeur « Haute » via le mécanisme officiel Windows (Image File Execution Options —
    /// aucune injection, compatible anticheat). Quand le CPU sature, le jeu coché passe devant
    /// les tâches de fond. Entièrement réversible, par jeu.
    /// </summary>
    internal class GameProfileForm : Form
    {
        private readonly Action<string, int> _log;
        private CheckedListBox _list;
        private Label _summary;
        private Button _btnApply, _btnAllDetected, _btnNone, _btnClose;
        private List<Game> _games = new List<Game>();

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);

        private class Game
        {
            public string Name; public string[] Exes; public bool Detected; public bool HighPriority;
            public Game(string name, params string[] exes) { Name = name; Exes = exes; }
        }

        // Jeux compétitifs courants + leurs exécutables (priorité CPU pertinente sur ces titres).
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

        public GameProfileForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Scan();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "DesTinGOOD — Priorité par jeu";
            ClientSize = new Size(620, 480);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Priorité CPU par jeu — booste ton jeu principal",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var intro = new Label
            {
                Text = "Coche les jeux à lancer en priorité processeur « Haute » (mécanisme officiel Windows, aucune "
                     + "injection — compatible anticheat). Utile surtout si ton PC est limité par le CPU. "
                     + "Les jeux détectés sur ce PC sont marqués . Réversible : décoche et applique.",
                Location = new Point(18, 58), Size = new Size(584, 46), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(intro);

            _list = new CheckedListBox
            {
                Location = new Point(18, 110), Size = new Size(584, 268), CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9.5f), IntegralHeight = false
            };
            Controls.Add(_list);

            _summary = new Label
            {
                Location = new Point(18, 384), Size = new Size(584, 22),
                Font = new Font("Segoe UI Semibold", 9.5f), ForeColor = Color.FromArgb(60, 64, 72)
            };
            Controls.Add(_summary);

            _btnAllDetected = MakeBtn("Cocher les jeux détectés", 18, 414, 200, 36, false);
            _btnAllDetected.Click += (s, e) => { for (int i = 0; i < _games.Count; i++) if (_games[i].Detected) _list.SetItemChecked(i, true); };
            _btnNone = MakeBtn("Tout décocher", 228, 414, 130, 36, false);
            _btnNone.Click += (s, e) => { for (int i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, false); };
            _btnApply = MakeBtn("APPLIQUER", 368, 414, 150, 36, true);
            _btnApply.Click += OnApply;
            _btnClose = MakeBtn("Fermer", 528, 414, 74, 36, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnAllDetected); Controls.Add(_btnNone); Controls.Add(_btnApply); Controls.Add(_btnClose);
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
            _btnApply.Enabled = !busy; _btnAllDetected.Enabled = !busy; _btnNone.Enabled = !busy; _list.Enabled = !busy;
        }

        private static bool ExeHasHighPriority(string exe)
        {
            return Sys.IntEquals(Sys.GetMachine(GameScan.IfeoKey + "\\" + exe + "\\PerfOptions", "CpuPriorityClass"), 3);
        }

        private void Scan()
        {
            SetBusy(true);
            _summary.Text = "Analyse...";
            Task.Run(() =>
            {
                _games = Catalog();
                // Détection installée (par nom, via GameScan).
                var known = GameScan.Known();
                GameScan.Detect(known);
                var detectedNames = new HashSet<string>(
                    known.Where(g => g.Detected).Select(g => g.Name), StringComparer.OrdinalIgnoreCase);

                foreach (Game g in _games)
                {
                    g.Detected = detectedNames.Contains(g.Name);
                    g.HighPriority = g.Exes.All(ExeHasHighPriority);
                }
                try { BeginInvoke((Action)Populate); } catch { }
            });
        }

        private void Populate()
        {
            _list.Items.Clear();
            int on = 0, detected = 0;
            foreach (Game g in _games)
            {
                if (g.HighPriority) on++;
                if (g.Detected) detected++;
                _list.Items.Add((g.Detected ? "" : "     ") + g.Name
                    + (g.HighPriority ? "   — priorité HAUTE active" : ""), g.HighPriority);
            }
            _summary.Text = on + " jeu(x) en priorité haute · " + detected + " détecté(s) sur ce PC.";
            SetBusy(false);
        }

        private void OnApply(object sender, EventArgs e)
        {
            SetBusy(true);
            var wanted = new bool[_games.Count];
            for (int i = 0; i < _games.Count; i++) wanted[i] = _list.GetItemChecked(i);

            Task.Run(() =>
            {
                int set = 0, cleared = 0;
                for (int i = 0; i < _games.Count; i++)
                {
                    Game g = _games[i];
                    bool want = wanted[i];
                    if (want == g.HighPriority) continue;   // pas de changement
                    foreach (string exe in g.Exes)
                    {
                        string key = GameScan.IfeoKey + "\\" + exe + "\\PerfOptions";
                        try
                        {
                            if (want) Sys.SetMachine(key, "CpuPriorityClass", 3, RegistryValueKind.DWord);
                            else Sys.DelMachine(key, "CpuPriorityClass");
                        }
                        catch (Exception ex) { if (_log != null) _log(g.Name + " (" + exe + ") : " + ex.Message, 2); }
                    }
                    if (want) { set++; if (_log != null) _log(g.Name + " → priorité CPU Haute.", 1); }
                    else { cleared++; if (_log != null) _log(g.Name + " → priorité CPU normale (rétablie).", 0); }
                }
                if (_log != null) _log("Priorité par jeu : " + set + " activée(s), " + cleared + " rétablie(s).", 1);
                try { BeginInvoke((Action)Scan); } catch { }
            });
        }
    }
}
