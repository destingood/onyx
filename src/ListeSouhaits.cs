using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// LISTE DE SOUHAITS STEAM (logique PURE) : compte Steam du PC (loginusers.vdf), réponse de
    /// IWishlistService/GetWishlist/v1 — sans clé, forme vérifiée le 26/11/2026 :
    /// {"response":{"items":[{"appid":…,"priority":…,"date_added":…}]}} ; profil privé ou liste
    /// vide → {"response":{}}. L'ancienne page wishlistdata est fermée depuis novembre 2024.
    /// </summary>
    internal static class ListeSouhaits
    {
        private static readonly Regex Compte64 = new Regex("\"(\\d{17})\"\\s*\\{([^{}]*)\\}", RegexOptions.CultureInvariant);
        private static readonly Regex Recent = new Regex("\"MostRecent\"\\s*\"1\"", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex Id64 = new Regex("^\\d{17}$", RegexOptions.CultureInvariant);

        /// <summary>SteamID64 du compte marqué "MostRecent" "1", sinon du premier compte, sinon null.</summary>
        public static string SteamId64(string loginusersVdf)
        {
            if (string.IsNullOrEmpty(loginusersVdf)) return null;
            string premier = null;
            foreach (Match m in Compte64.Matches(loginusersVdf))
            {
                if (premier == null) premier = m.Groups[1].Value;
                if (Recent.IsMatch(m.Groups[2].Value)) return m.Groups[1].Value;
            }
            return premier;
        }

        /// <summary>Adresse de la liste ; null si l'identifiant n'est pas un SteamID64.</summary>
        public static string Url(string steamId64)
        {
            if (steamId64 == null || !Id64.IsMatch(steamId64)) return null;
            return "https://api.steampowered.com/IWishlistService/GetWishlist/v1/?steamid=" + steamId64;
        }

        /// <summary>AppID par "priority" croissante (0 = non classé, en dernier) ; réponse vide ou abîmée → liste vide.</summary>
        public static List<int> AppIds(string json)
        {
            var lus = new List<KeyValuePair<int, int>>();   // appid, priorité
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json ?? ""))
                {
                    JsonElement r, items;
                    if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("response", out r)
                        || r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("items", out items) || items.ValueKind != JsonValueKind.Array)
                        return new List<int>();
                    var vus = new HashSet<int>();
                    foreach (JsonElement it in items.EnumerateArray())
                    {
                        int id, prio;
                        if (!Entier(it, "appid", out id) || id <= 0 || !vus.Add(id)) continue;
                        if (!Entier(it, "priority", out prio) || prio < 0) prio = 0;
                        lus.Add(new KeyValuePair<int, int>(id, prio));
                    }
                }
            }
            catch { return new List<int>(); }
            // OrderBy est stable : à priorité égale, l'ordre de Steam est gardé.
            return lus.OrderBy(kv => kv.Value == 0 ? int.MaxValue : kv.Value).Select(kv => kv.Key).ToList();
        }

        /// <summary>Ajoute les jeux dans l'ordre jusqu'à <see cref="Suivi.Max"/> ; remise « à amorcer ».
        /// Nom : celui de la fiche Steam s'il est connu, sinon « Jeu &lt;appid&gt; » jusqu'au prochain contrôle.</summary>
        public static void Importe(List<JeuSuivi> suivis, List<int> appIds, Dictionary<int, string> noms,
            out int ajoutes, out int dejaSuivis, out int laisses)
        {
            ajoutes = dejaSuivis = laisses = 0;
            if (suivis == null || appIds == null) return;
            foreach (int id in appIds)
            {
                if (id <= 0) continue;
                if (suivis.Exists(j => j.AppId == id)) { dejaSuivis++; continue; }
                string nom;
                if (noms == null || !noms.TryGetValue(id, out nom) || string.IsNullOrWhiteSpace(nom))
                    nom = "Jeu " + id.ToString(CultureInfo.InvariantCulture);
                if (Suivi.AjouteInstalle(suivis, id, nom)) ajoutes++;
                else laisses++;
            }
        }

        /// <summary>« 12 jeux ajoutés, 3 déjà suivis, 20 laissés de côté (50 jeux suivis au plus). »</summary>
        public static string Compte(int ajoutes, int dejaSuivis, int laisses)
        {
            string t = ajoutes.ToString(CultureInfo.InvariantCulture) + (ajoutes > 1 ? " jeux ajoutés, " : " jeu ajouté, ")
                + dejaSuivis.ToString(CultureInfo.InvariantCulture) + (dejaSuivis > 1 ? " déjà suivis" : " déjà suivi");
            if (laisses > 0)
                t += ", " + laisses.ToString(CultureInfo.InvariantCulture) + (laisses > 1 ? " laissés" : " laissé")
                    + " de côté (" + Suivi.Max.ToString(CultureInfo.InvariantCulture) + " jeux suivis au plus)";
            return t + ".";
        }

        private static bool Entier(JsonElement e, string nom, out int v)
        {
            JsonElement x;
            v = 0;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nom, out x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out v);
        }
    }
}
