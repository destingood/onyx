using System;
using System.Globalization;

namespace BTOptimizer
{
    /// <summary>
    /// Retrait de l'IA locale à la mise à jour (partie pure, testée). Au premier lancement après
    /// la mise à jour, ONYX efface ses propres fichiers d'IA et désinstalle Ollama avec ses modèles,
    /// mais SEULEMENT si c'est ONYX qui l'avait installé : un Ollama posé par l'utilisateur pour
    /// autre chose reste en place.
    ///
    /// Comment savoir qui l'a installé : aucun fichier ne le note. La case de l'ancien installateur
    /// (bt-ia-consent.txt) ne prouve rien, elle était cochée par défaut et réécrite à chaque mise à
    /// jour. Le signal fiable est l'horloge : avant d'installer Ollama, ONYX créait bt-ia-setup.txt
    /// (compteur d'essais), puis winget posait Ollama dans les minutes suivantes. Un dossier Ollama
    /// créé dans cette fenêtre est celui d'ONYX ; plus ancien, il existait déjà ; bien plus récent,
    /// l'utilisateur l'a installé lui-même plus tard.
    /// </summary>
    internal static partial class RetraitIaLocale
    {
        /// <summary>Fichiers d'IA écrits par ONYX (drapeaux, consentement, compteur, mémoire apprise, index des vecteurs).</summary>
        public static readonly string[] FichiersOnyx =
            { "bt-ia-locale.txt", "bt-ia-off.txt", "bt-ia-setup.txt", "bt-ia-consent.txt", "bt-appris.md", "bt-kb-index.txt" };

        /// <summary>Marqueur du retrait : « fait|bilan » une fois terminé, « echec N » sinon.</summary>
        public const string Marqueur = "bt-ia-retiree.txt";

        private static readonly TimeSpan Avance = TimeSpan.FromMinutes(5);   // horloges et écriture du compteur
        private static readonly TimeSpan Delai = TimeSpan.FromHours(3);     // winget attendait jusqu'à 60 min, plus le téléchargement

        /// <summary>Vrai si le dossier d'Ollama est apparu juste après le premier essai d'installation d'ONYX.</summary>
        public static bool InstalleeParOnyx(DateTime? premierEssaiOnyx, DateTime? creationOllama)
        {
            if (premierEssaiOnyx == null || creationOllama == null) return false;
            DateTime e = premierEssaiOnyx.Value, c = creationOllama.Value;
            return c >= e - Avance && c <= e + Delai;
        }

        /// <summary>Vrai si l'entrée « Ollama » du démarrage de Windows lance l'Ollama de ce dossier (ONYX la posait).</summary>
        public static bool RunDOllama(string valeur, string dossierOllama)
        {
            if (string.IsNullOrWhiteSpace(valeur) || string.IsNullOrWhiteSpace(dossierOllama)) return false;
            string d = dossierOllama.TrimEnd('\\', '/');
            return valeur.IndexOf(d + "\\", StringComparison.OrdinalIgnoreCase) >= 0
                && valeur.IndexOf("ollama app.exe", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Vrai si OLLAMA_KEEP_ALIVE vaut ce qu'ONYX y écrivait (« 30m ») : une autre valeur vient de l'utilisateur.</summary>
        public static bool KeepAliveDOnyx(string valeur)
        {
            return valeur == "30m";
        }

        /// <summary>Nombre d'échecs notés dans le marqueur (« echec N »).</summary>
        public static int Echecs(string marqueur)
        {
            int n;
            if (marqueur != null && marqueur.StartsWith("echec ", StringComparison.Ordinal)
                && int.TryParse(marqueur.Substring(6).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return 0;
        }

        /// <summary>Vrai s'il reste à faire : jamais fait, ou moins de trois échecs.</summary>
        public static bool AFaire(string marqueur)
        {
            if (marqueur == null) return true;
            if (marqueur.StartsWith("fait", StringComparison.Ordinal)) return false;
            return Echecs(marqueur) < 3;
        }

        /// <summary>Phrase du journal une fois le retrait fait.</summary>
        public static string Bilan(bool desinstalle, bool gardeCarUtilisateur, long octetsLiberes)
        {
            if (desinstalle) return "IA locale retirée : Ollama et ses modèles désinstallés (" + Taille(octetsLiberes) + " libérés).";
            if (gardeCarUtilisateur) return "IA locale retirée d'ONYX. Ollama est gardé : tu l'avais installé toi-même.";
            return "IA locale retirée d'ONYX.";
        }

        /// <summary>Taille lisible : « 6,2 Go », ou « 500 Mo » sous le gigaoctet.</summary>
        public static string Taille(long octets)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            if (octets >= 1L << 30) return (octets / (double)(1L << 30)).ToString("0.#", fr) + " Go";
            return Math.Max(0, octets / (1L << 20)).ToString(fr) + " Mo";
        }
    }
}
