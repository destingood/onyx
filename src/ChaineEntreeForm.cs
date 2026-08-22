using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// L'ÉCRAN DE LA CHAÎNE D'ENTRÉE — CE QUE CHAQUE MAILLON COÛTE, ET LEQUEL VAUT LA PEINE.
    ///
    /// La barre empilée en haut EST l'argument. Une liste de millisecondes en colonne se lit comme
    /// une liste : on la parcourt, on retient le premier item, et on va régler celui dont on a
    /// entendu parler. La même chose en barre proportionnelle se lit d'un coup — et on voit que le
    /// segment « file de rendu » écrase le segment « souris », ce qu'aucune phrase ne fait
    /// comprendre aussi vite.
    ///
    /// Les maillons NON MESURÉS gardent leur ligne, en creux. Les faire disparaître donnerait un
    /// budget qui a l'air complet alors qu'il lui manque la dalle — et un budget qui a l'air
    /// complet ne se fait plus jamais compléter.
    ///
    /// Les leviers sont classés par millisecondes rendues ICI, jamais par réputation. Celui qu'ONYX
    /// sait appliquer porte un bouton ; celui qui se règle ailleurs dit OÙ, au lieu de faire semblant.
    /// </summary>
    internal sealed class ChaineEntreeForm : Form
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int sz);

        private readonly Action<string, int> _log;

        private Panel _entete, _budget, _chaine, _leviers, _piste;
        private Button _mesurerSouris, _relire, _fermer;
        private readonly List<Button> _boutonsLevier = new List<Button>();

        private ChaineEntree.Releve _releve = new ChaineEntree.Releve();
        private List<ChaineEntree.Maillon> _maillons = new List<ChaineEntree.Maillon>();
        private List<ChaineEntree.Levier> _lev = new List<ChaineEntree.Levier>();

        private const int Marge = 22;
        private const int HauteurLevier = 56;

        public ChaineEntreeForm(Action<string, int> log)
        {
            _log = log ?? delegate (string m, int n) { };
            Construit();
            Relit();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int v = 1; DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }
        }

        // ==================================================================
        //  Construction
        // ==================================================================

        private void Construit()
        {
            Text = "ONYX — chaîne d'entrée";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1080, 800);
            MinimumSize = new Size(900, 680);
            BackColor = FpsUi.BgMain;
            Font = FpsUi.Body;
            DoubleBuffered = true;
            try { Icon = Logo.MakeIcon(32, FpsUi.Gold); } catch { }

            Controls.Add(ConstruitPied());
            Controls.Add(ConstruitPiste());
            Controls.Add(ConstruitLeviers());
            Controls.Add(ConstruitChaine());
            Controls.Add(ConstruitBudget());
            Controls.Add(ConstruitTitre());
        }

        private Panel ConstruitTitre()
        {
            _entete = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Color.Transparent };
            _entete.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                FpsUi.DrawTracked(g, "CHAÎNE D'ENTRÉE", FpsUi.H2, FpsUi.Gold, Marge, 40, 2.4f);
                TextRenderer.DrawText(g, "Du clic au pixel : ce que chaque maillon coûte sur CETTE machine.",
                    FpsUi.Small, new Point(Marge, 58), FpsUi.Dim2, TextFormatFlags.NoPadding);
                using (var p = new Pen(FpsUi.Border))
                    g.DrawLine(p, Marge, _entete.Height - 1, _entete.Width - Marge, _entete.Height - 1);
            };
            return _entete;
        }

        // ------------------------------------------------------------------ le budget, en barre

        private Panel ConstruitBudget()
        {
            _budget = new Panel { Dock = DockStyle.Top, Height = 172, BackColor = Color.Transparent };
            _budget.Paint += DessineBudget;
            return _budget;
        }

        /// <summary>Couleur d'un maillon. La file de rendu prend l'orange d'alerte parce que c'est
        /// presque toujours elle qui domine la barre — et qu'on ne la regarde jamais.</summary>
        private static Color Teinte(string nom)
        {
            if (nom == ChaineEntree.NomSouris) return FpsUi.Gold;
            if (nom == ChaineEntree.NomRendu) return FpsUi.Warn;
            if (nom == ChaineEntree.NomEcran) return FpsUi.Dim;
            return FpsUi.Dim2;
        }

        private void DessineBudget(object envoyeur, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(Marge, 12, Math.Max(60, _budget.Width - Marge * 2), _budget.Height - 26);
            FpsUi.PaintCard(g, r, FpsUi.Card, FpsUi.Border, 10f);

            double total = ChaineEntree.TotalMoyen(_maillons);
            if (total <= 0)
            {
                TextRenderer.DrawText(g, "Rien de mesuré pour l'instant. « Mesurer la souris » ouvre le testeur ; "
                    + "la fréquence d'écran et le profil graphique sont lus automatiquement.",
                    FpsUi.Small, new Rectangle(r.X + 20, r.Y, r.Width - 40, r.Height), FpsUi.Dim2,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                return;
            }

            // L'OR EST RÉSERVÉ À UN BUDGET QUI EN EST UN. Avec un seul maillon chiffré sur trois,
            // « 1,00 ms » en gros et en doré se lit « ta latence vaut une milliseconde » — flatteur,
            // faux, et précisément le chiffre que ce module reproche aux autres. Le nombre reste
            // affiché, en sourdine, et la ligne dessous dit ce qui manque pour qu'il vaille quelque chose.
            bool credible = ChaineEntree.BudgetCredible(_maillons);
            int chiffres = ChaineEntree.Chiffres(_maillons);

            TextRenderer.DrawText(g, ChaineEntree.Nombre(total) + " ms", FpsUi.Num,
                new Point(r.X + 20, r.Y + 14), credible ? FpsUi.Gold : FpsUi.Dim2, TextFormatFlags.NoPadding);
            int xd = r.X + 24 + TextRenderer.MeasureText(ChaineEntree.Nombre(total) + " ms", FpsUi.Num).Width;
            TextRenderer.DrawText(g,
                credible ? "au minimum, entre ton geste et l'image"
                         : "sur " + chiffres + " maillon(s) chiffré(s) : ce n'est pas encore un budget",
                FpsUi.Small, new Point(xd, r.Y + 26), credible ? FpsUi.Dim : FpsUi.Warn, TextFormatFlags.NoPadding);

            int manquants = ChaineEntree.NonMesures(_maillons);
            if (manquants > 0)
                TextRenderer.DrawText(g,
                    credible
                        ? manquants + " maillon(s) non mesuré(s), dont la dalle — c'est un plancher, pas un total."
                        : "Mesure la souris et lis la file de rendu : sans elles, rien ne se compare.",
                    FpsUi.Tiny, new Point(r.X + 21, r.Y + 50), FpsUi.Dim2, TextFormatFlags.NoPadding);

            // ---- la barre empilée : le seul endroit où les proportions se voient d'un coup
            var barre = new Rectangle(r.X + 20, r.Y + 76, r.Width - 40, 26);
            using (var fond = FpsUi.Round(new RectangleF(barre.X, barre.Y, barre.Width, barre.Height), 6f))
                g.FillPath(new SolidBrush(Color.FromArgb(40, FpsUi.Dim2)), fond);

            int x = barre.X;
            foreach (ChaineEntree.Maillon m in _maillons)
            {
                if (m == null || !m.Ms.HasValue || m.Ms.Value <= 0) continue;
                int l = (int)Math.Round(barre.Width * (m.Ms.Value / total));
                if (l < 2) continue;
                var seg = new Rectangle(x, barre.Y, l, barre.Height);
                using (var p = FpsUi.Round(new RectangleF(seg.X, seg.Y, seg.Width, seg.Height), 5f))
                    g.FillPath(new SolidBrush(Color.FromArgb(225, Teinte(m.Nom))), p);
                // Le pourcentage n'est écrit que s'il tient : un chiffre tronqué vaut moins que pas
                // de chiffre du tout, et la légende dessous porte déjà les noms.
                string part = Math.Round(m.Ms.Value / total * 100) + " %";
                if (l > TextRenderer.MeasureText(part, FpsUi.Tiny).Width + 12)
                    TextRenderer.DrawText(g, part, FpsUi.Tiny, seg, Color.FromArgb(18, 15, 11),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += l;
            }

            // ---- légende
            int lx = barre.X;
            foreach (ChaineEntree.Maillon m in _maillons)
            {
                if (m == null || !m.Ms.HasValue || m.Ms.Value <= 0) continue;
                using (var p = FpsUi.Round(new RectangleF(lx, barre.Bottom + 12, 9, 9), 2f))
                    g.FillPath(new SolidBrush(Teinte(m.Nom)), p);
                string t = m.Nom + "  " + ChaineEntree.Nombre(m.Ms.Value) + " ms";
                TextRenderer.DrawText(g, t, FpsUi.Tiny, new Point(lx + 14, barre.Bottom + 10), FpsUi.Dim,
                    TextFormatFlags.NoPadding);
                lx += 14 + TextRenderer.MeasureText(t, FpsUi.Tiny).Width + 22;
            }
        }

        // ------------------------------------------------------------------ la chaîne, maillon par maillon

        private Panel ConstruitChaine()
        {
            _chaine = new Panel { Dock = DockStyle.Top, Height = 280, BackColor = Color.Transparent };
            _chaine.Paint += DessineChaine;
            return _chaine;
        }

        private void DessineChaine(object envoyeur, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            TextRenderer.DrawText(g, "LES MAILLONS", FpsUi.Tiny, new Point(Marge, 4), FpsUi.Dim2, TextFormatFlags.NoPadding);

            int y = 22, l = Math.Max(60, _chaine.Width - Marge * 2);
            foreach (ChaineEntree.Maillon m in _maillons)
            {
                var r = new Rectangle(Marge, y, l, 48);
                bool vide = m.Source == ChaineEntree.Source.NonMesure;

                // Un maillon non mesuré garde sa place, en creux : le faire disparaître donnerait
                // un budget qui a l'air complet — et un budget complet ne se fait plus compléter.
                FpsUi.PaintCard(g, r, vide ? Color.FromArgb(18, 16, 13) : FpsUi.Card, FpsUi.Border, 8f);
                if (!vide)
                    g.FillRectangle(new SolidBrush(Color.FromArgb(200, Teinte(m.Nom))),
                        new Rectangle(r.X, r.Y + 2, 3, r.Height - 4));

                TextRenderer.DrawText(g, m.Nom, FpsUi.Body, new Point(r.X + 16, r.Y + 7),
                    vide ? FpsUi.Dim2 : FpsUi.Ink, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, m.Valeur, FpsUi.Tiny, new Point(r.X + 16, r.Y + 27), FpsUi.Dim2,
                    TextFormatFlags.NoPadding);

                // Le chiffre à droite, aligné : c'est la colonne qu'on parcourt du regard.
                string ms = m.Ms.HasValue ? ChaineEntree.Nombre(m.Ms.Value) + " ms"
                          : m.MsPire.HasValue ? ChaineEntree.Nombre(m.MsPire.Value) + " ms au pire"
                          : "—";
                TextRenderer.DrawText(g, ms, m.Ms.HasValue ? FpsUi.H3 : FpsUi.Small,
                    new Rectangle(r.Right - 190, r.Y + 6, 174, 20),
                    m.Ms.HasValue ? FpsUi.Ink : FpsUi.Dim2,
                    TextFormatFlags.Right | TextFormatFlags.NoPadding);

                TextRenderer.DrawText(g, m.Explique, FpsUi.Tiny,
                    new Rectangle(r.X + 150, r.Y + 27, r.Width - 340, 16), FpsUi.Dim2,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

                y += 52;
            }
        }

        // ------------------------------------------------------------------ les leviers

        private Panel ConstruitLeviers()
        {
            _leviers = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.Transparent };
            _leviers.Paint += DessineLeviers;
            return _leviers;
        }

        private void DessineLeviers(object envoyeur, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            TextRenderer.DrawText(g, "CE QUI RAPPORTE LE PLUS, D'ABORD", FpsUi.Tiny,
                new Point(Marge, 4), FpsUi.Dim2, TextFormatFlags.NoPadding);

            int l = Math.Max(60, _leviers.Width - Marge * 2);
            if (_lev.Count == 0)
            {
                var vide = new Rectangle(Marge, 22, l, 40);
                FpsUi.PaintCard(g, vide, FpsUi.Card, FpsUi.Border, 8f);
                // « Aucun levier » ne veut PAS dire la même chose selon qu'on a mesuré ou non.
                // Féliciter une machine dont on ne connaît qu'un maillon sur trois, c'est faire
                // passer une absence de mesure pour un bon résultat — le mensonge le plus facile.
                bool credible = ChaineEntree.BudgetCredible(_maillons);
                TextRenderer.DrawText(g,
                    credible
                        ? "Aucun levier ne rendrait de millisecondes ici. C'est un bon résultat, pas une panne."
                        : "Trop peu de maillons mesurés pour classer quoi que ce soit. Ce n'est pas « rien à faire », "
                          + "c'est « rien de mesuré ».",
                    FpsUi.Small, new Rectangle(vide.X + 16, vide.Y, vide.Width - 32, vide.Height),
                    credible ? FpsUi.Dim : FpsUi.Warn,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return;
            }

            int y = 22;
            foreach (ChaineEntree.Levier v in _lev)
            {
                var r = new Rectangle(Marge, y, l, HauteurLevier - 8);
                FpsUi.PaintCard(g, r, FpsUi.Card, FpsUi.Border, 8f);

                // Le gain EN OR, et sa part du budget juste dessous : c'est la comparaison qui
                // remet les leviers dans l'ordre, pas leur notoriété.
                TextRenderer.DrawText(g, "− " + ChaineEntree.Nombre(v.GainMs) + " ms", FpsUi.H3,
                    new Point(r.X + 16, r.Y + 6), FpsUi.Gold, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, Math.Round(v.Part * 100) + " % du budget", FpsUi.Tiny,
                    new Point(r.X + 17, r.Y + 26), FpsUi.Dim2, TextFormatFlags.NoPadding);

                TextRenderer.DrawText(g, v.Nom, FpsUi.Body, new Point(r.X + 160, r.Y + 6), FpsUi.Ink,
                    TextFormatFlags.NoPadding);
                string bas = v.Quoi + (v.Coute.Length > 0 ? "   ·   coûte : " + v.Coute : "");
                TextRenderer.DrawText(g, bas, FpsUi.Tiny,
                    new Rectangle(r.X + 160, r.Y + 26, r.Width - 340, 16), FpsUi.Dim2,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

                y += HauteurLevier;
            }
        }

        // ------------------------------------------------------------------ la piste non chiffrée

        private Panel ConstruitPiste()
        {
            _piste = new Panel { Dock = DockStyle.Top, Height = 0, BackColor = Color.Transparent };
            _piste.Paint += delegate (object s, PaintEventArgs e)
            {
                string t = ChaineEntree.PisteFileRendu(_releve);
                if (t == null) return;
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(Marge, 6, Math.Max(60, _piste.Width - Marge * 2), _piste.Height - 16);
                FpsUi.PaintCard(g, r, FpsUi.BlendGold(FpsUi.Card, 0.06f), FpsUi.GoldDim, 8f);
                TextRenderer.DrawText(g, "PISTE NON CHIFFRÉE", FpsUi.Tiny,
                    new Point(r.X + 16, r.Y + 8), FpsUi.Gold, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, t, FpsUi.Small,
                    new Rectangle(r.X + 16, r.Y + 24, r.Width - 32, r.Height - 30), FpsUi.Dim,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            };
            return _piste;
        }

        // ------------------------------------------------------------------ pied

        private Panel ConstruitPied()
        {
            var pied = new Panel { Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(Marge, 12, Marge, 20), BackColor = Color.Transparent };

            _fermer = FpsUi.GhostButton("Fermer");
            _fermer.Click += delegate { Close(); };

            _relire = FpsUi.GhostButton("Relire la machine");
            _relire.Click += delegate { Relit(); };

            _mesurerSouris = FpsUi.GoldButton("Mesurer la souris");
            _mesurerSouris.Click += delegate { MesureSouris(); };

            int[] largeurs = { 92, 160, 190 };
            Button[] b = { _fermer, _relire, _mesurerSouris };
            for (int i = 0; i < b.Length; i++)
            {
                b[i].Dock = DockStyle.Right;
                b[i].Width = largeurs[i];
                pied.Controls.Add(b[i]);
                pied.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 10, BackColor = Color.Transparent });
            }
            return pied;
        }

        // ==================================================================
        //  Données
        // ==================================================================

        private void Relit()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                _releve = ChaineEntree.Mesure();
                _maillons = ChaineEntree.Construit(_releve);
                _lev = ChaineEntree.Leviers(_releve);
            }
            catch (Exception ex)
            {
                JournalTechnique.Echec("ChaineEntreeForm.Relit", ex);
            }
            finally { Cursor = Cursors.Default; }

            RetireBoutonsLevier();
            _leviers.Height = 30 + (_lev.Count == 0 ? 48 : _lev.Count * HauteurLevier);
            _piste.Height = ChaineEntree.PisteFileRendu(_releve) == null ? 0 : 96;
            _chaine.Height = 30 + _maillons.Count * 52;
            PoseBoutonsLevier();

            foreach (Control c in new Control[] { _entete, _budget, _chaine, _leviers, _piste }) c.Invalidate();
        }

        private void RetireBoutonsLevier()
        {
            foreach (Button b in _boutonsLevier)
            {
                try { if (b.Parent != null) b.Parent.Controls.Remove(b); b.Dispose(); } catch { }
            }
            _boutonsLevier.Clear();
        }

        /// <summary>
        /// Un bouton par levier qu'ONYX sait appliquer LUI-MÊME. Les autres n'en reçoivent pas :
        /// un bouton qui ouvrirait une page d'aide en promettant d'agir vaut moins qu'une phrase
        /// qui dit où aller.
        /// </summary>
        private void PoseBoutonsLevier()
        {
            int y = 22;
            foreach (ChaineEntree.Levier v in _lev)
            {
                Button b = null;
                if (v.Nom == "Fréquence d'affichage" && _releve.EcranHzMax.HasValue)
                {
                    int hz = _releve.EcranHzMax.Value;
                    b = FpsUi.GoldButton("Passer à " + hz + " Hz");
                    b.Click += delegate { AppliqueHz(hz); };
                }
                else if (v.Nom == "Taux de rapport")
                {
                    // Le taux de rapport se règle DANS la souris, par le logiciel du fabricant.
                    // Aucun logiciel tiers ne peut l'imposer : dire où aller vaut mieux que
                    // proposer un bouton qui n'aurait rien à faire.
                    b = FpsUi.GhostButton("Dans le logiciel de ta souris");
                    b.Enabled = false;
                }
                else if (v.Nom == "Images en attente")
                {
                    b = FpsUi.GhostButton("Profil « Sûr »");
                    b.Click += delegate { AppliqueProfilSur(); };
                }

                if (b != null)
                {
                    b.Width = 210; b.Height = 32;
                    b.Location = new Point(Math.Max(60, _leviers.Width - Marge - 226), y + 8);
                    b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    _leviers.Controls.Add(b);
                    _boutonsLevier.Add(b);
                }
                y += HauteurLevier;
            }
        }

        // ==================================================================
        //  Gestes
        // ==================================================================

        private void MesureSouris()
        {
            // On n'invente pas une seconde mesure : c'est le testeur existant qui mesure, et il
            // retient désormais son chiffre. La chaîne le relit ensuite.
            try { using (var f = new MouseForm(_log)) f.ShowDialog(this); }
            catch (Exception ex) { JournalTechnique.Echec("ChaineEntreeForm.MesureSouris", ex); }
            Relit();
        }

        private void AppliqueHz(int hz)
        {
            if (MessageBox.Show(this,
                "Passer l'écran à " + hz + " Hz ?\n\nWindows revient tout seul à la fréquence précédente "
                + "si l'image disparaît — c'est le mécanisme de secours de l'affichage, pas une promesse d'ONYX.",
                "ONYX", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            Cursor = Cursors.WaitCursor;
            try
            {
                bool ok = false;
                foreach (DisplayInfo.DisplayMode m in DisplayInfo.Query())
                    if (m != null && m.MaxHz == hz) { ok = DisplayInfo.SetHz(m.Device, hz); break; }
                _log(ok ? "Écran passé à " + hz + " Hz." : "L'écran a refusé " + hz + " Hz.", ok ? 1 : 3);
            }
            catch (Exception ex)
            {
                JournalTechnique.Echec("ChaineEntreeForm.AppliqueHz", ex);
                _log("Changement de fréquence impossible : " + ex.Message, 3);
            }
            finally { Cursor = Cursors.Default; }
            Relit();
        }

        private void AppliqueProfilSur()
        {
            if (MessageBox.Show(this,
                "Poser le profil graphique « Sûr » ?\n\nIl active le mode faible latence du pilote, "
                + "qui borne la file de rendu à une image, et laisse le reste des réglages d'usine.\n\n"
                + "Réversible : le profil « Défaut » remet les valeurs d'origine.",
                "ONYX", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            Cursor = Cursors.WaitCursor;
            try { NvProfile.Applique(NvProfile.Kind.Sur, _log); }
            catch (Exception ex)
            {
                JournalTechnique.Echec("ChaineEntreeForm.AppliqueProfilSur", ex);
                _log("Profil graphique impossible à poser : " + ex.Message, 3);
            }
            finally { Cursor = Cursors.Default; }
            Relit();
        }
    }
}
