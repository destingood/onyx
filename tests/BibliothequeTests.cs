using System;
using System.Collections.Generic;
using System.Text;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Page Jeux : tri jeu / application, jaquettes Epic lues en local, et un seul exemplaire d'un jeu
    /// possédé sur plusieurs plateformes. Données raccourcies, relevées sur un vrai PC le 21/11/2026.
    /// </summary>
    internal static class BibliothequeTests
    {
        static string Manifeste(string nom, string categories, string app, string principal, string id)
        {
            return "{\"DisplayName\":\"" + nom + "\",\"InstallLocation\":\"F:\\\\epic\\\\X\",\"LaunchExecutable\":\"Bin/x.exe\",\"AppName\":\"" + app
                + "\",\"MainGameAppName\":\"" + principal + "\",\"CatalogItemId\":\"" + id + "\"" + (categories == null ? "" : ",\"AppCategories\":[" + categories + "]") + "}";
        }

        public static void Tout()
        {
            // ------------------------------------------------ Epic : jeu ou pas
            var fn = BibliothequeJeux.EpicManifeste(Manifeste("Fortnite", "\"games\",\"applications\"", "Fortnite", "", "4fe75bbc5a674f4f9b356b5c90567da5"));
            Banc.Verifie("Epic : Fortnite est un jeu", true, fn != null && fn.EstJeu && fn.Nom == "Fortnite" && fn.Id == "4fe75bbc5a674f4f9b356b5c90567da5"
                && fn.Dossier == @"F:\epic\X" && fn.Exe == "Bin/x.exe");
            Banc.Verifie("Epic : moteur, plugin et asset écartés", true,
                !BibliothequeJeux.EpicManifeste(Manifeste("Unreal Engine", "\"engines/ue5\",\"engines\"", "UE_5.8", "", "a")).EstJeu
                && !BibliothequeJeux.EpicManifeste(Manifeste("Fab UE Plugin", "\"plugins/engine\",\"plugins\"", "FabPlugin_5.8", "UE_5.8", "b")).EstJeu
                && !BibliothequeJeux.EpicManifeste(Manifeste("EngineWire", "\"Intelligence artificielle\"", "MCP", "MCP", "c")).EstJeu);
            Banc.Verifie("Epic : extension d'un jeu (éditeur, contenu caché) écartée", true,
                !BibliothequeJeux.EpicManifeste(Manifeste("Unreal Editor pour Fortnite", "\"addons/launchable\",\"software\",\"addons\",\"applications\"", "Fortnite_Studio", "Fortnite", "d")).EstJeu
                && !BibliothequeJeux.EpicManifeste(Manifeste("Contenu de LEGO® Fortnite", "\"hidden\"", "94bc", "Fortnite", "e")).EstJeu);
            Banc.Verifie("Epic : ancien manifeste sans catégories gardé, sauf extension", true,
                BibliothequeJeux.EpicManifeste(Manifeste("Hades", null, "Min", "", "f")).EstJeu
                && !BibliothequeJeux.EpicManifeste(Manifeste("Hades DLC", null, "Dlc1", "Min", "g")).EstJeu);
            Banc.Verifie("Epic : manifeste illisible ou sans nom", true, BibliothequeJeux.EpicManifeste("<x>") == null
                && BibliothequeJeux.EpicManifeste("{\"AppName\":\"a\"}") == null && BibliothequeJeux.EpicManifeste(null) == null);

            // ------------------------------------------------ Epic : jaquettes du cache local (catcache.bin = JSON en base64)
            string cat = "[{\"id\":\"4fe75bbc5a674f4f9b356b5c90567da5\",\"keyImages\":["
                + "{\"type\":\"DieselGameBox\",\"url\":\"https://cdn1.epicgames.com/item/fn/large.jpg\"},"
                + "{\"type\":\"DieselGameBoxTall\",\"url\":\"https://cdn1.epicgames.com/item/fn/tall.jpg\"}]},"
                + "{\"id\":\"wide\",\"keyImages\":[{\"type\":\"Screenshot\",\"url\":\"https://cdn1.epicgames.com/s.jpg\"},{\"type\":\"OfferImageWide\",\"url\":\"https://cdn1.epicgames.com/w.jpg\"}]},"
                + "{\"id\":\"piege\",\"keyImages\":[{\"type\":\"DieselGameBoxTall\",\"url\":\"http://evil.example/x.jpg\"},{\"type\":\"Thumbnail\",\"url\":\"https://evil.example/y.jpg\"}]},"
                + "{\"id\":\"vide\",\"keyImages\":[]}]";
            var img = BibliothequeJeux.ImagesEpic(Convert.ToBase64String(Encoding.UTF8.GetBytes(cat)));
            Banc.Verifie("Epic : jaquette verticale préférée, réduite", true, img.ContainsKey("4fe75bbc5a674f4f9b356b5c90567da5")
                && img["4fe75bbc5a674f4f9b356b5c90567da5"] == "https://cdn1.epicgames.com/item/fn/tall.jpg?h=480&resize=1");
            Banc.Verifie("Epic : à défaut, image large ; capture d'écran ignorée", true, img["wide"] == "https://cdn1.epicgames.com/w.jpg?h=480&resize=1");
            Banc.Verifie("Epic : hôte étranger ou http refusé, entrée vide absente", true, !img.ContainsKey("piege") && !img.ContainsKey("vide"));
            Banc.Verifie("Epic : cache illisible → vide", true, BibliothequeJeux.ImagesEpic("pas du base64 !").Count == 0
                && BibliothequeJeux.ImagesEpic(null).Count == 0 && BibliothequeJeux.ImagesEpic(Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"))).Count == 0);

            // ------------------------------------------------ programmes du registre : indices de jeu
            Banc.Verifie("indice : jeu ancien (Bink), Unity, Unreal", true,
                BibliothequeJeux.IndiceDeJeu(new[] { @"Game\binkw32.dll", @"Game\IncPc.exe", "Launcher.exe" })
                && BibliothequeJeux.IndiceDeJeu(new[] { "Hearthstone.exe", "UnityPlayer.dll" })
                && BibliothequeJeux.IndiceDeJeu(new[] { @"Content\Paks\pakchunk0-Windows.pak" })
                && BibliothequeJeux.IndiceDeJeu(new[] { "steam_api64.dll" }));
            Banc.Verifie("indice : applications sans signe de jeu", true,
                !BibliothequeJeux.IndiceDeJeu(new[] { "Wireshark.exe", "d3dcompiler_47.dll", "avcodec-61.dll", @"imageformats\qgif.dll" })
                && !BibliothequeJeux.IndiceDeJeu(new[] { @"current\LuaTools.exe", @"fr\LuaTools.resources.dll" })
                && !BibliothequeJeux.IndiceDeJeu(new[] { "RemotePlay.exe", "libcurl.dll", "Microsoft.Web.WebView2.Core.dll" })
                && !BibliothequeJeux.IndiceDeJeu(null));
            Banc.Verifie("outil installé par WinGet", true,
                BibliothequeJeux.CheminOutil(@"C:\Users\U\AppData\Local\Microsoft\WinGet\Packages\jqlang.jq_Microsoft.Winget.Source_8wekyb3d8bbwe")
                && !BibliothequeJeux.CheminOutil(@"C:\Program Files (x86)\THQ\Disney\Les Indestructibles\") && !BibliothequeJeux.CheminOutil(null));
            Banc.Verifie("Xbox : launcher Minecraft écarté, jeu gardé", true, BibliothequeJeux.EstLauncher("Minecraft Launcher")
                && BibliothequeJeux.EstLauncher("Battle.net") && !BibliothequeJeux.EstLauncher("Minecraft for Windows"));

            // ------------------------------------------------ Steam : logiciels vendus sur le store (type 6 Software, 13 Tool…)
            var logiciels = BibliothequeJeux.LogicielsSteam("{\"response\":{\"store_items\":["
                + "{\"appid\":431960,\"success\":1,\"type\":6,\"name\":\"Wallpaper Engine\"},{\"appid\":730,\"success\":1,\"type\":0},"
                + "{\"appid\":5,\"success\":1,\"type\":13},{\"appid\":6,\"success\":2,\"type\":6},{\"appid\":7,\"success\":1},{\"appid\":8,\"success\":1,\"type\":4}]}}");
            Banc.Verifie("Steam : logiciels et outils repérés, jeux et fiches ratées gardés", true,
                logiciels.Count == 2 && logiciels.Contains(431960) && logiciels.Contains(5));
            Banc.Verifie("Steam : réponse illisible → aucun logiciel", true, BibliothequeJeux.LogicielsSteam("<html>").Count == 0
                && BibliothequeJeux.LogicielsSteam(null).Count == 0 && BibliothequeJeux.LogicielsSteam("{\"response\":{}}").Count == 0);

            // ------------------------------------------------ mot-clé du catalogue dans une clé de désinstallation
            Banc.Verifie("mot-clé : mot entier seulement", true, BibliothequeJeux.MotCleDans("Rust", "Rust")
                && BibliothequeJeux.MotCleDans("Tom Clancy's Rainbow Six® Siege", "Rainbow Six")
                && BibliothequeJeux.MotCleDans("Call of Duty®: Black Ops 6", "Call of Duty")
                && !BibliothequeJeux.MotCleDans("Battlefield Vietnam", "Battlefield V") && !BibliothequeJeux.MotCleDans("Rustup", "Rust"));
            Banc.Verifie("mot-clé : outil de développement ignoré (rustup)", true, !BibliothequeJeux.MotCleDans("Rustup: the Rust toolchain installer", "Rust")
                && !BibliothequeJeux.MotCleDans("Rust", null) && !BibliothequeJeux.MotCleDans(null, "Rust"));

            // ------------------------------------------------ même jeu sur deux plateformes
            Banc.Verifie("clé : symboles et éditions retirés", true, BibliothequeJeux.Cle("Control Ultimate Edition") == "control"
                && BibliothequeJeux.Cle("CONTROL™") == "control" && BibliothequeJeux.Cle("The Witcher 3: Wild Hunt – Game of the Year Edition") == "thewitcher3wildhunt"
                && BibliothequeJeux.Cle("Cyberpunk 2077 Deluxe") == "cyberpunk2077");
            Banc.Verifie("clé : jamais vidée par les mots d'édition", true, BibliothequeJeux.Cle("Gold") == "gold" && BibliothequeJeux.Cle("Ultimate Edition") == "ultimateedition"
                && BibliothequeJeux.Cle(null) == "" && BibliothequeJeux.Cle("Dark Souls Remastered") == "darksoulsremastered");
            Banc.Verifie("même jeu : même AppID Steam", true, BibliothequeJeux.MemeJeu("Call of Duty", 1938090, null, "Call of Duty® HQ", 1938090));
            Banc.Verifie("même jeu : même nom à l'édition près", true, BibliothequeJeux.MemeJeu("Control", 0, null, "Control Ultimate Edition", 870780));
            Banc.Verifie("même jeu : nom court du launcher = mot-clé du catalogue", true, BibliothequeJeux.MemeJeu("Overwatch 2", 2357570, new[] { "Overwatch" }, "Overwatch", 0));
            Banc.Verifie("jeux différents : suite ou épisode", true, !BibliothequeJeux.MemeJeu("Call of Duty", 1938090, new[] { "Call of Duty" }, "Call of Duty®: Black Ops 6", 4384550)
                && !BibliothequeJeux.MemeJeu("Dying Light", 239140, null, "Dying Light: The Beast", 3008130) && !BibliothequeJeux.MemeJeu("", 0, null, "", 0));

            var stores = new List<string>();
            BibliothequeJeux.AjouteStore(stores, "STEAM"); BibliothequeJeux.AjouteStore(stores, "epic"); BibliothequeJeux.AjouteStore(stores, "Steam"); BibliothequeJeux.AjouteStore(stores, " ");
            Banc.Verifie("plateformes : sans doublon ni vide", true, string.Join(",", stores) == "STEAM,EPIC");
            Banc.Verifie("pastille : toutes les plateformes", true, BibliothequeJeux.Pastille(stores) == "STEAM + EPIC"
                && BibliothequeJeux.Pastille(new List<string>()) == "Autres" && BibliothequeJeux.Pastille(null) == "Autres");
        }
    }
}
