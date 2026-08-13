using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace BTOptimizer
{
    /// <summary>
    /// Présence Discord (Rich Presence) — affiche « Optimise son PC · avec ONYX » dans le statut
    /// Discord de l'utilisateur, via l'IPC local de Discord (named pipe discord-ipc-N). AUCUNE
    /// dépendance externe. Se tait proprement si Discord n'est pas lancé, ou si l'App ID n'est pas
    /// encore configuré. Activable/désactivable, choix persisté (bt-discord.txt).
    ///
    /// L'application « ONYX » du portail développeur Discord est désormais celle par défaut : la
    /// présence fonctionne sans rien coller. Un identifiant personnel reste possible via le menu
    /// (⋯ → Système), et il a la priorité — utile pour tester une autre application.
    ///
    /// Un Application ID n'est PAS un secret : c'est un identifiant public, présent en clair dans
    /// tout client qui utilise Rich Presence. Il n'ouvre aucun accès au compte, contrairement au
    /// jeton du bot — qui, lui, n'a rien à faire ici et n'y est pas.
    ///
    /// Pour que le logo s'affiche à côté du statut, l'application doit porter une ressource nommée
    /// exactement « logo » dans Rich Presence → Art Assets. Sans elle, le texte s'affiche seul.
    /// </summary>
    internal static class DiscordPresence
    {
        private static NamedPipeClientStream _pipe;
        private static Thread _thread;
        private static volatile bool _running;
        private static string _appId;

        private static string ConfigPath
        {
            get { return AppPaths.File("bt-discord.txt"); }
        }
        private static string AppIdPath
        {
            get { return AppPaths.File("bt-discord-appid.txt"); }
        }

        /// <summary>Activée par défaut ; « 0 » dans le fichier = désactivée.</summary>
        public static bool Enabled
        {
            get { try { return !File.Exists(ConfigPath) || File.ReadAllText(ConfigPath).Trim() != "0"; } catch { return true; } }
            set { try { File.WriteAllText(ConfigPath, value ? "1" : "0"); } catch { } }
        }

        /// <summary>Application « ONYX » officielle du portail développeur Discord. Identifiant
        /// PUBLIC (voir la note de la classe) : c'est ce qui permet à la présence de marcher dès
        /// l'installation, sans manipulation.</summary>
        public const string AppIdParDefaut = "1537266582785495120";

        /// <summary>Application ID Discord. Celui collé par l'utilisateur (menu ⋯ → Système)
        /// l'emporte ; sinon l'application ONYX officielle sert. Un fichier présent mais illisible
        /// n'est PAS pris pour argent comptant : on retombe sur le défaut plutôt que d'envoyer un
        /// identifiant invalide à Discord, qui refuserait la connexion en silence.</summary>
        public static string AppId
        {
            get
            {
                if (_appId != null) return _appId;
                try
                {
                    if (File.Exists(AppIdPath))
                    {
                        string s = File.ReadAllText(AppIdPath).Trim();
                        if (EstValide(s)) { _appId = s; return _appId; }
                    }
                }
                catch { }
                _appId = AppIdParDefaut;
                return _appId;
            }
            set
            {
                _appId = null;   // relire depuis le fichier au prochain accès
                try { File.WriteAllText(AppIdPath, (value ?? "").Trim()); } catch { }
            }
        }

        /// <summary>PUR : cette chaîne est-elle un Application ID Discord plausible ? Un identifiant
        /// Discord (« snowflake ») est un entier non signé de 17 à 20 chiffres.</summary>
        public static bool EstValide(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s.Length < 17 || s.Length > 20) return false;
            ulong u;
            return ulong.TryParse(s, out u) && u > 0;
        }

        /// <summary>true si un App ID réel est configuré (sinon la présence reste inerte).</summary>
        public static bool Configured { get { return EstValide(AppId); } }

        /// <summary>true si l'identifiant utilisé est celui d'ONYX, et non un identifiant personnel.</summary>
        public static bool UtiliseCeluiDOnyx { get { return AppId == AppIdParDefaut; } }

        public static void StartIfEnabled()
        {
            if (Enabled) Start();
        }

        public static void Start()
        {
            if (_running || !Configured) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "DiscordRPC" };
            _thread.Start();
        }

        public static void Stop()
        {
            _running = false;
            try { if (_pipe != null) { _pipe.Dispose(); _pipe = null; } } catch { }
        }

        private static void Loop()
        {
            try
            {
                if (!Connect()) { _running = false; return; }
                Write(0, "{\"v\":1,\"client_id\":\"" + AppId + "\"}");   // handshake
                ReadFrame();                                            // READY (ignoré)
                int tick = 0, secs = 0;
                SendActivity(tick++);
                // Garde le pipe vivant, et fait TOURNER l'activité toutes les 60 s (statut vivant).
                while (_running && _pipe != null && _pipe.IsConnected)
                {
                    Thread.Sleep(1000);
                    if (_running && ++secs % 60 == 0) SendActivity(tick++);
                }
            }
            catch { }
            finally
            {
                try { if (_pipe != null) _pipe.Dispose(); } catch { }
                _pipe = null; _running = false;
            }
        }

        private static bool Connect()
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                    p.Connect(500);
                    _pipe = p;
                    return true;
                }
                catch { }
            }
            return false;
        }

        private static void Write(int op, string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] frame = new byte[8 + payload.Length];
            BitConverter.GetBytes(op).CopyTo(frame, 0);
            BitConverter.GetBytes(payload.Length).CopyTo(frame, 4);
            payload.CopyTo(frame, 8);
            _pipe.Write(frame, 0, frame.Length);
            _pipe.Flush();
        }

        private static void ReadFrame()
        {
            // Un pipe peut rendre MOINS d'octets que demandé : un seul Read() tronquait
            // silencieusement l'en-tête (donc la longueur lue était fausse), et le corps était
            // lu partiellement en laissant des octets dans le tuyau — de quoi désynchroniser
            // toutes les trames suivantes. ReadExactly boucle jusqu'à complétion et lève si le
            // flux se termine avant : les catch reproduisent l'ancien « on abandonne » (CA2022).
            byte[] head = new byte[8];
            try { _pipe.ReadExactly(head, 0, 8); } catch { return; }

            int len = BitConverter.ToInt32(head, 4);
            if (len <= 0 || len >= 65536) return;

            byte[] buf = new byte[len];
            try { _pipe.ReadExactly(buf, 0, len); } catch { }
        }

        private static readonly long StartTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        private static int _tweakCount = -1;   // compté une fois, en tâche de fond

        // Lignes d'activité qui TOURNENT (60 s) : un statut vivant plutôt qu'une phrase figée.
        private static string DetailsLine(int tick)
        {
            if (_tweakCount < 0) { try { _tweakCount = Catalog.All().Count; } catch { _tweakCount = 0; } }
            string[] lines =
            {
                "Optimise son PC",
                "Traque les FPS perdus",
                _tweakCount > 0 ? _tweakCount + " optimisations sous la main" : "Optimise son PC",
                "Consulte son Copilote IA",
            };
            return lines[((tick % lines.Length) + lines.Length) % lines.Length];
        }

        private static void SendActivity(int tick)
        {
            int pid; try { pid = System.Diagnostics.Process.GetCurrentProcess().Id; } catch { pid = 0; }
            string ver = "";
            try { var v = typeof(DiscordPresence).Assembly.GetName().Version; ver = " v" + v.Major + "." + v.Minor.ToString("00"); } catch { }
            string nonce = Guid.NewGuid().ToString();
            string json =
                "{\"cmd\":\"SET_ACTIVITY\",\"nonce\":\"" + nonce + "\",\"args\":{\"pid\":" + pid + ",\"activity\":{" +
                "\"details\":\"" + DetailsLine(tick) + "\",\"state\":\"avec ONYX" + ver + "\"," +
                "\"assets\":{\"large_image\":\"logo\",\"large_text\":\"ONYX — l'optimiseur gaming\"}," +
                "\"timestamps\":{\"start\":" + StartTs + "}}}}";
            try { Write(1, json); ReadFrame(); } catch { }
        }
    }
}
