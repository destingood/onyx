using System;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="RappelNettoyage"/> — échéance de l'analyse hebdomadaire du poids mort,
    /// texte de la notification et fichier d'état (bt-rappel-nettoyage.txt).
    /// </summary>
    internal static class RappelNettoyageTests
    {
        public static void Tout()
        {
            Banc.Titre("Rappel hebdomadaire du poids mort");

            var t = new DateTime(2026, 11, 26, 18, 0, 0);
            Banc.Verifie("jamais analysé : dû", true, RappelNettoyage.Du(null, t));
            Banc.Verifie("il y a 6 jours : pas encore dû", true, !RappelNettoyage.Du(t.AddDays(-6), t));
            Banc.Verifie("il y a 7 jours : dû", true, RappelNettoyage.Du(t.AddDays(-7), t));
            Banc.Verifie("date dans le futur (horloge reculée) : dû", true, RappelNettoyage.Du(t.AddDays(1), t));

            Banc.Egal("5 Go dans 3 éléments", "5 Go à récupérer dans 3 éléments. Ouvre Stockage pour choisir.",
                RappelNettoyage.Texte(5368709120L, 3));
            Banc.Egal("1,5 Go dans 1 élément", "1,5 Go à récupérer dans 1 élément. Ouvre Stockage pour choisir.",
                RappelNettoyage.Texte(1610612736L, 1));
            Banc.Verifie("sous 1 Go : pas de notification", true, RappelNettoyage.Texte(1073741823L, 4) == null);
            Banc.Verifie("aucun élément : pas de notification", true, RappelNettoyage.Texte(5368709120L, 0) == null);

            RappelNettoyage.Etat e = RappelNettoyage.Lit(RappelNettoyage.Ecrit(new RappelNettoyage.Etat { Active = true, Derniere = t }));
            Banc.Verifie("état relu : activé", true, e.Active);
            Banc.Verifie("état relu : même date", true, e.Derniere == t);
            RappelNettoyage.Etat vide = RappelNettoyage.Lit(null);
            Banc.Verifie("pas de fichier : décoché par défaut", true, !vide.Active && vide.Derniere == null);
            RappelNettoyage.Etat abime = RappelNettoyage.Lit("oui\nn'importe quoi");
            Banc.Verifie("fichier abîmé : décoché, jamais analysé", true, !abime.Active && abime.Derniere == null);
            RappelNettoyage.Etat sansDate = RappelNettoyage.Lit("1\n");
            Banc.Verifie("activé sans date : dû tout de suite", true, sansDate.Active && RappelNettoyage.Du(sansDate.Derniere, t));
        }
    }
}
