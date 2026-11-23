namespace BTOptimizer
{
    /// <summary>
    /// Présence Discord, partie pure : identifiant de l'application ONYX et image affichée.
    /// </summary>
    internal static partial class DiscordPresence
    {
        /// <summary>Application « ONYX » officielle du portail développeur Discord. Identifiant
        /// PUBLIC (voir la note de la classe) : c'est ce qui permet à la présence de marcher dès
        /// l'installation, sans manipulation.</summary>
        public const string AppIdParDefaut = "1537266582785495120";

        /// <summary>
        /// Grande image de la présence : la ressource « logo » de l'application ONYX, déposée
        /// dans le portail développeur Discord (Rich Presence → Art Assets, 1024 × 1024, fichier
        /// source marketing/discord-logo-1024.png). La 15.87 envoyait à la place l'adresse de
        /// l'icône de l'application : elle ne fait que 256 px et Discord ne l'a pas affichée.
        /// Si la ressource « logo » disparaît du portail, le statut redevient une page blanche.
        /// </summary>
        public const string ImageLogo = "logo";

        /// <summary>Bloc « assets » de l'activité envoyée à Discord.</summary>
        public static string Assets()
        {
            return "\"assets\":{\"large_image\":\"" + ImageLogo + "\",\"large_text\":\"ONYX — l'optimiseur gaming\"}";
        }

        /// <summary>
        /// Ordre qui EFFACE la présence. Fermer le tuyau sans l'envoyer ne suffit pas : Discord
        /// garde l'activité affichée jusqu'à son propre redémarrage, et l'utilisateur qui décoche
        /// la case ne voit rien changer. Le pid est celui annoncé à SET_ACTIVITY, sinon Discord
        /// ne sait pas quelle activité retirer.
        /// </summary>
        public static string OrdreEffacement(int pid)
        {
            return "{\"cmd\":\"CLEAR_ACTIVITY\",\"nonce\":\"" + System.Guid.NewGuid().ToString()
                 + "\",\"args\":{\"pid\":" + pid + "}}";
        }
    }
}
