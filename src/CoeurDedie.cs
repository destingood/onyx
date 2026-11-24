using System.Collections.Generic;
using System.Linq;

namespace BTOptimizer
{
    /// <summary>
    /// CARTE GRAPHIQUE SUR UN CŒUR DÉDIÉ — la moitié PURE (testée au banc). Le registre vit dans
    /// IrqTuning.cs.
    ///
    /// « Répartir » (politique 5) ne fait rien sur une carte qui n'expose qu'un seul vecteur
    /// d'interruption : tout reste sur le cœur 0, où tourne le fil principal du jeu. Ce qui marche
    /// dans ce cas, c'est de DÉSIGNER le cœur : politique 4 (IrqPolicySpecifiedProcessors) et un
    /// masque de processeurs dans AssignmentSetOverride (Microsoft, « Interrupt Affinity and
    /// Priority »).
    ///
    /// Constaté sur la machine de test (Ryzen 9 9950X3D, RTX 4080 SUPER, vecteur unique) : après
    /// redémarrage, 65 % des travaux différés de la machine tombent sur le processeur logique 14
    /// au lieu du cœur 0. Le gain en latence ressentie n'a pas été mesuré.
    /// </summary>
    internal static class CoeurDedie
    {
        /// <summary>IrqPolicySpecifiedProcessors : « ces processeurs-là, et pas d'autres ».</summary>
        public const int PolitiqueChoisie = 4;

        /// <summary>
        /// Processeur logique où envoyer les interruptions de la carte graphique : le premier fil
        /// du DERNIER cœur parmi ceux que le jeu préfère (CCD V-Cache d'un X3D, cœurs P d'un Intel
        /// hybride, tout sauf le cœur 0 ailleurs). Loin du cœur 0, et jamais sur un cœur que
        /// Windows met en veille pendant le jeu. −1 si la topologie ne permet aucun choix.
        /// </summary>
        public static int Choisit(IList<Topologie.Proc> procs)
        {
            List<int> preferes = Topologie.Preferes(procs);
            if (preferes.Count == 0) return -1;
            var garde = new HashSet<int>(preferes);
            int coeur = procs.Where(p => garde.Contains(p.Index)).OrderByDescending(p => p.Index).First().Coeur;
            return procs.Where(p => p.Coeur == coeur && garde.Contains(p.Index)).Min(p => p.Index);
        }

        /// <summary>Masque de processeurs (KAFFINITY, 8 octets petit-boutiste) pour un seul
        /// processeur logique. null hors de 0 à 63.</summary>
        public static byte[] Masque(int logique)
        {
            if (logique < 0 || logique > 63) return null;
            var m = new byte[8];
            m[logique / 8] = (byte)(1 << (logique % 8));
            return m;
        }

        /// <summary>Le périphérique est-il épinglé sur des processeurs choisis ? Il faut la
        /// politique 4 ET un masque non vide : l'un sans l'autre ne désigne rien.</summary>
        public static bool Epingle(int? politique, byte[] masque)
        {
            return politique == PolitiqueChoisie && masque != null && masque.Any(o => o != 0);
        }

        /// <summary>Deux masques désignent-ils les mêmes processeurs ? Les zéros de fin ne comptent pas.</summary>
        public static bool MemeMasque(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            int n = a.Length > b.Length ? a.Length : b.Length;
            for (int i = 0; i < n; i++)
                if ((i < a.Length ? a[i] : 0) != (i < b.Length ? b[i] : 0)) return false;
            return true;
        }

        /// <summary>
        /// COHABITATION avec « répartir les interruptions » : les deux réglages écrivent la même
        /// valeur (DevicePolicy). Le cœur dédié gagne : la répartition ne touche ni n'attend un
        /// périphérique épinglé, pas plus qu'un périphérique à vecteur unique où elle ne peut rien.
        /// </summary>
        public static bool ARepartir(int? vecteurs, bool epingle)
        {
            return !epingle && (!vecteurs.HasValue || vecteurs.Value > 1);
        }

        /// <summary>Index des cartes à épingler : les cartes dédiées ; s'il n'y en a aucune, toutes
        /// (la puce intégrée est alors la carte du jeu).</summary>
        public static List<int> Cartes(IList<string> noms)
        {
            var dediees = new List<int>();
            var toutes = new List<int>();
            if (noms == null) return toutes;
            for (int i = 0; i < noms.Count; i++)
            {
                if (string.IsNullOrEmpty(noms[i])) continue;
                toutes.Add(i);
                if (!ModeXbox.EstIntegree(noms[i])) dediees.Add(i);
            }
            return dediees.Count > 0 ? dediees : toutes;
        }
    }
}
