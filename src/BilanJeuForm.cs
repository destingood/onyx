using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Fenêtre « Bilan de jeu » (bouton de Mes jeux) : résumé de <see cref="BilanJeu"/> sur 7 jours,
    /// 30 jours ou tout le journal, puis les jeux les plus joués en barres. Lecture seule.
    /// </summary>
    internal sealed class BilanJeuForm : Form
    {
        private const int Rang = 48;      // hauteur d'une ligne de jeu
        private const int MaxLignes = 6;

        private readonly List<JournalJeu.Partie> _parties;
        private readonly DateTime _maintenant;
        private readonly List<Button> _periodes = new List<Button>();
        private readonly Label _resume;
        private readonly BufferedPanel _jeux;
        private BilanJeu.Resultat _r;

        public BilanJeuForm(List<JournalJeu.Partie> parties, DateTime maintenant)
        {
            _parties = parties ?? new List<JournalJeu.Partie>();
            _maintenant = maintenant;
            Text = "ONYX — Bilan de jeu";
            ClientSize = new Size(720, 566);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = FpsUi.BgMain; ForeColor = FpsUi.Ink; Font = FpsUi.Body;
            try { Icon = Logo.MakeIcon(32, FpsUi.Gold); } catch { }

            int x = 20;
            foreach (int j in new[] { 7, 30, 0 })
            {
                int jours = j;
                Button b = OnyxDesign.Button(j == 0 ? "Depuis le début" : j + " jours");
                b.SetBounds(x, 16, j == 0 ? 150 : 110, 34);
                b.Tag = jours;
                b.Click += (s, e) => Affiche(jours);
                Controls.Add(b); _periodes.Add(b);
                x += b.Width + 10;
            }

            _resume = new Label { Location = new Point(20, 62), Size = new Size(680, 100), ForeColor = FpsUi.Ink, Font = FpsUi.Body };
            Controls.Add(_resume);

            Controls.Add(new Label
            {
                Text = "JEUX LES PLUS JOUÉS", Location = new Point(20, 168), Size = new Size(680, 22),
                ForeColor = FpsUi.Dim, Font = FpsUi.Small
            });
            _jeux = new BufferedPanel { Location = new Point(20, 192), Size = new Size(680, Rang * MaxLignes + 20), BackColor = FpsUi.BgMain };
            _jeux.Paint += (s, e) => DessineJeux(e.Graphics);
            Controls.Add(_jeux);

            Button fermer = OnyxDesign.Button("Fermer");
            fermer.SetBounds(590, 520, 110, 34);
            fermer.Click += (s, e) => Close();
            Controls.Add(fermer);
            CancelButton = fermer;

            Affiche(7);
        }

        private void Affiche(int jours)
        {
            _r = BilanJeu.Calcule(_parties, _maintenant, jours);
            foreach (Button b in _periodes)
            {
                bool actif = (int)b.Tag == jours;
                b.ForeColor = actif ? FpsUi.Gold : FpsUi.Dim;
                b.BackColor = actif ? FpsUi.BlendGold(FpsUi.Card, .1f) : FpsUi.Card;
                b.FlatAppearance.BorderSize = actif ? 1 : 0;
                b.FlatAppearance.BorderColor = actif ? FpsUi.GoldDim : FpsUi.Border;
            }
            _resume.Text = string.Join("\r\n", BilanJeu.Resume(_r));
            _jeux.Invalidate();
        }

        private void DessineJeux(Graphics g)
        {
            if (_r == null || _r.Jeux.Count == 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int max = Math.Max(1, _r.Jeux[0].Secondes);
            int w = _jeux.Width - 4;
            int n = Math.Min(MaxLignes, _r.Jeux.Count);
            using (var fond = new SolidBrush(FpsUi.Card))
            using (var barre = new SolidBrush(FpsUi.Gold))
            {
                for (int i = 0; i < n; i++)
                {
                    BilanJeu.Ligne l = _r.Jeux[i];
                    int y = i * Rang;
                    TextRenderer.DrawText(g, l.Jeu, FpsUi.Body, new Rectangle(0, y, 300, 22), FpsUi.Ink,
                        TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, BilanJeu.Detail(l), FpsUi.Small, new Rectangle(300, y + 2, w - 300, 20), FpsUi.Dim,
                        TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    var piste = new Rectangle(0, y + 26, w, 8);
                    g.FillRectangle(fond, piste);
                    int lg = Math.Max(4, (int)Math.Round((double)piste.Width * l.Secondes / max));
                    g.FillRectangle(barre, new Rectangle(piste.X, piste.Y, lg, piste.Height));
                }
                if (_r.Jeux.Count > n)
                    TextRenderer.DrawText(g, "+ " + (_r.Jeux.Count - n) + " autres jeux", FpsUi.Small,
                        new Rectangle(0, n * Rang, w, 20), FpsUi.Dim, TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }
        }
    }
}
