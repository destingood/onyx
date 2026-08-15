using System;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="JournalTechnique"/>.
    ///
    /// Seules les décisions PURES sont testées : ce qui mérite une ligne, comment une ligne est
    /// formée, et ce qui doit disparaître avant tout export. L'écriture sur disque ne l'est pas —
    /// elle n'a qu'une obligation, ne jamais lever, et un test qui écrirait dans le dossier de
    /// données de l'utilisateur en cours d'exécution ferait plus de dégâts qu'il n'en éviterait.
    /// </summary>
    internal static class JournalTechniqueTests
    {
        public static void Tout()
        {
            Repetition();
            MiseEnLigne();
            DonneesPersonnelles();
        }

        /// <summary>
        /// Une boucle de mesure qui échoue toutes les 500 ms produirait des dizaines de milliers
        /// de lignes identiques. On garde les dix premières, puis les puissances de dix : la
        /// trace d'un échec rare ET l'ordre de grandeur d'un échec massif.
        /// </summary>
        private static void Repetition()
        {
            Banc.Titre("Répétition comptée, pas recopiée");

            Banc.Verifie("la 1re occurrence s'écrit", true, JournalTechnique.MeriteUneLigne(1));
            Banc.Verifie("la 10e s'écrit", true, JournalTechnique.MeriteUneLigne(10));
            Banc.Verifie("la 11e ne s'écrit PAS", false, JournalTechnique.MeriteUneLigne(11));
            Banc.Verifie("la 99e non plus", false, JournalTechnique.MeriteUneLigne(99));
            Banc.Verifie("la 100e s'écrit", true, JournalTechnique.MeriteUneLigne(100));
            Banc.Verifie("la 101e non", false, JournalTechnique.MeriteUneLigne(101));
            Banc.Verifie("la 1000e s'écrit", true, JournalTechnique.MeriteUneLigne(1000));
            Banc.Verifie("la 999e non", false, JournalTechnique.MeriteUneLigne(999));
            Banc.Verifie("le million s'écrit", true, JournalTechnique.MeriteUneLigne(1000000));

            // Sur 100 000 echecs identiques, on veut une poignee de lignes, pas 100 000.
            int lignes = 0;
            for (long i = 1; i <= 100000; i++) if (JournalTechnique.MeriteUneLigne(i)) lignes++;
            Banc.Verifie("100 000 échecs identiques tiennent en 14 lignes", true, lignes == 14);
        }

        private static void MiseEnLigne()
        {
            Banc.Titre("Une entrée tient sur une ligne");

            Banc.Egal("les retours chariot disparaissent",
                "Accès refusé au fichier",
                JournalTechnique.SurUneLigne("Accès refusé\r\nau  fichier"));
            Banc.Egal("les tabulations aussi",
                "a b", JournalTechnique.SurUneLigne("a\tb"));
            Banc.Egal("vide reste vide", "", JournalTechnique.SurUneLigne(""));
            Banc.Egal("null ne lève pas", "", JournalTechnique.SurUneLigne(null));
            Banc.Verifie("un message fleuve est tronqué à 300",
                true, JournalTechnique.SurUneLigne(new string('x', 900)).Length == 300);

            string l = JournalTechnique.Ligne(new DateTime(2026, 8, 15, 17, 15, 0),
                "PilotesRefuses.Lire", "UnauthorizedAccessException", "Accès refusé", 1);
            Banc.Egal("format d'une première occurrence",
                "15/08/2026 17:15:00 | PilotesRefuses.Lire | UnauthorizedAccessException | Accès refusé", l);

            string r = JournalTechnique.Ligne(new DateTime(2026, 8, 15, 17, 15, 0),
                "zone", "type", "", 100);
            Banc.Egal("répétition marquée, message absent omis",
                "15/08/2026 17:15:00 | zone | type   (×100)", r);
        }

        /// <summary>
        /// Le bloc « infos de support » promet noir sur blanc qu'il ne contient ni nom
        /// d'utilisateur ni chemin privé. Un message d'exception cite pourtant très souvent le
        /// chemin du profil. C'est ici que la promesse se tient.
        /// </summary>
        private static void DonneesPersonnelles()
        {
            Banc.Titre("Rien de personnel ne sort");

            Banc.Egal("le chemin de profil est masqué",
                @"Accès refusé : %PROFIL%\Desktop\bt",
                JournalTechnique.SansDonneesPerso(
                    @"Accès refusé : C:\Users\Lucas\Desktop\bt", "Lucas", @"C:\Users\Lucas"));

            // Le profil AVANT le nom : le chemin CONTIENT le nom, et l'ordre inverse laisserait
            // un « C:\Users\%UTILISATEUR% » a moitie nettoye.
            Banc.Verifie("aucun reste du nom dans le chemin masqué", false,
                JournalTechnique.SansDonneesPerso(
                    @"C:\Users\Lucas\x", "Lucas", @"C:\Users\Lucas").Contains("Lucas"));

            Banc.Egal("le nom seul est masqué aussi",
                "session de %UTILISATEUR% fermée",
                JournalTechnique.SansDonneesPerso("session de Lucas fermée", "Lucas", ""));

            Banc.Egal("la casse ne protège pas le nom",
                "%UTILISATEUR% et %UTILISATEUR%",
                JournalTechnique.SansDonneesPerso("LUCAS et lucas", "Lucas", ""));

            // Un nom tres court est un piege : masquer « ab » mutilerait tous les mots qui le
            // contiennent, et le journal deviendrait illisible pour proteger ce qui n'est pas
            // identifiant.
            Banc.Egal("un nom de moins de 3 lettres n'est pas masqué",
                "abandon de la tache", JournalTechnique.SansDonneesPerso("abandon de la tache", "ab", ""));

            Banc.Egal("ligne vide", "", JournalTechnique.SansDonneesPerso("", "Lucas", @"C:\Users\Lucas"));
            Banc.Egal("ligne nulle ne lève pas", "",
                JournalTechnique.SansDonneesPerso(null, "Lucas", @"C:\Users\Lucas"));
            Banc.Egal("sans nom ni profil, la ligne est intacte",
                "rien a masquer", JournalTechnique.SansDonneesPerso("rien a masquer", "", ""));
        }
    }
}
