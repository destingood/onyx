using System;
using System.Collections.Generic;
using System.Linq;

namespace BTOptimizer.Tests
{
    /// <summary>Topologie : quels processeurs logiques réserver au jeu (cœurs P, CCD V-Cache, hors cœur 0).</summary>
    internal static class TopologieTests
    {
        private static Topologie.Proc P(int index, int coeur, int eff, int l3, long octets)
        {
            return new Topologie.Proc { Index = index, Coeur = coeur, Efficacite = eff, L3 = l3, L3Octets = octets };
        }

        private static bool Egal(IList<int> a, IEnumerable<int> b) { return a != null && a.SequenceEqual(b); }

        private const long Mo = 1024L * 1024;

        public static void Tout()
        {
            Banc.Titre("Topologie — cœurs du jeu");

            // Intel hybride : 8 cœurs P avec SMT (logiques 0..15), 8 cœurs E (16..23), un seul L3.
            var hyb = new List<Topologie.Proc>();
            for (int i = 0; i < 16; i++) hyb.Add(P(i, i / 2, 1, 0, 30 * Mo));
            for (int i = 16; i < 24; i++) hyb.Add(P(i, 8 + (i - 16), 0, 0, 30 * Mo));
            Banc.Verifie("hybride : cœurs P seulement", true, Egal(Topologie.Preferes(hyb), Enumerable.Range(0, 16)));

            // X3D à deux CCD : 96 Mo sur 0..15, 32 Mo sur 16..31.
            var x3d = new List<Topologie.Proc>();
            for (int i = 0; i < 32; i++) x3d.Add(P(i, i / 2, 0, i < 16 ? 0 : 1, i < 16 ? 96 * Mo : 32 * Mo));
            Banc.Verifie("X3D : CCD à V-Cache", true, Egal(Topologie.Preferes(x3d), Enumerable.Range(0, 16)));

            var x3dInv = new List<Topologie.Proc>();
            for (int i = 0; i < 32; i++) x3dInv.Add(P(i, i / 2, 0, i < 16 ? 0 : 1, i < 16 ? 32 * Mo : 96 * Mo));
            Banc.Verifie("X3D inversé : CCD à V-Cache en second", true, Egal(Topologie.Preferes(x3dInv), Enumerable.Range(16, 16)));

            // Deux CCD de même cache (7950X) : règle homogène, pas un seul CCD.
            var deuxCcd = new List<Topologie.Proc>();
            for (int i = 0; i < 32; i++) deuxCcd.Add(P(i, i / 2, 0, i < 16 ? 0 : 1, 32 * Mo));
            Banc.Verifie("deux CCD identiques : hors cœur 0", true, Egal(Topologie.Preferes(deuxCcd), Enumerable.Range(2, 30)));

            var smt = new List<Topologie.Proc>();
            for (int i = 0; i < 16; i++) smt.Add(P(i, i / 2, 0, 0, 16 * Mo));
            Banc.Verifie("homogène SMT : les deux logiques du cœur 0 écartés", true, Egal(Topologie.Preferes(smt), Enumerable.Range(2, 14)));

            var sansSmt = new List<Topologie.Proc>();
            for (int i = 0; i < 6; i++) sansSmt.Add(P(i, i, 0, 0, 16 * Mo));
            Banc.Verifie("homogène sans SMT : processeur 0 écarté", true, Egal(Topologie.Preferes(sansSmt), Enumerable.Range(1, 5)));

            var unCoeur = new List<Topologie.Proc> { P(0, 0, 0, 0, 4 * Mo), P(1, 0, 0, 0, 4 * Mo) };
            Banc.Verifie("un seul cœur : aucune restriction", true, Topologie.Preferes(unCoeur).Count == 0);
            Banc.Verifie("liste vide : aucune restriction", true, Topologie.Preferes(new List<Topologie.Proc>()).Count == 0);
            Banc.Verifie("null : aucune restriction", true, Topologie.Preferes(null).Count == 0);

            var grand = new List<Topologie.Proc>(smt) { P(64, 8, 0, 0, 16 * Mo) };
            Banc.Verifie("index ≥ 64 : aucune restriction", true, Topologie.Preferes(grand).Count == 0);

            Banc.Verifie("libellé hybride", true, Topologie.Libelle(hyb).StartsWith("Intel hybride"));
            Banc.Verifie("libellé X3D", true, Topologie.Libelle(x3d).StartsWith("X3D"));

            // Logiciels de fond : l'autre CCD, sur X3D seulement.
            Banc.Verifie("X3D : autres = CCD sans V-Cache", true, Egal(Topologie.Autres(x3d), Enumerable.Range(16, 16)));
            Banc.Verifie("X3D inversé : autres = premier CCD", true, Egal(Topologie.Autres(x3dInv), Enumerable.Range(0, 16)));
            Banc.Verifie("hybride : rien à déplacer (cœurs E trop lents pour le vocal et l'overlay)", true, Topologie.Autres(hyb).Count == 0);
            Banc.Verifie("deux CCD de même cache : rien à déplacer", true, Topologie.Autres(deuxCcd).Count == 0);
            Banc.Verifie("null : rien à déplacer", true, Topologie.Autres(null).Count == 0);
            Banc.Verifie("libellé homogène", true, Topologie.Libelle(smt).StartsWith("homogène"));
        }
    }
}
