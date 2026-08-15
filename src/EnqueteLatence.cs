using System;
using System.Collections.Generic;
using System.Globalization;

namespace BTOptimizer
{
    /// <summary>
    /// TROUVER D'OÙ VIENT LA LATENCE, ET LE PROUVER.
    ///
    /// Ce module existe parce qu'une session entière de mesures a montré qu'on se trompe presque
    /// toujours de chiffre, et toujours dans le sens qui flatte.
    ///
    /// TROIS COLONNES, TROIS PIÈGES :
    ///
    ///   « Pire temps » est un MAXIMUM SUR UN ÉCHANTILLON. Plus un pilote s'exécute, plus il a de
    ///   chances de tomber sur un cas extrême. Mesurer 10 secondes au lieu de 60 fait baisser ce
    ///   chiffre sans qu'une seule ligne de code ait changé. Constaté ici : un relevé au repos
    ///   donnait 0,524 ms, le même PC en jeu — avec quarante fois plus d'événements — donnait
    ///   0,319 ms. Le chiffre s'était amélioré parce que la machine travaillait DAVANTAGE.
    ///
    ///   « Temps total » suit l'ACTIVITÉ. Une machine qui ne fait rien gagne toujours. Le même PC
    ///   est passé de 391 ms à 5 130 ms en lançant un jeu : treize fois « pire », alors que rien
    ///   n'avait été dégradé.
    ///
    ///   « Nombre d'événements » ne dit rien à lui seul, mais c'est LUI qui permet de rendre les
    ///   deux autres comparables.
    ///
    /// CE QUE CE MODULE MESURE À LA PLACE :
    ///
    ///   µs PAR ÉVÉNEMENT — le travail que coûte réellement un DPC. Indépendant de l'activité :
    ///   c'est le seul chiffre que les réglages font bouger, et le seul qu'on puisse comparer
    ///   entre deux relevés pris dans des états différents.
    ///
    ///   % D'UN CŒUR — ce que la latence coûte vraiment à la machine, ici et maintenant.
    ///
    /// ET SURTOUT : il REFUSE de conclure quand les charges diffèrent trop. Rendre un verdict
    /// flatteur sur une comparaison invalide est la pire chose à faire — l'utilisateur garderait
    /// un réglage inutile en croyant l'avoir mesuré.
    /// </summary>
    internal static class EnqueteLatence
    {
        /// <summary>Un pilote dans un relevé.</summary>
        public sealed class Pilote
        {
            public string Nom = "";
            public long Evenements;      // ISR + DPC
            public double TotalMs;       // temps d'exécution cumulé
            public double PireMs;        // pire exécution unitaire

            /// <summary>Le chiffre qui ne dépend pas de l'activité : coût moyen d'un événement.</summary>
            public double MicrosParEvenement
            {
                get { return Evenements > 0 ? TotalMs / Evenements * 1000.0 : 0; }
            }
        }

        /// <summary>Un relevé complet, avec sa durée — sans elle, rien n'est comparable.</summary>
        public sealed class Releve
        {
            public DateTime Quand = DateTime.Now;
            public double Secondes;
            public string Etat = "";     // « au repos », « en jeu », … : dit par l'utilisateur
            public readonly List<Pilote> Pilotes = new List<Pilote>();

            public long Evenements
            {
                get { long n = 0; foreach (Pilote p in Pilotes) n += p.Evenements; return n; }
            }
            public double TotalMs
            {
                get { double t = 0; foreach (Pilote p in Pilotes) t += p.TotalMs; return t; }
            }
            /// <summary>Charge : c'est ELLE qui décide si deux relevés sont comparables.</summary>
            public double EvenementsParSeconde
            {
                get { return Secondes > 0 ? Evenements / Secondes : 0; }
            }
            /// <summary>Part d'UN cœur consommée par les DPC et interruptions.</summary>
            public double PourcentUnCoeur
            {
                get { return Secondes > 0 ? TotalMs / (Secondes * 1000.0) * 100.0 : 0; }
            }
            public double MicrosParEvenement
            {
                get { return Evenements > 0 ? TotalMs / Evenements * 1000.0 : 0; }
            }
        }

        // ==================================================================
        //  Comparabilité — la règle qui empêche de se mentir
        // ==================================================================

        /// <summary>Au-delà, l'écart de charge explique à lui seul les variations qu'on cherche
        /// à mesurer. Même seuil que DpcCompare, pour la même raison.</summary>
        public const double EcartMaximal = 0.20;

