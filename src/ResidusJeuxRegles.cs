using System;
using System.Collections.Generic;

namespace BTOptimizer
{
    /// <summary>
    /// Résidus des jeux proposés au nettoyage (liste courte, à part des caches : jamais vidés par une
    /// routine, jamais cochés d'avance, envoyés à la Corbeille) :
    /// - jeux Unreal Engine : %LOCALAPPDATA%\&lt;Jeu&gt;\Saved\Logs (journaux) et \Saved\Crashes
    ///   (rapports de plantage), recréés par le jeu ; jamais Saved\Config ni Saved\SaveGames ;
    /// - Steam : dossier « dumps » (vidages de plantage du client).
    /// Un dossier n'est retenu comme jeu Unreal que s'il a la forme du moteur (Saved\Config\Windows…).
    /// Partie logique, sans machine : testée par le banc.
    /// </summary>
    internal static class ResidusJeuxRegles
    {
        /// <summary>Sous-dossiers de « Saved » vidables. Rien d'autre.</summary>
        public static readonly string[] SousDossiers = { "Logs", "Crashes" };

        // Dossier de configuration d'Unreal Engine sous Windows (nom selon la version du moteur).
        private static readonly string[] ConfigUnreal = { "Windows", "WindowsNoEditor", "WindowsClient" };

        // Noms de dossier → nom du jeu, quand le dossier ne le dit pas.
        private static readonly Dictionary<string, string> Noms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "FortniteGame", "Fortnite" }, { "EpicGamesLauncher", "Epic Games Launcher" }, { "Pal", "Palworld" },
        };

        /// <summary>Le dossier « Saved\Config » contient-il un sous-dossier propre à Unreal Engine ?</summary>
        public static bool EstUnreal(IEnumerable<string> sousDossiersConfig)
        {
            if (sousDossiersConfig == null) return false;
            foreach (string d in sousDossiersConfig)
                foreach (string u in ConfigUnreal)
                    if (string.Equals(d, u, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Dossiers retenus par leur nom, sans la forme « jeu » : le lanceur Epic (configuration « WindowsEditor »).
        /// Les éditeurs (UEFN, Unreal Editor) restent hors liste : leurs journaux servent au créateur.
        /// </summary>
        public static bool RetenuParNom(string dossier)
        {
            return string.Equals(dossier, "EpicGamesLauncher", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>« FortniteGame » → « Fortnite » ; « MonJeuGame » → « MonJeu » ; sinon le nom du dossier.</summary>
        public static string NomJeu(string dossier)
        {
            string n;
            if (string.IsNullOrEmpty(dossier)) return "";
            if (Noms.TryGetValue(dossier, out n)) return n;
            return dossier.Length > 6 && dossier.EndsWith("Game", StringComparison.Ordinal) ? dossier.Substring(0, dossier.Length - 4) : dossier;
        }

        public static string Libelle(string jeu, string sousDossier)
        {
            return jeu + " — " + (sousDossier == "Crashes" ? "rapports de plantage" : "journaux");
        }
    }
}
