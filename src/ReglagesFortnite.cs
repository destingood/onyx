using System;
using System.Collections.Generic;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// PRÉRÉGLAGES GRAPHIQUES FORTNITE (logique PURE) : quatre modes écrits dans GameUserSettings.ini.
    /// Seules les valeurs visées changent, le reste du fichier reste identique octet pour octet ;
    /// une clé absente n'est jamais ajoutée ; « Rétablir » remet l'ancienne valeur seulement là
    /// où ONYX a encore la main (valeur = celle qu'il a écrite). Clés relevées le 26/11/2026 ;
    /// valeurs : proposition du plan, uniquement des crans que le menu Vidéo du jeu propose.
    /// Jamais touchés : résolution, FrameRateLimit, plein écran, moteur de rendu, DLSS/TSR.
    /// </summary>
    internal static partial class ReglagesFortnite
    {
        public enum Mode { Competition, Performance, Equilibre, Qualite }

        public sealed class Changement { public string Section, Cle, Avant, Apres; }

        private const string Jeu = "/Script/FortniteGame.FortGameUserSettings";
        private const string Echelle = "ScalabilityGroups";

        // Section, clé, valeurs pour Compétition, Performance, Équilibré, Qualité.
        private static readonly string[][] Cibles =
        {
            new[] { Echelle, "sg.ViewDistanceQuality",      "2", "2", "2", "3" },
            new[] { Echelle, "sg.AntiAliasingQuality",      "0", "1", "2", "3" },
            new[] { Echelle, "sg.ShadowQuality",            "0", "0", "2", "3" },
            new[] { Echelle, "sg.GlobalIlluminationQuality","0", "0", "1", "3" },
            new[] { Echelle, "sg.ReflectionQuality",        "0", "0", "1", "3" },
            new[] { Echelle, "sg.PostProcessQuality",       "0", "0", "2", "3" },
            new[] { Echelle, "sg.TextureQuality",           "1", "2", "2", "3" },
            new[] { Echelle, "sg.EffectsQuality",           "0", "1", "2", "3" },
            new[] { Echelle, "sg.FoliageQuality",           "0", "1", "2", "3" },
            new[] { Echelle, "sg.ShadingQuality",           "0", "1", "2", "3" },
            new[] { Echelle, "sg.LandscapeQuality",         "0", "1", "2", "3" },
            new[] { Jeu,     "bMotionBlur",                 "False", "False", "False", "False" },
            new[] { Jeu,     "bUseVSync",                   "False", "False", "False", "False" },
            new[] { Jeu,     "bUseNanite",                  "False", "False", "False", "True" },
        };

        public static string NomMode(Mode m)
        {
            switch (m)
            {
                case Mode.Competition: return "Compétition";
                case Mode.Performance: return "Performance";
                case Mode.Equilibre: return "Équilibré";
                default: return "Qualité";
            }
        }

        /// <summary>Ce qui change vraiment pour ce mode ; les clés introuvables vont dans <paramref name="clesAbsentes"/>.</summary>
        public static List<Changement> Prevoit(string ini, Mode m, out List<string> clesAbsentes)
        {
            var res = new List<Changement>();
            clesAbsentes = new List<string>();
            Dictionary<string, string> valeurs = Valeurs(ini ?? "");
            foreach (string[] t in Cibles)
            {
                string cible = t[2 + (int)m], avant;
                if (!valeurs.TryGetValue(Id(t[0], t[1]), out avant)) { clesAbsentes.Add(t[1]); continue; }
                if (Pareil(avant, cible)) continue;
                res.Add(new Changement { Section = t[0], Cle = t[1], Avant = avant, Apres = cible });
            }
            return res;
        }

        public static string Applique(string ini, List<Changement> changements)
        {
            return Reecrit(ini, changements, (c, actuel) => c.Apres);
        }

        /// <summary>Remet Avant seulement là où la valeur vaut encore Apres (le joueur garde ce qu'il a changé).</summary>
        public static string Retablit(string iniActuel, List<Changement> changements)
        {
            return Reecrit(iniActuel, changements, (c, actuel) => Pareil(actuel, c.Apres) ? c.Avant : null);
        }

        /// <summary>
        /// État enregistré + nouveaux changements (deuxième préréglage appliqué par-dessus le premier) :
        /// Avant reste la valeur d'ORIGINE, Apres devient la nouvelle ; une clé revenue à son origine
        /// sort de l'état. « Rétablir » ramène donc toujours au fichier d'avant le premier préréglage.
        /// </summary>
        public static List<Changement> Fusionne(List<Changement> etat, List<Changement> nouveaux)
        {
            var res = new List<Changement>();
            if (etat != null) foreach (Changement e in etat) if (e != null) res.Add(new Changement { Section = e.Section, Cle = e.Cle, Avant = e.Avant, Apres = e.Apres });
            if (nouveaux == null) return res;
            foreach (Changement n in nouveaux)
            {
                if (n == null) continue;
                Changement e = res.Find(x => Id(x.Section, x.Cle) == Id(n.Section, n.Cle));
                if (e == null) { res.Add(new Changement { Section = n.Section, Cle = n.Cle, Avant = n.Avant, Apres = n.Apres }); continue; }
                e.Apres = n.Apres;
                if (Pareil(e.Avant, e.Apres)) res.Remove(e);
            }
            return res;
        }

        private static readonly Dictionary<string, string> Noms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "sg.ViewDistanceQuality", "Distance d'affichage" }, { "sg.AntiAliasingQuality", "Anticrénelage" },
            { "sg.ShadowQuality", "Ombres" }, { "sg.GlobalIlluminationQuality", "Illumination globale" },
            { "sg.ReflectionQuality", "Réflexions" }, { "sg.PostProcessQuality", "Post-traitement" },
            { "sg.TextureQuality", "Textures" }, { "sg.EffectsQuality", "Effets" }, { "sg.FoliageQuality", "Feuillage" },
            { "sg.ShadingQuality", "Ombrage" }, { "sg.LandscapeQuality", "Terrain" },
            { "bMotionBlur", "Flou de mouvement" }, { "bUseVSync", "Synchronisation verticale" }, { "bUseNanite", "Maillages Nanite" },
        };

        private static readonly string[] Crans = { "Bas", "Moyen", "Élevé", "Épique" };

        public static string Libelle(string cle)
        {
            string n;
            return cle != null && Noms.TryGetValue(cle, out n) ? n : cle;
        }

        private static string Valeur(string v)
        {
            string t = (v ?? "").Trim();
            int i;
            if (int.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out i) && i >= 0 && i < Crans.Length) return Crans[i];
            if (string.Equals(t, "True", StringComparison.OrdinalIgnoreCase)) return "activé";
            if (string.Equals(t, "False", StringComparison.OrdinalIgnoreCase)) return "désactivé";
            return t;
        }

        /// <summary>« Ombres : Épique → Bas ».</summary>
        public static string Ligne(Changement c)
        {
            return c == null ? "" : Libelle(c.Cle) + " : " + Valeur(c.Avant) + " → " + Valeur(c.Apres);
        }

        public static List<Changement> Lire(string texte)
        {
            var res = new List<Changement>();
            if (string.IsNullOrEmpty(texte)) return res;
            foreach (string brute in texte.Split('\n'))
            {
                string[] c = brute.TrimEnd('\r').Split('\t');
                if (c.Length < 4 || c[0].Length == 0 || c[1].Length == 0) continue;
                res.Add(new Changement { Section = c[0], Cle = c[1], Avant = c[2], Apres = c[3] });
            }
            return res;
        }

        public static string Ecrire(IEnumerable<Changement> changements)
        {
            var sb = new StringBuilder();
            if (changements == null) return "";
            foreach (Changement c in changements)
                if (c != null) sb.Append(Champ(c.Section)).Append('\t').Append(Champ(c.Cle)).Append('\t')
                                 .Append(Champ(c.Avant)).Append('\t').Append(Champ(c.Apres)).Append('\n');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ lecture ligne à ligne

        private static string Champ(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

        private static string Id(string section, string cle) { return section.ToLowerInvariant() + "\n" + cle.ToLowerInvariant(); }

        /// <summary>Unreal lit les booléens sans casse ; les crans sg.* sont des entiers.</summary>
        private static bool Pareil(string a, string b) { return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase); }

        /// <summary>Découpe en lignes en gardant chaque fin de ligne telle quelle.</summary>
        private static List<KeyValuePair<string, string>> Lignes(string texte)
        {
            var res = new List<KeyValuePair<string, string>>();   // contenu, fin de ligne
            int i = 0;
            while (i < texte.Length)
            {
                int n = texte.IndexOf('\n', i);
                if (n < 0) { res.Add(new KeyValuePair<string, string>(texte.Substring(i), "")); break; }
                int fin = n > i && texte[n - 1] == '\r' ? n - 1 : n;
                res.Add(new KeyValuePair<string, string>(texte.Substring(i, fin - i), texte.Substring(fin, n + 1 - fin)));
                i = n + 1;
            }
            return res;
        }

        private static string Section(string ligne)
        {
            string t = ligne.Trim();
            return t.Length >= 2 && t[0] == '[' && t[t.Length - 1] == ']' ? t.Substring(1, t.Length - 2) : null;
        }

        /// <summary>« Cle=Valeur » → clé et valeur ; commentaires et lignes sans « = » → faux.</summary>
        private static bool Paire(string ligne, out string cle, out string valeur)
        {
            cle = valeur = null;
            int eg = ligne.IndexOf('=');
            if (eg <= 0 || ligne.TrimStart().StartsWith(";")) return false;
            cle = ligne.Substring(0, eg).Trim();
            valeur = ligne.Substring(eg + 1);
            return cle.Length > 0;
        }

        /// <summary>Première occurrence de chaque clé, par section (comme Unreal pour une clé simple).</summary>
        private static Dictionary<string, string> Valeurs(string ini)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            string sec = "";
            foreach (var l in Lignes(ini))
            {
                string s = Section(l.Key);
                if (s != null) { sec = s; continue; }
                string cle, val;
                if (Paire(l.Key, out cle, out val) && !d.ContainsKey(Id(sec, cle))) d[Id(sec, cle)] = val;
            }
            return d;
        }

        private static string Reecrit(string ini, List<Changement> changements, Func<Changement, string, string> nouvelle)
        {
            if (ini == null || changements == null || changements.Count == 0) return ini;
            var parId = new Dictionary<string, Changement>(StringComparer.Ordinal);
            foreach (Changement c in changements) if (c != null && c.Section != null && c.Cle != null) parId[Id(c.Section, c.Cle)] = c;
            var faits = new HashSet<string>(StringComparer.Ordinal);
            var sb = new StringBuilder(ini.Length + 64);
            string sec = "";
            foreach (var l in Lignes(ini))
            {
                string ligne = l.Key, s = Section(ligne), cle, val;
                Changement c;
                if (s != null) sec = s;
                else if (Paire(ligne, out cle, out val) && parId.TryGetValue(Id(sec, cle), out c) && faits.Add(Id(sec, cle)))
                {
                    string v = nouvelle(c, val);
                    if (v != null) ligne = ligne.Substring(0, ligne.IndexOf('=') + 1) + v;
                }
                sb.Append(ligne).Append(l.Value);
            }
            return sb.ToString();
        }
    }
}