        /// <summary>PUR : écart de charge en proportion. -1 si l'une des deux est inconnue.</summary>
        public static double EcartDeCharge(double a, double b)
        {
            if (a <= 0 || b <= 0) return -1;
            double grand = Math.Max(a, b), petit = Math.Min(a, b);
            return (grand - petit) / grand;
        }

        /// <summary>PUR : ces deux relevés permettent-ils une conclusion ?</summary>
        public static bool Comparable(Releve avant, Releve apres)
        {
            if (avant == null || apres == null) return false;
            if (avant.Secondes <= 0 || apres.Secondes <= 0) return false;
            double e = EcartDeCharge(avant.EvenementsParSeconde, apres.EvenementsParSeconde);
            return e >= 0 && e <= EcartMaximal;
        }

        // ==================================================================
        //  Où va le temps
        // ==================================================================

        /// <summary>
        /// PUR : les pilotes classés par temps total décroissant, avec leur part.
        /// C'est la question « d'où vient la latence » — répondue par le volume de travail.
        /// </summary>
        public static List<Pilote> Coupables(Releve r)
        {
            var l = new List<Pilote>();
            if (r == null) return l;
            foreach (Pilote p in r.Pilotes) if (p.TotalMs > 0) l.Add(p);
            l.Sort((a, b) => b.TotalMs.CompareTo(a.TotalMs));
            return l;
        }

        /// <summary>PUR : part du temps noyau prise par ce pilote, en pourcentage.</summary>
        public static double Part(Pilote p, Releve r)
        {
            if (p == null || r == null || r.TotalMs <= 0) return 0;
            return p.TotalMs / r.TotalMs * 100.0;
        }

        /// <summary>
        /// PUR : combien de pilotes faut-il traiter pour couvrir <paramref name="cible"/> du temps ?
        ///
        /// Sert à dire « inutile de chercher ailleurs » : sur la machine de référence, DEUX
        /// pilotes représentaient 91,5 % du temps noyau. Optimiser le reste ne pouvait rien
        /// rapporter de visible.
        /// </summary>
        public static int CombienCouvrent(Releve r, double cible)
        {
            List<Pilote> c = Coupables(r);
            if (c.Count == 0 || r.TotalMs <= 0) return 0;
            double cumul = 0;
            for (int i = 0; i < c.Count; i++)
            {
                cumul += c[i].TotalMs / r.TotalMs;
                if (cumul >= cible) return i + 1;
            }
            return c.Count;
        }

        // ==================================================================
        //  Attribution — mesurer l'effet d'UN changement
        // ==================================================================

        public sealed class Verdict
        {
            public bool Concluant;          // a-t-on le droit de conclure ?
            public string Refus;            // si non : pourquoi
            public double VariationPct;     // sur les µs/événement (négatif = mieux)
            public double AvantUs, ApresUs;
            public double EcartChargePct;
            public string Texte = "";
        }

