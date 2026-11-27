using System.Collections.Generic;
using BTOptimizer;

namespace BTOptimizer.Tests
{
    /// <summary>« Populaire en ce moment » : classement Steam par joueurs connectés.</summary>
    internal static class PopulairesTests
    {
        // Extrait d'une vraie réponse (28/11/2026), réduit ; rangs 3 et 2 inversés, un doublon, une ligne sans appid.
        private const string Reponse = "{\"response\":{\"last_update\":1795822330,\"ranks\":["
            + "{\"rank\":1,\"appid\":730,\"concurrent_in_game\":693285,\"peak_in_game\":1206781},"
            + "{\"rank\":3,\"appid\":4262460,\"concurrent_in_game\":367262,\"peak_in_game\":417917},"
            + "{\"rank\":2,\"appid\":570,\"concurrent_in_game\":\"528939\",\"peak_in_game\":804636},"
            + "{\"rank\":4,\"appid\":570,\"concurrent_in_game\":1},"
            + "{\"rank\":5,\"concurrent_in_game\":9}]}}";

        public static void Tout()
        {
            Banc.Titre("Populaire en ce moment");
            List<Populaires.Rang> l = Populaires.Lit(Reponse, 10);
            Banc.Egal("ordre du rang, doublon et ligne sans jeu écartés", "730,570,4262460", string.Join(",", l.ConvertAll(r => r.AppId.ToString())));
            Banc.Verifie("joueurs et pic lus (nombre ou texte)", true, l[0].Joueurs == 693285 && l[0].Pic == 1206781 && l[1].Joueurs == 528939);
            Banc.Egal("plafond respecté", "2", Populaires.Lit(Reponse, 2).Count.ToString());
            Banc.Verifie("réponse vide, illisible ou sans classement : vide", true,
                Populaires.Lit("", 10).Count == 0 && Populaires.Lit("{pas du json", 10).Count == 0 && Populaires.Lit("{\"response\":{}}", 10).Count == 0 && Populaires.Lit("[]", 10).Count == 0);
            Banc.Egal("texte : milliers séparés", "693 285 joueurs en ce moment", Populaires.TexteJoueurs(693285));
            Banc.Egal("texte : singulier", "1 joueur en ce moment", Populaires.TexteJoueurs(1));
            Banc.Egal("texte : inconnu", "", Populaires.TexteJoueurs(0));
            Banc.Verifie("adresse publique, sans clé", true, Populaires.Url.StartsWith("https://api.steampowered.com/") && !Populaires.Url.Contains("key="));
        }
    }
}
