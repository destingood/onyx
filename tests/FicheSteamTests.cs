namespace BTOptimizer.Tests
{
    /// <summary>Fiche Steam d'un jeu : genre tiré des étiquettes, éditeur, date, avis.</summary>
    internal static class FicheSteamTests
    {
        // Extrait d'une vraie réponse GetItems (28/11/2026), réduit à ce qui est lu.
        private const string Reponse = @"{""response"":{""store_items"":[
 {""success"":1,""appid"":1091500,""type"":0,""name"":""Cyberpunk 2077"",
  ""tags"":[{""tagid"":6650,""weight"":731},{""tagid"":4115,""weight"":872},{""tagid"":122,""weight"":670},{""tagid"":1695,""weight"":827}],
  ""basic_info"":{""publishers"":[{""name"":""CD PROJEKT RED""}],""developers"":[{""name"":""CD PROJEKT RED""}]},
  ""release"":{""steam_release_date"":1607558400,""original_release_date"":0},
  ""reviews"":{""summary_filtered"":{""review_count"":911882,""percent_positive"":87,""review_score"":8,""review_score_label"":""très positives""}}},
 {""success"":1,""appid"":730,""tags"":[{""tagid"":1663,""weight"":1161},{""tagid"":19,""weight"":603}],
  ""basic_info"":{""publishers"":[{""name"":""Valve""}],""developers"":[{""name"":""Valve""},{""name"":""Hidden Path""}]},
  ""release"":{""steam_release_date"":1345568400}},
 {""success"":2,""appid"":5},
 {""success"":1,""appid"":42}
]}}";

        public static void Tout()
        {
            Banc.Titre("Fiche Steam d'un jeu");
            var l = FicheSteam.Lit(Reponse);
            Banc.Egal("fiches réussies seulement", "1091500,730,42", string.Join(",", l.ConvertAll(f => f.AppId.ToString())));
            Banc.Egal("genre : l'étiquette « Nudité » (6650) et « Cyberpunk » sont sautées", "RPG", l[0].Genre);
            Banc.Egal("genre : FPS passe devant Action (poids)", "FPS", l[1].Genre);
            Banc.Verifie("fiche vide : rien d'inventé", true, l[2].Genre == null && l[2].Editeur == null && l[2].Sortie == 0 && FicheSteam.TexteAvis(l[2]) == null);
            Banc.Egal("lignes de Cyberpunk", "Genre : RPG|Développé et édité par CD PROJEKT RED|Sortie : 10 décembre 2020|Avis Steam : 87 % positives sur 911 882 avis (très positives)",
                string.Join("|", FicheSteam.Lignes(l[0])));
            Banc.Egal("lignes de CS2 : développeurs différents de l'éditeur, sans avis", "Genre : FPS|Développeur : Valve, Hidden Path|Éditeur : Valve|Sortie : 21 août 2012",
                string.Join("|", FicheSteam.Lignes(l[1])));
            Banc.Egal("premier du mois", "1er mars 2024", FicheSteam.TexteSortie(1709251200));
            Banc.Verifie("réponse illisible : aucune fiche", true, FicheSteam.Lit("<html>").Count == 0 && FicheSteam.Lit(null).Count == 0);
            Banc.Verifie("adresse : 50 AppID au plus, avis et sortie demandés", true,
                FicheSteam.Url(new[] { 0, -3 }) == null && System.Uri.UnescapeDataString(FicheSteam.Url(new[] { 730 })).Contains("\"include_reviews\":true"));
        }
    }
}
