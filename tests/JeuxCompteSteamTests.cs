using System;
using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="JeuxCompteSteam"/> — lecture du bloc apps de localconfig.vdf (et de lui
    /// seul), exclusion des jeux installés, tri des fiches du store (jeux gardés, logiciels écartés).
    /// </summary>
    internal static class JeuxCompteSteamTests
    {
        private const string Vdf =
            "\"UserLocalConfigStore\"\n{\n" +
            "\t\"apps\"\n\t{\n\t\t\"999\"\n\t\t{\n\t\t\t\"LastPlayed\"\t\t\"1700000000\"\n\t\t}\n\t}\n" +      // hors Software › Valve › Steam : ignoré
            "\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n" +
            "\t\t\t\t\"apps\"\n\t\t\t\t{\n" +
            "\t\t\t\t\t\"730\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LastPlayed\"\t\t\"1764000000\"\n\t\t\t\t\t\t\"Playtime\"\t\t\"5400\"\n" +
            "\t\t\t\t\t\t\"cloud\"\n\t\t\t\t\t\t{\n\t\t\t\t\t\t\t\"last_sync_state\"\t\t\"synchronized\"\n\t\t\t\t\t\t}\n\t\t\t\t\t}\n" +
            "\t\t\t\t\t\"1172470\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LastPlayed\"\t\t\"1764100000\"\n\t\t\t\t\t\t\"Playtime\"\t\t\"42\"\n\t\t\t\t\t}\n" +
            "\t\t\t\t\t\"760\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"cloud\"\n\t\t\t\t\t\t{\n\t\t\t\t\t\t\t\"quota\"\t\t\"1\"\n\t\t\t\t\t\t}\n\t\t\t\t\t}\n" +   // pas de partie : ignoré
            "\t\t\t\t\t\"440\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"Playtime\"\t\t\"90\"\n\t\t\t\t\t}\n" +
            "\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n";

        public static void Tout()
        {
            Banc.Titre("Bibliothèque : jeux du compte Steam non installés");

            Banc.Egal("numéro du dossier userdata depuis le SteamID64", "22202", JeuxCompteSteam.Compte("76561197960287930"));
            Banc.Verifie("SteamID64 invalide : aucun dossier", true, JeuxCompteSteam.Compte("abc") == null && JeuxCompteSteam.Compte("") == null);

            List<JeuxCompteSteam.Possede> l = JeuxCompteSteam.Lire(Vdf);
            var ids = new List<string>();
            foreach (JeuxCompteSteam.Possede p in l) ids.Add(p.AppId + ":" + p.Minutes);
            Banc.Egal("seul le bloc Software › Valve › Steam › apps compte, du plus récent au plus ancien, sans entrée vide",
                "1172470:42,730:5400,440:90", string.Join(",", ids));
            Banc.Verifie("fichier vide ou tronqué : liste vide, sans exception", true,
                JeuxCompteSteam.Lire("").Count == 0 && JeuxCompteSteam.Lire("\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{").Count == 0);

            var absents = JeuxCompteSteam.NonInstalles(l, new HashSet<int> { 730 });
            Banc.Verifie("les jeux installés sont retirés", true, absents.Count == 2 && absents.Find(p => p.AppId == 730) == null);

            string json = "{\"response\":{\"store_items\":["
                + "{\"item_type\":0,\"id\":1172470,\"success\":1,\"appid\":1172470,\"name\":\"Apex Legends\",\"type\":0},"
                + "{\"item_type\":0,\"id\":431960,\"success\":1,\"appid\":431960,\"name\":\"Wallpaper Engine\",\"type\":6},"
                + "{\"item_type\":0,\"id\":228980,\"success\":15,\"appid\":228980},"
                + "{\"item_type\":0,\"id\":440,\"success\":1,\"appid\":440,\"name\":\"Team Fortress 2\"},"
                + "{\"item_type\":0,\"id\":232250,\"success\":1,\"appid\":232250,\"name\":\"Team Fortress 2 Dedicated Server\",\"type\":0}"
                + "]}}";
            Dictionary<int, string> jeux = JeuxCompteSteam.Jeux(json);
            Banc.Verifie("jeu gardé avec son nom", true, jeux.ContainsKey(1172470) && jeux[1172470] == "Apex Legends");
            Banc.Verifie("logiciel (type 6) écarté", false, jeux.ContainsKey(431960));
            Banc.Verifie("fiche retirée du store (success 15) écartée", false, jeux.ContainsKey(228980));
            Banc.Verifie("type absent : traité comme un jeu", true, jeux.ContainsKey(440));
            Banc.Verifie("serveur dédié écarté", false, jeux.ContainsKey(232250));
            Banc.Verifie("réponse illisible : rien, sans exception", true, JeuxCompteSteam.Jeux("<html>").Count == 0);

            Banc.Egal("statut : heures jouées", "Sur ton compte Steam · 90 h jouées", JeuxCompteSteam.Statut(5400));
            Banc.Egal("statut : minutes sous une heure", "Sur ton compte Steam · 42 min jouées", JeuxCompteSteam.Statut(42));
            Banc.Egal("statut : jamais compté", "Sur ton compte Steam · pas installé", JeuxCompteSteam.Statut(0));
        }
    }
}
