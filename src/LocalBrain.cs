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

        /// <summary>Vrai si l'utilisateur a activé le cerveau IA local (fichier-drapeau).</summary>
        public static bool Enabled
        {
            get { try { return File.Exists(FlagPath); } catch { return false; } }
        }

        public static void SetEnabled(bool on)
        {
            try
            {
                if (on) File.WriteAllText(FlagPath, "IA locale activée par l'utilisateur (« active l'ia » dans le Copilote).\n");
                else if (File.Exists(FlagPath)) File.Delete(FlagPath);
            }
            catch { }
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
