using System;
using System.Collections.Generic;

namespace BTOptimizer
{
    /// <summary>
    /// MODE JEU — règles pures (testées au banc).
    ///
    /// Elles viennent de mesures faites le 25/11/2026 sur un PC de 32 Go à quatre fichiers
    /// d'échange. À chaque enclenchement, le Mode Jeu vidait la mémoire de TOUS les processus alors
    /// que 18 Go étaient libres : les pages vidées repartaient dans les fichiers d'échange
    /// (46 Mo/s écrits sur les quatre disques, relevés 34 s après), puis revenaient par fautes de
    /// page — en pleine partie. Et Windows Search, arrêté, était relancé par Windows 9 s plus
    /// tard, l'indexeur repartant d'un journal de volume neuf (58 fois en 24 h).
    /// </summary>
    internal static class ModeJeuRegles
    {
        /// <summary>En dessous de cette part de mémoire disponible, vider les processus de fond
        /// rend de la place au jeu. Au-dessus, c'est du disque et des fautes de page pour rien.
        /// 20 % est un choix prudent, pas une valeur mesurée.</summary>
        public const int SeuilMemoirePourcent = 20;

        /// <summary>PUR : la mémoire disponible est-elle assez basse pour justifier un vidage ?
        /// Total inconnu (0) : on ne vide rien.</summary>
        public static bool MemoireACourt(long disponibleMo, long totalMo)
        {
            if (totalMo <= 0) return false;
            return disponibleMo * 100 < totalMo * SeuilMemoirePourcent;
        }

        /// <summary>
        /// Services que le Mode Jeu ne suspend JAMAIS, même s'ils figurent dans une liste écrite
        /// sur le disque par une version précédente.
        ///
        /// WSearch : Windows le relance dès qu'on l'interroge ; l'arrêt ne tient pas et chaque
        /// redémarrage relance l'indexation. Qui veut s'en passer le désactive (réglage dédié).
        /// NvContainerLocalSystem : il porte la superposition, l'enregistrement et l'application
        /// NVIDIA — voir SondesMaterielles.ServicesSondes, qui l'avait déjà écarté.
        /// </summary>
        private static readonly HashSet<string> JamaisSuspendus =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WSearch", "NvContainerLocalSystem" };

        /// <summary>PUR : ce service est-il hors de portée du Mode Jeu ?</summary>
        public static bool JamaisSuspendu(string service)
        {
            return service != null && JamaisSuspendus.Contains(service.Trim());
        }

        /// <summary>PUR : la liste des candidats, sans les services jamais suspendus, ordre gardé.</summary>
        public static string[] Suspendables(IEnumerable<string> candidats)
        {
            var l = new List<string>();
            if (candidats != null)
                foreach (string s in candidats)
                    if (!string.IsNullOrEmpty(s) && !JamaisSuspendu(s)) l.Add(s);
            return l.ToArray();
        }

        /// <summary>Processus principaux des lanceurs dont la priorité peut être relevée pendant la
        /// partie (option, coupée par défaut). Leurs sous-processus (navigateur intégré…) restent tels quels.</summary>
        public static readonly string[] Lanceurs = { "steam", "EpicGamesLauncher", "EADesktop", "Battle.net", "upc", "GalaxyClient" };

        public const int PrioriteNormale = 0x20, PrioriteAuDessusNormale = 0x8000;

        public static bool EstLanceur(string nom)
        {
            if (string.IsNullOrEmpty(nom)) return false;
            foreach (string l in Lanceurs) if (string.Equals(l, nom, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>PUR : nouvelle priorité d'un lanceur, ou -1 pour n'y rien changer. Seule la priorité
        /// normale est relevée, d'un cran : une autre valeur a été choisie par quelqu'un (l'utilisateur,
        /// le mode Efficacité), on la respecte. Jamais « haute » ni « temps réel » : le lanceur
        /// passerait devant le jeu.</summary>
        public static int PrioriteLanceur(int actuelle)
        {
            return actuelle == PrioriteNormale ? PrioriteAuDessusNormale : -1;
        }
    }
}
