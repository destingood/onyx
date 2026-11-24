using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Liste des optimisations en UN seul contrôle peint : seules les cartes visibles sont
    /// dessinées, aucune n'a de handle Windows. Remplace ~210 cartes × 6 contrôles (2,1 s à l'ouverture).
    /// Rendu repris des anciennes cartes : puits d'icône, titre, description repliée à 52 px,
    /// « Détails ↓ » si elle dépasse, ligne de statut, interrupteur en haut à droite.
    /// Clavier : flèches/Début/Fin = carte, Espace = bascule, Entrée = déplier. Lecteur d'écran :
    /// chaque carte est un enfant accessible (case à cocher).</summary>
    internal sealed class TweakListe : Control
    {
        private const int HautPad = 4, BasPad = 20, PasMolette = 60, DescCompacte = 52;
        private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

        private readonly VScrollBar _vs = new VScrollBar();
        private List<Tweak> _items = new List<Tweak>();
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>();
        private readonly Dictionary<string, bool> _etats = new Dictionary<string, bool>();
        private readonly HashSet<string> _deplies = new HashSet<string>();
        private readonly HashSet<string> _occupes = new HashSet<string>();
        private readonly Dictionary<string, int[]> _mesures = new Dictionary<string, int[]>();   // id → { hauteur du titre, hauteur pleine de la description }
        private int _largeurMesure = -1;
        private GrilleCartes.Case[] _cases = new GrilleCartes.Case[0];
        private int _total;
        private int _survol = -1, _survolZone;   // zone : 0 carte, 1 interrupteur, 2 détails
        private int _focus = -1;
        private bool _etatsCharges;

        /// <summary>Clic ou Espace sur l'interrupteur : la carte affiche déjà l'état demandé.</summary>
        public event Action<Tweak, bool> Bascule;

        public TweakListe()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Name = "tweak-liste";
            BackColor = FpsUi.BgMain;
            TabStop = true;
            AccessibleName = "Optimisations";
            AccessibleRole = AccessibleRole.List;
            _vs.Dock = DockStyle.Right;
            _vs.Visible = false;
            _vs.SmallChange = PasMolette;
            _vs.TabStop = false;
            _vs.ValueChanged += (s, e) => { _survol = -1; Invalidate(); };
            Controls.Add(_vs);
        }

        // ------------------------------------------------------------------ données

        internal int Nombre { get { return _items.Count; } }
        internal Tweak At(int index) { return _items[index]; }
        internal bool EtatsSontCharges { get { return _etatsCharges; } }

        /// <summary>Nombre de cartes affichées dont l'optimisation est active.</summary>
        internal int Actives()
        {
            int n = 0;
            foreach (Tweak t in _items) if (EtatDe(t)) n++;
            return n;
        }

        /// <summary>Remplace la liste filtrée. Défilement remis en haut si la liste change.</summary>
        public void Definir(IReadOnlyList<Tweak> visibles)
        {
            bool change = visibles.Count != _items.Count;
            for (int i = 0; !change && i < visibles.Count; i++) change = !ReferenceEquals(visibles[i], _items[i]);
            if (!change) return;
            _items = new List<Tweak>(visibles);
            _index.Clear();
            for (int i = 0; i < _items.Count; i++) _index[Cle(_items[i])] = i;
            _survol = -1;
            _focus = _items.Count > 0 ? 0 : -1;
            if (_vs.Visible) _vs.Value = 0;
            Recalcule();
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.Reorder, -1);
        }

        /// <summary>État lu ou appliqué : n'invalide que la carte concernée.</summary>
        public void Etat(string id, bool actif)
        {
            bool avant;
            if (_etats.TryGetValue(id, out avant) && avant == actif) return;
            _etats[id] = actif;
            InvalideCarte(id);
        }

        /// <summary>Interrupteur grisé et inactif pendant l'application d'une optimisation.</summary>
        public void Occupe(string id, bool occupe)
        {
            if (occupe ? _occupes.Add(id) : _occupes.Remove(id)) InvalideCarte(id);
        }

        public void EtatsCharges() { _etatsCharges = true; Invalidate(); }

        internal bool Deplie(int index) { return _deplies.Contains(Cle(_items[index])); }
        internal void TestClicDetails(int index) { BasculeDetails(index); }

        private static string Cle(Tweak t) { return t.Id ?? t.Name ?? ""; }
        private bool EtatDe(Tweak t) { bool v; return _etats.TryGetValue(Cle(t), out v) && v; }

        // ------------------------------------------------------------------ disposition

        private int LargeurDispo { get { return Math.Max(300, ClientSize.Width - SystemInformation.VerticalScrollBarWidth); } }
        private int Defilement { get { return _vs.Visible ? _vs.Value : 0; } }

        private void Recalcule()
        {
            int dispo = LargeurDispo, w = GrilleCartes.LargeurCarte(dispo);
            if (w != _largeurMesure) { _mesures.Clear(); _largeurMesure = w; }
            var hauteurs = new int[_items.Count];
            for (int i = 0; i < hauteurs.Length; i++) hauteurs[i] = Hauteur(_items[i], w);
            _cases = GrilleCartes.Place(dispo, hauteurs, out _total);
            int contenu = _total + HautPad + BasPad, vue = Math.Max(1, ClientSize.Height);
            if (contenu > vue)
            {
                int max = contenu - vue;
                if (_vs.Value > max) _vs.Value = max;
                _vs.LargeChange = vue;
                _vs.Maximum = contenu - 1;
                _vs.Visible = true;
            }
            else { _vs.Value = 0; _vs.Visible = false; }
        }

        internal int MesuresDescPourTest;

        private int[] Mesures(Tweak t, int w)
        {
            int[] m;
            if (_mesures.TryGetValue(Cle(t), out m)) return m;
            int nameW = Math.Max(80, w - 138);
            int nameH = Math.Max(40, TextRenderer.MeasureText(t.Name ?? "", FpsUi.H3, new Size(nameW, 0), Wrap).Height);
            m = new[] { nameH, -1 };   // description : mesurée à la première peinture ou au dépliage
            _mesures[Cle(t)] = m;
            return m;
        }

        /// <summary>Hauteur de la description entière. Mesurée seulement quand la carte est peinte
        /// ou dépliée : la mesurer pour les 210 cartes coûtait ~40 ms à l'ouverture de la page.</summary>
        private int Pleine(Tweak t, int[] m, int w)
        {
            if (m[1] < 0)
            {
                m[1] = Math.Max(48, TextRenderer.MeasureText(t.Desc ?? "", FpsUi.Body, new Size(Math.Max(1, w - 32), 0), Wrap).Height + 4);
                MesuresDescPourTest++;
            }
            return m[1];
        }

        private int Hauteur(Tweak t, int w)
        {
            int[] m = Mesures(t, w);
            int descH = _deplies.Contains(Cle(t)) ? Math.Max(DescCompacte, Pleine(t, m, w)) : DescCompacte;
            return 16 + m[0] + 8 + descH + 16 + 46;
        }

        /// <summary>Rectangle de la carte en coordonnées client, défilement compris.</summary>
        internal Rectangle RectCarte(int index)
        {
            GrilleCartes.Case c = _cases[index];
            return new Rectangle(c.X, c.Y + HautPad - Defilement, c.W, c.H);
        }

        private struct Geo { public Rectangle Carte, Nom, Desc, Statut, Interrupteur, Details; public bool ADetails; }

        private Geo Geometrie(int index)
        {
            Tweak t = _items[index];
            Rectangle r = RectCarte(index);
            int w = r.Width;
            int[] m = Mesures(t, w);
            int descH = _deplies.Contains(Cle(t)) ? Math.Max(DescCompacte, Pleine(t, m, w)) : DescCompacte;
            var g = new Geo { Carte = r };
            g.Nom = new Rectangle(r.X + 60, r.Y + 16, Math.Max(80, w - 138), m[0]);
            g.Interrupteur = new Rectangle(r.X + w - 62, r.Y + 16, 46, 24);
            g.Desc = new Rectangle(r.X + 16, g.Nom.Bottom + 8, w - 32, descH);
            int bas = g.Desc.Bottom + 16;
            g.Statut = new Rectangle(r.X + 16, bas, Math.Max(1, w - 158), 36);
            g.ADetails = Pleine(t, m, w) > DescCompacte;
            g.Details = new Rectangle(r.X + w - 126, bas - 2, 110, 32);
            return g;
        }

        private void Touche(Point p, out int index, out int zone)
        {
            zone = 0;
            index = GrilleCartes.IndexA(_cases, p.X, p.Y - HautPad + Defilement);
            if (index < 0) return;
            Geo g = Geometrie(index);
            if (g.Interrupteur.Contains(p)) zone = 1;
            else if (g.ADetails && g.Details.Contains(p)) zone = 2;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Recalcule();
        }

        // ------------------------------------------------------------------ peinture

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics gr = e.Graphics;
            gr.Clear(BackColor);
            int premier, dernier;
            GrilleCartes.Visibles(_cases, Defilement - HautPad, ClientSize.Height, out premier, out dernier);
            for (int i = premier; i <= dernier; i++)
            {
                Geo g = Geometrie(i);
                if (g.Carte.IntersectsWith(e.ClipRectangle)) PeintCarte(gr, i, g);
            }
        }

        private void PeintCarte(Graphics gr, int index, Geo g)
        {
            Tweak t = _items[index];
            bool actif = EtatDe(t), occupe = _occupes.Contains(Cle(t));
            OnyxDesign.PaintCard(gr, g.Carte, FpsUi.Card, actif ? OnyxDesign.Edge : FpsUi.Border, 12);
            if (_survol == index && g.Carte.Width > 4 && g.Carte.Height > 4)
                using (var path = FpsUi.Round(new RectangleF(g.Carte.X + .5f, g.Carte.Y + .5f, g.Carte.Width - 1, g.Carte.Height - 1), 12))
                using (var pen = new Pen(FpsUi.GoldDim)) gr.DrawPath(pen, path);
            int icon = t.Category == Cat.Gpu ? NavIcons.Jeux : t.Category == Cat.Stockage ? NavIcons.Stockage
                : t.Category == Cat.Alim ? NavIcons.Optimisations : NavIcons.Systeme;
            OnyxDesign.IconWell(gr, icon, new Rectangle(g.Carte.X + 16, g.Carte.Y + 16, 32, 32), actif);
            TextRenderer.DrawText(gr, t.Name ?? "", FpsUi.H3, g.Nom, FpsUi.Ink, Wrap);
            TextRenderer.DrawText(gr, t.Desc ?? "", FpsUi.Body, g.Desc, FpsUi.Dim, Wrap | TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(gr, Statut(t), FpsUi.Tiny, g.Statut, t.Risque != Risque.Aucun || t.Reboot ? FpsUi.Warn : FpsUi.Dim, Wrap);
            ToggleSwitch.Dessine(gr, g.Interrupteur, actif, occupe || !Enabled, Focused && ShowFocusCues && _focus == index);
            if (g.ADetails)
            {
                bool chaud = _survol == index && _survolZone == 2;
                OnyxDesign.PaintCard(gr, g.Details, FpsUi.BlendGold(FpsUi.CardHi, chaud ? .11f : 0f), FpsUi.Border, 8);
                TextRenderer.DrawText(gr, _deplies.Contains(Cle(t)) ? "Réduire ↑" : "Détails ↓", FpsUi.Small, Rectangle.Inflate(g.Details, -8, -2), FpsUi.Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }

        internal static string Statut(Tweak t)
        {
            string s = t.Risque == Risque.Securite ? "Protection réduite"
                : t.Risque == Risque.Fonction ? "Fonction Windows modifiée"
                : t.Risque == Risque.AnnulationNonGarantie ? "Rétablissement à vérifier"
                : t.Recommended ? "Recommandé" : "Au choix";
            if (t.Reboot) s += " · Redémarrage";
            return s;
        }

        private void InvalideCarte(string id)
        {
            int i;
            if (_index.TryGetValue(id, out i)) InvalideCarte(i);
        }

        private void InvalideCarte(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            Rectangle r = RectCarte(index);
            r.Inflate(1, 1);
            Invalidate(r);
        }

        // ------------------------------------------------------------------ actions

        private void Basculer(int index)
        {
            if (index < 0 || index >= _items.Count || !Enabled) return;
            Tweak t = _items[index];
            if (_occupes.Contains(Cle(t))) return;
            bool voulu = !EtatDe(t);
            _etats[Cle(t)] = voulu;
            InvalideCarte(index);
            AccessibilityNotifyClients(AccessibleEvents.StateChange, index);
            var h = Bascule;
            if (h != null) h(t, voulu);
        }

        private void BasculeDetails(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            string id = Cle(_items[index]);
            if (!_deplies.Remove(id)) _deplies.Add(id);
            Recalcule();
            Invalidate();
            AmeneDansLaVue(index);
        }

        private void ChoisitFocus(int index)
        {
            if (_items.Count == 0) return;
            index = Math.Max(0, Math.Min(_items.Count - 1, index));
            int avant = _focus;
            _focus = index;
            InvalideCarte(avant); InvalideCarte(index);
            AmeneDansLaVue(index);
            if (Focused) AccessibilityNotifyClients(AccessibleEvents.Focus, index);
        }

        private void AmeneDansLaVue(int index)
        {
            if (!_vs.Visible || index < 0 || index >= _items.Count) return;
            Rectangle r = RectCarte(index);
            int v = _vs.Value;
            if (r.Top < 0) v += r.Top - 8;
            else if (r.Bottom > ClientSize.Height) v += Math.Min(r.Top - 8, r.Bottom - ClientSize.Height + 8);
            Defile(v);
        }

        private void Defile(int valeur)
        {
            if (!_vs.Visible) return;
            int max = Math.Max(0, _vs.Maximum - _vs.LargeChange + 1);
            _vs.Value = Math.Max(0, Math.Min(max, valeur));
        }

        // ------------------------------------------------------------------ souris et clavier

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i, zone;
            Touche(e.Location, out i, out zone);
            if (i == _survol && zone == _survolZone) return;
            int avant = _survol;
            _survol = i; _survolZone = zone;
            InvalideCarte(avant); InvalideCarte(i);
            Cursor = zone > 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            int avant = _survol;
            _survol = -1;
            InvalideCarte(avant);
            Cursor = Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Focused) Focus();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            int i, zone;
            Touche(e.Location, out i, out zone);
            if (i < 0) return;
            ChoisitFocus(i);
            if (zone == 1) Basculer(i);
            else if (zone == 2) BasculeDetails(i);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Defile(_vs.Value - e.Delta * PasMolette / 120);
            var h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right:
                case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown:
                case Keys.Space: case Keys.Enter:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_items.Count == 0) return;
            int f = Math.Max(0, _focus), cols = GrilleCartes.Colonnes(LargeurDispo);
            int page = cols * Math.Max(1, ClientSize.Height / 220);
            switch (e.KeyCode)
            {
                case Keys.Left: ChoisitFocus(f - 1); break;
                case Keys.Right: ChoisitFocus(f + 1); break;
                case Keys.Up: ChoisitFocus(f - cols); break;
                case Keys.Down: ChoisitFocus(f + cols); break;
                case Keys.Home: ChoisitFocus(0); break;
                case Keys.End: ChoisitFocus(_items.Count - 1); break;
                case Keys.PageUp: ChoisitFocus(f - page); break;
                case Keys.PageDown: ChoisitFocus(f + page); break;
                case Keys.Enter: BasculeDetails(f); break;
                case Keys.Space: break;   // au relâchement, comme ToggleSwitch : pas de répétition
                default: return;
            }
            e.Handled = e.SuppressKeyPress = true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space && _items.Count > 0) { Basculer(Math.Max(0, _focus)); e.Handled = true; }
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            if (_focus < 0 && _items.Count > 0) _focus = 0;
            InvalideCarte(_focus);
            if (_focus >= 0) AccessibilityNotifyClients(AccessibleEvents.Focus, _focus);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            InvalideCarte(_focus);
        }

        // ------------------------------------------------------------------ accessibilité

        protected override AccessibleObject CreateAccessibilityInstance() { return new ListeAcc(this); }

        private sealed class ListeAcc : ControlAccessibleObject
        {
            private readonly TweakListe _l;
            public ListeAcc(TweakListe l) : base(l) { _l = l; }
            public override AccessibleRole Role { get { return AccessibleRole.List; } }
            public override int GetChildCount() { return _l.Nombre; }
            public override AccessibleObject GetChild(int index) { return index >= 0 && index < _l.Nombre ? new CarteAcc(_l, index) : null; }
            public override AccessibleObject GetFocused() { return _l.Focused && _l._focus >= 0 && _l._focus < _l.Nombre ? GetChild(_l._focus) : base.GetFocused(); }
            public override AccessibleObject HitTest(int x, int y)
            {
                int i, zone;
                _l.Touche(_l.PointToClient(new Point(x, y)), out i, out zone);
                return i >= 0 ? GetChild(i) : base.HitTest(x, y);
            }
        }

        private sealed class CarteAcc : AccessibleObject
        {
            private readonly TweakListe _l;
            private readonly int _i;
            public CarteAcc(TweakListe l, int i) { _l = l; _i = i; }
            private Tweak T { get { return _i < _l.Nombre ? _l._items[_i] : null; } }
            public override string Name { get { return T == null ? "" : T.Name; } }
            public override string Description { get { return T == null ? "" : T.Desc; } }
            public override string Help { get { return T == null ? "" : Statut(T); } }
            public override AccessibleRole Role { get { return AccessibleRole.CheckButton; } }
            public override AccessibleObject Parent { get { return _l.AccessibilityObject; } }
            public override Rectangle Bounds { get { return _i < _l.Nombre ? _l.RectangleToScreen(_l.RectCarte(_i)) : Rectangle.Empty; } }
            public override string DefaultAction { get { return T != null && _l.EtatDe(T) ? "Désactiver" : "Activer"; } }
            public override AccessibleStates State
            {
                get
                {
                    var s = AccessibleStates.Focusable | AccessibleStates.Selectable;
                    if (T == null) return s | AccessibleStates.Unavailable;
                    if (_l.EtatDe(T)) s |= AccessibleStates.Checked;
                    if (_l._occupes.Contains(Cle(T))) s |= AccessibleStates.Unavailable;
                    if (_l.Focused && _l._focus == _i) s |= AccessibleStates.Focused;
                    return s;
                }
            }
            public override void DoDefaultAction() { _l.Basculer(_i); }
            public override void Select(AccessibleSelection flags)
            {
                if ((flags & AccessibleSelection.TakeFocus) != 0) { _l.Focus(); _l.ChoisitFocus(_i); }
            }
        }
    }
}
