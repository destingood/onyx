using System;
using System.Threading.Tasks;

namespace BTOptimizer
{
    /// <summary>
    /// LE RELEVÉ D'<see cref="AutoJeu"/> — la seule moitié qui a besoin d'une machine.
    ///
    /// La décision vit dans AutoJeu.cs et ne connaît ni processus, ni écran, ni service : c'est ce
    /// qui permet de rejouer un alt-tab de six secondes au banc d'essai en trois appels, au lieu
    /// de lancer un jeu vingt fois pour espérer voir le défaut.
    /// </summary>
    internal static partial class AutoJeu
    {
        /// <summary>Un geste est en cours. Le drapeau vit ICI, avec le seul code qui s'en sert :
        /// déclaré du côté décision, il y devenait un champ inutilisé au banc d'essai.</summary>
        private static bool _occupe;

        /// <summary>Vrai si c'est CE module qui a enclenché le Mode Jeu en cours.</summary>
        public static bool EngageParNous { get { return _etat.ParNous && GameBoost.IsActive; } }

        /// <summary>
        /// Un relevé. À appeler toutes les 2 s depuis le minuteur d'un shell ; les deux peuvent
        /// l'appeler, l'état est commun et l'exécution protégée.
        ///
        /// Le geste part sur un fil de fond : arrêter huit services prend plusieurs secondes, et
        /// figer l'interface pendant qu'un jeu démarre serait le pire moment pour le faire.
        /// <paramref name="apres"/> est rappelé sur ce fil-là, une fois le geste terminé.
        /// </summary>
        public static void Tick(Action<string, int> log, Action apres)
        {
            if (!Actif || _occupe) return;

            bool jeuConnu = false, pleinEcran = false;
            string nom = null;
            try { nom = GameScan.RunningKnownGame(); jeuConnu = nom != null; }
            catch (Exception ex) { JournalTechnique.Echec("AutoJeu.Tick/jeu", ex); }
            try { pleinEcran = Native.IsGameFullscreen(); }
            catch (Exception ex) { JournalTechnique.Echec("AutoJeu.Tick/pleinEcran", ex); }

            _etat.BoostActif = GameBoost.IsActive;
            Geste g = Decide(_etat, jeuConnu, pleinEcran);
            if (g == Geste.Rien) return;

            string raison = g == Geste.Engager
                ? (jeuConnu ? "jeu détecté (" + nom + ")" : "application en plein écran depuis " + (TicksPourEngager * 2) + " s")
                : "plus aucun jeu depuis " + (TicksPourRelacher * 2) + " s";

            _occupe = true;
            Task.Run(delegate
            {
                try
                {
                    if (g == Geste.Engager)
                    {
                        // LE NOM DU JEU EST PASSÉ, ET CE N'EST PAS UN DÉTAIL. Depuis que le Mode
                        // Jeu ferme aussi des applications de fond, l'appeler sans dire QUI a
                        // déclenché reviendrait à laisser ONYX fermer le jeu qu'il vient de
                        // détecter. En plein écran sans processus reconnu, il n'y a rien à
                        // protéger — et rien de listé ne ressemble à un jeu.
                        GameBoost.Activate(log, nom);
                        _etat.ParNous = true;
                        if (log != null) log("MODE JEU AUTO : " + raison + " → services de fond suspendus.", 1);
                    }
                    else
                    {
                        GameBoost.Deactivate(log);
                        _etat.ParNous = false;
                        if (log != null) log("MODE JEU AUTO : " + raison + " → services de fond relancés.", 0);
                    }
                }
                catch (Exception ex) { JournalTechnique.Echec("AutoJeu.Tick/geste", ex); }
                finally
                {
                    _occupe = false;
                    try { if (apres != null) apres(); } catch { }
                }
            });
        }

    }
}
