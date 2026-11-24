using System.Collections.Generic;

namespace BTOptimizer
{
    /// <summary>Relevé GPU complet (tous constructeurs), lu nativement.</summary>
    internal class GpuReading
    {
        public bool Ok;
        public string Name;
        public double TempC = double.NaN, LoadPct = double.NaN, CoreMhz = double.NaN, MemMhz = double.NaN, PowerW = double.NaN;
        public long VramUsedMB = -1, VramTotalMB = -1;
    }

    /// <summary>
    /// Choisit, parmi les GPU relevés, celui que le moniteur affiche.
    ///
    /// Un PC de jeu en a souvent deux : la carte dédiée et la puce intégrée au processeur
    /// (tous les Ryzen 7000/9000, la plupart des Intel). L'ordre de la liste ne dit pas laquelle
    /// est la carte de jeu : sur la machine de référence la puce intégrée, sans aucun capteur,
    /// sort en premier. Prendre « la première » revenait à ne rien lire, donc à relancer
    /// nvidia-smi.exe à chaque rafraîchissement.
    /// </summary>
    internal static class ChoixGpu
    {
        /// <summary>Le GPU aux capteurs les plus complets ; à égalité, celui qui a le plus de
        /// mémoire vidéo (la carte dédiée) ; à égalité encore, le premier listé. null si aucun.</summary>
        public static GpuReading Meilleur(IList<GpuReading> gpus)
        {
            GpuReading choisi = null;
            if (gpus == null) return null;
            foreach (GpuReading g in gpus)
            {
                if (g == null) continue;
                if (choisi == null
                    || Capteurs(g) > Capteurs(choisi)
                    || (Capteurs(g) == Capteurs(choisi) && g.VramTotalMB > choisi.VramTotalMB))
                    choisi = g;
            }
            return choisi;
        }

        /// <summary>2 : température ET fréquence (ce que le moniteur exige pour se passer de
        /// nvidia-smi) ; 1 : température seule ; 0 : rien.</summary>
        private static int Capteurs(GpuReading g)
        {
            if (double.IsNaN(g.TempC)) return 0;
            return double.IsNaN(g.CoreMhz) ? 1 : 2;
        }
    }
}