        /// <summary>
        /// PUR : qu'a produit le changement, entre ces deux relevés ?
        ///
        /// Compare les µs PAR ÉVÉNEMENT, pas les totaux : deux relevés n'ont jamais exactement la
        /// même activité, et comparer des totaux reviendrait à récompenser l'inactivité.
        /// </summary>
        public static Verdict Attribue(Releve avant, Releve apres, string quoi)
        {
            var v = new Verdict();
            if (avant == null || apres == null)
            {
                v.Refus = "Il manque un relevé.";
                v.Texte = v.Refus;
                return v;
            }

            v.AvantUs = avant.MicrosParEvenement;
            v.ApresUs = apres.MicrosParEvenement;
            double ec = EcartDeCharge(avant.EvenementsParSeconde, apres.EvenementsParSeconde);
            v.EcartChargePct = ec < 0 ? -1 : ec * 100.0;

            if (!Comparable(avant, apres))
            {
                v.Refus = ec < 0
                    ? "l'une des deux charges n'a pas pu être lue"
                    : "les charges diffèrent de " + (ec * 100).ToString("0") + " %";
                v.Texte =
                    "COMPARAISON REFUSÉE — " + v.Refus + ".\n\n"
                  + "Les deux relevés n'ont pas été pris dans le même état de machine : "
                  + avant.EvenementsParSeconde.ToString("#,0") + " événements/s d'un côté, "
                  + apres.EvenementsParSeconde.ToString("#,0") + " de l'autre.\n\n"
                  + "Refais-les à l'identique — même durée, même scène de jeu ou même bureau au "
                  + "repos. Tant que l'écart dépasse " + (EcartMaximal * 100).ToString("0")
                  + " %, aucun verdict ne serait honnête.";
                return v;
            }

            v.Concluant = true;
            if (v.AvantUs > 0) v.VariationPct = (v.ApresUs / v.AvantUs - 1.0) * 100.0;

            string nom = string.IsNullOrEmpty(quoi) ? "Ce changement" : "« " + quoi + " »";
            string sens = v.VariationPct < -5 ? "GAIN" : v.VariationPct > 5 ? "PERTE" : "AUCUN EFFET";
            string detail = v.VariationPct < -5
                ? nom + " fait baisser le coût d'un événement de " + (-v.VariationPct).ToString("0")
                      + " %, à charge comparable. Garde-le."
                : v.VariationPct > 5
                ? nom + " fait MONTER le coût d'un événement de " + v.VariationPct.ToString("0")
                      + " %. Annule-le."
                : nom + " ne change rien de mesurable (" + v.VariationPct.ToString("+0;-0;0")
                      + " %). Garde-le ou non, mais ne compte pas dessus.";

            v.Texte =
                "AVANT   " + v.AvantUs.ToString("0.0") + " µs/événement   ("
                + avant.EvenementsParSeconde.ToString("#,0") + " évts/s · "
                + avant.PourcentUnCoeur.ToString("0.00") + " % d'un cœur)\n"
              + "APRÈS   " + v.ApresUs.ToString("0.0") + " µs/événement   ("
                + apres.EvenementsParSeconde.ToString("#,0") + " évts/s · "
                + apres.PourcentUnCoeur.ToString("0.00") + " % d'un cœur)\n\n"
              + sens + " — charges comparables (écart " + v.EcartChargePct.ToString("0") + " %).\n\n"
              + detail;
            return v;
        }

        // ==================================================================
        //  Sérialisation — un relevé doit survivre à un redémarrage
        // ==================================================================

        public static string Serialise(Releve r)
        {
            if (r == null) return "";
            var c = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append(r.Quand.ToString("o", c)).Append('\n');
            sb.Append(r.Secondes.ToString(c)).Append('\n');
            sb.Append((r.Etat ?? "").Replace("\n", " ")).Append('\n');
            foreach (Pilote p in r.Pilotes)
                sb.Append(p.Nom.Replace(";", "")).Append(';')
                  .Append(p.Evenements.ToString(c)).Append(';')
                  .Append(p.TotalMs.ToString(c)).Append(';')
                  .Append(p.PireMs.ToString(c)).Append('\n');
            return sb.ToString();
        }

        /// <summary>Lecture PURE. null si inexploitable — jamais un relevé partiel, qui
        /// produirait une comparaison fausse plutôt qu'une absence de comparaison.</summary>
        public static Releve Analyse(string contenu)
        {
            if (string.IsNullOrEmpty(contenu)) return null;
            string[] l = contenu.Replace("\r", "").Split('\n');
            if (l.Length < 4) return null;
            var c = CultureInfo.InvariantCulture;
            try
            {
                DateTime q; double s;
                if (!DateTime.TryParse(l[0], c, DateTimeStyles.RoundtripKind, out q)) return null;
                if (!double.TryParse(l[1], NumberStyles.Float, c, out s) || s <= 0) return null;
                var r = new Releve { Quand = q, Secondes = s, Etat = l[2] };
                for (int i = 3; i < l.Length; i++)
                {
                    if (l[i].Trim().Length == 0) continue;
                    string[] p = l[i].Split(';');
                    if (p.Length < 4) continue;
                    long ev; double tot, pire;
                    if (!long.TryParse(p[1], NumberStyles.Integer, c, out ev)) continue;
                    if (!double.TryParse(p[2], NumberStyles.Float, c, out tot)) continue;
                    if (!double.TryParse(p[3], NumberStyles.Float, c, out pire)) continue;
                    r.Pilotes.Add(new Pilote { Nom = p[0], Evenements = ev, TotalMs = tot, PireMs = pire });
                }
                return r.Pilotes.Count > 0 ? r : null;
            }
            catch { return null; }
        }
    }
}
