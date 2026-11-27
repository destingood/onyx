using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="ReglagesCod"/> — préréglages Black Ops 6 : seule la valeur des lignes visées
    /// bouge, uniquement vers un cran listé par le jeu ; « Rétablir » rend le fichier exact et laisse
    /// ce que le joueur a changé lui-même.
    /// </summary>
    internal static class ReglagesCodTests
    {
        private const string Texte =
            "\r\n//\r\n// Graphics\r\n\r\n" +
            "// Shadow quality\r\nShadowQuality@0;3981;58629 = High // one of Very_Low, Low, Medium, High, Very_High\r\n\r\n" +
            "SSRQuality@0;42742;43096 = High // one of Off, Low, High\r\n\r\n" +
            "DepthOfField@0;1;2 = true\r\n\r\n" +
            "ModelQuality@0;1;2 = High Quality // one of Low Quality, Medium Quality, High Quality\r\n\r\n" +
            "TextureFilter@0;1;2 = aniso 16x // one of aniso 2x, aniso 4x, aniso 8x, aniso 16x\r\n\r\n" +
            "Tessellation@0;1;2 = 2_All // one of 0_Off, 1_Near, 2_All\r\n" +
            "Tessellation@0;9;9 = 2_All // one of 0_Off, 1_Near, 2_All\r\n\r\n" +
            "VRS@0;1;2 = false\r\n" +
            "CorpseLimit@0;1;2 = 28 // 0 to 28\r\n" +
            "VSync@0;55075;7764 = disabled // one of disabled, 100%, 50%, 33%, 25%\r\n" +
            "CapFps@0;1;2 = true\r\n" +
            "MaxFpsInGame@0;1;2 = 250 // 30 to 300\r\n" +
            "ConfigCloudStorageEnabled@0;1;2 = true\r\n" +
            "SoundOutputDevice@0;1;2 = \r\n";

        public static void Tout()
        {
            Banc.Titre("Préréglages Black Ops 6");

            List<string> absentes;
            List<ReglagesCod.Changement> c = ReglagesCod.Prevoit(Texte, ReglagesCod.Mode.Competition, out absentes);
            Banc.Egal("compétition : réglages présents qui changent, dans l'ordre de la table",
                "ShadowQuality High>Very_Low|SSRQuality High>Off|ModelQuality High Quality>Medium Quality|TextureFilter aniso 16x>aniso 8x|DepthOfField true>false|VRS false>true",
                string.Join("|", c.ConvertAll(x => x.Cle + " " + x.Avant + ">" + x.Apres)));
            Banc.Verifie("réglage en double (Tessellation) : non touché, signalé", true, absentes.Contains("Tessellation") && !c.Exists(x => x.Cle == "Tessellation"));
            Banc.Verifie("réglage absent du fichier signalé", true, absentes.Contains("WaterCausticsMode"));

            string apres = ReglagesCod.Applique(Texte, c);
            string attendu = Texte
                .Replace("ShadowQuality@0;3981;58629 = High //", "ShadowQuality@0;3981;58629 = Very_Low //")
                .Replace("SSRQuality@0;42742;43096 = High //", "SSRQuality@0;42742;43096 = Off //")
                .Replace("ModelQuality@0;1;2 = High Quality //", "ModelQuality@0;1;2 = Medium Quality //")
                .Replace("TextureFilter@0;1;2 = aniso 16x //", "TextureFilter@0;1;2 = aniso 8x //")
                .Replace("DepthOfField@0;1;2 = true\r\n", "DepthOfField@0;1;2 = false\r\n")
                .Replace("VRS@0;1;2 = false\r\n", "VRS@0;1;2 = true\r\n");
            Banc.Egal("seules les valeurs changent, commentaires et fins de ligne intacts", attendu, apres);
            Banc.Egal("rétablir rend le fichier exact", Texte, ReglagesCod.Retablit(apres, c));

            string joueur = apres.Replace("SSRQuality@0;42742;43096 = Off //", "SSRQuality@0;42742;43096 = Low //");
            string retabli = ReglagesCod.Retablit(joueur, c);
            Banc.Verifie("rétablir garde ce que le joueur a changé ensuite dans le menu", true,
                retabli.Contains("SSRQuality@0;42742;43096 = Low //") && retabli.Contains("ShadowQuality@0;3981;58629 = High //"));

            List<ReglagesCod.Changement> qualite = ReglagesCod.Prevoit(apres, ReglagesCod.Mode.Qualite, out absentes);
            string deuxFois = ReglagesCod.Applique(apres, qualite);
            Banc.Egal("deux préréglages puis rétablir : fichier d'origine", Texte, ReglagesCod.Retablit(deuxFois, ReglagesCod.Fusionne(c, qualite)));

            string sansVeryLow = Texte.Replace("one of Very_Low, Low,", "one of Low,");
            ReglagesCod.Prevoit(sansVeryLow, ReglagesCod.Mode.Competition, out absentes);
            Banc.Verifie("cran visé absent de la liste du jeu : non touché, signalé", true, absentes.Contains("ShadowQuality"));
            string horsListe = Texte.Replace("ShadowQuality@0;3981;58629 = High //", "ShadowQuality@0;3981;58629 = Epic //");
            ReglagesCod.Prevoit(horsListe, ReglagesCod.Mode.Competition, out absentes);
            Banc.Verifie("valeur actuelle hors liste : non touchée", true, absentes.Contains("ShadowQuality"));
            Banc.Verifie("écriture d'un cran que le menu ne propose pas : refusée", true,
                ReglagesCod.Applique(Texte, new List<ReglagesCod.Changement> { new ReglagesCod.Changement { Cle = "SSRQuality", Avant = "High", Apres = "Medium" } }) == Texte);
            Banc.Verifie("plage de nombres jamais écrite", true,
                ReglagesCod.Applique(Texte, new List<ReglagesCod.Changement> { new ReglagesCod.Changement { Cle = "CorpseLimit", Avant = "28", Apres = "0" } }) == Texte);
            Banc.Verifie("fichier vide ou absent : rien de prévu", true, ReglagesCod.Prevoit("", ReglagesCod.Mode.Competition, out absentes).Count == 0 && ReglagesCod.Prevoit(null, ReglagesCod.Mode.Qualite, out absentes).Count == 0);

            bool vsync;
            Banc.Verifie("limite d'images lue (250), synchro coupée", true, ReglagesCod.LimiteImages(Texte, out vsync) == 250 && !vsync);
            Banc.Verifie("limite décochée : 0 ; synchro 100 % : activée", true,
                ReglagesCod.LimiteImages(Texte.Replace("CapFps@0;1;2 = true", "CapFps@0;1;2 = false").Replace("= disabled //", "= 100% //"), out vsync) == 0 && vsync);
            Banc.Verifie("synchro cloud détectée", true, ReglagesCod.Nuage(Texte) && !ReglagesCod.Nuage(Texte.Replace("ConfigCloudStorageEnabled@0;1;2 = true", "ConfigCloudStorageEnabled@0;1;2 = false")));

            Banc.Egal("ligne lisible", "Qualité des ombres : Élevé → Très bas", ReglagesCod.Ligne(c[0]));
            Banc.Egal("ligne du filtrage", "Filtrage des textures : 16x → 8x", ReglagesCod.Ligne(c[3]));
            List<ReglagesCod.Changement> lu = ReglagesCod.Lire(ReglagesCod.Ecrire(c));
            Banc.Verifie("état relu à l'identique (valeurs avec espaces comprises)", true, lu.Count == c.Count && lu[2].Avant == "High Quality" && lu[2].Apres == "Medium Quality");
            Banc.Verifie("fichier d'état abîmé : lignes ignorées", true, ReglagesCod.Lire("x\ty\n\n").Count == 0 && ReglagesCod.Lire(null).Count == 0);

            Banc.Verifie("Black Ops 6 reconnu (id Steam, nom, lanceur « BO6 »), pas Black Ops III", true,
                ReglagesCod.EstLeJeu(4384550, "") && ReglagesCod.EstLeJeu(0, "Call of Duty®: Black Ops 6") && ReglagesCod.EstLeJeu(1938090, "Call of Duty (MW / Warzone / BO6)")
                && !ReglagesCod.EstLeJeu(311210, "Call of Duty: Black Ops III"));

            // Le vrai fichier de ce PC, s'il existe : lu seulement, jamais écrit.
            string vrai = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Call of Duty BO6", "players", "s.1.0.cod24.txt0");
            if (File.Exists(vrai))
            {
                string texte = File.ReadAllText(vrai);
                foreach (ReglagesCod.Mode m in new[] { ReglagesCod.Mode.Competition, ReglagesCod.Mode.Qualite })
                {
                    List<ReglagesCod.Changement> v = ReglagesCod.Prevoit(texte, m, out absentes);
                    string ecrit = ReglagesCod.Applique(texte, v);
                    Banc.Verifie("vrai fichier, " + ReglagesCod.NomMode(m) + " : tous les réglages reconnus (" + v.Count + " changements)", true, absentes.Count == 0);
                    Banc.Verifie("vrai fichier, " + ReglagesCod.NomMode(m) + " : même nombre de lignes, rétablir le rend exact", true,
                        ecrit.Split('\n').Length == texte.Split('\n').Length && ReglagesCod.Retablit(ecrit, v) == texte && ReglagesCod.Prevoit(ecrit, m, out absentes).Count == 0);
                }
            }
        }
    }
}
