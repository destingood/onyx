using System;

namespace BTOptimizer
{
    /// <summary>
    /// OÙ TOMBENT LES INTERRUPTIONS — LA COLONNE QU'ONYX ALLAIT ENCORE CHERCHER AILLEURS.
    ///
    /// ONYX mesure déjà la latence par pilote : combien d'exécutions, quel temps total, quelle
    /// pire exécution. Il lui manquait la répartition PAR CŒUR, et c'est précisément ce qu'on
    /// est allé lire dans le rapport complet de LatencyMon pour établir ceci :
    ///
    ///     CPU 0     360 758 DPC   92,2 %   5,427 s
    ///     CPU 1      18 952 DPC    4,8 %   0,334 s
    ///     CPU 2-15    9 836 DPC    2,5 %   0,064 s
    ///
    /// C'était la dernière raison d'ouvrir un outil externe. Elle disparaît ici.
    ///
    /// MAIS LA VRAIE RAISON EST PLUS EMBARRASSANTE QUE LA PARITÉ :
    ///
    ///   ONYX écrit « DevicePolicy = 5 » pour étaler les interruptions sur tous les cœurs. Il
    ///   applique donc un réglage dont l'effet se mesure EXACTEMENT par cette répartition — et
    ///   il n'avait aucun moyen de vérifier qu'il avait produit quoi que ce soit. C'est le même
    ///   défaut que celui corrigé dans « ONYX annonçait répartir des interruptions qui ne peuvent
    ///   pas l'être », vu de l'autre bout : là on écrivait sans savoir si c'était possible, ici on
    ///   ne savait pas si ça avait marché.
    ///
    /// CE QUE CE MODULE REFUSE DE FAIRE :
    ///
    ///   Traiter la concentration comme un défaut. Elle est souvent NORMALE et impossible à
    ///   corriger : un périphérique qui n'expose qu'un seul vecteur d'interruption ne peut pas
    ///   être étalé, et le cœur 0 reçoit de toute façon les minuteurs du système. Crier au
    ///   déséquilibre enverrait quelqu'un chercher un réglage qui n'existe pas — le rapport
    ///   renvoie donc vers le nombre de vecteurs, seul endroit où la question se tranche.
    ///
    ///   Comparer à une répartition parfaite. Une machine à seize cœurs ne fera jamais 6,25 %
    ///   partout, et attendre ça produirait une alerte permanente. On compare le cœur DOMINANT à
    ///   ce qu'une répartition idéale donnerait, et on ne parle qu'au-delà d'un écart franc.
    /// </summary>
    internal static class RepartitionCoeurs
    {
        /// <summary>Plafond de cœurs suivis. Au-delà, l'index est ignoré plutôt que de faire
        /// sortir un tableau de ses bornes dans un rappel ETW — où l'exception serait avalée.</summary>
        public const int MaxCoeurs = 256;

        /// <summary>
        /// Part du cœur dominant au-delà de laquelle on parle de concentration.
        ///
        /// Choisi haut volontairement. Sur une machine à seize cœurs, la répartition idéale donne
        /// 6,25 % par cœur ; un dominant à 20 % n'a rien d'anormal — les minuteurs et le cœur de
        /// démarrage tirent naturellement. À 50 %, en revanche, la moitié du travail d'interruption
        /// de la machine passe par un seul cœur, et ça se voit à l'usage.
        /// </summary>
        public const double PartQuiConcentre = 0.50;

        /// <summary>En dessous, le relevé ne prouve rien : quelques centaines d'événements se
        /// répartissent mal par hasard, pas par réglage.</summary>
        public const long MiniPourConclure = 2000;

        public sealed class Etat
        {
            /// <summary>Nombre d'événements (DPC + interruptions) par cœur.</summary>
            public long[] Evenements = new long[0];
            /// <summary>Temps cumulé par cœur, en millisecondes.</summary>
            public double[] Ms = new double[0];
            /// <summary>Durée du relevé. Sans elle, le « % d'un cœur » n'existe pas.</summary>
            public double Secondes;
        }

