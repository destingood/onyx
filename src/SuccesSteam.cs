using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// SUCCÈS STEAM — PUR : lit les deux fichiers que Steam garde en cache sur le PC
    /// (appcache\stats) : UserGameStatsSchema_&lt;jeu&gt;.bin (la liste des succès, noms traduits)
    /// et UserGameStats_&lt;compte&gt;_&lt;jeu&gt;.bin (ce que le joueur a débloqué). Format « KeyValues
    /// binaire » de Valve : un octet de type, un nom terminé par 0, la valeur. Un succès est un bit
    /// d'un « stat » de type ACHIEVEMENTS : débloqué si ce bit est à 1 dans « data » du joueur,
    /// avec sa date dans AchievementTimes. Rien ne part sur le réseau.
    /// </summary>
    internal static partial class SuccesSteam
    {
        public sealed class Noeud
        {
            public string Nom = "";
            public string Texte;          // valeur texte (type 1)
            public long? Nombre;          // valeur entière (types 2 et 7)
            public readonly List<Noeud> Enfants = new List<Noeud>();

            public Noeud Enfant(string nom)
            {
                foreach (Noeud n in Enfants) if (string.Equals(n.Nom, nom, StringComparison.OrdinalIgnoreCase)) return n;
                return null;
            }
        }

        public sealed class Succes
        {
            public string Nom = "", Description = "";
            public bool Cache, Obtenu;
            public long DateUnix;
            public int Ordre;
        }

        public sealed class Bilan
        {
            public string Jeu = "";
            public readonly List<Succes> Liste = new List<Succes>();
            public int Obtenus, Total;
        }

        private const int Profondeur = 32;

        /// <summary>Arbre du fichier ; null s'il est vide, tronqué ou d'un type inconnu.</summary>
        public static Noeud LitKv(byte[] b)
        {
            if (b == null || b.Length == 0) return null;
            var racine = new Noeud();
            int i = 0;
            return Bloc(b, ref i, racine, 0) ? racine : null;
        }

        private static bool Bloc(byte[] b, ref int i, Noeud parent, int prof)
        {
            if (prof > Profondeur) return false;
            while (i < b.Length)
            {
                byte t = b[i++];
                if (t == 8) return true;
                string nom;
                if (!Chaine(b, ref i, out nom)) return false;
                var n = new Noeud { Nom = nom };
                switch (t)
                {
                    case 0: if (!Bloc(b, ref i, n, prof + 1)) return false; break;
                    case 1: { string v; if (!Chaine(b, ref i, out v)) return false; n.Texte = v; break; }
                    case 2: if (i + 4 > b.Length) return false; n.Nombre = BitConverter.ToInt32(b, i); i += 4; break;
                    case 3: if (i + 4 > b.Length) return false; n.Texte = BitConverter.ToSingle(b, i).ToString(CultureInfo.InvariantCulture); i += 4; break;
                    case 4: case 6: if (i + 4 > b.Length) return false; n.Nombre = BitConverter.ToInt32(b, i); i += 4; break;   // pointeur, couleur
                    case 7: if (i + 8 > b.Length) return false; n.Nombre = (long)BitConverter.ToUInt64(b, i); i += 8; break;
                    case 10: if (i + 8 > b.Length) return false; n.Nombre = BitConverter.ToInt64(b, i); i += 8; break;
                    default: return false;
                }
                parent.Enfants.Add(n);
            }
            return prof == 0;   // la racine peut finir sans marqueur ; un bloc ouvert, non
        }

        private static bool Chaine(byte[] b, ref int i, out string s)
        {
            s = null;
            int j = Array.IndexOf(b, (byte)0, i);
            if (j < 0) return false;
            s = Encoding.UTF8.GetString(b, i, j - i);
            i = j + 1;
            return true;
        }

        /// <summary>Texte traduit d'un bloc { english, french… } : français, sinon anglais, sinon le premier.</summary>
        public static string Traduit(Noeud n)
        {
            if (n == null) return "";
            if (n.Texte != null) return n.Texte;
            foreach (string l in new[] { "french", "english" })
            {
                Noeud x = n.Enfant(l);
                if (x != null && !string.IsNullOrEmpty(x.Texte)) return x.Texte;
            }
            foreach (Noeud x in n.Enfants) if (!string.IsNullOrEmpty(x.Texte) && x.Nom != "token") return x.Texte;
            return "";
        }

        private static bool EstSucces(Noeud stat)
        {
            Noeud t = stat.Enfant("type");
            if (t == null) return stat.Enfant("bits") != null;
            if (t.Nombre.HasValue) return t.Nombre.Value == 4;
            return string.Equals(t.Texte, "ACHIEVEMENTS", StringComparison.OrdinalIgnoreCase) || t.Texte == "4";
        }

        private static long Entier(Noeud n)
        {
            if (n == null) return 0;
            if (n.Nombre.HasValue) return n.Nombre.Value;
            long v;
            return long.TryParse(n.Texte, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        /// <summary>
        /// Succès du jeu, débloqués d'abord (du plus récent au plus ancien), puis les autres dans
        /// l'ordre du jeu. joueur peut être null (Steam n'a encore rien relevé : tout est à faire).
        /// null si le schéma est illisible ou si le jeu n'a pas de succès.
        /// </summary>
        public static Bilan Lit(byte[] schema, byte[] joueur)
        {
            Noeud s = LitKv(schema);
            if (s == null || s.Enfants.Count == 0) return null;
            Noeud jeu = s.Enfants[0];
            Noeud stats = jeu.Enfant("stats");
            if (stats == null) return null;
            Noeud cache = null;
            Noeud j = LitKv(joueur);
            if (j != null) cache = j.Enfant("cache") ?? (j.Enfants.Count > 0 ? j.Enfants[0] : null);

            var bilan = new Bilan();
            Noeud gn = jeu.Enfant("gamename");
            bilan.Jeu = gn != null && gn.Texte != null ? gn.Texte : "";
            int ordre = 0;
            foreach (Noeud stat in stats.Enfants)
            {
                Noeud bits = stat.Enfant("bits");
                if (bits == null || !EstSucces(stat)) continue;
                Noeud etat = cache != null ? cache.Enfant(stat.Nom) : null;
                long data = Entier(etat != null ? etat.Enfant("data") : null);
                Noeud dates = etat != null ? etat.Enfant("AchievementTimes") : null;
                foreach (Noeud bit in bits.Enfants)
                {
                    int k;
                    if (!int.TryParse(bit.Nom, NumberStyles.None, CultureInfo.InvariantCulture, out k) || k < 0 || k > 31) continue;
                    Noeud d = bit.Enfant("display");
                    var x = new Succes
                    {
                        Nom = Traduit(d != null ? d.Enfant("name") : null),
                        Description = Traduit(d != null ? d.Enfant("desc") : null),
                        Cache = d != null && Entier(d.Enfant("hidden")) != 0,
                        Obtenu = ((data >> k) & 1) == 1,
                        Ordre = ordre++,
                    };
                    if (x.Nom.Length == 0) { Noeud api = bit.Enfant("name"); x.Nom = api != null && api.Texte != null ? api.Texte : "Succès " + (x.Ordre + 1); }
                    if (x.Obtenu) x.DateUnix = Entier(dates != null ? dates.Enfant(bit.Nom) : null);
                    bilan.Liste.Add(x);
                }
            }
            if (bilan.Liste.Count == 0) return null;
            bilan.Total = bilan.Liste.Count;
            bilan.Obtenus = bilan.Liste.FindAll(x => x.Obtenu).Count;
            bilan.Liste.Sort((a, b) =>
            {
                if (a.Obtenu != b.Obtenu) return a.Obtenu ? -1 : 1;
                if (a.Obtenu && a.DateUnix != b.DateUnix) return b.DateUnix.CompareTo(a.DateUnix);
                return a.Ordre.CompareTo(b.Ordre);
            });
            return bilan;
        }

        /// <summary>Nom affiché : un succès caché non débloqué garde son secret, comme dans Steam.</summary>
        public static string NomAffiche(Succes s)
        {
            return s.Cache && !s.Obtenu ? "Succès caché" : s.Nom;
        }

        public static string DescriptionAffichee(Succes s)
        {
            return s.Cache && !s.Obtenu ? "Débloque-le pour le découvrir." : s.Description;
        }

        /// <summary>« Débloqué le 12/11/2026 », « Débloqué » sans date, « À débloquer ».</summary>
        public static string Etat(Succes s)
        {
            if (!s.Obtenu) return "À débloquer";
            if (s.DateUnix <= 0) return "Débloqué";
            return "Débloqué le " + DateTimeOffset.FromUnixTimeSeconds(s.DateUnix).LocalDateTime.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        /// <summary>« 10 / 57 (18 %) ».</summary>
        public static string Resume(Bilan b)
        {
            if (b == null || b.Total == 0) return "";
            return b.Obtenus + " / " + b.Total + " (" + (int)Math.Floor(100.0 * b.Obtenus / b.Total) + " %)";
        }
    }
}
