using System;
using System.IO;

namespace BTOptimizer
{
    /// <summary>Interrupteur « coupe internet » : quand le fichier-drapeau existe, la recherche web
    /// du Copilote est coupée et il reste 100 % hors-ligne.</summary>
    internal static class WebPrefs
    {
        private static string WebOffPath
        {
            get { return AppPaths.File("bt-web-off.txt"); }
        }
        public static bool WebOff() { try { return File.Exists(WebOffPath); } catch { return false; } }
        public static void SetWebEnabled(bool on)
        {
            try
            {
                if (on) { if (File.Exists(WebOffPath)) File.Delete(WebOffPath); }
                else File.WriteAllText(WebOffPath, "Recherche internet coupée par l'utilisateur — le Copilote reste 100 % hors-ligne.\n");
            }
            catch { }
        }
    }
}
