using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace BTOptimizer
{
    /// <summary>
    /// JOURNAL DE JEU — branchement : relevé de la veille (fil de fond, toutes les 2 s), index
    /// exécutable → jeu construit en fond à partir des jeux installés, fichier bt-journal-jeu.txt.
    /// Tout est sous un verrou : Tick tourne sur un fil de fond, l'interface lit des copies.
    /// </summary>
    internal static partial class JournalJeu
    {
        private const string Fichier = "bt-journal-jeu.txt";
        private const int ProfondeurMax = 3;
        private const int ExesParJeuMax = 300;
        private static readonly TimeSpan IndexValable = TimeSpan.FromHours(1);

        private static readonly object _verrou = new object();
        private static List<Partie> _parties;
        private static readonly Etat _etat = new Etat();
        private static volatile Dictionary<string, string> _index;
        private static DateTime _indexQuand = DateTime.MinValue;
        private static bool _indexEnCours;

        /// <summary>Levé après l'enregistrement d'une partie close (fil de fond : passer par BeginInvoke).</summary>
        public static event Action Change;

        /// <summary>Copie des parties enregistrées.</summary>
        public static List<Partie> Parties
        {
            get { lock (_verrou) { return new List<Partie>(Charge()); } }
        }

        /// <summary>Jeu reconnu en ce moment par la veille, ou null.</summary>
        public static string JeuEnCours
        {
            get { lock (_verrou) { return _etat.Jeu; } }
        }

        /// <summary>Partie close pendant que l'application tourne, FPS remplis si mesurés (fil de fond).</summary>
        public static event Action<Partie> PartieClose;

        /// <summary>Un relevé de la veille. Appelé sur le fil de fond du minuteur, un à la fois.</summary>
        public static void Tick(VeilleJeu.Releve r)
        {
            if (r == null) return;
            DateTime now = DateTime.Now;
            ReconstruitSiBesoin(now);
            Dictionary<string, string> index = _index ?? IndexDeSecours();
            // Le jeu reconnu par la veille (exe distinctif) passe avant les correspondances de l'index.
            string jeu = r.JeuConnu != null ? JeuParmi(new[] { r.JeuConnu }, index) : null;
            if (jeu == null) jeu = JeuParmi(r.Noms, index);
            Partie close; string enCours;
            lock (_verrou) { close = Avance(_etat, jeu, now); enCours = _etat.Jeu; }
            if (close != null)
            {
                FinMesure(close);
                Enregistre(close);
                try { var h = PartieClose; if (h != null) h(close); } catch { }
            }
            if (enCours != null && MesureFps) { ReleveFps(enCours, index, now); ReleveMateriel(enCours); }
            else if (enCours == null) ArreteMesure();
        }

        /// <summary>Fermeture d'ONYX : la partie en cours est close à son dernier relevé.</summary>
        public static void Termine()
        {
            Partie close = null;
            lock (_verrou)
            {
                if (_etat.Jeu != null) close = Avance(_etat, null, _etat.DernierVu.AddSeconds(GraceSecondes + 1));
            }
            if (close != null) { FinMesure(close); Enregistre(close); }
            ArreteMesure();
        }

        // ------------------------------------------------------------------ FPS de la partie

        private static readonly object _verrouMesure = new object();
        private static FpsEtw _fps;
        private static Mesure _mesure;
        private static string _jeuMesure;
        private static bool? _mesureFps;

        private static string CheminOption { get { return AppPaths.File("bt-journal-fps.txt"); } }

        /// <summary>Option « Mesurer chaque partie » (FPS, charge et températures). Défaut : NON (session
        /// ETW, droits administrateur) ; persistante.</summary>
        public static bool MesureFps
        {
            get
            {
                if (_mesureFps.HasValue) return _mesureFps.Value;
                try { _mesureFps = File.Exists(CheminOption) && File.ReadAllText(CheminOption).Trim() == "1"; }
                catch { _mesureFps = false; }
                return _mesureFps.Value;
            }
            set
            {
                _mesureFps = value;
                try { File.WriteAllText(CheminOption, value ? "1" : "0"); }
                catch (Exception ex) { JournalTechnique.Echec("JournalJeu.MesureFps=", ex); }
                if (!value) ArreteMesure();
            }
        }

        private static void ReleveFps(string jeu, Dictionary<string, string> index, DateTime now)
        {
            lock (_verrouMesure)
            {
                if (!string.Equals(_jeuMesure, jeu, StringComparison.OrdinalIgnoreCase))
                {
                    StopEtw();
                    _jeuMesure = jeu;
                    _mesure = new Mesure();
                    var f = new FpsEtw("BTOptimizer-FPS-Journal");
                    if (f.Start()) _fps = f;
                    else { JournalTechnique.Echec("JournalJeu.FpsEtw", f.LastError ?? "démarrage refusé"); f.Dispose(); }
                    return;   // première image au prochain relevé
                }
                if (_fps == null || _mesure == null) return;
                FpsEtw.ProcStat s = null;
                foreach (FpsEtw.ProcStat p in _fps.Snapshot(1000))
                {
                    string nom;
                    if (index.TryGetValue(Cle(p.Name), out nom) && string.Equals(nom, jeu, StringComparison.OrdinalIgnoreCase)) { s = p; break; }
                }
                if (s != null && s.Fps > 0) _mesure.Ajoute(s.Pid, s.Total, s.Fps, now);
                else _mesure.Rien();
            }
        }

        // Matériel de la partie : même option, même fil de fond que les FPS. Lecture seule (compteurs
        // Windows, API du constructeur de la carte graphique) : rien n'est lu dans le jeu.
        private static HwMonitor _hw;
        private static Materiel _materiel;
        private static string _jeuMateriel;

        private static void ReleveMateriel(string jeu)
        {
            lock (_verrouMesure)
            {
                if (!string.Equals(_jeuMateriel, jeu, StringComparison.OrdinalIgnoreCase))
                {
                    StopHw();
                    _jeuMateriel = jeu;
                    _materiel = new Materiel();
                    try { _hw = new HwMonitor(); }
                    catch (Exception ex) { JournalTechnique.Echec("JournalJeu.HwMonitor", ex); }
                    return;   // la charge du processeur se mesure entre deux relevés : premier chiffre au suivant
                }
                if (_hw == null || _materiel == null) return;
                try
                {
                    HwSample s = _hw.Sample();
                    bool gpu = s.Gpu != null && s.Gpu.Ok;
                    _materiel.Ajoute(s.CpuLoad, s.CpuTempC, gpu ? s.Gpu.Util : double.NaN, gpu ? s.Gpu.TempC : double.NaN, s.RamLoad);
                }
                catch (Exception ex) { JournalTechnique.Echec("JournalJeu.ReleveMateriel", ex); }
            }
        }

        private static void StopHw()
        {
            try { if (_hw != null) _hw.Dispose(); } catch { }
            _hw = null;
        }

        /// <summary>Remplit les FPS et le matériel de la partie close si c'est elle qu'on mesurait, puis arrête.</summary>
        private static void FinMesure(Partie p)
        {
            lock (_verrouMesure)
            {
                if (_mesure != null && string.Equals(_jeuMesure, p.Jeu, StringComparison.OrdinalIgnoreCase)) _mesure.Remplit(p);
                if (_materiel != null && string.Equals(_jeuMateriel, p.Jeu, StringComparison.OrdinalIgnoreCase)) _materiel.Remplit(p);
            }
            ArreteMesure();
        }

        private static void ArreteMesure()
        {
            lock (_verrouMesure)
            {
                if (_jeuMesure == null && _fps == null && _jeuMateriel == null && _hw == null) return;
                StopEtw();
                StopHw();
                _jeuMesure = null;
                _mesure = null;
                _jeuMateriel = null;
                _materiel = null;
            }
        }

        private static void StopEtw()
        {
            try { if (_fps != null) _fps.Dispose(); } catch { }
            _fps = null;
        }

        private static List<Partie> Charge()
        {
            if (_parties != null) return _parties;
            string texte = null;
            try { string c = AppPaths.File(Fichier); if (File.Exists(c)) texte = File.ReadAllText(c, Encoding.UTF8); }
            catch { }
            _parties = Lire(texte);
            return _parties;
        }

        private static void Enregistre(Partie p)
        {
            string texte;
            lock (_verrou)
            {
                Charge().Add(p);
                texte = Ecrire(_parties);
            }
            try
            {
                string chemin = AppPaths.File(Fichier);
                string tmp = chemin + ".tmp";
                File.WriteAllText(tmp, texte, new UTF8Encoding(false));
                File.Move(tmp, chemin, true);
            }
            catch (Exception ex) { JournalTechnique.Echec("JournalJeu.Enregistre", ex); }
            try { var h = Change; if (h != null) h(); } catch { }
        }

        // ------------------------------------------------------------------ index exe → jeu

        private static Dictionary<string, string> IndexDeSecours()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string exe in VeilleJeu.ExesPrioritaires())
            {
                string k = Cle(exe);
                d[k] = k.StartsWith("fortnite") ? "Fortnite" : k.StartsWith("valorant") ? "VALORANT" : k == "cs2" ? "Counter-Strike 2" : Path.GetFileNameWithoutExtension(exe);
            }
            return d;
        }

        private static void ReconstruitSiBesoin(DateTime now)
        {
            lock (_verrou)
            {
                if (_indexEnCours || now - _indexQuand < IndexValable) return;
                _indexEnCours = true;
                _indexQuand = now;
            }
            Task.Run(() =>
            {
                try { _index = ConstruitIndex(); }
                catch (Exception ex) { JournalTechnique.Echec("JournalJeu.ConstruitIndex", ex); }
                finally { lock (_verrou) { _indexEnCours = false; } }
            });
        }

        private static Dictionary<string, string> ConstruitIndex()
        {
            Dictionary<string, string> d = IndexDeSecours();
            var ambigus = new HashSet<string>(StringComparer.Ordinal);
            var vus = new Dictionary<string, string>(StringComparer.Ordinal);
            List<GameLibrary.InstalledGame> jeux = GameLibrary.ScanAll() ?? new List<GameLibrary.InstalledGame>();
            // Les logiciels vendus sur Steam (fond d'écran animé, mise à l'échelle) tournent pendant
            // des heures, parfois À CÔTÉ du jeu : comptés, ils feraient des parties fantômes.
            var ids = new List<int>();
            foreach (GameLibrary.InstalledGame g in jeux) if (g != null) ids.Add(g.SteamAppId);
            HashSet<int> logiciels = PageGames.LogicielsSteam(ids);
            foreach (GameLibrary.InstalledGame g in jeux)
            {
                if (g == null || string.IsNullOrWhiteSpace(g.Name) || string.IsNullOrEmpty(g.InstallDir)) continue;
                if (BibliothequeJeux.EstLauncher(g.Name) || logiciels.Contains(g.SteamAppId)) continue;
                foreach (string k in ExesDe(g.InstallDir))
                {
                    string deja;
                    if (vus.TryGetValue(k, out deja) && !string.Equals(deja, g.Name, StringComparison.OrdinalIgnoreCase)) ambigus.Add(k);
                    else vus[k] = g.Name;
                }
            }
            // Un même nom d'exécutable dans deux jeux (« game », « start ») ne désigne aucun des deux.
            foreach (var kv in vus) if (!ambigus.Contains(kv.Key)) d[kv.Key] = kv.Value;
            return d;
        }

        private static List<string> ExesDe(string racine)
        {
            var cles = new List<string>();
            var file = new Queue<KeyValuePair<string, int>>();
            file.Enqueue(new KeyValuePair<string, int>(racine, 0));
            while (file.Count > 0 && cles.Count < ExesParJeuMax)
            {
                var cur = file.Dequeue();
                try
                {
                    foreach (string f in Directory.EnumerateFiles(cur.Key, "*.exe"))
                    {
                        string k = Cle(Path.GetFileName(f));
                        if (ExeExclu(k)) continue;
                        cles.Add(k);
                        if (cles.Count >= ExesParJeuMax) break;
                    }
                    if (cur.Value < ProfondeurMax)
                        foreach (string sous in Directory.EnumerateDirectories(cur.Key))
                            file.Enqueue(new KeyValuePair<string, int>(sous, cur.Value + 1));
                }
                catch { }   // dossier illisible : on passe
            }
            return cles;
        }
    }
}
