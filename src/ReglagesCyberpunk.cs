using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// PRÉRÉGLAGES GRAPHIQUES CYBERPUNK 2077 (logique PURE) : quatre modes écrits dans
    /// UserSettings.json (%LOCALAPPDATA%\CD Projekt Red\Cyberpunk 2077, version 152 relevée le
    /// 27/11/2026). Chaque option vit dans un groupe (« /graphics/advanced ») sous la forme
    /// {"name","type","value","index","values"} : index = position de value dans values. Seuls
    /// les jetons value et index des options visées sont réécrits, le reste du fichier reste
    /// identique octet pour octet ; une option absente, ou dont la valeur n'est pas dans la liste
    /// du jeu, n'est ni ajoutée ni touchée. Valeurs : proposition d'ONYX, uniquement des crans que
    /// le menu propose. Jamais touchés : résolution, DLSS/FSR/XeSS, génération d'images,
    /// anticrénelage, synchronisation verticale, limite d'images, Reflex, préréglage rapide.
    /// </summary>
    internal static partial class ReglagesCyberpunk
    {
        public enum Mode { Competition, Performance, Equilibre, Qualite }

        public sealed class Changement { public string Section, Cle, Avant, Apres; }

        private const string Pre = "/graphics/presets", Ava = "/graphics/advanced", Rt = "/graphics/raytracing",
                             Perf = "/graphics/performance", Base = "/graphics/basic";

        // Groupe, option, valeurs pour Compétition, Performance, Équilibré, Qualité (null : non touché dans ce mode).
        private static readonly string[][] Cibles =
        {
            new[] { Pre,  "TextureQuality",                "Medium", "High",   "High",   "High" },
            new[] { Ava,  "ContactShadows",                "false",  "false",  "true",   "true" },
            new[] { Ava,  "FacialTangentUpdates",          "false",  "false",  "true",   "true" },
            new[] { Ava,  "Anisotropy",                    "8",      "16",     "16",     "16" },
            new[] { Ava,  "ShadowMeshQuality",             "Low",    "Medium", "High",   "High" },
            new[] { Ava,  "LocalShadowsQuality",           "Low",    "Medium", "Medium", "High" },
            new[] { Ava,  "CascadedShadowsRange",          "Low",    "Low",    "Medium", "High" },
            new[] { Ava,  "CascadedShadowsResolution",     "Low",    "Medium", "Medium", "High" },
            new[] { Ava,  "DistantShadowsResolution",      "Low",    "Low",    "High",   "High" },
            new[] { Ava,  "VolumetricFogResolution",       "Low",    "Low",    "Medium", "High" },
            new[] { Ava,  "VolumetricCloudsQuality",       "Off",    "Medium", "Medium", "High" },
            new[] { Ava,  "MaxDynamicDecals",              "Low",    "Medium", "High",   "Ultra" },
            new[] { Ava,  "ScreenSpaceReflectionsQuality", "Off",    "Low",    "Medium", "High" },
            new[] { Ava,  "SubsurfaceScatteringQuality",   "Low",    "Medium", "High",   "High" },
            new[] { Ava,  "AmbientOcclusion",              "Low",    "Low",    "Medium", "High" },
            new[] { Ava,  "ColorPrecision",                "Medium", "Medium", "High",   "High" },
            new[] { Ava,  "MirrorQuality",                 "Low",    "Low",    "Medium", "High" },
            new[] { Ava,  "LODPreset",                     "Medium", "Medium", "High",   "High" },
            new[] { Perf, "CrowdDensity",                  "Low",    "Medium", "Medium", "High" },
            new[] { Rt,   "RayTracing",                    "false",  "false",  "false",  null },
            new[] { Base, "FilmGrain",                     "false",  "false",  "false",  "false" },
            new[] { Base, "ChromaticAberration",           "false",  "false",  "false",  "false" },
            new[] { Base, "DepthOfField",                  "false",  "false",  "true",   "true" },
            new[] { Base, "LensFlares",                    "false",  "false",  "true",   "true" },
            new[] { Base, "MotionBlur",                    "Off",    "Off",    "Off",    "Low" },
        };

        /// <summary>Valeurs réécrites telles quelles entre guillemets : rien qui demande un échappement JSON.</summary>
        private static readonly Regex Simple = new Regex(@"^[A-Za-z0-9 _+.\-]{1,64}$", RegexOptions.CultureInvariant);

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

        /// <summary>
        /// Ce qui change vraiment pour ce mode. Options introuvables, ou dont la valeur actuelle ou
        /// visée sort de la liste du jeu (fichier d'une autre version) : dans <paramref name="clesAbsentes"/>, non touchées.
        /// </summary>
        public static List<Changement> Prevoit(string json, Mode m, out List<string> clesAbsentes)
        {
            var res = new List<Changement>();
            clesAbsentes = new List<string>();
            Dictionary<string, Option> opts = Options(json);
            foreach (string[] t in Cibles)
            {
                string cible = t[2 + (int)m];
                if (cible == null) continue;
                Option o;
                if (opts == null || !opts.TryGetValue(Id(t[0], t[1]), out o) || !Reconnue(o, cible)) { clesAbsentes.Add(t[1]); continue; }
                if (string.Equals(o.Valeur, cible, StringComparison.Ordinal)) continue;
                res.Add(new Changement { Section = t[0], Cle = t[1], Avant = o.Valeur, Apres = cible });
            }
            return res;
        }

        public static string Applique(string json, List<Changement> changements)
        {
            return Reecrit(json, changements, (c, actuel) => c.Apres);
        }

        /// <summary>Remet Avant seulement là où la valeur vaut encore Apres (le joueur garde ce qu'il a changé dans le menu).</summary>
        public static string Retablit(string jsonActuel, List<Changement> changements)
        {
            return Reecrit(jsonActuel, changements, (c, actuel) => string.Equals(actuel, c.Apres, StringComparison.Ordinal) ? c.Avant : null);
        }

        /// <summary>Comme ReglagesFortnite.Fusionne : Avant reste la valeur d'ORIGINE d'avant le premier préréglage.</summary>
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
                if (string.Equals(e.Avant, e.Apres, StringComparison.Ordinal)) res.Remove(e);
            }
            return res;
        }

        /// <summary>Valeur actuelle de chaque option (clé : groupe + nom) ; vide si le fichier est illisible.</summary>
        public static Dictionary<string, string> Valeurs(string json)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, Option> opts = Options(json);
            if (opts != null) foreach (KeyValuePair<string, Option> kv in opts) d[kv.Key] = kv.Value.Valeur;
            return d;
        }

        /// <summary>Limite d'images du jeu (0 = aucune) et synchro verticale ; lues, jamais écrites.</summary>
        public static int LimiteImages(string json, out bool vsync)
        {
            Dictionary<string, string> v = Valeurs(json);
            string s, n;
            vsync = v.TryGetValue(Id("/video/display", "VSync"), out s) && !string.IsNullOrEmpty(s) && !s.EndsWith("-Off", StringComparison.Ordinal);
            int i;
            return v.TryGetValue(Id("/video/display", "MaximumFPS_OnOff"), out s) && s == "true"
                && v.TryGetValue(Id("/video/display", "MaximumFPS_Value"), out n) && int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out i) && i > 0
                ? i : 0;
        }

        public static string Id(string groupe, string nom) { return (groupe ?? "") + "\n" + (nom ?? ""); }

        // ------------------------------------------------------------------ textes

        private static readonly Dictionary<string, string> Noms = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "TextureQuality", "Qualité des textures" }, { "ContactShadows", "Ombres de contact" },
            { "FacialTangentUpdates", "Éclairage amélioré du visage" }, { "Anisotropy", "Filtrage anisotrope" },
            { "ShadowMeshQuality", "Maillage des ombres" }, { "LocalShadowsQuality", "Ombres locales" },
            { "CascadedShadowsRange", "Portée des ombres en cascade" }, { "CascadedShadowsResolution", "Résolution des ombres en cascade" },
            { "DistantShadowsResolution", "Ombres lointaines" }, { "VolumetricFogResolution", "Brouillard volumétrique" },
            { "VolumetricCloudsQuality", "Nuages volumétriques" }, { "MaxDynamicDecals", "Décalcomanies dynamiques" },
            { "ScreenSpaceReflectionsQuality", "Réflexions en espace écran" }, { "SubsurfaceScatteringQuality", "Diffusion sous la surface" },
            { "AmbientOcclusion", "Occlusion ambiante" }, { "ColorPrecision", "Précision des couleurs" },
            { "MirrorQuality", "Qualité des miroirs" }, { "LODPreset", "Niveau de détail" }, { "CrowdDensity", "Densité de la foule" },
            { "RayTracing", "Ray tracing" }, { "FilmGrain", "Grain" }, { "ChromaticAberration", "Aberration chromatique" },
            { "DepthOfField", "Profondeur de champ" }, { "LensFlares", "Reflets d'objectif" }, { "MotionBlur", "Flou de mouvement" },
        };

        private static readonly Dictionary<string, string> Crans = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Off", "désactivé" }, { "Low", "Bas" }, { "Medium", "Moyen" }, { "High", "Élevé" }, { "Ultra", "Ultra" },
            { "Insane", "Démentiel" }, { "Psycho", "Psycho" }, { "true", "activé" }, { "false", "désactivé" },
        };

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
            int i;
            return int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out i) ? v + "x" : v;
        }

        /// <summary>« Ombres locales : Élevé → Bas ».</summary>
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
                if (c.Length < 4 || c[0].Length == 0 || c[1].Length == 0 || !Simple.IsMatch(c[2]) || !Simple.IsMatch(c[3])) continue;
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

        private static string Champ(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

        // ------------------------------------------------------------------ lecture du JSON

        private sealed class Option
        {
            public string Groupe, Nom, Type, Valeur;
            public int ValeurDebut = -1, ValeurLong, IndexDebut = -1, IndexLong, Index = int.MinValue;
            public List<string> Liste;
        }

        private static bool EstListe(string type) { return type == "string_list" || type == "name_list" || type == "int_list"; }

        /// <summary>Type connu, valeur actuelle et visée valides pour ce type, index cohérent avec la liste.</summary>
        private static bool Reconnue(Option o, string cible)
        {
            if (o.ValeurDebut < 0 || o.Valeur == null) return false;
            if (o.Type == "bool") return (cible == "true" || cible == "false") && (o.Valeur == "true" || o.Valeur == "false");
            if (!EstListe(o.Type) || o.Liste == null || o.IndexDebut < 0) return false;
            if (!Simple.IsMatch(o.Valeur) || !Simple.IsMatch(cible)) return false;
            return o.Index >= 0 && o.Index < o.Liste.Count && o.Liste[o.Index] == o.Valeur && o.Liste.IndexOf(cible) >= 0;
        }

        private sealed class Cadre
        {
            public bool Objet;
            public string Propriete;            // propriété en attente de sa valeur (objets)
            public string Groupe;               // « group_name » de cet objet, s'il en a un
            public Option Opt;                  // objet qui porte un « name »
            public bool ListeDeValeurs;         // tableau « values » de l'option parente
        }

        /// <summary>Options du fichier par groupe + nom, avec la place de leurs jetons ; null si le JSON est illisible.</summary>
        private static Dictionary<string, Option> Options(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            byte[] b = Encoding.UTF8.GetBytes(json);
            return Options(b);
        }

        private static Dictionary<string, Option> Options(byte[] b)
        {
            var res = new Dictionary<string, Option>(StringComparer.Ordinal);
            var pile = new List<Cadre>();
            try
            {
                var r = new Utf8JsonReader(b, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                while (r.Read())
                {
                    Cadre haut = pile.Count > 0 ? pile[pile.Count - 1] : null;
                    switch (r.TokenType)
                    {
                        case JsonTokenType.StartObject:
                            Prend(haut);
                            pile.Add(new Cadre { Objet = true, Opt = new Option() });
                            break;
                        case JsonTokenType.StartArray:
                            bool valeurs = haut != null && haut.Objet && haut.Propriete == "values";
                            Prend(haut);
                            pile.Add(new Cadre { ListeDeValeurs = valeurs });
                            if (valeurs) haut.Opt.Liste = new List<string>();
                            break;
                        case JsonTokenType.EndObject:
                            if (haut != null && haut.Opt.Nom != null && haut.Opt.Type != null)
                            {
                                Option o = haut.Opt;
                                o.Groupe = GroupeParent(pile);
                                if (o.Groupe != null && !res.ContainsKey(Id(o.Groupe, o.Nom))) res[Id(o.Groupe, o.Nom)] = o;
                            }
                            pile.RemoveAt(pile.Count - 1);
                            break;
                        case JsonTokenType.EndArray:
                            pile.RemoveAt(pile.Count - 1);
                            break;
                        case JsonTokenType.PropertyName:
                            if (haut != null) haut.Propriete = r.GetString();
                            break;
                        default:   // valeur simple
                            if (haut == null) break;
                            if (!haut.Objet)
                            {
                                if (haut.ListeDeValeurs) pile[pile.Count - 2].Opt.Liste.Add(Texte(ref r));
                                break;
                            }
                            string p = haut.Propriete;
                            haut.Propriete = null;
                            int debut = (int)r.TokenStartIndex, longueur = r.ValueSpan.Length + (r.TokenType == JsonTokenType.String ? 2 : 0);
                            if (p == "group_name" && r.TokenType == JsonTokenType.String) haut.Groupe = r.GetString();
                            else if (p == "name" && r.TokenType == JsonTokenType.String) haut.Opt.Nom = r.GetString();
                            else if (p == "type" && r.TokenType == JsonTokenType.String) haut.Opt.Type = r.GetString();
                            else if (p == "value") { haut.Opt.Valeur = Texte(ref r); haut.Opt.ValeurDebut = debut; haut.Opt.ValeurLong = longueur; }
                            else if (p == "index" && r.TokenType == JsonTokenType.Number)
                            {
                                int i;
                                if (r.TryGetInt32(out i)) { haut.Opt.Index = i; haut.Opt.IndexDebut = debut; haut.Opt.IndexLong = longueur; }
                            }
                            break;
                    }
                }
                return pile.Count == 0 ? res : null;
            }
            catch { return null; }
        }

        /// <summary>La valeur d'une propriété est un objet ou un tableau : la propriété n'attend plus rien.</summary>
        private static void Prend(Cadre haut) { if (haut != null && haut.Objet) haut.Propriete = null; }

        private static string GroupeParent(List<Cadre> pile)
        {
            for (int i = pile.Count - 2; i >= 0; i--) if (pile[i].Groupe != null) return pile[i].Groupe;
            return null;
        }

        private static string Texte(ref Utf8JsonReader r)
        {
            switch (r.TokenType)
            {
                case JsonTokenType.String: return r.GetString();
                case JsonTokenType.True: return "true";
                case JsonTokenType.False: return "false";
                case JsonTokenType.Number: return Encoding.UTF8.GetString(r.ValueSpan);
                default: return null;
            }
        }

        private static string Jeton(Option o, string v)
        {
            if (o.Type == "bool") return v == "true" || v == "false" ? v : null;
            if (!Simple.IsMatch(v)) return null;
            return o.Type == "int_list" ? v : "\"" + v + "\"";
        }

        private static string Reecrit(string json, List<Changement> changements, Func<Changement, string, string> nouvelle)
        {
            if (json == null || changements == null || changements.Count == 0) return json;
            byte[] b = Encoding.UTF8.GetBytes(json);
            Dictionary<string, Option> opts = Options(b);
            if (opts == null) return json;
            var coupes = new List<KeyValuePair<int, KeyValuePair<int, string>>>();   // début, (longueur, texte)
            var faits = new HashSet<string>(StringComparer.Ordinal);
            foreach (Changement c in changements)
            {
                Option o;
                if (c == null || !faits.Add(Id(c.Section, c.Cle)) || !opts.TryGetValue(Id(c.Section, c.Cle), out o) || o.ValeurDebut < 0) continue;
                string v = nouvelle(c, o.Valeur);
                if (v == null || v == o.Valeur) continue;
                string jeton = Jeton(o, v);
                if (jeton == null) continue;
                if (EstListe(o.Type))
                {
                    int i = o.Liste == null ? -1 : o.Liste.IndexOf(v);
                    if (i < 0 || o.IndexDebut < 0) continue;   // valeur que le menu ne propose pas : on ne touche à rien
                    coupes.Add(new KeyValuePair<int, KeyValuePair<int, string>>(o.IndexDebut, new KeyValuePair<int, string>(o.IndexLong, i.ToString(CultureInfo.InvariantCulture))));
                }
                coupes.Add(new KeyValuePair<int, KeyValuePair<int, string>>(o.ValeurDebut, new KeyValuePair<int, string>(o.ValeurLong, jeton)));
            }
            if (coupes.Count == 0) return json;
            coupes.Sort((x, y) => x.Key.CompareTo(y.Key));
            var sortie = new List<byte>(b.Length + 64);
            int pos = 0;
            foreach (var k in coupes)
            {
                for (int i = pos; i < k.Key; i++) sortie.Add(b[i]);
                sortie.AddRange(Encoding.UTF8.GetBytes(k.Value.Value));
                pos = k.Key + k.Value.Key;
            }
            for (int i = pos; i < b.Length; i++) sortie.Add(b[i]);
            return Encoding.UTF8.GetString(sortie.ToArray());
        }
    }
}
