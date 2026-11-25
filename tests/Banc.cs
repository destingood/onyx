using System;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Le banc : compte les assertions et fixe le code de sortie.
    ///
    /// Volontairement minuscule. Un module d'analyse d'ONYX est écrit pour être PUR — même
    /// entrée, même sortie, aucune machine à interroger — et une fonction pure se vérifie avec
    /// une égalité. Le jour où il faudra des fixtures, des mocks et un runner, ce sera le signe
    /// qu'un module a cessé d'être pur, pas que ce banc est trop pauvre.
    /// </summary>
    internal static class Banc
    {
        private static int _ok, _ko;

        public static void Titre(string t)
        {
            Console.WriteLine();
            Console.WriteLine("=== " + t + " ===");
        }

        /// <summary>Une assertion. Le libellé décrit le COMPORTEMENT attendu, pas l'appel :
        /// c'est lui qu'on lira le jour où le test tombera.</summary>
        public static void Verifie(string quoi, bool attendu, bool obtenu)
        {
            if (attendu == obtenu) { _ok++; Console.WriteLine("  [OK]  " + quoi); }
            else
            {
                _ko++;
                Console.WriteLine("  [KO]  " + quoi + "   (attendu " + attendu
                    + ", obtenu " + obtenu + ")");
            }
        }

        public static void Egal(string quoi, string attendu, string obtenu)
        {
            if (string.Equals(attendu, obtenu, StringComparison.Ordinal))
            {
                _ok++;
                Console.WriteLine("  [OK]  " + quoi);
            }
            else
            {
                _ko++;
                Console.WriteLine("  [KO]  " + quoi);
                Console.WriteLine("           attendu : " + Montre(attendu));
                Console.WriteLine("           obtenu  : " + Montre(obtenu));
            }
        }

        private static string Montre(string s)
        {
            if (s == null) return "(null)";
            return "«" + s.Replace("\r", "\\r").Replace("\n", "\\n") + "»";
        }

        public static int Bilan()
        {
            Console.WriteLine();
            Console.WriteLine("  " + _ok + " OK, " + _ko + " KO");
            return _ko == 0 ? 0 : 1;
        }
    }

    internal static class Programme
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            // « -- reseau » : seul le test des vraies sources (Bons plans, Actus). Le banc par défaut reste hors ligne.
            if (Array.IndexOf(args, "reseau") >= 0)
            {
                OffresReseauTests.Tout();
                return Banc.Bilan();
            }
            PilotesRefusesTests.Tout();
            JournalTechniqueTests.Tout();
            CanauxWindowsTests.Tout();
            RepartitionCoeursTests.Tout();
            FormatDiagnosticTests.Tout();
            InventaireServicesTests.Tout();
            AutoJeuTests.Tout();
            ChaineEntreeTests.Tout();
            PreuveTests.Tout();
            TopologieTests.Tout();
            ProfilJeuTests.Tout();
            CompetitionTests.Tout();
            CatalogueTests.Tout();
            DiskWalkTests.Tout();
            SuiviParalleleTests.Tout();
            ConstatsMaterielTests.Tout();
            ProblemesConnusTests.Tout();
            ShadersPrecompilesTests.Tout();
            ModeXboxTests.Tout();
            IntegriteMemoireTests.Tout();
            ReflexJeuxTests.Tout();
            CoherenceFpsTests.Tout();
            LatenceReelleTests.Tout();
            EtatOrigineTests.Tout();
            MsiConditionnelTests.Tout();
            EfficaciteTests.Tout();
            ModePresentationTests.Tout();
            OffresTests.Tout();
            DealArtworkTests.Tout();
            MonetisationTests.Tout();
            OffresSourcesTests.Tout();
            BoutiquesTests.Tout();
            ComparateurTests.Tout();
            BibliothequeTests.Tout();
            RetraitIaLocaleTests.Tout();
            CopiloteTexteTests.Tout();
            CopiloteReglagesTests.Tout();
            SauvegardesTests.Tout();
            DiscordImageTests.Tout();
            CachesApplisTests.Tout();
            SuiviTests.Tout();
            MajLogiqueTests.Tout();
            ActusTests.Tout();
            GrilleCartesTests.Tout();
            ChoixGpuTests.Tout();
            CoeurDedieTests.Tout();
            VeilleJeuTests.Tout();
            ModeJeuReglesTests.Tout();
            return Banc.Bilan();
        }
    }
}
