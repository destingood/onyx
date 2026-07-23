using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
// GDI : rend les polices embarquées visibles à TextRenderer (GDI) — souvent là où GDI+ échoue.

namespace BTOptimizer
{
    /// <summary>
    /// Charge les polices OFFICIELLES de FPS Doctor embarquées (Ubuntu = titres/nav, Inter = corps,
    /// Garet = display/logo) via PrivateFontCollection (AddMemoryFont). Repli automatique sur Segoe UI
    /// si une police manque. Les blocs mémoire restent vivants pour la durée de l'app (requis par GDI+).
    /// </summary>
    internal static class Fonts
    {
        [DllImport("gdi32.dll")] private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);

        private static readonly PrivateFontCollection _pfc = new PrivateFontCollection();
        private static readonly List<IntPtr> _mem = new List<IntPtr>();
        private static readonly List<string> _log = new List<string>();
        private static FontFamily _ubuntu, _ubuntuMed, _inter, _garet;
        private static bool _init;

        private static void Init()
        {
            if (_init) return; _init = true;
            // Une SEULE face par style : Inter-Medium/SemiBold se nomment tous « Inter Regular »
            // (doublons qui font échouer AddMemoryFont pour toute la famille) → on ne charge que Regular.
            foreach (var f in new[] { "Ubuntu-Regular.ttf", "Ubuntu-Medium.ttf", "Ubuntu-Bold.ttf",
                                      "Inter-Regular.ttf", "Garet-Regular.otf" })
                Load(f);
            _ubuntu = Find("Ubuntu", true) ?? Installed("Ubuntu");        // « Ubuntu » exact (Regular + Bold)
            _ubuntuMed = Find("Ubuntu Medium", true) ?? Installed("Ubuntu Medium");
            _inter = Find("Inter", false) ?? Installed("Inter");
            _garet = Find("Garet", false) ?? Installed("FSP DEMO - Garet") ?? Installed("Garet"); // « FSP DEMO - Garet »
        }

        private static void Load(string name)
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                {
                    if (s == null) { _log.Add(name + "=absent"); return; }
                    var data = new byte[s.Length];
                    int off = 0, r; while (off < data.Length && (r = s.Read(data, off, data.Length - off)) > 0) off += r;
                    IntPtr p = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, p, data.Length);
                    try { _pfc.AddMemoryFont(p, data.Length); } catch { }      // voie GDI+
                    uint cnt = 0; AddFontMemResourceEx(p, (uint)data.Length, IntPtr.Zero, ref cnt);  // voie GDI (TextRenderer)
                    _mem.Add(p);   // ne jamais libérer : GDI/GDI+ lisent ce bloc à la demande
                    _log.Add(name + "=ok");
                }
            }
            catch (Exception ex) { _log.Add(name + "=ERR:" + ex.GetType().Name); }
        }

        // Préfère une famille au nom EXACT (ex. « Ubuntu »), sinon la première qui contient le terme.
        private static FontFamily Find(string name, bool preferExact)
        {
            try
            {
                if (preferExact) foreach (var f in _pfc.Families) if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
                foreach (var f in _pfc.Families) if (f.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return f;
            }
            catch { }
            return null;
        }

        // Famille visible côté GDI/GDI+ par son nom (les fonts AddFontMemResourceEx y apparaissent).
        private static FontFamily Installed(string name)
        {
            try { return new FontFamily(name); } catch { return null; }
        }

        public static FontFamily Ubuntu { get { Init(); return _ubuntu; } }
        public static FontFamily UbuntuMedium { get { Init(); return _ubuntuMed ?? _ubuntu; } }
        public static FontFamily Inter { get { Init(); return _inter; } }
        public static FontFamily Garet { get { Init(); return _garet; } }

        /// <summary>Fabrique une Font à partir d'une famille embarquée, avec repli robuste sur Segoe UI.</summary>
        public static Font Make(FontFamily fam, float size, FontStyle style, string fallback)
        {
            if (fam != null)
            {
                try { if (fam.IsStyleAvailable(style)) return new Font(fam, size, style); } catch { }
                try { return new Font(fam, size, FontStyle.Regular); } catch { }
            }
            try { return new Font(fallback, size, style); } catch { }
            return new Font("Segoe UI", size);
        }

        /// <summary>Noms de familles chargées (diagnostic).</summary>
        public static string Diagnostic()
        {
            Init();
            return "Polices résolues : Ubuntu=" + (_ubuntu != null ? _ubuntu.Name : "NON") +
                   " · Inter=" + (_inter != null ? _inter.Name : "NON") +
                   " · Garet=" + (_garet != null ? _garet.Name : "NON") +
                   "  [" + string.Join(",", _log) + "]";
        }
    }
}
