using System;

namespace BTOptimizer
{
    /// <summary>
    /// BUFFERBLOAT — « mon ping explose dès que quelqu'un télécharge ».
    ///
    /// Le symptôme est connu de tous les joueurs et presque jamais mesuré. La cause n'est pas la
    /// bande passante : c'est une FILE D'ATTENTE. Quand un transfert sature le lien montant, la box
    /// (ou l'équipement du fournisseur) empile les paquets dans un tampon surdimensionné au lieu
    /// d'en jeter. Chaque paquet de jeu doit alors traverser toute la file — le débit reste bon, le
    /// temps d'aller-retour est ruiné.
    ///
    /// CE QUI SE MESURE, ET COMMENT. La seule mesure qui compte est l'ÉCART entre le ping au repos
    /// et le ping pendant que le lien est saturé. ONYX téléchargeait déjà 25 Mo pour son test de
    /// débit, et mesurait la latence AVANT — jamais PENDANT. Le chiffre manquait donc de trois
    /// lignes de code, pas d'un serveur ni d'un outil tiers.
    ///
    /// CE QU'IL FAUT DIRE HONNÊTEMENT QUAND C'EST MAUVAIS. Le tampon fautif est dans la box ou chez
    /// le fournisseur, PAS dans Windows. Aucun réglage de la machine ne peut vider une file d'attente
    /// qui vit dans un autre appareil. Les guides qui promettent de corriger le bufferbloat avec
    /// netsh vendent du vent — et le seul remède réel (un routeur qui fait du SQM : CAKE, fq_codel)
    /// n'est pas un réglage Windows. ONYX mesure et le dit ; il ne prétend pas le réparer.
    ///
    /// L'échelle reprend celle du test de référence Waveform, pour que les résultats soient
    /// comparables à ce que l'utilisateur trouvera ailleurs.
    /// </summary>
    internal static class Bufferbloat
    {
        /// <summary>Note PURE à partir de la HAUSSE de latence sous charge, en millisecondes.
        /// Chaîne vide si la mesure est inexploitable — on n'invente pas une note.</summary>
        public static string Note(double hausseMs)
        {
            if (double.IsNaN(hausseMs)) return "";
            if (hausseMs < 5) return "A+";
            if (hausseMs < 30) return "A";
            if (hausseMs < 60) return "B";
            if (hausseMs < 200) return "C";
            if (hausseMs < 400) return "D";
            return "F";
        }

        /// <summary>Hausse PURE : ce que la charge ajoute au ping. NaN si l'une des deux mesures
        /// manque. Une hausse négative (le réseau était plus calme pendant le test) est ramenée à 0
        /// plutôt que présentée comme un gain — ce serait du bruit de mesure, pas un progrès.</summary>
        public static double Hausse(double reposMs, double chargeMs)
        {
            if (double.IsNaN(reposMs) || double.IsNaN(chargeMs)) return double.NaN;
            double d = chargeMs - reposMs;
            return d < 0 ? 0 : d;
        }

        /// <summary>true si la note mérite d'être signalée comme un problème.</summary>
        public static bool EstProblematique(string note)
        {
            return note == "C" || note == "D" || note == "F";
        }

        /// <summary>
        /// Verdict PUR, en toutes lettres. null si rien de mesurable.
        /// </summary>
        public static string Verdict(double reposMs, double chargeMs)
        {
            double h = Hausse(reposMs, chargeMs);
            if (double.IsNaN(h)) return null;
            string note = Note(h);

            string chiffres = "Ping au repos " + reposMs.ToString("0") + " ms, sous charge "
                            + chargeMs.ToString("0") + " ms — soit +" + h.ToString("0")
                            + " ms. Note " + note + ".";

            if (note == "A+" || note == "A")
                return chiffres + " Ta connexion tient sous charge : quelqu'un peut télécharger "
                     + "pendant que tu joues sans que ça se voie.";

            if (note == "B")
                return chiffres + " C'est correct sans être irréprochable : une saturation longue "
                     + "peut se sentir sur un jeu nerveux.";

            return chiffres + " C'est du bufferbloat net : dès que la connexion est saturée, tes "
                 + "paquets font la queue. AUCUN réglage de Windows ne corrige ça — la file "
                 + "d'attente est dans ta box, pas dans ton PC. Le seul remède réel est un routeur "
                 + "qui gère la file (SQM : CAKE ou fq_codel), ou la limitation de débit montant "
                 + "proposée par certaines box.";
        }
    }
}
