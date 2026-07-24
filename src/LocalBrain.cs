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
                    if (FreeSystemGb() < 4) { SetupStatus = null; return; }
                    SetupStatus = "téléchargement du modèle (≈ 2 Go, une seule fois)";
                    if (log != null) log("IA locale : téléchargement du modèle llama3.2:3b (≈ 2 Go, une fois)…", 0);
                    BumpTries();
                    string exe2 = OllamaExe(); if (exe2 == null) exe2 = "ollama";
                    Sys.Run(exe2, "pull llama3.2:3b");       // interrompu ? Ollama REPREND le téléchargement au prochain essai
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
        private static readonly string[] Preferred = { "qwen2.5:3b", "llama3.2:3b", "llama3.2", "qwen2.5", "mistral", "phi3", "gemma2" };

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
