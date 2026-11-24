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

        // Logiciels de fond sur l'autre CCD (X3D) : processus déjà traités, et quand.
        private static readonly HashSet<int> _vus = new HashSet<int>();
        private static List<int> _autres;
        private static DateTime _dernierBalayage;

        private static string CheminEtat { get { return AppPaths.File(FichierEtat); } }

        /// <summary>
        /// Envoie les logiciels de fond (et ONYX lui-même) sur l'autre CCD : le jeu garde pour lui
        /// le CCD à V-Cache et son cache L3. Rien n'est fermé. Un processus n'est tenté qu'une
        /// fois ; ceux lancés en cours de partie sont pris au balayage suivant.
        /// </summary>
        private static int Relegue(string jeu, List<int> autres)
        {
            _dernierBalayage = DateTime.UtcNow;
            Process[] tous = new Process[0];
            try { tous = Process.GetProcesses(); } catch { }
            var noms = new string[tous.Length];
            for (int i = 0; i < tous.Length; i++) { try { noms[i] = tous[i].ProcessName; } catch { } }
            var cibles = new HashSet<string>(AReleguer(noms, GameScan.ExesFor(jeu)), StringComparer.OrdinalIgnoreCase);
            int moi = -1;
            try { using (var p = Process.GetCurrentProcess()) moi = p.Id; } catch { }
            int n = 0;
            for (int i = 0; i < tous.Length; i++)
            {
                try
                {
                    int pid = tous[i].Id;
                    if (pid == moi || (noms[i] != null && cibles.Contains(noms[i])))
                    {
                        bool nouveau;
                        lock (_vus) nouveau = _vus.Add(pid);
                        if (nouveau && Topologie.AppliqueCpuSets(pid, autres, null)) n++;
                    }
                }
                catch { }
                tous[i].Dispose();
            }
            return n;
        }

        /// <summary>Rend à Windows le placement des logiciels de fond (liste connue et ONYX).</summary>
        private static void Libere()
        {
            var connus = new HashSet<string>(LogicielsDeFond, StringComparer.OrdinalIgnoreCase);
            int moi = -1;
            try { using (var p = Process.GetCurrentProcess()) moi = p.Id; } catch { }
            Process[] tous = new Process[0];
            try { tous = Process.GetProcesses(); } catch { }
            foreach (Process p in tous)
            {
                try { if (p.Id == moi || connus.Contains(p.ProcessName)) Topologie.AppliqueCpuSets(p.Id, null, null); } catch { }
                p.Dispose();
            }
            lock (_vus) _vus.Clear();
            _autres = null;
        }

        /// <summary>Non bloquant : sort tout de suite si un tour est en cours.</summary>
        public static void Tick(Action<string, int> log) { Tick(log, null); }

        /// <summary>Même tour sur un relevé déjà pris (null : le tour relève lui-même les processus, en fond).</summary>
        public static void Tick(Action<string, int> log, VeilleJeu.Releve r)
        {
            lock (_verrou)
            {
                if (_occupe) return;
                _occupe = true;
            }
            Action<string, int> journal = Journal(log);
            Task.Run(delegate
            {
                try { Tour(journal, r != null ? r.Noms : null); }
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

        private static void Tour(Action<string, int> log, string[] noms)
        {
            Dictionary<string, Tuple<string, long>> profils = Sys.LoadGameAffinity();
            var exes = new List<KeyValuePair<string, string[]>>();
            foreach (string jeu in profils.Keys) exes.Add(new KeyValuePair<string, string[]>(jeu, GameScan.ExesFor(jeu)));
            ISet<string> releve = noms != null ? new HashSet<string>(noms, StringComparer.OrdinalIgnoreCase) : NomsProcessus();
            string detecte = JeuDetecte(exes, releve);

            if (!_repris) { _repris = true; Reprend(detecte, log); }

            string enCours;
            lock (_verrou) enCours = _etat == null ? null : _etat.Jeu;

            switch (Decide(enCours, detecte))
            {
                case Geste.Appliquer: Applique(detecte, profils[detecte], log); break;
                case Geste.Retirer: Retire(log); break;
                case Geste.Rien:
                    // Partie en cours : les logiciels ouverts depuis rejoignent l'autre CCD
                    // (toutes les 10 s au plus, un relevé des processus coûte quelques ms).
                    Etat e;
                    lock (_verrou) e = _etat;
                    if (e != null && e.Fond && (DateTime.UtcNow - _dernierBalayage).TotalSeconds >= 10)
                    {
                        if (_autres == null) _autres = Topologie.Autres(Topologie.Lit());   // état adopté après un redémarrage
                        if (_autres.Count > 0) Relegue(e.Jeu, _autres);
                    }
                    break;
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
                    if (f.Fond) Libere();
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
            List<int> autres = new List<int>();
            if (string.Equals(profil.Item1, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                List<Topologie.Proc> topo = Topologie.Lit();
                preferes = Topologie.Preferes(topo);
                autres = Topologie.Autres(topo);
            }
            long masque;
            Mode cible = Cible(profil.Item1, profil.Item2, preferes, out masque);
            if (cible != Mode.CpuSets) autres = new List<int>();

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
            var e = new Etat { Jeu = jeu, PlanOrigine = bascule ? planActif : null, Fond = autres.Count > 0 };
            EcritFichier(e);
            lock (_verrou) _etat = e;
            // X3D à deux CCD : le reste part sur l'autre CCD. Le plan Performances que pose le
            // profil empêche le pilote AMD de garer ce CCD ; sans ce geste, navigateur, vocal et
            // lanceurs partageraient le cache du jeu.
            int deplaces = 0;
            if (autres.Count > 0) { _autres = autres; deplaces = Relegue(jeu, autres); }
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
                + (bascule ? ", plan Performances" : ", plan inchangé")
                + (autres.Count > 0 ? ", " + deplaces + " logiciel(s) de fond sur l'autre CCD." : "."), 1);

            // Plafond FPS réellement atteignable (lecture seule : fichier du jeu, panneau NVIDIA, écran).
            if (jeu.StartsWith("Overwatch", StringComparison.OrdinalIgnoreCase))
                try { foreach (string c in Diagnostics.CoherenceOverwatch()) log(c, 2); } catch { }
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
            if (e.Fond) Libere();
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
