using System;
using System.Drawing;

namespace BTOptimizer
{
    /// <summary>
    /// Définition PARTAGÉE des badges (id, apparence, palier, forme, métrique + seuil). Utilisé
    /// par la page Collection (rendu + progression) et par la notification (lookup id → affichage).
    /// </summary>
    internal static class BadgeCatalog
    {
        public sealed class Stats { public int OptiActive, OptiTotal, GamesDet, Health, Checkups; public bool Boost; }

        // shape : 0 hexagone · 1 cercle · 2 bouclier · 3 étoile · 4 losange
        public sealed class Badge
        {
            public string Id, Glyph, Name, Crit;
            public int Tier, Shape, Target, Page;   // Page = page du shell pour progresser (1 opti · 2 jeux · 3 check up)
            public Func<Stats, int> Metric;   // valeur courante (comparée à Target)

            public Badge(string id, string g, string n, string c, int tier, int shape, int target, int page, Func<Stats, int> metric)
            { Id = id; Glyph = g; Name = n; Crit = c; Tier = tier; Shape = shape; Target = target; Page = page; Metric = metric; }

            public bool Ok(Stats s) { return s != null && Metric(s) >= Target; }
            public float Progress(Stats s)
            {
                if (s == null || Target <= 0) return 0f;
                float f = (float)Metric(s) / Target; return f < 0 ? 0 : f > 1 ? 1 : f;
            }
            public string ProgressLabel(Stats s) { return s == null ? "" : Math.Min(Metric(s), Target) + " / " + Target; }
        }

        public static readonly Color[] TierColor =
        {
            Color.FromArgb(0, 255, 136),   // palier 1 — vert néon
            Color.FromArgb(0, 200, 255),   // palier 2 — cyan
            Color.FromArgb(255, 200, 60),  // palier 3 — or
        };

        public static readonly Badge[] All =
        {
            new Badge("premiers",   "🩹", "Premiers Soins",  "Applique 1 optimisation",    1, 0, 1,  1, s => s.OptiActive),
            new Badge("optimiseur", "🚀", "Optimiseur",      "15 optimisations actives",   1, 0, 15, 1, s => s.OptiActive),
            new Badge("chirurgien", "🔧", "Chirurgien",      "40 optimisations actives",   2, 2, 40, 1, s => s.OptiActive),
            new Badge("blinde",     "🛡", "Blindé",          "Santé du PC ≥ 60 %",         1, 2, 60, 1, s => s.Health),
            new Badge("perfect",    "🏆", "Perfectionniste", "Santé du PC ≥ 85 %",         3, 3, 85, 1, s => s.Health),
            new Badge("joueur",     "🎮", "Joueur",          "1 jeu détecté",              1, 1, 1,  2, s => s.GamesDet),
            new Badge("ludo",       "📚", "Ludothèque",      "4 jeux détectés",            2, 1, 4,  2, s => s.GamesDet),
            new Badge("modejeu",    "⚡", "Mode Jeu",        "Active le Mode Jeu",         1, 0, 1,  2, s => s.Boost ? 1 : 0),
            new Badge("infirmier",  "🩺", "Infirmier",       "1 Check Up réalisé",         1, 1, 1,  3, s => s.Checkups),
            new Badge("routine",    "💊", "Routine",         "5 Check Up réalisés",        2, 0, 5,  3, s => s.Checkups),
            new Badge("legende",    "💎", "Légende",         "40 opti · 85 % · jeu · Check Up", 3, 4, 4, 1,
                s => (s.OptiActive >= 40 ? 1 : 0) + (s.Health >= 85 ? 1 : 0) + (s.GamesDet >= 1 ? 1 : 0) + (s.Checkups >= 1 ? 1 : 0)),
        };

        public static Badge ById(string id)
        {
            foreach (Badge b in All) if (b.Id == id) return b;
            return null;
        }

        /// <summary>Évalue TOUS les badges contre un état complet et marque les nouveaux gagnés.</summary>
        public static void Evaluate(Stats s)
        {
            if (s == null) return;
            foreach (Badge b in All) { try { if (b.Ok(s)) BadgeStore.MarkEarned(b.Id); } catch { } }
        }

        /// <summary>Évalue les badges basés sur des ÉVÉNEMENTS (Check Up, Mode Jeu) contre les
        /// compteurs persistés — à appeler juste après une action, sans recalcul matériel coûteux.</summary>
        public static void EvaluateEvents()
        {
            Evaluate(new Stats { Checkups = BadgeStore.Checkups, Boost = BadgeStore.BoostUsed });
        }
    }
}
