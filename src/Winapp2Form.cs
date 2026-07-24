using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Nettoyage avancé par application, basé sur les règles communautaires « winapp2.ini »
    /// (la base que BleachBit utilise) — mais en MODE SÛR : que des fichiers cache, jamais le
    /// registre, uniquement les applications détectées, rien de coché d'office, aperçu avant
    /// suppression. Aucun téléchargement sans que l'utilisateur clique lui-même.
    /// </summary>
    internal class Winapp2Form : Form
    {
        private readonly Action<string, int> _log;
        private CheckedListBox _list;
        private Label _total, _source;
        private Button _btnLoad, _btnDownload, _btnClean, _btnClose;
        private List<Winapp2.Entry> _entries = new List<Winapp2.Entry>();

        private static readonly Color Accent = Theme.AccentColor;

        public Winapp2Form(Action<string, int> log)
        {
            _log = log;
            Build();
            Theme.Apply(this);
            if (Winapp2.HasLocal()) LoadFrom(() => Winapp2.LoadFile(Winapp2.LocalPath), "fichier local");
            else SetSource("Aucune règle chargée. Clique « Télécharger les règles » ou « Charger un fichier… ».");
        }

        private void Build()
        {
            Text = "ONYX — Nettoyage avancé (winapp2.ini)";
            ClientSize = new Size(600, 540);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.FromArgb(245, 246, 248);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(28, 30, 38) };
            banner.Controls.Add(new Label
            {
                Text = "  Nettoyage avancé par application", Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var note = new Label
            {
                Location = new Point(18, 60), Size = new Size(564, 58), ForeColor = Color.FromArgb(90, 94, 102),
                Text = "Règles communautaires (base winapp2.ini, comme BleachBit). MODE SÛR : ONYX ne touche "
                     + "JAMAIS au registre, ne supprime que des fichiers cache sous des dossiers temporaires connus, "
                     + "n'affiche que les applications installées, et ne coche rien d'office. Vérifie la sélection avant de nettoyer."
            };
            Controls.Add(note);

            _source = new Label { Location = new Point(18, 122), Size = new Size(564, 18), ForeColor = Color.FromArgb(120, 124, 132), Font = new Font("Segoe UI", 8.5f) };
            Controls.Add(_source);

            _btnDownload = MakeBtn("⭳ Télécharger les règles", 18, 146, 190, 32, false);
            _btnDownload.Click += OnDownload;
            _btnLoad = MakeBtn("Charger un fichier…", 216, 146, 160, 32, false);
            _btnLoad.Click += OnLoadFile;
            Controls.Add(_btnDownload); Controls.Add(_btnLoad);

            _list = new CheckedListBox
            {
                Location = new Point(18, 188), Size = new Size(564, 262), CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9.5f), IntegralHeight = false
            };
            Controls.Add(_list);

            _total = new Label { Location = new Point(18, 458), Size = new Size(564, 22), ForeColor = Color.FromArgb(60, 64, 72), Font = new Font("Segoe UI Semibold", 9.5f) };
            Controls.Add(_total);

            _btnClean = MakeBtn("Nettoyer la sélection", 18, 490, 210, 36, true);
            _btnClean.Click += OnClean;
            _btnClose = MakeBtn("Fermer", 492, 490, 90, 36, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnClean); Controls.Add(_btnClose);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 10f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void SetSource(string text) { try { _source.Text = text; } catch { } }

        private void SetBusy(bool busy)
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            _btnDownload.Enabled = !busy; _btnLoad.Enabled = !busy; _btnClean.Enabled = !busy; _list.Enabled = !busy;
        }

        // --- Chargement des règles (fichier local / picker / téléchargement) ---
        private void OnLoadFile(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Filter = "Règles winapp2 (*.ini)|*.ini|Tous les fichiers|*.*", Title = "Charger un fichier de règles" })
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    string path = dlg.FileName;
                    LoadFrom(() => Winapp2.LoadFile(path), "fichier : " + System.IO.Path.GetFileName(path));
                }
        }

        private void OnDownload(object sender, EventArgs e)
        {
            if (MessageBox.Show(this,
                    "Télécharger la base de règles communautaire winapp2.ini (fichier texte, quelques Mo) depuis "
                    + "le dépôt public GitHub officiel ?\n\nElle sera enregistrée à côté de l'application pour la prochaine fois.",
                    "Télécharger les règles", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            LoadFrom(() => Winapp2.Parse(Winapp2.Download()), "règles communautaires (GitHub)");
        }

        private void LoadFrom(Func<List<Winapp2.Entry>> loader, string sourceLabel)
        {
            SetBusy(true);
            _total.Text = "";
            SetSource("Chargement et analyse (" + sourceLabel + ")…");
            _list.Items.Clear();
            Task.Run(() =>
            {
                List<Winapp2.Entry> entries = null;
                string error = null;
                try
                {
                    entries = loader();
                    foreach (Winapp2.Entry en in entries) Winapp2.Measure(en);
                }
                catch (Exception ex) { error = ex.Message; }
                try { BeginInvoke((Action)(() => Populate(entries, sourceLabel, error))); } catch { }
            });
        }

        private void Populate(List<Winapp2.Entry> entries, string sourceLabel, string error)
        {
            SetBusy(false);
            if (error != null)
            {
                SetSource("Échec : " + error);
                MessageBox.Show(this, "Impossible de charger les règles :\n" + error, "winapp2.ini",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // On ne montre que les applications détectées ET ayant quelque chose à nettoyer.
            _entries = new List<Winapp2.Entry>();
            foreach (Winapp2.Entry en in entries) if (en.SizeMB > 0) _entries.Add(en);
            _entries.Sort((a, b) => b.SizeMB.CompareTo(a.SizeMB));

            _list.Items.Clear();
            long sum = 0;
            foreach (Winapp2.Entry en in _entries)
            {
                string label = (en.Warning != null ? "⚠ " : "") + en.Name + "   —   " + en.SizeMB.ToString("N0") + " Mo";
                _list.Items.Add(label, false);   // rien de coché d'office
                sum += en.SizeMB;
            }

            SetSource(sourceLabel + " · " + _entries.Count + " application(s) détectée(s) avec du cache à nettoyer.");
            _total.Text = _entries.Count == 0
                ? "Rien à nettoyer parmi les applications détectées."
                : string.Format("Total récupérable : {0:N0} Mo  (coche ce que tu veux supprimer)", sum);
        }

        // --- Nettoyage ---
        private void OnClean(object sender, EventArgs e)
        {
            var sel = new List<Winapp2.Entry>();
            for (int i = 0; i < _list.Items.Count && i < _entries.Count; i++)
                if (_list.GetItemChecked(i)) sel.Add(_entries[i]);
            if (sel.Count == 0) return;

            bool hasWarn = sel.Exists(x => x.Warning != null);
            string msg = "Supprimer le contenu cache des " + sel.Count + " application(s) cochée(s) ?\n"
                       + "(Fichiers cache uniquement — action non réversible, mais ces caches se régénèrent.)";
            if (hasWarn) msg += "\n\n⚠ Une ou plusieurs entrées portent un avertissement de la communauté "
                              + "(elles peuvent effacer des données de session/connexion de l'appli concernée).";

            if (MessageBox.Show(this, msg, "Nettoyage avancé", MessageBoxButtons.OKCancel,
                    hasWarn ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.OK)
                return;

            SetBusy(true);
            Task.Run(() =>
            {
                int files = 0;
                foreach (Winapp2.Entry en in sel) files += Winapp2.Clean(en, _log);
                foreach (Winapp2.Entry en in sel) Winapp2.Measure(en);
                int done = files;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        RepopulateSizes();
                        if (_log != null) _log("Nettoyage avancé terminé : " + done + " fichier(s) supprimé(s).", 1);
                    }));
                }
                catch { }
            });
        }

        private void RepopulateSizes()
        {
            SetBusy(false);
            var kept = new List<Winapp2.Entry>();
            foreach (Winapp2.Entry en in _entries) if (en.SizeMB > 0) kept.Add(en);
            _entries = kept;
            _entries.Sort((a, b) => b.SizeMB.CompareTo(a.SizeMB));

            _list.Items.Clear();
            long sum = 0;
            foreach (Winapp2.Entry en in _entries)
            {
                _list.Items.Add((en.Warning != null ? "⚠ " : "") + en.Name + "   —   " + en.SizeMB.ToString("N0") + " Mo", false);
                sum += en.SizeMB;
            }
            _total.Text = string.Format("Total récupérable restant : {0:N0} Mo", sum);
        }
    }
}
