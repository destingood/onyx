using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// Constats matériels du diagnostic (logique pure, sans lecture machine). Chacun rend la phrase
    /// à afficher, ou null s'il n'y a rien d'honnête à dire. Aucun ne modifie quoi que ce soit.
    /// Sources et chiffres : docs/plan-optimisations-manquantes.md.
    /// </summary>
    internal static class ConstatsMateriel
    {
        /// <summary>Une seule barrette = simple canal, à coup sûr. Deux barrettes dans les mauvais
        /// emplacements ne se lisent pas de façon fiable depuis Windows : on n'en dit rien.
        /// <paramref name="emplacements"/> à 0 = inconnu ; à 1, il n'y a rien à corriger.</summary>
        public static string SimpleCanal(int barrettes, int emplacements, bool portable)
        {
            if (barrettes != 1 || emplacements == 1) return null;
            return "Une seule barrette de RAM détectée : la mémoire tourne en simple canal. Dans les tests "
                 + "publiés, une seconde barrette identique rend de 0 à 20 % de FPS selon le jeu, surtout "
                 + "sur les creux (1 % low)."
                 + (portable ? " Sur un portable, vérifie d'abord qu'une partie de la mémoire n'est pas soudée." : "");
        }

        // GeForce RTX 40 et 50 (4050 à 4090, 5050 à 5090, portables compris). Les cartes de station
        // de travail (RTX A4000, RTX 4000 Ada) ne sont volontairement pas reconnues.
        private static readonly Regex RtxGenImages = new Regex(@"\bRTX\s*[45]0[5-9]0\b", RegexOptions.IgnoreCase);

        /// <summary>Carte capable de génération d'images DLSS.</summary>
        public static bool CarteGenerationImages(string gpu)
        {
            return gpu != null && RtxGenImages.IsMatch(gpu);
        }

        /// <summary>La génération d'images DLSS exige la planification GPU matérielle (HAGS).
        /// <paramref name="hagsActif"/> null = état non lu : on n'affirme rien.</summary>
        public static string HagsManquant(string gpu, bool? hagsActif)
        {
            if (hagsActif != false || !CarteGenerationImages(gpu)) return null;
            return "Planification GPU matérielle (HAGS) désactivée : la génération d'images DLSS ne peut pas "
                 + "fonctionner sur ta " + gpu.Trim() + ". Active « HAGS » dans Optimisations (redémarrage requis).";
        }

        private static readonly Regex Ryzen = new Regex(@"Ryzen\s+\d\s+(?:PRO\s+)?(\d)\d(\d)\d([A-Z0-9]*)", RegexOptions.IgnoreCase);

        /// <summary>Ryzen 7000 ou 9000 d'architecture Zen 4 ou Zen 5. Les 7000 de portable
        /// recyclent d'anciennes puces : seul le troisième chiffre à 4 désigne un Zen 4
        /// (7840HS oui, 7735HS et 7520U non).</summary>
        public static bool RyzenZen4Ou5(string cpu)
        {
            if (cpu == null) return false;
            Match m = Ryzen.Match(cpu);
            if (!m.Success) return false;
            string serie = m.Groups[1].Value, suffixe = m.Groups[3].Value.ToUpperInvariant();
            if (serie == "9") return true;
            if (serie != "7") return false;
            bool bureau = suffixe == "" || suffixe == "X" || suffixe == "X3D" || suffixe == "F";
            return bureau || m.Groups[2].Value == "4";
        }

        // Windows 11 24H2. L'optimisation de prédiction de branche des Ryzen y est d'origine.
        private const int Build24H2 = 26100;
        // KB5041587 (août 2024) l'apporte à 22H2 (22621) et 23H2 (22631) à partir de la révision 4112.
        private const int RevisionKb5041587 = 4112;

        /// <summary>Windows 11 sans l'optimisation des Ryzen 7000/9000. Windows 10 n'est pas
        /// signalé : y remédier demande de changer de système, pas une mise à jour.</summary>
        public static string WindowsEnRetard(string cpu, int build, int ubr)
        {
            if (build < 22000 || build >= Build24H2 || !RyzenZen4Ou5(cpu)) return null;
            if ((build == 22621 || build == 22631) && ubr >= RevisionKb5041587) return null;
            return "Ton Windows 11 n'a pas l'optimisation de prédiction de branche des Ryzen 7000/9000 "
                 + "(d'origine dans la version 24H2, ou apportée par la mise à jour KB5041587 sur 22H2/23H2). "
                 + "Les tests publiés relèvent jusqu'à 10 % de FPS environ. Mets Windows à jour.";
        }

        // Ryzen 9 7900X3D, 7950X3D, 9900X3D, 9950X3D : V-Cache sur un seul des deux CCD. Une
        // variante au nom inconnu (suffixe après X3D) n'est pas reconnue : on n'extrapole pas.
        private static readonly Regex X3D2Ccd = new Regex(@"Ryzen\s+9\s+(?:PRO\s+)?[79]9[05]0X3D\b", RegexOptions.IgnoreCase);

        /// <summary>Ryzen X3D à deux CCD, où le pilote AMD gare pendant un jeu le CCD sans V-Cache.</summary>
        public static bool X3DDeuxCcd(string cpu)
        {
            return cpu != null && X3D2Ccd.IsMatch(cpu);
        }

        /// <summary>Réglage qui empêche ce parcage (AMD recommande le plan Équilibré) : jamais coché
        /// d'office sur ces processeurs, toujours cochable à la main. Le déparquage de base
        /// (cpu_unpark_cores) n'y figure pas : AMD gare les cœurs dans le profil du mode Jeu, pas
        /// par la valeur de base, qui vaut d'ailleurs déjà 100 % par défaut sur certains PC.</summary>
        public static readonly string[] ConflitsX3D = { "power_ultimate" };

        /// <summary>Ce qui empêche le pilote AMD de garer le CCD sans V-Cache pendant un jeu.
        /// <paramref name="pilote"/> null = non lu ; <paramref name="planBloquant"/> = nom du plan
        /// actif s'il est Performances ultimes ou élevées, sinon null.</summary>
        public static string X3DSansParcage(string cpu, bool? pilote, string planBloquant, bool modeJeuCoupe)
        {
            if (!X3DDeuxCcd(cpu)) return null;
            var causes = new System.Collections.Generic.List<string>();
            if (pilote == false) causes.Add("le pilote AMD 3D V-Cache est absent (installe le pilote chipset AMD)");
            if (planBloquant != null) causes.Add("le plan « " + planBloquant + " » est actif (AMD recommande le plan Équilibré)");
            if (modeJeuCoupe) causes.Add("le mode Jeu de Windows est coupé");
            if (causes.Count == 0) return null;
            return "Ryzen X3D à deux CCD : le pilote AMD ne peut pas réserver le CCD à V-Cache au jeu, car "
                 + string.Join(" ; ", causes) + ". Le jeu risque de s'étaler sur les deux CCD. "
                 + "Un profil de jeu ONYX en mode Automatique place déjà le jeu sur le CCD V-Cache.";
        }
    }
}
