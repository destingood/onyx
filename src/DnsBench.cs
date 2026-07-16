using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DTGOptimizer
{
    /// <summary>Mesure la latence réelle d'un résolveur DNS via une requête UDP brute (port 53).</summary>
    internal static class DnsBench
    {
        /// <summary>Meilleur temps de réponse (ms) sur plusieurs essais, ou -1 si aucun ne répond.</summary>
        public static double QueryMs(string server, string domain, int timeoutMs, int tries)
        {
            double best = -1;
            for (int i = 0; i < tries; i++)
            {
                double ms = OneQuery(server, domain, timeoutMs, (ushort)(0x1000 + i));
                if (ms >= 0 && (best < 0 || ms < best)) best = ms;
            }
            return best;
        }

        private static double OneQuery(string server, string domain, int timeoutMs, ushort id)
        {
            try
            {
                byte[] query = BuildQuery(domain, id);
                using (var udp = new UdpClient())
                {
                    udp.Client.ReceiveTimeout = timeoutMs;
                    udp.Client.SendTimeout = timeoutMs;
                    var sw = Stopwatch.StartNew();
                    udp.Send(query, query.Length, server, 53);
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] resp = udp.Receive(ref remote);
                    sw.Stop();
                    // Réponse valide si l'ID correspond.
                    if (resp.Length >= 2 && resp[0] == (byte)(id >> 8) && resp[1] == (byte)(id & 0xFF))
                        return sw.Elapsed.TotalMilliseconds;
                    return -1;
                }
            }
            catch { return -1; }
        }

        private static byte[] BuildQuery(string domain, ushort id)
        {
            var ms = new MemoryStream();
            ms.WriteByte((byte)(id >> 8)); ms.WriteByte((byte)(id & 0xFF)); // ID
            ms.WriteByte(0x01); ms.WriteByte(0x00); // flags : récursion demandée
            ms.WriteByte(0x00); ms.WriteByte(0x01); // QDCOUNT = 1
            ms.WriteByte(0x00); ms.WriteByte(0x00); // ANCOUNT
            ms.WriteByte(0x00); ms.WriteByte(0x00); // NSCOUNT
            ms.WriteByte(0x00); ms.WriteByte(0x00); // ARCOUNT
            foreach (string label in domain.Split('.'))
            {
                if (label.Length == 0) continue;
                byte[] b = Encoding.ASCII.GetBytes(label);
                ms.WriteByte((byte)b.Length);
                ms.Write(b, 0, b.Length);
            }
            ms.WriteByte(0x00);                     // fin du nom
            ms.WriteByte(0x00); ms.WriteByte(0x01); // QTYPE = A
            ms.WriteByte(0x00); ms.WriteByte(0x01); // QCLASS = IN
            return ms.ToArray();
        }
    }
}