        // ==================================================================
        //  Analyse — PURE, donc testable sans machine ni session ETW
        // ==================================================================

        /// <summary>PUR : total des événements. Zéro sur un tableau vide ou nul.</summary>
        public static long Total(long[] e)
        {
            if (e == null) return 0;
            long t = 0;
            foreach (long x in e) t += x;
            return t;
        }

        /// <summary>PUR : nombre de cœurs qui ont réellement vu passer quelque chose. Compter
        /// les cases du tableau donnerait le nombre de cœurs de la machine, pas celui des cœurs
        /// qui travaillent — et c'est le second qui nous intéresse.</summary>
        public static int CoeursActifs(long[] e)
        {
            if (e == null) return 0;
            int n = 0;
            foreach (long x in e) if (x > 0) n++;
            return n;
        }

        /// <summary>PUR : part d'un cœur, entre 0 et 1. Zéro si l'index sort des bornes — un
        /// index invalide n'est pas une exception ici, c'est une absence de donnée.</summary>
        public static double Part(long[] e, int coeur)
        {
            long t = Total(e);
            if (t <= 0 || e == null || coeur < 0 || coeur >= e.Length) return 0;
            return (double)e[coeur] / t;
        }

        /// <summary>PUR : l'index du cœur le plus chargé, ou -1 s'il n'y a rien à classer.</summary>
        public static int CoeurDominant(long[] e)
        {
            if (e == null) return -1;
            int meilleur = -1;
            long max = 0;
            for (int i = 0; i < e.Length; i++)
                if (e[i] > max) { max = e[i]; meilleur = i; }
            return meilleur;
        }

        /// <summary>PUR : part du cœur dominant, entre 0 et 1.</summary>
        public static double PartDuDominant(long[] e)
        {
            return Part(e, CoeurDominant(e));
        }

        /// <summary>PUR : ce que donnerait une répartition idéale sur les cœurs actifs.
        /// Sert de repère au lecteur, jamais de seuil d'alerte — voir l'en-tête.</summary>
        public static double PartIdeale(long[] e)
        {
            int n = CoeursActifs(e);
            return n > 0 ? 1.0 / n : 0;
        }

        /// <summary>
        /// PUR : le travail d'interruption est-il concentré sur un cœur ?
        ///
        /// Faux tant qu'on n'a pas assez d'événements pour que la question ait un sens. Répondre
        /// « oui » sur trois cents événements reviendrait à conclure sur du bruit.
        /// </summary>
        public static bool EstConcentre(long[] e)
        {
            return Total(e) >= MiniPourConclure && PartDuDominant(e) >= PartQuiConcentre;
        }

        /// <summary>PUR : part d'un cœur passée en interruptions, entre 0 et 1. C'est la dernière
        /// colonne de LatencyMon : ce que le cœur a effectivement perdu.</summary>
        public static double PartDuCoeurConsommee(double ms, double secondes)
        {
            if (secondes <= 0 || ms <= 0) return 0;
            return ms / (secondes * 1000.0);
        }

