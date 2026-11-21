using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Une offre de jeu (promo Steam, jeu gratuit Epic ou GamerPower). Prix en centimes d'euro.
    /// </summary>
    internal sealed class Offre
    {
        public string Source;          // "steam" | "epic" | "gamerpower"
        public string Id;              // identifiant dans la source
        public string Nom;
        public int PrixCts = -1;       // centimes d'euro ; 0 = gratuit ; -1 = inconnu
        public int PrixOrigineCts = -1;
        public int Remise;             // 0 à 100
        public DateTime? Fin;          // fin de l'offre, UTC
        public DateTime? Sortie;       // date de sortie, UTC
        public string[] Plateformes = new string[0];
        public int[] Tags = new int[0];   // tagids Steam
        public string Url;
        public int SteamAppId;         // 0 hors Steam
        public string Cle { get { return Source + ":" + Id; } }
    }

    /// <summary>
    /// Bons plans (logique PURE) : prix affichés, plafonds, catégories, filtre, tri, dédoublonnage
    /// et contrôle des liens. Ni réseau, ni disque, ni horloge : le réseau vit dans OffresReseau.
    /// </summary>
    internal static class Offres
    {
        public sealed class Filtre { public string Texte; public int PlafondCts = -1; public int Tag; public string Plateforme; }

        public static readonly int[] PlafondsCts = { 0, 100, 500, 1000, 2000 };

        /// <summary>Tags Steam (store.steampowered.com/tagdata/populartags/french, relevés le 21/11/2026).</summary>
        public static readonly KeyValuePair<string, int>[] Categories =
        {
            new KeyValuePair<string, int>("Action", 19),
            new KeyValuePair<string, int>("Aventure", 21),
            new KeyValuePair<string, int>("RPG", 122),
            new KeyValuePair<string, int>("Stratégie", 9),
            new KeyValuePair<string, int>("Simulation", 599),
            new KeyValuePair<string, int>("Indépendant", 492),
            new KeyValuePair<string, int>("Casual", 597),
            new KeyValuePair<string, int>("Multijoueur", 3859),
            new KeyValuePair<string, int>("Coop", 1685),
            new KeyValuePair<string, int>("FPS", 1663),
            new KeyValuePair<string, int>("Tir", 1774),
            new KeyValuePair<string, int>("Course", 699),
            new KeyValuePair<string, int>("Sport", 701),
            new KeyValuePair<string, int>("Horreur", 1667),
            new KeyValuePair<string, int>("Survie", 1662),
            new KeyValuePair<string, int>("Monde ouvert", 1695),
            new KeyValuePair<string, int>("Gestion", 12472),
            new KeyValuePair<string, int>("Construction", 1643),
        };

        public static readonly string[] Plateformes =
            { "Steam", "Epic Games", "GOG", "PlayStation", "Xbox", "Switch", "Android", "iOS", "PC" };

        public static string LibellePlafond(int cts)
        {
            if (cts < 0) return "Tous";
            if (cts == 0) return "Gratuit";
            return Prix(cts) + " max";
        }

        /// <summary>749 → « 7,49 € », 2000 → « 20 € », 0 → « Gratuit », -1 → « — ».</summary>
        public static string Prix(int cts)
        {
            if (cts < 0) return "—";
            if (cts == 0) return "Gratuit";
            int euros = cts / 100, centimes = cts % 100;
            if (centimes == 0) return euros.ToString(CultureInfo.InvariantCulture) + " €";
            return euros.ToString(CultureInfo.InvariantCulture) + "," + centimes.ToString("00", CultureInfo.InvariantCulture) + " €";
        }

        /// <summary>Plafond inclusif ; un prix inconnu ne passe sous aucun plafond.</summary>
        public static bool SousPlafond(Offre o, int plafondCts)
        {
            if (plafondCts < 0) return true;
            return o != null && o.PrixCts >= 0 && o.PrixCts <= plafondCts;
        }

        /// <summary>Minuscules, sans accents, lettres et chiffres seulement (« Épée & Bouclier » → « epeebouclier »).</summary>
        public static string NomNormalise(string nom)
        {
            if (string.IsNullOrEmpty(nom)) return "";
            var sb = new StringBuilder(nom.Length);
            foreach (char c in nom.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>Seul un lien absolu http ou https venu d'une API ou d'un flux est ouvert.</summary>
        public static bool LienSur(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            Uri u;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out u)) return false;
            return u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp;
        }

        public static List<Offre> Filtrer(IEnumerable<Offre> offres, Filtre f)
        {
            var res = new List<Offre>();
            if (offres == null) return res;
            f = f ?? new Filtre();
            string texte = NomNormalise(f.Texte);
            foreach (var o in offres)
            {
                if (o == null || string.IsNullOrWhiteSpace(o.Nom)) continue;
                if (texte.Length > 0 && !NomNormalise(o.Nom).Contains(texte)) continue;
                if (!SousPlafond(o, f.PlafondCts)) continue;
                if (f.Tag > 0 && (o.Tags == null || Array.IndexOf(o.Tags, f.Tag) < 0)) continue;
                if (!string.IsNullOrEmpty(f.Plateforme)
                    && (o.Plateformes == null || !o.Plateformes.Any(p => string.Equals(p, f.Plateforme, StringComparison.OrdinalIgnoreCase))))
                    continue;
                res.Add(o);
            }
            return res;
        }

        /// <summary>Tri stable : « remise » (défaut), « prix », « recent », « nom ».</summary>
        public static List<Offre> Trier(List<Offre> offres, string tri)
        {
            if (offres == null) return new List<Offre>();
            switch (tri)
            {
                case "prix":
                    return offres.OrderBy(o => o.PrixCts < 0 ? 1 : 0).ThenBy(o => o.PrixCts).ToList();
                case "recent":
                    return offres.OrderBy(o => o.Sortie.HasValue ? 0 : 1).ThenByDescending(o => o.Sortie ?? DateTime.MinValue).ToList();
                case "nom":
                    return offres.OrderBy(o => o.Nom ?? "", StringComparer.Create(CultureInfo.GetCultureInfo("fr-FR"), true)).ToList();
                default:
                    return offres.OrderByDescending(o => o.Remise).ToList();
            }
        }

        /// <summary>Offres encore valables : sans date de fin, ou fin pas encore passée.</summary>
        public static List<Offre> EnCours(IEnumerable<Offre> offres, DateTime maintenantUtc)
        {
            if (offres == null) return new List<Offre>();
            return offres.Where(o => o != null && (!o.Fin.HasValue || o.Fin.Value >= maintenantUtc)).ToList();
        }

        /// <summary>Retire les offres des sources nommées (sources en échec : leur cache peut être périmé).</summary>
        public static List<Offre> SansSources(IEnumerable<Offre> offres, IEnumerable<string> sources)
        {
            if (offres == null) return new List<Offre>();
            var exclues = new HashSet<string>(sources ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return offres.Where(o => o != null && !exclues.Contains(o.Source ?? "")).ToList();
        }

        /// <summary>Une offre par clé ; parmi les gratuites, une par nom (Epic préféré à GamerPower).</summary>
        public static List<Offre> Dedoublonner(IEnumerable<Offre> offres)
        {
            var res = new List<Offre>();
            if (offres == null) return res;
            var cles = new HashSet<string>();
            var gratuits = new Dictionary<string, int>();   // nom normalisé → index dans res
            foreach (var o in offres)
            {
                if (o == null || !cles.Add(o.Cle)) continue;
                string n = o.PrixCts == 0 ? NomNormalise(o.Nom) : "";
                if (n.Length == 0) { res.Add(o); continue; }
                int i;
                if (!gratuits.TryGetValue(n, out i)) { gratuits[n] = res.Count; res.Add(o); continue; }
                if (o.Source == "epic" && res[i].Source != "epic") res[i] = o;
            }
            return res;
        }
    }
}
