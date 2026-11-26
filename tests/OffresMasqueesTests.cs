using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="OffresMasquees"/> — « J'ai déjà ce jeu » / « Pas pour moi » dans Bons plans :
    /// le jeu disparaît de toutes les boutiques, la liste se relit et se vide, rien d'autre n'est touché.
    /// </summary>
    internal static class OffresMasqueesTests
    {
        public static void Tout()
        {
            Banc.Titre("Bons plans : jeux masqués");

            var t = new DateTime(2026, 11, 26, 21, 0, 0);
            var l = new List<OffresMasquees.Masque>();
            Banc.Verifie("ajout « j'ai déjà »", true, OffresMasquees.Ajoute(l, "Hades II", OffresMasquees.Possede, t));
            Banc.Verifie("même jeu une 2e fois : refusé", true, !OffresMasquees.Ajoute(l, "HADES II™", OffresMasquees.PasPourMoi, t));
            Banc.Verifie("nom vide : refusé", true, !OffresMasquees.Ajoute(l, "  ", OffresMasquees.Possede, t));
            Banc.Verifie("reconnu sous une autre graphie", true, OffresMasquees.Contient(l, "Hades II™"));
            Banc.Verifie("reconnu dans son édition Deluxe", true, OffresMasquees.Contient(l, "Hades II Deluxe Edition"));
            Banc.Verifie("un autre jeu n'est pas masqué", true, !OffresMasquees.Contient(l, "Hades"));

            var offres = new List<Offre>
            {
                new Offre { Source = "steam", Nom = "Hades II" },
                new Offre { Source = "gog", Nom = "Hades II" },
                new Offre { Source = "epic", Nom = "Celeste" },
            };
            Banc.Egal("filtre : 2 offres retirées (toutes boutiques)", "2", OffresMasquees.Filtre(offres, l).ToString());
            Banc.Egal("filtre : reste Celeste", "Celeste", offres.Count == 1 ? offres[0].Nom : "?");
            Banc.Egal("filtre sans liste : rien retiré", "0", OffresMasquees.Filtre(offres, null).ToString());

            OffresMasquees.Ajoute(l, "Celeste", OffresMasquees.PasPourMoi, t.AddMinutes(1));
            List<OffresMasquees.Masque> relu = OffresMasquees.Lire(OffresMasquees.Ecrire(l));
            Banc.Egal("relu : 2 jeux", "2", relu.Count.ToString());
            Banc.Egal("relu : nom affiché conservé", "Hades II", relu[0].Nom);
            Banc.Egal("relu : raison conservée", OffresMasquees.PasPourMoi, relu[1].Raison);
            Banc.Verifie("relu : date conservée", true, relu[1].Quand == t.AddMinutes(1));

            List<OffresMasquees.Masque> sale = OffresMasquees.Lire("\n\tpossede\tsans clé\n" + "doom\tinconnue\tDoom\tpasunedate\ndoom\tpossede\tDoom\t\n");
            Banc.Egal("fichier abîmé : lignes sans clé ignorées, doublon fusionné", "1", sale.Count.ToString());
            Banc.Egal("raison inconnue : « pas pour moi »", OffresMasquees.PasPourMoi, sale[0].Raison);
            Banc.Verifie("lecture de null : liste vide", true, OffresMasquees.Lire(null).Count == 0);

            Banc.Verifie("retrait", true, OffresMasquees.Retire(l, "Hades II"));
            Banc.Verifie("retrait d'un absent : faux", true, !OffresMasquees.Retire(l, "Hades II"));
            Banc.Verifie("après retrait : réaffiché", true, !OffresMasquees.Contient(l, "Hades II"));

            var plein = new List<OffresMasquees.Masque>();
            for (int i = 0; i < OffresMasquees.Max; i++) OffresMasquees.Ajoute(plein, "Jeu " + i, OffresMasquees.Possede, t.AddMinutes(i));
            OffresMasquees.Ajoute(plein, "Le dernier", OffresMasquees.Possede, t.AddDays(1));
            Banc.Egal("plafond : jamais plus de " + OffresMasquees.Max, OffresMasquees.Max.ToString(), plein.Count.ToString());
            Banc.Verifie("plafond : le plus ancien part", true, !OffresMasquees.Contient(plein, "Jeu 0") && OffresMasquees.Contient(plein, "Le dernier"));
        }
    }
}