        /// <summary>
        /// PUR : le rapport, ou une chaîne vide s'il n'y a rien d'honnête à dire.
        ///
        /// Vide quand le relevé est trop court pour conclure. Un tableau de pourcentages calculé
        /// sur deux cents événements a l'air aussi sérieux qu'un autre, et c'est bien le problème.
        /// </summary>
        public static string Rapport(Etat etat)
        {
            if (etat == null || etat.Evenements == null) return "";
            long total = Total(etat.Evenements);
            if (total < MiniPourConclure) return "";

            int dominant = CoeurDominant(etat.Evenements);
            if (dominant < 0) return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("OÙ TOMBENT LES INTERRUPTIONS");
            sb.AppendLine("   " + total.ToString("#,0") + " événements sur "
                + CoeursActifs(etat.Evenements) + " cœur(s), en "
                + etat.Secondes.ToString("0") + " s.");
            sb.AppendLine();

            // Les cœurs qui comptent, du plus chargé au moins chargé. Lister seize lignes dont
            // douze à 0,1 % ferait perdre celle qui porte l'information.
            int[] ordre = OrdreDecroissant(etat.Evenements);
            int montres = 0;
            double cumul = 0;
            foreach (int c in ordre)
            {
                if (etat.Evenements[c] == 0) break;
                double part = Part(etat.Evenements, c);
                if (montres >= 4 && cumul >= 0.95) break;
                double ms = c < etat.Ms.Length ? etat.Ms[c] : 0;
                sb.AppendLine("   CPU " + c.ToString().PadRight(3)
                    + etat.Evenements[c].ToString("#,0").PadLeft(10)
                    + (part * 100).ToString("0.0").PadLeft(8) + " %"
                    + (PartDuCoeurConsommee(ms, etat.Secondes) * 100).ToString("0.0").PadLeft(9)
                    + " % du cœur");
                cumul += part;
                montres++;
            }

            int restants = CoeursActifs(etat.Evenements) - montres;
            if (restants > 0)
                sb.AppendLine("   … et " + restants + " autre(s) cœur(s), négligeables.");
            sb.AppendLine();

            if (!EstConcentre(etat.Evenements))
            {
                sb.Append(PilotesRefuses.Plie("Réparti : le cœur le plus chargé prend "
                    + (PartDuDominant(etat.Evenements) * 100).ToString("0") + " % du travail, pour "
                    + (PartIdeale(etat.Evenements) * 100).ToString("0")
                    + " % dans une répartition idéale. Rien à corriger de ce côté.", 69, "   "));
                return sb.ToString();
            }

            sb.Append(PilotesRefuses.Plie("CONCENTRÉ SUR LE CŒUR " + dominant + " : il prend "
                + (PartDuDominant(etat.Evenements) * 100).ToString("0.0")
                + " % de tout le travail d'interruption, là où une répartition idéale en donnerait "
                + (PartIdeale(etat.Evenements) * 100).ToString("0") + " %.", 69, "   "));
            sb.AppendLine();
            sb.Append(PilotesRefuses.Plie("CE QUE ÇA NE PROUVE PAS : que ce soit réparable. Un "
                + "périphérique qui n'expose qu'UN SEUL vecteur d'interruption ne peut pas être "
                + "étalé — la politique de répartition le fera migrer de cœur en cœur, chaque DPC "
                + "arrivant sur un cache froid, et ce sera pire. Le cœur 0 reçoit de plus les "
                + "minuteurs du système quoi qu'il arrive.", 69, "   "));
            sb.AppendLine();
            sb.Append(PilotesRefuses.Plie("À VÉRIFIER AVANT DE TOUCHER À QUOI QUE CE SOIT : le "
                + "nombre de vecteurs des gros producteurs d'interruptions (rapport « réglages qui "
                + "coûtent »). S'ils sont à 1, cette concentration est normale et définitive.",
                69, "   "));
            return sb.ToString();
        }

        /// <summary>PUR : les index des cœurs, du plus chargé au moins chargé. Tri par insertion :
        /// le tableau fait au plus quelques centaines d'entrées, et cette version se relit.</summary>
        public static int[] OrdreDecroissant(long[] e)
        {
            if (e == null) return new int[0];
            var idx = new int[e.Length];
            for (int i = 0; i < e.Length; i++) idx[i] = i;
            for (int i = 1; i < idx.Length; i++)
            {
                int cle = idx[i];
                int j = i - 1;
                while (j >= 0 && e[idx[j]] < e[cle]) { idx[j + 1] = idx[j]; j--; }
                idx[j + 1] = cle;
            }
            return idx;
        }
    }
}
