using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Carte graphique sur un cœur dédié : choix du cœur depuis la topologie, masque écrit dans
    /// le registre, et règle de cohabitation avec la répartition des interruptions.
    /// </summary>
    internal static class CoeurDedieTests
    {
        /// <summary>coeurs × fils logiques, à partir de l'index « debut », dans un groupe L3.</summary>
        private static void Ajoute(List<Topologie.Proc> l, int debut, int coeurs, int fils, int efficacite, int l3, long l3Octets)
        {
            for (int c = 0; c < coeurs; c++)
                for (int f = 0; f < fils; f++)
                    l.Add(new Topologie.Proc
                    {
                        Index = debut + c * fils + f, Coeur = 1000 * l3 + debut + c,
                        Efficacite = efficacite, L3 = l3, L3Octets = l3Octets
                    });
        }

        private static string Hex(byte[] b) { return b == null ? "(null)" : BitConverter.ToString(b); }

        public static void Tout()
        {
            Banc.Titre("Carte graphique sur un cœur dédié");

            // ---- Choix du cœur
            var x3d = new List<Topologie.Proc>();
            Ajoute(x3d, 0, 8, 2, 0, 0, 96L << 20);    // CCD V-Cache : logiques 0 à 15
            Ajoute(x3d, 16, 8, 2, 0, 1, 32L << 20);   // autre CCD : logiques 16 à 31
            Banc.Verifie("Ryzen 9 X3D (machine de test) : logique 14, dernier cœur du CCD V-Cache", true, CoeurDedie.Choisit(x3d) == 14);

            var hybride = new List<Topologie.Proc>();
            Ajoute(hybride, 0, 8, 2, 1, 0, 36L << 20);    // 8 cœurs P avec SMT : logiques 0 à 15
            Ajoute(hybride, 16, 16, 1, 0, 0, 36L << 20);  // 16 cœurs E
            Banc.Verifie("Intel hybride : premier fil du dernier cœur P, jamais un cœur E", true, CoeurDedie.Choisit(hybride) == 14);

            var homogene = new List<Topologie.Proc>();
            Ajoute(homogene, 0, 6, 1, 0, 0, 32L << 20);
            Banc.Verifie("6 cœurs sans SMT : logique 5", true, CoeurDedie.Choisit(homogene) == 5);

            var deux = new List<Topologie.Proc>();
            Ajoute(deux, 0, 2, 1, 0, 0, 8L << 20);
            Banc.Verifie("2 cœurs : logique 1 (hors du cœur 0)", true, CoeurDedie.Choisit(deux) == 1);

            var un = new List<Topologie.Proc>();
            Ajoute(un, 0, 1, 2, 0, 0, 8L << 20);
            Banc.Verifie("un seul cœur : aucun choix", true, CoeurDedie.Choisit(un) == -1);
            Banc.Verifie("topologie illisible (vide) : aucun choix", true, CoeurDedie.Choisit(new List<Topologie.Proc>()) == -1);
            Banc.Verifie("topologie absente (null) : aucun choix", true, CoeurDedie.Choisit(null) == -1);

            var groupes = new List<Topologie.Proc>();
            Ajoute(groupes, 0, 40, 2, 0, 0, 64L << 20);   // 80 logiques : plusieurs groupes de processeurs
            Banc.Verifie("plus de 64 logiques : aucun choix", true, CoeurDedie.Choisit(groupes) == -1);

            // ---- Masque (KAFFINITY sur 8 octets, petit-boutiste)
            Banc.Egal("logique 14 : le masque écrit à la main sur la machine de test", "00-40-00-00-00-00-00-00", Hex(CoeurDedie.Masque(14)));
            Banc.Egal("logique 0", "01-00-00-00-00-00-00-00", Hex(CoeurDedie.Masque(0)));
            Banc.Egal("logique 63", "00-00-00-00-00-00-00-80", Hex(CoeurDedie.Masque(63)));
            Banc.Egal("logique -1 : pas de masque", "(null)", Hex(CoeurDedie.Masque(-1)));
            Banc.Egal("logique 64 : pas de masque", "(null)", Hex(CoeurDedie.Masque(64)));

            // ---- Épinglé ? (politique 4 = processeurs choisis, avec un masque non vide)
            byte[] m14 = CoeurDedie.Masque(14);
            Banc.Verifie("politique 4 + masque : épinglé", true, CoeurDedie.Epingle(4, m14));
            Banc.Verifie("politique 5 (répartie) + masque oublié : pas épinglé", false, CoeurDedie.Epingle(5, m14));
            Banc.Verifie("politique 4 sans masque : pas épinglé", false, CoeurDedie.Epingle(4, null));
            Banc.Verifie("politique 4 + masque à zéro : pas épinglé", false, CoeurDedie.Epingle(4, new byte[8]));
            Banc.Verifie("aucune politique : pas épinglé", false, CoeurDedie.Epingle(null, m14));

            Banc.Verifie("même masque", true, CoeurDedie.MemeMasque(m14, new byte[] { 0, 0x40, 0, 0, 0, 0, 0, 0 }));
            Banc.Verifie("même masque, longueur différente (zéros de fin)", true, CoeurDedie.MemeMasque(m14, new byte[] { 0, 0x40 }));
            Banc.Verifie("autre cœur : masque différent", false, CoeurDedie.MemeMasque(m14, CoeurDedie.Masque(12)));
            Banc.Verifie("masque absent : différent", false, CoeurDedie.MemeMasque(m14, null));

            // ---- Cohabitation avec « répartir les interruptions »
            Banc.Verifie("plusieurs vecteurs, pas épinglé : à répartir", true, CoeurDedie.ARepartir(2048, false));
            Banc.Verifie("vecteurs non bornés, pas épinglé : à répartir", true, CoeurDedie.ARepartir(null, false));
            Banc.Verifie("un seul vecteur : rien à répartir", false, CoeurDedie.ARepartir(1, false));
            Banc.Verifie("épinglé sur un cœur : la répartition n'y touche pas", false, CoeurDedie.ARepartir(2048, true));

            // ---- Quelles cartes épingler
            Banc.Egal("puce intégrée listée avant la RTX (machine de test) : la RTX seule", "1",
                string.Join(",", CoeurDedie.Cartes(new[] { "AMD Radeon(TM) Graphics", "NVIDIA GeForce RTX 4080 SUPER" })));
            Banc.Egal("puce intégrée seule : c'est elle la carte du jeu", "0",
                string.Join(",", CoeurDedie.Cartes(new[] { "AMD Radeon(TM) Graphics" })));
            Banc.Egal("deux cartes dédiées : les deux", "0,1",
                string.Join(",", CoeurDedie.Cartes(new[] { "NVIDIA GeForce RTX 4080 SUPER", "AMD Radeon RX 7900 XT" })));
            Banc.Egal("aucune carte : rien", "", string.Join(",", CoeurDedie.Cartes(new string[0])));
            Banc.Egal("liste absente : rien", "", string.Join(",", CoeurDedie.Cartes(null)));
        }
    }
}
