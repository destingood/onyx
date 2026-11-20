namespace BTOptimizer.Tests
{
    /// <summary>
    /// Mode de présentation du jeu : classement des compteurs, jointure par séquence (fenêtre de
    /// 250 ms) et texte du constat. Logique pure, sans session ETW.
    /// </summary>
    internal static class ModePresentationTests
    {
        private static ModePresentation.Releve R(int flipsJeu, int copies, int flipsCompo, int presents, int directs, int flipsVus = 100)
        {
            return new ModePresentation.Releve
            {
                FlipsJeu = flipsJeu, CopiesJeu = copies, FlipsCompo = flipsCompo,
                Presents = presents, Directs = directs, FlipsVus = flipsVus
            };
        }

        public static void Tout()
        {
            Banc.Titre("Mode de présentation du jeu");

            // ---- Classement
            Banc.Verifie("moins de 30 événements : indéterminé", true,
                ModePresentation.Classe(R(10, 5, 10, 100, 0)) == ModePresentation.ModeImage.Inconnu);
            Banc.Verifie("flips du jeu majoritaires : plein écran exclusif", true,
                ModePresentation.Classe(R(100, 0, 0, 0, 0)) == ModePresentation.ModeImage.PleinEcranExclusif);
            Banc.Verifie("copies majoritaires : copie composée", true,
                ModePresentation.Classe(R(0, 100, 0, 0, 0)) == ModePresentation.ModeImage.CopieComposee);
            Banc.Verifie("flips composés, 90 % de présents directs : direct", true,
                ModePresentation.Classe(R(0, 0, 100, 100, 90)) == ModePresentation.ModeImage.Direct);
            Banc.Verifie("flips composés, aucun présent direct : composé", true,
                ModePresentation.Classe(R(0, 0, 100, 100, 0)) == ModePresentation.ModeImage.Compose);
            Banc.Verifie("flips composés, 30 % de présents directs : mixte", true,
                ModePresentation.Classe(R(0, 0, 100, 100, 30)) == ModePresentation.ModeImage.Mixte);
            Banc.Verifie("flips composés mais moins de 30 présents : indéterminé", true,
                ModePresentation.Classe(R(0, 0, 100, 20, 0)) == ModePresentation.ModeImage.Inconnu);
            Banc.Verifie("flips composés, aucun flip de jointure lu : indéterminé (pas de faux composé)", true,
                ModePresentation.Classe(R(0, 0, 100, 100, 0, 0)) == ModePresentation.ModeImage.Inconnu);
            Banc.Verifie("aucune catégorie à 50 % : indéterminé", true,
                ModePresentation.Classe(R(40, 40, 40, 100, 0)) == ModePresentation.ModeImage.Inconnu);
            Banc.Verifie("relevé absent : indéterminé", true,
                ModePresentation.Classe(null) == ModePresentation.ModeImage.Inconnu);

            // ---- Jointure par séquence
            var s = new ModePresentation.Suivi();
            s.PresentJeu(1, 1000);
            s.FlipDirect(1, 1200);
            Banc.Verifie("flip direct à 200 ms : compté", true, s.Releve().Directs == 1);
            Banc.Verifie("flip de jointure lu : compté dans FlipsVus", true, s.Releve().FlipsVus == 1);
            s.FlipDirect(0, 1210);
            Banc.Verifie("flip à séquence nulle (champ illisible) : ignoré", true, s.Releve().FlipsVus == 1);
            s.Reinitialise();
            Banc.Verifie("Reinitialise remet FlipsVus à zéro", true, s.Releve().FlipsVus == 0);

            s = new ModePresentation.Suivi();
            s.PresentJeu(1, 1000);
            s.FlipDirect(1, 1300);
            Banc.Verifie("flip direct à 300 ms : non compté", true, s.Releve().Directs == 0);

            s = new ModePresentation.Suivi();
            s.PresentJeu(1, 1000);
            s.FlipDirect(1, 1100);
            s.FlipDirect(1, 1150);
            Banc.Verifie("même séquence vue deux fois en direct : comptée une fois", true, s.Releve().Directs == 1);

            s = new ModePresentation.Suivi();
            s.PresentJeu(1, 1000);
            s.FlipDirect(2, 1100);
            Banc.Verifie("séquence inconnue : ignorée", true, s.Releve().Directs == 0);

            s = new ModePresentation.Suivi();
            s.PresentJeu(1, 1000);
            s.PresentJeu(1, 1005);
            s.PresentJeu(2, 1010);
            Banc.Verifie("présents comptés par séquence distincte", true, s.Releve().Presents == 2);

            s = new ModePresentation.Suivi();
            s.PresentJeu(1, 0);
            s.PresentJeu(2, 5000);
            s.FlipDirect(1, 100);
            Banc.Verifie("séquence de plus de 2 s oubliée", true, s.Releve().Directs == 0);

            s = new ModePresentation.Suivi();
            s.PresentJeu(1, 1000);
            s.FlipDirect(1, 1100);
            s.Reinitialise();
            ModePresentation.Releve vide = s.Releve();
            Banc.Verifie("Reinitialise remet les compteurs à zéro", true, vide.Presents == 0 && vide.Directs == 0);
            s.FlipDirect(1, 1120);
            Banc.Verifie("Reinitialise oublie aussi les séquences", true, s.Releve().Directs == 0);

            // ---- Constat
            Banc.Verifie("constat nul pour direct", true,
                ModePresentation.Constat(ModePresentation.ModeImage.Direct, "jeu.exe", 144, false) == null);
            Banc.Verifie("constat nul pour plein écran exclusif", true,
                ModePresentation.Constat(ModePresentation.ModeImage.PleinEcranExclusif, "jeu.exe", 144, false) == null);
            Banc.Verifie("constat nul pour mixte", true,
                ModePresentation.Constat(ModePresentation.ModeImage.Mixte, "jeu.exe", 144, false) == null);
            string c = ModePresentation.Constat(ModePresentation.ModeImage.Compose, "jeu.exe", 144, false);
            Banc.Verifie("composé à 144 Hz : « 6,9 ms à 144 Hz »", true, c != null && c.Contains("6,9 ms à 144 Hz"));
            Banc.Verifie("le texte dit que c'est un calcul non mesuré", true, c != null && c.Contains("calcul, non mesuré"));
            Banc.Verifie("composé : nomme Composed: Flip", true, c != null && c.Contains("Composed: Flip"));
            Banc.Verifie("réglage absent : le texte le propose", true, c != null && c.Contains("peut la faire passer directement"));
            string d = ModePresentation.Constat(ModePresentation.ModeImage.CopieComposee, "jeu.exe", 240, true);
            Banc.Verifie("copie composée à 240 Hz : « 4,2 ms à 240 Hz »", true, d != null && d.Contains("4,2 ms à 240 Hz"));
            Banc.Verifie("copie composée : nomme Composed: Copy", true, d != null && d.Contains("Composed: Copy"));
            Banc.Verifie("réglage déjà actif : conseille plein écran sans bordure", true,
                d != null && d.Contains("déjà actif") && d.Contains("plein écran") && !d.Contains("peut la faire passer"));
            string z = ModePresentation.Constat(ModePresentation.ModeImage.Compose, "jeu.exe", 0, false);
            Banc.Verifie("Hz inconnu : aucun chiffre en ms", true, z != null && !z.Contains(" ms") && !z.Contains("Hz"));

            Banc.Egal("libellé du composé", "composé par Windows (Composed: Flip)",
                ModePresentation.Libelle(ModePresentation.ModeImage.Compose));
            Banc.Egal("libellé de l'inconnu", "indéterminé", ModePresentation.Libelle(ModePresentation.ModeImage.Inconnu));
        }
    }
}
