namespace BTOptimizer.Tests
{
    /// <summary>
    /// Compréhension des messages du Copilote : normalisation (accents, apostrophes, lettres
    /// répétées), langage texto et tolérance aux fautes. Les cas « régression » figent le
    /// comportement d'avant l'extraction ; les autres décrivent ce qu'il comprend en plus.
    /// </summary>
    internal static class CopiloteTexteTests
    {
        private static string P(string s) { return CopiloteTexte.Prepare(s); }
        private static bool H(string message, params string[] cles) { return CopiloteTexte.Has(P(message), cles); }

        private static void Egal(string quoi, string attendu, string obtenu)
        {
            Banc.Verifie(attendu == obtenu ? quoi : quoi + " [obtenu : « " + obtenu + " »]", true, attendu == obtenu);
        }

        public static void Tout()
        {
            // ------------------------------------------------ régression : déjà compris avant
            Banc.Verifie("faute de frappe reconnue (« ecrqn » = écran)", true, H("mon ecrqn clignote", "ecran"));
            Banc.Verifie("mot court jamais trouvé dans un autre mot (« blague » ne contient pas « lag »)", false, H("raconte une blague", "lag"));
            Banc.Verifie("phrase en ordre libre (« disque plein »)", true, H("mon disque est plein", "plein disque"));
            Banc.Verifie("nombre collé à son unité (« 60hz »)", true, H("mon ecran est en 60hz", "hz"));
            Egal("texto développé", "salut j'ai un probleme", P("slt g un pb"));
            Egal("accents à plat, minuscules", "ecran bleu", P("Écran BLEU"));
            Banc.Verifie("oui court", true, CopiloteTexte.IsYes(P("Vas-y !")));
            Banc.Verifie("non court", true, CopiloteTexte.IsNo(P("non merci")));
            Banc.Verifie("une vraie phrase n'est pas un oui", false, CopiloteTexte.IsYes(P("oui mais mon pc rame")));

            // ------------------------------------------------ nouveau : écriture de téléphone et de Windows
            Egal("apostrophe typographique (’) comme l'apostrophe droite", "l'ecran scintille", P("L’écran scintille"));
            Banc.Verifie("« l’écran » trouve « ecran »", true, H("l’écran scintille", "ecran"));
            Egal("ligatures (œ, æ) à plat", "coeur ex aequo", P("cœur ex æquo"));
            Egal("tous les accents à plat, pas seulement le français", "pinata noel", P("piñata Noël"));
            Egal("espaces multiples et insécables réduits", "pc lent", P("pc    lent"));

            // ------------------------------------------------ nouveau : texto collé à la ponctuation
            Egal("abréviation suivie d'une ponctuation développée", "salut, j'ai un probleme?", P("slt, g un pb?"));
            Egal("mot à trait d'union gardé entier (« wi-fi » n'est pas « oui »)", "mon wifi coupe", P("mon wi-fi coupe"));

            // ------------------------------------------------ nouveau : lettres répétées pour insister
            Banc.Verifie("lettres répétées ramenées (« laaaag »)", true, H("mon pc laaaag", "lag"));
            Egal("lettres répétées ramenées (« rammmme »)", "ca rame", P("ça rammmme"));
            Egal("double lettre normale gardée (« connexion », « nulle »)", "connexion nulle", P("connexion nulle"));
            Banc.Verifie("oui insistant", true, CopiloteTexte.IsYes(P("ouiiii !")));

            // ------------------------------------------------ nouveau : vocabulaire de panne courant
            Egal("BSOD = écran bleu", "ecran bleu au demarrage", P("BSOD au démarrage"));
            Egal("« rezo » = réseau", "le reseau lag", P("le rezo lag"));
            Egal("« connection » (anglicisme) = connexion", "ma connexion saute", P("ma connection saute"));
            Egal("« y'a » = il y a", "il y a plus de son", P("y'a plus de son"));
        }
    }
}
