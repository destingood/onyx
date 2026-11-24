using System;
using System.Collections.Generic;
using System.Linq;

namespace BTOptimizer
{
    /// <summary>
    /// Quels processeurs logiques réserver à un jeu en mode « Auto ».
    /// Cette moitié est PURE (testée au banc) ; la lecture du processeur et l'application par
    /// CPU Sets vivent dans TopologieMachine.cs.
    ///
    /// Règle, dans l'ordre :
    /// 1. Intel hybride (classes d'efficacité différentes) → les cœurs de la classe la plus haute (P).
    /// 2. Sinon, caches L3 de tailles différentes (Ryzen X3D à deux CCD) → le CCD au plus grand L3.
    /// 3. Sinon, au moins deux cœurs → tous sauf le cœur physique qui porte le processeur 0.
    /// 4. Sinon → aucune restriction (liste vide).
    /// Un index ≥ 64 (machine à plusieurs groupes) → aucune restriction : mieux vaut ne rien
    /// appliquer qu'appliquer un choix faux.
    /// </summary>
    internal static partial class Topologie
    {
        public sealed class Proc
        {
            public int Index;       // processeur logique (groupe 0)
            public int Coeur;       // identifiant du cœur physique
            public int Efficacite;  // EfficiencyClass : plus haut = plus performant
            public int L3;          // identifiant du groupe de cache L3
            public long L3Octets;
        }

        private enum Cas { Aucun, Hybride, X3D, Homogene }

        private static Cas Classe(IList<Proc> procs)
        {
            if (procs == null || procs.Count == 0 || procs.Any(p => p.Index < 0 || p.Index >= 64)) return Cas.Aucun;
            if (procs.Select(p => p.Efficacite).Distinct().Count() > 1) return Cas.Hybride;
            if (procs.GroupBy(p => p.L3).Select(g => g.Max(p => p.L3Octets)).Distinct().Count() > 1) return Cas.X3D;
            if (procs.Select(p => p.Coeur).Distinct().Count() >= 2) return Cas.Homogene;
            return Cas.Aucun;
        }

        /// <summary>Index triés des processeurs logiques à préférer. Vide = aucune restriction.</summary>
        public static List<int> Preferes(IList<Proc> procs)
        {
            IEnumerable<Proc> garde;
            switch (Classe(procs))
            {
                case Cas.Hybride:
                    int max = procs.Max(p => p.Efficacite);
                    garde = procs.Where(p => p.Efficacite == max);
                    break;
                case Cas.X3D:
                    int l3 = procs.GroupBy(p => p.L3).OrderByDescending(g => g.Max(p => p.L3Octets)).First().Key;
                    garde = procs.Where(p => p.L3 == l3);
                    break;
                case Cas.Homogene:
                    int coeur0 = procs.OrderBy(p => p.Index).First().Coeur;
                    garde = procs.Where(p => p.Coeur != coeur0);
                    break;
                default:
                    return new List<int>();
            }
            return garde.Select(p => p.Index).Distinct().OrderBy(i => i).ToList();
        }

        /// <summary>
        /// Processeurs où envoyer les logiciels de fond pendant le jeu : l'autre CCD d'un X3D, et
        /// rien ailleurs. Sur Intel hybride, les cœurs E serviraient mal le vocal et les overlays ;
        /// sur un processeur homogène, il ne resterait que le cœur 0, déjà chargé des interruptions.
        /// </summary>
        public static List<int> Autres(IList<Proc> procs)
        {
            if (Classe(procs) != Cas.X3D) return new List<int>();
            var jeu = new HashSet<int>(Preferes(procs));
            return procs.Select(p => p.Index).Where(i => !jeu.Contains(i)).Distinct().OrderBy(i => i).ToList();
        }

        /// <summary>Phrase courte pour le journal et la commande -topologie.</summary>
        public static string Libelle(IList<Proc> procs)
        {
            int n = Preferes(procs).Count;
            int total = procs == null ? 0 : procs.Count;
            switch (Classe(procs))
            {
                case Cas.Hybride: return "Intel hybride : " + n + " logiques P sur " + total;
                case Cas.X3D:
                    long o = procs.GroupBy(p => p.L3).Max(g => g.Max(p => p.L3Octets));
                    return "X3D : CCD " + (o / (1024 * 1024)) + " Mo (" + n + " logiques sur " + total + ")";
                case Cas.Homogene: return "homogène : cœur 0 écarté (" + n + " logiques sur " + total + ")";
                default: return "aucune restriction (topologie inconnue ou un seul cœur)";
            }
        }
    }
}
