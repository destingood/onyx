using System;
using System.Collections.Generic;
using System.Globalization;

namespace BTOptimizer
{
    /// <summary>
    /// BILAN DE JEU — le résumé de Razer Cortex (« game summary »), tiré du journal des parties
    /// d'ONYX (<see cref="JournalJeu"/>) : temps joué, nombre de parties, jeux les plus joués, FPS
    /// moyens, plus longue partie, jour préféré, écart avec la période d'avant.
    ///
    /// PUR : mêmes parties et même « maintenant », même bilan. Les succès Steam ont leur propre
    /// fenêtre (SuccesSteam, lus dans le cache local de Steam).
    /// </summary>
    internal static class BilanJeu
    {
        /// <summary>Un jeu de la période.</summary>
        public sealed class Ligne
        {
            public string Jeu;
            public int Secondes;
            public int Parties;
            /// <summary>Moyenne des parties mesurées, pondérée par leur durée ; 0 si aucune mesurée.</summary>
            public double FpsMoyen;
            public DateTime Derniere;
        }

        public sealed class Resultat
        {
            /// <summary>0 = depuis le début du journal.</summary>
            public int Jours;
            public DateTime Depuis;
            public int Secondes;
            public int Parties;
            /// <summary>Secondes jouées sur la période de même longueur juste avant ; -1 si sans objet (« tout »).</summary>
            public int SecondesAvant = -1;
            public List<Ligne> Jeux = new List<Ligne>();
            public JournalJeu.Partie PlusLongue;
            /// <summary>Jour de la semaine le plus joué (en secondes) ; null sans partie.</summary>
            public DayOfWeek? JourPrefere;
            /// <summary>Jours consécutifs joués jusqu'à aujourd'hui (ou hier : la série n'est pas perdue avant minuit).</summary>
            public int Serie;
        }

        /// <summary>Bilan des <paramref name="jours"/> derniers jours, aujourd'hui compris (0 = tout le journal).</summary>
        public static Resultat Calcule(List<JournalJeu.Partie> parties, DateTime maintenant, int jours)
        {
            var r = new Resultat { Jours = Math.Max(0, jours) };
            r.Depuis = r.Jours > 0 ? maintenant.Date.AddDays(-(r.Jours - 1)) : DateTime.MinValue;
            if (r.Jours > 0) r.SecondesAvant = 0;
            DateTime avant = r.Jours > 0 ? r.Depuis.AddDays(-r.Jours) : DateTime.MinValue;

            var parJeu = new Dictionary<string, Ligne>(StringComparer.OrdinalIgnoreCase);
            var fpsPoids = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var fpsSecondes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var parJour = new long[7];
            var joues = new HashSet<DateTime>();

            if (parties != null)
                foreach (JournalJeu.Partie p in parties)
                {
                    if (p == null || string.IsNullOrEmpty(p.Jeu) || p.Secondes <= 0 || p.Debut > maintenant) continue;
                    joues.Add(p.Debut.Date);
                    if (r.Jours > 0 && p.Debut < r.Depuis)
                    {
                        if (p.Debut >= avant) r.SecondesAvant += p.Secondes;
                        continue;
                    }
                    r.Secondes += p.Secondes; r.Parties++;
                    parJour[(int)p.Debut.DayOfWeek] += p.Secondes;
                    if (r.PlusLongue == null || p.Secondes > r.PlusLongue.Secondes) r.PlusLongue = p;

                    Ligne l;
                    if (!parJeu.TryGetValue(p.Jeu, out l)) { l = new Ligne { Jeu = p.Jeu }; parJeu[p.Jeu] = l; fpsPoids[p.Jeu] = 0; fpsSecondes[p.Jeu] = 0; }
                    l.Secondes += p.Secondes; l.Parties++;
                    DateTime fin = p.Debut.AddSeconds(p.Secondes);
                    if (fin > l.Derniere) l.Derniere = fin;
                    if (p.FpsMoyen > 0) { fpsPoids[p.Jeu] += p.FpsMoyen * p.Secondes; fpsSecondes[p.Jeu] += p.Secondes; }
                }

            foreach (Ligne l in parJeu.Values)
            {
                if (fpsSecondes[l.Jeu] > 0) l.FpsMoyen = fpsPoids[l.Jeu] / fpsSecondes[l.Jeu];
                r.Jeux.Add(l);
            }
            r.Jeux.Sort((a, b) => a.Secondes != b.Secondes ? b.Secondes.CompareTo(a.Secondes) : string.Compare(a.Jeu, b.Jeu, StringComparison.OrdinalIgnoreCase));

            long max = 0;
            for (int d = 0; d < 7; d++) if (parJour[d] > max) { max = parJour[d]; r.JourPrefere = (DayOfWeek)d; }

            DateTime jour = maintenant.Date;
            if (!joues.Contains(jour)) jour = jour.AddDays(-1);
            while (joues.Contains(jour)) { r.Serie++; jour = jour.AddDays(-1); }
            return r;
        }

