using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="ReglagesCyberpunk"/> — préréglages écrits dans UserSettings.json : seuls les
    /// jetons value / index visés bougent, « Rétablir » rend le fichier exact et laisse ce que le
    /// joueur a changé lui-même ; une valeur hors de la liste du jeu n'est jamais écrite.
    /// </summary>
    internal static class ReglagesCyberpunkTests
    {
        private const string Json =
            "{\n    \"version\": 152,\n    \"data\": [\n" +
            "        {\n            \"group_name\": \"/graphics/advanced\",\n            \"options\": [\n" +
            "                {\n                    \"name\": \"LocalShadowsQuality\",\n                    \"type\": \"string_list\",\n                    \"is_dynamic\": false,\n" +
            "                    \"value\": \"High\",\n                    \"index\": 3,\n                    \"default_index\": 3,\n                    \"values\": [\n                        \"Off\",\n                        \"Low\",\n                        \"Medium\",\n                        \"High\"\n                    ]\n                },\n" +
            "                {\n                    \"name\": \"Anisotropy\",\n                    \"type\": \"int_list\",\n                    \"value\": 16,\n                    \"index\": 3,\n                    \"default_index\": 3,\n                    \"values\": [ 1, 4, 8, 16 ]\n                },\n" +
            "                {\n                    \"name\": \"ContactShadows\",\n                    \"type\": \"bool\",\n                    \"value\": true,\n                    \"default_value\": true\n                },\n" +
            "                {\n                    \"name\": \"ScreenSpaceReflectionsQuality\",\n                    \"type\": \"string_list\",\n                    \"value\": \"Insane\",\n                    \"index\": 5,\n                    \"values\": [\"Off\", \"Low\", \"Medium\", \"High\", \"Ultra\", \"Insane\"]\n                }\n" +
            "            ]\n        },\n" +
            "        {\n            \"group_name\": \"/graphics/raytracing\",\n            \"options\": [\n" +
            "                {\n                    \"name\": \"RayTracing\",\n                    \"type\": \"bool\",\n                    \"value\": true,\n                    \"default_value\": false\n                }\n            ]\n        },\n" +
            "        {\n            \"group_name\": \"/gameplay/misc\",\n            \"options\": [\n" +
            "                {\n                    \"name\": \"MotionBlur\",\n                    \"type\": \"string_list\",\n                    \"value\": \"High\",\n                    \"index\": 2,\n                    \"values\": [\"Off\", \"Low\", \"High\"]\n                }\n            ]\n        }\n" +
            "    ]\n}";

        public static void Tout()
        {
            Banc.Titre("Préréglages Cyberpunk 2077");

            List<string> absentes;
            List<ReglagesCyberpunk.Changement> c = ReglagesCyberpunk.Prevoit(Json, ReglagesCyberpunk.Mode.Competition, out absentes);
            Banc.Egal("compétition : options présentes qui changent",
                "ContactShadows true>false|Anisotropy 16>8|LocalShadowsQuality High>Low|ScreenSpaceReflectionsQuality Insane>Off|RayTracing true>false",
                string.Join("|", c.ConvertAll(x => x.Cle + " " + x.Avant + ">" + x.Apres)));
            Banc.Verifie("option d'un autre groupe au même nom (MotionBlur de /gameplay) : ignorée, la vraie est signalée absente", true,
                absentes.Contains("MotionBlur") && !c.Exists(x => x.Section == "/gameplay/misc"));

            string apres = ReglagesCyberpunk.Applique(Json, c);
            string attendu = Json
                .Replace("\"value\": \"High\",\n                    \"index\": 3,", "\"value\": \"Low\",\n                    \"index\": 1,")
                .Replace("\"value\": 16,\n                    \"index\": 3,", "\"value\": 8,\n                    \"index\": 2,")
                .Replace("\"name\": \"ContactShadows\",\n                    \"type\": \"bool\",\n                    \"value\": true,", "\"name\": \"ContactShadows\",\n                    \"type\": \"bool\",\n                    \"value\": false,")
                .Replace("\"value\": \"Insane\",\n                    \"index\": 5,", "\"value\": \"Off\",\n                    \"index\": 0,")
                .Replace("\"name\": \"RayTracing\",\n                    \"type\": \"bool\",\n                    \"value\": true,", "\"name\": \"RayTracing\",\n                    \"type\": \"bool\",\n                    \"value\": false,");
            Banc.Egal("seuls value et index changent, octet pour octet (default_index intact)", attendu, apres);
            Banc.Egal("rétablir rend le fichier exact", Json, ReglagesCyberpunk.Retablit(apres, c));

            string joueur = apres.Replace("\"value\": \"Low\",\n                    \"index\": 1,", "\"value\": \"Medium\",\n                    \"index\": 2,");
            string retabli = ReglagesCyberpunk.Retablit(joueur, c);
            Banc.Verifie("rétablir garde ce que le joueur a changé ensuite dans le menu", true,
                retabli.Contains("\"value\": \"Medium\",\n                    \"index\": 2,") && retabli.Contains("\"value\": 16,") && retabli.Contains("\"value\": \"Insane\","));

            List<ReglagesCyberpunk.Changement> qualite = ReglagesCyberpunk.Prevoit(apres, ReglagesCyberpunk.Mode.Qualite, out absentes);
            Banc.Verifie("qualité : ray tracing laissé tel quel", true, !qualite.Exists(x => x.Cle == "RayTracing"));
            string deuxFois = ReglagesCyberpunk.Applique(apres, qualite);
            Banc.Egal("deux préréglages puis rétablir : fichier d'origine", Json, ReglagesCyberpunk.Retablit(deuxFois, ReglagesCyberpunk.Fusionne(c, qualite)));

            string sansLow = Json.Replace("\"Off\",\n                        \"Low\",\n", "\"Off\",\n");
            List<ReglagesCyberpunk.Changement> c2 = ReglagesCyberpunk.Prevoit(sansLow, ReglagesCyberpunk.Mode.Competition, out absentes);
            Banc.Verifie("valeur visée absente de la liste du jeu : non touchée, signalée", true,
                absentes.Contains("LocalShadowsQuality") && !c2.Exists(x => x.Cle == "LocalShadowsQuality"));
            string incoherent = Json.Replace("\"value\": \"High\",\n                    \"index\": 3,", "\"value\": \"High\",\n                    \"index\": 1,");
            ReglagesCyberpunk.Prevoit(incoherent, ReglagesCyberpunk.Mode.Competition, out absentes);
            Banc.Verifie("index qui ne correspond pas à la valeur : option non touchée", true, absentes.Contains("LocalShadowsQuality"));

            List<ReglagesCyberpunk.Changement> rien = ReglagesCyberpunk.Prevoit("{ \"data\": [", ReglagesCyberpunk.Mode.Competition, out absentes);
            Banc.Verifie("JSON tronqué : rien de prévu, fichier rendu tel quel", true,
                rien.Count == 0 && absentes.Count > 0 && ReglagesCyberpunk.Applique("{ \"data\": [", c) == "{ \"data\": [");
            Banc.Verifie("valeur à échapper refusée à l'écriture", true,
                ReglagesCyberpunk.Applique(Json, new List<ReglagesCyberpunk.Changement> { new ReglagesCyberpunk.Changement { Section = "/graphics/advanced", Cle = "LocalShadowsQuality", Avant = "High", Apres = "Lo\"w" } }) == Json);

            Banc.Egal("ligne lisible", "Ombres locales : Élevé → Bas", ReglagesCyberpunk.Ligne(c[2]));
            Banc.Egal("ligne d'un nombre", "Filtrage anisotrope : 16x → 8x", ReglagesCyberpunk.Ligne(c[1]));
            List<ReglagesCyberpunk.Changement> lu = ReglagesCyberpunk.Lire(ReglagesCyberpunk.Ecrire(c));
            Banc.Verifie("état relu à l'identique", true, lu.Count == c.Count && lu[3].Section == "/graphics/advanced" && lu[3].Avant == "Insane" && lu[3].Apres == "Off");
            Banc.Verifie("fichier d'état abîmé : lignes ignorées", true, ReglagesCyberpunk.Lire("x\ty\n\n").Count == 0 && ReglagesCyberpunk.Lire(null).Count == 0);

            // Le vrai fichier de ce PC, s'il existe : lu seulement, jamais écrit.
            string vrai = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CD Projekt Red", "Cyberpunk 2077", "UserSettings.json");
            if (File.Exists(vrai))
            {
                string texte = File.ReadAllText(vrai);
                List<ReglagesCyberpunk.Changement> v = ReglagesCyberpunk.Prevoit(texte, ReglagesCyberpunk.Mode.Competition, out absentes);
                string ecrit = ReglagesCyberpunk.Applique(texte, v);
                bool valide;
                try { using (JsonDocument.Parse(ecrit)) valide = true; } catch { valide = false; }
                Banc.Verifie("vrai fichier : toutes les options reconnues (" + v.Count + " changements)", true, absentes.Count == 0);
                Banc.Verifie("vrai fichier : JSON valide après écriture, rétablir le rend exact", true,
                    valide && ReglagesCyberpunk.Retablit(ecrit, v) == texte && ReglagesCyberpunk.Prevoit(ecrit, ReglagesCyberpunk.Mode.Competition, out absentes).Count == 0);
            }
        }
    }
}
