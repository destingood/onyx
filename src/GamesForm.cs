using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Mes jeux : le boost PAR JEU façon « FPS doctor » (leur écran principal).
    //  Chaque jeu (détecté ou ajouté à la main) a un niveau :
    //    Aucun   — rien n'est appliqué
    //    Léger   — priorité CPU élevée pour les exécutables du jeu (IFEO officiel)
    //    Complet — léger + exclusion antivirus du dossier du jeu (API Defender)
    //  Tout est réversible (« Aucun » retire tout), favoris ♥ en tête de liste,
    //  « Tout réinitialiser » = reset_games_opti. Persisté dans bt-games.txt.
    //  Pas de jaquettes distantes : tuile à initiale néon (aucun actif tiers).
    // ----------------------------------------------------------------------
    internal class GamesForm : Form
    {
        private class GameEntry
        {
            public string Name;
            public string InstallPath;    // dossier (peut être null si non résolu)
            public bool Manual;
            public int Level;             // 0 aucun · 1 léger · 2 complet
            public bool Fav;
            public string[] Exes = new string[0];

            public GroupBox Card;
            public Label State;
            public Button B0, B1, B2, BFav, BDel;
        }

        // Exécutables DISTINCTIFS par jeu connu (mêmes noms que la détection en jeu).
        // Minecraft : volontairement vide — javaw toucherait toutes les applis Java.
        private static readonly Dictionary<string, string[]> KnownExes =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Counter-Strike 2",          new[] { "cs2.exe" } },
            { "VALORANT",                  new[] { "VALORANT-Win64-Shipping.exe" } },
            { "Fortnite",                  new[] { "FortniteClient-Win64-Shipping.exe" } },
            { "Overwatch 2",               new[] { "Overwatch.exe" } },
            { "Apex Legends",              new[] { "r5apex.exe", "r5apex_dx12.exe" } },
            { "Call of Duty (MW / Warzone)", new[] { "cod.exe", "cod22-cod.exe", "cod23-cod.exe", "cod24-cod.exe" } },
            { "League of Legends",         new[] { "League of Legends.exe" } },
            { "Rocket League",             new[] { "RocketLeague.exe" } },
            { "Rainbow Six Siege",         new[] { "RainbowSix.exe", "RainbowSix_Vulkan.exe" } },
            { "Minecraft",                 new string[0] },
        };

        private readonly Action<string, int> _log;
        private readonly List<GameEntry> _games = new List<GameEntry>();
        private Panel _panel;
        private Label _status;
        private Button _btnAdd, _btnReset, _btnRescan;
        private bool _busy;

        public GamesForm(Action<string, int> log)
        {
            _log = log;
            BuildUi();
            Theme.Apply(this);
            Shown += (s, e) => ReloadGames();   // détection disque/registre en fond
        }

        private void BuildUi()
        {
            Text = "Fluide — Mes jeux (boost par jeu)";
            ClientSize = new Size(760, 620);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            var intro = new Label();
            intro.SetBounds(16, 10, 728, 34);
            intro.Text = "Un niveau de boost PAR JEU, 100 % réversible : Léger = priorité CPU élevée pour le jeu ; "
                       + "Complet = priorité + exclusion antivirus de son dossier. ♥ épingle tes jeux en tête.";
            intro.ForeColor = Theme.InkDimColor;

            _btnAdd = MakeBtn("➕ Ajouter un jeu (dossier)...", 16, 50, 210, 32, true);
            _btnAdd.Click += OnAddManual;
            _btnReset = MakeBtn("↩ Tout réinitialiser", 232, 50, 160, 32, false);
            _btnReset.Click += OnResetAll;
            _btnRescan = MakeBtn("🔄 Re-scan", 398, 50, 108, 32, false);
            _btnRescan.Click += (s, e) => ReloadGames();

            _panel = new Panel();
            _panel.SetBounds(16, 92, 728, 486);
            _panel.AutoScroll = true;
            _panel.BackColor = Color.White;

            _status = new Label();
            _status.SetBounds(16, 584, 728, 28);
            _status.ForeColor = Theme.InkDimColor;
            _status.Text = "Détection des jeux installés...";

            Controls.AddRange(new Control[] { intro, _btnAdd, _btnReset, _btnRescan, _panel, _status });
        }

        // ------------------------------------------------------------------
        //  Chargement : jeux connus détectés + jeux manuels + niveaux persistés
        // ------------------------------------------------------------------
        private void ReloadGames()
        {
            if (_busy) return;
            _busy = true;
            SetBusyUi(true);
            SetStatus("Détection des jeux installés...", 0);
            Task.Run(() =>
            {
                var list = new List<GameEntry>();
                try
                {
                    List<GameScan.GameInfo> known = GameScan.Known();
                    GameScan.Detect(known);
                    foreach (GameScan.GameInfo g in known)
                    {
                        if (!g.Detected) continue;
                        string[] exes;
                        if (!KnownExes.TryGetValue(g.Name, out exes)) exes = new string[0];
                        list.Add(new GameEntry { Name = g.Name, InstallPath = g.InstallPath, Exes = exes });
                    }
                }
                catch { }

                // Fusion avec l'état persisté (niveaux, favoris, jeux manuels).
                foreach (string[] rec in LoadStore())
                {
                    try
                    {
                        if (rec[0] == "k" && rec.Length >= 4)
                        {
                            GameEntry e = list.FirstOrDefault(x => !x.Manual && x.Name.Equals(rec[1], StringComparison.OrdinalIgnoreCase));
                            if (e != null) { e.Level = ParseLevel(rec[2]); e.Fav = rec[3] == "1"; }
                        }
                        else if (rec[0] == "m" && rec.Length >= 5)
                        {
                            var e = new GameEntry
                            {
                                Name = rec[1], InstallPath = rec[2], Manual = true,
                                Level = ParseLevel(rec[3]), Fav = rec[4] == "1",
                                Exes = ScanExes(rec[2])
                            };
                            list.Add(e);
                        }
                    }
                    catch { }
                }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _games.Clear();
                        _games.AddRange(list);
                        RebuildCards();
                        int boosted = _games.Count(g => g.Level > 0);
                        SetStatus(_games.Count + " jeu(x) — " + boosted + " boosté(s). Léger = priorité CPU · Complet = + exclusion antivirus.", 0);
                        _busy = false;
                        SetBusyUi(false);
                    }));
                }
                catch { _busy = false; }
            });
        }

        private static int ParseLevel(string s)
        {
            int v; return int.TryParse(s, out v) ? Math.Max(0, Math.Min(2, v)) : 0;
        }

        /// <summary>Exécutables plausibles d'un jeu manuel (2 niveaux, sans installeurs/anticheat).</summary>
        private static string[] ScanExes(string dir)
        {
            var found = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return found.ToArray();
                var bad = new[] { "unins", "setup", "install", "crash", "report", "redist", "vcredist",
                                  "dxsetup", "dotnet", "easyanticheat", "battleye", "eac_" };
                foreach (string f in Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                         .Concat(Directory.EnumerateDirectories(dir)
                             .SelectMany(d => { try { return Directory.EnumerateFiles(d, "*.exe", SearchOption.TopDirectoryOnly); }
                                                catch { return Enumerable.Empty<string>(); } })))
                {
                    string name = Path.GetFileName(f);
                    string low = name.ToLowerInvariant();
                    bool skip = false;
                    foreach (string b in bad) if (low.Contains(b)) { skip = true; break; }
                    if (!skip && !found.Contains(name, StringComparer.OrdinalIgnoreCase)) found.Add(name);
                    if (found.Count >= 12) break;
                }
            }
            catch { }
            return found.ToArray();
        }

        // ------------------------------------------------------------------
        //  Cartes (tri : favoris puis nom)
        // ------------------------------------------------------------------
        private void RebuildCards()
        {
            _panel.SuspendLayout();
            _panel.Controls.Clear();
            int y = 6;
            foreach (GameEntry g in _games.OrderByDescending(x => x.Fav).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                BuildCard(g, y);
                _panel.Controls.Add(g.Card);
                y += 72;
            }
            _panel.ResumeLayout();
            Theme.Apply(_panel);   // cartes arrondies + couleurs sur les nouveaux contrôles
        }

        private void BuildCard(GameEntry g, int y)
        {
            var card = new GroupBox();
            card.Text = "";
            card.SetBounds(6, y, 688, 64);

            // Tuile à initiale néon (aucune jaquette distante : zéro actif tiers).
            var tile = new Panel();
            tile.SetBounds(14, 14, 38, 38);
            string initial = string.IsNullOrEmpty(g.Name) ? "?" : g.Name.Substring(0, 1).ToUpperInvariant();
            tile.Paint += (s, e) =>
            {
                Graphics gr = e.Graphics;
                gr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var r = new RectangleF(0.5f, 0.5f, tile.Width - 1f, tile.Height - 1f);
                using (var p = Theme.RoundPath(r, 8f))
                {
                    using (var br = new SolidBrush(Theme.Dark ? Color.FromArgb(33, 33, 33) : Color.FromArgb(235, 238, 242)))
                        gr.FillPath(br, p);
                    using (var pen = new Pen(Theme.LineColor)) gr.DrawPath(pen, p);
                }
                TextRenderer.DrawText(gr, initial, new Font("Segoe UI Semibold", 15f), tile.ClientRectangle,
                    Theme.OkColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };

            var name = new Label();
            name.SetBounds(62, 12, 300, 20);
            name.Font = new Font("Segoe UI Semibold", 9.5f);
            name.Text = g.Name + (g.Manual ? "  (ajouté)" : "");

            g.State = new Label();
            g.State.SetBounds(62, 33, 320, 18);
            g.State.Font = new Font("Segoe UI", 8f);
            RefreshStateLabel(g);

            g.B0 = MakeBtn("Aucun", 388, 17, 66, 30, false);
            g.B1 = MakeBtn("Léger", 458, 17, 66, 30, false);
            g.B2 = MakeBtn("Complet", 528, 17, 74, 30, false);
            g.B0.Click += (s, e) => ApplyLevel(g, 0);
            g.B1.Click += (s, e) => ApplyLevel(g, 1);
            g.B2.Click += (s, e) => ApplyLevel(g, 2);

            g.BFav = MakeBtn(g.Fav ? "♥" : "♡", 608, 17, 32, 30, false);
            g.BFav.Click += (s, e) => { g.Fav = !g.Fav; SaveStore(); RebuildCards(); };

            g.BDel = MakeBtn("✕", 646, 17, 30, 30, false);
            g.BDel.Visible = g.Manual;
            g.BDel.Click += (s, e) => OnDeleteManual(g);

            HighlightLevel(g);
            card.Controls.AddRange(new Control[] { tile, name, g.State, g.B0, g.B1, g.B2, g.BFav, g.BDel });
            g.Card = card;
        }

        private void RefreshStateLabel(GameEntry g)
        {
            string txt = g.Level == 2 ? "Boost COMPLET : priorité CPU élevée + exclusion antivirus"
                       : g.Level == 1 ? "Boost léger : priorité CPU élevée"
                       : "Aucun boost appliqué";
            if (g.Level >= 1 && g.Exes.Length == 0)
                txt += " (priorité : aucun exécutable ciblable)";
            g.State.Text = txt;
            g.State.ForeColor = g.Level > 0 ? Theme.OkColor : Theme.InkDimColor;
        }

        /// <summary>Le bouton du niveau actif porte l'accent (néon en sombre, texte noir).</summary>
        private void HighlightLevel(GameEntry g)
        {
            Button[] all = { g.B0, g.B1, g.B2 };
            for (int i = 0; i < all.Length; i++)
            {
                bool on = g.Level == i;
                all[i].BackColor = on ? Theme.AccentColor
                                      : (Theme.Dark ? Color.FromArgb(33, 33, 33) : Color.White);
                all[i].ForeColor = on ? (Theme.Dark ? Color.Black : Color.White) : Theme.InkColor;
                all[i].FlatAppearance.BorderSize = on ? 0 : 1;
            }
        }

        // ------------------------------------------------------------------
        //  Application d'un niveau (opti_game / unopti_game)
        // ------------------------------------------------------------------
        private void ApplyLevel(GameEntry g, int level)
        {
            if (_busy || g.Level == level) return;

            // Sécurité : « Complet » retire le dossier du jeu de l'analyse Defender. Au TOUT
            // PREMIER usage, on prévient explicitement (opt-in éclairé) ; ensuite l'utilisateur
            // a été informé une fois pour toutes. Annuler garde le niveau actuel.
            if (level == 2 && !string.IsNullOrEmpty(g.InstallPath) && !DefenderWarningAccepted())
            {
                DialogResult r = MessageBox.Show(this,
                    "Le boost « Complet » retire le dossier de ce jeu de l'analyse de Windows Defender "
                    + "(moins de saccades, mais l'antivirus n'y regarde plus).\r\n\r\n"
                    + "À ne faire QUE pour des jeux d'origine sûre. JAMAIS pour un jeu piraté ou "
                    + "téléchargé d'une source douteuse : ce dossier ne serait plus protégé.\r\n\r\n"
                    + "Continuer ?",
                    "Exclusion antivirus — à confirmer", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (r != DialogResult.OK) return;   // annulé : on ne touche à rien
                MarkDefenderWarningAccepted();
            }

            _busy = true;
            SetBusyUi(true);
            SetStatus((level == 0 ? "Retrait du boost : " : "Boost " + (level == 1 ? "léger" : "complet") + " : ") + g.Name + "...", 0);
            int old = g.Level;
            Task.Run(() =>
            {
                bool ok = true;
                try
                {
                    // Priorité CPU (IFEO officiel, comme « Priorité CPU par jeu »).
                    foreach (string exe in g.Exes)
                    {
                        string key = GameScan.IfeoKey + "\\" + exe + "\\PerfOptions";
                        if (level >= 1) Sys.SetMachine(key, "CpuPriorityClass", 3, RegistryValueKind.DWord);
                        else Sys.DelMachine(key, "CpuPriorityClass");
                    }
                    // Exclusion antivirus du dossier (complet uniquement).
                    if (!string.IsNullOrEmpty(g.InstallPath))
                    {
                        if (level == 2) Sys.DefenderAddExclusion(g.InstallPath, RelayLog);
                        else if (old == 2) Sys.DefenderRemoveExclusion(g.InstallPath, RelayLog);
                    }
                }
                catch (Exception ex) { ok = false; RelayLog("Boost " + g.Name + " : " + ex.Message, 3); }

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (ok)
                        {
                            g.Level = level;
                            SaveStore();
                            RelayLog(g.Name + " → " + (level == 0 ? "boost retiré" : level == 1 ? "boost léger" : "boost complet")
                                + " (" + g.Exes.Length + " exécutable(s) ciblé(s)).", 1);
                        }
                        RefreshStateLabel(g);
                        HighlightLevel(g);
                        int boosted = _games.Count(x => x.Level > 0);
                        SetStatus(ok ? "Fait. " + boosted + " jeu(x) boosté(s)." : "Échec — vois le journal principal.", ok ? 1 : 2);
                        _busy = false;
                        SetBusyUi(false);
                    }));
                }
                catch { _busy = false; }
            });
        }

        private void OnResetAll(object sender, EventArgs e)
        {
            if (_busy) return;
            List<GameEntry> boosted = _games.Where(g => g.Level > 0).ToList();
            if (boosted.Count == 0) { SetStatus("Aucun boost à retirer.", 0); return; }
            _busy = true;
            SetBusyUi(true);
            SetStatus("Retrait du boost sur " + boosted.Count + " jeu(x)...", 0);
            Task.Run(() =>
            {
                foreach (GameEntry g in boosted)
                {
                    try
                    {
                        foreach (string exe in g.Exes)
                            Sys.DelMachine(GameScan.IfeoKey + "\\" + exe + "\\PerfOptions", "CpuPriorityClass");
                        if (g.Level == 2 && !string.IsNullOrEmpty(g.InstallPath))
                            Sys.DefenderRemoveExclusion(g.InstallPath, RelayLog);
                        g.Level = 0;
                    }
                    catch (Exception ex) { RelayLog("Réinitialisation " + g.Name + " : " + ex.Message, 2); }
                }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        SaveStore();
                        foreach (GameEntry g in boosted) { RefreshStateLabel(g); HighlightLevel(g); }
                        RelayLog("Boost par jeu réinitialisé sur " + boosted.Count + " jeu(x).", 1);
                        SetStatus("Tous les boosts par jeu sont retirés.", 1);
                        _busy = false;
                        SetBusyUi(false);
                    }));
                }
                catch { _busy = false; }
            });
        }

        // ------------------------------------------------------------------
        //  Jeux manuels (add_manual_game / delete_manual_game)
        // ------------------------------------------------------------------
        private void OnAddManual(object sender, EventArgs e)
        {
            if (_busy) return;
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Choisis le dossier d'installation du jeu";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string path = dlg.SelectedPath;
                if (_games.Any(x => x.Manual && string.Equals(x.InstallPath, path, StringComparison.OrdinalIgnoreCase)))
                {
                    SetStatus("Ce dossier est déjà dans la liste.", 2);
                    return;
                }
                var entry = new GameEntry
                {
                    Name = Path.GetFileName(path.TrimEnd('\\', '/')),
                    InstallPath = path,
                    Manual = true,
                    Exes = ScanExes(path)
                };
                _games.Add(entry);
                SaveStore();
                RebuildCards();
                SetStatus(entry.Name + " ajouté (" + entry.Exes.Length + " exécutable(s) trouvé(s)).", 1);
            }
        }

        private void OnDeleteManual(GameEntry g)
        {
            if (_busy || !g.Manual) return;
            _busy = true;
            SetBusyUi(true);
            SetStatus("Retrait de " + g.Name + "...", 0);
            int old = g.Level;
            Task.Run(() =>
            {
                // Annule d'abord tout boost appliqué, PUIS retire le jeu de la liste.
                try
                {
                    foreach (string exe in g.Exes)
                        Sys.DelMachine(GameScan.IfeoKey + "\\" + exe + "\\PerfOptions", "CpuPriorityClass");
                    if (old == 2 && !string.IsNullOrEmpty(g.InstallPath))
                        Sys.DefenderRemoveExclusion(g.InstallPath, RelayLog);
                }
                catch (Exception ex) { RelayLog("Retrait " + g.Name + " : " + ex.Message, 2); }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _games.Remove(g);
                        SaveStore();
                        _busy = false;
                        SetBusyUi(false);
                        RebuildCards();
                        SetStatus(g.Name + " retiré" + (old > 0 ? " (boost annulé)." : " de la liste."), 0);
                    }));
                }
                catch { _busy = false; }
            });
        }

        // --- Mémoire de l'avertissement « exclusion antivirus » (une fois) -----
        private static string DefenderWarnPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-defender-warned.txt"); }
        }
        private static bool DefenderWarningAccepted() { return File.Exists(DefenderWarnPath); }
        private static void MarkDefenderWarningAccepted()
        {
            try { File.WriteAllText(DefenderWarnPath, "1"); } catch { }
        }

        // ------------------------------------------------------------------
        //  Persistance (bt-games.txt)
        // ------------------------------------------------------------------
        private static string StorePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-games.txt"); }
        }

        private static List<string[]> LoadStore()
        {
            var recs = new List<string[]>();
            try
            {
                if (File.Exists(StorePath))
                    foreach (string line in File.ReadAllLines(StorePath))
                        if (line.Trim().Length > 0) recs.Add(line.Split('|'));
            }
            catch { }
            return recs;
        }

        private void SaveStore()
        {
            try
            {
                var lines = new List<string>();
                foreach (GameEntry g in _games)
                {
                    if (g.Manual)
                        lines.Add("m|" + g.Name + "|" + g.InstallPath + "|" + g.Level + "|" + (g.Fav ? "1" : "0"));
                    else if (g.Level > 0 || g.Fav)
                        lines.Add("k|" + g.Name + "|" + g.Level + "|" + (g.Fav ? "1" : "0"));
                }
                File.WriteAllLines(StorePath, lines);
            }
            catch { }
        }

        // ------------------------------------------------------------------
        //  Plomberie
        // ------------------------------------------------------------------
        private void SetBusyUi(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _panel.Enabled = !busy;
            _btnAdd.Enabled = !busy; _btnReset.Enabled = !busy; _btnRescan.Enabled = !busy;
        }

        private void SetStatus(string text, int level)
        {
            _status.Text = text;
            _status.ForeColor = level == 1 ? Theme.OkColor
                              : level >= 2 ? Color.FromArgb(200, 120, 0)
                              : Theme.InkDimColor;
        }

        private void RelayLog(string msg, int level)
        {
            if (_log == null) return;
            try { _log(msg, level); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;
                SetStatus("Patiente : opération en cours...", 2);
                return;
            }
            base.OnFormClosing(e);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            b.BackColor = primary ? Color.FromArgb(0, 150, 90) : Color.White;
            b.ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52);
            b.UseVisualStyleBackColor = false;
            return b;
        }
    }
}
