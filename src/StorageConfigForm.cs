using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// ⚙ Réglages du centre de stockage. C'est ici que l'utilisateur décide de TOUT :
    /// quels modules sont analysés, jusqu'où va « TOUT LIBÉRER », ce qu'on ne doit plus jamais
    /// lui proposer, et à partir de quel taux de remplissage ONYX se manifeste tout seul.
    ///
    /// Le curseur de sûreté s'arrête volontairement au niveau 1 : le niveau 2 (données
    /// personnelles) n'est pas atteignable par un lot, quel que soit le réglage.
    /// </summary>
    internal class StorageConfigForm : Form
    {
        private readonly StorageSettings _st;
        private CheckedListBox _modules;
        private RadioButton _rSafe, _rDeep;
        private NumericUpDown _alert, _minFile;
        private CheckBox _hebdo;
        private ListBox _ignored, _exclus;
        private Label _explain;
        private List<Storage.Module> _defs;

        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private static readonly Color Ink = Color.FromArgb(40, 44, 52);
        private static readonly Color Sub = Color.FromArgb(96, 100, 108);

        public StorageConfigForm(StorageSettings st)
        {
            _st = st ?? StorageSettings.Load();
            Build();
            Theme.Apply(this);
            LoadValues();
        }

        private void Build()
        {
            Text = "ONYX — Réglages du stockage";
            ClientSize = new Size(720, 772);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  ⚙ Stockage — c'est toi qui décides de ce qui est superflu",
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f),
                TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            int y = 66;
            Controls.Add(Title("Modules analysés", 16, y)); y += 24;
            Controls.Add(Hint("Décoche ce que tu ne veux pas voir analysé : le module disparaît de la page et n'est plus mesuré (l'analyse va plus vite).", 16, y)); y += 34;

            _modules = new CheckedListBox
            {
                Location = new Point(16, y),
                Size = new Size(688, 148),
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                IntegralHeight = false
            };
            Controls.Add(_modules);
            y += 162;

            Controls.Add(Title("Jusqu'où va « TOUT LIBÉRER »", 16, y)); y += 26;

            _rSafe = Radio("Superflu uniquement — aucune perte possible (recommandé)", 20, y);
            _rSafe.CheckedChanged += (s, e) => UpdateExplain();
            Controls.Add(_rSafe); y += 24;

            _rDeep = Radio("Superflu + « à vérifier » — inclut historique, corbeille et réglages Windows", 20, y);
            _rDeep.CheckedChanged += (s, e) => UpdateExplain();
            Controls.Add(_rDeep); y += 26;

            _explain = Hint("", 40, y);
            _explain.Size = new Size(660, 34);
            Controls.Add(_explain); y += 40;

            Controls.Add(Title("Alerte automatique", 16, y)); y += 26;
            Controls.Add(new Label
            {
                Text = "Prévenir quand il reste moins de",
                Location = new Point(20, y + 3),
                Size = new Size(190, 20),
                ForeColor = Ink
            });
            _alert = new NumericUpDown
            {
                Location = new Point(214, y),
                Size = new Size(64, 24),
                Minimum = 0, Maximum = 50,
                BackColor = Color.White
            };
            Controls.Add(_alert);
            Controls.Add(new Label
            {
                Text = "% d'espace libre sur le disque système  (0 = jamais)",
                Location = new Point(284, y + 3),
                Size = new Size(410, 20),
                ForeColor = Sub
            });
            y += 30;
            _hebdo = new CheckBox
            {
                Text = "Me prévenir chaque semaine du poids mort (journaux, restes de Steam) — analyse seule, rien n'est supprimé",
                Location = new Point(20, y), Size = new Size(684, 22), ForeColor = Ink
            };
            Controls.Add(_hebdo);
            y += 32;

            Controls.Add(Title("Gros fichiers (vidéos, archives, autres)", 16, y)); y += 26;
            Controls.Add(new Label
            {
                Text = "Considérer comme « gros » un fichier d'au moins",
                Location = new Point(20, y + 3),
                Size = new Size(272, 20),
                ForeColor = Ink
            });
            _minFile = new NumericUpDown
            {
                Location = new Point(296, y),
                Size = new Size(72, 24),
                Minimum = 50, Maximum = 4096, Increment = 50,
                BackColor = Color.White
            };
            Controls.Add(_minFile);
            Controls.Add(new Label
            {
                Text = "Mo  (plus c'est bas, plus le balayage remonte de fichiers)",
                Location = new Point(374, y + 3),
                Size = new Size(330, 20),
                ForeColor = Sub
            });
            y += 34;

            // Deux listes côte à côte : ce que tu as écarté d'une analyse, et les dossiers exclus à la main.
            Label tIgn = Title("Ne plus me proposer", 16, y); tIgn.Size = new Size(336, 20); Controls.Add(tIgn);
            Label tExc = Title("Dossiers exclus du nettoyage", 368, y); tExc.Size = new Size(336, 20); Controls.Add(tExc);
            y += 24;
            _ignored = new ListBox
            {
                Location = new Point(16, y),
                Size = new Size(336, 76),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White, HorizontalScrollbar = true
            };
            _ignored.AccessibleName = "Éléments à ne plus proposer";
            Controls.Add(_ignored);
            _exclus = new ListBox
            {
                Location = new Point(368, y),
                Size = new Size(336, 76),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White, HorizontalScrollbar = true
            };
            _exclus.AccessibleName = "Dossiers exclus du nettoyage";
            Controls.Add(_exclus);
            y += 82;

            Button bDel = MakeBtn("Retirer", 16, y, 110, 30, false);
            bDel.Click += OnRemoveIgnored;
            Controls.Add(bDel);
            Button bClear = MakeBtn("Tout retirer", 134, y, 120, 30, false);
            bClear.Click += (s, e) => { _st.ClearIgnored(); RefreshIgnored(); };
            Controls.Add(bClear);
            Button bAdd = MakeBtn("Ajouter un dossier…", 368, y, 170, 30, false);
            bAdd.Click += OnAddExclu;
            Controls.Add(bAdd);
            Button bRem = MakeBtn("Retirer", 546, y, 110, 30, false);
            bRem.Click += OnRemoveExclu;
            Controls.Add(bRem);
            y += 34;
            _exclusNote = Hint("Rien de ce qu'un dossier exclu contient n'est proposé, et un cache qui en contient un n'est plus vidé.", 368, y);
            _exclusNote.Size = new Size(336, 32);
            Controls.Add(_exclusNote);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.FromArgb(245, 246, 248) };
            Button ok = MakeBtn("Enregistrer", 496, 10, 120, 36, true);
            ok.Click += OnSave;
            Button cancel = MakeBtn("Annuler", 624, 10, 80, 36, false);
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            bottom.Controls.Add(ok); bottom.Controls.Add(cancel);
            Controls.Add(bottom);

            AcceptButton = ok; CancelButton = cancel;
        }

        private static Label Title(string t, int x, int y)
        {
            return new Label
            {
                Text = t, Location = new Point(x, y), Size = new Size(688, 20),
                Font = new Font("Segoe UI Semibold", 10f), ForeColor = Ink
            };
        }

        private static Label Hint(string t, int x, int y)
        {
            return new Label
            {
                Text = t, Location = new Point(x, y), Size = new Size(688, 30),
                Font = new Font("Segoe UI", 8.5f), ForeColor = Sub
            };
        }

        private static RadioButton Radio(string t, int x, int y)
        {
            return new RadioButton
            {
                Text = t, Location = new Point(x, y), Size = new Size(680, 22), ForeColor = Ink
            };
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Ink,
                Font = primary ? new Font("Segoe UI Semibold", 9f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void LoadValues()
        {
            _defs = Storage.Defs();
            _modules.Items.Clear();
            foreach (Storage.Module m in _defs)
            {
                string label = m.Glyph + "  " + m.Name + "   —   " + Storage.SafetyLabel(m.Safety)
                             + (m.Browse ? "  ·  présentation seule" : "");
                _modules.Items.Add(label, _st.IsEnabled(m.Id));
            }

            if (_st.MaxSafety >= Storage.SafeVerifier) _rDeep.Checked = true; else _rSafe.Checked = true;
            _alert.Value = Math.Max(_alert.Minimum, Math.Min(_alert.Maximum, _st.AlertPct));
            _minFile.Value = Math.Max(_minFile.Minimum, Math.Min(_minFile.Maximum, _st.MinFileMB));
            _hebdo.Checked = RappelNettoyage.Charge().Active;
            RefreshIgnored();
            RefreshExclus();
            UpdateExplain();
        }

        private Label _exclusNote;
        private const string NoteExclus = "Rien de ce qu'un dossier exclu contient n'est proposé, et un cache qui en contient un n'est plus vidé.";

        private void RefreshExclus()
        {
            _exclus.Items.Clear();
            List<string> list = _st.Exclus;
            list.Sort(StringComparer.CurrentCultureIgnoreCase);
            foreach (string s in list) _exclus.Items.Add(s);
            if (_exclus.Items.Count == 0) _exclus.Items.Add("(aucun dossier exclu)");
        }

        private void OnAddExclu(object sender, EventArgs e)
        {
            using (var d = new FolderBrowserDialog { Description = "Dossier à ne jamais nettoyer ni proposer", UseDescriptionForTitle = true, ShowNewFolderButton = false })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string refus = _st.Exclut(d.SelectedPath);
                _exclusNote.Text = refus ?? NoteExclus;
                _exclusNote.ForeColor = refus != null ? Color.FromArgb(190, 60, 50) : Sub;
                RefreshExclus();
            }
        }

        private void OnRemoveExclu(object sender, EventArgs e)
        {
            if (_exclus.SelectedItem == null) return;
            string s = Convert.ToString(_exclus.SelectedItem);
            if (s.StartsWith("(")) return;
            _st.RetireExclu(s);
            _exclusNote.Text = NoteExclus; _exclusNote.ForeColor = Sub;
            RefreshExclus();
        }

        private void UpdateExplain()
        {
            _explain.Text = _rDeep.Checked
                ? "Le bouton videra aussi la corbeille, l'historique de l'Explorateur et les caches de shaders (saccades le temps de les recompiler), et pourra couper la veille prolongée. Toujours annoncé avant, jamais tes fichiers personnels."
                : "Le bouton ne touchera que ce qui se régénère seul : temporaires et caches (pas les shaders, qui feraient saccader les jeux). Les modules « à vérifier » restent affichés, simplement décochés au départ.";
        }

        private void RefreshIgnored()
        {
            _ignored.Items.Clear();
            List<string> list = _st.IgnoredList;
            list.Sort(StringComparer.CurrentCultureIgnoreCase);
            foreach (string s in list) _ignored.Items.Add(s);
            if (_ignored.Items.Count == 0) _ignored.Items.Add("(rien d'ignoré pour l'instant)");
        }

        private void OnRemoveIgnored(object sender, EventArgs e)
        {
            if (_ignored.SelectedItem == null) return;
            string s = Convert.ToString(_ignored.SelectedItem);
            if (s.StartsWith("(")) return;
            _st.Unignore(s);
            RefreshIgnored();
        }

        private void OnSave(object sender, EventArgs e)
        {
            for (int i = 0; i < _defs.Count && i < _modules.Items.Count; i++)
                _st.SetEnabled(_defs[i].Id, _modules.GetItemChecked(i));

            _st.MaxSafety = _rDeep.Checked ? Storage.SafeVerifier : Storage.SafeSuperflu;
            _st.AlertPct = (int)_alert.Value;
            _st.MinFileMB = (int)_minFile.Value;
            _st.Save();
            RappelNettoyage.Active(_hebdo.Checked);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
