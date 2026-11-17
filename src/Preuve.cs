using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// « Optimisation prouvée » — la partie PURE : juger deux mesures, trouver le réglage fautif
    /// dans un lot, et relire/écrire l'attente de preuve après redémarrage. Aucune machine à
    /// interroger ici : tout se vérifie au banc (tests/PreuveTests.cs).
    ///
    /// La règle centrale : on n'annule QUE sur régression. Beaucoup de réglages (Game DVR,
    /// optimisations plein écran…) n'agissent qu'en jeu ; sans jeu ils paraissent neutres, et les
    /// retirer pour « absence de gain » serait retirer à tort ce qui sert en partie.
    /// </summary>
    internal static class Preuve
    {
        /// <summary>Une mesure du PC au repos. -1 = indicateur inconnu (ne compte pas).</summary>
        public sealed class Mesure
        {
            public DateTime Quand;
            public double ChargeUsParS = -1;   // temps passé en DPC+ISR, µs par seconde
            public double PireUs = -1;         // pire DPC ou ISR observé, µs
            public double SleepMoyMs = -1;     // durée réelle moyenne d'un Sleep(1)
            public double SleepMaxMs = -1;     // pire Sleep(1)
            public bool Etw;                   // false : mesure dégradée, sans session ETW

            public string Resume()
            {
                var sb = new StringBuilder();
                if (ChargeUsParS >= 0) sb.Append("charge DPC+ISR " + ChargeUsParS.ToString("0") + " µs/s · ");
                if (PireUs >= 0) sb.Append("pire " + PireUs.ToString("0") + " µs · ");
                if (SleepMoyMs >= 0) sb.Append("Sleep(1) " + SleepMoyMs.ToString("0.00") + " ms");
                if (SleepMaxMs >= 0) sb.Append(" (pire " + SleepMaxMs.ToString("0.00") + " ms)");
                if (!Etw) sb.Append(" · sans ETW");
                return sb.ToString();
            }
        }

        public enum Verdict { Gain, Neutre, Regression, Incomparable }

        /// <summary>Marges de bruit. Un écart sous la marge ne prouve rien, dans un sens comme dans
        /// l'autre. Relatif ET absolu : +50 % sur 20 µs/s, c'est du bruit, pas une régression.</summary>
        public static class Seuils
        {
            public const double ChargeRel = 0.15, ChargeAbsUs = 50;
            public const double PireRel = 0.30, PireAbsUs = 150;
            public const double SleepMoyAbsMs = 0.15;
            public const double SleepMaxAbsMs = 1.0;
        }

        /// <summary>-1 pire, 0 neutre, +1 mieux, pour un indicateur où « plus bas = mieux ».</summary>
        private static int Sens(double avant, double apres, double rel, double abs)
        {
            if (!(avant >= 0) || !(apres >= 0)) return 0;   // inconnu (-1) ou NaN : ne compte pas
            double d = apres - avant;
            if (Math.Abs(d) < abs) return 0;
            if (rel > 0 && Math.Abs(d) < rel * Math.Max(avant, 1e-9)) return 0;
            return d > 0 ? -1 : 1;
        }

        public static Verdict Juge(Mesure avant, Mesure apres)
        {
            if (avant == null || apres == null) return Verdict.Incomparable;
            // Une mesure avec ETW contre une sans : les indicateurs ne sont pas les mêmes.
            if (avant.Etw != apres.Etw) return Verdict.Incomparable;
            int[] s =
            {
                Sens(avant.ChargeUsParS, apres.ChargeUsParS, Seuils.ChargeRel, Seuils.ChargeAbsUs),
                Sens(avant.PireUs, apres.PireUs, Seuils.PireRel, Seuils.PireAbsUs),
                Sens(avant.SleepMoyMs, apres.SleepMoyMs, 0, Seuils.SleepMoyAbsMs),
                Sens(avant.SleepMaxMs, apres.SleepMaxMs, 0, Seuils.SleepMaxAbsMs),
            };
            bool pire = false, mieux = false;
            foreach (int v in s) { if (v < 0) pire = true; if (v > 0) mieux = true; }
            if (pire) return Verdict.Regression;
            return mieux ? Verdict.Gain : Verdict.Neutre;
        }

        public static string Libelle(Verdict v)
        {
            switch (v)
            {
                case Verdict.Gain: return "gain mesuré";
                case Verdict.Neutre: return "neutre (effet hors mesure ou sous le bruit)";
                case Verdict.Regression: return "régression";
                default: return "non comparable";
            }
        }

        /// <summary>
        /// Recherche par moitiés du ou des fautifs d'un lot DÉJÀ jugé mauvais et DÉJÀ annulé.
        /// <paramref name="essai"/> applique un sous-lot, le mesure, et rend true s'il le garde ;
        /// s'il rend false, c'est qu'il l'a lui-même annulé. Si aucune moitié ne régresse seule,
        /// la régression du lot entier était du bruit ou une interaction : rien n'est retiré.
        /// </summary>
        public static List<T> TrouveFautifs<T>(List<T> lotMauvais, Func<List<T>, bool> essai)
        {
            var fautifs = new List<T>();
            if (lotMauvais == null || lotMauvais.Count == 0) return fautifs;
            if (lotMauvais.Count == 1) { fautifs.Add(lotMauvais[0]); return fautifs; }
            int m = lotMauvais.Count / 2;
            List<T> a = lotMauvais.GetRange(0, m), b = lotMauvais.GetRange(m, lotMauvais.Count - m);
            if (!essai(a)) fautifs.AddRange(TrouveFautifs(a, essai));
            if (!essai(b)) fautifs.AddRange(TrouveFautifs(b, essai));
            return fautifs;
        }

        // ------------------------------------------------------------------
        //  Attente de preuve : réglages à redémarrage, jugés au lancement suivant
        // ------------------------------------------------------------------

        public sealed class Attente
        {
            public DateTime Applique;
            public Mesure Reference;
            public List<string> Ids = new List<string>();
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Ecrit(Attente a)
        {
            var sb = new StringBuilder();
            sb.Append("applique=").Append(a.Applique.ToString("o", Inv)).Append('\n');
            Mesure r = a.Reference ?? new Mesure();
            sb.Append("quand=").Append(r.Quand.ToString("o", Inv)).Append('\n');
            sb.Append("charge=").Append(r.ChargeUsParS.ToString("R", Inv)).Append('\n');
            sb.Append("pire=").Append(r.PireUs.ToString("R", Inv)).Append('\n');
            sb.Append("sleepmoy=").Append(r.SleepMoyMs.ToString("R", Inv)).Append('\n');
            sb.Append("sleepmax=").Append(r.SleepMaxMs.ToString("R", Inv)).Append('\n');
            sb.Append("etw=").Append(r.Etw ? "1" : "0").Append('\n');
            foreach (string id in a.Ids) sb.Append("id=").Append(id).Append('\n');
            return sb.ToString();
        }

        /// <summary>Rend null si le texte est vide, abîmé, ou sans réglage ni date.</summary>
        public static Attente Lit(string texte)
        {
            if (string.IsNullOrWhiteSpace(texte)) return null;
            var a = new Attente { Reference = new Mesure() };
            bool date = false;
            foreach (string brut in texte.Split('\n'))
            {
                string l = brut.Trim();
                int i = l.IndexOf('=');
                if (i <= 0) continue;
                string k = l.Substring(0, i), v = l.Substring(i + 1);
                double d; DateTime t;
                switch (k)
                {
                    case "applique":
                        if (DateTime.TryParse(v, Inv, DateTimeStyles.RoundtripKind, out t)) { a.Applique = t; date = true; }
                        break;
                    case "quand":
                        if (DateTime.TryParse(v, Inv, DateTimeStyles.RoundtripKind, out t)) a.Reference.Quand = t;
                        break;
                    case "charge": if (double.TryParse(v, NumberStyles.Float, Inv, out d)) a.Reference.ChargeUsParS = d; break;
                    case "pire": if (double.TryParse(v, NumberStyles.Float, Inv, out d)) a.Reference.PireUs = d; break;
                    case "sleepmoy": if (double.TryParse(v, NumberStyles.Float, Inv, out d)) a.Reference.SleepMoyMs = d; break;
                    case "sleepmax": if (double.TryParse(v, NumberStyles.Float, Inv, out d)) a.Reference.SleepMaxMs = d; break;
                    case "etw": a.Reference.Etw = v == "1"; break;
                    case "id": if (v.Length > 0) a.Ids.Add(v); break;
                }
            }
            return date && a.Ids.Count > 0 ? a : null;
        }

        /// <summary>L'attente se juge seulement si Windows a redémarré APRÈS l'application.</summary>
        public static bool AttenteMesurable(Attente a, DateTime dernierDemarrage)
        {
            return a != null && dernierDemarrage > a.Applique;
        }
    }
}
