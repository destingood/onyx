using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Mémorise la taille/position/état (maximisé) de la fenêtre principale entre deux lancements,
    /// dans bt-window.txt à côté de l'exe. Une position devenue hors-écran (moniteur débranché) est
    /// ignorée : on retombe alors sur le centrage par défaut plutôt que d'ouvrir une fenêtre injoignable.
    /// </summary>
    internal static class WindowBounds
    {
        private static string FilePath()
        {
            try { return Path.Combine(AppContext.BaseDirectory, "bt-window.txt"); } catch { return "bt-window.txt"; }
        }

        // Les harnais de test/capture imposent des tailles précises : ne pas restaurer/sauver alors.
        private static bool UnderTestHarness()
        {
            try
            {
                return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BT_UITEST"))
                    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BT_UISHOT"))
                    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BT_FORMSHOT"));
            }
            catch { return false; }
        }

        public static void Save(Form f)
        {
            if (UnderTestHarness()) return;
            try
            {
                bool max = f.WindowState == FormWindowState.Maximized;
                Rectangle r = f.WindowState == FormWindowState.Normal ? f.Bounds : f.RestoreBounds;
                if (r.Width <= 0 || r.Height <= 0) return;
                File.WriteAllText(FilePath(), r.X + "," + r.Y + "," + r.Width + "," + r.Height + "," + (max ? 1 : 0));
            }
            catch { }
        }

        public static void Restore(Form f)
        {
            if (UnderTestHarness()) return;
            try
            {
                string p = FilePath();
                if (!File.Exists(p)) return;
                var parts = File.ReadAllText(p).Trim().Split(',');
                if (parts.Length < 4) return;
                int x, y, w, h;
                if (!int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y)
                    || !int.TryParse(parts[2], out w) || !int.TryParse(parts[3], out h)) return;
                bool max = parts.Length >= 5 && parts[4] == "1";

                if (f.MinimumSize.Width > 0) w = Math.Max(w, f.MinimumSize.Width);
                if (f.MinimumSize.Height > 0) h = Math.Max(h, f.MinimumSize.Height);

                var want = new Rectangle(x, y, w, h);
                if (!TitleBarGrabbable(want)) return;   // barre de titre injoignable → on garde le défaut

                f.StartPosition = FormStartPosition.Manual;
                f.Bounds = want;
                if (max) f.WindowState = FormWindowState.Maximized;
            }
            catch { }
        }

        // Vrai si le milieu de la barre de titre tombe sur la zone de travail d'un écran branché
        // (donc l'utilisateur peut attraper et déplacer la fenêtre).
        private static bool TitleBarGrabbable(Rectangle r)
        {
            try
            {
                Point grab = new Point(r.X + r.Width / 2, r.Y + 15);
                foreach (var sc in Screen.AllScreens)
                    if (sc.WorkingArea.Contains(grab)) return true;
                return false;
            }
            catch { return true; }   // en cas de doute, on autorise
        }
    }
}
