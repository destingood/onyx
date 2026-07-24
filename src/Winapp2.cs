using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Moteur « winapp2.ini » en MODE SÛR. Interprète les règles de nettoyage communautaires
    /// (base MoscaDotTo/Winapp2, utilisée par BleachBit et consorts) MAIS avec des garde-fous
    /// stricts, pensés pour un logiciel qu'on vend :
    ///   • JAMAIS de suppression de clés de registre — on ignore purement les directives RegKey
    ///     (même philosophie que BleachBit : inutile et destructeur) ;
    ///   • uniquement des FICHIERS, et uniquement sous des dossiers cache/temp connus
    ///     (garde-fou de racine : rien dans Windows, Program Files, Documents, la racine d'un profil…) ;
    ///   • on ne montre QUE les sections dont l'application est réellement détectée (installée) ;
    ///   • rien n'est coché par défaut, la taille est mesurée et affichée avant toute suppression.
    /// Aucun téléchargement automatique : l'utilisateur charge un fichier local, ou clique lui-même
    /// sur « Télécharger les règles ». ONYX informe et nettoie du cache — il ne bricole rien.
    /// </summary>
    internal static class Winapp2
    {
        public const string SourceUrl = "https://raw.githubusercontent.com/MoscaDotTo/Winapp2/master/Winapp2.ini";

        public sealed class Entry
        {
            public string Name;             // titre affichable (sans le * final)
            public string Warning;          // avertissement winapp2 éventuel
            public bool DefaultOn;          // Default= (on ne coche jamais d'office de toute façon)
            public readonly List<FileKey> Files = new List<FileKey>();
            public long SizeMB;
            public int SkippedUnsafe;       // FileKeys écartés par le garde-fou de racine
        }

        public sealed class FileKey
        {
            public string Folder;           // dossier résolu, validé « racine sûre »
            public string[] Filters;        // motifs (*.log, *.tmp…) ; null => tous les fichiers
            public bool Recurse;
        }

        // ==================================================================
        //  Chargement du fichier de règles
        // ==================================================================
        public static string LocalPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "winapp2.ini"); }
        }

        public static bool HasLocal()
        {
            try { return File.Exists(LocalPath); } catch { return false; }
        }

        /// <summary>Lit un fichier .ini local (BOM détecté) et le parse.</summary>
        public static List<Entry> LoadFile(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            return Parse(Decode(bytes));
        }

        /// <summary>Télécharge la base communautaire (déclenché par l'utilisateur), l'écrit à côté de
        /// l'exe, et renvoie le texte. Lève en cas d'échec réseau.</summary>
        public static string Download()
        {
            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(60);
                http.DefaultRequestHeaders.Add("User-Agent", "ONYX-Optimizer");
                byte[] bytes = http.GetByteArrayAsync(SourceUrl).GetAwaiter().GetResult();
                string text = Decode(bytes);
                try { File.WriteAllText(LocalPath, text, new UTF8Encoding(false)); } catch { }
                return text;
            }
        }

        private static string Decode(byte[] bytes)
        {
            using (var sr = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true))
                return sr.ReadToEnd();
        }

        // ==================================================================
        //  Analyse (parser + détection d'installation)
        // ==================================================================
        public static List<Entry> Parse(string iniText)
        {
            var result = new List<Entry>();
            if (string.IsNullOrEmpty(iniText)) return result;

            string sectionName = null;
            var raw = new List<KeyValuePair<string, string>>();

            foreach (string lineRaw in iniText.Split('\n'))
            {
                string line = lineRaw.Trim();
                if (line.Length == 0 || line[0] == ';') continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    Flush(sectionName, raw, result);
                    sectionName = line.Substring(1, line.Length - 2).Trim();
                    raw.Clear();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                raw.Add(new KeyValuePair<string, string>(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
            }
            Flush(sectionName, raw, result);
            return result;
        }

        private static void Flush(string sectionName, List<KeyValuePair<string, string>> raw, List<Entry> result)
        {
            if (sectionName == null || raw.Count == 0) return;

            // 1) Bornes de système d'exploitation : hors plage => on écarte la section.
            foreach (KeyValuePair<string, string> kv in raw)
                if (kv.Key.StartsWith("DetectOS", StringComparison.OrdinalIgnoreCase) && !OsInRange(kv.Value))
                    return;

            // 2) Détection d'installation : au moins une preuve (registre, fichier, ou app connue).
            //    Pas de preuve du tout => on masque (les sections génériques, larges et risquées,
            //    recoupent déjà notre nettoyage intégré).
            bool hasDetector = false, detected = false;
            foreach (KeyValuePair<string, string> kv in raw)
            {
                string k = kv.Key;
                if (k.StartsWith("DetectFile", StringComparison.OrdinalIgnoreCase))
                { hasDetector = true; if (PathExists(Expand(kv.Value))) detected = true; }
                else if (k.StartsWith("Detect", StringComparison.OrdinalIgnoreCase) && !k.StartsWith("DetectOS", StringComparison.OrdinalIgnoreCase))
                { hasDetector = true; if (RegExists(kv.Value)) detected = true; }
                else if (k.Equals("SpecialDetect", StringComparison.OrdinalIgnoreCase))
                { hasDetector = true; if (SpecialDetect(kv.Value)) detected = true; }
            }
            if (!hasDetector || !detected) return;

            // 3) Construction des cibles FICHIER (les RegKey sont volontairement ignorées).
            var e = new Entry { Name = sectionName.TrimEnd('*').Trim(), DefaultOn = true };
            foreach (KeyValuePair<string, string> kv in raw)
            {
                if (kv.Key.Equals("Warning", StringComparison.OrdinalIgnoreCase)) e.Warning = kv.Value;
                else if (kv.Key.Equals("Default", StringComparison.OrdinalIgnoreCase))
                    e.DefaultOn = !kv.Value.Equals("False", StringComparison.OrdinalIgnoreCase);
                else if (kv.Key.StartsWith("FileKey", StringComparison.OrdinalIgnoreCase))
                    AddFileKey(e, kv.Value);
            }

            if (e.Files.Count > 0) result.Add(e);
        }

        private static void AddFileKey(Entry e, string value)
        {
            // Format : DOSSIER|MOTIF[;MOTIF...][|DRAPEAU...]
            string[] parts = value.Split('|');
            if (parts.Length == 0) return;

            string folder = Expand(parts[0].Trim());
            if (!IsSafeRoot(folder)) { e.SkippedUnsafe++; return; }

            string[] filters = null;
            if (parts.Length >= 2)
            {
                string f = parts[1].Trim();
                if (f.Length > 0 && f != "*" && f != "*.*")
                    filters = f.Split(';');
            }

            bool recurse = false;
            for (int i = 2; i < parts.Length; i++)
                if (parts[i].Trim().Equals("RECURSE", StringComparison.OrdinalIgnoreCase)
                    || parts[i].Trim().Equals("REMOVESELF", StringComparison.OrdinalIgnoreCase))
                    recurse = true;

            e.Files.Add(new FileKey { Folder = folder, Filters = filters, Recurse = recurse });
        }

        // ==================================================================
        //  Mesure & nettoyage
        // ==================================================================
        public static void Measure(Entry e)
        {
            long bytes = 0;
            foreach (FileKey fk in e.Files)
                foreach (string f in EnumFiles(fk))
                {
                    try { bytes += new FileInfo(f).Length; } catch { }
                }
            e.SizeMB = bytes / (1024 * 1024);
        }

        /// <summary>Supprime les fichiers cache de l'entrée. Ignore les fichiers verrouillés.
        /// Ne supprime jamais les dossiers eux-mêmes.</summary>
        public static int Clean(Entry e, Action<string, int> log)
        {
            int removed = 0;
            foreach (FileKey fk in e.Files)
                foreach (string f in EnumFiles(fk))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); removed++; } catch { }
                }
            if (log != null) log("winapp2 — " + e.Name + " : " + removed + " fichier(s) supprimé(s).", 1);
            return removed;
        }

        private const int PerKeyFileCap = 200000;   // garde-fou anti-boucle sur un dossier pathologique

        private static IEnumerable<string> EnumFiles(FileKey fk)
        {
            if (string.IsNullOrEmpty(fk.Folder) || !SafeDirExists(fk.Folder)) yield break;
            SearchOption opt = fk.Recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            if (fk.Filters == null)
            {
                int n = 0;
                foreach (string f in SafeEnum(fk.Folder, "*", opt))
                { if (++n > PerKeyFileCap) yield break; yield return f; }
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pattern in fk.Filters)
            {
                string p = pattern.Trim();
                if (p.Length == 0) continue;
                int n = 0;
                foreach (string f in SafeEnum(fk.Folder, p, opt))
                {
                    if (++n > PerKeyFileCap) break;
                    if (seen.Add(f)) yield return f;
                }
            }
        }

        private static IEnumerable<string> SafeEnum(string folder, string pattern, SearchOption opt)
        {
            IEnumerator<string> it;
            try { it = Directory.EnumerateFiles(folder, pattern, opt).GetEnumerator(); }
            catch { yield break; }
            while (true)
            {
                string cur;
                try { if (!it.MoveNext()) yield break; cur = it.Current; }
                catch { yield break; }   // accès refusé en cours de route
                yield return cur;
            }
        }

        private static bool SafeDirExists(string p)
        {
            try { return Directory.Exists(p); } catch { return false; }
        }

        // ==================================================================
        //  Garde-fou de racine : où a-t-on le DROIT de supprimer des fichiers ?
        // ==================================================================
        private static readonly string[] AllowedRoots = BuildAllowedRoots();
        private static readonly string[] ProtectedExact = BuildProtectedExact();

        private static string[] BuildAllowedRoots()
        {
            var roots = new List<string>();
            AddRoot(roots, Environment.GetEnvironmentVariable("TEMP"));
            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));      // Roaming
            AddRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)); // ProgramData
            AddRoot(roots, Environment.GetEnvironmentVariable("PUBLIC"));
            return roots.ToArray();
        }

        private static string[] BuildProtectedExact()
        {
            var p = new List<string>();
            AddRoot(p, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            AddRoot(p, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            AddRoot(p, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            AddRoot(p, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            AddRoot(p, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddRoot(p, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddRoot(p, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            AddRoot(p, Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            AddRoot(p, Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            AddRoot(p, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            return p.ToArray();
        }

        private static void AddRoot(List<string> list, string path)
        {
            string n = Norm(path);
            if (n != null && !list.Contains(n)) list.Add(n);
        }

        private static string Norm(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                string full = Path.GetFullPath(path);
                full = full.TrimEnd('\\', '/');
                return full.ToLowerInvariant();
            }
            catch { return null; }
        }

        /// <summary>Vrai seulement si le dossier est un SOUS-dossier (≥ 1 niveau) d'une racine cache
        /// autorisée, et n'est pas lui-même un emplacement protégé. Sinon on refuse la suppression.</summary>
        private static bool IsSafeRoot(string folder)
        {
            string n = Norm(folder);
            if (n == null || n.Length < 4) return false;

            foreach (string prot in ProtectedExact)
                if (n == prot) return false;

            foreach (string root in AllowedRoots)
            {
                if (n == root) continue;                       // la racine elle-même : interdit
                if (n.StartsWith(root + "\\", StringComparison.Ordinal))
                {
                    // Exiger au moins un niveau de sous-dossier réel (déjà garanti par le "\\"),
                    // et refuser un chemin qui remonterait (aucun ".." après normalisation).
                    return n.IndexOf("..", StringComparison.Ordinal) < 0;
                }
            }
            return false;
        }

        // ==================================================================
        //  Détection : registre / fichier / applications connues / OS
        // ==================================================================
        private static bool RegExists(string keyPath)
        {
            try
            {
                RegistryHive hive; string sub;
                if (!SplitHive(keyPath, out hive, out sub)) return false;
                using (RegistryKey b32 = RegistryKey.OpenBaseKey(hive, RegistryView.Registry32))
                using (RegistryKey k32 = b32.OpenSubKey(sub))
                    if (k32 != null) return true;
                using (RegistryKey b64 = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (RegistryKey k64 = b64.OpenSubKey(sub))
                    return k64 != null;
            }
            catch { return false; }
        }

        private static bool SplitHive(string keyPath, out RegistryHive hive, out string sub)
        {
            hive = RegistryHive.LocalMachine; sub = null;
            if (string.IsNullOrEmpty(keyPath)) return false;
            int slash = keyPath.IndexOf('\\');
            if (slash <= 0) return false;
            string root = keyPath.Substring(0, slash).ToUpperInvariant();
            sub = keyPath.Substring(slash + 1);
            switch (root)
            {
                case "HKCU": case "HKEY_CURRENT_USER": hive = RegistryHive.CurrentUser; return true;
                case "HKLM": case "HKEY_LOCAL_MACHINE": hive = RegistryHive.LocalMachine; return true;
                case "HKCR": case "HKEY_CLASSES_ROOT": hive = RegistryHive.ClassesRoot; return true;
                case "HKU": case "HKEY_USERS": hive = RegistryHive.Users; return true;
                default: return false;
            }
        }

        private static bool PathExists(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return false;
                return File.Exists(path) || Directory.Exists(path);
            }
            catch { return false; }
        }

        private static bool SpecialDetect(string token)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roam = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            switch ((token ?? "").ToUpperInvariant())
            {
                case "DET_CHROME": return Directory.Exists(Path.Combine(local, @"Google\Chrome\User Data"))
                                       || Directory.Exists(Path.Combine(local, @"Chromium\User Data"));
                case "DET_MOZILLA": return Directory.Exists(Path.Combine(roam, @"Mozilla\Firefox"));
                case "DET_THUNDERBIRD": return Directory.Exists(Path.Combine(roam, "Thunderbird"));
                case "DET_OPERA": return Directory.Exists(Path.Combine(roam, "Opera Software"))
                                     || Directory.Exists(Path.Combine(local, "Opera Software"));
                case "DET_JAVA": return Directory.Exists(Path.Combine(roam, @"Sun\Java"));
                case "DET_WINDOWS": return true;
                default: return false;   // app inconnue => on masque, par sécurité
            }
        }

        private static bool OsInRange(string spec)
        {
            // Format : min|max en version « major.minor » (ex. 6.1|10.0). Vide = pas de borne.
            try
            {
                string[] mm = (spec ?? "").Split('|');
                double cur = Environment.OSVersion.Version.Major + Environment.OSVersion.Version.Minor / 10.0;
                double v;
                if (mm.Length >= 1 && double.TryParse(mm[0].Trim(),
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)
                    && v > 0 && cur + 0.001 < v) return false;
                if (mm.Length >= 2 && double.TryParse(mm[1].Trim(),
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)
                    && v > 0 && cur - 0.001 > v) return false;
                return true;
            }
            catch { return true; }
        }

        // ==================================================================
        //  Expansion des variables winapp2 + variables d'environnement
        // ==================================================================
        private static string Expand(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string s = raw;
            s = Replace(s, "%AppData%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            s = Replace(s, "%LocalAppData%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            s = Replace(s, "%LocalLowAppData%", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow"));
            s = Replace(s, "%CommonAppData%", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            s = Replace(s, "%ProgramData%", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            s = Replace(s, "%ProgramFiles%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            s = Replace(s, "%CommonProgramFiles%", Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles));
            s = Replace(s, "%Documents%", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            s = Replace(s, "%Pictures%", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            s = Replace(s, "%Music%", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            s = Replace(s, "%Video%", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            s = Replace(s, "%UserProfile%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            s = Replace(s, "%Public%", Environment.GetEnvironmentVariable("PUBLIC"));
            s = Replace(s, "%SystemDrive%", (Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\").TrimEnd('\\'));
            s = Replace(s, "%SystemRoot%", Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            s = Replace(s, "%WinDir%", Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            s = Replace(s, "%Temp%", Environment.GetEnvironmentVariable("TEMP"));
            try { s = Environment.ExpandEnvironmentVariables(s); } catch { }
            return s;
        }

        private static string Replace(string s, string token, string value)
        {
            if (string.IsNullOrEmpty(value)) return s;
            int idx = s.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                s = s.Substring(0, idx) + value + s.Substring(idx + token.Length);
                idx = s.IndexOf(token, idx + value.Length, StringComparison.OrdinalIgnoreCase);
            }
            return s;
        }
    }
}
