using System;
using System.Collections.Generic;
using System.Globalization;

namespace BTOptimizer
{
    /// <summary>
    /// LATENCE RÉELLE ENTRÉE → AFFICHAGE, lue dans le CSV de PresentMon (Intel, licence MIT).
    ///
    /// Aucun guide d'optimisation (Calypto, PC-Tuning, PC-Optimization-Hub, WinUtil) ne fournit de
    /// mesure contrôlée pour ses réglages : la seule façon de savoir lequel fait gagner sur TA
    /// machine est de mesurer avant et après. PresentMon le fait sans injection dans le jeu
    /// (événements ETW de Windows) :
    ///   MsAllInputToPhotonLatency : de la première entrée clavier/souris prise en compte par une
    ///     image jusqu'à son affichage ;
    ///   MsPCLatency : de l'entrée reçue par le PC à l'image envoyée à l'écran, pour les jeux
    ///     instrumentés (Reflex / PC Latency) — même grandeur que « PC Latency » de NVIDIA.
    /// Limite honnête : c'est la latence du PC. La souris (USB, firmware) et l'écran (balayage,
    /// temps de réponse des pixels) n'y sont pas — il faut un capteur pour eux.
    ///
    /// Pur : aucun accès disque ni processus, testé dans tests/LatenceReelleTests.cs.
    /// </summary>
    internal static class LatenceReelle
    {
        public sealed class Releve
        {
            public string Application = "";
            public int Images;              // images présentées par l'application
            public int Entrees;             // images portant une entrée mesurée
            public double? EntreeMediane, EntreeP95, EntreeP99;
            public double? PcLatMediane, PcLatP95;
            public double? AffichageMediane; // début de l'image → affichage
            public double? Fps;
            public string Quand = "";        // date de la mesure mémorisée
        }

        public enum Sens { Insuffisant, Gain, Bruit, Perte }

        /// <summary>En dessous, une médiane bouge trop d'une mesure à l'autre pour conclure.</summary>
        public const int EntreesMin = 100;

        /// <summary>appli : nom de l'exécutable (casse ignorée) ; null = celle qui a le plus d'images.</summary>
        public static Releve Analyse(string csv, string appli)
        {
            var r = new Releve();
            if (string.IsNullOrEmpty(csv)) return r;
            string[] lignes = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lignes.Length < 2) return r;
            string[] entete = Champs(lignes[0]);
            int cApp = Index(entete, "Application"), cEntree = Index(entete, "MsAllInputToPhotonLatency"),
                cPcl = Index(entete, "MsPCLatency"), cAff = Index(entete, "DisplayLatency"),
                cEcart = Index(entete, "MsBetweenPresents");
            if (cApp < 0) return r;

            if (string.IsNullOrEmpty(appli))
            {
                var compte = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 1; i < lignes.Length; i++)
                {
                    string[] f = Champs(lignes[i]);
                    if (cApp >= f.Length) continue;
                    int n; compte.TryGetValue(f[cApp], out n); compte[f[cApp]] = n + 1;
                }
                int max = 0;
                foreach (var kv in compte) if (kv.Value > max) { max = kv.Value; appli = kv.Key; }
                if (appli == null) return r;
            }

