using System.Globalization;
using BTOptimizer;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Compteur de FPS : Windows ne livre pas les images une par une mais par paquets (au pire une
    /// fois par seconde). Le banc rejoue cette livraison avec un temps simulé et vérifie que le
    /// chiffre affiché reste celui du jeu, quel que soit l'instant du relevé.
    /// </summary>
    internal static class FpsEtwTests
    {
        private const int Jeu = 999001, Autre = 999002;

        private static double Lit(FpsEtw f, int pid)
        {
            foreach (FpsEtw.ProcStat p in f.Snapshot(1000)) if (p.Pid == pid) return p.Fps;
            return 0;
        }

        /// <summary>Une minute de jeu à <paramref name="fps"/>, livrée par paquets de
        /// <paramref name="paquetMs"/>, relevée toutes les <paramref name="releveMs"/>.</summary>
        private static void Rejoue(double fps, double paquetMs, double releveMs, out double min, out double max)
        {
            var f = new FpsEtw(); double mur = 0; f.Horloge = () => mur;
            double pas = 1000.0 / fps, prochaine = 0, livre = 0, releve = 3005;
            min = double.MaxValue; max = 0;
            while (releve < 60000)
            {
                while (livre + paquetMs <= releve)
                {
                    livre += paquetMs; mur = livre + 2;   // le paquet arrive 2 ms après sa dernière image
                    while (prochaine <= livre) { f.Recoit(Jeu, prochaine); prochaine += pas; }
                }
                mur = releve;
                double v = Lit(f, Jeu);
                if (v < min) min = v;
                if (v > max) max = v;
                releve += releveMs;
            }
        }

        private static void Stable(string quoi, double fps, double paquetMs, double releveMs)
        {
            double min, max; Rejoue(fps, paquetMs, releveMs, out min, out max);
            double marge = System.Math.Max(1.0, fps * 0.015);   // une fenêtre d'une seconde tient une image de plus ou de moins
            Banc.Verifie(quoi + " (lu : " + min.ToString("0.#", CultureInfo.InvariantCulture) + " à " + max.ToString("0.#", CultureInfo.InvariantCulture) + ")",
                true, min >= fps - marge && max <= fps + marge);
        }

        public static void Tout()
        {
            Banc.Titre("Compteur de FPS (livraison par paquets)");
            Stable("144 FPS, paquets d'une seconde, relevé toutes les 1010 ms", 144, 1000, 1010);
            Stable("60 FPS, paquets d'une seconde, relevé toutes les 1015 ms", 60, 1000, 1015);
            Stable("500 FPS, paquets de 640 ms (tampon plein), relevé toutes les secondes", 500, 640, 1000);
            Stable("144 FPS, images livrées presque une par une", 144, 7, 1000);

            // Un jeu qui cesse de présenter ne garde pas son dernier chiffre.
            var f = new FpsEtw(); double mur = 5000; f.Horloge = () => mur;
            for (double t = 0; t <= 5000; t += 1000.0 / 144) f.Recoit(Jeu, t);
            mur = 5002;
            Banc.Verifie("jeu en cours : 144 FPS", true, System.Math.Abs(Lit(f, Jeu) - 144) <= 2);
            mur = 5000 + 2700;
            Banc.Verifie("jeu arrêté depuis 2,7 s : plus de chiffre", true, Lit(f, Jeu) == 0);

            // Une autre application continue de présenter : le jeu figé retombe tout de suite.
            f = new FpsEtw(); mur = 6100; f.Horloge = () => mur;
            for (double t = 0; t <= 5000; t += 1000.0 / 144) f.Recoit(Jeu, t);
            for (double t = 4000; t <= 6100; t += 1000.0 / 60) f.Recoit(Autre, t);
            mur = 6102;
            Banc.Verifie("jeu figé, autre application active : 0 pour le jeu", true, Lit(f, Jeu) == 0);
            Banc.Verifie("l'autre application garde son chiffre", true, System.Math.Abs(Lit(f, Autre) - 60) <= 1);

            Banc.Verifie("la fenêtre finit sur la dernière image livrée", true,
                CatalogueTests.Source("FpsEtwCalcul.cs").Contains("FinFenetre(_frontiere, maintenant) - fenetre"));
            Banc.Verifie("fenêtre : dernière image livrée si elle est récente", true, FpsEtw.FinFenetre(5000, 5900) == 5000);
            Banc.Verifie("fenêtre : repart avec l'horloge quand plus rien n'arrive", true, FpsEtw.FinFenetre(5000, 9000) == 9000 - FpsEtw.RetardLivraisonMaxMs);
            Banc.Verifie("fenêtre : aucune image reçue", true, FpsEtw.FinFenetre(-1, 1234) == 1234);
        }
    }
}
