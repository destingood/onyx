using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace FluideKeygen
{
    // Outil VENDEUR de gestion de licences Fluide : génère les clés (nom signé RSA-2048,
    // même convention que src/License.cs) et tient un JOURNAL des licences émises
    // (licences-emises.csv, à côté de private.xml). Ne JAMAIS livrer ce dossier au client.
    internal sealed class KeygenForm : Form
    {
        private const char Sep = (char)0x1F;
        private const char DateSep = (char)0x1E;

        // Palette Fluide
        private static readonly Color Bg = Color.FromArgb(14, 14, 22);
        private static readonly Color Panel = Color.FromArgb(22, 22, 32);
        private static readonly Color Field = Color.FromArgb(18, 18, 27);
        private static readonly Color Accent = Color.FromArgb(129, 140, 248);
        private static readonly Color AccentBtn = Color.FromArgb(99, 102, 241);
        private static readonly Color Ink = Color.FromArgb(236, 237, 245);
        private static readonly Color Dim = Color.FromArgb(148, 152, 165);
        private static readonly Color Ok = Color.FromArgb(120, 205, 145);
        private static readonly Color Err = Color.FromArgb(240, 120, 120);

        private TextBox _licensee, _note, _key;
        private RadioButton _life, _sub;
        private NumericUpDown _days;
        private Label _status, _count;
        private ListView _log;
        private Button _gen, _copyKey, _copyRow, _export, _openDir;
        private string _privPath;

        public KeygenForm()
        {
            Text = "Fluide — Générateur de licences";
            ClientSize = new Size(880, 640);
            MinimumSize = new Size(820, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Ink;
            Font = new Font("Segoe UI", 9.5f);
            TryLoadIcon();

            BuildUi();
            _privPath = FindPrivateKey();
            LoadLog();
            RefreshStatus();
        }

        // ---------------------------------------------------------------- UI

        private void BuildUi()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Panel };
            header.Paint += (s, e) =>
            {
                using (var br = new SolidBrush(Accent))
                    e.Graphics.FillRectangle(br, 0, header.Height - 3, header.Width, 3);
            };
            var hTitle = new Label
            {
                Text = "Fluide — Licences", AutoSize = true,
                Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold), ForeColor = Accent
            };
            hTitle.SetBounds(24, 12, 400, 30);
            var hSub = new Label
            {
                Text = "Génère les clés d'activation et tient le journal de tes ventes.",
                AutoSize = true, ForeColor = Dim
            };
            hSub.SetBounds(26, 46, 600, 18);
            header.Controls.AddRange(new Control[] { hTitle, hSub });

            // --- Colonne gauche : formulaire d'émission --------------------
            var form = new Panel { BackColor = Bg };
            form.SetBounds(24, 90, 360, 470);
            form.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;

            int y = 6;
            form.Controls.Add(Caption("Licencié (nom ou email — visible dans l'app)", 0, y)); y += 22;
            _licensee = Input(); _licensee.SetBounds(0, y, 360, 28); form.Controls.Add(_licensee); y += 40;

            form.Controls.Add(Caption("Email / note (journal uniquement, optionnel)", 0, y)); y += 22;
            _note = Input(); _note.SetBounds(0, y, 360, 28); form.Controls.Add(_note); y += 44;

            _life = Radio("Licence à vie", true); _life.SetBounds(0, y, 130, 24); form.Controls.Add(_life);
            _sub = Radio("Abonnement", false); _sub.SetBounds(140, y, 110, 24); form.Controls.Add(_sub);
            y += 30;
            _days = new NumericUpDown
            {
                Minimum = 1, Maximum = 3650, Value = 365, Enabled = false, Width = 80,
                BackColor = Field, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle
            };
            _days.SetBounds(0, y, 80, 26); form.Controls.Add(_days);
            var daysL = new Label { Text = "jours de validité", AutoSize = true, ForeColor = Dim };
            daysL.SetBounds(90, y + 4, 160, 18); form.Controls.Add(daysL);
            _sub.CheckedChanged += (s, e) => _days.Enabled = _sub.Checked;
            y += 42;

            _gen = Btn("Générer la licence", true); _gen.SetBounds(0, y, 360, 40);
            _gen.Click += OnGenerate; form.Controls.Add(_gen); y += 52;

            form.Controls.Add(Caption("Clé générée", 0, y)); y += 22;
            _key = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(10, 10, 16), ForeColor = Ok,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9f), WordWrap = true
            };
            _key.SetBounds(0, y, 360, 96); form.Controls.Add(_key); y += 104;
            _copyKey = Btn("Copier la clé", false); _copyKey.SetBounds(0, y, 360, 32);
            _copyKey.Click += (s, e) => CopyText(_key.Text, _copyKey, "Copier la clé");
            form.Controls.Add(_copyKey);

            // --- Colonne droite : journal des licences ---------------------
            var jLabel = new Label { Text = "Journal des licences émises", AutoSize = true, ForeColor = Dim };
            jLabel.SetBounds(408, 92, 300, 18);
            jLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;

            _log = new ListView
            {
                View = View.Details, FullRowSelect = true, GridLines = false, HideSelection = false,
                BackColor = Panel, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle, OwnerDraw = true
            };
            _log.SetBounds(408, 114, 448, 400);
            _log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _log.Columns.Add("Date", 78);
            _log.Columns.Add("Licencié", 150);
            _log.Columns.Add("Type", 70);
            _log.Columns.Add("Expire", 80);
            _log.Columns.Add("Note", 64);
            _log.DrawColumnHeader += OnDrawHeader;
            _log.DrawItem += (s, e) => e.DrawDefault = true;
            _log.DrawSubItem += (s, e) => e.DrawDefault = true;
            _log.DoubleClick += (s, e) => CopySelectedKey();

            _copyRow = Btn("Copier la clé sélectionnée", false);
            _copyRow.SetBounds(408, 524, 220, 32);
            _copyRow.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _copyRow.Click += (s, e) => CopySelectedKey();

            _export = Btn("Exporter le journal", false);
            _export.SetBounds(636, 524, 130, 32);
            _export.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _export.Click += OnExport;

            _openDir = Btn("Dossier", false);
            _openDir.SetBounds(774, 524, 82, 32);
            _openDir.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _openDir.Click += (s, e) => { try { if (_privPath != null) System.Diagnostics.Process.Start("explorer.exe", Path.GetDirectoryName(_privPath)); } catch { } };

            // --- Barre d'état ---------------------------------------------
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 46, BackColor = Panel };
            _status = new Label { AutoSize = false, ForeColor = Dim, TextAlign = ContentAlignment.MiddleLeft };
            _status.SetBounds(24, 0, 620, 46);
            _status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
            _count = new Label { AutoSize = false, ForeColor = Accent, TextAlign = ContentAlignment.MiddleRight };
            _count.SetBounds(650, 0, 210, 46);
            _count.Anchor = AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
            var browse = new LinkLabel { Text = "Changer private.xml…", AutoSize = true, LinkColor = Accent, ActiveLinkColor = Ink };
            browse.SetBounds(650, 14, 140, 18);
            browse.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            browse.Visible = false; // affiché seulement si clé introuvable
            browse.Click += (s, e) => OnBrowse();
            _browseLink = browse;
            bar.Controls.AddRange(new Control[] { _status, _count });

            Controls.Add(_log);
            Controls.Add(jLabel);
            Controls.Add(_copyRow);
            Controls.Add(_export);
            Controls.Add(_openDir);
            Controls.Add(form);
            Controls.Add(bar);
            Controls.Add(header);
            Controls.Add(browse);
            browse.BringToFront();
        }

        private LinkLabel _browseLink;

        private Label Caption(string t, int x, int y)
        {
            var l = new Label { Text = t, AutoSize = true, ForeColor = Dim };
            l.SetBounds(x, y, 360, 18);
            return l;
        }

        private TextBox Input()
        {
            return new TextBox { BackColor = Field, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle };
        }

        private RadioButton Radio(string t, bool on)
        {
            return new RadioButton { Text = t, Checked = on, AutoSize = true, ForeColor = Ink };
        }

        private Button Btn(string text, bool primary)
        {
            var b = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
                BackColor = primary ? AccentBtn : Color.FromArgb(34, 34, 46),
                ForeColor = Color.White, Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 82);
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(120, 122, 255) : Color.FromArgb(44, 44, 60);
            return b;
        }

        private void OnDrawHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var br = new SolidBrush(Color.FromArgb(30, 30, 42))) e.Graphics.FillRectangle(br, e.Bounds);
            using (var pen = new Pen(Color.FromArgb(45, 45, 60))) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
                Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        // ------------------------------------------------------------ Logic

        private void RefreshStatus()
        {
            bool ok = _privPath != null && File.Exists(_privPath);
            _gen.Enabled = ok;
            if (_browseLink != null) _browseLink.Visible = !ok;
            _status.ForeColor = ok ? Ok : Err;
            _status.Text = ok ? ("Clé privée : " + _privPath) : "private.xml introuvable — clique « Changer private.xml… »";
        }

        private void OnBrowse()
        {
            using (var dlg = new OpenFileDialog { Filter = "Clé privée (private.xml)|*.xml|Tous|*.*", Title = "Choisir private.xml" })
                if (dlg.ShowDialog(this) == DialogResult.OK) { _privPath = dlg.FileName; LoadLog(); RefreshStatus(); }
        }

        private void OnGenerate(object sender, EventArgs e)
        {
            string name = (_licensee.Text ?? "").Trim();
            if (name.Length == 0) { _key.Text = "Entre d'abord un nom / email de licencié."; return; }
            if (_privPath == null || !File.Exists(_privPath)) { RefreshStatus(); return; }

            int? days = _sub.Checked ? (int?)(int)_days.Value : null;
            try
            {
                string privXml = File.ReadAllText(_privPath);
                DateTime? exp;
                string token = MakeKey(privXml, name, days, out exp);
                _key.Text = token;
                CopyText(token, _copyKey, "Copier la clé");
                _copyKey.Text = "Copié ✓";

                var rec = new Rec
                {
                    Date = DateTime.Now,
                    Licensee = name,
                    Note = (_note.Text ?? "").Trim(),
                    Lifetime = !days.HasValue,
                    Expiry = exp,
                    Key = token
                };
                AppendLog(rec);
                AddRow(rec, true);
                UpdateCount();
                _licensee.Clear(); _note.Clear(); _licensee.Focus();
            }
            catch (Exception ex)
            {
                _key.Text = "ERREUR : " + ex.Message;
            }
        }

        private static string MakeKey(string privXml, string name, int? days, out DateTime? expiry)
        {
            string payload = name;
            expiry = null;
            if (days.HasValue)
            {
                DateTime exp = DateTime.Now.Date.AddDays(days.Value);
                expiry = exp;
                payload = name + DateSep + exp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            using (RSA rsa = RSA.Create())
            {
                rsa.FromXmlString(privXml);
                byte[] sig = rsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                string p = payload + Sep + Convert.ToBase64String(sig);
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(p));
            }
        }

        private void CopySelectedKey()
        {
            if (_log.SelectedItems.Count == 0) return;
            var rec = _log.SelectedItems[0].Tag as Rec;
            if (rec != null) CopyText(rec.Key, _copyRow, "Copier la clé sélectionnée");
        }

        private void CopyText(string t, Button b, string reset)
        {
            if (string.IsNullOrEmpty(t)) return;
            try { Clipboard.SetText(t); string o = b.Text; b.Text = "Copié ✓"; var tm = new Timer { Interval = 1200 }; tm.Tick += (s, e) => { b.Text = reset; tm.Stop(); tm.Dispose(); }; tm.Start(); }
            catch { }
        }

        // ------------------------------------------------------------ Journal (CSV)

        private sealed class Rec
        {
            public DateTime Date;
            public string Licensee, Note, Key;
            public bool Lifetime;
            public DateTime? Expiry;
        }

        private string LogPath
        {
            get
            {
                string dir = _privPath != null ? Path.GetDirectoryName(_privPath) : AppContext.BaseDirectory;
                return Path.Combine(dir, "licences-emises.csv");
            }
        }

        private void LoadLog()
        {
            _log.Items.Clear();
            try
            {
                if (!File.Exists(LogPath)) { UpdateCount(); return; }
                string[] lines = File.ReadAllLines(LogPath, Encoding.UTF8);
                for (int i = 1; i < lines.Length; i++) // saute l'en-tête
                {
                    List<string> f = ParseCsv(lines[i]);
                    if (f.Count < 6) continue;
                    var rec = new Rec
                    {
                        Date = ParseD(f[0]),
                        Licensee = f[1],
                        Note = f[2],
                        Lifetime = f[3] == "vie",
                        Expiry = string.IsNullOrEmpty(f[4]) ? (DateTime?)null : ParseD(f[4]),
                        Key = f[5]
                    };
                    AddRow(rec, false);
                }
            }
            catch { }
            UpdateCount();
        }

        private static DateTime ParseD(string s)
        {
            DateTime d;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : DateTime.Now;
        }

        private void AppendLog(Rec r)
        {
            try
            {
                bool head = !File.Exists(LogPath);
                using (var w = new StreamWriter(LogPath, true, new UTF8Encoding(true)))
                {
                    if (head) w.WriteLine("Date,Licencie,Note,Type,Expiration,Cle");
                    w.WriteLine(string.Join(",", new[]
                    {
                        Csv(r.Date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                        Csv(r.Licensee), Csv(r.Note),
                        Csv(r.Lifetime ? "vie" : "abonnement"),
                        Csv(r.Expiry.HasValue ? r.Expiry.Value.ToString("yyyy-MM-dd") : ""),
                        Csv(r.Key)
                    }));
                }
            }
            catch (Exception ex) { MessageBox.Show(this, "Impossible d'écrire le journal :\n" + ex.Message, "Fluide"); }
        }

        private void AddRow(Rec r, bool top)
        {
            var it = new ListViewItem(r.Date.ToString("dd/MM/yy"));
            it.SubItems.Add(r.Licensee);
            it.SubItems.Add(r.Lifetime ? "À vie" : "Abonnement");
            it.SubItems.Add(r.Lifetime ? "—" : (r.Expiry.HasValue ? r.Expiry.Value.ToString("dd/MM/yy") : "?"));
            it.SubItems.Add(r.Note);
            it.Tag = r;
            it.UseItemStyleForSubItems = true;
            if (top) _log.Items.Insert(0, it); else _log.Items.Add(it);
        }

        private void UpdateCount()
        {
            _count.Text = _log.Items.Count + " licence(s) émise(s)";
        }

        private void OnExport(object sender, EventArgs e)
        {
            if (!File.Exists(LogPath)) { MessageBox.Show(this, "Aucune licence émise pour l'instant.", "Fluide"); return; }
            using (var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "licences-fluide.csv", Title = "Exporter le journal" })
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    try { File.Copy(LogPath, dlg.FileName, true); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Fluide"); }
        }

        private static string Csv(string s)
        {
            if (s == null) s = "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static List<string> ParseCsv(string line)
        {
            var res = new List<string>();
            if (line == null) return res;
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; }
                    else sb.Append(c);
                }
                else
                {
                    if (c == ',') { res.Add(sb.ToString()); sb.Length = 0; }
                    else if (c == '"') q = true;
                    else sb.Append(c);
                }
            }
            res.Add(sb.ToString());
            return res;
        }

        // ------------------------------------------------------------ Divers

        private void TryLoadIcon()
        {
            string ico = FindUp("src/app.ico");
            if (ico != null) { try { Icon = new Icon(ico); } catch { } }
        }

        private static string FindPrivateKey()
        {
            string byName = FindUp("private.xml");
            if (byName != null) return byName;
            return FindUp("seller/private.xml");
        }

        // Cherche un fichier en remontant l'arborescence depuis le dossier de l'exe (et le cwd).
        private static string FindUp(string rel)
        {
            var roots = new List<string>();
            roots.Add(AppContext.BaseDirectory);
            try { roots.Add(Directory.GetCurrentDirectory()); } catch { }
            foreach (string start in roots)
            {
                string d = start;
                for (int i = 0; i < 9 && !string.IsNullOrEmpty(d); i++)
                {
                    try { string c = Path.Combine(d, rel); if (File.Exists(c)) return c; } catch { }
                    try { DirectoryInfo p = Directory.GetParent(d); d = p != null ? p.FullName : null; } catch { d = null; }
                }
            }
            return null;
        }
    }
}
