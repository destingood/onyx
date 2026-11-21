using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Actions en un clic du Copilote : « désactive la transparence », « active le mode jeu »,
    /// « remets les widgets »… trouvent le bon réglage du catalogue et le bon sens (appliquer ou
    /// rétablir). Les noms reprennent ceux de Tweaks.cs. Sans verbe d'action, ou en cas de doute,
    /// rien n'est proposé : une question n'est pas un ordre.
    /// </summary>
    internal static class CopiloteReglagesTests
    {
        private static List<Tweak> Catalogue()
        {
            return new List<Tweak>
            {
                new Tweak { Id = "transparency", Name = "Désactiver la transparence de l'interface" },
                new Tweak { Id = "game_mode", Name = "Activer le Mode Jeu de Windows" },
                new Tweak { Id = "edge_background", Name = "Empêcher Microsoft Edge de tourner en fond / au démarrage" },
                new Tweak { Id = "chrome_bg_off", Name = "Empêcher Google Chrome de tourner en arrière-plan" },
                new Tweak { Id = "fastboot_off", Name = "Désactiver le démarrage rapide (Fast Startup)", Risque = Risque.Fonction },
                new Tweak { Id = "startup_delay", Name = "Supprimer le délai de lancement des applis au démarrage" },
                new Tweak { Id = "widgets_off", Name = "Désactiver les Widgets (Windows 11) / Actualités (Windows 10)", Risque = Risque.Fonction },
                new Tweak { Id = "menu_delay", Name = "Menus instantanés (MenuShowDelay = 0)" },
                new Tweak { Id = "mpo_off", Name = "Désactiver le MultiPlane Overlay (MPO)" },
                new Tweak { Id = "mouse_accel", Name = "Désactiver l'accélération de la souris (précision du pointeur)", Risque = Risque.AnnulationNonGarantie },
                new Tweak { Id = "spectre_off", Name = "Désactiver les protections Spectre / Meltdown", Risque = Risque.Securite },
                new Tweak { Id = "taskbar_anim_off", Name = "Désactiver les animations de la barre des tâches et des fenêtres" },
                new Tweak { Id = "visualfx", Name = "Effets visuels : réglé sur « Meilleures performances »" },
                // Voisins du vrai catalogue qui créaient des confusions.
                new Tweak { Id = "edge_boost_off", Name = "Désactiver le « démarrage rapide » d'Edge (Startup Boost)" },
                new Tweak { Id = "mouse_curve", Name = "Courbe de souris 1:1 (fix MarkC, zéro accélération résiduelle)", Risque = Risque.AnnulationNonGarantie },
                new Tweak { Id = "logon_anim_off", Name = "Désactiver l'animation de première connexion" },
                new Tweak { Id = "search_bing_off", Name = "Recherche du menu Démarrer 100 % locale (sans Bing)" },
                new Tweak { Id = "feedback_off", Name = "Ne plus demander de commentaires (feedback Windows)" },
                new Tweak { Id = "gamebar_popup", Name = "Désactiver l'ouverture de la Xbox Game Bar (Win+G / bouton manette)" },
                new Tweak { Id = "bt_absvol_off", Name = "Bluetooth : désactiver le « volume absolu » (contrôle fin du casque)" },
                new Tweak { Id = "explorer_separate", Name = "Explorateur : fenêtres de dossiers dans des processus séparés" },
                new Tweak { Id = "power_throttling", Name = "Désactiver le Power Throttling (bridage énergétique des processus)" },
                new Tweak { Id = "activity_history_off", Name = "Désactiver l'historique d'activité / Timeline" },
            };
        }

        private static string Id(string message, out bool retablir, out string refus)
        {
            CopiloteReglages.Choix c = CopiloteReglages.Trouve(CopiloteTexte.Prepare(message), Catalogue());
            retablir = c != null && c.Retablir;
            refus = c == null ? null : c.Refus;
            return c == null ? null : c.Reglage.Id;
        }

        private static void Attendu(string quoi, string message, string id, bool retablir)
        {
            bool r; string refus;
            string obtenu = Id(message, out r, out refus);
            bool ok = obtenu == id && r == retablir && refus == null;
            Banc.Verifie(ok ? quoi : quoi + " [obtenu : " + (obtenu ?? "rien") + (r ? ", rétablir" : ", appliquer") + (refus != null ? ", refus" : "") + "]", true, ok);
        }

        private static void Rien(string quoi, string message)
        {
            bool r; string refus;
            string obtenu = Id(message, out r, out refus);
            Banc.Verifie(obtenu == null ? quoi : quoi + " [obtenu : " + obtenu + "]", true, obtenu == null);
        }

        public static void Tout()
        {
            // ------------------------------------------------ appliquer
            Attendu("« désactive la transparence » applique le réglage", "désactive la transparence", "transparency", false);
            Attendu("faute et sans accent : « desactiv la transparance »", "desactive la transparance", "transparency", false);
            Attendu("« active le mode jeu »", "active le mode jeu", "game_mode", false);
            Attendu("marque citée seule : « empêche Edge de tourner en fond »", "empêche edge de tourner", "edge_background", false);
            Attendu("autre verbe : « coupe chrome en arrière-plan »", "coupe chrome en arriere plan", "chrome_bg_off", false);
            Attendu("deux réglages parlent du démarrage : le plus précis gagne", "désactive le démarrage rapide", "fastboot_off", false);
            Attendu("sigle : « désactive le MPO »", "desactive le mpo", "mpo_off", false);
            Attendu("réglage sans verbe dans son nom : « mets les menus instantanés »", "mets les menus instantanes", "menu_delay", false);
            Attendu("texto : « vire les widgets stp »", "vire les widgets stp", "widgets_off", false);

            // ------------------------------------------------ rétablir
            Attendu("« réactive la transparence » rétablit", "réactive la transparence", "transparency", true);
            Attendu("verbe inverse du réglage : « active la transparence » rétablit", "active la transparence", "transparency", true);
            Attendu("« désactive le mode jeu » rétablit le réglage « Activer le Mode Jeu »", "désactive le mode jeu", "game_mode", true);
            Attendu("« remets les widgets »", "remets les widgets", "widgets_off", true);
            Attendu("« annule les animations de la barre des tâches »", "annule les animations de la barre des taches", "taskbar_anim_off", true);

            // ------------------------------------------------ rien : question, doute, mot trop vague
            Rien("une question n'est pas un ordre", "c'est quoi la transparence");
            Rien("mot trop vague seul (« démarrage »)", "désactive le démarrage");
            Rien("aucun réglage ne correspond", "désactive bluetooth");
            Rien("phrase vide", "");
            Rien("le verbe ne compte pas comme sujet (« active » n'est pas « activité »)", "active internet");
            Rien("faute trop lointaine ou clé courte refusées (« defender » ≠ « demander », « windows » ≠ « Win »)", "désactive windows defender");
            Rien("marque du réglage absente (« tourne en fond » sans dire Edge)", "mon pc rame, désactive ce qui tourne en fond");
            Rien("deux réglages aussi proches : on ne devine pas", "désactive les animations");
            Rien("la catégorie seule ne suffit pas (« Bluetooth : … volume absolu »)", "active le bluetooth");
            Rien("mot long mais courant dans le catalogue (« processus »)", "coupe les processus en fond");
            Attendu("« Catégorie : désactiver… » est un réglage qui coupe", "désactive le volume absolu du bluetooth", "bt_absvol_off", false);
            Attendu("catégorie complète citée (« effets visuels »)", "mets les effets visuels en performances", "visualfx", false);

            // ------------------------------------------------ départage et sens des réglages « sans… »
            Attendu("« sans Bing » : couper Bing applique le réglage", "coupe la recherche bing", "search_bing_off", false);
            Attendu("« sans Bing » : remettre Bing le rétablit", "active la recherche bing", "search_bing_off", true);
            Attendu("le mot fort l'emporte (« chrome » contre « fond »)", "empêche chrome de tourner en fond", "chrome_bg_off", false);

            // ------------------------------------------------ réglages risqués : reconnus mais refusés
            bool rr; string refus;
            Banc.Verifie("réglage à annulation non garantie : refusé avec une raison", true,
                Id("désactive l'accélération de la souris", out rr, out refus) == "mouse_accel" && refus != null);
            Banc.Verifie("réglage qui baisse une protection : refusé avec une raison", true,
                Id("désactive les protections spectre", out rr, out refus) == "spectre_off" && refus != null);
        }
    }
}
