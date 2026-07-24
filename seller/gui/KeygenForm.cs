using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FluideKeygen
{
    // Générateur GRAPHIQUE de clés de licence Fluide.
    //  - tape un ou plusieurs noms (un par ligne), choisis À vie ou Abonnement, clique Générer.
    //  - signe avec private.xml (même convention que src/License.cs : nom [+ date] signé RSA-2048).
    //  ⚠️ Outil VENDEUR : ne JAMAIS le livrer à un client (il ne fonctionne de toute façon
    //     qu'avec private.xml, ta clé-maîtresse, à garder secrète).
    internal sealed class KeygenForm : Form
    {
        private const char Sep = (char)0x1F;      // sépare le payload signé de la signature
        private const char DateSep = (char)0x1E;  // sépare le nom de la date (abonnement)

        private TextBox _names, _out;
        private RadioButton _life, _sub;
        private NumericUpDown _days;
        private Label _status;
        private Button _gen, _copy, _browse;
        private string _privPath;

        public KeygenForm()
        {
            BuildUi();
            _privPath = FindPrivateKey();
            RefreshStatus();
        }

        private void BuildUi()
        {
            Text = "Fluide — Générateur de licences";
            ClientSize = new Size(720, 560);
            MinimumSize = new Size(640, 500);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(16, 16, 24);
            ForeColor = Color.FromArgb(235, 236, 242);
            Font = new Font("Segoe UI", 9.5f);

            var title = new Label
            {
                Text = "Générateur de clés d'activation",
                Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(129, 140, 248),
                AutoSize = true
            };
            title.SetBounds(20, 16, 400, 30);

            var help = new Label
            {
                Text = "Un nom (ou email) par ligne. Une clé sera générée pour chacun.",
                ForeColor = Color.FromArgb(150, 154, 165),
                AutoSize = true
            };
            help.SetBounds(22, 50, 640, 20);

            _names = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(24, 24, 34),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _names.SetBounds(22, 76, 676, 96);
            _names.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            _life = new RadioButton { Text = "À vie", Checked = true, AutoSize = true, ForeColor = ForeColor };
            _life.SetBounds(22, 184, 80, 24);
            _sub = new RadioButton { Text = "Abonnement", AutoSize = true, ForeColor = ForeColor };
            _sub.SetBounds(110, 184, 110, 24);
            _days = new NumericUpDown
            {
                Minimum = 1, Maximum = 3650, Value = 365, Enabled = false, Width = 70,
                BackColor = Color.FromArgb(24, 24, 34), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle
            };
            _days.SetBounds(224, 184, 70, 24);
            var daysLbl = new Label { Text = "jours", AutoSize = true, ForeColor = ForeColor };
            daysLbl.SetBounds(300, 187, 50, 20);
            _sub.CheckedChanged += (s, e) => _days.Enabled = _sub.Checked;

            _gen = MakeButton("Générer les clés", 520, 182, 178, 30, true);
            _gen.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _gen.Click += OnGenerate;

            var outLbl = new Label { Text = "Clés générées :", AutoSize = true, ForeColor = Color.FromArgb(150, 154, 165) };
            outLbl.SetBounds(22, 224, 200, 20);

            _out = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(10, 10, 16),
                ForeColor = Color.FromArgb(200, 230, 210),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f),
                WordWrap = true
            };
            _out.SetBounds(22, 248, 676, 236);
            _out.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            _copy = MakeButton("Tout copier", 22, 494, 130, 30, false);
            _copy.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _copy.Click += (s, e) => { if (_out.TextLength > 0) { try { Clipboard.SetText(_out.Text); _copy.Text = "Copié ✓"; } catch { } } };

            _browse = MakeButton("Choisir private.xml…", 162, 494, 180, 30, false);
            _browse.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _browse.Click += OnBrowse;

            _status = new Label { AutoSize = false, ForeColor = Color.FromArgb(150, 154, 165) };
            _status.SetBounds(352, 494, 346, 46);
            _status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            Controls.AddRange(new Control[]
            {
                title, help, _names, _life, _sub, _days, daysLbl, _gen,
                outLbl, _out, _copy, _browse, _status
            });
        }

        private Button MakeButton(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(99, 102, 241) : Color.FromArgb(32, 32, 44),
                ForeColor = Color.White,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 80);
            b.SetBounds(x, y, w, h);
            return b;
        }

        private void RefreshStatus()
        {
            if (_privPath != null)
            {
                _status.ForeColor = Color.FromArgb(120, 200, 140);
                _status.Text = "Clé privée : " + _privPath;
                _gen.Enabled = true;
            }
            else
            {
                _status.ForeColor = Color.FromArgb(240, 120, 120);
                _status.Text = "private.xml introuvable. Clique « Choisir private.xml… ».";
                _gen.Enabled = false;
            }
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Filter = "Clé privée (private.xml)|*.xml|Tous|*.*", Title = "Choisir private.xml" })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _privPath = dlg.FileName;
                    RefreshStatus();
                }
            }
        }

        private void OnGenerate(object sender, EventArgs e)
        {
            if (_privPath == null || !File.Exists(_privPath)) { RefreshStatus(); return; }
            _copy.Text = "Tout copier";

            string[] lines = _names.Text.Replace("\r\n", "\n").Split('\n');
            int? days = _sub.Checked ? (int?)(int)_days.Value : null;

            string privXml;
            try { privXml = File.ReadAllText(_privPath); }
            catch (Exception ex) { _out.Text = "Impossible de lire private.xml : " + ex.Message; return; }

            var sb = new StringBuilder();
            int n = 0;
            foreach (string raw in lines)
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                try
                {
                    string human;
                    string key = MakeKey(privXml, name, days, out human);
                    sb.AppendLine("=== " + name + " ===");
                    sb.AppendLine(human);
                    sb.AppendLine(key);
                    sb.AppendLine();
                    n++;
                }
                catch (Exception ex)
                {
                    sb.AppendLine("=== " + name + " ===");
                    sb.AppendLine("ERREUR : " + ex.Message);
                    sb.AppendLine();
                }
            }
            if (n == 0 && sb.Length == 0) sb.AppendLine("(Aucun nom saisi.)");
            _out.Text = sb.ToString();
        }

        private static string MakeKey(string privXml, string name, int? days, out string human)
        {
            string payload = name;
            if (days.HasValue)
            {
                DateTime exp = DateTime.Now.Date.AddDays(days.Value);
                payload = name + DateSep + exp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                human = "Abonnement — expire le " + exp.ToString("dd/MM/yyyy");
            }
            else human = "Licence À VIE";

            using (RSA rsa = RSA.Create())
            {
                rsa.FromXmlString(privXml);
                byte[] sig = rsa.SignData(Encoding.UTF8.GetBytes(payload),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                string p = payload + Sep + Convert.ToBase64String(sig);
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(p));
            }
        }

        // Cherche private.xml : dossier courant, dossier de l'exe, puis en remontant l'arborescence
        // (le .exe tourne dans seller/gui/bin/... ; private.xml est dans seller/).
        private static string FindPrivateKey()
        {
            var cands = new List<string>();
            try { cands.Add(Path.Combine(Directory.GetCurrentDirectory(), "private.xml")); } catch { }
            cands.Add(Path.Combine(AppContext.BaseDirectory, "private.xml"));
            string d = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(d); i++)
            {
                cands.Add(Path.Combine(d, "private.xml"));
                cands.Add(Path.Combine(d, "seller", "private.xml"));
                try { DirectoryInfo p = Directory.GetParent(d); d = p != null ? p.FullName : null; }
                catch { d = null; }
            }
            foreach (string c in cands)
            {
                try { if (File.Exists(c)) return c; } catch { }
            }
            return null;
        }
    }
}
