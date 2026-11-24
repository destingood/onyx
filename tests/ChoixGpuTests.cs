using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="ChoixGpu"/>.
    ///
    /// Le premier jeu d'essai est le relevé réel de la machine de référence (Ryzen 9 9950X3D +
    /// RTX 4080 SUPER, 24/11/2026) : LibreHardwareMonitor liste d'abord la puce Radeon intégrée
    /// au processeur, sans aucun capteur, puis la RTX. ONYX prenait la première, ne lisait donc
    /// rien, et se rabattait sur nvidia-smi.exe à chaque rafraîchissement du tableau de bord :
    /// vingt processus lancés par minute, relevés dans la trace noyau.
    /// </summary>
    internal static class ChoixGpuTests
    {
        private static GpuReading Integree()
        {
            return new GpuReading { Ok = true, Name = "AMD Radeon(TM) Graphics" };
        }

        private static GpuReading Rtx()
        {
            return new GpuReading
            {
                Ok = true, Name = "NVIDIA GeForce RTX 4080 SUPER",
                TempC = 53, LoadPct = 1, CoreMhz = 210, MemMhz = 11501.952, PowerW = 43.729,
                VramUsedMB = 2141, VramTotalMB = 16376
            };
        }

        private static string Nom(GpuReading r) { return r == null ? null : r.Name; }

        public static void Tout()
        {
            Banc.Titre("ChoixGpu — quel GPU le moniteur doit lire");

            Banc.Egal("puce intégrée sans capteur listée avant la RTX : c'est la RTX qui est lue",
                "NVIDIA GeForce RTX 4080 SUPER",
                Nom(ChoixGpu.Meilleur(new List<GpuReading> { Integree(), Rtx() })));

            Banc.Egal("même résultat quand la RTX est listée en premier",
                "NVIDIA GeForce RTX 4080 SUPER",
                Nom(ChoixGpu.Meilleur(new List<GpuReading> { Rtx(), Integree() })));

            Banc.Egal("un seul GPU : c'est lui, même sans capteur",
                "AMD Radeon(TM) Graphics",
                Nom(ChoixGpu.Meilleur(new List<GpuReading> { Integree() })));

            Banc.Verifie("aucun GPU : rien à lire", true, ChoixGpu.Meilleur(new List<GpuReading>()) == null);
            Banc.Verifie("liste absente : rien à lire", true, ChoixGpu.Meilleur(null) == null);

            // Processus élevé : la puce intégrée peut publier elle aussi température et fréquence.
            // La mémoire vidéo départage alors : la carte dédiée en a bien plus.
            var integreeComplete = new GpuReading
            {
                Ok = true, Name = "AMD Radeon(TM) Graphics",
                TempC = 48, CoreMhz = 600, VramUsedMB = 300, VramTotalMB = 512
            };
            Banc.Egal("deux GPU aux capteurs complets : celui qui a le plus de mémoire vidéo",
                "NVIDIA GeForce RTX 4080 SUPER",
                Nom(ChoixGpu.Meilleur(new List<GpuReading> { integreeComplete, Rtx() })));

            var temperatureSeule = new GpuReading { Ok = true, Name = "Carte B", TempC = 60 };
            Banc.Egal("une température seule vaut mieux qu'aucun capteur",
                "Carte B",
                Nom(ChoixGpu.Meilleur(new List<GpuReading> { Integree(), temperatureSeule })));

            var jumelleA = Rtx(); jumelleA.Name = "Carte A";
            var jumelleB = Rtx(); jumelleB.Name = "Carte B";
            Banc.Egal("égalité parfaite : la première listée (ordre stable)",
                "Carte A",
                Nom(ChoixGpu.Meilleur(new List<GpuReading> { jumelleA, jumelleB })));

            Banc.Egal("une entrée vide dans la liste est ignorée",
                "NVIDIA GeForce RTX 4080 SUPER",
                Nom(ChoixGpu.Meilleur(new List<GpuReading> { null, Rtx() })));
        }
    }
}
