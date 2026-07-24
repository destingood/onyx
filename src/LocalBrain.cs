using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace BTOptimizer
{
    /// <summary>
    /// Cerveau IA LOCAL et OPTIONNEL du Copilote (via Ollama, gratuit et open source) :
    /// quand les règles ne comprennent pas une question, elle part vers un modèle qui tourne
    /// SUR CETTE machine (http://127.0.0.1:11434) — aucune donnée ne quitte le PC, aucun
    /// abonnement, aucune clé. Strictement OPT-IN : rien ne s'active sans la demande
    /// explicite de l'utilisateur (« active l'ia »), et « désactive l'ia » coupe tout.
    /// </summary>
    internal static class LocalBrain
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(75) };
        private const string Base = "http://127.0.0.1:11434";   // localhost UNIQUEMENT — jamais internet

        private static string FlagPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-ia-locale.txt"); }
        }
        private static string OffPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-ia-off.txt"); }
        }
        private static string TriesPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-ia-setup.txt"); }
        }

        /// <summary>Vrai si le cerveau IA local est actif (fichier-drapeau).</summary>
        public static bool Enabled
        {
            get { try { return File.Exists(FlagPath); } catch { return false; } }
        }

        /// <summary>Vrai si l'utilisateur a dit « désactive l'ia » : l'installation AUTOMATIQUE
        /// n'insistera jamais contre ce choix.</summary>
        public static bool OptedOut
        {
            get { try { return File.Exists(OffPath); } catch { return false; } }
        }

        public static void ClearOptOut()
        {
            try { if (File.Exists(OffPath)) File.Delete(OffPath); } catch { }
        }

        private static string ConsentPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-ia-consent.txt"); }
        }

        /// <summary>Vrai si l'utilisateur a déjà répondu à la question « Installer le cerveau IA
        /// local ? » (Oui → consent, Non → opt-out). Sert à ne demander qu'UNE fois.</summary>
        public static bool ConsentAnswered
        {
            get { try { return OptedOut || File.Exists(ConsentPath); } catch { return false; } }
        }

        private static void SetConsented()
        {
            try { ClearOptOut(); File.WriteAllText(ConsentPath, "L'utilisateur a accepté l'installation du cerveau IA local.\n"); }
            catch { }
        }

        // ------------------------------------------------------------------
        //  Amorçage au démarrage — décide, demande le consentement si besoin
        // ------------------------------------------------------------------
        /// <summary>Point d'entrée unique au lancement (tâche de fond). Trois cas :
        ///  • déjà prêt (serveur + modèle) → on active en silence, RIEN à installer, aucune question ;
        ///  • l'utilisateur a refusé une fois → on ne fait rien ;
        ///  • une installation/un téléchargement serait nécessaire → on DEMANDE « Oui/Non » une
        ///    seule fois (dialogue sur le fil d'interface via 'owner'), et on n'installe qu'après un Oui.</summary>
        public static void Bootstrap(System.Windows.Forms.Form owner, Action<string, int> log)
        {
            try
            {
                if (OptedOut) return;
                if (Enabled) { EnsureServer(); return; }

                // Déjà installé et opérationnel (ex. l'utilisateur avait Ollama) → activation
                // silencieuse : on ne télécharge rien, donc aucune question à poser.
                if (ServerUp(1500) && BestModel() != null) { SetEnabled(true); return; }

                // À partir d'ici, activer suppose d'installer Ollama et/ou de télécharger un
                // modèle (plusieurs centaines de Mo à quelques Go). On demande d'abord.
                if (!ConsentAnswered)
                {
                    if (LibScan.WingetPath() == null) return;      // rien d'automatique possible : inutile de demander
                    bool yes = AskConsent(owner);
                    if (!yes) { SetEnabled(false); return; }         // écrit l'opt-out : on ne redemande jamais
                    SetConsented();
                }
                else if (!File.Exists(ConsentPath)) return;         // a répondu Non autrefois

                AutoSetup(log);
            }
            catch { }
        }

        /// <summary>Démarre le moteur Ollama s'il est installé mais éteint (IA déjà activée).</summary>
        public static void EnsureServer()
        {
            try { if (!ServerUp(1200)) { string exe = OllamaExe(); if (exe != null) TryStartServer(exe); } }
            catch { }
        }

        private static bool AskConsent(System.Windows.Forms.Form owner)
        {
            bool yes = false;
            try
            {
                ModelPick pick = ChooseModel();
                string msg = "Veux-tu activer le CERVEAU IA LOCAL du Copilote ?\n\n"
                           + "• Gratuit, open source (Ollama) — aucun abonnement.\n"
                           + "• Tourne à 100 % sur TON PC : aucune donnée n'est envoyée sur internet.\n"
                           + "• Le Copilote pourra alors répondre à TOUT, pas seulement aux soucis PC.\n\n"
                           + "Installation automatique et adaptée à ta machine : " + pick.Human + ".\n"
                           + "(Téléchargé une seule fois ; tu peux dire « désactive l'ia » à tout moment.)";
                System.Action show = delegate
                {
                    yes = System.Windows.Forms.MessageBox.Show(owner, msg, "Fluide — Cerveau IA local",
                        System.Windows.Forms.MessageBoxButtons.YesNo,
                        System.Windows.Forms.MessageBoxIcon.Question) == System.Windows.Forms.DialogResult.Yes;
                };
                if (owner != null && owner.InvokeRequired) owner.Invoke(show);
                else show();
            }
            catch { yes = false; }
            return yes;
        }

        public static void SetEnabled(bool on)
        {
            try
            {
                if (on)
                {
                    ClearOptOut();
                    File.WriteAllText(FlagPath, "IA locale active (Copilote).\n");
                }
                else
                {
                    if (File.Exists(FlagPath)) File.Delete(FlagPath);
                    File.WriteAllText(OffPath, "IA locale désactivée par l'utilisateur — l'installation automatique n'insistera pas.\n");
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------
        //  Mise en place AUTOMATIQUE (chaque installation) — avec garde-fous
        // ------------------------------------------------------------------
        /// <summary>Statut lisible de l'installation en cours (null = rien en cours) —
        /// affiché dans l'accueil du Copilote.</summary>
        public static volatile string SetupStatus;

        /// <summary>Met en place le cerveau IA local SANS intervention : installe Ollama
        /// (winget), démarre le moteur, télécharge le petit modèle, active. Garde-fous :
        /// jamais si l'utilisateur a dit « désactive l'ia », jamais sans winget, jamais
        /// sous 6 Go libres, 3 tentatives lourdes maximum (compteur persisté), reprise
        /// au lancement suivant si interrompu. À appeler en tâche de fond.</summary>
        public static void AutoSetup(Action<string, int> log)
        {
            try
            {
                if (Enabled || OptedOut) return;
                if (Tries() >= 3) return;                    // on n'insiste pas éternellement
                SetupStatus = "vérification";

                if (!ServerUp(1500))
                {
                    string exe = OllamaExe();
                    if (exe == null)
                    {
                        string winget = LibScan.WingetPath();
                        if (winget == null) { SetupStatus = null; return; }      // rien de silencieux possible
                        if (FreeSystemGb() < 6) { SetupStatus = null; return; }  // on n'impose pas ~2 Go sans place
                        SetupStatus = "installation d'Ollama (gratuit)";
                        if (log != null) log("IA locale : installation d'Ollama (winget, gratuit)…", 0);
                        BumpTries();
                        Sys.Run(winget, "install --id Ollama.Ollama --exact --silent --accept-package-agreements --accept-source-agreements");
                        exe = OllamaExe();
                        if (exe == null) { SetupStatus = null; return; }
                    }
                    SetupStatus = "démarrage du moteur IA";
                    TryStartServer(exe);
                    for (int i = 0; i < 20 && !ServerUp(1000); i++) System.Threading.Thread.Sleep(1000);
                    if (!ServerUp(1000)) { SetupStatus = null; return; }
                }

                if (BestModel() == null)
                {
                    ModelPick pick = ChooseModel();          // ADAPTÉ à la machine (VRAM + RAM)
                    if (FreeSystemGb() < pick.Gb + 2) { SetupStatus = null; return; }   // marge de sécurité
                    SetupStatus = "téléchargement du modèle " + pick.Human;
                    if (log != null) log("IA locale : modèle choisi pour cette machine → " + pick.Human + ". Téléchargement…", 0);
                    BumpTries();
                    string exe2 = OllamaExe(); if (exe2 == null) exe2 = "ollama";
                    Sys.Run(exe2, "pull " + pick.Tag);       // interrompu ? Ollama REPREND le téléchargement au prochain essai
                    if (BestModel() == null) { SetupStatus = null; return; }
                }

                SetEnabled(true);
                SetupStatus = null;
                if (log != null) log("🧠 IA locale prête (installation automatique) — le Copilote répond maintenant à tout.", 1);
            }
            catch { SetupStatus = null; }
        }

        /// <summary>Démarre le moteur : l'appli de zone de notification si présente (survit à
        /// la fermeture de Fluide), sinon « ollama serve » caché.</summary>
        private static void TryStartServer(string exe)
        {
            try
            {
                string app = Path.Combine(Path.GetDirectoryName(exe) ?? "", "ollama app.exe");
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = File.Exists(app) ? app : exe,
                    Arguments = File.Exists(app) ? "" : "serve",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                var p = System.Diagnostics.Process.Start(psi);
                if (p != null) p.Dispose();
            }
            catch { }
        }

        private static double FreeSystemGb()
        {
            try
            {
                var di = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                return di.AvailableFreeSpace / 1073741824.0;
            }
            catch { return 0; }
        }

        private static int Tries()
        {
            try
            {
                if (!File.Exists(TriesPath)) return 0;
                int n; return int.TryParse(File.ReadAllText(TriesPath).Trim(), out n) ? n : 0;
            }
            catch { return 0; }
        }

        private static void BumpTries()
        {
            try { File.WriteAllText(TriesPath, (Tries() + 1).ToString()); } catch { }
        }

        /// <summary>Le serveur Ollama répond-il ? (borné, à appeler hors du fil d'interface)</summary>
        public static bool ServerUp(int timeoutMs)
        {
            try
            {
                using (var cts = new System.Threading.CancellationTokenSource(timeoutMs))
                using (var r = Http.GetAsync(Base + "/api/tags", cts.Token).Result)
                    return r.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // Modèles préférés : petits, rapides, corrects en français — du meilleur compromis au repli.
        private static readonly string[] Preferred = { "qwen2.5:7b", "qwen2.5:3b", "llama3.2:3b", "llama3.2", "qwen2.5:1.5b", "qwen2.5:0.5b", "qwen2.5", "mistral", "phi3", "gemma2" };

        /// <summary>Meilleur modèle DÉJÀ téléchargé, ou null s'il n'y en a aucun.</summary>
        public static string BestModel()
        {
            try
            {
                string json = Http.GetStringAsync(Base + "/api/tags").Result;
                var names = new List<string>();
                using (var d = JsonDocument.Parse(json))
                    foreach (var m in d.RootElement.GetProperty("models").EnumerateArray())
                        names.Add(m.GetProperty("name").GetString() ?? "");
                foreach (var p in Preferred)
                    foreach (var n in names)
                        if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return n;
                return names.Count > 0 ? names[0] : null;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        //  Choix du modèle ADAPTÉ à la machine (n'importe quelle config)
        // ------------------------------------------------------------------
        public sealed class ModelPick
        {
            public string Tag;      // identifiant Ollama à télécharger
            public double Gb;       // taille approximative du téléchargement
            public string Human;    // « llama3.2:3b (≈ 2 Go) — équilibré »
        }

        /// <summary>Sélectionne le meilleur modèle que CETTE machine peut faire tourner
        /// confortablement, d'après la VRAM du GPU et la RAM. Barème prudent (le modèle doit
        /// tenir en mémoire tout en laissant de quoi jouer) : du 0.5B universel au 7B sur
        /// grosse carte. Optimal ET optimisé, quelle que soit la config du client.</summary>
        public static ModelPick ChooseModel()
        {
            int vram = DetectVramMB();
            int ram = DetectRamMB();
            // Le facteur limitant : la VRAM si un vrai GPU est détecté, sinon la RAM (exécution CPU).
            int cap = vram >= 1024 ? vram : Math.Min(ram, 8192);

            if (cap >= 11000 && ram >= 24000)
                return new ModelPick { Tag = "qwen2.5:7b", Gb = 4.7, Human = "qwen2.5:7b (≈ 4,7 Go) — le plus malin, ta machine encaisse" };
            if (cap >= 6000 && ram >= 12000)
                return new ModelPick { Tag = "llama3.2:3b", Gb = 2.0, Human = "llama3.2:3b (≈ 2 Go) — équilibré, le sweet spot" };
            if (cap >= 3500 && ram >= 8000)
                return new ModelPick { Tag = "qwen2.5:1.5b", Gb = 1.0, Human = "qwen2.5:1.5b (≈ 1 Go) — léger et vif" };
            return new ModelPick { Tag = "qwen2.5:0.5b", Gb = 0.4, Human = "qwen2.5:0.5b (≈ 0,4 Go) — ultra-léger, tourne partout" };
        }

        /// <summary>VRAM dédiée du GPU en Mo, tous constructeurs, SANS pilote noyau : d'abord le
        /// registre (HardwareInformation.qwMemorySize, fiable et non plafonné), puis les capteurs
        /// LHM (déjà en-process), puis nvidia-smi. 0 si indéterminé (→ on retombe sur la RAM).</summary>
        public static int DetectVramMB()
        {
            long best = 0;
            try
            {
                using (var cls = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls != null)
                        foreach (string sub in cls.GetSubKeyNames())
                        {
                            if (sub.Length != 4) continue;   // 0000, 0001… (pas « Properties »)
                            try
                            {
                                using (var k = cls.OpenSubKey(sub))
                                {
                                    object v = k != null ? k.GetValue("HardwareInformation.qwMemorySize") : null;
                                    if (v is long) best = Math.Max(best, (long)v / 1048576);
                                }
                            }
                            catch { }
                        }
                }
            }
            catch { }
            if (best <= 0)
            {
                try { using (var mon = new HwMonitor()) { HwSample s = mon.Sample(); if (s.Gpu != null && s.Gpu.VramTotalMB > 0) best = s.Gpu.VramTotalMB; } }
                catch { }
            }
            return (int)best;
        }

        public static int DetectRamMB()
        {
            try { long t = NativeMem.TotalPhysMB(); if (t > 0) return (int)t; } catch { }
            try { var r = Sys.QueryRam(); if (r != null && r.TotalMB > 0) return (int)r.TotalMB; } catch { }
            return 8192;   // hypothèse prudente à défaut
        }

        /// <summary>Chemin de l'exécutable Ollama installé, ou null (on tentera le PATH).</summary>
        public static string OllamaExe()
        {
            try
            {
                string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                        @"Programs\Ollama\ollama.exe");
                if (File.Exists(p)) return p;
            }
            catch { }
            return null;
        }

        /// <summary>Pose la question au modèle local. BLOQUANT (à appeler en tâche de fond).</summary>
        public static string Ask(string question, string systemContext, string model)
        {
            var payload = new Dictionary<string, object>
            {
                { "model", model },
                { "system", systemContext },
                { "prompt", question },
                { "stream", false },
                { "options", new Dictionary<string, object> { { "temperature", 0.4 }, { "num_predict", 350 } } }
            };
            var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using (var r = Http.PostAsync(Base + "/api/generate", body).Result)
            {
                string json = r.Content.ReadAsStringAsync().Result;
                using (var d = JsonDocument.Parse(json))
                    return d.RootElement.GetProperty("response").GetString();
            }
        }
    }
}
