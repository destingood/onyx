using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Affiche des toasts « nouveau badge débloqué ! » empilés en bas à droite.</summary>
    internal static class BadgeToastManager
    {
        private static readonly List<BadgeToast> _active = new List<BadgeToast>();

        public static void Show(BadgeCatalog.Badge b)
        {
            if (b == null) return;
            if (_active.Count >= 3) return;   // évite un flot de toasts (ex. premier lancement) — le badge reste gagné
            if (_active.Count == 0) { try { System.Media.SystemSounds.Asterisk.Play(); } catch { } }   // son une fois par salve
            var t = new BadgeToast(b);
            t.FormClosed += (s, e) => { _active.Remove(t); Reflow(); };
            _active.Add(t);
            t.ShowToast();
            Reflow();
        }

        private static void Reflow()
        {
            try
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                int y = wa.Bottom - 16;
                for (int i = 0; i < _active.Count; i++)
                {
                    var t = _active[i];
                    if (t.IsDisposed) continue;
                    y -= t.Height;
                    t.Location = new Point(wa.Right - t.Width - 16, y);
                    y -= 8;
                }
            }
            catch { }
        }
    }

    internal class BadgeToast : Form
    {
        private readonly BadgeCatalog.Badge _b;
        private readonly Timer _timer = new Timer();
        private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;

        public BadgeToast(BadgeCatalog.Badge b)
        {
            _b = b;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(12, 14, 13);
            DoubleBuffered = true;
            ClientSize = new Size(390, 104);
            try
            {
                using (var p = new GraphicsPath())
                {
                    var r = ClientRectangle; int d = 24;
                    p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                    p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                    p.CloseFigure(); Region = new Region(p);
                }
            }
            catch { }
            _timer.Interval = 4800;
            _timer.Tick += (s, e) => FadeThenClose();
            Click += (s, e) => FadeThenClose();   // clic = fermer
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST; return cp; }
        }

        private bool _closing;

        public void ShowToast()
        {
            if (Anim.On) { try { Opacity = 0.0; } catch { } }
            Show();
            _timer.Start();
            if (Anim.On)
                Anim.Tween(180, Ease.OutCubic, delegate (float p) { try { Opacity = p; } catch { } },
                    delegate { try { Opacity = 1.0; } catch { } });
        }

        // Fermeture en fondu (fin du minuteur ou clic). Coupé => fermeture immédiate.
        private void FadeThenClose()
        {
            if (_closing) return;
            _closing = true;
            try { _timer.Stop(); } catch { }
            if (!Anim.On) { try { Close(); } catch { } return; }
            Anim.Tween(160, Ease.Linear, delegate (float p) { try { Opacity = 1.0 - p; } catch { } },
                delegate { try { Close(); } catch { } });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var r = ClientRectangle;
            Color tier = BadgeCatalog.TierColor[Math.Max(0, Math.Min(2, _b.Tier - 1))];

            OnyxDesign.PaintCard(g, r, FpsUi.Card, tier, 12);
            BadgeRender.DrawShape(g, 14, 18, 64, _b.Glyph, true, tier, _b.Shape);
            OnyxDesign.Text(g, "NOUVEAU BADGE · " + PageCollection.TierName(_b.Tier), FpsUi.Tiny, tier, new Rectangle(92, 14, 282, 18));
            OnyxDesign.Text(g, _b.Name, FpsUi.H2, FpsUi.Ink, new Rectangle(92, 35, 282, 28));
            OnyxDesign.Text(g, _b.Crit, FpsUi.Tiny, FpsUi.Dim, new Rectangle(92, 67, 282, 30), true);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { _timer.Stop(); _timer.Dispose(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