        private static readonly string[] Jours = { "dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi" };

        public static string NomJour(DayOfWeek d) { return Jours[(int)d]; }

        /// <summary>« 7 derniers jours », « 30 derniers jours », « depuis le début ».</summary>
        public static string NomPeriode(int jours)
        {
            return jours <= 0 ? "depuis le début" : jours == 1 ? "aujourd'hui" : jours + " derniers jours";
        }

        /// <summary>« +35 % par rapport aux 7 jours d'avant » ; null si rien à comparer.</summary>
        public static string Ecart(Resultat r)
        {
            if (r == null || r.Jours <= 0 || r.SecondesAvant <= 0) return null;
            int pct = (int)Math.Round((r.Secondes - r.SecondesAvant) * 100.0 / r.SecondesAvant);
            if (pct == 0) return "autant que les " + r.Jours + " jours d'avant";
            return (pct > 0 ? "+" : "−") + Math.Abs(pct) + " % par rapport aux " + r.Jours + " jours d'avant";
        }

        /// <summary>Lignes du résumé, dans l'ordre d'affichage. Journal vide : une seule ligne qui dit pourquoi.</summary>
        public static List<string> Resume(Resultat r)
        {
            var l = new List<string>();
            if (r == null || r.Parties == 0)
            {
                l.Add(r != null && r.Jours > 0
                    ? "Aucune partie sur les " + NomPeriode(r.Jours) + "."
                    : "Aucune partie enregistrée. ONYX note chaque partie d'au moins une minute d'un jeu de « Mes jeux », ONYX ouvert.");
                return l;
            }
            string temps = JournalJeu.Duree(r.Secondes) + " de jeu, " + r.Parties + (r.Parties > 1 ? " parties" : " partie")
                         + ", " + r.Jeux.Count + (r.Jeux.Count > 1 ? " jeux" : " jeu");
            string e = Ecart(r);
            l.Add(e != null ? temps + " (" + e + ")." : temps + ".");
            if (r.PlusLongue != null)
                l.Add("Plus longue partie : " + JournalJeu.Duree(r.PlusLongue.Secondes) + " sur " + r.PlusLongue.Jeu
                    + ", le " + r.PlusLongue.Debut.ToString("dd'/'MM", CultureInfo.InvariantCulture) + ".");
            if (r.JourPrefere.HasValue && r.Parties > 1) l.Add("Jour où tu joues le plus : le " + NomJour(r.JourPrefere.Value) + ".");
            if (r.Serie > 1) l.Add("Série en cours : " + r.Serie + " jours de suite.");
            return l;
        }

        /// <summary>Titre de la carte image : « Bilan de jeu · 7 derniers jours ».</summary>
        public static string CarteTitre(Resultat r)
        {
            return "Bilan de jeu · " + NomPeriode(r == null ? 0 : r.Jours);
        }

        /// <summary>Sous le grand chiffre : « de jeu · 23 parties · 8 jeux ».</summary>
        public static string CarteSous(Resultat r)
        {
            if (r == null || r.Parties == 0) return "aucune partie sur la période";
            return "de jeu · " + r.Parties + (r.Parties > 1 ? " parties" : " partie") + " · " + r.Jeux.Count + (r.Jeux.Count > 1 ? " jeux" : " jeu");
        }

        /// <summary>Faits courts de la carte, trois au plus : écart, plus longue partie, série.</summary>
        public static List<string> CarteFaits(Resultat r)
        {
            var l = new List<string>();
            if (r == null || r.Parties == 0) return l;
            string e = Ecart(r);
            if (e != null) l.Add(char.ToUpperInvariant(e[0]) + e.Substring(1));
            if (r.PlusLongue != null) l.Add("Plus longue partie : " + JournalJeu.Duree(r.PlusLongue.Secondes) + " sur " + r.PlusLongue.Jeu);
            if (r.Serie > 1) l.Add("Série : " + r.Serie + " jours de suite");
            else if (r.JourPrefere.HasValue && r.Parties > 1) l.Add("Jour préféré : le " + NomJour(r.JourPrefere.Value));
            return l;
        }

        /// <summary>Nom de fichier proposé : « ONYX-bilan-7-jours-2026-11-27.png », « ONYX-bilan-complet-… ».</summary>
        public static string NomImage(int jours, DateTime maintenant)
        {
            return "ONYX-bilan-" + (jours > 0 ? jours + "-jours" : "complet") + "-" + maintenant.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture) + ".png";
        }

        /// <summary>« 12 h 30 · 8 parties · 236 FPS en moyenne » pour une ligne de jeu.</summary>
        public static string Detail(Ligne g)
        {
            if (g == null) return "";
            string s = JournalJeu.Duree(g.Secondes) + " · " + g.Parties + (g.Parties > 1 ? " parties" : " partie");
            if (g.FpsMoyen > 0) s += " · " + Math.Round(g.FpsMoyen).ToString(CultureInfo.InvariantCulture) + " FPS en moyenne";
            return s;
        }
    }
}
