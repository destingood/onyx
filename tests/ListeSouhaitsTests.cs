using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="ListeSouhaits"/> — compte Steam lu dans loginusers.vdf, réponse de
    /// IWishlistService/GetWishlist/v1 (forme vérifiée le 26/11/2026), import dans les suivis.
    /// </summary>
    internal static class ListeSouhaitsTests
    {
        private const string DeuxComptes =
            "\"users\"\n{\n" +
            "\t\"76561198000000001\"\n\t{\n\t\t\"AccountName\"\t\t\"a\"\n\t\t\"MostRecent\"\t\t\"0\"\n\t}\n" +
            "\t\"76561198000000002\"\n\t{\n\t\t\"AccountName\"\t\t\"b\"\n\t\t\"MostRecent\"\t\t\"1\"\n\t}\n}\n";

        private const string Reponse =
            "{\"response\":{\"items\":[" +
            "{\"appid\":730,\"priority\":2,\"date_added\":1700000000}," +
            "{\"appid\":570,\"priority\":0,\"date_added\":1700000001}," +
            "{\"appid\":440,\"priority\":1,\"date_added\":1700000002}]}}";

        public static void Tout()
        {
            Banc.Titre("Liste de souhaits Steam");

            Banc.Egal("vdf : le compte MostRecent passe avant le premier", "76561198000000002", ListeSouhaits.SteamId64(DeuxComptes));
            Banc.Egal("vdf : sans MostRecent, le premier compte",
                "76561198000000001", ListeSouhaits.SteamId64(DeuxComptes.Replace("\"MostRecent\"\t\t\"1\"", "\"MostRecent\"\t\t\"0\"")));
            Banc.Verifie("vdf : vide ou absent → null", true, ListeSouhaits.SteamId64("") == null && ListeSouhaits.SteamId64(null) == null);
            Banc.Egal("url", "https://api.steampowered.com/IWishlistService/GetWishlist/v1/?steamid=76561198000000002",
                ListeSouhaits.Url("76561198000000002"));
            Banc.Verifie("url : identifiant qui n'est pas un SteamID64 → null", true, ListeSouhaits.Url("12&x=1") == null);

            Banc.Egal("appids : priorité croissante, 0 en dernier", "440,730,570", string.Join(",", ListeSouhaits.AppIds(Reponse)));
            Banc.Verifie("appids : profil privé ou liste vide → liste vide", true, ListeSouhaits.AppIds("{\"response\":{}}").Count == 0);
            Banc.Verifie("appids : json abîmé → liste vide, pas d'exception", true,
                ListeSouhaits.AppIds("{\"response\":{\"items\":[{").Count == 0 && ListeSouhaits.AppIds(null).Count == 0);

            Banc.Egal("compte", "12 jeux ajoutés, 3 déjà suivis, 20 laissés de côté (50 jeux suivis au plus).", ListeSouhaits.Compte(12, 3, 20));
            Banc.Egal("compte : second import", "0 jeu ajouté, 3 déjà suivis.", ListeSouhaits.Compte(0, 3, 0));
            Banc.Egal("compte : singulier", "1 jeu ajouté, 1 déjà suivi.", ListeSouhaits.Compte(1, 1, 0));

            // Plus de 50 jeux : les premiers par priorité, le reste compté dehors.
            var suivis = new List<JeuSuivi> { new JeuSuivi { AppId = 730, Nom = "Counter-Strike 2" } };
            var ids = new List<int>();
            for (int i = 1; i <= 60; i++) ids.Add(i);
            ids.Insert(5, 730);
            var noms = new Dictionary<int, string> { { 1, "Premier" } };
            int ajoutes, deja, laisses;
            ListeSouhaits.Importe(suivis, ids, noms, out ajoutes, out deja, out laisses);
            Banc.Verifie("import : 49 ajoutés, 1 déjà suivi, 11 laissés", true, ajoutes == 49 && deja == 1 && laisses == 11);
            Banc.Verifie("import : 50 suivis au plus", true, suivis.Count == Suivi.Max);
            Banc.Verifie("import : ordre de priorité gardé", true, suivis[1].AppId == 1 && suivis[49].AppId == 49);
            Banc.Egal("import : nom de la fiche", "Premier", suivis[1].Nom);
            Banc.Egal("import : sans fiche, nom provisoire", "Jeu 2", suivis[2].Nom);
            Banc.Verifie("import : remise à amorcer (pas d'alerte pour une promo déjà en cours)", true, suivis[1].RemiseNotifiee == Suivi.AAmorcer);
            ListeSouhaits.Importe(suivis, ids, noms, out ajoutes, out deja, out laisses);
            Banc.Verifie("import : second passage n'ajoute rien", true, ajoutes == 0 && deja == 50 && laisses == 11);
        }
    }
}
