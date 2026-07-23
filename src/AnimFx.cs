using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Effets d'animation prêts à l'emploi, bâtis sur le moteur Anim :
    ///  • fenêtres de dialogue : fondu + légère montée à l'ouverture, fondu à la fermeture ;
    ///  • fenêtre principale : fondu à l'ouverture ;
    ///  • pages : cross-fade — un calque top-level (alpha DWM) montrant l'ANCIENNE page
    ///    (capturée via PrintWindow, seule méthode qui saisit le rendu double-buffered) s'efface
    ///    pour révéler la nouvelle. Aucun scintillement, repli instantané si la capture échoue.
    /// Tout est inerte quand les animations sont coupées (Anim.On == false).
    /// </summary>
    internal static class AnimFx
    {
        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        private const uint PW_RENDERFULLCONTENT = 2;

        // ---- Fenêtres de dialogue (toutes passent par DashboardForm.OpenDialog) ---------

        /// <summary>À appeler AVANT ShowDialog : fondu+montée à l'apparition, fondu à la fermeture.</summary>
        public static void HookDialog(Form f)
        {
            if (f == null) return;
            if (Anim.On) { try { f.Opacity = 0.0; } catch { } }   // évite un éclair pleine opacité avant le fondu
            f.Shown += delegate { FadeInRise(f); };
            bool[] closing = { false };
            f.FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                if (closing[0] || !Anim.On) return;
                if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing) return;
                closing[0] = true;
                e.Cancel = true;
                DialogResult keep = f.DialogResult;
                Anim.Tween(90, Ease.Linear,
                    delegate (float p) { try { f.Opacity = 1.0 - p; } catch { } },
                    delegate { try { f.DialogResult = keep; } catch { } try { f.Close(); } catch { } });
            };
        }

        public static void FadeInRise(Form f)
        {
            if (f == null) return;
            if (!Anim.On) { try { f.Opacity = 1.0; } catch { } return; }
            int y1 = f.Top;
            int y0 = y1 + 8;
            try { f.Opacity = 0.0; f.Top = y0; } catch { }
            Anim.Tween(150, Ease.OutCubic,
                delegate (float p) { try { f.Opacity = p; f.Top = y0 + (int)Math.Round((y1 - y0) * p); } catch { } },
                delegate { try { f.Opacity = 1.0; f.Top = y1; } catch { } });
        }

        /// <summary>Fenêtre principale : simple fondu (elle est déjà centrée, pas de déplacement).</summary>
        public static void FadeInForm(Form f)
        {
            if (f == null) return;
            if (!Anim.On) { try { f.Opacity = 1.0; } catch { } return; }
            try { f.Opacity = 0.0; } catch { }
            Anim.Tween(180, Ease.OutCubic,
                delegate (float p) { try { f.Opacity = p; } catch { } },
                delegate { try { f.Opacity = 1.0; } catch { } });
        }

        // ---- Pages : cross-fade par calque top-level ------------------------------------

        /// <summary>Fond enchaîné entre l'ancienne et la nouvelle page. `commit` réalise l'échange
        /// réel (rendre la nouvelle page visible) — il est exécuté SOUS le calque, donc invisible.</summary>
        public static void CrossFadePage(Panel host, Action commit)
        {
            Form form = host != null ? host.FindForm() : null;
            if (!Anim.On || form == null || host.ClientRectangle.Width < 4 || host.ClientRectangle.Height < 4)
            {
                if (commit != null) commit();
                return;
            }

            Bitmap snap = null;
            try { snap = CaptureRegion(form, host); } catch { snap = null; }
            if (snap == null) { if (commit != null) commit(); return; }   // repli : échange instantané

            OverlayWindow ov = null;
            try
            {
                ov = new OverlayWindow(snap);
                ov.Bounds = host.RectangleToScreen(host.ClientRectangle);
                ov.Show(form);          // montre l'ANCIENNE page par-dessus (identique à l'écran → transparent)
            }
            catch
            {
                if (ov != null) { try { ov.Dispose(); } catch { } }
                try { snap.Dispose(); } catch { }
                if (commit != null) commit();
                return;
            }

            if (commit != null) { try { commit(); } catch { } }   // échange réel, caché sous le calque
            try { form.Update(); } catch { }

            OverlayWindow ovf = ov; Bitmap snapf = snap;
            Anim.Tween(120, Ease.OutCubic,
                delegate (float p) { try { ovf.Opacity = 1.0 - p; } catch { } },
                delegate
                {
                    try { ovf.Close(); } catch { }
                    try { ovf.Dispose(); } catch { }
                    try { snapf.Dispose(); } catch { }
                });
        }

        private static Bitmap CaptureRegion(Form form, Control host)
        {
            Rectangle wr = form.Bounds;   // rectangle FENÊTRE (coords écran)
            if (wr.Width < 1 || wr.Height < 1) return null;
            Bitmap full = new Bitmap(wr.Width, wr.Height);
            try
            {
                using (var g = Graphics.FromImage(full))
                {
                    IntPtr hdc = g.GetHdc();
                    bool ok;
                    try { ok = PrintWindow(form.Handle, hdc, PW_RENDERFULLCONTENT); }
                    finally { g.ReleaseHdc(hdc); }
                    if (!ok) { full.Dispose(); return null; }
                }
                Rectangle hs = host.RectangleToScreen(host.ClientRectangle);
                int w = host.ClientRectangle.Width, h = host.ClientRectangle.Height;
                Bitmap outb = new Bitmap(w, h);
                using (var g = Graphics.FromImage(outb))
                {
                    g.Clear(FpsUi.BgMain);
                    g.DrawImage(full, new Rectangle(0, 0, w, h),
                        new Rectangle(hs.X - wr.X, hs.Y - wr.Y, w, h), GraphicsUnit.Pixel);
                }
                return outb;
            }
            finally { full.Dispose(); }
        }

        /// <summary>Fenêtre superposée sans bordure, sans focus, cliquable au travers, à alpha DWM.</summary>
        private sealed class OverlayWindow : Form
        {
            private readonly Bitmap _img;
            private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000;

            public OverlayWindow(Bitmap img)
            {
                _img = img;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                DoubleBuffered = true;
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get { CreateParams cp = base.CreateParams; cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED; return cp; }
            }

            protected override void OnPaintBackground(PaintEventArgs e) { }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (_img != null) e.Graphics.DrawImage(_img, 0, 0, Width, Height);
            }
        }
    }
}
