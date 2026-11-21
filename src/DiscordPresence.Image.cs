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
        /// Grande image de la présence : l'icône de l'application ONYX, servie par Discord.
        /// L'application n'a aucune ressource dans Rich Presence → Art Assets ; demander la clé
        /// « logo » donnait une image blanche chez tous ceux qui voyaient le statut. Discord
        /// accepte une adresse à la place d'une clé, à condition qu'elle soit en https.
        /// Si l'icône de l'application change, ce hachage change aussi : le mettre à jour ici.
        /// </summary>
        public const string ImageLogo =
            "https://cdn.discordapp.com/app-icons/" + AppIdParDefaut + "/7f8cd3c27bca764ed9667980345b769e.png?size=1024";

        /// <summary>Bloc « assets » de l'activité envoyée à Discord.</summary>
        public static string Assets()
        {
            return "\"assets\":{\"large_image\":\"" + ImageLogo + "\",\"large_text\":\"ONYX — l'optimiseur gaming\"}";
        }
    }
}
