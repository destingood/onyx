namespace BTOptimizer.Tests
{
    /// <summary>Raccourcis clavier globaux : lecture, refus, conflits, fichier.</summary>
    internal static class RaccourcisTests
    {
        public static void Tout()
        {
            Banc.Titre("Raccourcis clavier");
            Banc.Egal("défaut du Mode Jeu", "Ctrl+Alt+G", Raccourcis.Texte(Raccourcis.Defaut(Raccourcis.Action.ModeJeu)));
            Banc.Egal("lecture et écriture", "Ctrl+Alt+Maj+K", Raccourcis.Texte(Raccourcis.Lit("ctrl + alt + shift + k")));
            Banc.Egal("touche F", "Alt+F9", Raccourcis.Texte(Raccourcis.Lit("Alt+F9")));
            Banc.Egal("désactivé", "Désactivé", Raccourcis.Texte(Raccourcis.Lit("désactivé")));
            Banc.Verifie("texte illisible refusé", true, Raccourcis.Lit("Ctrl+Truc+G") == null && Raccourcis.Lit("Ctrl+F13") == null && Raccourcis.Lit("") == null);
            Banc.Verifie("touche seule refusée", true, Raccourcis.Refus(Raccourcis.Lit("G")) != null);
            Banc.Verifie("Maj seule refusée", true, Raccourcis.Refus(Raccourcis.Lit("Maj+G")) != null);
            Banc.Verifie("F9 seule refusée", true, Raccourcis.Refus(Raccourcis.Lit("F9")) != null);
            Banc.Verifie("Alt+F4 refusé", true, Raccourcis.Refus(Raccourcis.Lit("Alt+F4")) != null);
            Banc.Verifie("Ctrl+Alt+K accepté", true, Raccourcis.Refus(Raccourcis.Lit("Ctrl+Alt+K")) == null);

            var tous = Raccourcis.TousParDefaut();
            Banc.Egal("conflit trouvé avec le Mode Jeu", "0", Raccourcis.Conflit(tous, Raccourcis.Lit("Ctrl+Alt+G"), 2).ToString());
            Banc.Egal("pas de conflit avec soi-même", "-1", Raccourcis.Conflit(tous, Raccourcis.Lit("Ctrl+Alt+G"), 0).ToString());
            Banc.Egal("une combinaison désactivée n'entre jamais en conflit", "-1", Raccourcis.Conflit(tous, Raccourcis.Lit("désactivé"), 1).ToString());

            var lus = Raccourcis.LitFichier("modejeu=Ctrl+Alt+K\r\ncompteur=désactivé\ncapture=G\ninconnu=Ctrl+Alt+Z\n");
            Banc.Egal("fichier : changé, désactivé, refusé gardé au défaut",
                "Ctrl+Alt+K|Ctrl+Alt+O|Désactivé|Ctrl+Alt+C", Raccourcis.Texte(lus[0]) + "|" + Raccourcis.Texte(lus[1]) + "|" + Raccourcis.Texte(lus[2]) + "|" + Raccourcis.Texte(lus[3]));
            lus = Raccourcis.LitFichier("modejeu=Ctrl+Alt+O\n");
            Banc.Egal("fichier : conflit, la seconde action prend une autre combinaison", "Ctrl+Alt+O|Désactivé",
                Raccourcis.Texte(lus[0]) + "|" + Raccourcis.Texte(lus[1]));
            lus = Raccourcis.LitFichier("afficher=Ctrl+Alt+G\n");
            Banc.Egal("fichier : la seconde revient à son défaut s'il est libre", "Ctrl+Alt+G|Ctrl+Alt+O",
                Raccourcis.Texte(lus[0]) + "|" + Raccourcis.Texte(lus[1]));
            Banc.Egal("aller-retour du fichier", "modejeu=Ctrl+Alt+K\nafficher=Ctrl+Alt+O\ncompteur=désactivé\ncapture=Ctrl+Alt+C\nminuteur=Ctrl+Alt+T\nminuteur0=Ctrl+Alt+R\n",
                Raccourcis.EcritFichier(Raccourcis.LitFichier(Raccourcis.EcritFichier(Raccourcis.LitFichier("modejeu=Ctrl+Alt+K\ncompteur=désactivé")))));
            var anciens = Raccourcis.LitFichier("modejeu=Ctrl+Alt+G\nafficher=Ctrl+Alt+O\ncompteur=Ctrl+Alt+F\ncapture=Ctrl+Alt+C\n");
            Banc.Egal("fichier d'avant le minuteur : ses deux raccourcis prennent leur défaut", "Ctrl+Alt+T|Ctrl+Alt+R",
                Raccourcis.Texte(anciens[(int)Raccourcis.Action.Minuteur]) + "|" + Raccourcis.Texte(anciens[(int)Raccourcis.Action.MinuteurZero]));
            Banc.Verifie("aucun conflit entre les raccourcis par défaut", true, PasDeConflit());
        }

        private static bool PasDeConflit()
        {
            var t = Raccourcis.TousParDefaut();
            for (int i = 0; i < t.Length; i++) if (Raccourcis.Conflit(t, t[i], i) >= 0 || Raccourcis.Refus(t[i]) != null) return false;
            return true;
        }
    }
}
