using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Installation automatique des prérequis des jeux (bibliothèques marquées Essential :
    /// Visual C++, DirectX, .NET, OpenAL). Trois modes persistés dans bt-autoinstall.txt :
    ///   ask  — proposer au démarrage si des prérequis manquent (défaut : « automatique » au
    ///          sens où l'app le propose toute seule, en un clic « tout installer ») ;
    ///   auto — installer en silence au démarrage, sans rien demander ;
    ///   off  — ne rien faire au démarrage.
    /// Tout passe par winget (Microsoft) via LibScan.Install. Réglable en un écran.
    /// </summary>
    internal static class AutoInstall
    {
        public const string ModeAsk = "ask";
        public const string ModeAuto = "auto";
        public const string ModeOff = "off";

        private static string ConfigPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-autoinstall.txt"); }
        }

        /// <summary>Mode courant ; « ask » par défaut (au premier lancement, l'app propose).</summary>
        public static string Mode
        {
            get
            {
                try
                {
                    if (File.Exists(ConfigPath))
                    {
                        string s = File.ReadAllText(ConfigPath).Trim().ToLowerInvariant();
                        if (s == ModeAuto || s == ModeOff || s == ModeAsk) return s;
                    }
                }
                catch { }
                return ModeAsk;
            }
        }

        public static void SetMode(string mode)
        {
            try { File.WriteAllText(ConfigPath, mode); } catch { }
        }

        /// <summary>Prérequis « essentiels » (jeux) absents de ce PC.</summary>
        public static List<LibScan.LibItem> MissingEssentials()
        {
            var list = new List<LibScan.LibItem>();
            try
            {
                foreach (LibScan.LibItem it in LibScan.Items())
                {
                    if (!it.Essential) continue;
                    bool ok = false;
                    try { ok = it.Installed(); } catch { }
                    if (!ok) list.Add(it);
                }
            }
            catch { }
            return list;
        }

        /// <summary>
        /// Installe en arrière-plan la liste via winget ; rappelle onDone(réussis, total) sur
        /// le thread UI. Isolé par item : un échec n'interrompt pas les autres.
        /// </summary>
        public static void InstallInBackground(Control ui, List<LibScan.LibItem> items,
                                               Action<string, int> log, Action<int, int> onDone)
        {
            if (items == null || items.Count == 0) { if (onDone != null) onDone(0, 0); return; }
            string winget = LibScan.WingetPath();
            if (winget == null)
            {
                if (log != null) log("winget introuvable — installe « App Installer » (gratuit, Microsoft Store), puis réessaie.", 2);
                if (onDone != null) onDone(0, items.Count);
                return;
            }
            int total = items.Count;
            Task.Run(() =>
            {
                int ok = 0;
                foreach (LibScan.LibItem it in items)
                {
                    try { if (LibScan.Install(winget, it, log)) ok++; }
                    catch { }
                }
                int done = ok;
                try
                {
                    if (ui != null && !ui.IsDisposed)
                        ui.BeginInvoke((Action)(() => { if (onDone != null) onDone(done, total); }));
                }
                catch { }
            });
        }
    }
}
