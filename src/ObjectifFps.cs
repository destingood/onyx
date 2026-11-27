using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// FPS VISÉS (logique PURE). Cortex « prédit » le réglage qui atteint un nombre de FPS grâce à
    /// sa base de données ; ONYX n'en a pas : il MESURE 60 s en jeu (PreuveJeu), compare les FPS
    /// moyens à l'objectif et conseille un cran : plus léger si l'objectif est manqué, plus beau
    /// s'il reste au moins 25 % de marge. Les modes vont de 0 (Compétition, le plus léger) à 3
    /// (Qualité). Une limite d'images ou la synchro verticale du jeu sous l'objectif est signalée
    /// d'abord : aucun préréglage ne la franchit (ONYX ne touche pas la limite du jeu).
    /// </summary>
    internal static class ObjectifFps
    {
        public const int Min = 30, Max = 500, Defaut = 144;
        public const string Fichier = "bt-objectif-fps.txt";

        /// <summary>Marge au-dessus de l'objectif pour conseiller un cran plus beau. [Hypothèse : un cran coûte 15 à 25 % de FPS ; à calibrer.]</summary>
        public const double MargeMontee = 1.25;

        public enum Conseil { Atteint, Monter, Baisser, HorsDePortee, Limite }

        public sealed class Avis
        {
            public Conseil Conseil;
            public int? Mode;          // mode conseillé (Monter, Baisser), sinon null
            public string Texte;
        }

        public static int Borne(int objectif) { return Math.Max(Min, Math.Min(Max, objectif)); }

        /// <summary>Mode dont le fichier a déjà toutes les valeurs (aucun changement prévu) ; null si aucun (réglages perso).</summary>
        public static int? ModeActuel(IList<int> changementsParMode)
        {
            if (changementsParMode == null) return null;
            for (int m = 0; m < changementsParMode.Count; m++) if (changementsParMode[m] == 0) return m;
            return null;
        }

        /// <summary>Avant toute mesure : la limite d'images du jeu (0 = aucune) ou sa synchro verticale empêche-t-elle l'objectif ?</summary>
        public static string Obstacle(string jeu, int objectif, int limiteJeu, bool vsync)
        {
            if (limiteJeu > 0 && limiteJeu < objectif)
                return jeu + " est limité à " + N(limiteJeu) + " FPS dans ses propres réglages : l'objectif de " + N(objectif)
                     + " est au-dessus. Monte la limite d'images dans le jeu (ONYX n'y touche pas).";
            if (vsync)
                return "La synchro verticale de " + jeu + " est active : les FPS ne dépasseront pas la fréquence de ton écran.";
            return null;
        }

        /// <param name="modeActuel">Mode que le fichier suit pendant la mesure (null : réglages perso).</param>
        /// <param name="nomMode">Nom affiché d'un mode (0 à 3).</param>
        public static Avis Juge(string jeu, double fpsMoy, double low1, int objectif, int? modeActuel, int limiteJeu, bool vsync, Func<int, string> nomMode)
        {
            objectif = Borne(objectif);
            string mesure = N(fpsMoy) + " FPS moyens pour " + N(objectif) + " visés (1 % low : " + N(low1) + ")";
            string obstacle = Obstacle(jeu, objectif, limiteJeu, vsync);
            if (limiteJeu > 0 && limiteJeu < objectif)
                return new Avis { Conseil = Conseil.Limite, Texte = obstacle };

            if (fpsMoy >= objectif)
            {
                bool plafonne = vsync || (limiteJeu > 0 && fpsMoy >= limiteJeu * 0.95);
                if (!plafonne && modeActuel.HasValue && modeActuel.Value < 3 && fpsMoy >= objectif * MargeMontee)
                {
                    int m = modeActuel.Value + 1;
                    return new Avis
                    {
                        Conseil = Conseil.Monter, Mode = m,
                        Texte = "Objectif atteint avec " + N((fpsMoy / objectif - 1) * 100) + " % de marge : " + mesure
                              + ". Tu peux essayer « " + nomMode(m) + " », un cran plus beau, puis remesurer."
                    };
                }
                return new Avis { Conseil = Conseil.Atteint, Texte = "Objectif atteint : " + mesure + "." + (plafonne ? " Les FPS butent sur la limite du jeu ou de l'écran : la marge réelle est inconnue." : "") };
            }

            string suite = obstacle != null ? " " + obstacle : "";
            if (modeActuel.HasValue && modeActuel.Value == 0)
                return new Avis
                {
                    Conseil = Conseil.HorsDePortee,
                    Texte = "Objectif manqué même en « " + nomMode(0) + " » : " + mesure
                          + ". Le reste se joue sur la résolution ou DLSS/FSR, qu'ONYX ne touche pas." + suite
                };
            int cible = modeActuel.HasValue ? modeActuel.Value - 1 : 1;
            return new Avis
            {
                Conseil = Conseil.Baisser, Mode = cible,
                Texte = "Objectif manqué : " + mesure + "."
                      + (modeActuel.HasValue ? " Essaie « " + nomMode(cible) + " », un cran plus léger, puis remesure."
                                             : " Tes réglages ne suivent aucun mode d'ONYX : essaie « " + nomMode(cible) + " », puis remesure.")
                      + suite
            };
        }

        // ------------------------------------------------------------------ objectif par jeu

        public static Dictionary<string, int> Lire(string texte)
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(texte)) return d;
            foreach (string brute in texte.Split('\n'))
            {
                string[] c = brute.TrimEnd('\r').Split('\t');
                int v;
                if (c.Length >= 2 && c[0].Length > 0 && int.TryParse(c[1], NumberStyles.None, CultureInfo.InvariantCulture, out v)) d[c[0]] = Borne(v);
            }
            return d;
        }

        public static string Ecrire(Dictionary<string, int> d)
        {
            var sb = new StringBuilder();
            if (d == null) return "";
            var cles = new List<string>(d.Keys);
            cles.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string k in cles) sb.Append(k.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ')).Append('\t').Append(Borne(d[k]).ToString(CultureInfo.InvariantCulture)).Append('\n');
            return sb.ToString();
        }

        private static string N(double v) { return Math.Round(v).ToString("0", CultureInfo.InvariantCulture); }
    }
}
