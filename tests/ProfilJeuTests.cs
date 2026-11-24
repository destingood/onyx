using System;
using System.Collections.Generic;
using System.Linq;

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
            Banc.Verifie("« Manual » (valeur écrite par GameProfileForm) : affinité stricte", true,
                ProfilJeu.Cible("Manual", 0xF0, preferes, out m) == ProfilJeu.Mode.Affinite && m == 0xF0);
            Banc.Verifie("Manuel sans cœur : rien", true, ProfilJeu.Cible("Manuel", 0, preferes, out m) == ProfilJeu.Mode.Aucun);
            Banc.Verifie("mode inconnu : rien", true, ProfilJeu.Cible("Désactivé", 0xF0, preferes, out m) == ProfilJeu.Mode.Aucun);

            Banc.Verifie("plan Performances (majuscules) : pas de bascule", false, ProfilJeu.DoitBasculer(ProfilJeu.PlanPerformances.ToUpperInvariant()));
            Banc.Verifie("plan illisible : pas de bascule", false, ProfilJeu.DoitBasculer(null));
            Banc.Verifie("plan Équilibré : bascule", true, ProfilJeu.DoitBasculer("381b4222-f694-41f0-9685-ff5bb260df2e"));

            Banc.Verifie("« Performances optimales » (eb61) : pas de bascule", false, ProfilJeu.DoitBasculer("e9a42b02-d5df-448d-aa00-03f14749eb61"));
            Banc.Verifie("« Performances optimales » (copie eb62) : pas de bascule", false, ProfilJeu.DoitBasculer("E9A42B02-D5DF-448D-AA00-03F14749EB62"));

            Banc.Verifie("powercfg en français : GUID lu", true, ProfilJeu.GuidPlan(
                "GUID du mode de gestion de l'alimentation : 381b4222-f694-41f0-9685-ff5bb260df2e  (Utilisation normale)")
                == "381b4222-f694-41f0-9685-ff5bb260df2e");
            Banc.Verifie("powercfg en anglais : GUID lu", true, ProfilJeu.GuidPlan(
                "Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)") == ProfilJeu.PlanPerformances);
            Banc.Verifie("powercfg illisible : null", true, ProfilJeu.GuidPlan("Paramètres non valides") == null && ProfilJeu.GuidPlan(null) == null);

            var fichier = new ProfilJeu.Etat { Jeu = "cs2", PlanOrigine = "381b4222-f694-41f0-9685-ff5bb260df2e" };
            Banc.Verifie("reprise : pas de fichier → rien", true, ProfilJeu.Reprise(null, "cs2") == ProfilJeu.Reprendre.Rien);
            Banc.Verifie("reprise : ONYX relancé, même jeu en cours → adopter l'état (plan d'origine gardé)", true,
                ProfilJeu.Reprise(fichier, "CS2") == ProfilJeu.Reprendre.Adopter);
            Banc.Verifie("reprise : jeu fermé entre-temps → rétablir", true, ProfilJeu.Reprise(fichier, null) == ProfilJeu.Reprendre.Retablir);
            Banc.Verifie("reprise : autre jeu en cours → rétablir", true, ProfilJeu.Reprise(fichier, "valorant") == ProfilJeu.Reprendre.Retablir);

            var exes = new List<KeyValuePair<string, string[]>>
            {
                new KeyValuePair<string, string[]>("Apex", new[] { @"r5apex.exe" }),
                new KeyValuePair<string, string[]>("cs2", new[] { "cs2.exe" }),
                new KeyValuePair<string, string[]>("Valorant", null),
            };
            var noms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "explorer", "CS2" };
            Banc.Verifie("jeu détecté sur un seul relevé des processus (casse ignorée)", true, ProfilJeu.JeuDetecte(exes, noms) == "cs2");
            noms.Add("r5apex");
            Banc.Verifie("deux jeux : le premier profil l'emporte (ordre d'avant)", true, ProfilJeu.JeuDetecte(exes, noms) == "Apex");
            Banc.Verifie("aucun jeu : null", true, ProfilJeu.JeuDetecte(exes, new HashSet<string>(StringComparer.OrdinalIgnoreCase)) == null);

            var e = new ProfilJeu.Etat { Jeu = "cs2", PlanOrigine = "381b4222-f694-41f0-9685-ff5bb260df2e" };
            ProfilJeu.Etat r = ProfilJeu.LitEtat(ProfilJeu.EcritEtat(e));
            Banc.Verifie("état relu", true, r != null && r.Jeu == "cs2" && r.PlanOrigine == e.PlanOrigine);
            ProfilJeu.Etat sansPlan = ProfilJeu.LitEtat(ProfilJeu.EcritEtat(new ProfilJeu.Etat { Jeu = "cs2" }));
            Banc.Verifie("état sans plan relu : plan null", true, sansPlan != null && sansPlan.PlanOrigine == null);
            Banc.Verifie("état vide : rien", true, ProfilJeu.LitEtat("") == null);
            Banc.Verifie("état sans jeu : rien", true, ProfilJeu.LitEtat("plan=abc\n") == null);

            // Logiciels de fond déplacés hors du CCD du jeu : jamais fermés, jamais le jeu lui-même.
            ProfilJeu.Etat avecFond = ProfilJeu.LitEtat(ProfilJeu.EcritEtat(new ProfilJeu.Etat { Jeu = "Overwatch 2", Fond = true }));
            Banc.Verifie("état : logiciels de fond déplacés relu", true, avecFond != null && avecFond.Fond);
            Banc.Verifie("état ancien (sans ligne fond) : rien de déplacé", true, !ProfilJeu.LitEtat("jeu=cs2\n").Fond);
            var vivants = new[] { "Overwatch", "Battle.net", "chrome", "chrome", "Discord", "explorer", "nvcontainer", "amd3dvcacheUser", "lghub_agent", "audiodg" };
            List<string> fond = ProfilJeu.AReleguer(vivants, new[] { @"C:\Jeux\Overwatch\Overwatch.exe" });
            Banc.Verifie("fond : navigateur, vocal, lanceur et utilitaires", true,
                fond.Contains("chrome") && fond.Contains("Discord") && fond.Contains("Battle.net") && fond.Contains("lghub_agent"));
            Banc.Verifie("fond : un nom une seule fois", true, fond.Count(n => n == "chrome") == 1);
            Banc.Verifie("fond : ni le jeu, ni Windows, ni NVIDIA, ni le pilote AMD", true,
                !fond.Contains("Overwatch") && !fond.Contains("explorer") && !fond.Contains("nvcontainer") && !fond.Contains("amd3dvcacheUser") && !fond.Contains("audiodg"));
            List<string> lanceurJeu = ProfilJeu.AReleguer(new[] { "Battle.net", "Discord" }, new[] { "Battle.net.exe", "Overwatch.exe" });
            Banc.Verifie("fond : un exécutable du profil reste avec le jeu", true, !lanceurJeu.Contains("Battle.net") && lanceurJeu.Contains("Discord"));
            Banc.Verifie("fond : casse ignorée", true, ProfilJeu.AReleguer(new[] { "DISCORD" }, null).Count == 1);
            Banc.Verifie("fond : rien de vivant, rien à faire", true, ProfilJeu.AReleguer(null, null).Count == 0);
        }
    }
}
