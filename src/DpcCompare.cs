using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// AVANT / APRÈS — et le refus de conclure quand la comparaison ne veut rien dire.
    ///
    /// Comparer deux mesures de latence est un piège, et il se referme à chaque fois de la même
    /// façon. Le « pire temps d'exécution » est un MAXIMUM SUR UN ÉCHANTILLON : plus un pilote
    /// s'exécute, plus il a de chances de tomber sur un cas extrême. Une machine moins occupée rend
    /// donc de meilleurs chiffres SANS QU'AUCUN RÉGLAGE N'AIT CHANGÉ.
    ///
    /// Constaté sur une machine réelle, en confrontant deux mesures encadrant un réglage : sur neuf
    /// pilotes, HUIT voyaient leur pire temps varier dans le même sens que leur nombre
    /// d'exécutions. Les deux seuls dont le nombre d'événements montait étaient les deux seuls dont
    /// le pire temps montait. Le réglage n'y était probablement pour rien — mais lu naïvement, le
    /// tableau donnait l'impression d'un franc progrès.
    ///
    /// Ce module compare deux relevés ET REFUSE DE CONCLURE quand les charges diffèrent trop. Rendre
    /// un verdict flatteur sur une comparaison invalide serait la pire chose à faire : l'utilisateur
    /// garderait un réglage inutile en croyant l'avoir mesuré.
    /// </summary>
    internal static class DpcCompare
    {
        /// <summary>Un relevé conservé, réduit à ce qui permet de comparer.</summary>
        public sealed class Releve
        {
            public DateTime Quand;
            public double Secondes;
            public double PireMs;              // pire exécution, tous pilotes confondus
            public double EvenementsParSeconde; // la charge : c'est ELLE qui décide si on peut comparer
            public string PirePilote = "";
        }

        /// <summary>
        /// Écart de charge PUR, en proportion. 0 = charges identiques, 0,5 = 50 % d'écart.
        /// Rend -1 si l'une des deux charges est inconnue : on ne compare pas dans le vide.
        /// </summary>
        public static double EcartDeCharge(double a, double b)
        {
            if (a <= 0 || b <= 0) return -1;
            double grand = Math.Max(a, b), petit = Math.Min(a, b);
            return (grand - petit) / grand;
        }

        /// <summary>
        /// PUR : ces deux relevés sont-ils comparables ?
        ///
        /// Le seuil est à 20 %. Au-delà, l'écart de charge explique à lui seul des variations de
        /// pire temps du même ordre que celles qu'on cherche à mesurer — la comparaison ne prouve
        /// plus rien, quel que soit le résultat.
        /// </summary>
        public static bool Comparable(Releve avant, Releve apres)
        {
            if (avant == null || apres == null) return false;
            double e = EcartDeCharge(avant.EvenementsParSeconde, apres.EvenementsParSeconde);
            return e >= 0 && e <= 0.20;
        }

        /// <summary>Variation PURE du pire temps, en pourcentage (négatif = amélioration).</summary>
        public static double Variation(double avant, double apres)
        {
            if (avant <= 0 || apres <= 0) return 0;
            return (apres / avant - 1.0) * 100.0;
        }

        /// <summary>
        /// Verdict PUR. Ne dit JAMAIS « c'est mieux » sur une comparaison invalide.
        /// </summary>
        public static string Verdict(Releve avant, Releve apres)
        {
            if (avant == null) return "Aucun relevé de référence : lance d'abord une mesure « avant ».";
            if (apres == null) return "Aucune mesure courante.";

            string entete =
                "AVANT   " + avant.PireMs.ToString("0.000") + " ms   ("
                + avant.EvenementsParSeconde.ToString("0") + " événements/s, "
                + avant.Quand.ToString("dd/MM HH:mm") + ")\n"
                + "APRÈS   " + apres.PireMs.ToString("0.000") + " ms   ("
                + apres.EvenementsParSeconde.ToString("0") + " événements/s, "
                + apres.Quand.ToString("dd/MM HH:mm") + ")\n\n";

            double ec = EcartDeCharge(avant.EvenementsParSeconde, apres.EvenementsParSeconde);
            if (!Comparable(avant, apres))
            {
                string cause = ec < 0
                    ? "l'une des deux charges n'a pas pu être lue"
                    : "les charges diffèrent de " + (ec * 100).ToString("0") + " %";
                return entete
                     + "COMPARAISON REFUSÉE — " + cause + ".\n\n"
                     + "Le pire temps d'exécution est un maximum sur un échantillon : plus un pilote "
                     + "s'exécute, plus il a de chances de tomber sur un cas extrême. Une machine "
                     + "moins occupée rend donc de meilleurs chiffres sans qu'aucun réglage n'ait "
                     + "changé.\n\nRefais les deux mesures dans le MÊME état : un jeu qui tourne sur "
                     + "la même scène des deux côtés, ou un bureau strictement au repos. Tant que "
                     + "l'écart de charge dépasse 20 %, aucun verdict ne serait honnête.";
            }

            double v = Variation(avant.PireMs, apres.PireMs);
            string sens = v < -10 ? "AMÉLIORATION" : v > 10 ? "RÉGRESSION" : "PAS DE CHANGEMENT NET";
            string detail = v < -10
                ? "Le pire temps baisse de " + (-v).ToString("0") + " % à charge comparable : "
                  + "cette fois, ça compte."
                : v > 10
                ? "Le pire temps monte de " + v.ToString("0") + " % à charge comparable. "
                  + "Le réglage que tu viens de changer coûte plus qu'il ne rapporte — reviens en arrière."
                : "L'écart est dans le bruit de mesure (" + v.ToString("+0;-0;0")
                  + " %). Le réglage ne change rien de mesurable ici : garde-le ou non, ça n'a pas "
                  + "d'importance, mais ne compte pas dessus.";

            return entete + sens + " — charges comparables (écart " + (ec * 100).ToString("0") + " %).\n\n" + detail
                 + "\n\nPilote au pire temps : avant « " + avant.PirePilote + " », après « " + apres.PirePilote + " ».";
        }

        // ------------------------------------------------------------------ fichier

        private static string Chemin { get { return AppPaths.File("bt-dpc-avant.txt"); } }

        /// <summary>Sérialisation PURE, une valeur par ligne — lisible à l'œil en cas de doute.</summary>
        public static string Serialise(Releve r)
        {
            if (r == null) return "";
            var c = CultureInfo.InvariantCulture;
            return r.Quand.ToString("o", c) + "\n"
                 + r.Secondes.ToString(c) + "\n"
                 + r.PireMs.ToString(c) + "\n"
                 + r.EvenementsParSeconde.ToString(c) + "\n"
                 + (r.PirePilote ?? "") + "\n";
        }

        /// <summary>Lecture PURE. null si le contenu est inexploitable — jamais un relevé partiel,
        /// qui produirait une comparaison fausse plutôt qu'une absence de comparaison.</summary>
        public static Releve Analyse(string contenu)
        {
            if (string.IsNullOrEmpty(contenu)) return null;
            string[] l = contenu.Replace("\r", "").Split('\n');
            if (l.Length < 5) return null;
            var c = CultureInfo.InvariantCulture;
            try
            {
                DateTime q; double s, p, e;
                if (!DateTime.TryParse(l[0], c, DateTimeStyles.RoundtripKind, out q)) return null;
                if (!double.TryParse(l[1], NumberStyles.Float, c, out s)) return null;
                if (!double.TryParse(l[2], NumberStyles.Float, c, out p)) return null;
                if (!double.TryParse(l[3], NumberStyles.Float, c, out e)) return null;
                if (s <= 0 || p <= 0 || e <= 0) return null;
                return new Releve { Quand = q, Secondes = s, PireMs = p, EvenementsParSeconde = e, PirePilote = l[4] };
            }
            catch { return null; }
        }

        /// <summary>Construit PUREMENT un relevé à partir d'un classement de pilotes.</summary>
        public static Releve Depuis(List<DpcLive.Pilote> classement, double secondes)
        {
            if (classement == null || classement.Count == 0 || secondes <= 0) return null;
            double pire = 0; string qui = "";
            long evts = 0;
            foreach (DpcLive.Pilote p in classement)
            {
                evts += p.Dpc + p.Isr;
                if (p.PireMs > pire) { pire = p.PireMs; qui = p.Nom; }
            }
            if (pire <= 0 || evts <= 0) return null;
            return new Releve
            {
                Quand = DateTime.Now,
                Secondes = secondes,
                PireMs = pire,
                EvenementsParSeconde = evts / secondes,
                PirePilote = qui
            };
        }

        public static Releve Reference()
        {
            try { return File.Exists(Chemin) ? Analyse(File.ReadAllText(Chemin)) : null; }
            catch { return null; }
        }

        public static void EnregistreReference(Releve r)
        {
            try { if (r != null) File.WriteAllText(Chemin, Serialise(r)); }
            catch { }
        }

        public static void OublieReference()
        {
            try { if (File.Exists(Chemin)) File.Delete(Chemin); }
            catch { }
        }
    }
}
