using System;

namespace BTOptimizer
{
    /// <summary>
    /// CE QUE LE CHECK UP+ A RÉELLEMENT FAIT — pas ce qu'il a tenté.
    ///
    /// Les routines de maintenance rendaient un compte-rendu écrit à l'avance. « Cache DNS et ARP
    /// vidés. » était renvoyé quoi qu'il arrive : la commande était lancée sans attendre, son code
    /// de retour n'était jamais lu, et l'échec du lancement lui-même était avalé par un catch
    /// silencieux. L'utilisateur lisait une confirmation ; elle ne reposait sur rien.
    ///
    /// Ces fonctions transforment un résultat CONSTATÉ en phrase. Elles sont pures : c'est la seule
    /// façon de vérifier qu'aucun chemin ne rend une confirmation sans l'avoir méritée.
    ///
    /// Distinction qui structure tout le reste : une commande courte, on l'ATTEND et on rend compte
    /// du résultat ; une commande longue ou interactive (SFC, défragmentation), on la LANCE et on
    /// dit qu'on l'a lancée — jamais qu'elle a abouti.
    /// </summary>
    internal static class Maintenance
    {
        /// <summary>PUR : compte-rendu du rafraîchissement réseau, selon ce qui a vraiment abouti.</summary>
        public static string ResumeReseau(bool dnsOk, bool arpOk)
        {
            if (dnsOk && arpOk) return "Cache DNS et cache ARP vidés.";
            if (dnsOk) return "Cache DNS vidé — mais le cache ARP a refusé de se vider.";
            if (arpOk) return "Cache ARP vidé — mais le cache DNS a refusé de se vider.";
            return "Rafraîchissement réseau ÉCHOUÉ : ni le cache DNS ni le cache ARP n'ont pu être vidés.";
        }

        /// <summary>
        /// PUR : compte-rendu d'une commande longue qu'on ne fait que lancer. On dit « lancée »,
        /// jamais « terminée » — on n'en sait rien, et le prétendre serait une invention.
        /// </summary>
        public static string ResumeLancement(bool lance, string quoi)
        {
            if (string.IsNullOrEmpty(quoi)) quoi = "La commande";
            return lance
                ? quoi + " lancé(e) dans une console — surveille sa fenêtre, elle peut durer plusieurs minutes."
                : quoi + " N'A PAS PU ÊTRE LANCÉ(E) : Windows a refusé de démarrer le programme.";
        }

        /// <summary>
        /// PUR : ce fichier est-il un cache de vignettes ou d'icônes de l'Explorateur ?
        ///
        /// Le dossier qui les contient héberge aussi des fichiers qui n'ont rien à y faire
        /// (« RecommendationsFilterList.json », un journal de démarrage). Nettoyer le dossier en
        /// bloc les emporterait avec le reste : on filtre donc par motif, pas par emplacement.
        /// </summary>
        public static bool EstCacheMiniature(string nomFichier)
        {
            if (string.IsNullOrEmpty(nomFichier)) return false;
            string n = nomFichier.ToLowerInvariant();
            if (!n.EndsWith(".db", StringComparison.Ordinal)) return false;
            return n.StartsWith("thumbcache_", StringComparison.Ordinal)
                || n.StartsWith("iconcache_", StringComparison.Ordinal);
        }
    }
}
