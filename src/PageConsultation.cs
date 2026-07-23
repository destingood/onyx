using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Consultation : CHAT avec « Le Copilote » — assistant LOCAL (aucun réseau). Comprend la demande,
    // s'appuie sur les vraies données du PC, et ouvre le bon outil. Équivalent du /app/chat de FPS Doctor.
    internal class PageConsultation : FpsPage
    {
        private FlowLayoutPanel _flow;
        private TextBox _input;
        private Button _send;
        private ScrollWheelFilter _wheel;
        private BadgeCatalog.Stats _stats;
        private bool _greeted;
        private bool _seeded;

        public PageConsultation(DashboardForm host) : base(host)
        {
            Build();
        }

        private void Build()
        {
            _flow = new BufferedFlow { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = FpsUi.BgMain };
            _flow.Padding = new Padding(20, 12, 20, 12);
            Controls.Add(_flow);
            _wheel = new ScrollWheelFilter(_flow);
            try { Application.AddMessageFilter(_wheel); } catch { }

            _input = new TextBox();
            try { _input.PlaceholderText = "Décris ton souci au Copilote… (ex. « ça rame en jeu », « ping élevé », « écran bloqué à 60 Hz »)"; } catch { }
            _input.BackColor = FpsUi.Card; _input.ForeColor = FpsUi.Ink;
            _input.BorderStyle = BorderStyle.FixedSingle; _input.Font = FpsUi.Body;
            _input.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendInput(); } };
            Controls.Add(_input);

            _send = FpsUi.NeonButton("Envoyer");
            _send.Click += (s, e) => SendInput();
            Controls.Add(_send);

            Resize += (s, e) => DoLayout();
        }

        public override void OnShown()
        {
            DoLayout();
            AppStats.Get(a => { try { BeginInvoke((Action)(() => { _stats = new BadgeCatalog.Stats { OptiActive = a.OptiActive, OptiTotal = a.OptiTotal, GamesDet = a.GamesDet, Health = a.Health }; Greet(); })); } catch { } });
            Greet();
            // Démo pour la capture hors-écran : montre un échange complet (bulles alignées + avatars).
            try { if (!_seeded && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BT_UISHOT"))) { _seeded = true; Send("ça rame et ça saccade en jeu"); ShowTyping(); } } catch { }
        }

        private void Greet()
        {
            if (_greeted) return;
            _greeted = true;
            AddBubble(true, null, DocAssistant.Intro(_stats));
        }

        private void SendInput()
        {
            string q = _input.Text.Trim();
            if (q.Length == 0) return;
            _input.Clear();
            Send(q);
        }

        private Timer _typingAnim;
        private Panel _typingRow;
        private int _dot;

        private void Send(string q)
        {
            AddBubble(false, q, null);
            var reply = DocAssistant.Answer(q, _stats, Host.Log);
            // Sous capture : réponse immédiate (pas de message loop long). En vrai : « Le Copilote écrit… ».
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BT_UISHOT"))) { AddBubble(true, reply.Text, reply); return; }
            ShowTyping();
            var t = new Timer { Interval = 650 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); HideTyping(); AddBubble(true, reply.Text, reply); };
            t.Start();
        }

        // Indicateur « Le Copilote écrit… » : mini-bulle avec 3 points qui pulsent.
        private void ShowTyping()
        {
            HideTyping();
            var bubble = new Panel { Size = new Size(66, 38), BackColor = Color.FromArgb(17, 19, 18) };
            bubble.SizeChanged += (s, e) => { try { using (var p = Round(bubble.ClientRectangle, 14)) bubble.Region = new Region(p); } catch { } };
            var dots = new Label { Dock = DockStyle.Fill, Font = FpsUi.H3, ForeColor = FpsUi.Neon, TextAlign = ContentAlignment.MiddleCenter, Text = "●··", BackColor = Color.Transparent };
            bubble.Controls.Add(dots);
            var avatar = MakeAvatar(true);
            var row = new Panel { Size = new Size(AV + GAP + 66, Math.Max(AV, 38)), BackColor = Color.Transparent, Margin = new Padding(6, 7, 10, 7), Tag = "typing" };
            avatar.Location = new Point(0, 0); bubble.Location = new Point(AV + GAP, 0);
            row.Controls.Add(avatar); row.Controls.Add(bubble);
            _typingRow = row; _flow.Controls.Add(row); try { _flow.ScrollControlIntoView(row); } catch { }
            _dot = 1;
            _typingAnim = new Timer { Interval = 320 };
            _typingAnim.Tick += (s, e) => { _dot = _dot % 3 + 1; dots.Text = new string('●', _dot) + new string('·', 3 - _dot); };
            _typingAnim.Start();
        }

        private void HideTyping()
        {
            if (_typingAnim != null) { try { _typingAnim.Stop(); _typingAnim.Dispose(); } catch { } _typingAnim = null; }
            if (_typingRow != null) { try { _flow.Controls.Remove(_typingRow); _typingRow.Dispose(); } catch { } _typingRow = null; }
        }

        private Panel MakeAvatar(bool doc)
        {
            var avatar = new Panel { Size = new Size(AV, AV), BackColor = Color.Transparent };
            avatar.Paint += (s, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                var rr = new Rectangle(0, 0, AV - 1, AV - 1);
                using (var br = new SolidBrush(doc ? Color.FromArgb(0, 34, 22) : Color.FromArgb(26, 28, 26))) g.FillEllipse(br, rr);
                using (var pen = new Pen(doc ? FpsUi.Neon : FpsUi.Border, 1.5f)) g.DrawEllipse(pen, rr);
                if (doc) Logo.Draw(g, new RectangleF(8, 8, AV - 16, AV - 16), FpsUi.Neon, false);
                else TextRenderer.DrawText(g, "🙂", FpsUi.Glyph, rr, FpsUi.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            return avatar;
        }

        // Suggestions de démarrage (label affiché → texte envoyé au Copilote).
        private static readonly (string, string)[] Starters =
        {
            ("Ça rame en jeu", "ça rame et ça saccade en jeu"),
            ("FPS bas", "mes fps sont bas"),
            ("Ping / lag en ligne", "ça lag en ligne, ping élevé"),
            ("Un jeu ne démarre pas", "un jeu refuse de démarrer, dll manquante"),
            ("Écran bloqué à 60 Hz", "mon écran semble bloqué à 60 hz"),
            ("Bilan complet du PC", "fais un bilan complet de mon pc"),
            ("Le PC chauffe", "le pc ou le gpu chauffe et bride"),
            ("Libérer de l'espace", "libérer de l'espace disque"),
        };

        private const int AV = 36, GAP = 10;

        private void AddBubble(bool doc, string text, DocAssistant.Reply reply)
        {
            string body = doc && reply != null ? reply.Text : text;
            int flowW = _flow.ClientSize.Width;
            int maxTextW = Math.Min(520, Math.Max(220, flowW - AV - GAP - 150));

            // Bulle auto-dimensionnée : Doc sombre / Toi vert accent, coins arrondis + liseré.
            Color bg = doc ? Color.FromArgb(17, 19, 18) : Color.FromArgb(0, 46, 29);
            Color bord = doc ? FpsUi.Border : Color.FromArgb(0, 96, 60);
            var bubble = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = bg, Padding = new Padding(14, 10, 16, 12), Margin = new Padding(0) };
            bubble.SizeChanged += (s, e) => { try { using (var p = Round(bubble.ClientRectangle, 14)) bubble.Region = new Region(p); } catch { } };
            bubble.Paint += (s, e) => { try { using (var pen = new Pen(bord)) using (var p = Round(new Rectangle(0, 0, bubble.Width - 1, bubble.Height - 1), 14)) e.Graphics.DrawPath(pen, p); } catch { } };

            var col = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            col.Controls.Add(new Label { AutoSize = true, Font = FpsUi.Small, ForeColor = doc ? FpsUi.Neon : Color.FromArgb(150, 255, 200), BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 4), Text = doc ? "COPILOTE" : "TOI" });
            col.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(maxTextW, 0), Font = FpsUi.Body, ForeColor = FpsUi.Ink, BackColor = Color.Transparent, Text = body ?? "" });

            if (reply != null && reply.Tool != null)
            {
                var btn = FpsUi.NeonButton("Ouvrir « " + reply.Tool.Tool + " »  →");
                btn.AutoSize = false; btn.Size = new Size(Math.Min(maxTextW, 320), 34); btn.Margin = new Padding(0, 8, 0, 2);
                var entry = reply.Tool;
                btn.Click += (s, e) => { try { Host.OpenDialog(entry.Open()); } catch { } };
                col.Controls.Add(btn);
            }
            if (reply != null && reply.ShowStarters)
            {
                var chips = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new Size(maxTextW, 0), BackColor = Color.Transparent, Margin = new Padding(0, 8, 0, 0) };
                foreach (var st in Starters) { var c = Chip(st.Item1); string sendText = st.Item2; c.Click += (s, e) => Send(sendText); chips.Controls.Add(c); }
                col.Controls.Add(chips);
            }
            bubble.Controls.Add(col);
            Size bs = bubble.PreferredSize;

            var avatar = MakeAvatar(doc);

            // Ligne avatar+bulle : Doc à GAUCHE, Toi à DROITE (vraie messagerie).
            int rowW = AV + GAP + bs.Width, rowH = Math.Max(AV, bs.Height);
            var row = new Panel { Size = new Size(rowW, rowH), BackColor = Color.Transparent, Tag = doc ? "doc" : "user", Margin = new Padding(doc ? 6 : Math.Max(6, flowW - rowW - 28), 7, 10, 7) };
            if (doc) { avatar.Location = new Point(0, 0); bubble.Location = new Point(AV + GAP, 0); }
            else { bubble.Location = new Point(0, 0); avatar.Location = new Point(bs.Width + GAP, 0); }
            row.Controls.Add(avatar); row.Controls.Add(bubble);
            _flow.Controls.Add(row);
            try { _flow.ScrollControlIntoView(row); } catch { }
        }

        // Ré-aligne les bulles « Toi » à droite quand la largeur change.
        private void RealignUserRows()
        {
            if (_flow == null) return;
            int flowW = _flow.ClientSize.Width;
            foreach (Control c in _flow.Controls)
                if ((c.Tag as string) == "user") { var m = c.Margin; m.Left = Math.Max(6, flowW - c.Width - 28); c.Margin = m; }
        }

        private static Button Chip(string text)
        {
            var b = new Button
            {
                Text = text, AutoSize = false, Height = 28,
                Width = TextRenderer.MeasureText(text, FpsUi.Small).Width + 24,
                FlatStyle = FlatStyle.Flat, Font = FpsUi.Small, Cursor = Cursors.Hand,
                BackColor = Color.FromArgb(13, 15, 14), ForeColor = FpsUi.Dim, Margin = new Padding(0, 0, 6, 6)
            };
            b.FlatAppearance.BorderColor = FpsUi.Border;
            b.MouseEnter += (s, e) => { b.ForeColor = FpsUi.Neon; b.FlatAppearance.BorderColor = FpsUi.Neon; };
            b.MouseLeave += (s, e) => { b.ForeColor = FpsUi.Dim; b.FlatAppearance.BorderColor = FpsUi.Border; };
            return b;
        }

        private void DoLayout()
        {
            if (_flow == null) return;
            int inputH = 40, m = 34, bottom = ClientSize.Height - 20;
            _flow.SetBounds(20, 96, ClientSize.Width - 40, Math.Max(120, ClientSize.Height - 96 - inputH - 24));
            if (_send != null) _send.SetBounds(ClientSize.Width - m - 120, bottom - inputH, 120, inputH);
            if (_input != null) _input.SetBounds(m, bottom - inputH + 6, Math.Max(120, ClientSize.Width - m - 120 - 12 - m), inputH - 12);
            RealignUserRows();
        }

        private static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath(); int d = rad * 2;
            if (r.Width < d || r.Height < d) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { HideTyping(); } catch { } if (_wheel != null) { try { Application.RemoveMessageFilter(_wheel); } catch { } _wheel = null; } }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "COPILOTE", "Le Copilote — décris ton souci, je t'ouvre le bon outil (assistant local, hors-ligne).");
        }
    }
}
