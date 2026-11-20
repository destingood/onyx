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
            /// <summary>Régressions déjà vues après ce redémarrage (vérifications précédentes).</summary>
            public int Regressions;
        }

        /// <summary>
        /// Délai minimal depuis le démarrage de Windows avant de juger les réglages à redémarrage.
        /// Les 3 minutes d'avant ne suffisaient pas : mises à jour, antivirus et lanceurs (Epic,
        /// EA, Steam) chargent encore, et la mesure sortait « en régression ».
        /// </summary>
        public const int CalmeApresDemarrageS = 600;

        public enum Decision { Garder, Remesurer, Annuler }

        /// <summary>
        /// La référence date d'AVANT le redémarrage, la mesure d'APRÈS : deux états du système
        /// différents. Une régression isolée ne suffit donc pas à annuler ; il en faut une
        /// seconde, à une vérification suivante.
        /// </summary>
        public static Decision ApresRedemarrage(Verdict v, int regressionsPrecedentes)
        {
            if (v != Verdict.Regression) return Decision.Garder;
            return regressionsPrecedentes >= 1 ? Decision.Annuler : Decision.Remesurer;
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
            if (a.Regressions > 0) sb.Append("regressions=").Append(a.Regressions.ToString(Inv)).Append('\n');
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
                    case "regressions": { int n; if (int.TryParse(v, NumberStyles.Integer, Inv, out n) && n > 0) a.Regressions = n; } break;
                }
            }
            return date && a.Ids.Count > 0 ? a : null;
        }

        // ------------------------------------------------------------------
        //  Validation en jeu (phase C) : même partie, avant puis après la preuve
        // ------------------------------------------------------------------

        /// <summary>Une mesure en jeu : FPS moyen, 1 % low, régularité et frametime P99.</summary>
        public sealed class MesureJeu
        {
            public DateTime Quand;
            public string Jeu = "";
            public double FpsMoy = -1, Low1 = -1, P99Ms = -1, DureeS;
            public int Images;
            public int Plafond = -1;   // plafond FPS posé par ONYX pendant la mesure ; -1 = inconnu
            /// <summary>Mode de présentation relevé pendant la mesure. NON persisté (EcritJeu/LitJeu l'ignorent).</summary>
            public ModePresentation.ModeImage Presentation = ModePresentation.ModeImage.Inconnu;
            public double Regularite { get { return FpsMoy > 0 && Low1 > 0 ? Low1 / FpsMoy : -1; } }

            public string Resume()
            {
                return Jeu + " : " + (Valide(FpsMoy) ? FpsMoy.ToString("0") : "—")
                     + " FPS moyens, 1 % low " + (Valide(Low1) ? Low1.ToString("0") : "—")
                     + " FPS, régularité " + (Valide(Regularite) ? (Regularite * 100).ToString("0") + " %" : "—")
                     + ", P99 " + (Valide(P99Ms) ? P99Ms.ToString("0.00") + " ms" : "—")
                     + " (" + Images + " images, " + DureeS.ToString("0") + " s)";
            }

            private static bool Valide(double valeur) { return valeur >= 0 && !double.IsNaN(valeur) && !double.IsInfinity(valeur); }
        }

        /// <summary>Seuils du jeu : une partie ne se rejoue jamais à l'identique, la marge est
        /// donc plus large qu'au repos. [À calibrer sur machines réelles.]</summary>
        public static class SeuilsJeu
        {
            public const double Low1Rel = 0.10;     // 1 % low : −10 %
            public const double MoyRel = 0.07;      // FPS moyen : −7 %
            public const int ImagesMin = 1000;      // en dessous, le 1 % low repose sur < 10 images
            public const double DureeMinS = 30;
        }

        /// <summary>FPS moyen = images / temps total ; 1 % low = 1000 / moyenne du pire 1 % ; P99 = nearest-rank (rang supérieur).</summary>
        public static MesureJeu CalculeJeu(string jeu, IList<double> frametimesMs)
        {
            var m = new MesureJeu { Quand = DateTime.Now, Jeu = jeu ?? "" };
            if (frametimesMs == null || frametimesMs.Count == 0) return m;
            var tri = new List<double>();
            double total = 0;
            foreach (double f in frametimesMs) if (f > 0 && !double.IsNaN(f) && !double.IsInfinity(f)) { tri.Add(f); total += f; }
            if (tri.Count == 0 || total <= 0) return m;
            m.Images = tri.Count;
            m.DureeS = total / 1000.0;
            m.FpsMoy = tri.Count * 1000.0 / total;
            tri.Sort();
            int k = Math.Max(1, tri.Count / 100);
            double pire = 0;
            for (int i = tri.Count - k; i < tri.Count; i++) pire += tri[i];
            m.Low1 = 1000.0 / (pire / k);
            int p99Rank = Math.Max(1, (int)Math.Ceiling(tri.Count * 0.99));
            m.P99Ms = tri[p99Rank - 1];
            return m;
        }

        public static bool JeuValide(MesureJeu m)
        {
            return m != null && m.Images >= SeuilsJeu.ImagesMin && m.DureeS >= SeuilsJeu.DureeMinS && m.FpsMoy > 0 && m.Low1 > 0;
        }

        /// <summary>Deux mesures du même jeu et plafond, dont les FPS diffèrent de moins que les seuils de jugement.</summary>
        public static bool Repetable(MesureJeu a, MesureJeu b)
        {
            if (!JeuValide(a) || !JeuValide(b)
                || !string.Equals(a.Jeu, b.Jeu, StringComparison.OrdinalIgnoreCase)
                || PlafondChange(a, b)) return false;
            return Math.Abs((b.Low1 - a.Low1) / a.Low1) < SeuilsJeu.Low1Rel
                && Math.Abs((b.FpsMoy - a.FpsMoy) / a.FpsMoy) < SeuilsJeu.MoyRel;
        }

        /// <summary>Plus haut = mieux. Régression si le 1 % low OU le FPS moyen baisse au-delà de sa marge.</summary>
        public static Verdict JugeJeu(MesureJeu avant, MesureJeu apres)
        {
            if (!JeuValide(avant) || !JeuValide(apres)) return Verdict.Incomparable;
            if (!string.Equals(avant.Jeu, apres.Jeu, StringComparison.OrdinalIgnoreCase)) return Verdict.Incomparable;
            // Un plafond baisse les FPS par construction : changé entre les deux, rien n'est comparable.
            if (PlafondChange(avant, apres)) return Verdict.Incomparable;
            double dLow = (apres.Low1 - avant.Low1) / avant.Low1;
            double dMoy = (apres.FpsMoy - avant.FpsMoy) / avant.FpsMoy;
            if (dLow <= -SeuilsJeu.Low1Rel || dMoy <= -SeuilsJeu.MoyRel) return Verdict.Regression;
            if (dLow >= SeuilsJeu.Low1Rel || dMoy >= SeuilsJeu.MoyRel) return Verdict.Gain;
            return Verdict.Neutre;
        }

        /// <summary>Rapport avant/après des FPS moyens, du 1 % low, de sa régularité et du P99.</summary>
        public static string RapportJeu(MesureJeu avant, MesureJeu apres)
        {
            if (avant == null || apres == null) return "Rapport en jeu indisponible.";
            var sb = new StringBuilder("Rapport avant/après :");
            AjouteEcart(sb, "FPS moyens", avant.FpsMoy, apres.FpsMoy, " FPS");
            AjouteEcart(sb, "1 % low", avant.Low1, apres.Low1, " FPS");
            AjouteEcart(sb, "Régularité low/moyenne", avant.Regularite * 100, apres.Regularite * 100, " %");
            AjouteEcart(sb, "Frametime P99", avant.P99Ms, apres.P99Ms, " ms");
            return sb.ToString();
        }

        private static void AjouteEcart(StringBuilder sb, string nom, double avant, double apres, string unite)
        {
            bool avantValide = avant >= 0 && !double.IsNaN(avant) && !double.IsInfinity(avant);
            bool apresValide = apres >= 0 && !double.IsNaN(apres) && !double.IsInfinity(apres);
            sb.Append("\r\n").Append(nom).Append(" : ")
                .Append(avantValide ? avant.ToString("0.00", Inv) : "—").Append(unite)
                .Append(" → ").Append(apresValide ? apres.ToString("0.00", Inv) : "—").Append(unite);
            if (avantValide && apresValide && avant > 0)
                sb.Append(" (").Append(((apres - avant) / avant * 100).ToString("+0.0;-0.0;0.0", Inv)).Append(" %)");
        }

        /// <summary>Vrai si les deux plafonds sont connus et différents.</summary>
        public static bool PlafondChange(MesureJeu avant, MesureJeu apres)
        {
            return avant != null && apres != null && avant.Plafond >= 0 && apres.Plafond >= 0 && avant.Plafond != apres.Plafond;
        }

        public static string EcritJeu(MesureJeu m)
        {
            var sb = new StringBuilder();
            sb.Append("quand=").Append(m.Quand.ToString("o", Inv)).Append('\n');
            sb.Append("jeu=").Append((m.Jeu ?? "").Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
            sb.Append("fpsmoy=").Append(m.FpsMoy.ToString("R", Inv)).Append('\n');
            sb.Append("low1=").Append(m.Low1.ToString("R", Inv)).Append('\n');
            sb.Append("p99=").Append(m.P99Ms.ToString("R", Inv)).Append('\n');
            sb.Append("duree=").Append(m.DureeS.ToString("R", Inv)).Append('\n');
            sb.Append("images=").Append(m.Images.ToString(Inv)).Append('\n');
            sb.Append("plafond=").Append(m.Plafond.ToString(Inv)).Append('\n');
            return sb.ToString();
        }

        /// <summary>Rend null si le texte est vide, abîmé ou sans date ni jeu.</summary>
        public static MesureJeu LitJeu(string texte)
        {
            if (string.IsNullOrWhiteSpace(texte)) return null;
            var m = new MesureJeu();
            bool date = false;
            foreach (string brut in texte.Split('\n'))
            {
                string l = brut.Trim();
                int i = l.IndexOf('=');
                if (i <= 0) continue;
                string k = l.Substring(0, i), v = l.Substring(i + 1);
                double d; DateTime t; int n;
                switch (k)
                {
                    case "quand": if (DateTime.TryParse(v, Inv, DateTimeStyles.RoundtripKind, out t)) { m.Quand = t; date = true; } break;
                    case "jeu": m.Jeu = v; break;
                    case "fpsmoy": if (double.TryParse(v, NumberStyles.Float, Inv, out d)) m.FpsMoy = d; break;
                    case "low1": if (double.TryParse(v, NumberStyles.Float, Inv, out d)) m.Low1 = d; break;
                    case "p99": if (double.TryParse(v, NumberStyles.Float, Inv, out d)) m.P99Ms = d; break;
                    case "duree": if (double.TryParse(v, NumberStyles.Float, Inv, out d)) m.DureeS = d; break;
                    case "images": if (int.TryParse(v, NumberStyles.Integer, Inv, out n)) m.Images = n; break;
                    case "plafond": if (int.TryParse(v, NumberStyles.Integer, Inv, out n)) m.Plafond = n; break;
                }
            }
            return date && m.Jeu.Length > 0 ? m : null;
        }

        /// <summary>Quelle mesure en jeu faire maintenant : « après » seulement si une preuve a eu lieu
        /// APRÈS la mesure « avant » ; sinon (re)faire la mesure « avant ».</summary>
        public static bool MesureApres(MesureJeu avant, Attente dernierePreuve)
        {
            return avant != null && dernierePreuve != null && dernierePreuve.Applique > avant.Quand;
        }

        /// <summary>L'attente se juge seulement si Windows a redémarré APRÈS l'application.</summary>
        public static bool AttenteMesurable(Attente a, DateTime dernierDemarrage)
        {
            return a != null && dernierDemarrage > a.Applique;
        }
    }
}
