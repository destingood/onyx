namespace BTOptimizer.Tests
{
    /// <summary>
    /// Règles pures du Mode Jeu, écrites après trois défauts MESURÉS le 25/11/2026 sur un PC à trois
    /// écrans : l'appli Claude en plein écran sur le deuxième écran enclenchait le Mode Jeu auto
    /// (58 fois en 24 h) ; chaque enclenchement vidait la mémoire de tous les processus alors que
    /// 18 Go étaient libres ; et Windows Search, arrêté, était relancé par Windows 9 s plus tard.
    /// </summary>
    internal static class ModeJeuReglesTests
    {
        internal static void Tout()
        {
            Banc.Titre("Mode Jeu : plein écran d'un jeu, pas d'une application");
            Banc.Verifie("un jeu sans bordure qui couvre son écran compte",
                true, VeilleJeu.PleinEcranDeJeu("Wow", "waApplication Window", true));
            Banc.Verifie("un jeu non listé (moteur Unreal) compte",
                true, VeilleJeu.PleinEcranDeJeu("MonJeu-Win64-Shipping", "UnrealWindow", true));
            Banc.Verifie("classe de fenêtre inconnue : le nom du processus décide",
                true, VeilleJeu.PleinEcranDeJeu("MonJeu", null, true));
            Banc.Verifie("une fenêtre qui ne couvre pas son écran ne compte pas",
                false, VeilleJeu.PleinEcranDeJeu("MonJeu", "UnrealWindow", false));
            Banc.Verifie("l'appli Claude en plein écran n'est pas un jeu (défaut mesuré)",
                false, VeilleJeu.PleinEcranDeJeu("claude", "Chrome_WidgetWin_1", true));
            Banc.Verifie("un navigateur Chromium en plein écran n'est pas un jeu",
                false, VeilleJeu.PleinEcranDeJeu("msedge", "Chrome_WidgetWin_1", true));
            Banc.Verifie("la classe de fenêtre est comparée sans la casse",
                false, VeilleJeu.PleinEcranDeJeu("Discord", "chrome_widgetwin_1", true));
            Banc.Verifie("Firefox en plein écran n'est pas un jeu",
                false, VeilleJeu.PleinEcranDeJeu("firefox", "MozillaWindowClass", true));
            Banc.Verifie("l'outil de capture d'écran n'est pas un jeu",
                false, VeilleJeu.PleinEcranDeJeu("SnippingTool", "Microsoft.UI.Content.DesktopChildSiteBridge", true));
            Banc.Verifie("un lecteur vidéo en plein écran n'est pas un jeu",
                false, VeilleJeu.PleinEcranDeJeu("vlc", "Qt5QWindowIcon", true));
            Banc.Verifie("le nom du processus est comparé sans la casse",
                false, VeilleJeu.PleinEcranDeJeu("VLC", "Qt5QWindowIcon", true));
            Banc.Verifie("le bureau (explorer) n'est pas un jeu",
                false, VeilleJeu.PleinEcranDeJeu("explorer", "Progman", true));
            Banc.Verifie("processus sans nom : rien à enclencher",
                false, VeilleJeu.PleinEcranDeJeu(null, "UnrealWindow", true));

            Banc.Titre("Mode Jeu : la mémoire n'est vidée que si elle manque");
            Banc.Verifie("18 Go libres sur 32 : on ne vide rien (défaut mesuré)",
                false, ModeJeuRegles.MemoireACourt(18743, 32000));
            Banc.Verifie("4 Go libres sur 32 : mémoire à court",
                true, ModeJeuRegles.MemoireACourt(4000, 32000));
            Banc.Verifie("pile au seuil de 20 % : pas à court",
                false, ModeJeuRegles.MemoireACourt(6400, 32000));
            Banc.Verifie("juste sous le seuil : à court",
                true, ModeJeuRegles.MemoireACourt(6399, 32000));
            Banc.Verifie("total inconnu : on ne vide rien",
                false, ModeJeuRegles.MemoireACourt(0, 0));

            Banc.Titre("Mode Jeu : services jamais suspendus");
            Banc.Verifie("Windows Search : Windows le relance aussitôt, l'arrêt ne sert à rien",
                true, ModeJeuRegles.JamaisSuspendu("WSearch"));
            Banc.Verifie("conteneur NVIDIA : il porte la superposition et l'enregistrement",
                true, ModeJeuRegles.JamaisSuspendu("nvcontainerlocalsystem"));
            Banc.Verifie("le spouleur d'impression reste suspendable",
                false, ModeJeuRegles.JamaisSuspendu("Spooler"));
            Banc.Verifie("nom nul", false, ModeJeuRegles.JamaisSuspendu(null));
            Banc.Egal("une liste déjà enregistrée sur le disque est filtrée, l'ordre gardé",
                "SysMain,Spooler",
                string.Join(",", ModeJeuRegles.Suspendables(new[] { "SysMain", "WSearch", "NvContainerLocalSystem", "Spooler" })));
            Banc.Egal("liste nulle", "", string.Join(",", ModeJeuRegles.Suspendables(null)));

            Banc.Titre("Mode Jeu : les règles sont bien branchées");
            string gb = CatalogueTests.Source("GameBoost.cs");
            Banc.Verifie("le socle ne nomme plus Windows Search", false, gb.Contains("\"SysMain\", \"WSearch\""));
            Banc.Verifie("la liste suspendue passe par le filtre", true, gb.Contains("ModeJeuRegles.Suspendables("));
            Banc.Verifie("la mémoire n'est vidée que sous condition", true, gb.Contains("ModeJeuRegles.MemoireACourt("));
            Banc.Verifie("le plein écran passe par la règle pure",
                true, CatalogueTests.Source("Native.cs").Contains("VeilleJeu.PleinEcranDeJeu("));
        }
    }
}
