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
    /// <summary>Comparaison lisible des offres officielles, conversions explicites et recherches de clés séparées.</summary>
    internal sealed class ComparateurForm : Form
    {
        private readonly Offre _o;
        private OffresReseau.ComparatifJeu _c;
        private List<OffresSources.Ligne> _lignes = new List<OffresSources.Ligne>();
        private readonly OnyxScrollPanel _list = new OnyxScrollPanel();
        private readonly Button _stores, _resellers, _bestLink, _original, _close, _itad, _steamDb, _refresh;
        private readonly CheckBox _euros;
        private readonly Label _listHint, _empty, _source;
        private bool _keys, _loading;
        private bool _preview = false;
        private int _leftWidth, _rightX, _rightWidth;
        private const int MarginSize = 24;
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int sz);

        public ComparateurForm(Offre o)
        {
            _o = o ?? new Offre();
            Text = "ONYX — Comparateur : " + (_o.Nom ?? "");
            ClientSize = new Size(1100, 780); MinimumSize = new Size(940, 700);
            StartPosition = FormStartPosition.CenterParent; BackColor = FpsUi.BgMain;
            Font = FpsUi.Body; DoubleBuffered = true;
            try { Icon = Logo.MakeIcon(32, FpsUi.Gold); } catch { }
            _original = MakeButton("Offre repérée  ↗", () => Open(_o.Url), false);
            _original.Enabled = Offres.LienSur(_o.Url);
            _bestLink = MakeButton("Voir la meilleure offre  ↗", () => { var best = Best(); if (best != null) Open(best.Url); }, true);
            _stores = MakeButton("Boutiques", () => SetTab(false), false);
            _resellers = MakeButton("Revendeurs de clés", () => SetTab(true), false);
            _euros = new CheckBox { Text = "En euros uniquement", ForeColor = FpsUi.Dim, Font = FpsUi.Small, AutoSize = false, AccessibleName = "Afficher uniquement les prix facturés en euros" };
            _euros.CheckedChanged += (s, e) => Rebuild(); Controls.Add(_euros);
            _refresh = MakeButton("Actualiser", async () => await LoadAsync(), false);
            _close = MakeButton("Fermer", Close, false); _close.DialogResult = DialogResult.Cancel; CancelButton = _close;
            _itad = MakeButton("Historique · IsThereAnyDeal  ↗", () => Open(OffresSources.UrlRevendeur(OffresSources.Itad, _o.Nom)), false);
            _steamDb = MakeButton("Historique · SteamDB  ↗", () => Open(OffresSources.UrlSteamDb(_o.SteamAppId)), false);
            _steamDb.Enabled = _o.SteamAppId > 0; _itad.Enabled = OffresSources.UrlRevendeur(OffresSources.Itad, _o.Nom) != null;
            _listHint = Label(FpsUi.Small); _source = Label(FpsUi.Tiny); _empty = Label(FpsUi.Body);
            Controls.Add(_list); _list.Content.Controls.Add(_empty);
            Resize += (s, e) => { LayoutContent(); Invalidate(); };
            Rebuild();
        }
        private Button MakeButton(string text, Action action, bool primary)
        {
            var button = primary ? OnyxDesign.PrimaryButton(text) : OnyxDesign.Button(text);
            button.Click += (s, e) => action(); Controls.Add(button); return button;
        }
        private Label Label(Font font)
        {
            var label = new Label { Font = font, ForeColor = FpsUi.Dim, AutoSize = false, BackColor = Color.Transparent };
            Controls.Add(label); return label;
        }
        private static void Open(string url) { if (Offres.LienSur(url)) PageBonsPlans.Ouvre(url); }
        public static void Montre(Offre o, IWin32Window parent)
        {
            if (o == null) return;
            var form = new ComparateurForm(o); try { form.Show(parent); } catch { form.Show(); }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int dark = 1; DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { }
        }
        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e); if (!_preview) await LoadAsync();
        }
        private async System.Threading.Tasks.Task LoadAsync()
        {
            if (_loading || IsDisposed || _preview) return;
            _loading = true; _refresh.Enabled = false; Rebuild();
            OffresReseau.ComparatifJeu data;
            try { data = await OffresReseau.Comparatif(_o); }
            catch { data = new OffresReseau.ComparatifJeu(); }
            if (IsDisposed) return;
            _loading = false; _refresh.Enabled = true; Charge(data);
        }
        internal void Charge(OffresReseau.ComparatifJeu data)
        {
            if (IsDisposed) return;
            _c = data ?? new OffresReseau.ComparatifJeu();
            _lignes = OffresSources.Lignes(_o, _c.Prix, _c.Taux); Rebuild();
        }
        private IEnumerable<OffresSources.Ligne> VisibleOffers() { return !_keys && _euros.Checked ? _lignes.Where(l => l.Devise == "EUR") : _lignes; }
        private OffresSources.Ligne Best() { return VisibleOffers().FirstOrDefault(); }
        private void SetTab(bool keys) { _keys = keys; Rebuild(); }
        private void Rebuild()
        {
            if (_source == null) return;
            _list.Content.SuspendLayout();
            foreach (Control control in _list.Content.Controls.Cast<Control>().ToArray())
                if (control != _empty) { _list.Content.Controls.Remove(control); control.Dispose(); }
            if (_keys)
            {
                foreach (var reseller in OffresSources.Revendeurs)
                {
                    string url = OffresSources.UrlRevendeur(reseller, _o.Nom);
                    if (url != null) _list.Content.Controls.Add(new PriceRow(reseller, url));
                }
            }
            else
            {
                int rank = 0;
                foreach (var line in VisibleOffers()) _list.Content.Controls.Add(new PriceRow(line, ++rank));
            }
            _list.Content.ResumeLayout();
            _stores.Text = "Boutiques · " + _lignes.Count;
            _stores.ForeColor = _keys ? FpsUi.Dim : FpsUi.Gold;
            _resellers.ForeColor = _keys ? FpsUi.Gold : FpsUi.Dim;
            _euros.Visible = !_keys;
            _listHint.Text = _keys ? "Recherches sur les sites · aucun prix relevé dans cet onglet"
                : _loading ? "Actualisation des prix…" : _euros.Checked ? "Prix facturés en euros · du moins cher au plus cher"
                : _c != null && _c.Taux <= 0 ? "Taux indisponible · euros puis dollars, sans comparaison entre devises"
                : "Du moins cher au plus cher · ≈ indique une conversion en euros";
            _empty.Visible = _list.Content.Controls.Count == 1;
            _empty.Text = _loading || _c == null ? "Recherche des offres en cours…" : _euros.Checked ? "Aucune offre facturée en euros. Décoche le filtre pour voir les autres boutiques." : "Aucun prix disponible. Actualise ou consulte les revendeurs de clés.";
            var best = Best(); _bestLink.Enabled = best != null && Offres.LienSur(best.Url);
            _source.Text = "Prix US et historique : CheapShark. " + (_c != null && _c.Taux > 0
                ? "Conversion BCE du " + _c.JourTaux.ToString("dd/MM/yyyy") + " : 1 € = " + _c.Taux.ToString("0.0000", CultureInfo.GetCultureInfo("fr-FR")) + " $ US. Prix local à vérifier sur la boutique."
                : "Conversion en euros indisponible. Le prix local est indiqué par la boutique.");
            _list.ScrollTo(0); LayoutContent(); Invalidate();
        }
        private void LayoutContent()
        {
            if (_source == null) return;
            _rightWidth = ClientSize.Width >= 1060 ? 300 : 270;
            _leftWidth = ClientSize.Width - MarginSize * 2 - 20 - _rightWidth;
            _rightX = MarginSize + _leftWidth + 20;
            _original.SetBounds(222, 119, Math.Min(220, _leftWidth - 198), 32);
            _bestLink.SetBounds(_rightX + 18, 211, _rightWidth - 36, 36);
            _stores.SetBounds(24, 266, 150, 36); _resellers.SetBounds(182, 266, 174, 36);
            _euros.SetBounds(370, 270, Math.Max(150, _leftWidth - 346), 28);
            _listHint.SetBounds(24, 314, _leftWidth, 28);
            _list.SetBounds(24, 350, _leftWidth + 16, Math.Max(140, ClientSize.Height - 424));
            int y = 0;
            foreach (Control control in _list.Content.Controls)
            {
                if (control == _empty) continue;
                control.SetBounds(0, y, _list.ContentWidth, 68); y += 78;
            }
            _empty.SetBounds(20, 25, _list.ContentWidth - 40, 90);
            _list.ContentHeight = Math.Max(130, y);
            _itad.SetBounds(_rightX + 16, 465, _rightWidth - 32, 36);
            _steamDb.SetBounds(_rightX + 16, 511, _rightWidth - 32, 36);
            _refresh.SetBounds(_rightX, ClientSize.Height - 56, 128, 34);
            _close.SetBounds(ClientSize.Width - 134, ClientSize.Height - 56, 110, 34);
            _source.SetBounds(24, ClientSize.Height - 56, _leftWidth, 42);
        }
        private void Ready()
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)(() => { if (!IsDisposed) Invalidate(); })); } catch { }
        }
        private static string NativePrice(int cts, string currency) { return Offres.Prix(cts, currency) + (currency == "USD" && cts > 0 ? " US" : ""); }
        private static string MainPrice(OffresSources.Ligne line)
        {
            return line.EurCts >= 0 ? (line.Devise == "USD" && line.EurCts > 0 ? "≈ " : "") + Offres.Prix(line.EurCts, "EUR") : NativePrice(line.Cts, line.Devise);
        }
        private static void TextAt(Graphics g, string text, Font font, Color color, int x, int y, int width, int height, bool wrap = false)
        { OnyxDesign.Text(g, text, font, color, new Rectangle(x, y, width, height), wrap); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var g = e.Graphics; var best = Best();
            g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var art = new Rectangle(24, 26, 176, 100);
            OnyxDesign.PaintCard(g, art, FpsUi.Card, FpsUi.Border, 10);
            Image image = null; try { image = GameArt.ForDeal(_o, Ready); } catch { }
            if (image != null)
            {
                var state = g.Save();
                using (var path = FpsUi.Round(art, 10)) { g.SetClip(path); g.DrawImage(image, DealArtwork.Fit(image.Size, art)); }
                g.Restore(state);
            }
            else Logo.Draw(g, new RectangleF(82, 46, 60, 60), FpsUi.Gold, false);
            TextAt(g, "COMPARATEUR ONYX", FpsUi.Tiny, FpsUi.Gold, 222, 24, _leftWidth - 198, 20);
            TextAt(g, _o.Nom ?? "Jeu", OnyxDesign.Title, FpsUi.Ink, 222, 50, _leftWidth - 198, 62, true);
            TextAt(g, OffresSources.NomBoutique(_o.Source) + " · " + NativePrice(_o.PrixCts, _o.Devise), FpsUi.Small, FpsUi.Dim, 24, 138, 184, 20);
            int metricW = (_leftWidth - 12) / 2;
            PaintMetric(g, new Rectangle(24, 170, metricW, 78), true, best);
            PaintMetric(g, new Rectangle(36 + metricW, 170, _leftWidth - metricW - 12, 78), false, best);
            PaintBest(g, best); PaintInsight(g);
            var history = new Rectangle(_rightX, 408, _rightWidth, 157);
            OnyxDesign.PaintCard(g, history, FpsUi.Card, FpsUi.Border, 12);
            TextAt(g, "Suivre l’évolution des prix", FpsUi.H3, FpsUi.Ink, _rightX + 16, 424, _rightWidth - 32, 24);
            if (ClientSize.Height >= 700) TextAt(g, "Vérifie la région et l’édition du jeu sur la boutique avant de choisir ton offre.", FpsUi.Small, FpsUi.Dim, _rightX + 4, 583, _rightWidth - 8, 44, true);
        }
        private void PaintBest(Graphics g, OffresSources.Ligne best)
        {
            var rect = new Rectangle(_rightX, 24, _rightWidth, 240);
            OnyxDesign.PaintCard(g, rect, FpsUi.BlendGold(FpsUi.Card, .06f), OnyxDesign.Edge, 14);
            bool comparable = _c != null && (_c.Taux > 0 || !_lignes.Any(l => l.Devise == "USD") || !_lignes.Any(l => l.Devise == "EUR") || (!_keys && _euros.Checked));
            TextAt(g, comparable ? "MEILLEUR PRIX RELEVÉ" : "OFFRE À CONSULTER", FpsUi.Tiny, FpsUi.Gold, rect.X + 18, 43, rect.Width - 36, 20);
            TextAt(g, best == null ? (_c == null ? "…" : "Indisponible") : MainPrice(best), best == null ? OnyxDesign.Title : OnyxDesign.Display, FpsUi.Gold, rect.X + 16, 74, rect.Width - 32, 54);
            TextAt(g, best == null ? (_c == null ? "Recherche en cours" : "Aucun prix pour cette sélection") : best.Store, FpsUi.H3, FpsUi.Ink, rect.X + 18, 136, rect.Width - 36, 24);
            string detail = best == null ? "Actualise pour relancer la recherche." : best.Devise == "USD" ? "Facturé " + NativePrice(best.Cts, best.Devise) : "Prix facturé en euros";
            TextAt(g, detail, FpsUi.Small, FpsUi.Dim, rect.X + 18, 169, rect.Width - 36, 32, true);
            _bestLink.Text = comparable ? "Voir la meilleure offre  ↗" : "Voir l’offre  ↗";
        }
        private void PaintMetric(Graphics g, Rectangle rect, bool history, OffresSources.Ligne best)
        {
            OnyxDesign.PaintCard(g, rect, FpsUi.Card, FpsUi.Border, 10);
            TextAt(g, history ? "PLUS BAS HISTORIQUE US" : "PRIX HORS REMISE", FpsUi.Tiny, FpsUi.Dim, rect.X + 14, rect.Y + 10, rect.Width - 28, 18);
            string price = "—", note = _c == null ? "Recherche en cours" : "Non disponible";
            if (history && _c != null && _c.Histo != null && _c.Histo.PlusBasCts >= 0)
            {
                int usd = _c.Histo.PlusBasCts, eur = OffresSources.EnEuros(usd, _c.Taux);
                price = eur >= 0 ? (eur > 0 ? "≈ " : "") + Offres.Prix(eur, "EUR") : NativePrice(usd, "USD");
                note = NativePrice(usd, "USD") + (_c.Histo.PlusBasLe.HasValue ? " · " + _c.Histo.PlusBasLe.Value.ToLocalTime().ToString("dd/MM/yyyy") : "");
            }
            else if (!history && best != null && best.OrigineCts > 0)
            { price = NativePrice(best.OrigineCts, best.Devise); note = best.Store + (best.Remise > 0 ? " · −" + best.Remise + " %" : ""); }
            TextAt(g, price, FpsUi.H2, FpsUi.Ink, rect.X + 14, rect.Y + 30, rect.Width - 28, 24);
            TextAt(g, note, FpsUi.Tiny, FpsUi.Dim, rect.X + 14, rect.Y + 56, rect.Width - 28, 18);
        }
        private void PaintInsight(Graphics g)
        {
            var rect = new Rectangle(_rightX, 280, _rightWidth, 112);
            OnyxDesign.PaintCard(g, rect, FpsUi.Card, FpsUi.Border, 12);
            TextAt(g, "REPÈRE HISTORIQUE · PRIX US", FpsUi.Tiny, FpsUi.Dim, rect.X + 16, rect.Y + 14, rect.Width - 32, 18);
            var usd = _lignes.Where(l => l.Devise == "USD").OrderBy(l => l.Cts).FirstOrDefault();
            int level = 0; string verdict = null;
            if (usd != null && _c != null && _c.Histo != null) verdict = OffresSources.Verdict(usd.Cts, _c.Histo.PlusBasCts, out level);
            if (verdict != null && level == 0) verdict = "Un prix plus bas a déjà été relevé.";
            TextAt(g, verdict ?? "Pas assez d’historique pour situer cette offre.", FpsUi.Body, verdict == null ? FpsUi.Dim : level == 2 ? FpsUi.Ok : FpsUi.Gold, rect.X + 16, rect.Y + 42, rect.Width - 32, 58, true);
        }
        private sealed class PriceRow : BufferedPanel
        {
            private readonly OffresSources.Ligne _line;
            private readonly OffresSources.Revendeur _reseller;
            private readonly Button _open;
            private readonly int _rank;
            internal PriceRow(OffresSources.Ligne line, int rank) : this(line.Url, "Voir l’offre  ↗") { _line = line; _rank = rank; _open.AccessibleName = "Voir l’offre chez " + line.Store; }
            internal PriceRow(OffresSources.Revendeur reseller, string url) : this(url, "Chercher  ↗") { _reseller = reseller; _open.AccessibleName = "Chercher chez " + reseller.Nom; }
            private PriceRow(string url, string text)
            {
                BackColor = FpsUi.BgMain;
                _open = OnyxDesign.Button(text); _open.Enabled = Offres.LienSur(url); _open.Click += (s, e) => Open(url); Controls.Add(_open);
                Resize += (s, e) => _open.SetBounds(Width - 114, 17, 100, 34);
                OnyxDesign.HoverCard(this);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e); var g = e.Graphics;
                OnyxDesign.PaintCard(g, ClientRectangle, FpsUi.Card, ContainsFocus ? FpsUi.Gold : _rank == 1 ? OnyxDesign.Edge : FpsUi.Border, 10);
                if (_line != null)
                {
                    TextAt(g, _rank.ToString("00"), FpsUi.Small, _rank == 1 ? FpsUi.Gold : FpsUi.Dim, 12, 25, 26, 22);
                    int priceX = Width - 282;
                    TextAt(g, _line.Store, FpsUi.H3, FpsUi.Ink, 44, 12, priceX - 54, 24);
                    TextAt(g, _line.DeLaCarte ? "Offre repérée" : _line.Remise > 0 ? "Remise de " + _line.Remise + " %" : "Boutique officielle", FpsUi.Tiny, _line.DeLaCarte ? FpsUi.Gold : FpsUi.Dim, 44, 40, priceX - 54, 18);
                    TextAt(g, MainPrice(_line), FpsUi.H2, _rank == 1 ? FpsUi.Gold : FpsUi.Ink, priceX, 12, 158, 24);
                    TextAt(g, _line.Devise == "USD" ? (_line.EurCts >= 0 ? NativePrice(_line.Cts, "USD") : "Prix en dollars US") : "Facturé en euros", FpsUi.Tiny, FpsUi.Dim, priceX, 40, 158, 18);
                }
                else
                {
                    TextAt(g, _reseller.Nom, FpsUi.H3, FpsUi.Ink, 16, 12, Width - 144, 24);
                    TextAt(g, _reseller.Nom == "AllKeyShop" ? "Comparateur de revendeurs" : _reseller.PlaceDeMarche ? "Place de marché · vendeurs tiers" : "Revendeur de clés", FpsUi.Small, FpsUi.Dim, 16, 40, Width - 144, 20);
                }
            }
        }
