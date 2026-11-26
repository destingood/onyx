namespace BTOptimizer
{
    /// <summary>
    /// CAPTURE DES 30 DERNIÈRES SECONDES (logique PURE) : « Enregistrer ce qui s'est passé » de la
    /// Game Bar. Valeurs lues dans le registre (relevées le 26/11/2026, doc. Windows.Media.Capture.
    /// AppCaptureSettings) : stratégie HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR\AllowGameDVR,
    /// HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled (absent = permis)
    /// et HistoricalCaptureEnabled (absent = coupé, réglage par défaut de Windows).
    /// </summary>
    internal static partial class CaptureFond
    {
        public enum Etat { BloqueeParStrategie, Coupee, Active }

        public static Etat Juge(int? strategie, int? captureApplis, int? historique)
        {
            if (strategie == 0) return Etat.BloqueeParStrategie;
            if (captureApplis == 0 || historique != 1) return Etat.Coupee;
            return Etat.Active;
        }

        public static string Explique(Etat e, bool gamedvrOffApplique)
        {
            switch (e)
            {
                case Etat.BloqueeParStrategie:
                    return gamedvrOffApplique
                        ? "Bloquée par ONYX : le réglage « Désactiver Game DVR » coupe toute capture. Pour garder les 30 dernières "
                          + "secondes, « Rétablir » d'abord ce réglage (ici ou dans Réglages), puis « Activer »."
                        : "Bloquée par une stratégie de Windows (AllowGameDVR = 0) qu'ONYX n'a pas posée : un autre logiciel "
                          + "ou l'administrateur du PC l'a mise. ONYX n'y touche pas.";
                case Etat.Coupee:
                    return "Coupée : Windows ne garde rien en mémoire. « Activer » lui fait garder les 30 dernières secondes ; "
                         + "Win+Alt+G les enregistre ensuite dans Captures.";
                default:
                    return "Active : Win+Alt+G enregistre les 30 dernières secondes dans Captures. Coût non mesuré sur ton PC : "
                         + "« Mesurer le coût en jeu » le chiffre.";
            }
        }
    }
}
