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
    /// protégé par un verrou, et un seul tour tourne à la fois.
    ///
    /// L'état est écrit sur le disque AVANT de basculer le plan d'alimentation. Le PREMIER tour
    /// après le lancement d'ONYX le relit : même jeu encore en cours → l'état est adopté tel quel
    /// (plan d'origine compris) ; sinon le plan d'origine est rétabli. Fermer ONYX normalement
    /// rétablit le plan tout de suite (<see cref="Arrete"/>).
    /// </summary>
    internal static partial class ProfilJeu
    {
        public const string FichierEtat = "bt-profiljeu-etat.txt";

        private static readonly object _verrou = new object();
        private static bool _occupe, _repris;
        private static Etat _etat;   // jeu profilé en cours (null : aucun)

        private static string CheminEtat { get { return AppPaths.File(FichierEtat); } }

        /// <summary>Non bloquant : sort tout de suite si un tour est en cours.</summary>
        public static void Tick(Action<string, int> log)
        {
            lock (_verrou)
            {
                if (_occupe) return;
                _occupe = true;
            }
            Action<string, int> journal = Journal(log);
            Task.Run(delegate
            {
                try { Tour(journal); }
                catch (Exception ex) { JournalTechnique.Echec("ProfilJeu.Tick", ex); }
                finally { lock (_verrou) _occupe = false; }
            });
        }

        /// <summary>
        /// Les avertissements et erreurs vont AUSSI au journal technique : le tableau de bord n'a
        /// pas de journal à l'écran, et un refus d'anti-triche doit rester retrouvable.
        /// </summary>
        private static Action<string, int> Journal(Action<string, int> log)
        {
            return (m, niveau) =>
            {
                if (niveau >= 2) JournalTechnique.Echec("ProfilJeu", m);
                if (log != null) { try { log(m, niveau); } catch { } }
            };
        }

        private static void Tour(Action<string, int> log)
        {
            Dictionary<string, Tuple<string, long>> profils = Sys.LoadGameAffinity();
            var exes = new List<KeyValuePair<string, string[]>>();
            foreach (string jeu in profils.Keys) exes.Add(new KeyValuePair<string, string[]>(jeu, GameScan.ExesFor(jeu)));
            string detecte = JeuDetecte(exes, NomsProcessus());

            if (!_repris) { _repris = true; Reprend(detecte, log); }

            string enCours;
            lock (_verrou) enCours = _etat == null ? null : _etat.Jeu;

            switch (Decide(enCours, detecte))
            {
                case Geste.Appliquer: Applique(detecte, profils[detecte], log); break;
                case Geste.Retirer: Retire(log); break;
            }
        }

        /// <summary>UN relevé des processus par tour (et non un par exécutable de chaque profil).</summary>
        private static HashSet<string> NomsProcessus()
        {
            var noms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Process[] tous = new Process[0];
            try { tous = Process.GetProcesses(); } catch { }
            foreach (Process p in tous)
            {
                try { noms.Add(p.ProcessName); } catch { }
                p.Dispose();
            }
            return noms;
        }

        private static void Reprend(string detecte, Action<string, int> log)
        {
            Etat f = null;
            try { if (File.Exists(CheminEtat)) f = LitEtat(File.ReadAllText(CheminEtat)); }
            catch (Exception ex) { JournalTechnique.Echec("ProfilJeu.Reprend/lecture", ex); }

            switch (Reprise(f, detecte))
            {
                case Reprendre.Adopter:
                    // Les CPU Sets et la priorité vivent avec le processus : ils sont encore posés.
                    lock (_verrou) _etat = f;
                    log("Profil de " + f.Jeu + " repris après le redémarrage d'ONYX.", 0);
                    break;
                case Reprendre.Retablir:
                    if (f.PlanOrigine != null)
                    {
                        Sys.SetActivePowerProfile(f.PlanOrigine);
                        log("Profil de " + f.Jeu + " resté actif après une fermeture d'ONYX : plan d'alimentation d'origine rétabli.", 1);
                    }
                    EffaceFichier();
                    break;
            }
        }

        private static void Applique(string jeu, Tuple<string, long> profil, Action<string, int> log)
        {
            List<Process> procs = Processus(jeu);
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
                p.Dispose();
            }

            // État écrit AVANT la bascule : un plantage entre les deux laisse au pire un fichier
            // qui rétablit un plan déjà en place, jamais un plan Performances orphelin.
            string planActif = Sys.GetActivePowerProfile();
            bool bascule = DoitBasculer(planActif);
            var e = new Etat { Jeu = jeu, PlanOrigine = bascule ? planActif : null };
            EcritFichier(e);
            lock (_verrou) _etat = e;
            if (bascule)
            {
                Sys.SetActivePowerProfile(PlanPerformances);
                // Le plan n'existe pas partout (portables en veille moderne) et powercfg échoue sans
                // bruit : on relit pour ne pas annoncer une bascule qui n'a pas eu lieu.
                if (!string.Equals(Sys.GetActivePowerProfile(), PlanPerformances, StringComparison.OrdinalIgnoreCase))
                {
                    bascule = false;
                    e.PlanOrigine = null;
                    EcritFichier(e);
                    log("Plan « Performances élevées » indisponible sur ce PC : plan d'alimentation inchangé.", 2);
                }
            }

            string coeurs = cible == Mode.CpuSets ? "cœurs préférés " + Topologie.Libelle(Topologie.Lit())
                          : cible == Mode.Affinite ? "affinité manuelle"
                          : "cœurs laissés à Windows";
            log(jeu + " détecté : " + coeurs + " (" + poses + "/" + procs.Count + " processus), priorité Haute"
                + (bascule ? ", plan Performances." : ", plan inchangé."), 1);
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

        private static void Retire(Action<string, int> log)
        {
            Etat e;
            lock (_verrou) { e = _etat; _etat = null; }
            if (e == null) return;
            if (e.PlanOrigine != null) Sys.SetActivePowerProfile(e.PlanOrigine);
            EffaceFichier();
            log(e.Jeu + " fermé" + (e.PlanOrigine != null ? " : plan d'alimentation d'origine rétabli." : "."), 0);
        }

        /// <summary>
        /// Fermeture d'ONYX : rétablit le plan d'origine tout de suite, au lieu d'attendre le
        /// lancement suivant. Synchrone (appelé pendant la fermeture de la fenêtre).
        /// </summary>
        public static void Arrete(Action<string, int> log)
        {
            try { Retire(Journal(log)); }
            catch (Exception ex) { JournalTechnique.Echec("ProfilJeu.Arrete", ex); }
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
