using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// CAPTURE D'ÉCRAN EN JEU — branchement : copie de l'écran où se trouve la fenêtre au premier
    /// plan (rien n'est injecté dans le jeu), PNG enregistré en fond dans le dossier Captures de
    /// Windows, nommé d'après le jeu en cours. Un jeu en plein écran exclusif peut donner une
    /// image noire : le mode « plein écran fenêtré » (le plus courant aujourd'hui) passe.
    /// </summary>
    internal static partial class CaptureEcran
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

        private static int _enCours;

        /// <summary>Copie l'écran tout de suite (fil d'interface), puis encode et enregistre en fond ;
        /// <paramref name="fini"/> reçoit le chemin, ou null et le message d'erreur.</summary>
        public static void Prend(Action<string, string> fini)
        {
            if (System.Threading.Interlocked.Exchange(ref _enCours, 1) == 1) return;   // une à la fois
            Bitmap img = null;
            string jeu = null;
            try
            {
                Screen ecran = Screen.FromHandle(GetForegroundWindow());
                Rectangle r = ecran.Bounds;
                img = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(img)) g.CopyFromScreen(r.Location, Point.Empty, r.Size, CopyPixelOperation.SourceCopy);
                jeu = JournalJeu.JeuEnCours;
            }
            catch (Exception ex)
            {
                if (img != null) img.Dispose();
                System.Threading.Interlocked.Exchange(ref _enCours, 0);
                if (fini != null) fini(null, ex.Message);
                return;
            }
            DateTime quand = DateTime.Now;
            Task.Run(() =>
            {
                string chemin = null, erreur = null;
                try
                {
                    string dossier = Clips.DossierCaptures();
                    Directory.CreateDirectory(dossier);
                    string nom = Libre(NomFichier(jeu, quand), n => File.Exists(Path.Combine(dossier, n)));
                    chemin = Path.Combine(dossier, nom);
                    img.Save(chemin, ImageFormat.Png);
                }
                catch (Exception ex) { chemin = null; erreur = ex.Message; }
                finally { img.Dispose(); System.Threading.Interlocked.Exchange(ref _enCours, 0); }
                if (fini != null) try { fini(chemin, erreur); } catch { }
            });
        }
    }
}
