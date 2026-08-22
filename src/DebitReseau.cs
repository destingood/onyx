using System;

namespace BTOptimizer
{
    /// <summary>
    /// MESURER UN DÉBIT SANS COMPTER LE TEMPS OÙ RIEN NE CIRCULE.
    ///
    /// L'ancien test lançait le chronomètre AVANT d'ouvrir la connexion, puis divisait les octets
    /// reçus par ce temps-là. Or entre le départ du chronomètre et le premier octet, il y a la
    /// résolution DNS, l'établissement TCP, la poignée de main TLS et l'attente des en-têtes :
    /// autant de temps pendant lequel il ne passe pas un seul des octets comptés.
    ///
    /// Mesuré sur cette machine : 181 ms de mise en place pour 142 ms de transfert. Plus de la
    /// moitié de la « durée du téléchargement » ne transférait rien. Résultat affiché
    /// 617,9 Mb/s au lieu de 1404,5 — l'app annonçait 56 % de moins que la réalité.
    ///
    /// Le biais est d'autant plus fort que la connexion est rapide : la mise en place ne raccourcit
    /// pas, elle. C'est donc le meilleur abonnement qui était le plus injustement noté.
    ///
    /// Deuxième piège, celui-là insoluble par le seul chronomètre : TCP démarre lentement, en
    /// augmentant progressivement son débit. Mesurer 25 Mo qui passent en 142 ms, c'est mesurer
    /// surtout cette montée en régime. On écarte donc un échauffement, et — surtout — on REFUSE
    /// d'annoncer un chiffre précis quand la fenêtre de mesure a été trop courte pour valoir
    /// quelque chose.
    /// </summary>
    internal static class DebitReseau
    {
        /// <summary>
        /// Tailles demandées au serveur, de la plus utile à la plus sûre.
        ///
        /// Plus le fichier est gros, plus la fenêtre de mesure est longue et le chiffre solide.
        /// Mais le serveur a un plafond : vérifié sur speed.cloudflare.com, 75 Mo passent et
        /// 100 Mo rendent 403. Coder une seule taille en dur, c'est risquer que le test entier
        /// tombe en panne le jour où ce plafond bouge — et l'app annoncerait alors « pas de
        /// connexion » à un utilisateur parfaitement connecté.
        ///
        /// On essaie donc dans l'ordre, et on se rabat. La dernière valeur est celle qui a
        /// toujours fonctionné.
        /// </summary>
        public static readonly long[] TaillesAEssayer = { 75000000, 25000000, 10000000 };

        /// <summary>Durée écartée en début de transfert : le temps que TCP atteigne son régime.</summary>
        public const double EchauffementSecondes = 0.25;

        /// <summary>En deçà, la mesure est trop courte pour être annoncée comme un chiffre ferme.</summary>
        public const double FenetreMinimale = 0.40;

        /// <summary>PUR : l'adresse de test pour une taille donnée.</summary>
        public static string Adresse(long octets)
        {
            return "https://speed.cloudflare.com/__down?bytes=" + octets;
        }

        /// <summary>PUR : débit en Mb/s. Rend -1 quand il n'y a rien à calculer, jamais un
        /// chiffre tiré d'une division par presque zéro.</summary>
        public static double Mbps(long octets, double secondes)
        {
            if (octets <= 0 || secondes <= 0 || double.IsNaN(secondes) || double.IsInfinity(secondes)) return -1;
            return octets * 8.0 / 1000000.0 / secondes;
        }

        /// <summary>PUR : la fenêtre de mesure permet-elle d'annoncer un chiffre ferme ?</summary>
        public static bool FenetreSuffisante(double secondes)
        {
            return secondes >= FenetreMinimale;
        }

        /// <summary>
        /// PUR : la réserve à afficher à côté du chiffre. Vide quand la mesure tient debout.
        ///
        /// Quand la connexion vide les 100 Mo plus vite que la fenêtre minimale, le résultat reste
        /// un PLANCHER : le lien est au moins aussi rapide que ça, on ne sait pas de combien plus.
        /// Le dire vaut mieux que d'afficher deux décimales trompeuses.
        /// </summary>
        public static string Reserve(double secondesMesurees)
        {
            if (FenetreSuffisante(secondesMesurees)) return "";
            return "mesure sur " + secondesMesurees.ToString("0.00") + " s seulement : ta connexion vide le "
                 + "fichier de test trop vite pour qu'on la mesure précisément. Le chiffre est un PLANCHER — "
                 + "ton débit réel est au moins celui-là.";
        }
    }
}
