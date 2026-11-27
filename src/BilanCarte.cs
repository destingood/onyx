using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace BTOptimizer
{
    /// <summary>
    /// Carte image du bilan de jeu (1200 × 630, le format des aperçus de liens) : période, temps total,
    /// écart avec la période d'avant, faits marquants et les 5 jeux les plus joués. Rien n'est envoyé :
    /// l'image est enregistrée ou copiée, l'utilisateur la partage lui-même. Les textes viennent de <see cref="BilanJeu"/>.
    /// </summary>
    internal static class BilanCarte
    {
        public const int Largeur = 1200, Hauteur = 630, MaxJeux = 5;

        public static Bitmap Dessine(BilanJeu.Resultat r, string ligneSucces, DateTime maintenant)
        {
            var bmp = new Bitmap(Largeur, Hauteur);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var police = new Polices())
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(FpsUi.BgMain);
                // Halo doré discret en haut à gauche, cadre fin.
                using (var halo = new GraphicsPath())
                {
                    halo.AddEllipse(-260, -320, 900, 760);
                    using (var pb = new PathGradientBrush(halo) { CenterColor = Color.FromArgb(38, FpsUi.Gold), SurroundColors = new[] { Color.FromArgb(0, FpsUi.Gold) } })
                        g.FillPath(pb, halo);
                }
                using (var cadre = new Pen(FpsUi.Border, 2)) g.DrawRectangle(cadre, 1, 1, Largeur - 3, Hauteur - 3);

                Logo.Draw(g, new RectangleF(56, 44, 52, 52), FpsUi.Gold, true);
                Logo.DrawWordmark(g, new RectangleF(124, 56, 120, 28), FpsUi.Ink);
                Ecrit(g, maintenant.ToString("dd'/'MM'/'yyyy", CultureInfo.InvariantCulture), police.Petite, FpsUi.Dim, new RectangleF(800, 56, 344, 30), StringAlignment.Far);

                // Colonne gauche : le temps de jeu.
                Ecrit(g, BilanJeu.CarteTitre(r).ToUpperInvariant(), police.Etiquette, FpsUi.Gold, new RectangleF(56, 136, 560, 30));
                bool vide = r == null || r.Parties == 0;
                Ecrit(g, vide ? "—" : JournalJeu.Duree(r.Secondes), police.Chiffre, FpsUi.Ink, new RectangleF(48, 168, 580, 130));
                Ecrit(g, BilanJeu.CarteSous(r), police.Texte, FpsUi.Dim, new RectangleF(58, 298, 560, 34));
                var faits = BilanJeu.CarteFaits(r);
                if (!string.IsNullOrEmpty(ligneSucces))
                {
                    int p = ligneSucces.IndexOf(" (", StringComparison.Ordinal);   // le détail du dernier succès ne tient pas
                    faits.Add((p > 0 ? ligneSucces.Substring(0, p) : ligneSucces).TrimEnd('.'));
                }
                float y = 368;
                using (var puce = new SolidBrush(FpsUi.Gold))
                    for (int i = 0; i < faits.Count && i < 4; i++)
                    {
                        g.FillEllipse(puce, 58, y + 12, 8, 8);
                        Ecrit(g, faits[i], police.Fait, i == 0 && faits[i].StartsWith("+", StringComparison.Ordinal) ? FpsUi.Gold : FpsUi.Ink,
                            new RectangleF(78, y, 540, 32));
                        y += 44;
                    }

                // Colonne droite : les jeux les plus joués.
                using (var sep = new Pen(FpsUi.Border, 1)) g.DrawLine(sep, 660, 140, 660, 560);
                Ecrit(g, "JEUX LES PLUS JOUÉS", police.Etiquette, FpsUi.Dim, new RectangleF(700, 136, 444, 30));
                if (!vide)
                {
                    int max = Math.Max(1, r.Jeux[0].Secondes);
                    int n = Math.Min(MaxJeux, r.Jeux.Count);
                    using (var piste = new SolidBrush(FpsUi.Card))
                    using (var barre = new LinearGradientBrush(new Rectangle(700, 0, 444, 10), FpsUi.GoldDim, FpsUi.Gold, 0f))
                        for (int i = 0; i < n; i++)
                        {
                            BilanJeu.Ligne l = r.Jeux[i];
                            float yy = 184 + i * 74;
                            Ecrit(g, l.Jeu, police.Jeu, FpsUi.Ink, new RectangleF(700, yy, 300, 32));
                            Ecrit(g, JournalJeu.Duree(l.Secondes), police.Petite, FpsUi.Dim, new RectangleF(1000, yy + 4, 144, 28), StringAlignment.Far);
                            var p = new RectangleF(700, yy + 38, 444, 10);
                            using (var chemin = FpsUi.Round(Rectangle.Round(p), 5)) g.FillPath(piste, chemin);
                            float lg = Math.Max(10, p.Width * l.Secondes / max);
                            using (var chemin = FpsUi.Round(Rectangle.Round(new RectangleF(p.X, p.Y, lg, p.Height)), 5)) g.FillPath(barre, chemin);
                        }
                    if (r.Jeux.Count > n)
                        Ecrit(g, "+ " + (r.Jeux.Count - n) + (r.Jeux.Count - n > 1 ? " autres jeux" : " autre jeu"), police.Petite, FpsUi.Dim, new RectangleF(700, 184 + n * 74, 444, 28));
                }
                else Ecrit(g, "Joue avec ONYX ouvert : chaque partie compte.", police.Petite, FpsUi.Dim, new RectangleF(700, 184, 444, 60));

                Ecrit(g, "Mesuré sur mon PC par ONYX", police.Petite, FpsUi.Dim, new RectangleF(56, 570, 600, 28));
            }
            return bmp;
        }

        private static void Ecrit(Graphics g, string texte, Font f, Color c, RectangleF r, StringAlignment h = StringAlignment.Near)
        {
            using (var sf = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, Alignment = h, LineAlignment = StringAlignment.Near })
            using (var b = new SolidBrush(c))
                g.DrawString(texte ?? "", f, b, r, sf);
        }

        /// <summary>Polices de la carte, en pixels (l'image ne dépend pas de l'échelle de l'écran).</summary>
        private sealed class Polices : IDisposable
        {
            public readonly Font Chiffre = new Font("Segoe UI Semibold", 104, FontStyle.Regular, GraphicsUnit.Pixel);
            public readonly Font Etiquette = new Font("Segoe UI Semibold", 20, FontStyle.Regular, GraphicsUnit.Pixel);
            public readonly Font Texte = new Font("Segoe UI", 26, FontStyle.Regular, GraphicsUnit.Pixel);
            public readonly Font Fait = new Font("Segoe UI", 23, FontStyle.Regular, GraphicsUnit.Pixel);
            public readonly Font Jeu = new Font("Segoe UI Semibold", 24, FontStyle.Regular, GraphicsUnit.Pixel);
            public readonly Font Petite = new Font("Segoe UI", 20, FontStyle.Regular, GraphicsUnit.Pixel);
            public void Dispose() { foreach (Font f in new[] { Chiffre, Etiquette, Texte, Fait, Jeu, Petite }) f.Dispose(); }
        }
    }
}
