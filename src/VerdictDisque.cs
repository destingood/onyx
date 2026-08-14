using System;

namespace BTOptimizer
{
    /// <summary>
    /// CE QUE LE BENCHMARK DISQUE PEUT AFFIRMER.
    ///
    /// Deux défauts corrigés ici, tous deux du même genre : conclure au lieu de lire.
    ///
    /// 1) « Disque système lent — c'est un disque dur mécanique » était affirmé à partir d'un seul
    ///    débit séquentiel sous 150 Mo/s. Or un SSD SATA occupé, un NVMe presque plein ou en
    ///    limitation thermique descendent sous ce seuil sans cesser d'être des SSD. L'utilisateur
    ///    se voyait alors conseiller d'acheter le disque qu'il possédait déjà.
    ///
    ///    Windows connaît pourtant la réponse : MSFT_PhysicalDisk.MediaType vaut 3 pour un disque
    ///    mécanique et 4 pour un SSD, et ONYX sait déjà le lire (Diagnostics.DriveTypes). On lit
    ///    donc, et le débit ne sert plus qu'à qualifier une LENTEUR, pas à deviner un matériel.
    ///
    /// 2) Quand la mesure échouait, DiskBench laissait ses deux valeurs à -1 — et l'affichage les
    ///    imprimait telles quelles : « écriture -1 Mo/s · lecture -1 Mo/s », un échec présenté
    ///    comme un résultat.
    ///
    /// À noter : le média n'est pas toujours lisible (les boîtiers USB rendent un type inconnu, ce
    /// qui est vérifié sur cette machine). Dans ce cas on formule une SUPPOSITION, en le disant.
    /// </summary>
    internal static class VerdictDisque
    {
        public const int MediaHdd = 3;
        public const int MediaSsd = 4;

        /// <summary>Sous ce débit séquentiel, quelque chose ne va pas — reste à savoir quoi.</summary>
        public const double SeuilLentMBs = 150;

        /// <summary>PUR : un débit à afficher. Les valeurs négatives sont des SENTINELLES d'échec,
        /// pas des mesures : elles ne doivent jamais atterrir à l'écran telles quelles.</summary>
        public static string Debit(double mbs)
        {
            if (mbs < 0 || double.IsNaN(mbs)) return "n/d";
            return mbs.ToString("N0") + " Mo/s";
        }

        /// <summary>PUR : la mesure a-t-elle abouti ?</summary>
        public static bool Mesure(double mbs)
        {
            return mbs >= 0 && !double.IsNaN(mbs);
        }

        /// <summary>
        /// PUR : le verdict sur le disque mesuré. Rend null quand il n'y a rien à signaler.
        ///
        /// <paramref name="mediaType"/> : 3 = mécanique, 4 = SSD, toute autre valeur = inconnu.
        /// </summary>
        public static string Media(int mediaType, double lectureMBs, string lecteur)
        {
            if (!Mesure(lectureMBs)) return null;
            string ou = string.IsNullOrEmpty(lecteur) ? "Le disque mesuré" : "Le disque " + lecteur;
            bool lent = lectureMBs < SeuilLentMBs;

            if (mediaType == MediaHdd)
                return ou + " est un disque MÉCANIQUE (Windows le confirme). Passer Windows et tes jeux "
                     + "sur un SSD est le plus gros gain de réactivité possible.";

            if (mediaType == MediaSsd)
                return lent
                    ? ou + " est bien un SSD, mais il ne rend que " + lectureMBs.ToString("0")
                         + " Mo/s en lecture séquentielle — c'est anormalement bas. Un SSD presque plein, "
                         + "en limitation thermique, ou occupé par autre chose pendant la mesure donne ce "
                         + "résultat. Inutile d'en changer : cherche d'abord la cause."
                    : null;

            // Média inconnu (boîtier USB, contrôleur exotique) : on suppose, et on le dit.
            return lent
                ? ou + " ne rend que " + lectureMBs.ToString("0") + " Mo/s. Windows n'indique pas son type, "
                     + "donc impossible d'affirmer : ce débit ÉVOQUE un disque mécanique, mais un SSD très "
                     + "plein ou occupé donne le même chiffre."
                : null;
        }
    }
}
