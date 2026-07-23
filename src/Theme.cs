using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Thème clair/sombre appliqué récursivement + habillage « pro » global :
    /// barre de titre sombre fusionnée au bandeau (DWM), menus contextuels assortis,
    /// boutons plats redessinés en boutons arrondis (survol/pression/focus/désactivé),
    /// GroupBox en cartes arrondies, en-têtes de tableaux plats, liseré dégradé
    /// signature sous chaque bandeau. L'applicateur reconnaît les conventions de
    /// couleur déjà présentes dans l'app (bannières sombres, boutons accent, tuiles)
    /// et mémorise le rôle de chaque contrôle à la première visite pour que les
    /// bascules de thème successives restent stables.
    /// </summary>
    internal static class Theme
    {
        public static bool Dark { get; private set; }

        // Repères des couleurs codées en dur dans les fenêtres.
        private static readonly Color HeaderBg = Color.FromArgb(12, 14, 13);   // bandeau quasi-noir (identité DTG)
        private static readonly Color AccentRef = Color.FromArgb(0, 150, 90);

        // Liseré signature sous les bandeaux (identité Fluide).
        private static readonly Color BrandA = Color.FromArgb(0, 205, 130);
        private static readonly Color BrandB = Color.FromArgb(0, 140, 235);

        // Tokens (basculent avec le thème).
        private static Color Bg, Panel, Ink, InkDim, Line, GroupInk, FieldBg;
        private static Color MenuBg, MenuHot, MenuLine;

        // Couleurs exposées aux fenêtres (toujours lisibles dans le thème courant).
        public static Color InkColor { get { return Ink; } }
        public static Color InkDimColor { get { return InkDim; } }
        public static Color PanelColor { get { return Panel; } }
        public static Color FieldColor { get { return FieldBg; } }
        public static Color LineColor { get { return Line; } }
        public static Color OkColor { get { return Dark ? Color.FromArgb(70, 200, 130) : Color.FromArgb(0, 130, 0); } }
        public static Color AccentColor { get { return Dark ? Color.FromArgb(0, 190, 120) : AccentRef; } }

        private static string StorePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-theme.txt"); } }

        static Theme()
        {
            // Sombre par défaut (identité gaming Fluide) ; le choix de l'utilisateur,
            // une fois fait, est respecté (bt-theme.txt).
            try
            {
                Dark = File.Exists(StorePath)
                    ? File.ReadAllText(StorePath).Trim() == "dark"
                    : true;
            }
            catch { Dark = true; }
            LoadTokens();
            // Tous les menus de l'app (menu ☰, tray, popups) passent par ce moteur de rendu.
            try { ToolStripManager.Renderer = new MenuRenderer(); } catch { }
        }

        /// <summary>Force l'initialisation statique (moteur de rendu des menus sombre) au
        /// démarrage, même si aucune fenêtre v14 n'a encore été ouverte. Sans cet appel, le
        /// menu ⋯ / tray du shell s'affichait avec le rendu clair par défaut de Windows.</summary>
        public static void Prime() { }

        private static void LoadTokens()
        {
            if (Dark)
            {
                // Palette DTG : noir profond + vert néon, alignée sur le shell (FpsUi).
                // Toutes les fenêtres v14 passent par ces tokens (Theme.Apply) — les retoucher
                // ici reskin l'ensemble des ~34 fenêtres d'un coup.
                Bg = Color.FromArgb(9, 11, 10); Panel = Color.FromArgb(16, 18, 17);
                Ink = Color.FromArgb(240, 242, 241); InkDim = Color.FromArgb(150, 154, 150);
                Line = Color.FromArgb(34, 37, 35); GroupInk = Color.FromArgb(150, 182, 165);
                FieldBg = Color.FromArgb(13, 15, 14);
                MenuBg = Color.FromArgb(16, 18, 17); MenuHot = Color.FromArgb(20, 40, 30);
                MenuLine = Color.FromArgb(34, 37, 35);
            }
            else
            {
                Bg = Color.FromArgb(245, 246, 248); Panel = Color.White;
                Ink = Color.FromArgb(45, 49, 57); InkDim = Color.FromArgb(110, 115, 125);
                Line = Color.FromArgb(200, 204, 210); GroupInk = Color.FromArgb(50, 70, 130);
                FieldBg = Color.White;
                MenuBg = Color.White; MenuHot = Color.FromArgb(232, 236, 242);
                MenuLine = Color.FromArgb(205, 209, 216);
            }
        }

        public static void Toggle()
        {
            Dark = !Dark;
            LoadTokens();
            try { File.WriteAllText(StorePath, Dark ? "dark" : "light"); } catch { }
        }

        // ------------------------------------------------------------------
        //  Petites aides couleur / dessin
        // ------------------------------------------------------------------
        private static bool Near(Color a, Color b)
        {
            return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 26;
        }

        /// <summary>Couleur de survol : éclaircit les fonds sombres, assombrit les fonds clairs (steps 1-2).</summary>
        private static Color Hover(Color c, int step)
        {
            int d = (c.GetBrightness() < 0.5f ? 1 : -1) * (step == 1 ? 16 : 30);
            return Color.FromArgb(
                Math.Max(0, Math.Min(255, c.R + d)),
                Math.Max(0, Math.Min(255, c.G + d)),
                Math.Max(0, Math.Min(255, c.B + d)));
        }

        private static bool IsGrayish(Color c)
        {
            return Math.Abs(c.R - c.G) < 18 && Math.Abs(c.G - c.B) < 18;
        }

        private static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private static float Dpi(Control c, float v)
        {
            try { return v * c.DeviceDpi / 96f; } catch { return v; }
        }

        /// <summary>Wordmark « DesTin » blanc + « GOOD » dégradé ; renvoie la largeur peinte.</summary>
        internal static float DrawWordmark(Graphics g, Font font, float x, float y)
        {
            var old = g.TextRenderingHint;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float w;
            using (var sf = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                SizeF a = g.MeasureString("DesTin", font, PointF.Empty, sf);
                SizeF b = g.MeasureString("GOOD", font, PointF.Empty, sf);
                using (var br = new SolidBrush(Color.White))
                    g.DrawString("DesTin", font, br, x, y, sf);
                var gr = new RectangleF(x + a.Width, y, b.Width + 2f, b.Height);
                using (var lg = new LinearGradientBrush(gr, Color.FromArgb(0, 225, 140), Color.FromArgb(0, 170, 255), 0f))
                    g.DrawString("GOOD", font, lg, x + a.Width, y, sf);
                w = a.Width + b.Width;
            }
            g.TextRenderingHint = old;
            return w;
        }

        /// <summary>Rectangle arrondi (partagé avec les fenêtres pour puces et cartes).</summary>
        internal static GraphicsPath RoundPath(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ------------------------------------------------------------------
        //  Barre de titre : sombre et de la couleur exacte du bandeau (DWM),
        //  pour que la fenêtre semble d'un seul tenant (Windows 11 ; sur
        //  Windows 10 on obtient la barre sombre générique).
        // ------------------------------------------------------------------
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DwmDarkMode = 20, DwmDarkModeOld = 19;
        private const int DwmCorner = 33, DwmCaptionColor = 35, DwmTextColor = 36;
        private const int CornerRoundSmall = 2;

        private static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        private static void SetCaption(Form f)
        {
            try
            {
                if (!f.IsHandleCreated) return;
                int on = 1;
                if (DwmSetWindowAttribute(f.Handle, DwmDarkMode, ref on, 4) != 0)
                    DwmSetWindowAttribute(f.Handle, DwmDarkModeOld, ref on, 4);
                int cap = ColorRef(HeaderBg);
                DwmSetWindowAttribute(f.Handle, DwmCaptionColor, ref cap, 4);
                int txt = ColorRef(Color.White);
                DwmSetWindowAttribute(f.Handle, DwmTextColor, ref txt, 4);
            }
            catch { }
        }

        private static void OnFormHandleCreated(object sender, EventArgs e)
        {
            SetCaption((Form)sender);
        }

        // ------------------------------------------------------------------
        //  Menus contextuels assortis au thème (fond, survol, coche, flèches)
        //  + coins arrondis des popups sur Windows 11.
        // ------------------------------------------------------------------
        private sealed class MenuTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return MenuBg; } }
            public override Color ImageMarginGradientBegin { get { return MenuBg; } }
            public override Color ImageMarginGradientMiddle { get { return MenuBg; } }
            public override Color ImageMarginGradientEnd { get { return MenuBg; } }
            public override Color MenuBorder { get { return MenuLine; } }
            public override Color MenuItemBorder { get { return MenuHot; } }
            public override Color MenuItemSelected { get { return MenuHot; } }
            public override Color MenuItemSelectedGradientBegin { get { return MenuHot; } }
            public override Color MenuItemSelectedGradientEnd { get { return MenuHot; } }
            public override Color MenuItemPressedGradientBegin { get { return MenuBg; } }
            public override Color MenuItemPressedGradientEnd { get { return MenuBg; } }
            public override Color SeparatorDark { get { return MenuLine; } }
            public override Color SeparatorLight { get { return MenuBg; } }
            public override Color CheckBackground { get { return AccentRef; } }
            public override Color CheckSelectedBackground { get { return AccentRef; } }
            public override Color CheckPressedBackground { get { return AccentRef; } }
            public override Color ToolStripBorder { get { return MenuLine; } }
        }

        private sealed class MenuRenderer : ToolStripProfessionalRenderer
        {
            public MenuRenderer() : base(new MenuTable()) { RoundedEdges = false; }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Ink : InkDim;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = Ink;
                base.OnRenderArrow(e);
            }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                base.OnRenderToolStripBackground(e);
                try
                {
                    if (e.ToolStrip is ToolStripDropDown && e.ToolStrip.IsHandleCreated)
                    {
                        int pref = CornerRoundSmall;
                        DwmSetWindowAttribute(e.ToolStrip.Handle, DwmCorner, ref pref, 4);
                    }
                }
                catch { }
            }
        }

        // ------------------------------------------------------------------
        //  Rôle de chaque contrôle, décidé à la première visite (les couleurs
        //  d'origine posées par la fenêtre) puis mémorisé : les Apply suivants
        //  (bascule de thème) ne confondent plus un panneau recoloré avec un
        //  bandeau, et chaque contrôle n'est câblé qu'une fois.
        // ------------------------------------------------------------------
        private sealed class RoleInfo
        {
            public bool Banner;      // panneau sombre « bandeau / tuile » à préserver
            public bool Underline;   // vrai bandeau de fenêtre → liseré signature
            public bool AccentBtn;   // bouton couleur pleine à laisser tel quel
            public bool Wired;       // peintres/évènements déjà branchés
        }

        private static readonly ConditionalWeakTable<Control, RoleInfo> Roles =
            new ConditionalWeakTable<Control, RoleInfo>();

        private sealed class BtnState { public bool Hover; public bool Down; }
        private static readonly ConditionalWeakTable<Button, BtnState> Buttons =
            new ConditionalWeakTable<Button, BtnState>();

        private static RoleInfo RoleOf(Control c)
        {
            RoleInfo r;
            if (Roles.TryGetValue(c, out r)) return r;
            r = new RoleInfo();
            if (c is Panel && Near(c.BackColor, HeaderBg))
            {
                r.Banner = true;
                // Seuls les bandeaux en tête de fenêtre reçoivent le liseré (pas les tuiles).
                r.Underline = c.Parent is Form && c.Height <= 110
                    && (c.Dock == DockStyle.Top || c.Top == 0);
            }
            var b = c as Button;
            if (b != null)
                r.AccentBtn = Near(b.BackColor, AccentRef)
                    || (b.FlatStyle == FlatStyle.Flat && b.FlatAppearance.BorderSize == 0 && !IsGrayish(b.BackColor));
            Roles.Add(c, r);
            return r;
        }

        // ------------------------------------------------------------------
        //  Application du thème
        // ------------------------------------------------------------------
        public static void Apply(Control root)
        {
            // 1) Classification d'après les couleurs POSÉES PAR LA FENÊTRE, avant tout
            //    recoloriage : sinon les panneaux sans couleur explicite héritent du fond
            //    sombre du formulaire déjà recoloré et se font prendre pour des bandeaux.
            try { Classify(root); } catch { }
            try { Walk(root, false); } catch { }
        }

        private static void Classify(Control c)
        {
            RoleOf(c);
            foreach (Control ch in c.Controls) Classify(ch);
        }

        private static void Walk(Control c, bool inHeader)
        {
            RoleInfo role = RoleOf(c);
            bool header = inHeader || role.Banner;

            if (!role.Wired)
            {
                role.Wired = true;
                var form = c as Form;
                if (form != null)
                {
                    if (form.IsHandleCreated) SetCaption(form);
                    else form.HandleCreated += OnFormHandleCreated;
                }
                var wb = c as Button;
                if (wb != null && wb.FlatStyle == FlatStyle.Flat) WireButton(wb);
                var gb = c as GroupBox;
                if (gb != null) WireGroup(gb);
                var lv = c as ListView;
                if (lv != null) WireList(lv);
                if (role.Underline)
                {
                    c.Paint += OnPaintBannerLine;
                    c.Resize += OnInvalidateSelf;
                }
            }

            if (header)
            {
                if (c is Panel) c.BackColor = HeaderBg;
                if (c is Label)
                    c.ForeColor = (c.Font != null && c.Font.Size >= 12.5f) ? Color.White : Color.FromArgb(170, 175, 185);
            }
            else if (c is Form)
            {
                c.BackColor = Bg;
            }
            else if (c is TextBox)
            {
                var tb = (TextBox)c;
                try { tb.BorderStyle = BorderStyle.FixedSingle; } catch { }
                tb.BackColor = FieldBg; tb.ForeColor = Ink;
            }
            else if (c is RichTextBox)
            {
                c.BackColor = FieldBg; c.ForeColor = Ink;
            }
            else if (c is ListView)
            {
                c.BackColor = Panel; c.ForeColor = Ink;
            }
            else if (c is CheckedListBox)
            {
                c.BackColor = Panel; c.ForeColor = Ink;
            }
            else if (c is ComboBox)
            {
                var cb = (ComboBox)c;
                try { cb.FlatStyle = FlatStyle.Flat; } catch { }
                cb.BackColor = FieldBg; cb.ForeColor = Ink;
            }
            else if (c is NumericUpDown)
            {
                var nud = (NumericUpDown)c;
                try { nud.BorderStyle = BorderStyle.FixedSingle; } catch { }
                nud.BackColor = FieldBg; nud.ForeColor = Ink;
            }
            else if (c is Button)
            {
                var b = (Button)c;
                if (!role.AccentBtn) // laisse les boutons accent (couleur pleine) tels quels
                {
                    b.BackColor = Panel; b.ForeColor = Ink;
                    try { b.FlatAppearance.BorderColor = Line; } catch { }
                }
            }
            else if (c is GroupBox)
            {
                // La carte est peinte par-dessus ; le BackColor sert de fond ambiant aux cases.
                c.BackColor = Panel;
                c.ForeColor = GroupInk;
            }
            else if (c is CheckBox || c is RadioButton)
            {
                if (IsGrayish(c.ForeColor) || Near(c.ForeColor, SystemColors.ControlText)) c.ForeColor = Ink;
            }
            else if (c is Label)
            {
                // On ne touche qu'au texte "par défaut" (gris/noir), pas aux libellés colorés (accent/statut).
                if (IsGrayish(c.ForeColor)) c.ForeColor = c.ForeColor.GetBrightness() < 0.5f ? Ink : InkDim;
            }
            else if (c is Panel || c is FlowLayoutPanel || c is TableLayoutPanel)
            {
                c.BackColor = Bg;
            }

            foreach (Control ch in c.Controls) Walk(ch, header);
        }

        private static void OnInvalidateSelf(object sender, EventArgs e)
        {
            ((Control)sender).Invalidate();
        }

        // ------------------------------------------------------------------
        //  Liseré dégradé signature sous les bandeaux de fenêtres
        // ------------------------------------------------------------------
        private static void OnPaintBannerLine(object sender, PaintEventArgs e)
        {
            var p = (Control)sender;
            Rectangle r = p.ClientRectangle;
            if (r.Width < 8 || r.Height < 10) return;
            int h = Math.Max(2, (int)Dpi(p, 3f));
            var line = new Rectangle(0, r.Height - h, r.Width, h);
            using (var br = new LinearGradientBrush(line, BrandA, BrandB, 0f))
                e.Graphics.FillRectangle(br, line);
        }

        // ------------------------------------------------------------------
        //  Boutons : les boutons plats de l'app sont redessinés en boutons
        //  arrondis avec survol, pression, focus et état désactivé propres.
        //  Les couleurs restent celles posées par la fenêtre (accent conservé).
        // ------------------------------------------------------------------
        private static void WireButton(Button b)
        {
            BtnState st;
            if (Buttons.TryGetValue(b, out st)) return;
            st = new BtnState();
            Buttons.Add(b, st);
            b.MouseEnter += delegate { st.Hover = true; b.Invalidate(); };
            b.MouseLeave += delegate { st.Hover = false; st.Down = false; b.Invalidate(); };
            b.MouseDown += delegate (object s, MouseEventArgs me)
            {
                if (me.Button == MouseButtons.Left) { st.Down = true; b.Invalidate(); }
            };
            b.MouseUp += delegate { st.Down = false; b.Invalidate(); };
            b.GotFocus += delegate { b.Invalidate(); };
            b.LostFocus += delegate { b.Invalidate(); };
            b.Resize += OnInvalidateSelf;
            b.Paint += OnPaintButton;
        }

        private static void OnPaintButton(object sender, PaintEventArgs e)
        {
            var b = (Button)sender;
            BtnState st;
            if (!Buttons.TryGetValue(b, out st)) return;
            Graphics g = e.Graphics;
            Rectangle r = b.ClientRectangle;
            if (r.Width < 4 || r.Height < 4) return;

            Color parentBg = (b.Parent != null) ? b.Parent.BackColor : Bg;
            using (var br = new SolidBrush(parentBg)) g.FillRectangle(br, r);

            Color back = b.BackColor;
            if (!b.Enabled) back = Blend(back, parentBg, 0.55f);
            else if (st.Down) back = Hover(back, 2);
            else if (st.Hover) back = Hover(back, 1);

            float rad = Dpi(b, 8f);
            if (rad > r.Height / 2f - 1f) rad = r.Height / 2f - 1f;
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundPath(rf, rad))
            {
                using (var br = new SolidBrush(back)) g.FillPath(br, path);
                if (b.FlatAppearance.BorderSize > 0)
                {
                    Color bc = b.FlatAppearance.BorderColor;
                    if (bc.IsEmpty || bc.A == 0) bc = Line;
                    if (!b.Enabled) bc = Blend(bc, parentBg, 0.5f);
                    using (var pen = new Pen(bc)) g.DrawPath(pen, path);
                }
                if (b.Focused && b.Enabled)
                {
                    var fr = RectangleF.Inflate(rf, -2f, -2f);
                    using (GraphicsPath fp = RoundPath(fr, Math.Max(2f, rad - 2f)))
                    using (var pen = new Pen(Color.FromArgb(90, b.ForeColor)))
                        g.DrawPath(pen, fp);
                }
            }
            g.SmoothingMode = old;

            Color fore = b.Enabled ? b.ForeColor : Blend(b.ForeColor, parentBg, 0.5f);
            var tr = new Rectangle(
                r.X + b.Padding.Left, r.Y + b.Padding.Top,
                Math.Max(0, r.Width - b.Padding.Horizontal),
                Math.Max(0, r.Height - b.Padding.Vertical));
            TextFormatFlags flags = AlignFlags(b.TextAlign) | TextFormatFlags.EndEllipsis;
            if (b.Text == null || b.Text.IndexOf('\n') < 0) flags |= TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, b.Text, b.Font, tr, fore, flags);
        }

        private static TextFormatFlags AlignFlags(ContentAlignment a)
        {
            switch (a)
            {
                case ContentAlignment.TopLeft: return TextFormatFlags.Top | TextFormatFlags.Left;
                case ContentAlignment.TopCenter: return TextFormatFlags.Top | TextFormatFlags.HorizontalCenter;
                case ContentAlignment.TopRight: return TextFormatFlags.Top | TextFormatFlags.Right;
                case ContentAlignment.MiddleLeft: return TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
                case ContentAlignment.MiddleRight: return TextFormatFlags.VerticalCenter | TextFormatFlags.Right;
                case ContentAlignment.BottomLeft: return TextFormatFlags.Bottom | TextFormatFlags.Left;
                case ContentAlignment.BottomCenter: return TextFormatFlags.Bottom | TextFormatFlags.HorizontalCenter;
                case ContentAlignment.BottomRight: return TextFormatFlags.Bottom | TextFormatFlags.Right;
                default: return TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;
            }
        }

        // ------------------------------------------------------------------
        //  GroupBox : carte arrondie avec pastille accent devant le titre
        //  (remplace la bordure gravée héritée de Windows 95).
        // ------------------------------------------------------------------
        private static void WireGroup(GroupBox gb)
        {
            gb.Paint += OnPaintGroup;
            gb.Resize += OnInvalidateSelf;
        }

        private static void OnPaintGroup(object sender, PaintEventArgs e)
        {
            var gb = (GroupBox)sender;
            Graphics g = e.Graphics;
            Rectangle r = gb.ClientRectangle;
            if (r.Width < 8 || r.Height < 8) return;

            Color parentBg = (gb.Parent != null) ? gb.Parent.BackColor : Bg;
            using (var br = new SolidBrush(parentBg)) g.FillRectangle(br, r);

            var rf = new RectangleF(0.5f, 0.5f, r.Width - 1f, r.Height - 1f);
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundPath(rf, Dpi(gb, 9f)))
            {
                using (var br = new SolidBrush(Panel)) g.FillPath(br, path);
                using (var pen = new Pen(Line)) g.DrawPath(pen, path);
            }
            g.SmoothingMode = old;

            Color bar = Dark ? Color.FromArgb(0, 190, 120) : AccentRef;
            using (var br = new SolidBrush(bar))
                g.FillRectangle(br, 12, 6, 3, 11);
            TextRenderer.DrawText(g, gb.Text, gb.Font,
                new Rectangle(21, 1, Math.Max(0, r.Width - 30), 20), Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        // ------------------------------------------------------------------
        //  ListView : bordure enfoncée retirée + en-têtes de colonnes plats
        //  assortis au thème (les lignes restent rendues par Windows, donc
        //  les couleurs par pilote/état des fenêtres sont conservées).
        // ------------------------------------------------------------------
        private sealed class ListExtra { public int LastColWidth; }
        private static readonly ConditionalWeakTable<ListView, ListExtra> Lists =
            new ConditionalWeakTable<ListView, ListExtra>();

        private static void WireList(ListView lv)
        {
            try { lv.BorderStyle = BorderStyle.None; } catch { }
            lv.OwnerDraw = true;
            lv.DrawColumnHeader += OnDrawListHeader;
            lv.DrawItem += OnDrawListDefault;
            lv.DrawSubItem += OnDrawListSubDefault;
            // La dernière colonne absorbe l'espace restant (sinon la zone d'en-tête
            // à droite garde le fond clair natif). La largeur d'origine reste le minimum.
            var extra = new ListExtra();
            extra.LastColWidth = lv.Columns.Count > 0 ? lv.Columns[lv.Columns.Count - 1].Width : 0;
            Lists.Add(lv, extra);
            lv.Resize += OnListResize;
            StretchLastColumn(lv);
        }

        private static void OnListResize(object sender, EventArgs e)
        {
            StretchLastColumn((ListView)sender);
        }

        private static void StretchLastColumn(ListView lv)
        {
            try
            {
                if (lv.View != View.Details || lv.Columns.Count == 0) return;
                ListExtra extra;
                if (!Lists.TryGetValue(lv, out extra)) return;
                int sum = 0;
                for (int i = 0; i < lv.Columns.Count - 1; i++) sum += lv.Columns[i].Width;
                int want = lv.ClientSize.Width - sum - 4;
                ColumnHeader last = lv.Columns[lv.Columns.Count - 1];
                int target = Math.Max(extra.LastColWidth, want);
                if (last.Width != target) last.Width = target;
            }
            catch { }
        }

        private static void OnDrawListHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            var lv = (ListView)sender;
            Rectangle r = e.Bounds;
            using (var br = new SolidBrush(Dark ? Color.FromArgb(36, 40, 48) : Color.FromArgb(240, 242, 245)))
                e.Graphics.FillRectangle(br, r);
            using (var pen = new Pen(Line))
                e.Graphics.DrawLine(pen, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);

            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            if (e.Header.TextAlign == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
            else if (e.Header.TextAlign == HorizontalAlignment.Center) flags |= TextFormatFlags.HorizontalCenter;
            else flags |= TextFormatFlags.Left;
            var tr = new Rectangle(r.X + 8, r.Y, Math.Max(0, r.Width - 14), r.Height);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, lv.Font, tr, InkDim, flags);
        }

        private static void OnDrawListDefault(object sender, DrawListViewItemEventArgs e)
        {
            e.DrawDefault = true;
        }

        private static void OnDrawListSubDefault(object sender, DrawListViewSubItemEventArgs e)
        {
            e.DrawDefault = true;
        }
    }
}
