using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace BTOptimizer
{
    // ----------------------------------------------------------------------
    //  Filtre couleur global (façon "digital vibrance") via la rampe gamma
    //  GDI de l'écran principal. 100 % réversible (retour rampe linéaire),
    //  fonctionne sur tous les GPU. Réglages persistés dans bt-colorfilter.txt
    //  et ré-appliqués au démarrage (comme le reapply_color_filter de l'app
    //  d'origine).
    // ----------------------------------------------------------------------
    internal static class ColorFilter
    {
        [DllImport("gdi32.dll")]
        private static extern bool SetDeviceGammaRamp(IntPtr hDC, ref RAMP lpRamp);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [StructLayout(LayoutKind.Sequential)]
        private struct RAMP
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256 * 3)]
            public ushort[] Table;
        }

        /// <summary>Presets (index 0 = neutre/désactivé).</summary>
        public static readonly string[] PresetNames =
        {
            "Neutre (désactivé)",
            "Vif (vibrance)",
            "eSport (net & saturé)",
            "Chaud",
            "Froid",
            "Nuit (moins de bleu)",
            "Contraste +"
        };

        // Par preset : contraste, gamma R/V/B, gain R/V/B (à pleine intensité).
        private struct Preset { public double Contrast, Gr, Gg, Gb, Kr, Kg, Kb; }

        private static Preset P(double c, double gr, double gg, double gb, double kr, double kg, double kb)
        {
            Preset p; p.Contrast = c; p.Gr = gr; p.Gg = gg; p.Gb = gb; p.Kr = kr; p.Kg = kg; p.Kb = kb; return p;
        }

        private static Preset ForIndex(int index)
        {
            switch (index)
            {
                case 1: return P(1.16, 0.92, 0.92, 0.92, 1.04, 1.02, 1.00); // Vif
                case 2: return P(1.28, 0.88, 0.88, 0.90, 1.06, 1.02, 0.98); // eSport
                case 3: return P(1.06, 0.95, 0.98, 1.06, 1.06, 1.00, 0.92); // Chaud
                case 4: return P(1.06, 1.06, 0.99, 0.94, 0.94, 1.00, 1.07); // Froid
                case 5: return P(1.02, 1.02, 1.00, 1.14, 1.02, 0.99, 0.80); // Nuit
                case 6: return P(1.34, 1.00, 1.00, 1.00, 1.00, 1.00, 1.00); // Contraste +
                default: return P(1.00, 1.00, 1.00, 1.00, 1.00, 1.00, 1.00); // Neutre
            }
        }

        private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        /// <summary>Applique le filtre. value 0..100 = intensité ; vivid = boost de contraste.</summary>
        public static bool Apply(int index, int value, bool vivid)
        {
            if (index <= 0) return Disable();
            double t = value; if (t < 0) t = 0; if (t > 100) t = 100; t /= 100.0;
            Preset ps = ForIndex(index);
            double contrast = ps.Contrast * (vivid ? 1.12 : 1.0);

            var ramp = new RAMP { Table = new ushort[256 * 3] };
            for (int i = 0; i < 256; i++)
            {
                double x = i / 255.0;
                ramp.Table[i]       = Channel(x, contrast, ps.Gr, ps.Kr, t); // Rouge
                ramp.Table[256 + i] = Channel(x, contrast, ps.Gg, ps.Kg, t); // Vert
                ramp.Table[512 + i] = Channel(x, contrast, ps.Gb, ps.Kb, t); // Bleu
            }
            return SetRamp(ref ramp);
        }

        private static ushort Channel(double x, double contrast, double gamma, double gain, double t)
        {
            // Contraste autour de 0.5, correction gamma, gain de canal.
            double y = (x - 0.5) * contrast + 0.5;
            y = Clamp01(y);
            if (gamma > 0 && Math.Abs(gamma - 1.0) > 1e-6) y = Math.Pow(y, 1.0 / gamma);
            y *= gain;
            y = Clamp01(y);
            // Fondu vers l'identité selon l'intensité t.
            double outv = x * (1.0 - t) + y * t;
            int v = (int)Math.Round(Clamp01(outv) * 65535.0);
            if (v < 0) v = 0; if (v > 65535) v = 65535;
            return (ushort)v;
        }

        /// <summary>Rétablit la rampe linéaire (filtre off).</summary>
        public static bool Disable()
        {
            var ramp = new RAMP { Table = new ushort[256 * 3] };
            for (int i = 0; i < 256; i++)
            {
                ushort v = (ushort)(i * 257); // 0..65535 linéaire
                ramp.Table[i] = v; ramp.Table[256 + i] = v; ramp.Table[512 + i] = v;
            }
            return SetRamp(ref ramp);
        }

        private static bool SetRamp(ref RAMP ramp)
        {
            IntPtr hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return false;
            try { return SetDeviceGammaRamp(hdc, ref ramp); }
            catch { return false; }
            finally { ReleaseDC(IntPtr.Zero, hdc); }
        }

        // ---- Persistance : bt-colorfilter.txt = "index;value;vivid" ----
        public static string ConfigPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-colorfilter.txt"); }
        }

        public static void Save(int index, int value, bool vivid)
        {
            try
            {
                File.WriteAllText(ConfigPath,
                    index.ToString(CultureInfo.InvariantCulture) + ";" +
                    value.ToString(CultureInfo.InvariantCulture) + ";" +
                    (vivid ? "1" : "0"));
            }
            catch { }
        }

        public static bool Load(out int index, out int value, out bool vivid)
        {
            index = 0; value = 50; vivid = false;
            try
            {
                if (!File.Exists(ConfigPath)) return false;
                string[] p = File.ReadAllText(ConfigPath).Trim().Split(';');
                if (p.Length >= 1) int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
                if (p.Length >= 2) int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                if (p.Length >= 3) vivid = p[2] == "1";
                return true;
            }
            catch { return false; }
        }

        /// <summary>Ré-applique le dernier filtre au démarrage de l'app.</summary>
        public static void ReapplyOnStartup(Action<string, int> log)
        {
            int index, value; bool vivid;
            if (!Load(out index, out value, out vivid) || index <= 0) return;
            try
            {
                if (Apply(index, value, vivid) && log != null)
                    log("Filtre couleur ré-appliqué : " + PresetNames[Math.Min(index, PresetNames.Length - 1)] + ".", 0);
            }
            catch { }
        }
    }
}
