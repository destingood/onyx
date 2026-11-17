using System.Collections.Generic;
using System.Threading.Tasks;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Suivi des modules de la page Stockage analysés en parallèle : la page reçoit les modules
    /// terminés DANS L'ORDRE D'AFFICHAGE, quel que soit l'ordre d'arrivée, et sait lequel elle attend.
    /// </summary>
    internal static class SuiviParalleleTests
    {
        public static void Tout()
        {
            Banc.Titre("Suivi des modules en parallèle (stockage)");
            var noms = new List<string> { "a", "b", "c", "d" };
            var s = new SuiviParallele<string>(noms);

            SuiviParallele<string>.Etat e = s.Fini(2);
            Banc.Verifie("1er fini (c) : liste = [c]", true, string.Join(",", e.Finis) == "c");
            Banc.Verifie("1er fini : on attend a", true, e.Reste && e.EnAttente == "a" && e.Faits == 1);

            e = s.Fini(0);
            Banc.Verifie("2e fini (a) : liste dans l'ordre d'affichage = [a,c]", true, string.Join(",", e.Finis) == "a,c");
            Banc.Verifie("2e fini : on attend b", true, e.EnAttente == "b" && e.Faits == 2);

            e = s.Fini(0);
            Banc.Verifie("même module deux fois : compté une seule fois", true, e.Faits == 2 && string.Join(",", e.Finis) == "a,c");

            s.Fini(3);
            e = s.Fini(1);
            Banc.Verifie("tout fini : 4 modules dans l'ordre, plus rien en attente", true,
                string.Join(",", e.Finis) == "a,b,c,d" && !e.Reste && e.Faits == 4);

            var beaucoup = new List<int>();
            for (int i = 0; i < 500; i++) beaucoup.Add(i);
            var s2 = new SuiviParallele<int>(beaucoup);
            Parallel.For(0, 500, i => s2.Fini(i));
            SuiviParallele<int>.Etat e2 = s2.Fini(0);
            Banc.Verifie("500 fins simultanées : aucune perdue", true, e2.Faits == 500 && e2.Finis.Count == 500 && !e2.Reste);
        }
    }
}
