using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="EtiquettesJeux"/> — saisie libre nettoyée, fichier relu à l'identique,
    /// même jeu reconnu quel que soit le magasin, liste du menu sans doublon de casse.
    /// </summary>
    internal static class EtiquettesJeuxTests
    {
        public static void Tout()
        {
            Banc.Titre("Bibliothèque : étiquettes des jeux");

            Banc.Egal("virgules, points-virgules, espaces et « # » nettoyés, doublon de casse retiré",
                "solo|coop|à finir", string.Join("|", EtiquettesJeux.Analyse("  solo ,#coop;  à   finir , SOLO,, ")));
            Banc.Egal("saisie vide : aucune étiquette", "", string.Join("|", EtiquettesJeux.Analyse("  , ; ")));
            Banc.Verifie("8 étiquettes au plus par jeu", true, EtiquettesJeux.Analyse("a,b,c,d,e,f,g,h,i,j").Count == EtiquettesJeux.MaxParJeu);
            Banc.Verifie("une étiquette trop longue est coupée à 24 caractères", true,
                EtiquettesJeux.Normalise(new string('x', 40)).Length == EtiquettesJeux.Longueur);
            Banc.Egal("la barre verticale (séparateur du fichier) ne survit pas dans une étiquette", "a b", EtiquettesJeux.Normalise("a|b"));

            var d = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            Banc.Verifie("pose des étiquettes", true, EtiquettesJeux.Pose(d, "Counter-Strike 2", "fps, compétitif"));
            EtiquettesJeux.Pose(d, "Hades II", "Roguelike, solo");
            Banc.Egal("même jeu sous un autre nom de magasin : mêmes étiquettes", "fps|compétitif",
                string.Join("|", EtiquettesJeux.De(d, "COUNTER STRIKE 2")));
            Banc.Egal("jeu sans étiquette : liste vide", "", string.Join("|", EtiquettesJeux.De(d, "Inconnu")));

            string texte = EtiquettesJeux.Ecrire(d);
            Banc.Egal("relu puis réécrit : fichier identique", texte, EtiquettesJeux.Ecrire(EtiquettesJeux.Lire(texte)));
            Banc.Egal("ligne malformée ou en double ignorée à la lecture", texte,
                EtiquettesJeux.Ecrire(EtiquettesJeux.Lire("sans-tabulation\n" + texte + texte)));

            EtiquettesJeux.Pose(d, "Valorant", "FPS");
            Banc.Egal("menu : chaque étiquette une fois, casse de la première, ordre alphabétique",
                "compétitif|fps|Roguelike|solo", string.Join("|", EtiquettesJeux.Toutes(d)));
            Banc.Verifie("filtre insensible à la casse", true, EtiquettesJeux.Porte(EtiquettesJeux.De(d, "Valorant"), "fps"));
            Banc.Verifie("recherche : texte contenu dans une étiquette", true, EtiquettesJeux.Contient(EtiquettesJeux.De(d, "Hades II"), "rogue"));
            Banc.Verifie("recherche : rien de contenu", false, EtiquettesJeux.Contient(EtiquettesJeux.De(d, "Hades II"), "coop"));

            EtiquettesJeux.Pose(d, "Valorant", " ");
            Banc.Verifie("saisie vide : le jeu perd ses étiquettes", false, d.ContainsKey(BibliothequeJeux.Cle("Valorant")));

            var plein = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = 0; i < EtiquettesJeux.MaxJeux; i++) plein["k" + i] = new List<string> { "x" };
            Banc.Verifie("liste pleine : nouveau jeu refusé", false, EtiquettesJeux.Pose(plein, "Nouveau jeu", "x"));
            string k1 = BibliothequeJeux.Cle("k1");
            plein.Remove("k1"); plein[k1] = new List<string> { "x" };
            Banc.Verifie("liste pleine : jeu déjà étiqueté modifiable", true, EtiquettesJeux.Pose(plein, "k1", "y"));
        }
    }
}