#if BTTEST
        internal void SeedPreview(OffresReseau.ComparatifJeu data) { _preview = true; _loading = false; Charge(data); }
        internal void PreviewLoading() { _preview = true; _c = null; _lignes.Clear(); _loading = true; Rebuild(); }
        internal void PreviewKeys(bool keys) { SetTab(keys); }
        internal void PreviewEuros(bool only) { _euros.Checked = only; }
        internal void PreviewBottom() { _list.ScrollTo(int.MaxValue); }
        internal void ValidateUi()
        {
            if (!ClientRectangle.Contains(_close.Bounds) || _list.Bottom > _source.Top || _steamDb.Bottom > _source.Top) throw new InvalidOperationException("Comparateur : zones hors fenêtre.");
            int count = _list.Content.Controls.OfType<PriceRow>().Count();
            if (!_keys && count != VisibleOffers().Count()) throw new InvalidOperationException("Offres masquées par la liste.");
            foreach (var row in _list.Content.Controls.OfType<PriceRow>())
            {
                if (row.Right > _list.ContentWidth) throw new InvalidOperationException("Ligne trop large.");
                foreach (Control control in row.Controls) if (!row.ClientRectangle.Contains(control.Bounds) || !control.TabStop) throw new InvalidOperationException("Action inaccessible.");
            }
            if (!_keys && _euros.Checked && VisibleOffers().Any(l => l.Devise != "EUR")) throw new InvalidOperationException("Le filtre euros contient une conversion.");
            var best = Best();
            if (best != null && best.Devise == "USD" && best.EurCts > 0 && !MainPrice(best).StartsWith("≈ ")) throw new InvalidOperationException("Conversion non signalée.");
            if (_c != null && _c.Taux <= 0 && _lignes.Where(l => l.Devise == "USD").Any(l => l.EurCts >= 0)) throw new InvalidOperationException("Conversion sans taux.");
            _list.ValidateScrolling();
        }
#endif
    }
}
