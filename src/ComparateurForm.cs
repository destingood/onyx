using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Comparateur de prix d'un bon plan : meilleur prix (converti en euros au taux BCE), plus bas historique et verdict,
    /// prix de chaque store officiel (CheapShark), et liens de recherche chez les revendeurs de clés (aucun prix lu chez eux).
    /// </summary>
    internal sealed class ComparateurForm : Form
    {
        private const int W = 960, H = 770, M = 28, Gauche = 600, XDroite = M + Gauche + 24, LDroite = W - M - XDroite;
        private const int YTuiles = 196, YSection = 290, YLignes = 322, HLigne = 40, MaxLignes = 7, HTuileRev = 66;

        private readonly Offre _o;
        private OffresReseau.ComparatifJeu _c;
        private List<OffresSources.Ligne> _lignes = new List<OffresSources.Ligne>();
        private readonly List<KeyValuePair<Rectangle, string>> _zones = new List<KeyValuePair<Rectangle, string>>();
        private int _survol = -1;

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int sz);

        public ComparateurForm(Offre o)
        {
            _o = o;
            Text = "ONYX — Comparateur : " + (o.Nom ?? "");
            ClientSize = new Size(W, H);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = FpsUi.BgMain;
            Font = FpsUi.Body;
            DoubleBuffered = true;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var fermer = FpsUi.GhostButton("Fermer");
            fermer.SetBounds(W - M - 96, H - 44, 96, 30);
            fermer.Click += (s, e) => Close();
            Controls.Add(fermer);
        }

        /// <summary>Ouvre la fenêtre au-dessus de la page (non modale : on peut comparer plusieurs jeux).</summary>
        public static void Montre(Offre o, IWin32Window parent)
        {
            if (o == null) return;
            var f = new ComparateurForm(o);
            try { f.Show(parent); } catch { f.Show(); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int v = 1; DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }   // barre de titre sombre
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            OffresReseau.ComparatifJeu c;
            try { c = await OffresReseau.Comparatif(_o); }
            catch { c = new OffresReseau.ComparatifJeu(); }
            Charge(c);
        }

        /// <summary>Données reçues (aussi appelé par l'aperçu hors écran).</summary>
        internal void Charge(OffresReseau.ComparatifJeu c)
        {
            if (IsDisposed) return;
            _c = c ?? new OffresReseau.ComparatifJeu();
            _lignes = OffresSources.Lignes(_o, _c.Prix, _c.Taux);
            Invalidate();
        }

        private void Pret()
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)(() => { if (!IsDisposed) Invalidate(); })); } catch { }
        }

        // ------------------------------------------------------------------ souris

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = _zones.FindIndex(z => z.Key.Contains(e.Location));
            Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
            if (i != _survol) { _survol = i; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_survol >= 0) { _survol = -1; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            int i = _zones.FindIndex(z => z.Key.Contains(e.Location));
            if (i >= 0) PageBonsPlans.Ouvre(_zones[i].Value);
        }

        private bool Survole(Rectangle r)
        {
            return _survol >= 0 && _survol < _zones.Count && _zones[_survol].Key == r;
        }

        // ------------------------------------------------------------------ rendu

        private static string Prix(int cts, string devise)
        {
            string p = Offres.Prix(cts, devise);
            return devise == "USD" && cts > 0 ? p + " US" : p;
        }

        private static void Texte(Graphics g, string t, Font f, Color c, Rectangle r, TextFormatFlags plus = TextFormatFlags.Default)
        {
            TextRenderer.DrawText(g, t ?? "", f, r, c, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | plus);
        }

        private static void Pastille(Graphics g, string t, Font f, Color c, Rectangle r, int alpha = 46)
        {
            using (var p = FpsUi.Round(r, r.Height / 2f))
            using (var br = new SolidBrush(Color.FromArgb(alpha, c))) g.FillPath(br, p);
            Texte(g, t, f, c, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private static int Largeur(string t, Font f)
        {
            return TextRenderer.MeasureText(t, f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            _zones.Clear();
            bool charge = _c != null;

            // En-tête : jaquette, nom, store du bon plan.
            var rc = new Rectangle(M, 20, 112, 156);
            Image img = null;
            try
            {
                if (_o.Image != null) img = GameArt.GetUrl(_o.Image, Pret);
                if (img == null && _o.SteamAppId > 0) img = GameArt.Get(_o.SteamAppId, Pret);
            }
            catch { }
            using (var p = FpsUi.Round(rc, 8f))
            {
                if (img != null)
                {
                    Region avant = g.Clip;
                    g.SetClip(p, CombineMode.Intersect);
                    float cible = rc.Width / (float)rc.Height, source = img.Width / (float)img.Height;
                    RectangleF src = source > cible
                        ? new RectangleF((img.Width - img.Height * cible) / 2f, 0, img.Height * cible, img.Height)
                        : new RectangleF(0, (img.Height - img.Width / cible) / 2f, img.Width, img.Width / cible);
                    try { g.DrawImage(img, rc, src, GraphicsUnit.Pixel); } catch { }
                    g.Clip = avant;
                }
                else using (var br = new SolidBrush(FpsUi.Accent(24))) g.FillPath(br, p);
                using (var pen = new Pen(FpsUi.Border)) g.DrawPath(pen, p);
            }
            int tx = rc.Right + 20;
            Texte(g, "COMPARATEUR DE PRIX", FpsUi.Tiny, FpsUi.GoldDim, new Rectangle(tx, 22, 300, 16));
            Texte(g, _o.Nom, FpsUi.H1, FpsUi.Ink, new Rectangle(tx, 40, Gauche - tx + M, 70), TextFormatFlags.WordBreak);
            string store = OffresSources.NomBoutique(_o.Source);
            Color cs = PageBonsPlans.CouleurStore(_o.Source);
            string bonPlan = "Bon plan vu sur " + store + " : " + Prix(_o.PrixCts, _o.Devise) + (_o.Remise > 0 ? "  (−" + _o.Remise.ToString(CultureInfo.InvariantCulture) + " %)" : "");
            Texte(g, bonPlan, FpsUi.Small, FpsUi.Dim, new Rectangle(tx, 118, Gauche - tx + M, 18));
            int ny = 144;
            if (charge)
            {
                int nStores = _lignes.Count(l => l.Devise == "USD");
                Pastille(g, nStores + " store" + (nStores > 1 ? "s" : "") + " comparé" + (nStores > 1 ? "s" : ""), FpsUi.Tiny, FpsUi.Gold, new Rectangle(tx, ny, 130, 22));
                Pastille(g, "6 revendeurs de clés", FpsUi.Tiny, FpsUi.Dim, new Rectangle(tx + 140, ny, 140, 22), 30);
            }
            else Texte(g, "Recherche des prix dans les stores…", FpsUi.Small, FpsUi.Gold, new Rectangle(tx, ny, 300, 22), TextFormatFlags.VerticalCenter);

            // Meilleur prix.
            var rb = new Rectangle(XDroite, 20, LDroite, 156);
            OnyxDesign.PaintCard(g, rb, FpsUi.Card, OnyxDesign.Edge, 12f);
            using (var lb = new LinearGradientBrush(rb, FpsUi.Accent(34), Color.FromArgb(0, FpsUi.Gold), LinearGradientMode.ForwardDiagonal))
            using (var p = FpsUi.Round(rb, 12f)) g.FillPath(lb, p);
            Texte(g, "MEILLEUR PRIX", FpsUi.Tiny, FpsUi.GoldDim, new Rectangle(rb.X + 18, rb.Y + 14, 200, 16));
            OffresSources.Ligne best = _lignes.Count > 0 ? _lignes[0] : null;
            if (best != null)
            {
                string grand = best.EurCts >= 0 ? (best.Devise == "USD" && best.EurCts > 0 ? "≈ " : "") + Offres.Prix(best.EurCts, "EUR") : Prix(best.Cts, best.Devise);
                Texte(g, grand, FpsUi.DisplayL, FpsUi.Gold, new Rectangle(rb.X + 14, rb.Y + 34, rb.Width - 28, 56), TextFormatFlags.VerticalCenter);
                string detail = "chez " + best.Store + (best.Devise == "USD" ? "  ·  " + Prix(best.Cts, "USD") : "");
                Texte(g, detail, FpsUi.H3, FpsUi.Ink, new Rectangle(rb.X + 18, rb.Y + 94, rb.Width - 36, 20));
                if (best.Remise > 0)
                    Pastille(g, "−" + best.Remise.ToString(CultureInfo.InvariantCulture) + " %", FpsUi.Small, best.Remise >= 75 ? FpsUi.Gold : FpsUi.Ok, new Rectangle(rb.X + 18, rb.Y + 122, 64, 22), 60);
                var rv = new Rectangle(rb.Right - 118, rb.Y + 120, 100, 26);
                bool sv = Survole(rv);
                Pastille(g, "Y aller  ↗", FpsUi.Small, FpsUi.Gold, rv, sv ? 90 : 50);
                _zones.Add(new KeyValuePair<Rectangle, string>(rv, best.Url));
            }
            else Texte(g, charge ? "—" : "…", FpsUi.DisplayL, FpsUi.Dim2, new Rectangle(rb.X + 14, rb.Y + 34, rb.Width - 28, 56), TextFormatFlags.VerticalCenter);

            PeintTuiles(g, best);
            PeintStores(g, charge);
            PeintRevendeurs(g);
            PeintPlusLoin(g);

            string taux = _c != null && _c.Taux > 0
                ? "conversion au taux BCE du " + _c.JourTaux.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + " (1 € = " + _c.Taux.ToString("0.0000", CultureInfo.GetCultureInfo("fr-FR")) + " $), indicative : le prix affiché en France peut différer"
                : "conversion en euros indisponible";
            Texte(g, "Prix US et historique : CheapShark ; " + taux + ".", FpsUi.Tiny, FpsUi.Dim2, new Rectangle(M, H - 40, W - 2 * M - 110, 28), TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter);
        }

        private void PeintTuiles(Graphics g, OffresSources.Ligne best)
        {
            int lt = (W - 2 * M - 2 * 14) / 3;
            var t1 = new Rectangle(M, YTuiles, lt, 74);
            var t2 = new Rectangle(t1.Right + 14, YTuiles, lt, 74);
            var t3 = new Rectangle(t2.Right + 14, YTuiles, W - M - t2.Right - 14, 74);
            foreach (var t in new[] { t1, t2, t3 }) OnyxDesign.PaintCard(g, t, FpsUi.Card, FpsUi.Border, 10f);

            Texte(g, "PLUS BAS HISTORIQUE", FpsUi.Tiny, FpsUi.Dim2, new Rectangle(t1.X + 16, t1.Y + 10, t1.Width - 32, 16));
            OffresSources.HistoriquePrix h = _c == null ? null : _c.Histo;
            if (h != null && h.PlusBasCts >= 0)
            {
                int eur = OffresSources.EnEuros(h.PlusBasCts, _c.Taux);
                Texte(g, Prix(h.PlusBasCts, "USD") + (eur > 0 ? "  ≈ " + Offres.Prix(eur, "EUR") : ""), FpsUi.H2, FpsUi.Ink, new Rectangle(t1.X + 16, t1.Y + 28, t1.Width - 32, 22));
                if (h.PlusBasLe.HasValue)
                    Texte(g, "le " + h.PlusBasLe.Value.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), FpsUi.Small, FpsUi.Dim, new Rectangle(t1.X + 16, t1.Y + 50, t1.Width - 32, 18));
            }
            else Texte(g, _c == null ? "…" : "inconnu", FpsUi.H2, FpsUi.Dim2, new Rectangle(t1.X + 16, t1.Y + 28, t1.Width - 32, 22));

            Texte(g, "PRIX DE BASE", FpsUi.Tiny, FpsUi.Dim2, new Rectangle(t2.X + 16, t2.Y + 10, t2.Width - 32, 16));
            OffresSources.Ligne base0 = _lignes.Where(l => l.OrigineCts > 0).OrderByDescending(l => l.OrigineCts).FirstOrDefault();
            if (base0 != null && best != null && best.EurCts >= 0)
            {
                int baseEur = base0.Devise == "USD" ? OffresSources.EnEuros(base0.OrigineCts, _c.Taux) : base0.OrigineCts;
                Texte(g, Prix(base0.OrigineCts, base0.Devise), FpsUi.H2, FpsUi.Ink, new Rectangle(t2.X + 16, t2.Y + 28, t2.Width - 32, 22));
                if (baseEur > best.EurCts)
                    Texte(g, "tu économises ≈ " + Offres.Prix(baseEur - best.EurCts, "EUR"), FpsUi.Small, FpsUi.Ok, new Rectangle(t2.X + 16, t2.Y + 50, t2.Width - 32, 18));
            }
            else Texte(g, _c == null ? "…" : "—", FpsUi.H2, FpsUi.Dim2, new Rectangle(t2.X + 16, t2.Y + 28, t2.Width - 32, 22));

            Texte(g, "VERDICT", FpsUi.Tiny, FpsUi.Dim2, new Rectangle(t3.X + 16, t3.Y + 10, t3.Width - 32, 16));
            int niveau = 0;
            string verdict = null;
            OffresSources.Ligne usd = _lignes.FirstOrDefault(l => l.Devise == "USD");
            if (usd != null && h != null) verdict = OffresSources.Verdict(usd.Cts, h.PlusBasCts, out niveau);
            Color cv = niveau == 2 ? FpsUi.Ok : niveau == 1 ? FpsUi.Gold : FpsUi.Warn;
            if (verdict != null)
            {
                using (var br = new SolidBrush(cv)) g.FillEllipse(br, t3.X + 16, t3.Y + 34, 10, 10);
                Texte(g, verdict, FpsUi.H3, cv, new Rectangle(t3.X + 34, t3.Y + 28, t3.Width - 50, 40), TextFormatFlags.WordBreak);
            }
            else Texte(g, _c == null ? "…" : "pas d'historique pour ce jeu", FpsUi.Body, FpsUi.Dim2, new Rectangle(t3.X + 16, t3.Y + 28, t3.Width - 32, 22));
        }

        private void PeintStores(Graphics g, bool charge)
        {
            Texte(g, "Stores officiels", FpsUi.H2, FpsUi.Ink, new Rectangle(M, YSection, 220, 24));
            Texte(g, "du moins cher au plus cher, en euros", FpsUi.Tiny, FpsUi.Dim2, new Rectangle(M + 150, YSection + 5, Gauche - 150, 18));
            if (!charge || _lignes.Count == 0)
            {
                var vide = new Rectangle(M, YLignes, Gauche, 120);
                OnyxDesign.PaintCard(g, vide, FpsUi.Card, FpsUi.Border, 10f);
                Texte(g, charge ? "Aucun store officiel ne vend ce jeu d'après CheapShark. Essaie les revendeurs à droite." : "Chargement des prix…",
                    FpsUi.Body, FpsUi.Dim, vide, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            int max = Math.Max(1, _lignes.Max(l => l.EurCts >= 0 ? l.EurCts : l.Cts));
            int n = Math.Min(MaxLignes, _lignes.Count);
            for (int i = 0; i < n; i++)
            {
                OffresSources.Ligne l = _lignes[i];
                var r = new Rectangle(M, YLignes + i * HLigne, Gauche, HLigne - 6);
                bool sv = Survole(r);
                OnyxDesign.PaintCard(g, r, sv ? FpsUi.CardHi : FpsUi.Card, i == 0 ? FpsUi.GoldDim : FpsUi.Border, 8f);
                _zones.Add(new KeyValuePair<Rectangle, string>(r, l.Url));
                int cy = r.Y + r.Height / 2;

                Texte(g, (i + 1).ToString(CultureInfo.InvariantCulture), FpsUi.H3, i == 0 ? FpsUi.Gold : FpsUi.Dim2, new Rectangle(r.X + 8, r.Y, 22, r.Height), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                Color c = PageBonsPlans.CouleurNom(l.Store);
                int pw = Math.Min(150, Largeur(l.Store, FpsUi.Tiny) + 18);
                Pastille(g, l.Store, FpsUi.Tiny, c, new Rectangle(r.X + 34, cy - 10, pw, 20));
                if (l.DeLaCarte) Texte(g, "ce bon plan", FpsUi.Tiny, FpsUi.GoldDim, new Rectangle(r.X + 34 + pw + 6, cy - 9, 70, 18), TextFormatFlags.VerticalCenter);

                // Barre : prix relatif au plus cher de la liste.
                var rbar = new Rectangle(r.X + 262, cy - 3, 110, 6);
                using (var p = FpsUi.Round(rbar, 3f)) using (var br = new SolidBrush(FpsUi.Border)) g.FillPath(br, p);
                int v = l.EurCts >= 0 ? l.EurCts : l.Cts;
                int bw = Math.Max(6, rbar.Width * v / max);
                using (var p = FpsUi.Round(new Rectangle(rbar.X, rbar.Y, bw, rbar.Height), 3f))
                using (var br = new SolidBrush(i == 0 ? FpsUi.Gold : Color.FromArgb(150, FpsUi.Dim2))) g.FillPath(br, p);

                Texte(g, Prix(l.Cts, l.Devise), FpsUi.H3, i == 0 ? FpsUi.Gold : FpsUi.Ink, new Rectangle(r.X + 380, r.Y, 96, r.Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                if (l.Devise == "USD" && l.EurCts > 0)
                    Texte(g, "≈ " + Offres.Prix(l.EurCts, "EUR"), FpsUi.Small, FpsUi.Dim, new Rectangle(r.X + 478, r.Y, 72, r.Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                if (l.Remise > 0)
                    Pastille(g, "−" + l.Remise.ToString(CultureInfo.InvariantCulture) + "%", FpsUi.Tiny, l.Remise >= 75 ? FpsUi.Gold : FpsUi.Ok, new Rectangle(r.X + 554, cy - 9, 40, 18), 50);
                if (sv) Texte(g, "↗", FpsUi.H3, FpsUi.Gold, new Rectangle(r.Right - 4, r.Y, 20, r.Height), TextFormatFlags.VerticalCenter);
            }
            if (_lignes.Count > n)
                Texte(g, "+ " + (_lignes.Count - n).ToString(CultureInfo.InvariantCulture) + " autres stores, plus chers", FpsUi.Tiny, FpsUi.Dim2, new Rectangle(M, YLignes + n * HLigne, Gauche, 18));
        }

        /// <summary>Historique complet sur des sites spécialisés (liens seulement).</summary>
        private void PeintPlusLoin(Graphics g)
        {
            var r = new Rectangle(M, H - 56 - 76, Gauche, 76);
            OnyxDesign.PaintCard(g, r, FpsUi.Card, FpsUi.Border, 10f);
            Texte(g, "Historique complet des prix", FpsUi.H3, FpsUi.Ink, new Rectangle(r.X + 16, r.Y + 12, 300, 20));
            Texte(g, "Courbe des prix et alertes, en euros.", FpsUi.Small, FpsUi.Dim, new Rectangle(r.X + 16, r.Y + 40, 330, 20));
            int x = r.Right - 16;
            var liens = new List<KeyValuePair<string, string>>();
            string steamDb = OffresSources.UrlSteamDb(_o.SteamAppId), itad = OffresSources.UrlRevendeur(OffresSources.Itad, _o.Nom);
            if (steamDb != null) liens.Add(new KeyValuePair<string, string>("SteamDB  ↗", steamDb));
            if (itad != null) liens.Add(new KeyValuePair<string, string>("IsThereAnyDeal  ↗", itad));
            foreach (var l in liens)
            {
                int lw = Largeur(l.Key, FpsUi.Small) + 28;
                var rl = new Rectangle(x - lw, r.Y + 24, lw, 28);
                Pastille(g, l.Key, FpsUi.Small, FpsUi.Gold, rl, Survole(rl) ? 90 : 40);
                _zones.Add(new KeyValuePair<Rectangle, string>(rl, l.Value));
                x = rl.X - 10;
            }
        }

        private void PeintRevendeurs(Graphics g)
        {
            Texte(g, "Revendeurs de clés", FpsUi.H2, FpsUi.Ink, new Rectangle(XDroite, YSection, LDroite, 24));
            Texte(g, "ONYX ouvre la recherche du jeu sur le site", FpsUi.Tiny, FpsUi.Dim2, new Rectangle(XDroite, YSection + 24, LDroite, 16));
            int y = YLignes + 14;
            foreach (OffresSources.Revendeur rv in OffresSources.Revendeurs)
            {
                string url = OffresSources.UrlRevendeur(rv, _o.Nom);
                if (url == null) continue;
                var r = new Rectangle(XDroite, y, LDroite, HTuileRev - 8);
                bool sv = Survole(r);
                OnyxDesign.PaintCard(g, r, sv ? FpsUi.CardHi : FpsUi.Card, sv ? FpsUi.GoldDim : FpsUi.Border, 10f);
                _zones.Add(new KeyValuePair<Rectangle, string>(r, url));
                Texte(g, rv.Nom, FpsUi.H3, FpsUi.Ink, new Rectangle(r.X + 14, r.Y + 8, 150, 20));
                bool comparateur = rv.Nom == "AllKeyShop";
                string type = comparateur ? "Comparateur" : rv.PlaceDeMarche ? "Place de marché" : "Revendeur";
                Color ct = comparateur ? FpsUi.Violet : rv.PlaceDeMarche ? FpsUi.Warn : FpsUi.Ok;
                int tw = Largeur(type, FpsUi.Tiny) + 16;
                Pastille(g, type, FpsUi.Tiny, ct, new Rectangle(r.Right - tw - 12, r.Y + 9, tw, 18));
                string note = comparateur ? "Compare les keyshops."
                    : rv.PlaceDeMarche ? "Vendeurs tiers : vois leur note."
                    : "Ses propres clés, en euros.";
                Texte(g, note, FpsUi.Small, FpsUi.Dim, new Rectangle(r.X + 14, r.Y + 32, r.Width - 90, 18));
                Texte(g, "Chercher ↗", FpsUi.Small, sv ? FpsUi.Gold : FpsUi.GoldDim, new Rectangle(r.Right - 84, r.Y + 32, 72, 18), TextFormatFlags.Right);
                y += HTuileRev;
            }
        }
    }
}
