using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Consultation : CHAT avec « Le Doc » — assistant LOCAL (aucun réseau). Comprend la demande,
    // s'appuie sur les vraies données du PC, et ouvre le bon outil. Équivalent du /app/chat de FPS Doctor.
    internal class PageConsultation : FpsPage
    {
        private FlowLayoutPanel _flow;
        private TextBox _input;
        private Button _send;
        private ScrollWheelFilter _wheel;
        private BadgeCatalog.Stats _stats;
        private bool _greeted;

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
            try { _input.PlaceholderText = "Décris ton souci au Doc… (ex. « ça rame en jeu », « ping élevé », « écran bloqué à 60 Hz »)"; } catch { }
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

        private void Send(string q)
        {
            AddBubble(false, q, null);
            var reply = DocAssistant.Answer(q, _stats, Host.Log);
            AddBubble(true, reply.Text, reply);
        }

        // Suggestions de démarrage (label affiché → texte envoyé au Doc).
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

        private void AddBubble(bool doc, string text, DocAssistant.Reply reply)
        {
            string body = doc && reply != null ? reply.Text : text;
            int maxTextW = Math.Max(240, _flow.ClientSize.Width - 160);

            var bubble = new Panel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = doc ? FpsUi.Card : Color.FromArgb(16, 30, 23),
                Margin = new Padding(doc ? 4 : 60, 6, 12, 6), Padding = new Padding(14, 10, 16, 12)
            };
            bubble.SizeChanged += (s, e) => { try { using (var p = Round(bubble.ClientRectangle, 12)) bubble.Region = new Region(p); } catch { } };

            var col = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            col.Controls.Add(new Label { AutoSize = true, Font = FpsUi.Small, ForeColor = doc ? FpsUi.Neon : FpsUi.Dim, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 4), Text = doc ? "●  LE DOC" : "TOI" });
            col.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(maxTextW, 0), Font = FpsUi.Body, ForeColor = FpsUi.Ink, BackColor = Color.Transparent, Text = body ?? "" });

            if (reply != null && reply.Tool != null)
            {
                var btn = FpsUi.NeonButton("Ouvrir « " + reply.Tool.Tool + " »  →");
                btn.AutoSize = false; btn.Size = new Size(Math.Min(maxTextW, 320), 34); btn.Margin = new Padding(0, 8, 0, 0);
                var entry = reply.Tool;
                btn.Click += (s, e) => { try { Host.OpenDialog(entry.Open()); } catch { } };
                col.Controls.Add(btn);
            }
            if (reply != null && reply.ShowStarters)
            {
                var chips = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new Size(maxTextW, 0), BackColor = Color.Transparent, Margin = new Padding(0, 8, 0, 0) };
                foreach (var st in Starters)
                {
                    var c = Chip(st.Item1); string sendText = st.Item2;
                    c.Click += (s, e) => Send(sendText);
                    chips.Controls.Add(c);
                }
                col.Controls.Add(chips);
            }
            bubble.Controls.Add(col);
            _flow.Controls.Add(bubble);
            try { _flow.ScrollControlIntoView(bubble); } catch { }
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
            if (disposing && _wheel != null) { try { Application.RemoveMessageFilter(_wheel); } catch { } _wheel = null; }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "CONSULTATION", "Le Doc — décris ton souci, je t'ouvre le bon soin (assistant local, hors-ligne).");
        }
    }
}
