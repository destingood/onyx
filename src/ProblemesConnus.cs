using System;

namespace BTOptimizer
{
    /// <summary>
    /// Problèmes connus et publiés : une mise à jour Windows précise qui, avec un pilote trop
    /// ancien, fait perdre des images. Logique pure, lecture seule. Chaque entrée se tait dès
    /// qu'une condition manque ou ne se lit pas. Pour en ajouter une, une ligne dans
    /// <see cref="Table"/> suffit, avec sa source dans le commentaire.
    /// </summary>
    internal static class ProblemesConnus
    {
        private sealed class Probleme
        {
            public int[] Builds;          // builds Windows concernés (26100 = 24H2, 26200 = 25H2)
            public int RevisionMin;       // première révision (UBR) qui porte la mise à jour
            public string Carte;          // texte cherché dans le nom de la carte graphique
            public int PiloteCorrige;     // première version NVIDIA corrigée, 581.94 = 58194
            public string Texte;
        }

        private static readonly Probleme[] Table =
        {
            // KB5066835, 14 octobre 2025 : builds 26100.6899 et 26200.6899. NVIDIA, note du
            // pilote correctif 581.94 (19 novembre 2025) : « Lower performance may be observed in
            // some games after updating to Windows 11 October 2025 KB5066835 ». Jusqu'à −50 %
            // relevé (Notebookcheck, TweakTown).
            new Probleme
            {
                Builds = new[] { 26100, 26200 }, RevisionMin = 6899, Carte = "NVIDIA", PiloteCorrige = 58194,
                Texte = "La mise à jour Windows d'octobre 2025 (KB5066835) fait chuter les FPS dans certains "
                      + "jeux avec un pilote NVIDIA antérieur au 581.94 (jusqu'à −50 % relevé dans un jeu). "
                      + "Installe le pilote NVIDIA 581.94 ou plus récent."
            },
        };

        /// <summary>Version NVIDIA lue dans la version Windows du pilote : les cinq derniers
        /// chiffres des deux derniers blocs (32.0.15.8194 → 58194, soit 581.94). −1 si illisible.</summary>
        public static int VersionNvidia(string versionWindows)
        {
            if (string.IsNullOrEmpty(versionWindows)) return -1;
            string[] p = versionWindows.Split('.');
            if (p.Length < 4) return -1;
            string chiffres = p[p.Length - 2] + p[p.Length - 1].PadLeft(4, '0');
            if (chiffres.Length < 5) return -1;
            int v;
            return int.TryParse(chiffres.Substring(chiffres.Length - 5), out v) ? v : -1;
        }

        /// <summary>
        /// Logiciel de carte mère qui coûte du temps noyau en arrière-plan, ou null.
        ///
        /// Gigabyte Control Center (GCC.exe) : sur la machine de test (carte Gigabyte B850,
        /// 24/11/2026), il interrogeait sans arrêt le contrôleur RGB par USB. Relevés LatencyMon de
        /// 60 s au bureau, avant puis après sa désinstallation : Wdf01000.sys de 8 358 à 367
        /// interruptions et de 51,53 ms à 6,10 ms. Un seul PC mesuré.
        ///
        /// Le nom seul ne suffit pas (« gcc.exe » est aussi le compilateur GNU) : il faut le chemin
        /// d'installation Gigabyte. Chemin illisible : on se tait.
        /// </summary>
        public static string LogicielCarteMere(System.Collections.Generic.IList<string> processus,
                                               System.Collections.Generic.IList<string> chemins)
        {
            if (processus == null || chemins == null) return null;
            for (int i = 0; i < processus.Count && i < chemins.Count; i++)
            {
                string nom = processus[i] ?? "";
                if (nom.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) nom = nom.Substring(0, nom.Length - 4);
                if (!nom.Equals("GCC", StringComparison.OrdinalIgnoreCase)) continue;
                if (chemins[i] == null || chemins[i].IndexOf(@"\GIGABYTE\", StringComparison.OrdinalIgnoreCase) < 0) continue;
                return "Gigabyte Control Center tourne en arrière-plan. Sur le PC de test, il interrogeait sans arrêt "
                     + "le contrôleur RGB de la carte mère par USB : après sa désinstallation, le temps noyau du "
                     + "pilote Wdf01000.sys (par lequel passe l'USB) est tombé de 51,5 ms à 6,1 ms sur 60 s au "
                     + "bureau (un seul PC mesuré). Ferme-le avant de jouer, ou désinstalle-le si tu ne t'en sers "
                     + "pas. ONYX ne le ferme pas à ta place.";
            }
            return null;
        }

        /// <summary>Premier problème connu qui s'applique, ou null.</summary>
        public static string Constat(int build, int ubr, string gpu, string versionPilote)
        {
            if (gpu == null) return null;
            int pilote = VersionNvidia(versionPilote);
            foreach (Probleme p in Table)
            {
                if (Array.IndexOf(p.Builds, build) < 0 || ubr < p.RevisionMin) continue;
                if (gpu.IndexOf(p.Carte, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (pilote < 0 || pilote >= p.PiloteCorrige) continue;
                return p.Texte;
            }
            return null;
        }
    }
}
