using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace BTOptimizer
{
    /// <summary>
    /// Connexion 4G/5G (box mobile) — mesures et corrections SPÉCIFIQUES aux accès mobiles
    /// (box 5G Bouygues/Orange/SFR/Free, partage de connexion) :
    ///
    ///  • MTU : le transport mobile (tunnel GTP) mange 40-80 octets ; Windows reste à 1500
    ///    → paquets fragmentés ou jetés, micro-freezes et débits en dents de scie. On MESURE
    ///    la vraie MTU du lien (ping DF par dichotomie) et on peut l'appliquer, réversible.
    ///  • CGNAT : l'adresse partagée des réseaux mobiles (100.64.0.0/10) — NAT strict, aucun
    ///    réglage Windows ne le contourne : on le DÉTECTE et on explique (IPv6 = la sortie).
    ///  • IPv6 : souvent le chemin le plus direct en 4G/5G (pas de CGNAT) — on vérifie.
    ///  • Bufferbloat : latence AU REPOS vs SOUS CHARGE (téléchargement réel pendant les
    ///    pings) — le mal n°1 des box mobiles quand quelqu'un stream pendant que tu joues.
    ///
    /// Tout est mesuré sur CE lien, rien n'est inventé, et la seule écriture (MTU) est
    /// sauvegardée puis rétablissable en un clic.
    /// </summary>
    internal static class MobileNet
    {
        public class Report
        {
            public string IfName;            // interface active (celle de la passerelle)
            public int IfIndex = -1;
            public int MtuCurrent = -1;
            public int MtuOptimal = -1;      // -1 = mesure impossible (ICMP DF bloqué)
            public double GwPing = -1, GwJitter = -1;   // vers la BOX : juge le câble/LAN, pas la radio
            public int GwLoss = -1;
            public double PingIdle = -1, JitterIdle = -1;
            public int LossIdle = -1;
            public double PingLoaded = -1;   // latence pendant un téléchargement réel (réception)
            public double PingUpLoaded = -1; // latence pendant un ENVOI réel — le tueur des box 5G
            public bool Cgnat;               // 100.64.0.0/10 vu sur le trajet = certain
            public bool CgnatProbable;       // privé (10/8…) APRÈS la box = probable
            public bool Ipv6;                // un ping IPv6 public répond
        }

        private const string ProbeV4 = "1.1.1.1";
        private const string ProbeV6 = "2606:4700:4700::1111";
        private const string LoadUrl = "https://speed.cloudflare.com/__down?bytes=80000000";
        private const string UpUrl = "https://speed.cloudflare.com/__up";
        private static string MtuStore { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-mtu.txt"); } }

        // ------------------------------------------------------------------
        //  Interface active
        // ------------------------------------------------------------------
        /// <summary>L'interface qui porte la passerelle par défaut (celle du vrai trafic).</summary>
        public static NetworkInterface ActiveInterface()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    IPInterfaceProperties p;
                    try { p = ni.GetIPProperties(); } catch { continue; }
                    foreach (GatewayIPAddressInformation gw in p.GatewayAddresses)
                        if (gw.Address != null && gw.Address.AddressFamily == AddressFamily.InterNetwork
                            && !IPAddress.IsLoopback(gw.Address))
                            return ni;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Remplit interface + MTU courante (lecture locale, instantané, aucun réseau).</summary>
        public static void FillInterface(Report r)
        {
            NetworkInterface ni = ActiveInterface();
            if (ni == null) return;
            r.IfName = ni.Name;
            try
            {
                IPv4InterfaceProperties v4 = ni.GetIPProperties().GetIPv4Properties();
                r.IfIndex = v4.Index;
                r.MtuCurrent = v4.Mtu;
            }
            catch { }
        }

        /// <summary>La passerelle IPv4 de l'interface active (l'adresse LAN de la box), ou null.</summary>
        public static IPAddress GatewayAddress()
        {
            try
            {
                NetworkInterface ni = ActiveInterface();
                if (ni == null) return null;
                foreach (GatewayIPAddressInformation gw in ni.GetIPProperties().GatewayAddresses)
                    if (gw.Address != null && gw.Address.AddressFamily == AddressFamily.InterNetwork)
                        return gw.Address;
            }
            catch { }
            return null;
        }

        /// <summary>Échantillon de pings vers un hôte ARBITRAIRE (la box, un serveur…) —
        /// même calcul que ChatActions.PingSample, cible libre.</summary>
        public static bool SampleTo(string host, int count, int timeoutMs, out double avg, out double jitter, out int lossPct)
        {
            avg = 0; jitter = 0; lossPct = 100;
            var times = new List<long>();
            int sent = 0;
            try
            {
                using (var p = new Ping())
                    for (int i = 0; i < count; i++)
                    {
                        sent++;
                        try
                        {
                            PingReply rep = p.Send(host, timeoutMs);
                            if (rep != null && rep.Status == IPStatus.Success) times.Add(rep.RoundtripTime);
                        }
                        catch { }
                    }
            }
            catch { return false; }
            if (times.Count == 0) return false;
            long sum = 0; foreach (long t in times) sum += t;
            avg = (double)sum / times.Count;
            double dev = 0; foreach (long t in times) dev += Math.Abs(t - avg);
            jitter = dev / times.Count;
            lossPct = (int)Math.Round(100.0 * (sent - times.Count) / sent);
            return true;
        }

        // ------------------------------------------------------------------
        //  Mesures (réseau réel — jamais appelées à la construction d'une fenêtre)
        // ------------------------------------------------------------------
        /// <summary>MTU réelle du lien : plus gros ping « ne pas fragmenter » qui passe,
        /// par dichotomie (payload 968-1472 → MTU 996-1500). -1 si l'ICMP DF est bloqué.</summary>
        public static int DiscoverMtu(Action<string, int> log)
        {
            Action<string, int> L = log ?? delegate { };
            if (!DfPingOk(968)) { L("MTU : mesure impossible (ICMP « ne pas fragmenter » bloqué sur ce lien).", 2); return -1; }
            int lo = 968, hi = 1472;                 // payload ICMP ; MTU = payload + 28
            if (DfPingOk(1472)) return 1500;         // lien Ethernet plein : rien à raboter
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (DfPingOk(mid)) lo = mid; else hi = mid;
            }
            int mtu = lo + 28;
            L("MTU réelle du lien : " + mtu + " octets (mesurée au ping DF).", 0);
            return mtu;
        }

        private static bool DfPingOk(int payload)
        {
            byte[] buf = new byte[payload];
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using (var p = new Ping())
                    {
                        PingReply rep = p.Send(ProbeV4, 1200, buf, new PingOptions(64, true));
                        if (rep != null && rep.Status == IPStatus.Success) return true;
                        // « Paquet trop gros » = réponse franche : inutile d'insister.
                        if (rep != null && rep.Status == IPStatus.PacketTooBig) return false;
                    }
                }
                catch { }
            }
            return false;
        }

        /// <summary>CGNAT : un saut du trajet répond depuis 100.64.0.0/10 (adresse partagée
        /// des opérateurs mobiles) = certain ; du privé APRÈS la box = probable.</summary>
        public static void DetectCgnat(Report r)
        {
            try
            {
                for (int ttl = 1; ttl <= 4; ttl++)
                {
                    IPAddress hop = HopAt(ttl);
                    if (hop == null) continue;
                    byte[] b = hop.GetAddressBytes();
                    if (b.Length != 4) continue;
                    bool shared = b[0] == 100 && b[1] >= 64 && b[1] <= 127;   // 100.64.0.0/10
                    bool priv = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
                    if (shared) { r.Cgnat = true; return; }
                    if (ttl >= 2 && priv) r.CgnatProbable = true;             // la box est le saut 1
                }
            }
            catch { }
        }

        private static IPAddress HopAt(int ttl)
        {
            try
            {
                using (var p = new Ping())
                {
                    PingReply rep = p.Send(ProbeV4, 1200, new byte[32], new PingOptions(ttl, true));
                    if (rep != null && (rep.Status == IPStatus.TtlExpired || rep.Status == IPStatus.Success))
                        return rep.Address;
                }
            }
            catch { }
            return null;
        }

        /// <summary>IPv6 opérationnelle ? Sur mobile c'est souvent le chemin SANS CGNAT.</summary>
        public static bool CheckIpv6()
        {
            try
            {
                using (var p = new Ping())
                {
                    PingReply rep = p.Send(ProbeV6, 1500, new byte[32]);
                    return rep != null && rep.Status == IPStatus.Success;
                }
            }
            catch { return false; }
        }

        /// <summary>Latence SOUS CHARGE en RÉCEPTION : pings pendant un téléchargement réel (~8 s).
        /// L'écart repos → charge, c'est le bufferbloat de la box.</summary>
        public static double LoadedPing(Action<string, int> log)
        {
            return PingWhile(log, cts =>
            {
                using (var http = new System.Net.Http.HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(12);
                    using (var s = http.GetStreamAsync(LoadUrl).GetAwaiter().GetResult())
                    {
                        byte[] buf = new byte[81920];
                        while (!cts.IsCancellationRequested && s.Read(buf, 0, buf.Length) > 0) { }
                    }
                }
            });
        }

        /// <summary>Latence SOUS CHARGE en ENVOI : pings pendant un téléversement réel.
        /// C'est LE talon d'Achille des box 4G/5G (montée étroite : un cloud qui synchronise
        /// suffit à faire exploser le ping de toute la maison).</summary>
        public static double UploadLoadedPing(Action<string, int> log)
        {
            return PingWhile(log, cts =>
            {
                byte[] chunk = new byte[4 * 1024 * 1024];
                new Random(7).NextBytes(chunk);   // incompressible : charge réellement la montée
                using (var http = new System.Net.Http.HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(12);
                    while (!cts.IsCancellationRequested)
                        using (var content = new System.Net.Http.ByteArrayContent(chunk))
                            http.PostAsync(UpUrl, content, cts).GetAwaiter().GetResult();
                }
            });
        }

        /// <summary>Fabrique commune : lance la charge en fond, ping pendant ~7 s, coupe.</summary>
        private static double PingWhile(Action<string, int> log, Action<System.Threading.CancellationToken> load)
        {
            Action<string, int> L = log ?? delegate { };
            var cts = new System.Threading.CancellationTokenSource();
            var task = System.Threading.Tasks.Task.Run(() => { try { load(cts.Token); } catch { } });
            System.Threading.Thread.Sleep(700);   // laisse le débit s'établir
            double avg, jit; int loss;
            bool ok = ChatActions.PingSample(8, 900, out avg, out jit, out loss);
            cts.Cancel();
            try { task.Wait(2000); } catch { }
            if (!ok) { L("Latence sous charge : mesure impossible (hors-ligne ?).", 2); return -1; }
            return avg;
        }

        // ------------------------------------------------------------------
        //  Correction MTU (la seule écriture) — sauvegardée, rétablissable
        // ------------------------------------------------------------------
        /// <summary>Vrai s'il existe une sauvegarde à rétablir.</summary>
        public static bool HasBackup { get { try { return File.Exists(MtuStore); } catch { return false; } } }

        /// <summary>Applique la MTU mesurée sur l'interface active (IPv4 + IPv6), après
        /// sauvegarde de la valeur d'origine. store=persistent : survit au redémarrage.</summary>
        public static bool ApplyMtu(Report r, Action<string, int> log)
        {
            Action<string, int> L = log ?? delegate { };
            if (r == null || r.IfIndex < 0 || r.MtuOptimal < 996) { L("MTU : rien à appliquer (mesure d'abord).", 2); return false; }
            try
            {
                if (!HasBackup)
                    File.WriteAllText(MtuStore, r.IfIndex + " " + r.MtuCurrent, new System.Text.UTF8Encoding(false));
            }
            catch { }
            bool ok4 = SetMtu("ipv4", r.IfIndex, r.MtuOptimal);
            bool ok6 = SetMtu("ipv6", r.IfIndex, r.MtuOptimal);   // v6 : au pire l'appel échoue sans rien casser
            if (ok4)
            {
                L("MTU " + r.MtuOptimal + " appliquée sur « " + r.IfName + " » (IPv4" + (ok6 ? " + IPv6" : "") + "), persistante. Annulable ici même.", 1);
                return true;
            }
            L("Échec de l'application de la MTU (droits administrateur requis ?).", 2);
            return false;
        }

        /// <summary>Rétablit la MTU d'origine sauvegardée, puis retire la sauvegarde.</summary>
        public static bool RevertMtu(Action<string, int> log)
        {
            Action<string, int> L = log ?? delegate { };
            try
            {
                if (!HasBackup) { L("MTU : aucune sauvegarde à rétablir.", 0); return false; }
                string[] parts = File.ReadAllText(MtuStore).Trim().Split(' ');
                int idx = int.Parse(parts[0]), mtu = int.Parse(parts[1]);
                bool ok = SetMtu("ipv4", idx, mtu);
                SetMtu("ipv6", idx, mtu);
                if (ok)
                {
                    try { File.Delete(MtuStore); } catch { }
                    L("MTU d'origine (" + mtu + ") rétablie.", 1);
                    return true;
                }
                L("Échec du rétablissement de la MTU.", 2);
                return false;
            }
            catch (Exception ex) { L("Rétablissement MTU : " + ex.Message, 2); return false; }
        }

        private static bool SetMtu(string family, int ifIndex, int mtu)
        {
            try
            {
                var res = Sys.Run(Sys.Sys32("netsh.exe"),
                    "interface " + family + " set subinterface " + ifIndex + " mtu=" + mtu + " store=persistent");
                return res != null && res.ExitCode == 0;
            }
            catch { return false; }
        }
    }
}
