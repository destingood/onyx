using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BTOptimizer
{
    /// <summary>
    /// Illustration de repli d'un jeu : son icône HAUTE RÉSOLUTION, extraite de son propre
    /// exécutable. Sert aux jeux sans jaquette Steam (World of Warcraft, Hearthstone, exclusivités
    /// Battle.net/EA/Epic…). 100 % local : aucun réseau, aucune clé d'API.
    ///
    /// Deux pièges contournés ici :
    ///  • Icon.ExtractAssociatedIcon plafonne à 32×32 → on passe par PrivateExtractIcons (Win32)
    ///    qui rend le 256×256 réellement embarqué (vérifié sur Wow.exe, bf6.exe, Hearthstone).
    ///  • Icon.FromHandle NE possède PAS le handle : convertir puis DestroyIcon peut rendre un
    ///    bitmap corrompu (carte grise) ou nul. On clone donc l'icône avant conversion.
    ///
    /// Le résultat est écrit en PNG dans bt-gamecache (alpha préservé) : relances instantanées.
    /// </summary>
    internal static class GameIcon
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int PrivateExtractIcons(string file, int index, int cx, int cy,
                                                      IntPtr[] hIcons, int[] ids, int count, int flags);
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private static readonly int[] Sizes = { 256, 128, 64, 48, 32 };

        private static readonly Dictionary<string, Image> _cache =
            new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _pending =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Icône du jeu installé dans <paramref name="installPath"/>, ou null si pas (encore)
        /// disponible. <paramref name="onReady"/> est appelé quand l'icône devient disponible.
        /// </summary>
        public static Image For(string installPath, Action onReady)
        {
            if (string.IsNullOrEmpty(installPath)) return null;

            lock (_cache)
            {
                Image cached;
                if (_cache.TryGetValue(installPath, out cached)) return cached;
                if (_pending.Contains(installPath)) return null;
                _pending.Add(installPath);
            }

            string path = installPath;
            Task.Run(() =>
            {
                Image made = null;
                try { made = LoadFromDisk(path); } catch { }      // déjà extraite lors d'un lancement précédent

                if (made == null)
                {
                    try
                    {
                        string exe = GameLibrary.GuessMainExe(path);
                        if (exe != null) made = Extract(exe);
                    }
                    catch { }
                    if (made != null) SaveToDisk(path, made);
                }

                if (made == null) return;                         // pas d'icône : on garde le repli manette
                lock (_cache) _cache[path] = made;
                if (onReady != null) { try { onReady(); } catch { } }
            });

            return null;
        }

        /// <summary>Meilleure icône disponible dans cet exécutable (256 px en priorité).</summary>
        private static Image Extract(string exe)
        {
            foreach (int sz in Sizes)
            {
                var handles = new IntPtr[1];
                var ids = new int[1];
                int n = 0;
                try { n = PrivateExtractIcons(exe, 0, sz, sz, handles, ids, 1, 0); }
                catch { }
                if (n <= 0 || handles[0] == IntPtr.Zero) continue;

                try
                {
                    // Icon.FromHandle ne possède pas le handle : on CLONE pour obtenir une icône
                    // autonome, sinon le bitmap peut être invalidé par DestroyIcon juste après.
                    using (Icon borrowed = Icon.FromHandle(handles[0]))
                    using (Icon owned = (Icon)borrowed.Clone())
                        return owned.ToBitmap();
                }
                catch { }
                finally { try { DestroyIcon(handles[0]); } catch { } }
            }

            try { using (Icon ic = Icon.ExtractAssociatedIcon(exe)) if (ic != null) return ic.ToBitmap(); }
            catch { }
            return null;
        }

        // ------------------------------------------------------------ cache disque (PNG)
        private static string IconPath(string installPath)
        {
            string leaf = "";
            try { leaf = Path.GetFileName(installPath.TrimEnd('\\', '/')) ?? ""; } catch { }
            var sb = new StringBuilder();
            foreach (char c in leaf.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            if (sb.Length > 28) sb.Length = 28;

            uint h = 2166136261;                                   // FNV-1a : évite les collisions de noms
            foreach (char c in installPath.ToLowerInvariant()) { h ^= c; h *= 16777619; }
            return Path.Combine(AppContext.BaseDirectory, "bt-gamecache",
                                "ico-" + sb + "-" + h.ToString("x8") + ".png");
        }

        private static Image LoadFromDisk(string installPath)
        {
            string p = IconPath(installPath);
            if (!File.Exists(p)) return null;
            // On passe par les octets : Image.FromFile garderait le fichier verrouillé.
            byte[] bytes = File.ReadAllBytes(p);
            using (var ms = new MemoryStream(bytes)) return new Bitmap(Image.FromStream(ms));
        }

        private static void SaveToDisk(string installPath, Image img)
        {
            try
            {
                string p = IconPath(installPath);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                img.Save(p, ImageFormat.Png);                      // PNG : conserve la transparence
            }
            catch { }
        }
    }
}
