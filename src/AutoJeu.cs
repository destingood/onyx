using System;

namespace BTOptimizer
{
    /// <summary>
    /// SUSPENDRE TOUT SEUL QUAND UN JEU DÉMARRE, ET REMETTRE QUAND IL SE FERME.
    ///
    /// Un embryon existait déjà, mais uniquement dans l'ancienne fenêtre (MainForm), derrière une
    /// case à cocher qu'il fallait trouver — et sa logique tenait dans une méthode de formulaire,
    /// donc invérifiable autrement qu'en lançant un jeu. L'interface actuelle (le shell ONYX)
    /// n'avait, elle, AUCUNE bascule automatique : qui utilise ONYX aujourd'hui n'avait pas la
    /// fonction du tout.
    ///
    /// LE DÉFAUT DE FOND ÉTAIT AILLEURS, ET IL SE VOYAIT EN PARTIE :
    ///
    ///   L'ancienne règle enclenchait après DEUX relevés stables, mais relâchait au PREMIER relevé
    ///   sans jeu. Or « sans jeu » se produit en pleine partie : un alt-tab vers le bureau, un
    ///   écran de chargement qui sort du plein écran, un menu en fenêtré. Le résultat était le pire
    ///   possible — tous les services redémarraient AU MILIEU de la partie, puis se faisaient
    ///   arrêter de nouveau deux secondes plus tard. Un service d'indexation relancé pendant un
    ///   match coûte exactement ce que le mode jeu prétendait faire gagner.
    ///
    ///   Les deux seuils ne sont donc PAS symétriques, et c'est volontaire : se tromper en tenant
    ///   trop longtemps coûte quelques secondes de service suspendu devant un bureau ; se tromper
    ///   en relâchant trop tôt coûte une micro-saccade en plein jeu. On tient.
    ///
    /// CE QU'IL NE FAIT JAMAIS :
    ///
    ///   Couper un Mode Jeu que l'utilisateur a activé LUI-MÊME. Ce module ne relâche que ce qu'il
    ///   a enclenché ; une activation manuelle lui survit, et c'est à la personne de la couper.
    ///
    ///   S'activer sans qu'on le lui ait demandé. Le réglage est OPT-IN et persistant : ONYX ne
    ///   suspend pas des services de fond parce qu'il a cru bien faire.
    /// </summary>
    internal static partial class AutoJeu
    {
        /// <summary>Relevés consécutifs avec jeu avant d'enclencher. Les deux shells appellent
        /// <see cref="Tick"/> toutes les 2 s : deux relevés ≈ 4 secondes.</summary>
        public const int TicksPourEngager = 2;

        /// <summary>Relevés consécutifs SANS jeu avant de relâcher — volontairement plus haut.
        /// Voir l'en-tête : un alt-tab de dix secondes ne doit pas relancer l'indexation du disque
        /// au milieu d'une partie.</summary>
        public const int TicksPourRelacher = 8;

        /// <summary>Ce que la machine renvoie, et ce que le module se rappelle entre deux relevés.
        /// Un objet séparé pour que la décision reste vérifiable sans lancer de jeu.</summary>
        public sealed class Etat
        {
            /// <summary>Le Mode Jeu tourne-t-il, quelle qu'en soit la cause ?</summary>
            public bool BoostActif;
            /// <summary>… et est-ce NOUS qui l'avons enclenché ? Sinon, on n'y touche pas.</summary>
            public bool ParNous;
            public int TicksAvecJeu;
            public int TicksSansJeu;
        }

        public enum Geste { Rien, Engager, Relacher }

        /// <summary>
        /// DÉCISION PURE. Met à jour les compteurs de <paramref name="e"/> et dit quoi faire.
        ///
        /// <paramref name="jeuConnu"/> : un processus de jeu identifié par son nom. Ces noms sont
        /// choisis distinctifs (GameScan.PriorityExes) — aucun faux positif possible, donc on
        /// enclenche SANS attendre : les premières secondes d'un chargement sont justement celles
        /// où le disque travaille le plus.
        ///
        /// <paramref name="pleinEcran"/> : une application couvre l'écran. C'est le repli pour les
        /// jeux non listés, et il peut se tromper — une vidéo en plein écran en est une. Lui seul
        /// exige donc la stabilité.
        /// </summary>
        public static Geste Decide(Etat e, bool jeuConnu, bool pleinEcran)
        {
            if (e == null) return Geste.Rien;

            bool presence = jeuConnu || pleinEcran;
            if (presence) { e.TicksAvecJeu++; e.TicksSansJeu = 0; }
            else { e.TicksSansJeu++; e.TicksAvecJeu = 0; }

            if (!e.BoostActif)
                return (jeuConnu || e.TicksAvecJeu >= TicksPourEngager) ? Geste.Engager : Geste.Rien;

            // Le Mode Jeu tourne. On ne relâche que ce qu'on a enclenché soi-même, et seulement
            // après une absence FRANCHE — voir l'en-tête sur l'asymétrie des deux seuils.
            if (!e.ParNous) return Geste.Rien;
            return e.TicksSansJeu >= TicksPourRelacher ? Geste.Relacher : Geste.Rien;
        }

        // ==================================================================
        //  Réglage persistant
        // ==================================================================

        private static string Chemin { get { return AppPaths.File("bt-autojeu.txt"); } }
        private static bool? _actif;

        /// <summary>
        /// OPT-IN, et persistant d'un lancement à l'autre. Défaut : NON.
        ///
        /// Suspendre des services de fond est un geste, même réversible, même temporaire. Le
        /// CHANGELOG promet que rien n'est modifié sans l'action de l'utilisateur ; une bascule
        /// automatique activée par défaut romprait cette promesse le premier jour.
        /// </summary>
        public static bool Actif
        {
            get
            {
                if (_actif.HasValue) return _actif.Value;
                try { _actif = System.IO.File.Exists(Chemin) && System.IO.File.ReadAllText(Chemin).Trim() == "1"; }
                catch (Exception ex) { JournalTechnique.Echec("AutoJeu.Actif", ex); _actif = false; }
                return _actif.Value;
            }
            set
            {
                _actif = value;
                try { System.IO.File.WriteAllText(Chemin, value ? "1" : "0"); }
                catch (Exception ex) { JournalTechnique.Echec("AutoJeu.Actif=", ex); }
            }
        }

        // ==================================================================
        //  Le relevé — la seule partie qui a besoin d'une machine
        // ==================================================================

        private static readonly Etat _etat = new Etat();

        /// <summary>État partagé par les deux shells : la bascule ne doit pas exister en double.</summary>
        public static Etat EtatCourant { get { return _etat; } }

        /// <summary>
        /// L'utilisateur vient de basculer le Mode Jeu À LA MAIN : l'automatique cesse d'en être
        /// propriétaire et ne le coupera donc plus tout seul. Sans ça, quelqu'un qui active le Mode
        /// Jeu exprès devant son bureau le verrait s'éteindre seize secondes plus tard, sans
        /// comprendre pourquoi.
        /// </summary>
        public static void Desapproprie() { _etat.ParNous = false; }

    }
}
