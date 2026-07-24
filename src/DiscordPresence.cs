using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace BTOptimizer
{
    /// <summary>
    /// Présence Discord (Rich Presence) — affiche « Optimise son PC · avec Fluide » dans le statut
    /// Discord de l'utilisateur, via l'IPC local de Discord (named pipe discord-ipc-N). AUCUNE
    /// dépendance externe. Se tait proprement si Discord n'est pas lancé, ou si l'App ID n'est pas
    /// encore configuré. Activable/désactivable, choix persisté (bt-discord.txt).
    ///
    /// ⚠️ POUR L'ACTIVER RÉELLEMENT : crée une application « Fluide » sur
    ///    https://discord.com/developers/applications , copie son « APPLICATION ID » dans AppId
    ///    ci-dessous, et dans Rich Presence → Art Assets, uploade un logo nommé exactement "logo".
    /// </summary>
    internal static class DiscordPresence
    {
        // ⚠️ REMPLACE ces zéros par l'Application ID de ton app Discud « Fluide » (18-19 chiffres).
        private const string AppId = "0000000000000000000";

        private static NamedPipeClientStream _pipe;
        private static Thread _thread;
        private static volatile bool _running;

        private static string ConfigPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-discord.txt"); }
        }

        /// <summary>Activée par défaut ; « 0 » dans le fichier = désactivée.</summary>
        public static bool Enabled
        {
            get { try { return !File.Exists(ConfigPath) || File.ReadAllText(ConfigPath).Trim() != "0"; } catch { return true; } }
            set { try { File.WriteAllText(ConfigPath, value ? "1" : "0"); } catch { } }
        }

        /// <summary>true si un App ID réel est configuré (sinon la présence reste inerte).</summary>
        public static bool Configured { get { return AppId != "0000000000000000000" && AppId.Length >= 17; } }

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
                SendActivity();
                // Garde le pipe vivant (Discord conserve l'activité tant que la connexion tient).
                while (_running && _pipe != null && _pipe.IsConnected)
                {
                    Thread.Sleep(1000);
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

        private static void SendActivity()
        {
            long start = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int pid; try { pid = System.Diagnostics.Process.GetCurrentProcess().Id; } catch { pid = 0; }
            string nonce = Guid.NewGuid().ToString();
            string json =
                "{\"cmd\":\"SET_ACTIVITY\",\"nonce\":\"" + nonce + "\",\"args\":{\"pid\":" + pid + ",\"activity\":{" +
                "\"details\":\"Optimise son PC\",\"state\":\"avec Fluide\"," +
                "\"assets\":{\"large_image\":\"logo\",\"large_text\":\"Fluide — l'optimiseur gaming\"}," +
                "\"timestamps\":{\"start\":" + start + "}}}}";
            try { Write(1, json); ReadFrame(); } catch { }
        }
    }
}
