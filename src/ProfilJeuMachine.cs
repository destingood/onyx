using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace BTOptimizer
{
    /// <summary>
    /// LE RELEVÉ ET LE GESTE du profil par jeu — la moitié qui touche la machine. La décision vit
    /// dans ProfilJeu.cs (pure, testée au banc).
    ///
    /// Appelé par les deux fenêtres (tableau de bord et fenêtre principale) : l'état est statique,
    /// protégé par un verrou, et un seul geste tourne à la fois.
    ///
    /// L'état est écrit sur le disque AVANT de basculer le plan d'alimentation : si ONYX se ferme
    /// ou plante pendant la partie, <see cref="Soigne"/> rétablit le plan d'origine au lancement
    /// suivant au lieu de laisser la machine en Performances pour toujours.
    /// </summary>
    internal static partial class ProfilJeu
    {
        public const string FichierEtat = "bt-profiljeu-etat.txt";

        private static readonly object _verrou = new object();
        private static bool _occupe;
        private static Etat _etat;   // jeu profilé en cours (null : aucun)

        private static string CheminEtat { get { return AppPaths.File(FichierEtat); } }

        /// <summary>Non bloquant : sort tout de suite si un geste est en cours.</summary>
        public static void Tick(Action<string, int> log)
        {
            lock (_verrou)
            {
                if (_occupe) return;
                _occupe = true;
            }
            Task.Run(delegate
            {
                try { Tour(log); }
                catch (Exception ex) { JournalTechnique.Echec("ProfilJeu.Tick", ex); }
                finally { lock (_verrou) _occupe = false; }
            });
        }

        private static void Tour(Action<string, int> log)
        {
            Dictionary<string, Tuple<string, long>> profils = Sys.LoadGameAffinity();
            string detecte = null;
            List<Process> procs = null;
            foreach (var kvp in profils)
            {
                List<Process> p = Processus(kvp.Key);
                if (p.Count > 0) { detecte = kvp.Key; procs = p; break; }
            }

            string enCours;
            lock (_verrou) enCours = _etat == null ? null : _etat.Jeu;

            switch (Decide(enCours, detecte))
            {
                case Geste.Appliquer: Applique(detecte, profils[detecte], procs, log); break;
                case Geste.Retirer: Retire(log); break;
            }
        }

        /// <summary>Tous les processus vivants des exécutables du jeu (un lanceur + le jeu, par ex.).</summary>
        private static List<Process> Processus(string jeu)
        {
            var l = new List<Process>();
            string[] exes = GameScan.ExesFor(jeu);
            if (exes == null) return l;
            foreach (string exe in exes)
            {
                try { l.AddRange(Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe))); } catch { }
            }
            return l;
        }

        private static void Applique(string jeu, Tuple<string, long> profil, List<Process> procs, Action<string, int> log)
        {
            List<int> preferes = new List<int>();
            if (string.Equals(profil.Item1, "Auto", StringComparison.OrdinalIgnoreCase))
                preferes = Topologie.Preferes(Topologie.Lit());
            long masque;
            Mode cible = Cible(profil.Item1, profil.Item2, preferes, out masque);

            int poses = 0;
            foreach (Process p in procs)
            {
                int pid;
                try { pid = p.Id; } catch { continue; }
                if (cible == Mode.CpuSets) { if (Topologie.AppliqueCpuSets(pid, preferes, log)) poses++; }
                else if (cible == Mode.Affinite) { Sys.SetProcessAffinity(pid, masque); poses++; }
                Sys.SetProcessPriority(pid, ProcessPriorityClass.High);
            }

            // État écrit AVANT la bascule : un plantage entre les deux laisse au pire un fichier
            // qui rétablit un plan déjà en place, jamais un plan Performances orphelin.
            string planActif = Sys.GetActivePowerProfile();
            bool bascule = DoitBasculer(planActif);
            var e = new Etat { Jeu = jeu, PlanOrigine = bascule ? planActif : null };
            EcritFichier(e);
            lock (_verrou) _etat = e;
            if (bascule) Sys.SetActivePowerProfile(PlanPerformances);

            string coeurs = cible == Mode.CpuSets ? "cœurs préférés " + Topologie.Libelle(Topologie.Lit())
                          : cible == Mode.Affinite ? "affinité manuelle"
                          : "cœurs laissés à Windows";
            if (log != null)
                log(jeu + " détecté : " + coeurs + " (" + poses + "/" + procs.Count + " processus), priorité Haute"
                    + (bascule ? ", plan Performances." : ", plan inchangé."), 1);
        }

        private static void Retire(Action<string, int> log)
        {
            Etat e;
            lock (_verrou) { e = _etat; _etat = null; }
            if (e == null) return;
            if (e.PlanOrigine != null) Sys.SetActivePowerProfile(e.PlanOrigine);
            EffaceFichier();
            if (log != null)
                log(e.Jeu + " fermé" + (e.PlanOrigine != null ? " : plan d'alimentation d'origine rétabli." : "."), 0);
        }

        /// <summary>
        /// Au lancement : un état resté sur le disque veut dire qu'ONYX s'est fermé pendant une
        /// partie. On rétablit le plan d'origine et on efface l'état ; si le jeu tourne encore,
        /// le prochain Tick le reprofilera depuis un état propre.
        /// </summary>
        public static void Soigne(Action<string, int> log)
        {
            Etat e = null;
            try { if (File.Exists(CheminEtat)) e = LitEtat(File.ReadAllText(CheminEtat)); }
            catch (Exception ex) { JournalTechnique.Echec("ProfilJeu.Soigne/lecture", ex); }
            if (e != null && e.PlanOrigine != null)
            {
                Sys.SetActivePowerProfile(e.PlanOrigine);
                if (log != null) log("Profil de " + e.Jeu + " resté actif après une fermeture d'ONYX : plan d'alimentation d'origine rétabli.", 1);
            }
            EffaceFichier();
            lock (_verrou) _etat = null;
        }

        private static void EcritFichier(Etat e)
        {
            try { File.WriteAllText(CheminEtat, EcritEtat(e), new UTF8Encoding(false)); }
            catch (Exception ex) { JournalTechnique.Echec("ProfilJeu.EcritFichier", ex); }
        }

        private static void EffaceFichier()
        {
            try { if (File.Exists(CheminEtat)) File.Delete(CheminEtat); } catch { }
        }
    }
}
