using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="ListeSouhaitsGog"/> — pseudo tapé ou adresse collée, extraction de
    /// « var gogData » (accolades dans les chaînes comprises), jeux gardés et DLC écartés.
    /// </summary>
    internal static class ListeSouhaitsGogTests
    {
        public static void Tout()
        {
            Banc.Titre("Bons plans : liste de souhaits GOG");

            Banc.Egal("pseudo tapé seul", "BoddyBoy_be", ListeSouhaitsGog.Pseudo("  BoddyBoy_be "));
            Banc.Egal("adresse de profil collée (langue comprise)", "aritrea", ListeSouhaitsGog.Pseudo("https://www.gog.com/fr/u/aritrea/wishlist"));
            Banc.Egal("adresse sans https", "joe-42", ListeSouhaitsGog.Pseudo("gog.com/u/joe-42"));
            Banc.Verifie("texte qui n'est pas un pseudo : refusé", true,
                ListeSouhaitsGog.Pseudo("mon pseudo") == null && ListeSouhaitsGog.Pseudo("a/b") == null && ListeSouhaitsGog.Pseudo("") == null);
            Banc.Egal("adresse de la page 1", "https://www.gog.com/u/joe-42/wishlist", ListeSouhaitsGog.Url("joe-42", 1));
            Banc.Egal("adresse de la page 3", "https://www.gog.com/u/joe-42/wishlist?page=3", ListeSouhaitsGog.Url("joe-42", 3));

            string html = "<html><script>\n        var gogData = {\"sortBy\":\"date_added\",\"page\":1,\"totalProducts\":3,\"totalPages\":2,\"productsPerPage\":100,"
                + "\"products\":[{\"id\":1,\"title\":\"Terra Nil\",\"isGame\":true,\"category\":\"Strat\\u00e9gie {x}\"},"
                + "{\"id\":2,\"title\":\"Cyberpunk 2077: Phantom Liberty\",\"isGame\":false},"
                + "{\"id\":3,\"title\":\"Disco \\\"Elysium\\\"\",\"isGame\":true},"
                + "{\"id\":4,\"title\":\"Terra Nil\",\"isGame\":true}]};\n        var autre = {};\n</script></html>";
            ListeSouhaitsGog.Page p = ListeSouhaitsGog.Lire(html);
            Banc.Verifie("page lue", true, p != null);
            if (p != null)
            {
                Banc.Egal("jeux gardés, DLC écarté, doublon retiré, guillemets échappés compris", "Terra Nil|Disco \"Elysium\"", string.Join("|", p.Titres));
                Banc.Verifie("numéro et nombre de pages", true, p.Numero == 1 && p.Pages == 2 && p.Total == 3);
            }
            Banc.Verifie("page sans gogData (profil privé, page d'erreur) : null", true, ListeSouhaitsGog.Lire("<html>404</html>") == null);
            Banc.Verifie("gogData tronqué : null, sans exception", true, ListeSouhaitsGog.Lire("var gogData = {\"products\":[{\"title\":\"A\"") == null);

            Banc.Egal("édition française retirée", "Cyberpunk 2077", ListeSouhaitsGog.Simplifie("Cyberpunk 2077 : Édition Ultime"));
            Banc.Egal("édition anglaise retirée", "Galactic Civilizations III", ListeSouhaitsGog.Simplifie("Galactic Civilizations III: Ultimate Edition"));
            Banc.Egal("marque et édition sans séparateur retirées", "Horizon Zero Dawn", ListeSouhaitsGog.Simplifie("Horizon Zero Dawn™ Complete Edition"));
            Banc.Egal("année entre parenthèses retirée", "Prey", ListeSouhaitsGog.Simplifie("Prey (2017)"));
            Banc.Verifie("titre sans édition : pas de second essai", true, ListeSouhaitsGog.Simplifie("Stardew Valley") == null);
            Banc.Verifie("titre réduit à moins de 3 caractères : pas de second essai", true, ListeSouhaitsGog.Simplifie("X: GOTY Edition") == null);
            ListeSouhaitsGog.Page dlc = ListeSouhaitsGog.Lire("var gogData = {\"page\":1,\"totalPages\":1,\"products\":[{\"title\":\"GreedFall - Adventurer’s Gear DLC\",\"isGame\":true},{\"title\":\"Hades\",\"isGame\":true}]}");
            Banc.Egal("DLC marqué « isGame » par GOG : écarté", "Hades", dlc == null ? "null" : string.Join("|", dlc.Titres));

            Banc.Egal("compte rendu avec titres absents de Steam",
                "GOG : 5 titres lus, 1 trouvé sur Steam. 1 jeu ajouté, 0 déjà suivi. Absents de Steam, non suivis : A, B, C (+1).",
                ListeSouhaitsGog.Compte(5, new List<string> { "A", "B", "C", "D" }, "1 jeu ajouté, 0 déjà suivi.", false));
            Banc.Egal("compte rendu d'une liste longue",
                "GOG : 2 titres lus, 2 trouvés sur Steam. x Liste longue : seules les premières pages ont pu être lues.",
                ListeSouhaitsGog.Compte(2, new List<string>(), "x", true));
        }
    }
}
