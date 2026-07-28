using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// « Bien brancher la box » — POUR TOUS LES TYPES : fibre, ADSL/VDSL, 4G/5G. Le schéma de
    /// l'arrière de chaque box, DESSINÉ (vectoriel, net à tout DPI — pas une photo floue), le bon
    /// port pour le PC, et la checklist complète des optimisations CÔTÉ BOX — celles qu'aucun
    /// réglage Windows ne remplace. Lecture seule : cette fenêtre n'écrit rien nulle part.
    /// </summary>
    internal class BoxWiringForm : Form
    {
        private MobileNet.Access _mode;
        private Button _bFiber, _bDsl, _bMobile;
        private BufferedPanel _schema;
        private Label _check;

        public BoxWiringForm() : this(FromEnv()) { }

        /// <summary>BT_BOXMODE=adsl|mobile : onglet initial pour les captures du harnais.</summary>
        private static MobileNet.Access FromEnv()
        {
            string m = Environment.GetEnvironmentVariable("BT_BOXMODE");
            if (m == "adsl") return MobileNet.Access.Dsl;
            if (m == "mobile") return MobileNet.Access.Mobile;
            return MobileNet.Access.Unknown;
        }

        public BoxWiringForm(MobileNet.Access mode)
        {
            // Par défaut : fibre (l'accès majoritaire) ; la mesure du panneau Ma connexion
            // pré-sélectionne le bon onglet quand elle a tourné.
            _mode = mode == MobileNet.Access.Unknown ? MobileNet.Access.Fiber : mode;
            Text = "ONYX — Bien brancher la box (fibre, ADSL, 4G/5G)";
            ClientSize = new Size(680, 656);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            Theme.Apply(this);
            UpdateMode(_mode);
        }

        private void BuildUi()
        {
            var banner = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(12, 14, 13) };
            banner.Controls.Add(new Label
            {
                Text = "  Bien brancher la box — le schéma + la checklist, par type", Dock = DockStyle.Fill,
                ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            _bFiber = ModeBtn("Fibre", 20, MobileNet.Access.Fiber);
            _bDsl = ModeBtn("ADSL / VDSL", 150, MobileNet.Access.Dsl);
            _bMobile = ModeBtn("Box 4G / 5G", 280, MobileNet.Access.Mobile);

            _schema = new BufferedPanel { Location = new Point(20, 112), Size = new Size(640, 236), BackColor = Color.FromArgb(11, 10, 9) };
            _schema.Paint += (s, e) => PaintSchema(e.Graphics);
            Controls.Add(_schema);

            _check = new Label { Location = new Point(20, 358), Size = new Size(640, 250), ForeColor = Theme.InkColor };
            Controls.Add(_check);

            var close = new Button { Text = "Fermer", Location = new Point(560, 614), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat };
            close.Click += (s, e) => Close();
            Controls.Add(close);
        }

        private Button ModeBtn(string label, int x, MobileNet.Access mode)
        {
            var b = new Button { Text = label, Location = new Point(x, 72), Size = new Size(124, 32), FlatStyle = FlatStyle.Flat };
            b.Click += (s, e) => UpdateMode(mode);
            Controls.Add(b);
            return b;
        }

        private void UpdateMode(MobileNet.Access mode)
        {
            _mode = mode;
            StyleModeBtn(_bFiber, mode == MobileNet.Access.Fiber);
            StyleModeBtn(_bDsl, mode == MobileNet.Access.Dsl);
            StyleModeBtn(_bMobile, mode == MobileNet.Access.Mobile);
            _check.Text = Checklist(mode);
            _schema.Invalidate();
        }

        private static void StyleModeBtn(Button b, bool on)
        {
            b.BackColor = on ? Theme.AccentColor : Theme.PanelColor;
            b.ForeColor = on ? Color.FromArgb(16, 13, 9) : Theme.InkColor;
            b.FlatAppearance.BorderSize = on ? 0 : 1;
        }

        // ------------------------------------------------------------------
        //  Les schémas, peints façon ONYX
        // ------------------------------------------------------------------
        private void PaintSchema(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            switch (_mode)
            {
                case MobileNet.Access.Dsl: PaintDsl(g); break;
                case MobileNet.Access.Mobile: PaintMobile(g); break;
                default: PaintFiber(g); break;
            }
        }

        private static readonly Color PortYellow = Color.FromArgb(230, 200, 60);
        private static readonly Color PortRed = Color.FromArgb(214, 78, 68);
        private static readonly Color PortWhite = Color.FromArgb(235, 230, 220);
        private static readonly Color PortGreen = Color.FromArgb(96, 200, 130);
        private static readonly Color PortGrey = Color.FromArgb(150, 145, 135);

        /// <summary>La carte PC + le câble doré qui part d'un port (le bon geste, tracé).</summary>
        private void DrawPc(Graphics g, float fromX, float fromY)
        {
            var pc = new Rectangle(452, 152, 150, 64);
            FpsUi.PaintCard(g, pc, FpsUi.Card, Color.FromArgb(150, FpsUi.Gold), 10f);
            TextRenderer.DrawText(g, "PC DE JEU", FpsUi.Small, new Rectangle(pc.X, pc.Y + 10, pc.Width, 18), FpsUi.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "câble RJ45 (Cat 5e suffit)", FpsUi.Tiny, new Rectangle(pc.X, pc.Y + 32, pc.Width, 16), FpsUi.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            using (var pen = new Pen(FpsUi.Gold, 2.6f))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawLine(pen, fromX, fromY, fromX, 136);
                g.DrawLine(pen, fromX, 136, pc.X + pc.Width / 2f, 136);
                g.DrawLine(pen, pc.X + pc.Width / 2f, 136, pc.X + pc.Width / 2f, pc.Y);
            }
        }

        private static void DrawPort(Graphics g, ref int x, int y, int w, int h, int gap, Color c, string label)
        {
            var r = new Rectangle(x, y, w, h);
            using (var path = FpsUi.Round(new RectangleF(r.X, r.Y, r.Width, r.Height), 5f))
            {
                using (var br = new SolidBrush(Color.FromArgb(46, c))) g.FillPath(br, path);
                using (var pen = new Pen(c, 1.6f)) g.DrawPath(pen, path);
            }
            using (var br = new SolidBrush(Color.FromArgb(120, c)))
                g.FillRectangle(br, r.X + w / 2 - 9, r.Y + h - 12, 18, 7);
            TextRenderer.DrawText(g, label, FpsUi.Tiny, new Rectangle(r.X - gap / 2, r.Bottom + 4, w + gap, 14),
                FpsUi.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            x += w + gap;
        }

        private void PaintFiber(Graphics g)
        {
            // La prise murale fibre (PTO) et sa fibre FRAGILE, en courbe large.
            var pto = new Rectangle(26, 40, 64, 44);
            FpsUi.PaintCard(g, pto, Color.FromArgb(26, 23, 18), FpsUi.Border, 8f);
            TextRenderer.DrawText(g, "PTO", FpsUi.Tiny, new Rectangle(pto.X, pto.Y + 6, pto.Width, 14), FpsUi.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "prise fibre", FpsUi.Tiny, new Rectangle(pto.X - 8, pto.Bottom + 4, pto.Width + 16, 14), FpsUi.Dim2,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);

            var back = new Rectangle(150, 18, 466, 92);
            FpsUi.PaintCard(g, back, Color.FromArgb(26, 23, 18), FpsUi.Border, 10f);
            TextRenderer.DrawText(g, "ARRIÈRE DE LA BOX FIBRE", FpsUi.Tiny, new Point(back.X + 10, back.Y + 6), FpsUi.Dim2, TextFormatFlags.NoPrefix);

            int px = back.X + 20, py = back.Y + 30, pw = 44, ph = 36, gap = 13;
            int fibX = px; DrawPort(g, ref px, py, pw, ph, gap, PortGreen, "FIBRE");
            DrawPort(g, ref px, py, pw, ph, gap, PortWhite, "TEL");
            int lan1X = px; DrawPort(g, ref px, py, pw, ph, gap, PortRed, "LAN1");
            DrawPort(g, ref px, py, pw, ph, gap, PortYellow, "LAN2");
            DrawPort(g, ref px, py, pw, ph, gap, PortYellow, "LAN3");
            DrawPort(g, ref px, py, pw, ph, gap, PortYellow, "LAN4");
            TextRenderer.DrawText(g, "2,5 G*", FpsUi.Tiny, new Point(lan1X + 4, py - 16), FpsUi.Gold, TextFormatFlags.NoPrefix);

            // La fibre : courbe LARGE volontaire — c'est le message (jamais pliée).
            using (var pen = new Pen(PortGreen, 2.2f))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawBezier(pen, pto.Right, pto.Y + 22, 118, 100, 128, 30, fibX + pw / 2f, py + ph);
            }
            DrawPc(g, lan1X + pw / 2f, py + ph);

            TextRenderer.DrawText(g, "① PC → LAN1 (2,5 G* sur les box récentes)", FpsUi.Small, new Point(150, 124), FpsUi.Ok, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "② fibre jamais pliée ni écrasée : boucles larges (> 3 cm)", FpsUi.Small, new Point(38, 152), Color.FromArgb(150, 210, 165), TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "③ TEL = téléphone · décodeur TV sur son port dédié", FpsUi.Tiny, new Point(38, 176), FpsUi.Dim2, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "* Livebox 6/7, Freebox Pop/Ultra, Bbox récentes", FpsUi.Tiny, new Point(38, 196), FpsUi.Dim2, TextFormatFlags.NoPrefix);
        }

        private void PaintDsl(Graphics g)
        {
            // La prise en T + LE FILTRE : la moitié des lignes ADSL lentes, c'est lui qui manque.
            var wall = new Rectangle(26, 34, 64, 44);
            FpsUi.PaintCard(g, wall, Color.FromArgb(26, 23, 18), FpsUi.Border, 8f);
            TextRenderer.DrawText(g, "PRISE T", FpsUi.Tiny, new Rectangle(wall.X, wall.Y + 6, wall.Width, 14), FpsUi.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            var filt = new Rectangle(38, 92, 44, 26);
            FpsUi.PaintCard(g, filt, Color.FromArgb(34, 30, 24), FpsUi.Gold, 6f);
            TextRenderer.DrawText(g, "FILTRE", FpsUi.Tiny, filt, FpsUi.Gold,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            var back = new Rectangle(150, 18, 466, 92);
            FpsUi.PaintCard(g, back, Color.FromArgb(26, 23, 18), FpsUi.Border, 10f);
            TextRenderer.DrawText(g, "ARRIÈRE DE LA BOX ADSL/VDSL", FpsUi.Tiny, new Point(back.X + 10, back.Y + 6), FpsUi.Dim2, TextFormatFlags.NoPrefix);

            int px = back.X + 20, py = back.Y + 30, pw = 44, ph = 36, gap = 13;
            int dslX = px; DrawPort(g, ref px, py, pw, ph, gap, PortGrey, "DSL");
            DrawPort(g, ref px, py, pw, ph, gap, PortWhite, "TEL");
            int lan1X = px; DrawPort(g, ref px, py, pw, ph, gap, PortYellow, "LAN1");
            DrawPort(g, ref px, py, pw, ph, gap, PortYellow, "LAN2");
            DrawPort(g, ref px, py, pw, ph, gap, PortYellow, "LAN3");
            DrawPort(g, ref px, py, pw, ph, gap, PortYellow, "LAN4");

            // Prise → filtre → port DSL (câble COURT : c'est aussi le message).
            using (var pen = new Pen(PortGrey, 2.2f))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawLine(pen, wall.X + wall.Width / 2f, wall.Bottom, filt.X + filt.Width / 2f, filt.Y);
                g.DrawLine(pen, filt.Right, filt.Y + filt.Height / 2f, dslX + pw / 2f, py + ph);
            }
            DrawPc(g, lan1X + pw / 2f, py + ph);

            TextRenderer.DrawText(g, "① un FILTRE sur CHAQUE prise téléphone", FpsUi.Small, new Point(150, 124), FpsUi.Gold, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "② câble DSL COURT (< 2 m), pas de rallonge plate", FpsUi.Small, new Point(38, 152), Color.FromArgb(214, 190, 110), TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "③ PC → LAN1-LAN4 (l'ADSL sature avant le port)", FpsUi.Tiny, new Point(38, 176), FpsUi.Dim2, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "④ box sur la prise PRINCIPALE de la maison", FpsUi.Tiny, new Point(38, 196), FpsUi.Dim2, TextFormatFlags.NoPrefix);
        }

        private void PaintMobile(Graphics g)
        {
            var back = new Rectangle(24, 18, 592, 92);
            FpsUi.PaintCard(g, back, Color.FromArgb(26, 23, 18), FpsUi.Border, 10f);
            TextRenderer.DrawText(g, "ARRIÈRE DE LA BOX 4G/5G", FpsUi.Tiny, new Point(back.X + 10, back.Y + 6), FpsUi.Dim2, TextFormatFlags.NoPrefix);

            int px = back.X + 22, py = back.Y + 30, pw = 44, ph = 36, gap = 14;
            DrawPort(g, ref px, py, pw, ph, gap, Color.FromArgb(70, 66, 58), "USB");
            int lanFirstX = px;
            for (int i = 4; i >= 1; i--) DrawPort(g, ref px, py, pw, ph, gap, PortYellow, "LAN" + i);
            int redX = px;
            DrawPort(g, ref px, py, pw, ph, gap, PortRed, "LAN/WAN");
            DrawPort(g, ref px, py, pw, ph, gap, PortWhite, "FXS");
            TextRenderer.DrawText(g, "2,5 Gb/s", FpsUi.Tiny, new Point(redX + 1, py - 16), FpsUi.Gold, TextFormatFlags.NoPrefix);

            DrawPc(g, redX + pw / 2f, py + ph);
            TextRenderer.DrawText(g, "① box 5G seule → port ROUGE (le plus rapide)", FpsUi.Small, new Point(374, 120), FpsUi.Ok, TextFormatFlags.NoPrefix);

            using (var pen = new Pen(Color.FromArgb(150, PortYellow), 2f))
            {
                pen.DashStyle = DashStyle.Dash;
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawLine(pen, lanFirstX + pw / 2f, py + ph, lanFirstX + pw / 2f, 164);
                g.DrawLine(pen, lanFirstX + pw / 2f, 164, 60, 164);
            }
            TextRenderer.DrawText(g, "② sinon : LAN1-LAN4 (1 Gb/s)", FpsUi.Small, new Point(38, 172), Color.FromArgb(214, 190, 110), TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "③ fibre branchée sur le port rouge ? il est pris : PC sur un port JAUNE", FpsUi.Tiny, new Point(38, 194), FpsUi.Dim2, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "④ FXS (blanc) = téléphone fixe, jamais le PC", FpsUi.Tiny, new Point(38, 212), FpsUi.Dim2, TextFormatFlags.NoPrefix);
        }

        // ------------------------------------------------------------------
        //  Les checklists côté box, par type
        // ------------------------------------------------------------------
        private static string Checklist(MobileNet.Access mode)
        {
            switch (mode)
            {
                case MobileNet.Access.Dsl:
                    return "Checklist CÔTÉ BOX ADSL/VDSL (interface : en général http://192.168.1.1) :\n"
                         + "•  Un FILTRE sur CHAQUE prise téléphone de la maison — un seul manquant suffit à polluer la ligne ;\n"
                         + "•  Câble DSL court (< 2 m), pas de rallonge téléphonique plate, box sur la prise principale ;\n"
                         + "•  Ping de base NORMAL : 15-40 ms (protection « interleaving »). Si le jeu compte : demande le\n"
                         + "    profil « fastpath » à ton FAI (−10 à −20 ms, un peu moins de protection contre les erreurs) ;\n"
                         + "•  Déconnexions fréquentes = ligne à faire tester par le FAI — ce n'est ni le PC ni Windows ;\n"
                         + "•  IPv6 : ACTIVÉ · UPnP : ACTIVÉ (NAT des jeux) · Wi-Fi coupé si tout le monde est en câble ;\n"
                         + "•  QoS / priorisation : PC de jeu en priorité haute si la box le propose ;\n"
                         + "•  Vérifie « débit négocié » dans Ma connexion : un câble RJ45 abîmé retombe à 100 Mb/s ;\n"
                         + "•  Redémarre la box après un orage ou une lenteur soudaine (resynchronisation de ligne).";
                case MobileNet.Access.Mobile:
                    return "Checklist CÔTÉ BOX 4G/5G (interface : en général http://192.168.1.1) :\n"
                         + "•  PC sur le port ROUGE « LAN/WAN » (2,5 Gb/s) si la box est en 5G seule — il sert de LAN quand\n"
                         + "    aucune fibre n'y est branchée. Sinon, n'importe quel port jaune LAN (1 Gb/s) ;\n"
                         + "•  SIGNAL : RSRP au-dessus de −100 dBm, SINR au-dessus de 10 dB — sinon déplace la box (fenêtre,\n"
                         + "    côté antenne-relais) ; chaque mur en moins se voit sur le ping ;\n"
                         + "•  IPv6 : ACTIVÉ (le chemin sans CGNAT) · UPnP : ACTIVÉ (le NAT local suit, le CGNAT reste) ;\n"
                         + "•  Wi-Fi coupé si tout le monde est en câble · QoS : PC de jeu prioritaire si disponible ;\n"
                         + "•  Vérifie « débit négocié » dans Ma connexion : un Cat 5e abîmé retombe à 100 Mb/s ;\n"
                         + "•  Redémarre la box chaque semaine : elle raccroche parfois une cellule lointaine et y reste ;\n"
                         + "•  FXS (port blanc) = téléphone fixe uniquement · En soirée (20 h-23 h) l'antenne sature :\n"
                         + "    mesure au panneau Ma connexion pour le prouver — ce n'est ni le PC ni la box.";
                default:
                    return "Checklist CÔTÉ BOX FIBRE (interface : 192.168.1.1, mafreebox.free.fr…) :\n"
                         + "•  PC sur le port le plus rapide : 2,5 G sur les box récentes (souvent LAN1), sinon n'importe quel\n"
                         + "    LAN — et vérifie « débit négocié » dans Ma connexion (100 Mb/s = câble/port en cause) ;\n"
                         + "•  La fibre verte ne se PLIE pas : boucles larges (> 3 cm), jamais coincée sous un meuble ou une\n"
                         + "    plinthe ; connecteurs PTO et box clipsés à fond, poussière = débit qui s'effondre ;\n"
                         + "•  Ping de base attendu : 2-10 ms — au-delà de 15 ms STABLE : redémarre la box, reteste à une\n"
                         + "    autre heure, sinon appelle le FAI avec les mesures du panneau ;\n"
                         + "•  IPv6 : ACTIVÉ · UPnP : ACTIVÉ (NAT des jeux) · Wi-Fi coupé si tout le monde est en câble ;\n"
                         + "•  QoS / priorisation : PC de jeu en priorité haute si la box le propose ;\n"
                         + "•  Décodeur TV sur son port dédié (il se réserve parfois une file de priorité) ;\n"
                         + "•  Redémarrage mensuel : les box fibre s'encrassent aussi (mémoire, sessions).";
            }
        }
    }
}
