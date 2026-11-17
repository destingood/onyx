using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>Profil par jeu : lancement/sortie, cible des cœurs, plan d'alimentation, état sur disque.</summary>
    internal static class ProfilJeuTests
    {
        public static void Tout()
        {
            Banc.Titre("Profil par jeu — décision et état");

            Banc.Verifie("jeu lancé : appliquer", true, ProfilJeu.Decide(null, "cs2") == ProfilJeu.Geste.Appliquer);
            Banc.Verifie("même jeu : rien (pas de ré-application toutes les 2 s)", true, ProfilJeu.Decide("cs2", "cs2") == ProfilJeu.Geste.Rien);
            Banc.Verifie("même jeu, casse différente : rien", true, ProfilJeu.Decide("CS2", "cs2") == ProfilJeu.Geste.Rien);
            Banc.Verifie("jeu fermé : retirer", true, ProfilJeu.Decide("cs2", null) == ProfilJeu.Geste.Retirer);
            Banc.Verifie("autre jeu : retirer d'abord", true, ProfilJeu.Decide("cs2", "valorant") == ProfilJeu.Geste.Retirer);
            Banc.Verifie("aucun jeu : rien", true, ProfilJeu.Decide(null, null) == ProfilJeu.Geste.Rien);

            long m;
            var preferes = new List<int> { 0, 1, 2, 3 };
            Banc.Verifie("Auto + topologie connue : CPU Sets", true, ProfilJeu.Cible("Auto", 0, preferes, out m) == ProfilJeu.Mode.CpuSets);
            Banc.Verifie("Auto + ancien masque, topologie inconnue : rien (masque ignoré)", true,
                ProfilJeu.Cible("Auto", 0xFFFE, new List<int>(), out m) == ProfilJeu.Mode.Aucun);
            Banc.Verifie("Manuel : affinité stricte", true, ProfilJeu.Cible("Manuel", 0xF0, preferes, out m) == ProfilJeu.Mode.Affinite && m == 0xF0);
            Banc.Verifie("Manuel sans cœur : rien", true, ProfilJeu.Cible("Manuel", 0, preferes, out m) == ProfilJeu.Mode.Aucun);
            Banc.Verifie("mode inconnu : rien", true, ProfilJeu.Cible("Désactivé", 0xF0, preferes, out m) == ProfilJeu.Mode.Aucun);

            Banc.Verifie("plan Performances (majuscules) : pas de bascule", false, ProfilJeu.DoitBasculer(ProfilJeu.PlanPerformances.ToUpperInvariant()));
            Banc.Verifie("plan illisible : pas de bascule", false, ProfilJeu.DoitBasculer(null));
            Banc.Verifie("plan Équilibré : bascule", true, ProfilJeu.DoitBasculer("381b4222-f694-41f0-9685-ff5bb260df2e"));

            var e = new ProfilJeu.Etat { Jeu = "cs2", PlanOrigine = "381b4222-f694-41f0-9685-ff5bb260df2e" };
            ProfilJeu.Etat r = ProfilJeu.LitEtat(ProfilJeu.EcritEtat(e));
            Banc.Verifie("état relu", true, r != null && r.Jeu == "cs2" && r.PlanOrigine == e.PlanOrigine);
            ProfilJeu.Etat sansPlan = ProfilJeu.LitEtat(ProfilJeu.EcritEtat(new ProfilJeu.Etat { Jeu = "cs2" }));
            Banc.Verifie("état sans plan relu : plan null", true, sansPlan != null && sansPlan.PlanOrigine == null);
            Banc.Verifie("état vide : rien", true, ProfilJeu.LitEtat("") == null);
            Banc.Verifie("état sans jeu : rien", true, ProfilJeu.LitEtat("plan=abc\n") == null);
        }
    }
}
