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
        public string Source;          // "steam" | "epic" | "gamerpower" | "gog" | "nintendo" | "itch" | store CheapShark
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
        public string Image;           // jaquette ou vignette (URL), null si la source n'en donne pas
        public string Devise = "EUR";  // "EUR" ou "USD" (CheapShark, itch.io : prix US affichés tels quels)
        public string Cle { get { return Source + ":" + Id; } }
        public Offre Copie() { return (Offre)MemberwiseClone(); }
    }

    /// <summary>
    /// Bons plans (logique PURE) : prix affichés, plafonds, catégories, filtre, tri, dédoublonnage
    /// et contrôle des liens. Ni réseau, ni disque, ni horloge : le réseau vit dans OffresReseau.
    /// </summary>
    internal static class Offres
    {
        /// <summary>
        /// PlafondCts : prix maximum inclusif (-1 : aucun) ; PrixMinCts : prix minimum exclusif (-1 : aucun) ;
        /// TauxUsd : dollars pour 1 euro (BCE), 0 si inconnu — sert à placer les prix en dollars dans une tranche.
        /// </summary>
        public sealed class Filtre { public string Texte; public int PlafondCts = -1, PrixMinCts = -1; public decimal TauxUsd; public int Tag; public string Plateforme; }

        /// <summary>Une tranche de prix : au-dessus de MinCts (exclusif, -1 : aucun) et jusqu'à MaxCts (inclusif, -1 : aucun).</summary>
        public sealed class Tranche
        {
            public readonly string Libelle; public readonly int MinCts, MaxCts;
            public Tranche(string libelle, int minCts, int maxCts) { Libelle = libelle; MinCts = minCts; MaxCts = maxCts; }
        }

        /// <summary>Tranches exclusives : « 5 – 10 € » ne montre pas les jeux à 1 €. Une borne haute est incluse (5 € est dans « 1 – 5 € »).</summary>
        public static readonly Tranche[] Tranches =
        {
            new Tranche("Tous", -1, -1),
            new Tranche("Gratuit", -1, 0),
            new Tranche("Moins de 1 €", 0, 100),
            new Tranche("1 – 5 €", 100, 500),
            new Tranche("5 – 10 €", 500, 1000),
            new Tranche("10 – 20 €", 1000, 2000),
            new Tranche("20 – 40 €", 2000, 4000),
            new Tranche("Plus de 40 €", 4000, -1),
        };

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
            { "Steam", "Epic Games", "GOG", "itch.io", "Ubisoft", "EA", "Battle.net", "PlayStation", "Xbox", "Switch", "Android", "iOS", "PC" };

        public static string LibellePlafond(int cts)
        {
            if (cts < 0) return "Tous";
            if (cts == 0) return "Gratuit";
            return Prix(cts) + " max";
        }

        /// <summary>749 → « 7,49 € », 2000 → « 20 € », 0 → « Gratuit », -1 → « — ».</summary>
        public static string Prix(int cts) { return Prix(cts, "EUR"); }

        /// <summary>Devise "USD" → « 9,99 $ » ; toute autre valeur → euros.</summary>
        public static string Prix(int cts, string devise)
        {
            if (cts < 0) return "—";
            if (cts == 0) return "Gratuit";
            string sym = devise == "USD" ? " $" : " €";
            int unites = cts / 100, centimes = cts % 100;
            if (centimes == 0) return unites.ToString(CultureInfo.InvariantCulture) + sym;
            return unites.ToString(CultureInfo.InvariantCulture) + "," + centimes.ToString("00", CultureInfo.InvariantCulture) + sym;
        }

        public static string PrixDe(Offre o) { return o == null ? "—" : Prix(o.PrixCts, o.Devise); }
        public static string PrixOrigineDe(Offre o) { return o == null ? "—" : Prix(o.PrixOrigineCts, o.Devise); }

        /// <summary>
        /// Plafond inclusif ; un prix inconnu ne passe sous aucun plafond. Les plafonds sont en euros :
        /// une offre en dollars n'y est pas soumise, sauf « Gratuit » (0).
        /// </summary>
        public static bool SousPlafond(Offre o, int plafondCts)
        {
            if (plafondCts < 0) return true;
            if (o == null || o.PrixCts < 0) return false;
            if (o.Devise == "USD" && plafondCts > 0) return true;
            return o.PrixCts <= plafondCts;
        }

        /// <summary>Prix en centimes d'euro : dollars convertis au taux BCE (dollars pour 1 euro) ; -1 si inconnu ou taux absent.</summary>
        public static int PrixEnEuros(Offre o, decimal tauxUsd)
        {
            if (o == null || o.PrixCts < 0) return -1;
            if (o.Devise != "USD" || o.PrixCts == 0) return o.PrixCts;
            if (tauxUsd <= 0m) return -1;
            return (int)Math.Round(o.PrixCts / tauxUsd, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Prix dans la tranche ]minCts ; maxCts] (-1 : borne absente). Un prix inconnu n'entre dans aucune tranche bornée.
        /// Dollars : convertis si le taux est connu ; sinon soumis seulement à « Gratuit », comme SousPlafond.
        /// </summary>
        public static bool DansPrix(Offre o, int minCts, int maxCts, decimal tauxUsd)
        {
            if (minCts < 0 && maxCts < 0) return true;
            if (o == null || o.PrixCts < 0) return false;
            if (o.Devise == "USD" && o.PrixCts > 0 && tauxUsd <= 0m) return maxCts != 0;
            int eur = PrixEnEuros(o, tauxUsd);
            if (eur < 0) return false;
            if (maxCts >= 0 && eur > maxCts) return false;
            if (minCts >= 0 && eur <= minCts) return false;
            return true;
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
                if (!DansPrix(o, f.PrixMinCts, f.PlafondCts, f.TauxUsd)) continue;
                if (f.Tag > 0 && (o.Tags == null || Array.IndexOf(o.Tags, f.Tag) < 0)) continue;
                if (!string.IsNullOrEmpty(f.Plateforme)
                    && (o.Plateformes == null || !o.Plateformes.Any(p => string.Equals(p, f.Plateforme, StringComparison.OrdinalIgnoreCase))))
                    continue;
                res.Add(o);
            }
            return res;
        }

        /// <summary>Tri stable : « remise » (défaut), « prix », « recent », « nom ».</summary>
        public static List<Offre> Trier(List<Offre> offres, string tri) { return Trier(offres, tri, 0m); }

        /// <summary>« prix » (croissant) et « prixdesc » comparent en euros : dollars convertis au taux donné (0 : tels quels).</summary>
        public static List<Offre> Trier(List<Offre> offres, string tri, decimal tauxUsd)
        {
            if (offres == null) return new List<Offre>();
            Func<Offre, int> eur = o => tauxUsd > 0m ? PrixEnEuros(o, tauxUsd) : o.PrixCts;
            switch (tri)
            {
                case "prix":
                    return offres.OrderBy(o => eur(o) < 0 ? 1 : 0).ThenBy(eur).ToList();
                case "prixdesc":
                    return offres.OrderBy(o => eur(o) < 0 ? 1 : 0).ThenByDescending(eur).ToList();
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