            var entrees = new List<double>(); var pcl = new List<double>(); var aff = new List<double>(); var ecarts = new List<double>();
            for (int i = 1; i < lignes.Length; i++)
            {
                string[] f = Champs(lignes[i]);
                if (cApp >= f.Length || !string.Equals(f[cApp], appli, StringComparison.OrdinalIgnoreCase)) continue;
                if (r.Images == 0) r.Application = f[cApp];
                r.Images++;
                Ajoute(f, cEntree, entrees); Ajoute(f, cPcl, pcl); Ajoute(f, cAff, aff); Ajoute(f, cEcart, ecarts);
            }
            r.Entrees = entrees.Count;
            r.EntreeMediane = Centile(entrees, 0.50); r.EntreeP95 = Centile(entrees, 0.95); r.EntreeP99 = Centile(entrees, 0.99);
            r.PcLatMediane = Centile(pcl, 0.50); r.PcLatP95 = Centile(pcl, 0.95);
            r.AffichageMediane = Centile(aff, 0.50);
            double? ecart = Centile(ecarts, 0.50);
            r.Fps = ecart != null && ecart.Value > 0 ? 1000.0 / ecart.Value : (double?)null;
            return r;
        }

        /// <summary>Écart de médiane d'au moins max(0,5 ms ; 5 %) pour conclure — seuil volontairement
        /// prudent : à refaire deux fois avant de garder un réglage.</summary>
        public static Sens Verdict(Releve avant, Releve apres)
        {
            if (avant == null || apres == null || avant.EntreeMediane == null || apres.EntreeMediane == null
                || avant.Entrees < EntreesMin || apres.Entrees < EntreesMin) return Sens.Insuffisant;
            double delta = apres.EntreeMediane.Value - avant.EntreeMediane.Value;
            double seuil = Math.Max(0.5, avant.EntreeMediane.Value * 0.05);
            if (delta <= -seuil) return Sens.Gain;
            if (delta >= seuil) return Sens.Perte;
            return Sens.Bruit;
        }

        public static string Texte(Releve avant, Releve apres)
        {
            Sens s = Verdict(avant, apres);
            if (s == Sens.Insuffisant)
                return "Pas assez d'entrées mesurées pour comparer (il en faut " + EntreesMin + " par mesure : bouge la souris et tire pendant la capture).";
            double a = avant.EntreeMediane.Value, b = apres.EntreeMediane.Value;
            string t = "Entrée → affichage (médiane) : " + Ms(a) + " → " + Ms(b);
            if (avant.EntreeP95 != null && apres.EntreeP95 != null) t += " ; 95e centile " + Ms(avant.EntreeP95.Value) + " → " + Ms(apres.EntreeP95.Value);
            t += ". ";
            if (s == Sens.Gain) t += "Gain réel : " + Ms(a - b) + " de moins. Refais la mesure une fois pour confirmer avant de garder le réglage.";
            else if (s == Sens.Perte) t += "Perte : " + Ms(b - a) + " de plus. Ce réglage te coûte de la latence : rétablis-le.";
            else t += "Écart dans le bruit de mesure : ce réglage ne change rien de mesurable chez toi.";
            return t;
        }

        public static string Resume(Releve r)
        {
            if (r == null || r.Images == 0) return "Aucune image capturée : le jeu doit être lancé et au premier plan pendant la mesure.";
            string t = r.Application + " : " + r.Images + " images";
            if (r.Fps != null) t += " (≈ " + Math.Round(r.Fps.Value).ToString(CultureInfo.InvariantCulture) + " FPS)";
            if (r.EntreeMediane != null)
                t += ", entrée → affichage " + Ms(r.EntreeMediane.Value) + " en médiane, " + Ms(r.EntreeP95.Value) + " au 95e centile ("
                   + r.Entrees + " entrées)";
            else t += ", aucune entrée clavier/souris captée";
            if (r.PcLatMediane != null) t += " ; PC Latency (Reflex) " + Ms(r.PcLatMediane.Value);
            return t + ".";
        }

        public static string Ms(double v) { return v.ToString("0.0", CultureInfo.GetCultureInfo("fr-FR")) + " ms"; }

        private static void Ajoute(string[] f, int c, List<double> l)
        {
            double v;
            if (c >= 0 && c < f.Length && double.TryParse(f[c], NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0 && !double.IsNaN(v))
                l.Add(v);
        }

        /// <summary>Interpolation linéaire entre rangs (méthode « R-7 », celle d'Excel CENTILE).</summary>
        private static double? Centile(List<double> l, double p)
        {
            if (l.Count == 0) return null;
            l.Sort();
            double pos = p * (l.Count - 1);
            int bas = (int)Math.Floor(pos);
            int haut = Math.Min(bas + 1, l.Count - 1);
            return l[bas] + (l[haut] - l[bas]) * (pos - bas);
        }

        private static int Index(string[] entete, string nom)
        {
            for (int i = 0; i < entete.Length; i++) if (string.Equals(entete[i], nom, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private static string[] Champs(string ligne)
        {
            string[] f = ligne.Split(',');
            for (int i = 0; i < f.Length; i++) f[i] = f[i].Trim().Trim('"');
            return f;
        }
    }
}
