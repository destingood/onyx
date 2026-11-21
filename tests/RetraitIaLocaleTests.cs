using System;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Retrait de l'IA locale à la mise à jour : Ollama n'est désinstallé que si c'est ONYX qui l'a
    /// posé. Dates relevées sur un vrai PC : premier essai ONYX 15/10/2026 00:32:19, dossier Ollama
    /// créé 00:34:35 (winget, deux minutes plus tard).
    /// </summary>
    internal static class RetraitIaLocaleTests
    {
        public static void Tout()
        {
            var essai = new DateTime(2026, 10, 15, 0, 32, 19);

            // ------------------------------------------------ qui a installé Ollama ?
            Banc.Verifie("Ollama posé par ONYX : dossier créé juste après son premier essai", true,
                RetraitIaLocale.InstalleeParOnyx(essai, new DateTime(2026, 10, 15, 0, 34, 35)));
            Banc.Verifie("Ollama posé par ONYX : installation winget lente (2 h) encore reconnue", true,
                RetraitIaLocale.InstalleeParOnyx(essai, essai.AddHours(2)));
            Banc.Verifie("Ollama déjà là avant ONYX : gardé", false,
                RetraitIaLocale.InstalleeParOnyx(essai, new DateTime(2026, 9, 1, 12, 0, 0)));
            Banc.Verifie("Ollama installé par l'utilisateur des jours après : gardé", false,
                RetraitIaLocale.InstalleeParOnyx(essai, essai.AddDays(3)));
            Banc.Verifie("aucun essai ONYX ou pas d'Ollama : rien à désinstaller", true,
                !RetraitIaLocale.InstalleeParOnyx(null, essai) && !RetraitIaLocale.InstalleeParOnyx(essai, null));

            // ------------------------------------------------ réglages posés par ONYX
            Banc.Verifie("démarrage auto : seule l'entrée qui lance CET Ollama est retirée", true,
                RetraitIaLocale.RunDOllama("\"C:\\Users\\U\\AppData\\Local\\Programs\\Ollama\\ollama app.exe\"", @"C:\Users\U\AppData\Local\Programs\Ollama")
                && !RetraitIaLocale.RunDOllama("\"D:\\Outils\\ollama app.exe\"", @"C:\Users\U\AppData\Local\Programs\Ollama")
                && !RetraitIaLocale.RunDOllama(null, @"C:\x") && !RetraitIaLocale.RunDOllama("x", null));
            Banc.Verifie("OLLAMA_KEEP_ALIVE : retiré seulement s'il vaut la valeur posée par ONYX", true,
                RetraitIaLocale.KeepAliveDOnyx("30m") && !RetraitIaLocale.KeepAliveDOnyx("5m") && !RetraitIaLocale.KeepAliveDOnyx(null));

            // ------------------------------------------------ une seule fois, trois essais au plus
            Banc.Verifie("à faire au premier lancement après la mise à jour", true, RetraitIaLocale.AFaire(null));
            Banc.Verifie("fait : on ne recommence pas", false, RetraitIaLocale.AFaire("fait|Ollama désinstallé"));
            Banc.Verifie("échec : réessai au lancement suivant, trois fois au plus", true,
                RetraitIaLocale.AFaire("echec 1") && RetraitIaLocale.AFaire("echec 2") && !RetraitIaLocale.AFaire("echec 3"));
            Banc.Verifie("compteur d'échecs lu dans le marqueur", true,
                RetraitIaLocale.Echecs(null) == 0 && RetraitIaLocale.Echecs("echec 2") == 2 && RetraitIaLocale.Echecs("fait|x") == 0);

            // ------------------------------------------------ fichiers d'ONYX supprimés
            Banc.Verifie("données IA d'ONYX listées (drapeaux, mémoire apprise, index des vecteurs)", true,
                string.Join(",", RetraitIaLocale.FichiersOnyx) == "bt-ia-locale.txt,bt-ia-off.txt,bt-ia-setup.txt,bt-ia-consent.txt,bt-appris.md,bt-kb-index.txt");

            // ------------------------------------------------ bilan affiché dans le journal
            Banc.Verifie("bilan : Ollama désinstallé, place libérée", true,
                RetraitIaLocale.Bilan(true, false, 6657199308L) == "IA locale retirée : Ollama et ses modèles désinstallés (6,2 Go libérés).");
            Banc.Verifie("bilan : Ollama gardé car installé par toi", true,
                RetraitIaLocale.Bilan(false, true, 0) == "IA locale retirée d'ONYX. Ollama est gardé : tu l'avais installé toi-même.");
            Banc.Verifie("bilan : rien d'autre à retirer", true,
                RetraitIaLocale.Bilan(false, false, 0) == "IA locale retirée d'ONYX.");
            Banc.Verifie("taille lisible", true, RetraitIaLocale.Taille(6657199308L) == "6,2 Go" && RetraitIaLocale.Taille(524288000L) == "500 Mo"
                && RetraitIaLocale.Taille(0) == "0 Mo");
        }
    }
}
