using System;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// « Fermer Steam à la fin de la partie » (coupé par défaut). Seulement Steam : sa commande
    /// « steam.exe -shutdown » le ferme proprement (option documentée par Valve). Battle.net, EA, Ubisoft,
    /// GOG et Epic n'ont pas de commande de fermeture documentée : ONYX ne les arrête pas de force.
    /// Steam n'est fermé que si ONYX l'a démarré en lançant le jeu (il ne tournait pas avant), et que
    /// la partie close est bien celle de ce jeu. Partie logique, sans machine : testée par le banc.
    /// </summary>
    internal static partial class FermeLanceur
    {
        /// <summary>Délai entre la fin de la partie et la fermeture : synchronisation des sauvegardes dans le nuage.</summary>
        public static readonly TimeSpan Delai = TimeSpan.FromMinutes(2);

        /// <summary>Au-delà, le lancement noté est oublié (partie jamais reconnue).</summary>
        public static readonly TimeSpan Validite = TimeSpan.FromHours(12);

        public sealed class Lancement { public string Jeu; public DateTime Quand; public bool SteamTournait; }

        /// <summary>Nom comparable : lettres et chiffres en minuscules (« Counter-Strike 2 » = « counterstrike2 »).</summary>
        public static string Norme(string nom)
        {
            var sb = new StringBuilder();
            foreach (char c in nom ?? "") if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        /// <summary>La partie close autorise-t-elle la fermeture de Steam ?</summary>
        public static bool Concerne(Lancement l, string jeuClos, DateTime maintenant)
        {
            if (l == null || l.SteamTournait || maintenant < l.Quand || maintenant - l.Quand > Validite) return false;
            string a = Norme(l.Jeu), b = Norme(jeuClos);
            return a.Length > 0 && a == b;
        }
    }
}
