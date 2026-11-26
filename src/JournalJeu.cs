using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// JOURNAL DE JEU — combien de temps on joue à quoi, et comment le jeu a tourné.
    ///
    /// Partie PURE (testée au banc) : découpage en parties à partir des relevés de la veille, cumuls,
    /// format du fichier, textes. Le branchement sur la veille et le disque vivent dans
    /// JournalJeuMachine.cs.
    ///
    /// Une partie va du premier au DERNIER relevé où le jeu a été vu : un PC mis en veille au milieu
    /// d'une partie ne compte pas la nuit. Une absence de 30 s au plus (plantage puis relance) ne
    /// coupe pas la partie ; moins d'une minute de jeu n'en fait pas une (launcher qui ouvre
    /// l'exécutable trois secondes).
    /// </summary>
    internal static partial class JournalJeu
    {
        public const int GraceSecondes = 30;
        public const int MinSecondes = 60;

        /// <summary>Une partie close. FpsMoyen et FpsBas à 0 = non mesurés.</summary>
        public sealed class Partie { public string Jeu; public DateTime Debut; public int Secondes; public double FpsMoyen; public double FpsBas; }

        /// <summary>La partie en cours. Jeu null = aucune.</summary>
        public sealed class Etat { public string Jeu; public DateTime Debut; public DateTime DernierVu; }

        /// <summary>Clé d'index d'un exécutable : nom sans « .exe », en minuscules.</summary>
        public static string Cle(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return "";
            string n = exe.Trim();
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            return n.ToLowerInvariant();
        }

        /// <summary>Nom du premier jeu reconnu parmi les processus, ou null. Les clés de l'index
        /// sont celles de <see cref="Cle"/>.</summary>
        public static string JeuParmi(IEnumerable<string> processus, Dictionary<string, string> exeVersJeu)
        {
            if (processus == null || exeVersJeu == null || exeVersJeu.Count == 0) return null;
            foreach (string p in processus)
            {
                string jeu;
                if (exeVersJeu.TryGetValue(Cle(p), out jeu)) return jeu;
            }
            return null;
        }

        /// <summary>Un relevé : <paramref name="jeu"/> = jeu vu à cet instant, ou null. Rend la
        /// partie CLOSE à ce relevé (elle a duré au moins une minute), sinon null.</summary>
        public static Partie Avance(Etat e, string jeu, DateTime maintenant)
        {
            if (e == null) return null;
            bool enCours = e.Jeu != null;
            bool trou = enCours && (maintenant - e.DernierVu).TotalSeconds > GraceSecondes;

            if (jeu == null)
            {
                if (!enCours || !trou) return null;
                return Clot(e);
            }

            if (enCours && !trou && string.Equals(e.Jeu, jeu, StringComparison.OrdinalIgnoreCase))
            {
                e.DernierVu = maintenant;
                return null;
            }

            // Autre jeu, ou même jeu revu après un trou (réveil) : la partie d'avant est close,
            // une nouvelle commence maintenant.
            Partie close = enCours ? Clot(e) : null;
            e.Jeu = jeu;
            e.Debut = maintenant;
            e.DernierVu = maintenant;
            return close;
        }

        private static Partie Clot(Etat e)
        {
            int secondes = (int)Math.Round((e.DernierVu - e.Debut).TotalSeconds);
            Partie p = secondes >= MinSecondes ? new Partie { Jeu = e.Jeu, Debut = e.Debut, Secondes = secondes } : null;
            e.Jeu = null;
            e.Debut = default(DateTime);
            e.DernierVu = default(DateTime);
            return p;
        }

        // ------------------------------------------------------------------ fichier

        private const string FormatDate = "yyyy-MM-dd'T'HH:mm:ss";

        /// <summary>Une partie par ligne : « Jeu \t date \t secondes \t fpsMoyen \t fpsBas ».
        /// Une ligne abîmée est ignorée.</summary>
        public static List<Partie> Lire(string texte)
        {
            var parties = new List<Partie>();
            if (string.IsNullOrEmpty(texte)) return parties;
            using (var r = new StringReader(texte))
            {
                string ligne;
                while ((ligne = r.ReadLine()) != null)
                {
                    string[] c = ligne.Split('\t');
                    if (c.Length < 5 || c[0].Length == 0) continue;
                    DateTime debut; int secondes; double moyen, bas;
                    if (!DateTime.TryParseExact(c[1], FormatDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out debut)) continue;
                    if (!int.TryParse(c[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out secondes) || secondes < 0) continue;
                    if (!double.TryParse(c[3], NumberStyles.Float, CultureInfo.InvariantCulture, out moyen)) continue;
                    if (!double.TryParse(c[4], NumberStyles.Float, CultureInfo.InvariantCulture, out bas)) continue;
                    parties.Add(new Partie { Jeu = c[0], Debut = debut, Secondes = secondes, FpsMoyen = moyen, FpsBas = bas });
                }
            }
            return parties;
        }

        public static string Ecrire(IEnumerable<Partie> parties)
        {
            var sb = new StringBuilder();
            if (parties == null) return "";
            foreach (Partie p in parties)
            {
                if (p == null) continue;
                string jeu = (p.Jeu ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (jeu.Length == 0) continue;
                sb.Append(jeu).Append('\t')
                  .Append(p.Debut.ToString(FormatDate, CultureInfo.InvariantCulture)).Append('\t')
                  .Append(p.Secondes.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(p.FpsMoyen.ToString("0.#", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(p.FpsBas.ToString("0.#", CultureInfo.InvariantCulture)).Append('\n');
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ cumuls et textes

        /// <summary>Secondes jouées depuis <paramref name="depuis"/> (début de partie). Jeu null = tous.</summary>
        public static int Secondes(List<Partie> parties, string jeu, DateTime depuis)
        {
            if (parties == null) return 0;
            long total = 0;
            foreach (Partie p in parties)
                if (p.Debut >= depuis && (jeu == null || string.Equals(p.Jeu, jeu, StringComparison.OrdinalIgnoreCase)))
                    total += p.Secondes;
            return (int)Math.Min(int.MaxValue, total);
        }

        /// <summary>Fin de la partie la plus récente du jeu, ou null s'il n'a jamais été joué.</summary>
        public static DateTime? DernierePartie(List<Partie> parties, string jeu)
        {
            if (parties == null || jeu == null) return null;
            DateTime? fin = null;
            foreach (Partie p in parties)
            {
                if (!string.Equals(p.Jeu, jeu, StringComparison.OrdinalIgnoreCase)) continue;
                DateTime f = p.Debut.AddSeconds(p.Secondes);
                if (fin == null || f > fin.Value) fin = f;
            }
            return fin;
        }

        /// <summary>45 → « moins d'une minute », 2700 → « 45 min », 7500 → « 2 h 05 ».</summary>
        public static string Duree(int secondes)
        {
            if (secondes < 60) return "moins d'une minute";
            int minutes = secondes / 60;
            if (minutes < 60) return minutes + " min";
            return (minutes / 60) + " h " + (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Résumé des FPS d'une partie. Moyenne = images / secondes ; au plus bas = 1er
        /// centile des relevés par seconde (0 sous 100 relevés : trop peu pour dire « 1 % »).</summary>
        public static void Resume(List<double> fpsParSeconde, long images, double secondes, out double moyen, out double bas)
        {
            moyen = secondes > 0 && images > 0 ? images / secondes : 0;
            bas = 0;
            if (fpsParSeconde == null || fpsParSeconde.Count < 100) return;
            var tries = new List<double>(fpsParSeconde);
            tries.Sort();
            int rang = Math.Max(0, (int)Math.Ceiling(tries.Count * 0.01) - 1);
            bas = tries[rang];
        }

        /// <summary>« 1 h 12 · 238 FPS en moyenne · 141 au plus bas » ; sans mesure, la durée seule.</summary>
        public static string Bilan(Partie p)
        {
            if (p == null) return "";
            string s = Duree(p.Secondes);
            if (p.FpsMoyen > 0) s += " · " + ((int)Math.Floor(p.FpsMoyen)) + " FPS en moyenne";
            if (p.FpsMoyen > 0 && p.FpsBas > 0) s += " · " + ((int)Math.Floor(p.FpsBas)) + " au plus bas";
            return s;
        }
    }
}
