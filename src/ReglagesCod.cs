using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// PRÉRÉGLAGES GRAPHIQUES CALL OF DUTY: BLACK OPS 6 (logique PURE) : quatre modes écrits dans
    /// s.1.0.cod24.txt0 et .txt1 (Documents\Call of Duty BO6\players, relevé le 27/11/2026). Une
    /// ligne par réglage : « Nom@a;b;c = valeur // one of X, Y, Z » ; le commentaire donne les crans
    /// que le menu propose, et ONYX n'écrit QUE l'un d'eux (ou true/false pour une case à cocher).
    /// Seule la valeur des lignes visées change, le reste du fichier reste identique octet pour
    /// octet ; un réglage absent, en double ou dont la valeur sort de la liste n'est pas touché.
    /// Valeurs : proposition d'ONYX. Affichage (au choix, à part) : résolution au format « 2560x1440 » et
    /// mode d'affichage parmi la liste du fichier. Jamais touchés : préréglage de qualité, textures,
    /// DLSS/FSR/XeSS, génération d'images, synchro verticale, limite d'images, Reflex.
    /// </summary>
    internal static partial class ReglagesCod
    {
        public enum Mode { Competition, Performance, Equilibre, Qualite }

        public sealed class Changement { public string Cle, Avant, Apres; }

        // Réglage, valeurs pour Compétition, Performance, Équilibré, Qualité.
        private static readonly string[][] Cibles =
        {
            new[] { "ShadowQuality",            "Very_Low",       "Low",            "Medium",         "High" },
            new[] { "ScreenSpaceShadowQuality", "Off",            "Off",            "Low",            "High" },
            new[] { "AmbientLightingQuality",   "Low",            "Low",            "Medium",         "High" },
            new[] { "SSRQuality",               "Off",            "Off",            "Low",            "High" },
            new[] { "ParticleQuality",          "low",            "low",            "medium",         "high" },
            new[] { "ModelQuality",             "Medium Quality", "Medium Quality", "High Quality",   "High Quality" },
            new[] { "DeferredPhysics",          "Low Quality",    "Low Quality",    "Medium Quality", "High Quality" },
            new[] { "WaterCausticsMode",        "Off",            "Off",            "Low Quality",    "High Quality" },
            new[] { "Tessellation",             "0_Off",          "1_Near",         "1_Near",         "2_All" },
            new[] { "TextureFilter",            "aniso 8x",       "aniso 8x",       "aniso 16x",      "aniso 16x" },
            new[] { "WorldStreamingQuality",    "Low",            "Low",            "High",           "High" },
            new[] { "DepthOfField",             "false",          "false",          "true",           "true" },
            new[] { "VRS",                      "true",           "true",           "true",           "false" },
        };

        /// <summary>« Nom@1;2;3 = valeur » suivi ou non de « // commentaire ».</summary>
        private static readonly Regex LigneReglage = new Regex(@"^(?<cle>[A-Za-z0-9_]+)@[0-9;]+ = (?<val>.*?)(?: // (?<com>.*))?$", RegexOptions.CultureInvariant);

        /// <summary>Valeurs réécrites ou gardées dans l'état : rien qui casserait une ligne.</summary>
        private static readonly Regex FormatResolution = new Regex(@"^[0-9]{3,5}x[0-9]{3,5}$", RegexOptions.CultureInvariant);
        private static readonly Regex Simple = new Regex(@"^[A-Za-z0-9 _+.:%\-]{1,64}$", RegexOptions.CultureInvariant);

        public const int SteamId = 4384550;

        /// <summary>Le jeu de la bibliothèque est-il Black Ops 6 (ou le lanceur Call of Duty qui le contient) ?</summary>
        public static bool EstLeJeu(int steamId, string nom)
        {
            if (steamId == SteamId) return true;
            string n = (nom ?? "").Replace("®", "").Replace("™", "");
            return n.IndexOf("Black Ops 6", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("BO6", StringComparison.Ordinal) >= 0;
        }

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

        private sealed class Reglage
        {
            public string Cle, Valeur;
            public int Debut, Longueur, Fois;
            public List<string> Liste;    // crans du menu (« one of »), null sinon
            public bool Commentaire;
        }

        private static Dictionary<string, Reglage> Reglages(string texte)
        {
            var d = new Dictionary<string, Reglage>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(texte)) return d;
            int pos = 0;
            while (pos < texte.Length)
            {
                int fin = texte.IndexOf('\n', pos);
                if (fin < 0) fin = texte.Length;
                int finContenu = fin > pos && texte[fin - 1] == '\r' ? fin - 1 : fin;
                Match m = LigneReglage.Match(texte.Substring(pos, finContenu - pos));
                if (m.Success)
                {
                    string cle = m.Groups["cle"].Value;
                    Reglage r;
                    if (d.TryGetValue(cle, out r)) r.Fois++;
                    else
                    {
                        r = new Reglage { Cle = cle, Valeur = m.Groups["val"].Value, Debut = pos + m.Groups["val"].Index, Longueur = m.Groups["val"].Length, Fois = 1 };
                        Group com = m.Groups["com"];
                        r.Commentaire = com.Success;
                        if (com.Success && com.Value.StartsWith("one of ", StringComparison.Ordinal))
                            r.Liste = new List<string>(com.Value.Substring(7).Split(new[] { ", " }, StringSplitOptions.None));
                        d[cle] = r;
                    }
                }
                pos = fin + 1;
            }
            return d;
        }

        /// <summary>Réglage unique, valeur actuelle et visée parmi les crans du menu (ou case à cocher).</summary>
        private static bool Reconnu(Reglage r, string cible)
        {
            if (r == null || r.Fois != 1 || !Simple.IsMatch(cible) || !Simple.IsMatch(r.Valeur)) return false;
            // Résolution : ligne sans liste ni commentaire, valeur actuelle et visée « 1920x1080 ».
            if (r.Cle == "Resolution") return r.Liste == null && !r.Commentaire && FormatResolution.IsMatch(r.Valeur) && FormatResolution.IsMatch(cible);
            if (r.Liste != null) return r.Liste.Contains(r.Valeur) && r.Liste.Contains(cible);
            if (r.Commentaire) return false;   // plage de nombres : jamais écrite ici
            return (r.Valeur == "true" || r.Valeur == "false") && (cible == "true" || cible == "false");
        }

        /// <summary>Ce qui change vraiment pour ce mode ; réglages introuvables ou inattendus dans <paramref name="clesAbsentes"/>.</summary>
        public static List<Changement> Prevoit(string texte, Mode m, out List<string> clesAbsentes)
        {
            var res = new List<Changement>();
            clesAbsentes = new List<string>();
            Dictionary<string, Reglage> d = Reglages(texte);
            foreach (string[] t in Cibles)
            {
                string cible = t[1 + (int)m];
                Reglage r;
                if (!d.TryGetValue(t[0], out r) || !Reconnu(r, cible)) { clesAbsentes.Add(t[0]); continue; }
                if (string.Equals(r.Valeur, cible, StringComparison.Ordinal)) continue;
                res.Add(new Changement { Cle = t[0], Avant = r.Valeur, Apres = cible });
            }
            return res;
        }

        /// <summary>« 2560x1440 », ou null si la ligne manque ou n'a pas ce format.</summary>
        public static string ResolutionActuelle(string texte)
        {
            Reglage r;
            return Reglages(texte).TryGetValue("Resolution", out r) && Reconnu(r, r.Valeur) ? r.Valeur : null;
        }

        /// <summary>Modes que le fichier propose (« one of » de DisplayMode) et le mode actuel ; null si la ligne est inattendue.</summary>
        public static List<string> ModesAffichage(string texte, out string actuel)
        {
            actuel = null;
            Reglage r;
            if (!Reglages(texte).TryGetValue("DisplayMode", out r) || r.Liste == null || !Reconnu(r, r.Valeur)) return null;
            actuel = r.Valeur;
            return new List<string>(r.Liste);
        }

        /// <summary>Nom de l'écran choisi dans le jeu (« Monitor »), ou null.</summary>
        public static string Ecran(string texte)
        {
            Reglage r;
            return Reglages(texte).TryGetValue("Monitor", out r) && r.Fois == 1 && r.Valeur.Length > 0 ? r.Valeur : null;
        }

        /// <summary>
        /// Résolution et mode d'affichage demandés, seulement vers une valeur que le fichier accepte.
        /// PreferredDisplayMode (mode du plein écran) suit quand le mode visé est dans sa propre liste.
        /// </summary>
        public static List<Changement> PrevoitAffichage(string texte, ChoixAffichage a, out List<string> clesAbsentes)
        {
            var res = new List<Changement>();
            clesAbsentes = new List<string>();
            if (a == null) return res;
            Dictionary<string, Reglage> d = Reglages(texte);
            Reglage r;
            if (a.Resolution != null)
            {
                if (!d.TryGetValue("Resolution", out r) || !Reconnu(r, a.Resolution)) clesAbsentes.Add("Resolution");
                else if (r.Valeur != a.Resolution) res.Add(new Changement { Cle = "Resolution", Avant = r.Valeur, Apres = a.Resolution });
            }
            if (a.Mode != null)
            {
                if (!d.TryGetValue("DisplayMode", out r) || r.Liste == null || !Reconnu(r, a.Mode)) clesAbsentes.Add("DisplayMode");
                else
                {
                    if (r.Valeur != a.Mode) res.Add(new Changement { Cle = "DisplayMode", Avant = r.Valeur, Apres = a.Mode });
                    Reglage p;
                    if (d.TryGetValue("PreferredDisplayMode", out p) && p.Liste != null && Reconnu(p, a.Mode) && p.Valeur != a.Mode)
                        res.Add(new Changement { Cle = "PreferredDisplayMode", Avant = p.Valeur, Apres = a.Mode });
                }
            }
            return res;
        }

        public static string Applique(string texte, List<Changement> changements)
        {
            return Reecrit(texte, changements, (c, actuel) => c.Apres);
        }

        /// <summary>Remet Avant seulement là où la valeur vaut encore Apres (le joueur garde ce qu'il a changé dans le menu).</summary>
        public static string Retablit(string texteActuel, List<Changement> changements)
        {
            return Reecrit(texteActuel, changements, (c, actuel) => string.Equals(actuel, c.Apres, StringComparison.Ordinal) ? c.Avant : null);
        }

        /// <summary>Avant reste la valeur d'ORIGINE d'avant le premier préréglage.</summary>
        public static List<Changement> Fusionne(List<Changement> etat, List<Changement> nouveaux)
        {
            var res = new List<Changement>();
            if (etat != null) foreach (Changement e in etat) if (e != null) res.Add(new Changement { Cle = e.Cle, Avant = e.Avant, Apres = e.Apres });
            if (nouveaux == null) return res;
            foreach (Changement n in nouveaux)
            {
                if (n == null) continue;
                Changement e = res.Find(x => x.Cle == n.Cle);
                if (e == null) { res.Add(new Changement { Cle = n.Cle, Avant = n.Avant, Apres = n.Apres }); continue; }
                e.Apres = n.Apres;
                if (string.Equals(e.Avant, e.Apres, StringComparison.Ordinal)) res.Remove(e);
            }
            return res;
        }

        public static Dictionary<string, string> Valeurs(string texte)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Reglage> kv in Reglages(texte)) if (kv.Value.Fois == 1) d[kv.Key] = kv.Value.Valeur;
            return d;
        }

        /// <summary>Limite d'images en jeu (0 = aucune) et synchro verticale ; lues, jamais écrites.</summary>
        public static int LimiteImages(string texte, out bool vsync)
        {
            Dictionary<string, string> v = Valeurs(texte);
            string s;
            vsync = v.TryGetValue("VSync", out s) && s.Length > 0 && s != "disabled";
            int i;
            return v.TryGetValue("CapFps", out s) && s == "true"
                && v.TryGetValue("MaxFpsInGame", out s) && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out i) && i > 0
                ? i : 0;
        }

        /// <summary>« Réglages dans le cloud » activés dans le jeu : il peut remettre ses propres valeurs au lancement.</summary>
        public static bool Nuage(string texte)
        {
            string s;
            return Valeurs(texte).TryGetValue("ConfigCloudStorageEnabled", out s) && s == "true";
        }

        private static string Reecrit(string texte, List<Changement> changements, Func<Changement, string, string> nouvelle)
        {
            if (texte == null || changements == null || changements.Count == 0) return texte;
            Dictionary<string, Reglage> d = Reglages(texte);
            var coupes = new List<KeyValuePair<Reglage, string>>();
            var faits = new HashSet<string>(StringComparer.Ordinal);
            foreach (Changement c in changements)
            {
                Reglage r;
                if (c == null || c.Cle == null || !faits.Add(c.Cle) || !d.TryGetValue(c.Cle, out r)) continue;
                string v = nouvelle(c, r.Valeur);
                if (v == null || v == r.Valeur || !Reconnu(r, v)) continue;   // jamais un cran que le menu ne propose pas
                coupes.Add(new KeyValuePair<Reglage, string>(r, v));
            }
            if (coupes.Count == 0) return texte;
            coupes.Sort((x, y) => x.Key.Debut.CompareTo(y.Key.Debut));
            var sb = new StringBuilder(texte.Length + 64);
            int pos = 0;
            foreach (var k in coupes)
            {
                sb.Append(texte, pos, k.Key.Debut - pos).Append(k.Value);
                pos = k.Key.Debut + k.Key.Longueur;
            }
            sb.Append(texte, pos, texte.Length - pos);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ textes

        private static readonly Dictionary<string, string> Noms = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "ShadowQuality", "Qualité des ombres" }, { "ScreenSpaceShadowQuality", "Ombres en espace écran" },
            { "AmbientLightingQuality", "Éclairage ambiant" }, { "SSRQuality", "Réflexions en espace écran" },
            { "ParticleQuality", "Qualité des particules" }, { "ModelQuality", "Qualité des modèles" },
            { "DeferredPhysics", "Qualité de la physique" }, { "WaterCausticsMode", "Caustiques de l'eau" },
            { "Tessellation", "Tessellation" }, { "TextureFilter", "Filtrage des textures" },
            { "WorldStreamingQuality", "Chargement du décor" }, { "DepthOfField", "Profondeur de champ" },
            { "VRS", "Ombrage à taux variable (VRS)" },
            { "Resolution", "Résolution" }, { "DisplayMode", "Mode d'affichage" }, { "PreferredDisplayMode", "Mode du plein écran" },
        };

        private static readonly Dictionary<string, string> Crans = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Off", "désactivé" }, { "Very_Low", "Très bas" }, { "very low", "Très bas" }, { "Low", "Bas" }, { "Medium", "Moyen" },
            { "High", "Élevé" }, { "Very_High", "Très élevé" }, { "Ultra", "Ultra" },
            { "Low Quality", "Bas" }, { "Medium Quality", "Moyen" }, { "High Quality", "Élevé" },
            { "0_Off", "désactivée" }, { "1_Near", "Proche" }, { "2_All", "Partout" },
            { "true", "activé" }, { "false", "désactivé" },
            { "Windowed", "Fenêtré" }, { "Fullscreen", "Plein écran" }, { "Fullscreen borderless window", "Plein écran fenêtré" },
            { "Fullscreen borderless extended window", "Plein écran fenêtré étendu" },
        };

        /// <summary>Libellé d'un mode d'affichage du fichier (« Plein écran fenêtré »).</summary>
        public static string NomAffichage(string v) { return Valeur(v); }

        public static string Libelle(string cle)
        {
            string n;
            return cle != null && Noms.TryGetValue(cle, out n) ? n : cle;
        }

        private static string Valeur(string v)
        {
            string n;
            if (v == null) return "";
            if (Crans.TryGetValue(v, out n)) return n;
            return v.StartsWith("aniso ", StringComparison.Ordinal) ? v.Substring(6) : v;
        }

        /// <summary>« Qualité des ombres : Élevé → Très bas ».</summary>
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
                if (c.Length < 3 || c[0].Length == 0 || !Simple.IsMatch(c[1]) || !Simple.IsMatch(c[2])) continue;
                res.Add(new Changement { Cle = c[0], Avant = c[1], Apres = c[2] });
            }
            return res;
        }

        public static string Ecrire(IEnumerable<Changement> changements)
        {
            var sb = new StringBuilder();
            if (changements == null) return "";
            foreach (Changement c in changements)
                if (c != null) sb.Append(Champ(c.Cle)).Append('\t').Append(Champ(c.Avant)).Append('\t').Append(Champ(c.Apres)).Append('\n');
            return sb.ToString();
        }

        private static string Champ(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
    }
}
