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
            Color.FromArgb(129, 140, 248),   // palier 1 — vert néon
            Color.FromArgb(0, 200, 255),   // palier 2 — cyan
            Color.FromArgb(255, 200, 60),  // palier 3 — or
        };

        public static readonly Badge[] All =
        {
            // Optimisations (page 1)
            new Badge("premiers",   "🔧", "Premier réglage",  "Applique 1 optimisation",    1, 0, 1,   1, s => s.OptiActive),
            new Badge("optimiseur", "🚀", "Optimiseur",      "15 optimisations actives",   1, 0, 15,  1, s => s.OptiActive),
            new Badge("chirurgien", "🔩", "Mécano",      "40 optimisations actives",   2, 2, 40,  1, s => s.OptiActive),
            new Badge("bloc",       "⚙", "Salle des machines", "80 optimisations actives",   3, 2, 80,  1, s => s.OptiActive),
            new Badge("total",      "💯", "Contrôle total","120 optimisations actives",  3, 0, 120, 1, s => s.OptiActive),
            // Santé (page 1)
            new Badge("blinde",     "🛡", "Blindé",          "Santé du PC ≥ 60 %",         1, 2, 60,  1, s => s.Health),
            new Badge("coeur",      "❤",  "Cœur solide",     "Santé du PC ≥ 75 %",         2, 2, 75,  1, s => s.Health),
            new Badge("perfect",    "🏆", "Perfectionniste", "Santé du PC ≥ 85 %",         3, 3, 85,  1, s => s.Health),
            new Badge("immacule",   "🌟", "Immaculé",        "Santé du PC ≥ 95 %",         3, 3, 95,  1, s => s.Health),
            // Jeux (page 2)
            new Badge("joueur",     "🎮", "Joueur",          "1 jeu détecté",              1, 1, 1,   2, s => s.GamesDet),
            new Badge("ludo",       "📚", "Ludothèque",      "4 jeux détectés",            2, 1, 4,   2, s => s.GamesDet),
            new Badge("collec",     "🎯", "Collectionneur",  "10 jeux détectés",           2, 1, 10,  2, s => s.GamesDet),
            new Badge("grandludo",  "🗄", "Grande Ludothèque","20 jeux détectés",          3, 4, 20,  2, s => s.GamesDet),
            new Badge("modejeu",    "⚡", "Mode Jeu",        "Active le Mode Jeu",         1, 0, 1,   2, s => s.Boost ? 1 : 0),
            // Check Up (page 3)
            new Badge("infirmier",  "🔍", "Inspecteur",       "1 Check Up réalisé",         1, 1, 1,   3, s => s.Checkups),
            new Badge("routine",    "📋", "Routine",         "5 Check Up réalisés",        2, 0, 5,   3, s => s.Checkups),
            new Badge("vigilant",   "🔬", "Vigilant",        "10 Check Up réalisés",       2, 0, 10,  3, s => s.Checkups),
            new Badge("marathon",   "🏅", "Marathon santé",  "20 Check Up réalisés",       3, 3, 20,  3, s => s.Checkups),
            // Ultime
            new Badge("legende",    "💎", "Légende",         "80 opti · 95 % · 10 jeux · 10 Check Up", 3, 4, 4, 1,
                s => (s.OptiActive >= 80 ? 1 : 0) + (s.Health >= 95 ? 1 : 0) + (s.GamesDet >= 10 ? 1 : 0) + (s.Checkups >= 10 ? 1 : 0)),
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
