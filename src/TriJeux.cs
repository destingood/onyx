using System;

namespace BTOptimizer
{
    /// <summary>
    /// ORDRE DE LA BIBLIOTHÈQUE (logique PURE) : les favoris ★ toujours en tête, puis soit l'ordre
    /// alphabétique, soit « joués récemment » (dernière partie lue dans le journal de jeu, du plus
    /// récent au plus ancien ; un jeu jamais vu au journal passe à la fin). Le nom départage.
    /// </summary>
    internal static class TriJeux
    {
        public struct Entree { public bool Favori; public DateTime? Derniere; public string Nom; }

        public static int Compare(Entree a, Entree b, bool recents)
        {
            if (a.Favori != b.Favori) return a.Favori ? -1 : 1;
            if (recents)
            {
                if (a.Derniere.HasValue != b.Derniere.HasValue) return a.Derniere.HasValue ? -1 : 1;
                if (a.Derniere.HasValue && a.Derniere.Value != b.Derniere.Value) return b.Derniere.Value.CompareTo(a.Derniere.Value);
            }
            return StringComparer.CurrentCultureIgnoreCase.Compare(a.Nom ?? "", b.Nom ?? "");
        }
    }
}
