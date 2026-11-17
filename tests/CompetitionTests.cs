using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>Mode Compétition : état réversible sur le disque, refus d'une double activation,
    /// annulation qui ne touche que ce qu'il a posé.</summary>
    internal static class CompetitionTests
    {
        public static void Tout()
        {
            Banc.Titre("Compétition — état et règles");

            var plein = new Competition.Etat
            {
                Active = new DateTime(2026, 11, 17, 20, 30, 0),
                ModeJeuPose = true,
                PlafondAvant = 138,
                Profils = new List<string> { "cs2", "Warhammer 40,000: Space Marine 2" },
                Reglages = new List<string> { "nagle_off", "mmcss_games" },
            };
            Competition.Etat r = Competition.Lit(Competition.Ecrit(plein));
            Banc.Verifie("état plein relu", true, r != null && r.Active == plein.Active && r.ModeJeuPose
                && r.PlafondAvant == 138 && r.Profils.Count == 2 && r.Profils[1] == "Warhammer 40,000: Space Marine 2"
                && string.Join(",", r.Reglages) == "nagle_off,mmcss_games");

            Competition.Etat vide = Competition.Lit(Competition.Ecrit(new Competition.Etat { Active = new DateTime(2026, 11, 17) }));
            Banc.Verifie("état vide relu : listes vides, plafond non touché", true, vide != null && vide.Profils.Count == 0
                && vide.Reglages.Count == 0 && vide.PlafondAvant == -1 && !vide.ModeJeuPose);
            Banc.Verifie("texte vide : pas d'état", true, Competition.Lit("") == null);
            Banc.Verifie("texte sans date d'activation : pas d'état", true, Competition.Lit("profils=cs2\n") == null);

            Banc.Verifie("aucune compétition en cours : activation permise", true, Competition.PeutActiver(null));
            Banc.Verifie("compétition déjà active : seconde activation refusée", false, Competition.PeutActiver(new Competition.Etat()));

            var profils = new Dictionary<string, Tuple<string, long>>(StringComparer.OrdinalIgnoreCase)
            {
                { "CS2", Tuple.Create("Manual", 0xF0L) },
            };
            List<string> sans = Competition.JeuxSansProfil(new[] { "cs2", "Valorant" }, profils);
            Banc.Verifie("jeux sans profil : celui qui en a un (casse différente) est exclu", true,
                sans.Count == 1 && sans[0] == "Valorant");

            var apres = new Dictionary<string, Tuple<string, long>>(StringComparer.OrdinalIgnoreCase)
            {
                { "cs2", Tuple.Create("Manual", 0xF0L) },   // repassé en manuel par l'utilisateur
                { "Valorant", Tuple.Create("Auto", 0L) },   // resté tel que posé
            };
            var e = new Competition.Etat { Profils = new List<string> { "cs2", "Valorant", "Apex" } };   // Apex supprimé
            List<string> retirer = Competition.ProfilsARetirer(e, apres);
            Banc.Verifie("annulation : seul le profil resté « Auto » est retiré", true,
                retirer.Count == 1 && retirer[0] == "Valorant");
        }
    }
}
