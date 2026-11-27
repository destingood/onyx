using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BTOptimizer
{
    /// <summary>
    /// JEUX DU COMPTE STEAM NON INSTALLÉS (logique PURE). Steam ne publie la liste des jeux achetés
    /// qu'avec une connexion ou une clé d'API ; ONYX lit à la place le fichier local du client,
    /// userdata\&lt;compte&gt;\config\localconfig.vdf, bloc Software › Valve › Steam › apps : chaque
    /// jeu lancé au moins une fois sur ce compte y a « LastPlayed » (date Unix) et « Playtime »
    /// (minutes). Limite : un jeu acheté mais jamais lancé n'y figure pas. Le type (jeu ou
    /// logiciel) et le nom viennent ensuite de la fiche publique du store (IStoreBrowseService).
    /// </summary>
    internal static partial class JeuxCompteSteam
    {
        /// <summary>Écart entre un SteamID64 et le numéro de compte (dossier userdata).</summary>
        private const long Base64 = 76561197960265728L;

        public sealed class Possede
        {
            public int AppId;
            public int Minutes;
            public long DernierUnix;
        }

        /// <summary>Numéro du dossier userdata pour un SteamID64, ou null s'il est invalide.</summary>
        public static string Compte(string steamId64)
        {
            long id;
            if (string.IsNullOrEmpty(steamId64) || !long.TryParse(steamId64.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) || id <= Base64)
                return null;
            return (id - Base64).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Jeux lancés au moins une fois sur ce compte, du plus récent au plus ancien.</summary>
        public static List<Possede> Lire(string localconfig)
        {
            var parId = new Dictionary<int, Possede>();
            if (string.IsNullOrEmpty(localconfig)) return new List<Possede>();
            var pile = new List<string>();
            string cle = null;
            int i = 0;
            string jeton; char genre;
            while (Jeton(localconfig, ref i, out jeton, out genre))
            {
                if (genre == '{') { pile.Add(cle ?? ""); cle = null; continue; }
                if (genre == '}') { if (pile.Count > 0) pile.RemoveAt(pile.Count - 1); cle = null; continue; }
                if (cle == null) { cle = jeton; continue; }
                // Paire « clé » « valeur » : on ne garde que celles d'un jeu du bloc apps.
                int id;
                if (DansApps(pile, out id))
                {
                    long v;
                    if (long.TryParse(jeton, NumberStyles.None, CultureInfo.InvariantCulture, out v))
                    {
                        Possede p;
                        if (!parId.TryGetValue(id, out p)) parId[id] = p = new Possede { AppId = id };
                        if (string.Equals(cle, "LastPlayed", StringComparison.OrdinalIgnoreCase)) p.DernierUnix = v;
                        else if (string.Equals(cle, "Playtime", StringComparison.OrdinalIgnoreCase)) p.Minutes = (int)Math.Min(int.MaxValue, v);
                    }
                }
                cle = null;
            }
            var res = new List<Possede>();
            foreach (Possede p in parId.Values) if (p.DernierUnix > 0 || p.Minutes > 0) res.Add(p);
            res.Sort((a, b) => b.DernierUnix != a.DernierUnix ? b.DernierUnix.CompareTo(a.DernierUnix) : a.AppId.CompareTo(b.AppId));
            return res;
        }

        /// <summary>La pile se termine par Software › Valve › Steam › apps › &lt;numéro&gt;.</summary>
        private static bool DansApps(List<string> pile, out int id)
        {
            id = 0;
            int n = pile.Count;
            return n >= 5
                && string.Equals(pile[n - 5], "Software", StringComparison.OrdinalIgnoreCase)
                && string.Equals(pile[n - 4], "Valve", StringComparison.OrdinalIgnoreCase)
                && string.Equals(pile[n - 3], "Steam", StringComparison.OrdinalIgnoreCase)
                && string.Equals(pile[n - 2], "apps", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(pile[n - 1], NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
        }

        /// <summary>Jeton VDF suivant : chaîne entre guillemets (genre '"'), « { » ou « } ». Commentaires « // » ignorés.</summary>
        private static bool Jeton(string s, ref int i, out string jeton, out char genre)
        {
            jeton = null; genre = '\0';
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                if (c == '{' || c == '}') { genre = c; i++; return true; }
                if (c == '"')
                {
                    var sb = new StringBuilder();
                    i++;
                    while (i < s.Length && s[i] != '"')
                    {
                        if (s[i] == '\\' && i + 1 < s.Length) { i++; sb.Append(s[i] == 'n' ? '\n' : s[i] == 't' ? '\t' : s[i]); }
                        else sb.Append(s[i]);
                        i++;
                    }
                    i++;
                    jeton = sb.ToString(); genre = '"';
                    return true;
                }
                // Jeton nu (rare) : jusqu'au prochain blanc ou accolade.
                int d = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '{' && s[i] != '}' && s[i] != '"') i++;
                jeton = s.Substring(d, i - d); genre = '"';
                return true;
            }
            return false;
        }

        /// <summary>Jeux du compte absents de ce PC (ordre de <see cref="Lire"/> conservé).</summary>
        public static List<Possede> NonInstalles(List<Possede> possedes, ICollection<int> installes)
        {
            var res = new List<Possede>();
            if (possedes == null) return res;
            foreach (Possede p in possedes) if (installes == null || !installes.Contains(p.AppId)) res.Add(p);
            return res;
        }

        /// <summary>
        /// Noms des JEUX dans une réponse IStoreBrowseService/GetItems : fiche trouvée (success 1),
        /// type 0 (jeu ; absent = jeu, comme dans <see cref="BibliothequeJeux.LogicielsSteam"/>), nom
        /// présent. Logiciels, vidéos, outils et fiches retirées du store sont écartés.
        /// </summary>
        public static Dictionary<int, string> Jeux(string json)
        {
            var res = new Dictionary<int, string>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json ?? ""))
                {
                    JsonElement r, items;
                    if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("response", out r)
                        || r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("store_items", out items) || items.ValueKind != JsonValueKind.Array) return res;
                    foreach (JsonElement it in items.EnumerateArray())
                    {
                        int id, ok, type;
                        if (!Entier(it, "success", out ok) || ok != 1 || !Entier(it, "appid", out id) || id <= 0) continue;
                        if (Entier(it, "type", out type) && type != 0) continue;
                        JsonElement n;
                        if (!it.TryGetProperty("name", out n) || n.ValueKind != JsonValueKind.String) continue;
                        string nom = (n.GetString() ?? "").Trim();
                        if (nom.Length == 0 || BibliothequeJeux.EstOutil(nom)) continue;   // « … Dedicated Server », « … SDK »
                        res[id] = nom;
                    }
                }
            }
            catch { }
            return res;
        }

        /// <summary>Ligne d'état d'une tuile : « Sur ton compte Steam · 12 h jouées ».</summary>
        public static string Statut(int minutes)
        {
            if (minutes <= 0) return "Sur ton compte Steam · pas installé";
            if (minutes < 60) return "Sur ton compte Steam · " + minutes.ToString(CultureInfo.InvariantCulture) + " min jouées";
            return "Sur ton compte Steam · " + (minutes / 60).ToString(CultureInfo.InvariantCulture) + " h jouées";
        }

        private static bool Entier(JsonElement e, string nom, out int v)
        {
            JsonElement x;
            v = 0;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nom, out x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out v);
        }
    }
}
