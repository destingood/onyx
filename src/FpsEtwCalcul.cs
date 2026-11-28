using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BTOptimizer
{
    /// <summary>
    /// Partie PURE du compteur de FPS : rangement des images reçues, fenêtre glissante, choix du jeu.
    /// Aucune dépendance à ETW, pour que le banc puisse rejouer une livraison d'images avec un temps
    /// simulé. La session ETW elle-même est dans FpsEtw.cs.
    /// </summary>
    internal sealed partial class FpsEtw
    {
        /// <summary>Statistiques d'un processus qui présente des images.</summary>
        public class ProcStat
        {
            public int Pid;
            public string Name = "?";
            public double Fps;            // images présentées sur la dernière seconde
            public double AvgMs;          // frametime moyen (1 s)
            public double OnePctLowFps;   // 1% low : 1000 / moyenne du pire 1 % des frametimes
            public double TenthPctLowFps; // 0.1% low (les pires micro-saccades) — 0 tant que < 1000 frames
            public double WorstMs;        // pire frametime (fenêtre récente)
            /// <summary>Durée RÉELLEMENT couverte par ces chiffres, en ms. Peut être inférieure à
            /// la fenêtre demandée : l'historique de frametimes est borné en temps. Un appelant qui
            /// annonce « benchmark d'une minute » doit dire ce qu'il a vraiment mesuré.</summary>
            public double FenetreMs;
            public long Total;            // total d'images depuis le début / la remise à zéro
        }

        // Par PID : horodatages (ms relatives) des dernières présentations + frametimes.
        private class Track
        {
            public double LastTs = -1;
            public readonly List<double> Times = new List<double>(4096);  // horodatage de chaque frame
            public readonly List<double> Fts = new List<double>(4096);    // frametime associé (ms)
            public long Total;
            public long Ajoutes;   // frametimes ajoutés à Fts depuis le début (élagage compris)
        }

        private readonly object _lock = new object();
        private readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();
        private readonly Dictionary<int, string> _names = new Dictionary<int, string>();

        /// <summary>Durée d'historique de frametimes conservée, en millisecondes. BORNÉE EN TEMPS,
        /// pas en nombre d'images : à 60 fps, 12 000 images font plus de trois minutes — un « 1 %
        /// low » calculé là-dessus décrit ce qui s'est passé il y a deux minutes, pas maintenant.</summary>
        private const double MemoireMs = 20000;

        // « Maintenant » NE PEUT PAS être l'horodatage du dernier événement : quand plus rien ne
        // présente (jeu fermé, minimisé, figé), cet horodatage se fige aussi — la fenêtre glissante
        // reste alors calée sur les dernières images vues et le compteur affiche indéfiniment le
        // dernier FPS connu. Une horloge murale sert donc de référence, et l'heure ETW est
        // extrapolée à partir du dernier événement reçu.
        private static readonly Stopwatch Chrono = Stopwatch.StartNew();
        /// <summary>Horloge murale en ms. Remplaçable : le banc y met un temps simulé.</summary>
        internal Func<double> Horloge = () => Chrono.Elapsed.TotalMilliseconds;
        private double _tsDernierEvt = -1;   // horodatage ETW du dernier événement
        private double _murAuDernierEvt;     // horloge murale au même instant

        // WINDOWS NE LIVRE PAS LES IMAGES UNE PAR UNE. Une session ETW en temps réel remet ses
        // tampons quand ils sont pleins (64 Ko) ou, à défaut, une fois par seconde : les images
        // arrivent par paquets, avec jusqu'à une seconde de retard.
        //
        // Le compteur comptait pourtant « la dernière seconde » jusqu'à l'instant présent. Les
        // images du jeu déjà affichées mais pas encore livrées manquaient donc au compte : un
        // relevé fait 0,6 s après un paquet n'en voyait que 40 %. Comme le relevé (toutes les
        // secondes) et la livraison ne battent pas au même rythme, le chiffre montait et
        // descendait sans que le jeu change : 144 FPS affichés entre 63 et 144.
        //
        // La fenêtre finit donc sur la dernière image LIVRÉE, tous processus confondus : jusque-là
        // rien ne manque. Elle ne reste pas figée pour autant — voir FinFenetre.
        private double _frontiere = -1;
        private long _horsOrdre;             // images arrivées avec une heure antérieure à la précédente

        /// <summary>Pour le contrôle (-fps) : images comptées pour ce processus, heure de la dernière
        /// image livrée, temps écoulé depuis, et images reçues dans le désordre — lus sous le même verrou.</summary>
        internal void Compte(int pid, out long total, out double frontiere, out double retardMs, out long horsOrdre)
        {
            lock (_lock)
            {
                Track t;
                total = _tracks.TryGetValue(pid, out t) ? t.Total : 0;
                frontiere = _frontiere;
                retardMs = _frontiere < 0 ? 0 : Maintenant() - _frontiere;
                horsOrdre = _horsOrdre;
            }
        }

        /// <summary>Retard de livraison toléré, en ms : une seconde de tampon ETW, plus une marge.</summary>
        public const double RetardLivraisonMaxMs = 1500;

        /// <summary>
        /// PUR : instant où finit la fenêtre de comptage. C'est la dernière image livrée tant
        /// qu'elle est récente. Si plus rien n'arrive (jeu fermé, figé, réduit), la fenêtre repart
        /// avec l'horloge : les dernières images en sortent et le chiffre retombe au lieu de rester.
        /// </summary>
        public static double FinFenetre(double frontiere, double maintenant)
        {
            if (frontiere < 0) return maintenant;
            return Math.Max(frontiere, maintenant - RetardLivraisonMaxMs);
        }

        /// <summary>Une image présentée par <paramref name="pid"/> à l'instant ETW <paramref name="ts"/>
        /// (en ms). Appelée par la session ETW pour chaque Present ; le banc l'appelle avec un temps simulé.</summary>
        internal void Recoit(int pid, double ts)
        {
            lock (_lock)
            {
                _tsDernierEvt = ts;
                if (ts > _frontiere) _frontiere = ts;
                _murAuDernierEvt = Horloge();
                Track t;
                if (!_tracks.TryGetValue(pid, out t))
                {
                    t = new Track();
                    _tracks[pid] = t;
                }
                if (t.LastTs >= 0)
                {
                    double ft = ts - t.LastTs;
                    if (ft < 0) _horsOrdre++;
                    if (ft >= 0 && ft < 10000)
                    {
                        t.Times.Add(ts);
                        t.Fts.Add(ft);
                        t.Ajoutes++;
                        Elague(t, ts);
                    }
                }
                t.LastTs = ts;
                t.Total++;
            }
        }

        /// <summary>Jette les images plus vieilles que <see cref="MemoireMs"/>. Le retrait se fait
        /// par blocs (jamais image par image) : retirer en tête d'une liste coûte cher, et ce code
        /// tourne sur CHAQUE image présentée — jusqu'à plusieurs centaines par seconde.</summary>
        private static void Elague(Track t, double maintenant)
        {
            if (t.Times.Count < 256) return;
            double limite = maintenant - MemoireMs;
            if (t.Times[0] >= limite) return;
            int coupe = PremierDansFenetre(t.Times, limite);
            if (coupe <= 0) return;
            t.Times.RemoveRange(0, coupe);
            t.Fts.RemoveRange(0, coupe);
        }

        /// <summary>PUR : index de la première image dont l'horodatage dépasse <paramref name="borne"/>.
        /// Rend Count si aucune ne la dépasse (fenêtre vide) — les horodatages sont croissants.</summary>
        public static int PremierDansFenetre(List<double> times, double borne)
        {
            if (times == null || times.Count == 0) return 0;
            int lo = 0, hi = times.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (times[mid] > borne) hi = mid; else lo = mid + 1;
            }
            return lo;
        }

        /// <summary>PUR : images comptées sur une fenêtre de durée connue. La durée est celle qui
        /// S'EST ÉCOULÉE, pas celle qui sépare la première et la dernière image — sans quoi un jeu
        /// qui s'arrête de présenter continuerait d'afficher son dernier FPS.</summary>
        public static double CalculeFps(int images, double fenetreMs)
        {
            if (images <= 0 || fenetreMs <= 0) return 0;
            return images * 1000.0 / fenetreMs;
        }

        /// <summary>Heure ETW estimée à l'instant présent (appeler sous verrou).</summary>
        private double Maintenant()
        {
            if (_tsDernierEvt < 0) return 0;
            return _tsDernierEvt + (Horloge() - _murAuDernierEvt);
        }

        /// <summary>Statistiques par processus (trié FPS décroissant). windowMs : fenêtre du FPS instantané.</summary>
        public List<ProcStat> Snapshot(double windowMs)
        {
            var result = new List<ProcStat>();

            // On ne peut pas compter sur des images qui n'existent plus. L'historique est borné à
            // MemoireMs ; demander une fenêtre plus large ne fait pas apparaître d'images, mais le
            // FPS était quand même divisé par la durée DEMANDÉE.
            //
            // Conséquence mesurée : un benchmark d'une minute annonçait le tiers du vrai FPS, deux
            // minutes le sixième. Le compteur en direct (fenêtre de 2 s) était juste, si bien que
            // l'utilisateur voyait 300 FPS pendant toute la capture puis lisait un résumé à 100.
            //
            // On borne donc la fenêtre à ce qui est réellement couvert. L'horloge murale reste la
            // référence : un jeu qui cesse de présenter voit toujours son FPS retomber, puisque ses
            // images vieillissent hors de la fenêtre au lieu d'être recomptées.
            double fenetre = Math.Min(windowMs, MemoireMs);

            lock (_lock)
            {
                double maintenant = Maintenant();
                foreach (KeyValuePair<int, Track> kv in _tracks)
                {
                    Track t = kv.Value;
                    if (t.Times.Count == 0) continue;
                    double cutoff = FinFenetre(_frontiere, maintenant) - fenetre;
                    if (t.Times[t.Times.Count - 1] < maintenant - 3000) continue;  // plus rien depuis 3 s : ignorer

                    int first = PremierDansFenetre(t.Times, cutoff);
                    int n = t.Times.Count - first;
                    if (n <= 0) continue;

                    double sum = 0, worst = 0;
                    for (int i = first; i < t.Times.Count; i++)
                    {
                        sum += t.Fts[i];
                        if (t.Fts[i] > worst) worst = t.Fts[i];
                    }

                    // 1% low / 0.1% low sur les ~20 dernières secondes de frames conservées.
                    double onePct = 0, tenthPct = 0;
                    int m = t.Fts.Count;
                    if (m >= 100)
                    {
                        var copy = new List<double>(t.Fts);
                        copy.Sort();
                        onePct = LowAvgFps(copy, m / 100);
                        if (m >= 1000) tenthPct = LowAvgFps(copy, m / 1000);
                    }

                    var st = new ProcStat
                    {
                        Pid = kv.Key,
                        Name = NameOf(kv.Key),
                        Fps = CalculeFps(n, fenetre),
                        FenetreMs = fenetre,
                        AvgMs = sum / n,
                        OnePctLowFps = onePct,
                        TenthPctLowFps = tenthPct,
                        WorstMs = worst,
                        Total = t.Total
                    };
                    result.Add(st);
                }
            }
            result.Sort((a, b) => b.Fps.CompareTo(a.Fps));
            return result;
        }

        // ------------------------------------------------------------------ quel processus est LE JEU

        /// <summary>
        /// Applications qui présentent des images sans être un jeu. Un navigateur avec
        /// l'accélération matérielle présente en continu, souvent PLUS VITE qu'un jeu synchronisé à
        /// 60 Hz — prendre « celui qui a le plus de FPS » désigne alors Chrome, pas le jeu.
        /// </summary>
        private static readonly string[] PasDesJeux =
        {
            "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore",
            "discord", "spotify", "steam", "steamwebhelper", "epicgameslauncher", "battle.net",
            "explorer", "dwm", "searchhost", "startmenuexperiencehost", "shellexperiencehost",
            "textinputhost", "widgets", "widgetboard", "applicationframehost", "systemsettings",
            "teams", "slack", "code", "devenv", "windowsterminal", "powershell", "cmd",
            "vlc", "mpc-hc64", "mpc-be64", "obs64", "obs32", "nvcontainer", "nvidia share",
            "btoptimizer", "onyx"
        };

        /// <summary>PUR : ce nom de processus est-il connu pour ne PAS être un jeu ?</summary>
        public static bool EstIgnore(string nom)
        {
            if (string.IsNullOrEmpty(nom)) return false;
            string n = nom.ToLowerInvariant();
            if (n.EndsWith(".exe")) n = n.Substring(0, n.Length - 4);
            foreach (string s in PasDesJeux)
                if (n == s) return true;
            return false;
        }

        /// <summary>
        /// PUR : lequel de ces processus est le jeu ?
        ///
        /// L'ANCIENNE RÈGLE ÉTAIT « celui qui a le plus de FPS », et elle est fausse par
        /// construction : un navigateur accéléré dépasse sans peine un jeu bridé à 60 Hz. Le
        /// compteur affichait alors le débit d'images de Chrome pendant une partie.
        ///
        /// La règle est maintenant : ce que l'utilisateur REGARDE, c'est-à-dire la fenêtre au
        /// premier plan. À défaut (premier plan inconnu, ou qui ne présente rien), on retombe sur le
        /// plus rapide en excluant ce qui n'est pas un jeu. Et si tout est exclu, on rend null
        /// plutôt qu'un mauvais candidat : « — » est une réponse honnête, un chiffre faux non.
        /// </summary>
        public static ProcStat ChoisirJeu(List<ProcStat> stats, int pidPremierPlan)
        {
            if (stats == null || stats.Count == 0) return null;

            // Le processus au premier plan est le meilleur indice — MAIS il reste soumis à la
            // liste d'exclusion. Sans cela, la fenêtre du compteur FPS elle-même, ou un navigateur
            // qu'on vient de consulter, deviendrait « le jeu » du seul fait d'être devant.
            if (pidPremierPlan > 0)
                foreach (ProcStat p in stats)
                    if (p.Pid == pidPremierPlan && p.Fps > 0 && !EstIgnore(p.Name)) return p;

            // REPLI : personne au premier plan ne présente. On CHERCHE alors un jeu, et une
            // recherche demande plus de preuves qu'une constatation.
            //
            // L'ancien code acceptait tout processus avec « Fps > 0 ». Sur une fenêtre d'une
            // seconde, un processus qui a présenté UNE SEULE image donne exactement 1 FPS — et
            // n'importe quelle fenêtre qui se repeint le fait : une notification, un lanceur, une
            // boîte de dialogue, un installateur. L'overlay s'accrochait à ce bruit et affichait
            // « 1 » alors qu'aucun jeu ne tournait.
            //
            // Au premier plan, on fait confiance quel que soit le débit : un jeu qui rame à 6 FPS,
            // c'est précisément le moment où l'utilisateur veut voir le chiffre. Mais un processus
            // que PERSONNE NE REGARDE et qui présente au compte-gouttes n'est pas un jeu qui
            // souffre — c'est une fenêtre qui vit sa vie.
            ProcStat meilleur = null;
            foreach (ProcStat p in stats)
            {
                if (p.Fps < FpsPlancherRepli || EstIgnore(p.Name)) continue;
                if (meilleur == null || p.Fps > meilleur.Fps) meilleur = p;
            }
            return meilleur;
        }

        /// <summary>Débit minimal pour qu'un processus HORS PREMIER PLAN soit pris pour un jeu.
        /// En dessous, c'est du bruit d'interface — voir ChoisirJeu.</summary>
        public const double FpsPlancherRepli = 20.0;

        /// <summary>PID de la fenêtre au premier plan, 0 si indéterminé.</summary>
        public static int PidPremierPlan()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return 0;
                int pid;
                GetWindowThreadProcessId(h, out pid);
                return pid;
            }
            catch { return 0; }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);

        /// <summary>Moyenne du pire k-ième des frametimes triés, convertie en FPS.</summary>
        private static double LowAvgFps(List<double> sorted, int k)
        {
            if (k < 1) k = 1;
            double s = 0;
            for (int i = sorted.Count - k; i < sorted.Count; i++) s += sorted[i];
            double avgWorst = s / k;
            return avgWorst > 0 ? 1000.0 / avgWorst : 0;
        }

        /// <summary>
        /// Frametimes arrivés depuis la dernière lecture, lus ET marqués lus sous le même verrou :
        /// aucune image comptée deux fois ni perdue entre deux relevés (sauf au-delà de
        /// l'historique de 20 s). <paramref name="vu"/> = -1 au départ : on ne prend alors rien,
        /// on pose seulement le repère.
        /// </summary>
        public double[] NouvellesImages(int pid, ref long vu)
        {
            lock (_lock)
            {
                Track t;
                if (!_tracks.TryGetValue(pid, out t)) return new double[0];
                long n = t.Ajoutes - vu;
                if (vu < 0 || n <= 0) { vu = t.Ajoutes; return new double[0]; }
                int k = (int)Math.Min(n, t.Fts.Count);
                var arr = new double[k];
                t.Fts.CopyTo(t.Fts.Count - k, arr, 0, k);
                vu = t.Ajoutes;
                return arr;
            }
        }

        /// <summary>Frametimes récents d'un PID (copie, pour le graphique).</summary>
        public double[] RecentFrametimes(int pid, int count)
        {
            lock (_lock)
            {
                Track t;
                if (!_tracks.TryGetValue(pid, out t) || t.Fts.Count == 0) return new double[0];
                int n = Math.Min(count, t.Fts.Count);
                var arr = new double[n];
                t.Fts.CopyTo(t.Fts.Count - n, arr, 0, n);
                return arr;
            }
        }

        private string NameOf(int pid)
        {
            string name;
            if (_names.TryGetValue(pid, out name)) return name;
            try { name = Process.GetProcessById(pid).ProcessName + ".exe"; }
            catch { name = "PID " + pid; }
            _names[pid] = name;
            return name;
        }

        public void Reset()
        {
            lock (_lock)
            {
                _tracks.Clear();
                _names.Clear();
            }
        }
    }
}
