using System;

namespace BTOptimizer
{
    /// <summary>Disposition des cartes d'Optimisations, sans WinForms : colonnes de 360 px minimum,
    /// rangées aussi hautes que leur plus haute carte, écart de 16 px. Sert à la liste virtualisée
    /// (seules les cartes visibles sont dessinées) et au test de clic.</summary>
    internal static class GrilleCartes
    {
        public struct Case { public int X, Y, W, H; }
        public const int Ecart = 16, LargeurMin = 360;

        public static int Colonnes(int largeurDispo) { return Math.Max(1, largeurDispo / LargeurMin); }

        public static int LargeurCarte(int largeurDispo) { return Math.Max(1, Math.Max(0, largeurDispo) / Colonnes(largeurDispo) - Ecart); }

        public static Case[] Place(int largeurDispo, int[] hauteurs, out int hauteurTotale)
        {
            int n = hauteurs == null ? 0 : hauteurs.Length;
            var cases = new Case[n];
            int cols = Colonnes(largeurDispo), w = LargeurCarte(largeurDispo), y = 0;
            for (int debut = 0; debut < n; debut += cols)
            {
                int haut = 0;
                for (int i = debut; i < Math.Min(n, debut + cols); i++)
                {
                    cases[i] = new Case { X = (i - debut) * (w + Ecart), Y = y, W = w, H = hauteurs[i] };
                    haut = Math.Max(haut, hauteurs[i]);
                }
                y += haut + Ecart;
            }
            hauteurTotale = y;
            return cases;
        }

        /// <summary>Plage [premier, dernier] des cartes qui croisent la vue ; aucune → dernier &lt; premier.</summary>
        public static void Visibles(Case[] cases, int defilement, int hauteurVue, out int premier, out int dernier)
        {
            premier = 0; dernier = -1;
            if (cases == null) return;
            int bas = defilement + hauteurVue;
            bool trouve = false;
            for (int i = 0; i < cases.Length; i++)
            {
                if (cases[i].Y + cases[i].H <= defilement || cases[i].Y >= bas) { if (trouve && cases[i].Y >= bas) break; continue; }
                if (!trouve) { premier = i; trouve = true; }
                dernier = i;
            }
            if (!trouve) { premier = cases.Length; dernier = cases.Length - 1; }
        }

        public static int IndexA(Case[] cases, int x, int y)
        {
            if (cases == null) return -1;
            for (int i = 0; i < cases.Length; i++)
            {
                Case c = cases[i];
                if (x >= c.X && x < c.X + c.W && y >= c.Y && y < c.Y + c.H) return i;
            }
            return -1;
        }
    }
}
