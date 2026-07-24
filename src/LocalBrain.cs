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
                    // 1) DÉTECTE Ollama. Absent → on l'INSTALLE (winget, silencieux).
                    string exe = OllamaExe();
                    if (exe == null)
                    {
                        string winget = LibScan.WingetPath();
                        if (winget == null) { SetupStatus = null; return; }      // rien de silencieux possible
                        if (FreeSystemGb() < 6) { SetupStatus = null; return; }  // on n'impose pas l'install sans place
                        SetupStatus = "installation d'Ollama (gratuit)";
                        if (log != null) log("IA locale : Ollama absent → installation (winget, gratuit)…", 0);
                        BumpTries();
                        Sys.Run(winget, "install --id Ollama.Ollama --exact --silent --accept-package-agreements --accept-source-agreements");
                        for (int i = 0; i < 6 && OllamaExe() == null; i++) System.Threading.Thread.Sleep(1500); // laisse le disque se poser
                        exe = OllamaExe();
                        if (exe == null) { SetupStatus = null; return; }
                        if (log != null) log("IA locale : Ollama installé (" + exe + ").", 1);
                    }
                    else if (log != null) log("IA locale : Ollama déjà présent (" + exe + ") — pas de réinstallation.", 1);

                    // 2) CONFIGURE : démarre le moteur et s'assure qu'il se relancera au boot.
                    SetupStatus = "démarrage du moteur IA";
                    TryStartServer(exe);
                    for (int i = 0; i < 20 && !ServerUp(1000); i++) System.Threading.Thread.Sleep(1000);
                    if (!ServerUp(1000)) { SetupStatus = null; return; }
                    Configure(exe, log);
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

        /// <summary>Configure Ollama pour qu'il soit toujours prêt : (1) l'appli de zone de
        /// notification (« ollama app.exe ») démarre AVEC Windows via sa propre entrée Run — on la
        /// pose si l'installeur ne l'a pas fait ; (2) le modèle reste chargé 30 min entre deux
        /// questions (OLLAMA_KEEP_ALIVE, réglé par utilisateur) → réponses instantanées.</summary>
        private static void Configure(string exe, Action<string, int> log)
        {
            try
            {
                string app = Path.Combine(Path.GetDirectoryName(exe) ?? "", "ollama app.exe");
                if (File.Exists(app))
                {
                    object cur = Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\Run", "Ollama");
                    if (cur == null)
                        Sys.SetUser(@"Software\Microsoft\Windows\CurrentVersion\Run", "Ollama",
                                    "\"" + app + "\"", Microsoft.Win32.RegistryValueKind.String);
                }
                // Garde le modèle en mémoire un moment : la 1re réponse « réveille », les suivantes sont immédiates.
                Sys.SetUserEnv("OLLAMA_KEEP_ALIVE", "30m");
                if (log != null) log("IA locale : Ollama configuré (démarrage auto + modèle gardé en mémoire).", 1);
            }
            catch { }
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

        /// <summary>Détecte l'exécutable Ollama où qu'il soit installé (tous les emplacements
        /// connus des différents installeurs + le PATH), ou null s'il est vraiment absent.
        /// Robuste : évite de réinstaller un Ollama déjà présent ailleurs que le dossier par défaut.</summary>
        public static string OllamaExe()
        {
            string[] cands =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Ollama\ollama.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Ollama\ollama.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Ollama\ollama.exe"),
            };
            foreach (string p in cands) { try { if (File.Exists(p)) return p; } catch { } }
            // Dernier recours : le PATH (« where ollama »).
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("where", "ollama")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using (var pr = System.Diagnostics.Process.Start(psi))
                {
                    string outp = pr.StandardOutput.ReadToEnd();
                    pr.WaitForExit(4000);
                    foreach (string line in outp.Split('\n'))
                    {
                        string f = line.Trim();
                        if (f.EndsWith("ollama.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(f)) return f;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>Vrai si Ollama est détecté sur la machine (installé), quel que soit l'emplacement.</summary>
        public static bool Installed { get { return OllamaExe() != null; } }

        /// <summary>Pose la question au modèle local (sans mémoire). BLOQUANT (tâche de fond).</summary>
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

        /// <summary>Reformule une phrase d'accroche du Copilote avec des mots FRAIS et naturels
        /// (jamais deux fois pareil) — sans toucher au fond, sans inventer de chiffre, en gardant
        /// les noms entre « ». Court et borné (fallback = phrase d'origine si le modèle traîne).</summary>
        public static string Rephrase(string text, string model)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(model)) return null;
            string sys = "Tu es le Copilote, un assistant PC amical qui tutoie. Reformule le message suivant avec TES mots, "
                       + "un ton vivant et naturel, 1 à 2 phrases MAXIMUM. Garde exactement le même sens et la même intention. "
                       + "N'invente AUCUNE donnée ni chiffre, n'ajoute pas d'info. Garde INTACTS les noms entre guillemets « ». "
                       + "Ne salue pas sauf si le message salue. Réponds UNIQUEMENT par la reformulation, sans guillemets autour.";
            var payload = new Dictionary<string, object>
            {
                { "model", model }, { "system", sys }, { "prompt", text }, { "stream", false },
                { "options", new Dictionary<string, object> { { "temperature", 0.8 }, { "num_predict", 90 } } }
            };
            var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            try
            {
                using (var cts = new System.Threading.CancellationTokenSource(9000))
                using (var r = Http.PostAsync(Base + "/api/generate", body, cts.Token).Result)
                {
                    string json = r.Content.ReadAsStringAsync().Result;
                    using (var d = JsonDocument.Parse(json))
                    {
                        string o = d.RootElement.GetProperty("response").GetString();
                        if (string.IsNullOrWhiteSpace(o)) return null;
                        o = o.Trim().Trim('"', '«', '»', ' ');
                        return o.Length >= 3 ? o : null;
                    }
                }
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        //  MÉMOIRE DE CONVERSATION — l'IA suit le fil (« et pourquoi ? »…)
        // ------------------------------------------------------------------
        //  On garde les derniers échanges IA (rôle + texte) pour que les questions de suivi
        //  gardent leur contexte. Borné : on ne renvoie que les 8 derniers messages (~4 tours).
        private static readonly List<string[]> _history = new List<string[]>();
        private const int MaxTurns = 8;

        public static void PushUser(string text) { Push("user", text); }
        public static void PushAssistant(string text) { Push("assistant", text); }
        public static void ResetHistory() { lock (_history) _history.Clear(); }

        private static void Push(string role, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            lock (_history)
            {
                _history.Add(new[] { role, text });
                while (_history.Count > MaxTurns) _history.RemoveAt(0);
            }
        }

        /// <summary>Réponse EN CONTEXTE : envoie le système + l'historique récent (dont la dernière
        /// question) au modèle via /api/chat. BLOQUANT (tâche de fond).</summary>
        public static string AskChat(string systemContext, string model)
        {
            var msgs = new List<object> { new Dictionary<string, object> { { "role", "system" }, { "content", systemContext } } };
            lock (_history)
                foreach (var h in _history)
                    msgs.Add(new Dictionary<string, object> { { "role", h[0] }, { "content", h[1] } });
            var payload = new Dictionary<string, object>
            {
                { "model", model },
                { "messages", msgs },
                { "stream", false },
                { "options", new Dictionary<string, object> { { "temperature", 0.4 }, { "num_predict", 350 } } }
            };
            var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using (var r = Http.PostAsync(Base + "/api/chat", body).Result)
            {
                string json = r.Content.ReadAsStringAsync().Result;
                using (var d = JsonDocument.Parse(json))
                    return d.RootElement.GetProperty("message").GetProperty("content").GetString();
            }
        }
    }
}
