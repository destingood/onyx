using System.Collections.Generic;
using BTOptimizer;

namespace BTOptimizer.Tests
{
    /// <summary>Notifications par catégorie : fichier des catégories coupées.</summary>
    internal static class NotifCategoriesTests
    {
        public static void Tout()
        {
            Banc.Titre("Notifications par catégorie");
            Banc.Verifie("fichier absent : tout actif", true, NotifCategories.Lire("").Count == 0 && NotifCategories.Active(NotifCategories.Lire(null), NotifCategories.Gardien));
            HashSet<string> c = NotifCategories.Lire("suivis\r\n  maj \ninconnue\n\nsuivis\n");
            Banc.Egal("lignes connues seulement, espaces et doublons ignorés", "2", c.Count.ToString());
            Banc.Verifie("catégorie coupée", false, NotifCategories.Active(c, NotifCategories.JeuxSuivis));
            Banc.Verifie("catégorie non listée : active", true, NotifCategories.Active(c, NotifCategories.Nettoyage));
            Banc.Egal("écriture dans l'ordre fixe", "maj\nsuivis\n", NotifCategories.Ecrire(c));
            Banc.Egal("rien de coupé : fichier vide", "", NotifCategories.Ecrire(new HashSet<string>()));
            Banc.Verifie("relu identique", true, NotifCategories.Lire(NotifCategories.Ecrire(c)).SetEquals(c));
            bool libelles = true;
            foreach (string id in NotifCategories.Toutes)
                libelles &= NotifCategories.Libelle(id) != id && NotifCategories.Detail(id).Length > 0;
            Banc.Verifie("chaque catégorie a un libellé et une précision", true, libelles);
            Banc.Verifie("les quatre sources sont branchées", true,
                CatalogueTests.Source("DashboardForm.cs").Contains("NotifReglages.Active(NotifCategories.Gardien)")
                && CatalogueTests.Source("DashboardForm.cs").Contains("NotifReglages.Active(NotifCategories.MiseAJour)")
                && CatalogueTests.Source("SuiviVeille.cs").Contains("NotifReglages.Active(NotifCategories.JeuxSuivis)")
                && CatalogueTests.Source("RappelNettoyageMachine.cs").Contains("NotifReglages.Active(NotifCategories.Nettoyage)"));
        }
    }
}
