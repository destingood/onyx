using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BTOptimizer
{
    /// <summary>
    /// « Flux » — la mascotte du Copilote. Un orbe dont la BOUCHE est la courbe de
    /// frametime du logo : plate quand le PC va bien, en dents de scie quand il y a
    /// un problème. Marque, logo et mascotte racontent donc la même chose.
    /// Dessinée en vectoriel (aucune image embarquée) → nette à toute taille.
    /// </summary>
    internal static class Mascot
    {
        public enum Mood
        {
            Calm,       // frametime plate : tout va bien
            Thinking,   // trois points : il réfléchit / mesure
            Alert       // dents de scie : il a repéré quelque chose
        }

        /// <summary>Humeur courante, dérivée du dernier bilan de santé connu. Les bulles du
        /// chat la lisent au moment de se peindre (voir <see cref="MoodForHealth"/>).</summary>
        public static Mood CurrentMood = Mood.Calm;

        /// <summary>Santé &lt; 0 = pas encore mesurée → il réfléchit ; &lt; 60 = il alerte.
        /// Mêmes seuils que l'anneau du QG, pour que l'app tienne un discours cohérent.</summary>
        public static Mood MoodForHealth(int health)
        {
            if (health < 0) return Mood.Thinking;
            return health < 60 ? Mood.Alert : Mood.Calm;
        }

        /// <summary>Dessine Flux dans 'box'. 'accent' pilote le trait (indigo Fluide).</summary>
        public static void Draw(Graphics g, RectangleF box, Color accent, Mood mood)
        {
            SmoothingMode sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float w = box.Width, h = box.Height;
            float stroke = Math.Max(1.2f, w * 0.055f);

            // Corps : carré aux angles très arrondis (même famille que la plaque du logo).
            var body = new RectangleF(box.X + stroke, box.Y + stroke, w - stroke * 2, h - stroke * 2);
            using (var path = Round(body, Math.Min(body.Width, body.Height) * 0.32f))
            using (var fill = new SolidBrush(Color.FromArgb(20, 20, 31)))
            {
                g.FillPath(fill, path);
                using (var pen = new Pen(accent, stroke)) g.DrawPath(pen, path);
            }

            // Yeux.
            float eyeR = Math.Max(1f, w * 0.055f);
            float eyeY = box.Y + h * 0.38f;
            using (var br = new SolidBrush(accent))
            {
                g.FillEllipse(br, box.X + w * 0.34f - eyeR, eyeY - eyeR, eyeR * 2, eyeR * 2);
                g.FillEllipse(br, box.X + w * 0.66f - eyeR, eyeY - eyeR, eyeR * 2, eyeR * 2);
            }

            // Bouche = la frametime.
            float my = box.Y + h * 0.64f;
            float x0 = box.X + w * 0.26f, x1 = box.X + w * 0.70f;
            float mouth = Math.Max(1.2f, w * 0.045f);
            using (var pen = new Pen(accent, mouth))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                using (var dot = new SolidBrush(accent))
                {
                    if (mood == Mood.Calm)
                    {
                        g.DrawLine(pen, x0, my, x1, my);
                        float r = mouth * 0.8f;
                        g.FillEllipse(dot, x1 - r, my - r, r * 2, r * 2);   // point de mesure, comme le logo
                    }
                    else if (mood == Mood.Thinking)
                    {
                        float r = mouth * 0.75f;
                        for (int i = 0; i < 3; i++)
                        {
                            float cx = x0 + (x1 - x0) * (0.12f + 0.38f * i);
                            g.FillEllipse(dot, cx - r, my - r, r * 2, r * 2);
                        }
                    }
                    else
                    {
                        float a = h * 0.075f;   // amplitude des pics
                        var pts = new[]
                        {
                            new PointF(x0, my),
                            new PointF(x0 + (x1 - x0) * 0.18f, my - a),
                            new PointF(x0 + (x1 - x0) * 0.36f, my + a),
                            new PointF(x0 + (x1 - x0) * 0.54f, my - a * 0.7f),
                            new PointF(x0 + (x1 - x0) * 0.72f, my + a * 0.5f),
                            new PointF(x1, my)
                        };
                        g.DrawLines(pen, pts);
                    }
                }
            }

            g.SmoothingMode = sm;
        }

        private static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
