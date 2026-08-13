using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// 💽 CENTRE DE STOCKAGE — le moteur qui fait LA PART DES CHOSES.
    ///
    /// Onyx savait déjà nettoyer (Sys.CleanTargets), mesurer le disque (ChatActions) et repérer les
    /// jeux oubliés (DormantGames) — mais en pièces détachées. Ce fichier fédère tout derrière une
    /// idée simple : chaque octet récupérable est classé sur TROIS niveaux de sûreté, et c'est
    /// l'utilisateur qui choisit son cran.
    ///
    ///   0 — SUPERFLU     : aucune perte possible (temporaires, caches, shaders). Coché par défaut.
    ///   1 — À VÉRIFIER   : régénérable mais visible (historique, corbeille, hibernation, WinSxS).
    ///   2 — DONNÉES PERSO: JAMAIS supprimé par un lot. Uniquement montré, avec une action manuelle.
    ///
    /// Promesse tenue au niveau du code : aucun chemin du niveau 2 ne porte d'action <c>Free</c>,
    /// et <see cref="FreeAll"/> refuse tout ce qui dépasse le cran demandé. Les désinstallations
    /// passent toujours par le désinstalleur officiel de l'application, jamais par une suppression
    /// de dossier.
    /// </summary>
    internal static class Storage
    {
        public const int SafeSuperflu = 0;
        public const int SafeVerifier = 1;
        public const int SafePerso = 2;

        public static string SafetyLabel(int s)
        {
            if (s <= SafeSuperflu) return "superflu";
            if (s == SafeVerifier) return "à vérifier";
            return "données perso";
        }

        // ------------------------------------------------------------------
        //  Modèle
        // ------------------------------------------------------------------
        internal class StorageItem
        {
            public string ModuleId;
            public string Key;          // identité stable, pour la liste des ignorés
            public string Name;
            public string Detail;       // précision affichée sous le nom (peut être null)
            public string Path;
            public long SizeMB;
            public int Safety;
            public object Tag;          // AppEntry / DormantGames.Entry selon le module

            /// <summary>Libère cet élément et renvoie les Mo RÉELLEMENT récupérés (mesuré, pas
            /// estimé). Null = élément informatif : rien à supprimer ici.</summary>
            public Func<Action<string, int>, long> Free;

            /// <summary>Ouvre l'outil Windows ou le dossier correspondant. Null = pas d'ouverture.</summary>
            public Action Open;
            public string OpenLabel;

            public string SizeText { get { return Human(SizeMB); } }
        }

        internal class Module
        {
            public string Id, Glyph, Name, Desc;
            public int Safety;
            public bool Browse;   // module de PRÉSENTATION : on montre, on ne nettoie jamais en lot
            public bool Pro;      // réservé aux licences Pro
            public readonly List<StorageItem> Items = new List<StorageItem>();

            public long SizeMB
            {
                get { long s = 0; foreach (StorageItem i in Items) s += i.SizeMB; return s; }
            }

            /// <summary>Mo réellement libérables ici (les éléments informatifs ne comptent pas).</summary>
            public long FreeableMB
            {
                get { long s = 0; foreach (StorageItem i in Items) if (i.Free != null) s += i.SizeMB; return s; }
            }

            /// <summary>Y a-t-il quelque chose à lancer ici ? Distinct de <see cref="FreeableMB"/> :
            /// le nettoyage WinSxS agit réellement mais son gain n'est connu qu'après coup.</summary>
            public bool HasActions
            {
                get { foreach (StorageItem i in Items) if (i.Free != null) return true; return false; }
            }
        }

        /// <summary>Les modules, dans l'ordre d'affichage. Reconstruits à chaque analyse (et lus tels
        /// quels par l'écran de configuration, qui n'a besoin que de leurs libellés).</summary>
        public static List<Module> Defs()
        {
            return new List<Module>
            {
                new Module { Id = "temp",    Glyph = "🗑", Name = "Fichiers temporaires & caches", Safety = SafeSuperflu,
                             Desc = "Temporaires Windows, mises à jour déjà installées, caches navigateurs, rapports de plantage." },
                new Module { Id = "gpu",     Glyph = "🎮", Name = "Caches graphiques & shaders", Safety = SafeSuperflu,
                             Desc = "NVIDIA, AMD, DirectX, Steam. Recompilés au prochain lancement (quelques saccades une seule fois)." },
                new Module { Id = "history", Glyph = "🕘", Name = "Historique & traces", Safety = SafeVerifier,
                             Desc = "Fichiers récents, Jump Lists, miniatures. Rien de perdu, mais tes listes « récents » repartent à zéro." },
                new Module { Id = "bin",     Glyph = "♻", Name = "Corbeille", Safety = SafeVerifier,
                             Desc = "Ce que tu as déjà supprimé. La vider rend la suppression définitive." },
                new Module { Id = "winsys",  Glyph = "🪟", Name = "Windows en profondeur", Safety = SafeVerifier, Pro = true,
                             Desc = "Veille prolongée, magasin de composants (WinSxS), ancienne installation, points de restauration." },
                new Module { Id = "apps",    Glyph = "📦", Name = "Applications installées", Safety = SafePerso, Browse = true,
                             Desc = "Classées par poids réel sur le disque. Désinstallation par l'outil officiel de l'application." },
                new Module { Id = "games",   Glyph = "🕹", Name = "Jeux dormants", Safety = SafePerso, Browse = true,
                             Desc = "Gros et pas lancés depuis des mois : c'est presque toujours là que dorment les dizaines de Go." },
                new Module { Id = "videos",  Glyph = "🎬", Name = "Vidéos & films", Safety = SafePerso, Browse = true,
                             Desc = "Les vidéos les plus lourdes de tes disques (captures, rushes, films). Suppression = corbeille Windows, récupérable." },
                new Module { Id = "archives", Glyph = "🗜", Name = "Archives & images disque", Safety = SafePerso, Browse = true,
                             Desc = "zip, rar, 7z, iso… Souvent déjà extraites : l'original dort en double." },
                new Module { Id = "bigfiles", Glyph = "🧱", Name = "Autres gros fichiers", Safety = SafePerso, Browse = true,
                             Desc = "Tout le reste au-dessus du seuil réglable : installeurs, exports, disques virtuels…" },
                new Module { Id = "folders", Glyph = "📁", Name = "Tes dossiers les plus lourds", Safety = SafePerso, Browse = true,
                             Desc = "Téléchargements, Vidéos, Bureau, Documents. Montrés pour info — je n'y touche JAMAIS." },
            };
        }

        // ------------------------------------------------------------------
        //  Analyse
        // ------------------------------------------------------------------
        /// <summary>Analyse complète. <paramref name="progress"/> reçoit (libellé, fait, total).
        /// Bloquant : à appeler depuis une tâche de fond.</summary>
        public static List<Module> ScanAll(StorageSettings st, Action<string, int, int> progress)
        {
            if (st == null) st = StorageSettings.Load();
            List<Module> mods = Defs();
            List<Sys.CleanTarget> targets = null;
            List<FileEntry> files = null;   // un seul balayage disque pour vidéos + archives + gros fichiers
            int total = mods.Count;

            for (int i = 0; i < mods.Count; i++)
            {
                Module m = mods[i];
                if (progress != null) { try { progress(m.Name, i, total); } catch { } }
                if (!st.IsEnabled(m.Id)) continue;
                // Module réservé Pro : on ne l'analyse même pas (DISM coûte une minute) — la page
                // affichera la carte verrouillée à partir de sa seule définition.
                if (m.Pro && !License.ProUnlocked) continue;
                try
                {
                    switch (m.Id)
                    {
                        case "temp":
                        case "gpu":
                        case "history":
                        case "bin":
                            if (targets == null) targets = Sys.CleanTargets();
                            FillFromTargets(m, targets, st);
                            break;
                        case "winsys": FillWindows(m, st); break;
                        case "apps": FillApps(m, st, null); break;
                        case "games": FillGames(m, st); break;
                        case "videos":
                        case "archives":
                        case "bigfiles":
                            if (files == null) { bool part; files = ScanBigFiles(st.MinFileMB, AppRoots(), out part); }
                            FillFiles(m, files, st);
                            break;
                        case "folders": FillFolders(m, st); break;
                    }
                }
                catch { }
            }
            if (progress != null) { try { progress(null, total, total); } catch { } }
            return mods;
        }

        // --- modules adossés à Sys.CleanTargets (le nettoyeur historique) ---
        private static void FillFromTargets(Module m, List<Sys.CleanTarget> targets, StorageSettings st)
        {
            foreach (Sys.CleanTarget t in targets)
            {
                if (t.Kind != m.Id || t.SizeMB <= 0) continue;
                string key = t.IsRecycleBin ? "recyclebin" : (t.Path ?? t.Name);
                if (st.IsIgnored(key)) continue;

                Sys.CleanTarget cap = t;
                m.Items.Add(new StorageItem
                {
                    ModuleId = m.Id,
                    Key = key,
                    Name = t.Name,
                    Path = t.Path,
                    SizeMB = t.SizeMB,
                    Safety = t.Safety,
                    Free = delegate (Action<string, int> log)
                    {
                        long before = cap.SizeMB;
                        try { Sys.CleanTargetNow(cap, log); } catch { return 0; }
                        long after = 0;
                        try { after = Sys.MeasureCleanTarget(cap); } catch { }
                        return Math.Max(0, before - after);
                    }
                });
            }
        }

        // --- Windows en profondeur : hibernation, WinSxS, Windows.old, points de restauration ---
        private static void FillWindows(Module m, StorageSettings st)
        {
            string sysRoot = null;
            try { sysRoot = Path.GetPathRoot(Environment.SystemDirectory); } catch { }

            // Veille prolongée : le gain le plus immédiat, et 100 % réversible.
            double hib = -1;
            try { hib = ChatActions.HibernationSizeGb(); } catch { }
            if (hib > 0.2 && !st.IsIgnored("hiberfil"))
            {
                double gb = hib;
                m.Items.Add(new StorageItem
                {
                    ModuleId = m.Id, Key = "hiberfil", Safety = SafeVerifier,
                    Name = "Veille prolongée (hiberfil.sys)",
                    Detail = "Récupéré en quelques secondes. Coupe aussi le démarrage rapide — réversible à tout moment.",
                    SizeMB = (long)(gb * 1024),
                    Free = delegate (Action<string, int> log)
                    {
                        double before = SysFreeGb();
                        try { ChatActions.HibernateOffAction(gb).Run(log); } catch { return 0; }
                        double after = SysFreeGb();
                        return (before >= 0 && after > before) ? (long)((after - before) * 1024) : 0;
                    }
                });
            }

            // Magasin de composants : on ne lance DISM que si son verdict n'est plus frais (24 h),
            // sinon chaque ouverture de la page coûterait une minute d'analyse.
            if (!st.IsIgnored("winsxs") && DismRecommendedCached())
            {
                m.Items.Add(new StorageItem
                {
                    ModuleId = m.Id, Key = "winsxs", Safety = SafeVerifier,
                    Name = "Magasin de composants Windows (WinSxS)",
                    Detail = "Nettoyage OFFICIEL recommandé par Windows lui-même — souvent 2 à 8 Go. Compte 5 à 20 minutes.",
                    SizeMB = 0,   // le gain n'est connu qu'après coup : on ne l'invente pas
                    Free = delegate (Action<string, int> log)
                    {
                        double before = SysFreeGb();
                        try { ChatActions.ComponentCleanupAction().Run(log); } catch { return 0; }
                        try { File.Delete(DismCachePath); } catch { }   // le verdict est périmé
                        double after = SysFreeGb();
                        return (before >= 0 && after > before) ? (long)((after - before) * 1024) : 0;
                    }
                });
            }

            // Windows.old : Windows le retire SEUL ~10 jours après une grosse mise à jour, et sa
            // suppression manuelle demande une prise de possession. On informe et on délègue à
            // l'outil Windows — supprimer ça nous-mêmes serait irréversible et fragile.
            try
            {
                string wold = sysRoot == null ? null : Path.Combine(sysRoot, "Windows.old");
                if (wold != null && Directory.Exists(wold) && !st.IsIgnored("windowsold"))
                {
                    long mb = DirSizeCappedMb(wold, 6000);
                    if (mb > 200)
                    {
                        DateTime made = DateTime.MinValue;
                        try { made = Directory.GetCreationTime(wold); } catch { }
                        int days = made == DateTime.MinValue ? -1 : Math.Max(0, (int)(DateTime.Now - made).TotalDays);
                        m.Items.Add(new StorageItem
                        {
                            ModuleId = m.Id, Key = "windowsold", Safety = SafeVerifier, Path = wold, SizeMB = mb,
                            Name = "Ancienne installation (Windows.old)",
                            Detail = days >= 0 && days < 10
                                ? "Créée il y a " + days + " j — c'est ELLE qui permet de revenir à l'ancienne version de Windows. Attends la fin des 10 jours si tu hésites."
                                : "Windows la supprime seul après ~10 jours. L'outil « Nettoyage de disque » de Windows la propose aussi.",
                            OpenLabel = "Nettoyage de disque Windows",
                            Open = delegate { Launch("cleanmgr.exe", sysRoot != null ? "/d " + sysRoot.TrimEnd('\\') : ""); }
                        });
                    }
                }
            }
            catch { }

            // Points de restauration : c'est le filet de sécurité que ONYX lui-même alimente avant
            // d'appliquer des optimisations. On l'affiche, on ne le coupe jamais à sa place.
            try
            {
                long shadow = ShadowStorageMb(sysRoot);
                if (shadow > 512 && !st.IsIgnored("restore"))
                {
                    m.Items.Add(new StorageItem
                    {
                        ModuleId = m.Id, Key = "restore", Safety = SafeVerifier, SizeMB = shadow,
                        Name = "Points de restauration système",
                        Detail = "C'est ton filet de sécurité (ONYX en crée un avant les grosses optimisations). Réduis le quota plutôt que de tout supprimer.",
                        OpenLabel = "Réglages de protection système",
                        Open = delegate { Launch("SystemPropertiesProtection.exe", ""); }
                    });
                }
            }
            catch { }
        }

        // --- applications installées, classées par poids réel ---
        private static void FillApps(Module m, StorageSettings st, Action<int, int> progress)
        {
            List<AppEntry> apps = ScanApps(progress);
            foreach (AppEntry a in apps)
            {
                if (a.SizeMB <= 0 || st.IsIgnored(a.Key)) continue;
                AppEntry cap = a;
                m.Items.Add(new StorageItem
                {
                    ModuleId = m.Id, Key = a.Key, Safety = SafePerso, SizeMB = a.SizeMB,
                    Name = a.Name, Path = a.InstallDir, Tag = a,
                    Detail = a.Publisher,
                    OpenLabel = "Ouvrir le dossier",
                    Open = delegate { OpenFolder(cap.InstallDir); }
                });
            }
        }

        // --- jeux dormants : on ne garde que l'espace réellement « dormant » ---
        private static void FillGames(Module m, StorageSettings st)
        {
            List<DormantGames.Entry> games;
            try { games = DormantGames.Scan(null); } catch { return; }
            foreach (DormantGames.Entry e in games)
            {
                if (!e.IsDormant || e.SizeMB <= 0) continue;
                if (st.IsIgnored(e.Name)) continue;
                DormantGames.Entry cap = e;
                m.Items.Add(new StorageItem
                {
                    ModuleId = m.Id, Key = e.Name, Safety = SafePerso, SizeMB = e.SizeMB,
                    Name = e.Name, Path = e.InstallDir, Tag = e,
                    Detail = (e.Launcher ?? "?") + " · dernière partie : " + e.IdleText,
                    OpenLabel = "Ouvrir le dossier",
                    Open = delegate { OpenFolder(cap.InstallDir); }
                });
            }
        }

        // --- vidéos / archives / gros fichiers : le stockage dans TOUS les domaines ---
        private static void FillFiles(Module m, List<FileEntry> files, StorageSettings st)
        {
            int kept = 0;
            foreach (FileEntry f in files)
            {
                if (f.Cat != m.Id || st.IsIgnored(f.Path)) continue;
                m.Items.Add(FileToItem(f));
                if (++kept >= 60) break;   // les 60 plus gros suffisent : au-delà c'est du bruit
            }
        }

        // --- dossiers personnels : montrés, jamais touchés ---
        private static void FillFolders(Module m, StorageSettings st)
        {
            string sysRoot = null;
            try { sysRoot = Path.GetPathRoot(Environment.SystemDirectory); } catch { }
            string profile = Environment.GetEnvironmentVariable("USERPROFILE") ?? "";

            var dirs = new List<string[]>
            {
                new[] { "Téléchargements", Path.Combine(profile, "Downloads") },
                new[] { "Vidéos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) },
                new[] { "Images", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) },
                new[] { "Bureau", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) },
                new[] { "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
            };

            foreach (string[] d in dirs)
            {
                if (string.IsNullOrEmpty(d[1]) || st.IsIgnored(d[1])) continue;
                long mb = DirSizeCappedMb(d[1], 4000);
                if (mb < 512) continue;
                // Un dossier redirigé vers un autre disque ne libérera rien sur C: — on le dit.
                bool other = false;
                try { other = sysRoot != null && !string.Equals(Path.GetPathRoot(d[1]), sysRoot, StringComparison.OrdinalIgnoreCase); }
                catch { }
                string cap = d[1];
                m.Items.Add(new StorageItem
                {
                    ModuleId = m.Id, Key = d[1], Safety = SafePerso, SizeMB = mb, Name = d[0], Path = d[1],
                    Detail = other ? "sur un autre disque : ne compte pas pour " + sysRoot : "à trier à la main — je n'y touche jamais",
                    OpenLabel = "Ouvrir",
                    Open = delegate { OpenFolder(cap); }
                });
            }
        }

        // ------------------------------------------------------------------
        //  Libération
        // ------------------------------------------------------------------
        /// <summary>Libère les éléments demandés, en refusant tout ce qui dépasse
        /// <paramref name="maxSafety"/>. Renvoie les Mo réellement récupérés.</summary>
        public static long FreeAll(IEnumerable<StorageItem> items, int maxSafety, Action<string, int> log)
        {
            if (log == null) log = delegate { };
            long freed = 0;
            foreach (StorageItem it in items)
            {
                if (it == null || it.Free == null) continue;
                if (it.Safety > maxSafety) continue;              // garde-fou : jamais au-delà du cran
                if (it.Safety >= SafePerso) continue;             // ceinture ET bretelles
                try { freed += it.Free(log); }
                catch (Exception ex) { log(it.Name + " : " + ex.Message, 2); }
            }
            try { AppStats.Invalidate(); } catch { }
            return freed;
        }

        /// <summary>Total récupérable (Mo) jusqu'au cran de sûreté demandé. Utilisé par le Copilote.</summary>
        public static long TotalRecoverableMB(int maxSafety)
        {
            long mb = 0;
            try
            {
                foreach (Sys.CleanTarget t in Sys.CleanTargets())
                    if (t.Safety <= maxSafety) mb += t.SizeMB;
            }
            catch { }
            return mb;
        }

        // ------------------------------------------------------------------
        //  Disques
        // ------------------------------------------------------------------
        internal class DriveView
        {
            public string Letter;      // « C: »
            public string Label;
            public long TotalMB, FreeMB;
            public long UsedMB { get { return Math.Max(0, TotalMB - FreeMB); } }
            public int FreePct { get { return TotalMB > 0 ? (int)Math.Round(FreeMB * 100.0 / TotalMB) : 0; } }
            public bool IsSystem;

            /// <summary>0 = confortable, 1 = ça se remplit, 2 = critique. Mêmes seuils que « Jeux &amp;
            /// disques » — mais la règle absolue (« moins de 15 Go libres ») ne vaut QUE pour un vrai
            /// disque de travail : appliquée à une partition de 256 Mo, elle la déclarait toujours
            /// critique alors qu'elle est aux trois quarts vide.</summary>
            public int Level
            {
                get
                {
                    double freeGb = FreeMB / 1024.0, totGb = TotalMB / 1024.0;
                    if (FreePct < 8) return 2;
                    if (totGb >= 64 && freeGb < 15) return 2;
                    if (FreePct < 15) return 1;
                    return 0;
                }
            }
        }

        public static List<DriveView> Drives()
        {
            var list = new List<DriveView>();
            string sysRoot = null;
            try { sysRoot = Path.GetPathRoot(Environment.SystemDirectory); } catch { }
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                        // Partitions techniques (EFI, récupération) : rien à y gérer, elles ne font
                        // qu'encombrer la vue.
                        if (d.TotalSize < 2L * 1024 * 1024 * 1024) continue;
                        list.Add(new DriveView
                        {
                            Letter = d.Name.TrimEnd('\\'),
                            Label = string.IsNullOrEmpty(d.VolumeLabel) ? "" : d.VolumeLabel,
                            TotalMB = d.TotalSize / 1048576,
                            FreeMB = d.AvailableFreeSpace / 1048576,
                            IsSystem = sysRoot != null && string.Equals(d.Name, sysRoot, StringComparison.OrdinalIgnoreCase)
                        });
                    }
                    catch { }
                }
            }
            catch { }
            list.Sort(delegate (DriveView a, DriveView b)
            {
                if (a.IsSystem != b.IsSystem) return a.IsSystem ? -1 : 1;   // le disque système en tête
                return string.Compare(a.Letter, b.Letter, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        /// <summary>Le disque système, ou null s'il n'est pas lisible.</summary>
        public static DriveView SystemDrive()
        {
            foreach (DriveView d in Drives()) if (d.IsSystem) return d;
            return null;
        }

        // ------------------------------------------------------------------
        //  Gros fichiers — vidéos, archives, tout le reste
        // ------------------------------------------------------------------
        internal class FileEntry
        {
            public string Path, Dir, Name, Ext;
            public string Cat;             // videos | archives | bigfiles (= l'Id du module)
            public long SizeMB;
            public DateTime Modified;      // MinValue = inconnue

            public string SizeText { get { return Human(SizeMB); } }

            public string AgeText
            {
                get
                {
                    if (Modified == DateTime.MinValue) return "";
                    int d = Math.Max(0, (int)(DateTime.Now - Modified).TotalDays);
                    if (d < 1) return "aujourd'hui";
                    if (d < 30) return "il y a " + d + " j";
                    if (d < 365) return "il y a " + (d / 30) + " mois";
                    return "il y a " + (d / 365) + " an(s)";
                }
            }
        }

        private static readonly HashSet<string> VideoExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v",
            ".mpg", ".mpeg", ".ts", ".m2ts", ".vob", ".3gp"
        };

        private static readonly HashSet<string> ArchiveExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".iso", ".tar", ".gz", ".bz2", ".xz", ".tgz",
            ".cab", ".img", ".vhd", ".vhdx", ".wim"
        };

        // Dossiers jamais parcourus : le système (couvert par les modules Windows), les
        // bibliothèques de jeux (couvertes par « Jeux dormants » — supprimer un fichier DANS un
        // jeu le casserait) et les dossiers techniques.
        private static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "windows", "program files", "program files (x86)", "programdata", "appdata",
            "$recycle.bin", "system volume information", "recovery", "perflogs", "windows.old",
            "$windows.~bt", "steamapps", "xboxgames", "epic games", "riot games", "battle.net",
            "node_modules"
        };

        // Fichier OneDrive « en ligne seulement » : sa taille annoncée ne vit pas sur le disque —
        // le proposer serait mentir sur le gain.
        private const FileAttributes CloudRecall = (FileAttributes)0x400000;   // RECALL_ON_DATA_ACCESS

        /// <summary>Balaye tous les disques fixes à la recherche des fichiers ≥ <paramref name="minMB"/>,
        /// hors système, hors dossiers d'applications/jeux (<paramref name="excludeRoots"/> — leurs
        /// fichiers appartiennent à leur application, pas à un tri manuel). Largeur d'abord et borné
        /// dans le temps : les gros fichiers « de surface » (Téléchargements, Vidéos, racines) sortent
        /// en premier. <paramref name="partial"/> = vrai si un cap a été atteint.</summary>
        public static List<FileEntry> ScanBigFiles(int minMB, List<string> excludeRoots, out bool partial)
        {
            partial = false;
            var list = new List<FileEntry>();
            long minBytes = (long)Math.Max(1, minMB) * 1048576L;

            var roots = new List<string>();
            if (excludeRoots != null)
                foreach (string r in excludeRoots)
                    if (!string.IsNullOrEmpty(r)) roots.Add(r.TrimEnd('\\') + "\\");

            foreach (DriveView dv in Drives())
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var queue = new Queue<string>();
                queue.Enqueue(dv.Letter + "\\");
                while (queue.Count > 0)
                {
                    if (sw.ElapsedMilliseconds > 12000 || list.Count >= 5000) { partial = true; break; }
                    string dir = queue.Dequeue();
                    try
                    {
                        foreach (string f in Directory.GetFiles(dir))
                        {
                            try
                            {
                                var fi = new FileInfo(f);
                                if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System
                                                      | FileAttributes.Offline | CloudRecall)) != 0) continue;
                                if (fi.Length < minBytes) continue;
                                string ext = fi.Extension;
                                list.Add(new FileEntry
                                {
                                    Path = f, Dir = dir.TrimEnd('\\'), Name = fi.Name, Ext = ext,
                                    Cat = VideoExt.Contains(ext) ? "videos" : ArchiveExt.Contains(ext) ? "archives" : "bigfiles",
                                    SizeMB = fi.Length / 1048576, Modified = fi.LastWriteTime
                                });
                            }
                            catch { }
                        }
                        foreach (string d in Directory.GetDirectories(dir))
                        {
                            try
                            {
                                var di = new DirectoryInfo(d);
                                if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                                string nm = di.Name;
                                if (nm.Length == 0 || nm[0] == '.' || SkipDirs.Contains(nm)) continue;
                                if (UnderAny(d, roots)) continue;
                                queue.Enqueue(d);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            list.Sort(delegate (FileEntry a, FileEntry b) { return b.SizeMB.CompareTo(a.SizeMB); });
            return list;
        }

        private static bool UnderAny(string dir, List<string> roots)
        {
            string probe = dir.TrimEnd('\\') + "\\";
            foreach (string r in roots)
                if (probe.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static StorageItem FileToItem(FileEntry f)
        {
            FileEntry cap = f;
            return new StorageItem
            {
                ModuleId = f.Cat, Key = f.Path, Name = f.Name, Path = f.Dir,
                SizeMB = f.SizeMB, Safety = SafePerso, Tag = f,
                Detail = f.Dir + (f.AgeText.Length > 0 ? "  ·  modifié " + f.AgeText : ""),
                OpenLabel = "Ouvrir l'emplacement",
                Open = delegate { RevealFile(cap.Path); }
            };
        }

        /// <summary>Ouvre l'Explorateur AVEC le fichier sélectionné (pas juste le dossier).</summary>
        public static void RevealFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch { }
        }

        // ------------------------------------------------------------------
        //  Corbeille Windows — la suppression RÉVERSIBLE
        // ------------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCTW
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);

        /// <summary>Envoie des fichiers à la CORBEILLE (récupérables tant qu'elle n'est pas vidée).
        /// C'est la seule suppression de fichiers personnels que ONYX s'autorise : jamais de
        /// File.Delete direct sur les données de l'utilisateur.</summary>
        public static bool RecycleFiles(IEnumerable<string> paths)
        {
            var sb = new StringBuilder();
            foreach (string p in paths)
                if (!string.IsNullOrEmpty(p)) sb.Append(p).Append('\0');
            if (sb.Length == 0) return true;
            sb.Append('\0');   // liste doublement terminée par NUL, exigence de l'API

            var op = new SHFILEOPSTRUCTW
            {
                wFunc = 3,                          // FO_DELETE
                pFrom = sb.ToString(),
                fFlags = 0x40 | 0x10 | 0x4 | 0x400  // ALLOWUNDO | NOCONFIRMATION | SILENT | NOERRORUI
            };
            try { return SHFileOperationW(ref op) == 0 && !op.fAnyOperationsAborted; }
            catch { return false; }
        }

        // ------------------------------------------------------------------
        //  Applications installées
        // ------------------------------------------------------------------
        internal class AppEntry
        {
            public string Name, Publisher, InstallDir, UninstallString, QuietUninstall;
            public long SizeMB;
            public DateTime InstallDate;   // MinValue = inconnue
            public bool Estimated;         // taille venant du registre, pas d'une mesure du dossier

            public string Key { get { return string.IsNullOrEmpty(InstallDir) ? Name : InstallDir; } }
            public string SizeText { get { return Human(SizeMB); } }
            public string DateText
            {
                get
                {
                    return InstallDate == DateTime.MinValue ? "—" : InstallDate.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
                }
            }
        }

        private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string UninstallKey32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

        /// <summary>Inventaire « Programmes et fonctionnalités » AVEC le poids réel sur le disque.
        /// Le registre ment souvent (EstimatedSize absent ou faux) : quand le dossier d'installation
        /// existe, on le MESURE, avec le même cache par date de modif que les jeux dormants.</summary>
        public static List<AppEntry> ScanApps(Action<int, int> progress)
        {
            var found = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
            var roots = new List<RegistryKey>();
            try { roots.Add(Registry.LocalMachine.OpenSubKey(UninstallKey)); } catch { }
            try { roots.Add(Registry.LocalMachine.OpenSubKey(UninstallKey32)); } catch { }
            try { roots.Add(Registry.CurrentUser.OpenSubKey(UninstallKey)); } catch { }
            try { roots.Add(Registry.CurrentUser.OpenSubKey(UninstallKey32)); } catch { }

            foreach (RegistryKey k in roots)
            {
                if (k == null) continue;
                try
                {
                    foreach (string sub in k.GetSubKeyNames())
                    {
                        using (RegistryKey e = k.OpenSubKey(sub))
                        {
                            AppEntry a = ReadEntry(e);
                            if (a == null) continue;
                            AppEntry prev;
                            // Une même appli apparaît souvent dans deux ruches : on garde celle qui
                            // connaît son dossier d'installation.
                            if (found.TryGetValue(a.Name, out prev) && !string.IsNullOrEmpty(prev.InstallDir)) continue;
                            found[a.Name] = a;
                        }
                    }
                }
                catch { }
                finally { try { k.Dispose(); } catch { } }
            }

            var list = new List<AppEntry>(found.Values);
            Dictionary<string, string> cache = LoadCache(AppCachePath);
            var measured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < list.Count; i++)
            {
                AppEntry a = list[i];
                if (progress != null) { try { progress(i + 1, list.Count); } catch { } }
                if (!string.IsNullOrEmpty(a.InstallDir) && Measurable(a.InstallDir) && measured.Add(a.InstallDir))
                {
                    long real = CachedDirMB(a.InstallDir, cache);
                    // Une mesure ratée (dossier illisible) ne doit pas écraser l'estimation du registre.
                    if (real > 0) { a.SizeMB = real; a.Estimated = false; }
                }
            }
            SaveCache(AppCachePath, cache);

            var keep = new List<AppEntry>();
            foreach (AppEntry a in list) if (a.SizeMB > 0) keep.Add(a);
            keep.Sort(delegate (AppEntry x, AppEntry y) { return y.SizeMB.CompareTo(x.SizeMB); });
            return keep;
        }

        /// <summary>Les dossiers d'installation connus (registre seul, AUCUNE mesure — instantané).
        /// Sert à exclure les fichiers des applications et des jeux du balayage « gros fichiers » :
        /// un .pak de 40 Go dans un jeu appartient au jeu, pas à un tri manuel.</summary>
        public static List<string> AppRoots()
        {
            var dirs = new List<string>();
            var roots = new List<RegistryKey>();
            try { roots.Add(Registry.LocalMachine.OpenSubKey(UninstallKey)); } catch { }
            try { roots.Add(Registry.LocalMachine.OpenSubKey(UninstallKey32)); } catch { }
            try { roots.Add(Registry.CurrentUser.OpenSubKey(UninstallKey)); } catch { }
            try { roots.Add(Registry.CurrentUser.OpenSubKey(UninstallKey32)); } catch { }
            foreach (RegistryKey k in roots)
            {
                if (k == null) continue;
                try
                {
                    foreach (string sub in k.GetSubKeyNames())
                        using (RegistryKey e = k.OpenSubKey(sub))
                        {
                            if (e == null) continue;
                            string loc = Convert.ToString(e.GetValue("InstallLocation"));
                            if (string.IsNullOrWhiteSpace(loc)) continue;
                            loc = loc.Trim().Trim('"').TrimEnd('\\');
                            if (loc.Length > 6) dirs.Add(loc);   // jamais une racine de disque
                        }
                }
                catch { }
                finally { try { k.Dispose(); } catch { } }
            }
            return dirs;
        }

        private static AppEntry ReadEntry(RegistryKey e)
        {
            if (e == null) return null;
            string name = Convert.ToString(e.GetValue("DisplayName"));
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (Convert.ToString(e.GetValue("SystemComponent")) == "1") return null;
            // Correctifs et mises à jour : ce sont des entrées rattachées, pas des applications.
            if (!string.IsNullOrEmpty(Convert.ToString(e.GetValue("ParentKeyName")))) return null;
            string rel = Convert.ToString(e.GetValue("ReleaseType")) ?? "";
            if (rel.IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0
                || rel.IndexOf("Hotfix", StringComparison.OrdinalIgnoreCase) >= 0) return null;

            var a = new AppEntry();
            a.Name = name.Trim();
            a.Publisher = (Convert.ToString(e.GetValue("Publisher")) ?? "").Trim();
            a.UninstallString = Convert.ToString(e.GetValue("UninstallString"));
            a.QuietUninstall = Convert.ToString(e.GetValue("QuietUninstallString"));

            string loc = Convert.ToString(e.GetValue("InstallLocation"));
            if (!string.IsNullOrWhiteSpace(loc))
            {
                loc = loc.Trim().Trim('"').TrimEnd('\\');
                try { if (Directory.Exists(loc)) a.InstallDir = loc; } catch { }
            }

            // EstimatedSize est en Ko et vaut ce qu'il vaut : c'est le repli quand le dossier est
            // inconnu (installeurs MSI qui éparpillent leurs fichiers).
            try
            {
                object es = e.GetValue("EstimatedSize");
                if (es != null)
                {
                    long kb = Convert.ToInt64(es);
                    if (kb > 0) { a.SizeMB = kb / 1024; a.Estimated = true; }
                }
            }
            catch { }

            try
            {
                string d = Convert.ToString(e.GetValue("InstallDate"));
                DateTime dt;
                if (!string.IsNullOrEmpty(d) && d.Length == 8
                    && DateTime.TryParseExact(d, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                    a.InstallDate = dt;
            }
            catch { }

            return a;
        }

        /// <summary>Refuse de mesurer (et a fortiori de toucher) les dossiers RACINES : mesurer
        /// « C:\ » ou « C:\Program Files » attribuerait tout le disque à une seule application.</summary>
        private static bool Measurable(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || dir.Length < 8) return false;
                string norm = dir.TrimEnd('\\').ToLowerInvariant();
                string root = (Path.GetPathRoot(dir) ?? "").TrimEnd('\\').ToLowerInvariant();
                if (norm == root) return false;
                foreach (string f in Forbidden())
                    if (norm == f.TrimEnd('\\').ToLowerInvariant()) return false;
                return Directory.Exists(dir);
            }
            catch { return false; }
        }

        private static IEnumerable<string> Forbidden()
        {
            var l = new List<string>();
            try { l.Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows)); } catch { }
            try { l.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)); } catch { }
            try { l.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)); } catch { }
            try { l.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)); } catch { }
            try { l.Add(Environment.GetEnvironmentVariable("USERPROFILE")); } catch { }
            try { l.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)); } catch { }
            try { l.Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)); } catch { }
            var clean = new List<string>();
            foreach (string s in l) if (!string.IsNullOrEmpty(s)) clean.Add(s);
            return clean;
        }

        /// <summary>Désinstalle par le désinstalleur OFFICIEL de l'application. Jamais de suppression
        /// de dossier : c'est le seul moyen de ne pas laisser une installation à moitié morte.</summary>
        public static bool Uninstall(AppEntry a, Action<string, int> log)
        {
            if (a == null) return false;
            string cmd = !string.IsNullOrEmpty(a.QuietUninstall) ? a.QuietUninstall : a.UninstallString;
            if (string.IsNullOrWhiteSpace(cmd)) { if (log != null) log(a.Name + " : aucune commande de désinstallation déclarée.", 2); return false; }
            try
            {
                string file, args;
                SplitCommand(cmd, out file, out args);
                var psi = new System.Diagnostics.ProcessStartInfo(file, args);
                psi.UseShellExecute = true;
                System.Diagnostics.Process.Start(psi);
                if (log != null) log("Désinstallation lancée : " + a.Name + " (" + a.SizeText + " à récupérer).", 0);
                return true;
            }
            catch (Exception ex)
            {
                if (log != null) log(a.Name + " : " + ex.Message, 3);
                return false;
            }
        }

        // « "C:\...\unins000.exe" /SILENT » → fichier + arguments.
        private static void SplitCommand(string cmd, out string file, out string args)
        {
            cmd = cmd.Trim();
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                if (end > 0)
                {
                    file = cmd.Substring(1, end - 1);
                    args = cmd.Substring(end + 1).Trim();
                    return;
                }
            }
            int sp = cmd.IndexOf(".exe ", StringComparison.OrdinalIgnoreCase);
            if (sp > 0) { file = cmd.Substring(0, sp + 4); args = cmd.Substring(sp + 5).Trim(); return; }
            file = cmd; args = "";
        }

        // ------------------------------------------------------------------
        //  Mesure de dossiers (bornée + mise en cache)
        // ------------------------------------------------------------------
        private static string AppCachePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-appsize.txt"); }
        }

        private static long CachedDirMB(string dir, Dictionary<string, string> cache)
        {
            string stamp;
            try
            {
                if (!Directory.Exists(dir)) return 0;
                stamp = Directory.GetLastWriteTimeUtc(dir).Ticks.ToString(CultureInfo.InvariantCulture);
            }
            catch { return 0; }

            string hit;
            if (cache.TryGetValue(dir, out hit))
            {
                int bar = hit.IndexOf('|');
                if (bar > 0 && hit.Substring(0, bar) == stamp)
                {
                    long cached;
                    if (long.TryParse(hit.Substring(bar + 1), out cached)) return cached;
                }
            }

            long mb = DirBytes(dir, 0) / 1048576;
            cache[dir] = stamp + "|" + mb.ToString(CultureInfo.InvariantCulture);
            return mb;
        }

        // Récursion bornée en profondeur, points de jonction ignorés (déjà comptés ailleurs).
        private static long DirBytes(string dir, int depth)
        {
            if (depth > 12) return 0;
            long total = 0;
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                    try { total += new FileInfo(f).Length; } catch { }
                foreach (string d in Directory.GetDirectories(dir))
                {
                    try
                    {
                        var di = new DirectoryInfo(d);
                        if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch { }
                    total += DirBytes(d, depth + 1);
                }
            }
            catch { }
            return total;
        }

        /// <summary>Taille en Mo PLAFONNÉE dans le temps : sur un dossier personnel de plusieurs
        /// centaines de milliers de fichiers, mieux vaut un ordre de grandeur tout de suite qu'une
        /// interface figée. −1 si le dossier n'existe pas.</summary>
        public static long DirSizeCappedMb(string path, int capMs)
        {
            long bytes = 0;
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return -1;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var stack = new Stack<string>();
                stack.Push(path);
                while (stack.Count > 0 && sw.ElapsedMilliseconds < capMs)
                {
                    string d = stack.Pop();
                    try
                    {
                        foreach (string f in Directory.EnumerateFiles(d)) { try { bytes += new FileInfo(f).Length; } catch { } }
                        foreach (string sub in Directory.EnumerateDirectories(d)) stack.Push(sub);
                    }
                    catch { }
                }
            }
            catch { }
            return bytes / 1048576;
        }

        // ------------------------------------------------------------------
        //  Sondes Windows
        // ------------------------------------------------------------------
        private static string DismCachePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-storage-dism.txt"); }
        }

        // DISM /AnalyzeComponentStore coûte 10 à 60 secondes : son verdict est mis en cache 24 h.
        private static bool DismRecommendedCached()
        {
            try
            {
                if (File.Exists(DismCachePath))
                {
                    string[] p = File.ReadAllText(DismCachePath).Trim().Split('|');
                    long ticks;
                    if (p.Length == 2 && long.TryParse(p[0], out ticks))
                    {
                        var when = new DateTime(ticks);
                        if ((DateTime.UtcNow - when).TotalHours < 24) return p[1] == "1";
                    }
                }
            }
            catch { }

            bool reco = false;
            try
            {
                string outp = Run("Dism.exe", "/Online /Cleanup-Image /AnalyzeComponentStore", 90000);
                reco = outp != null && UtilityTools.DismRecommended(outp);
            }
            catch { }
            try { File.WriteAllText(DismCachePath, DateTime.UtcNow.Ticks + "|" + (reco ? "1" : "0")); } catch { }
            return reco;
        }

        // Espace occupé par les points de restauration (vssadmin). La sortie est localisée : on
        // cherche donc un nombre suivi d'une unité sur la ligne « utilisé / used », sans se fier
        // aux libellés exacts.
        private static long ShadowStorageMb(string sysRoot)
        {
            try
            {
                string letter = (sysRoot ?? "C:\\").TrimEnd('\\');
                string outp = Run("vssadmin.exe", "list shadowstorage /for=" + letter, 20000);
                if (string.IsNullOrEmpty(outp)) return 0;
                foreach (string line in outp.Split('\n'))
                {
                    string low = line.ToLowerInvariant();
                    if (low.IndexOf("utilis", StringComparison.Ordinal) < 0 && low.IndexOf("used", StringComparison.Ordinal) < 0) continue;
                    Match mm = Regex.Match(line, @"([\d][\d\s.,]*)\s*(To|TB|Go|GB|Mo|MB|Ko|KB)", RegexOptions.IgnoreCase);
                    if (!mm.Success) continue;
                    double v = ParseNum(mm.Groups[1].Value);
                    string unit = mm.Groups[2].Value.ToLowerInvariant();
                    if (unit == "to" || unit == "tb") return (long)(v * 1024 * 1024);
                    if (unit == "go" || unit == "gb") return (long)(v * 1024);
                    if (unit == "mo" || unit == "mb") return (long)v;
                    return (long)(v / 1024);
                }
            }
            catch { }
            return 0;
        }

        // « 1 234,56 » / « 1,234.56 » : on ne sait pas dans quelle langue Windows répond.
        private static double ParseNum(string s)
        {
            try
            {
                s = (s ?? "").Replace(" ", "").Replace("\u00A0", "").Trim();
                int lastDot = s.LastIndexOf('.'), lastCom = s.LastIndexOf(',');
                if (lastDot >= 0 && lastCom >= 0)
                {
                    // Le séparateur décimal est le DERNIER des deux ; l'autre groupe les milliers.
                    if (lastDot > lastCom) s = s.Replace(",", "");
                    else s = s.Replace(".", "").Replace(',', '.');
                }
                else s = s.Replace(',', '.');
                double v;
                return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : 0;
            }
            catch { return 0; }
        }

        private static double SysFreeGb()
        {
            try { return new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)).AvailableFreeSpace / 1073741824.0; }
            catch { return -1; }
        }

        // Lecture LIGNE À LIGNE avec chrono : un ReadToEnd sur DISM ou vssadmin peut ne jamais rendre
        // la main, et on est sur un thread de fond qui alimente l'interface. Même pattern que
        // ChatActions.RunProc. On ne redirige PAS stderr : sans lecteur dédié il peut saturer.
        private static string Run(string file, string args, int timeoutMs)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    var sb = new StringBuilder();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (!p.StandardOutput.EndOfStream)
                    {
                        if (sw.ElapsedMilliseconds > timeoutMs) { try { p.Kill(); } catch { } return null; }
                        string line = p.StandardOutput.ReadLine();
                        if (line == null) break;
                        sb.Append(line).Append('\n');
                    }
                    if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } }
                    return sb.ToString();
                }
            }
            catch { return null; }
        }

        private static void Launch(string file, string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(file, args);
                psi.UseShellExecute = true;
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }

        public static void OpenFolder(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
            }
            catch { }
        }

        // ------------------------------------------------------------------
        //  Format
        // ------------------------------------------------------------------
        public static string Human(long mb)
        {
            if (mb <= 0) return "—";
            if (mb >= 1048576) return (mb / 1048576.0).ToString("0.00", CultureInfo.CurrentCulture) + " To";
            if (mb >= 1024) return (mb / 1024.0).ToString("0.0", CultureInfo.CurrentCulture) + " Go";
            return mb.ToString(CultureInfo.CurrentCulture) + " Mo";
        }

        // ------------------------------------------------------------------
        //  Cache clé=valeur (même format que bt-gamesize.txt)
        // ------------------------------------------------------------------
        private static Dictionary<string, string> LoadCache(string path)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return d;
                foreach (string line in File.ReadAllLines(path))
                {
                    int eq = line.LastIndexOf('=');
                    if (eq > 0) d[line.Substring(0, eq)] = line.Substring(eq + 1);
                }
            }
            catch { }
            return d;
        }

        private static void SaveCache(string path, Dictionary<string, string> cache)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (KeyValuePair<string, string> kv in cache)
                    sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
                File.WriteAllText(path, sb.ToString());
            }
            catch { }
        }
    }

    // ----------------------------------------------------------------------
    //  Réglages du centre de stockage — bt-stockage.txt
    // ----------------------------------------------------------------------
    /// <summary>
    /// Tout est modulable : quels modules analyser, jusqu'où va « TOUT LIBÉRER », ce qu'on ne veut
    /// plus jamais se voir proposer, et à partir de quel taux de remplissage ONYX alerte de
    /// lui-même. Un fichier texte à côté de l'exe, comme le reste des réglages de l'app.
    /// </summary>
    internal class StorageSettings
    {
        /// <summary>Cran maximal inclus dans une libération en lot. Jamais au-delà de 1 : le
        /// niveau 2 (données perso) n'est pas atteignable, par conception.</summary>
        public int MaxSafety = Storage.SafeSuperflu;

        /// <summary>Sous ce pourcentage de libre, ONYX le signale tout seul.</summary>
        public int AlertPct = 12;

        /// <summary>Taille (Mo) à partir de laquelle un fichier compte comme « gros » pour le
        /// balayage vidéos / archives / autres.</summary>
        public int MinFileMB = 100;

        private readonly HashSet<string> _off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static string ConfigPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-stockage.txt"); }
        }

        public bool IsEnabled(string moduleId) { return !_off.Contains(moduleId ?? ""); }

        public void SetEnabled(string moduleId, bool on)
        {
            if (string.IsNullOrEmpty(moduleId)) return;
            if (on) _off.Remove(moduleId); else _off.Add(moduleId);
        }

        public bool IsIgnored(string key) { return !string.IsNullOrEmpty(key) && _ignored.Contains(key); }

        public void Ignore(string key) { if (!string.IsNullOrEmpty(key)) _ignored.Add(key); }

        public void Unignore(string key) { if (!string.IsNullOrEmpty(key)) _ignored.Remove(key); }

        public void ClearIgnored() { _ignored.Clear(); }

        public List<string> IgnoredList { get { return new List<string>(_ignored); } }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("safety=").Append(MaxSafety).Append('\n');
                sb.Append("alerte=").Append(AlertPct).Append('\n');
                sb.Append("minfile=").Append(MinFileMB).Append('\n');
                sb.Append("off=").Append(string.Join(";", new List<string>(_off).ToArray())).Append('\n');
                sb.Append("ignore=").Append(string.Join(";", new List<string>(_ignored).ToArray())).Append('\n');
                File.WriteAllText(ConfigPath, sb.ToString());
            }
            catch { }
        }

        public static StorageSettings Load()
        {
            var s = new StorageSettings();
            try
            {
                if (!File.Exists(ConfigPath)) return s;
                foreach (string line in File.ReadAllLines(ConfigPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    if (k == "safety") { int n; if (int.TryParse(v, out n)) s.MaxSafety = Math.Max(0, Math.Min(Storage.SafeVerifier, n)); }
                    else if (k == "alerte") { int n; if (int.TryParse(v, out n)) s.AlertPct = Math.Max(0, Math.Min(50, n)); }
                    else if (k == "minfile") { int n; if (int.TryParse(v, out n)) s.MinFileMB = Math.Max(50, Math.Min(4096, n)); }
                    else if (k == "off") foreach (string id in v.Split(';')) if (id.Length > 0) s._off.Add(id);
                    else if (k == "ignore") foreach (string p in v.Split(';')) if (p.Length > 0) s._ignored.Add(p);
                }
            }
            catch { }
            return s;
        }
    }
}
