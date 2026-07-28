using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// « Bien brancher la box (4G/5G) » : le schéma de l'arrière d'une box mobile, DESSINÉ
    /// (vectoriel, net à tout DPI — pas une photo floue), avec le bon port pour le PC et la
    /// checklist complète des optimisations CÔTÉ BOX — celles qu'aucun réglage Windows ne
    /// remplace. Lecture seule : cette fenêtre n'écrit rien nulle part.
    /// </summary>
    internal class BoxWiringForm : Form
    {
        public BoxWiringForm()
        {
            Text = "ONYX — Bien brancher la box (4G/5G)";
            ClientSize = new Size(680, 640);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            Theme.Apply(this);
        }

        private void BuildUi()
        {
            var banner = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(12, 14, 13) };
            banner.Controls.Add(new Label
            {
                Text = "  Bien brancher la box — le schéma + la checklist côté box", Dock = DockStyle.Fill,
                ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            var schema = new BufferedPanel { Location = new Point(20, 74), Size = new Size(640, 240), BackColor = Color.FromArgb(11, 10, 9) };
            schema.Paint += PaintSchema;
            Controls.Add(schema);

            Controls.Add(new Label
            {
                Location = new Point(20, 324), Size = new Size(640, 258), ForeColor = Theme.InkColor,
                Text = "Checklist CÔTÉ BOX (à faire dans son interface, en général http://192.168.1.1) :\n"
                     + "•  PC sur le port ROUGE « LAN/WAN » (2,5 Gb/s) si la box est en 5G seule — il sert de LAN quand\n"
                     + "    aucune fibre n'y est branchée. Sinon, n'importe quel port jaune LAN (1 Gb/s).\n"
                     + "•  Vérifie le débit négocié dans le panneau Connexion 4G/5G : un Cat 5e abîmé retombe à 100 Mb/s\n"
                     + "    → change de câble ou de port, c'est physique, aucun logiciel ne le rattrape.\n"
                     + "•  SIGNAL : RSRP au-dessus de −100 dBm, SINR au-dessus de 10 dB — sinon déplace la box (fenêtre,\n"
                     + "    côté antenne-relais) ; chaque mur en moins se voit sur le ping.\n"
                     + "•  IPv6 : ACTIVÉ (c'est le chemin sans CGNAT sur les réseaux mobiles).\n"
                     + "•  UPnP : ACTIVÉ pour adoucir le NAT des jeux (le CGNAT opérateur reste, mais le NAT local suit).\n"
                     + "•  Wi-Fi de la box : COUPE-LE si tout le monde est en câble — moins de travail radio pour la box.\n"
                     + "•  QoS / priorisation : si la box le propose, mets le PC de jeu en priorité haute.\n"
                     + "•  Redémarre la box une fois par semaine : elle raccroche parfois une cellule lointaine et y reste.\n"
                     + "•  FXS (port blanc) = téléphone fixe uniquement — rien à brancher pour jouer.\n"
                     + "•  En soirée (20 h-23 h), l'antenne sature : mesure au panneau 4G/5G pour le prouver, ce n'est ni\n"
                     + "    le PC ni la box."
            });

            var close = new Button { Text = "Fermer", Location = new Point(560, 596), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat };
            close.Click += (s, e) => Close();
            Controls.Add(close);
        }

        // ------------------------------------------------------------------
        //  Le schéma : arrière de box + le bon câble, peint façon ONYX
        // ------------------------------------------------------------------
        private void PaintSchema(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Font cap = FpsUi.Tiny, capB = FpsUi.Small;
            Color yellow = Color.FromArgb(230, 200, 60), red = Color.FromArgb(214, 78, 68),
                  white = Color.FromArgb(235, 230, 220), okC = FpsUi.Ok;

            // Le panneau arrière de la box.
            var back = new Rectangle(24, 18, 592, 92);
            FpsUi.PaintCard(g, back, Color.FromArgb(26, 23, 18), FpsUi.Border, 10f);
            TextRenderer.DrawText(g, "ARRIÈRE DE LA BOX 4G/5G", cap, new Point(back.X + 10, back.Y + 6), FpsUi.Dim2, TextFormatFlags.NoPrefix);

            // Les ports : USB · LAN4..LAN1 (jaunes) · LAN/WAN (rouge, 2,5 Gb/s) · FXS (blanc).
            int px = back.X + 22, py = back.Y + 30, pw = 44, ph = 36, gap = 14;
            DrawPort(g, ref px, py, pw, ph, gap, Color.FromArgb(70, 66, 58), "USB", cap);
            int lanFirstX = px;
            for (int i = 4; i >= 1; i--) DrawPort(g, ref px, py, pw, ph, gap, yellow, "LAN" + i, cap);
            int redX = px;
            DrawPort(g, ref px, py, pw, ph, gap, red, "LAN/WAN", cap);
            int fxsX = px;
            DrawPort(g, ref px, py, pw, ph, gap, white, "FXS", cap);

            // Étiquette du port rouge : LE port à prendre.
            TextRenderer.DrawText(g, "2,5 Gb/s", cap, new Point(redX + 1, py - 16), FpsUi.Gold, TextFormatFlags.NoPrefix);

            // Le PC, relié au port ROUGE (câble doré = le bon branchement).
            var pc = new Rectangle(452, 156, 150, 64);
            FpsUi.PaintCard(g, pc, FpsUi.Card, Color.FromArgb(150, FpsUi.Gold), 10f);
            TextRenderer.DrawText(g, "PC DE JEU", capB, new Rectangle(pc.X, pc.Y + 10, pc.Width, 18), FpsUi.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "câble RJ45 (Cat 5e suffit)", cap, new Rectangle(pc.X, pc.Y + 32, pc.Width, 16), FpsUi.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);

            using (var pen = new Pen(FpsUi.Gold, 2.6f))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawLine(pen, redX + pw / 2f, py + ph, redX + pw / 2f, 140);
                g.DrawLine(pen, redX + pw / 2f, 140, pc.X + pc.Width / 2f, 140);
                g.DrawLine(pen, pc.X + pc.Width / 2f, 140, pc.X + pc.Width / 2f, pc.Y);
            }
            TextRenderer.DrawText(g, "① box 5G seule → port ROUGE (le plus rapide)", capB, new Point(374, 120), okC, TextFormatFlags.NoPrefix);

            // Le plan B : n'importe quel port jaune (pointillés).
            using (var pen = new Pen(Color.FromArgb(150, yellow), 2f))
            {
                pen.DashStyle = DashStyle.Dash;
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawLine(pen, lanFirstX + pw / 2f, py + ph, lanFirstX + pw / 2f, 168);
                g.DrawLine(pen, lanFirstX + pw / 2f, 168, 60, 168);
            }
            TextRenderer.DrawText(g, "② sinon : LAN1-LAN4 (1 Gb/s)", capB, new Point(38, 176), Color.FromArgb(214, 190, 110), TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "③ fibre branchée sur le port rouge ? il est pris : PC sur un port JAUNE", cap, new Point(38, 198), FpsUi.Dim2, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "④ FXS (blanc) = téléphone fixe, jamais le PC", cap, new Point(38, 216), FpsUi.Dim2, TextFormatFlags.NoPrefix);
        }

        private static void DrawPort(Graphics g, ref int x, int y, int w, int h, int gap, Color c, string label, Font cap)
        {
            var r = new Rectangle(x, y, w, h);
            using (var path = FpsUi.Round(new RectangleF(r.X, r.Y, r.Width, r.Height), 5f))
            {
                using (var br = new SolidBrush(Color.FromArgb(46, c))) g.FillPath(br, path);
                using (var pen = new Pen(c, 1.6f)) g.DrawPath(pen, path);
            }
            // La fente RJ45, pour que l'œil reconnaisse le port au premier regard.
            using (var br = new SolidBrush(Color.FromArgb(120, c)))
                g.FillRectangle(br, r.X + w / 2 - 9, r.Y + h - 12, 18, 7);
            TextRenderer.DrawText(g, label, cap, new Rectangle(r.X - gap / 2, r.Bottom + 4, w + gap, 14),
                FpsUi.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            x += w + gap;
        }
    }
}
