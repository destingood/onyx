#if BTTEST
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>Banc BT_PERF : un fil de fond poste un message au fil d'interface toutes les 25 ms
    /// et mesure le délai de réponse. Au-delà du seuil, le fil d'interface a gelé : ligne « GEL;ms ».</summary>
    internal static class SentinelleGel
    {
        private static Thread _fil;
        private static volatile bool _actif;
        private static int _gels;

        public static void Demarre(Control cible, int seuilMs)
        {
            Arrete();
            _gels = 0; _actif = true;
            _fil = new Thread(() =>
            {
                while (_actif)
                {
                    var reponse = new ManualResetEventSlim(false);
                    var sw = Stopwatch.StartNew();
                    try { cible.BeginInvoke((Action)(() => reponse.Set())); }
                    catch { return; }   // fenêtre détruite
                    while (_actif && !reponse.Wait(50)) { }
                    double ms = sw.Elapsed.TotalMilliseconds;
                    if (_actif && ms > seuilMs)
                    {
                        Interlocked.Increment(ref _gels);
                        Console.WriteLine("GEL;" + ms.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    Thread.Sleep(25);
                }
            }) { IsBackground = true, Name = "SentinelleGel" };
            _fil.Start();
        }

        /// <summary>Arrête la sentinelle et renvoie le nombre de gels vus depuis Demarre.</summary>
        public static int Arrete()
        {
            _actif = false;
            try { if (_fil != null) _fil.Join(500); } catch { }
            _fil = null;
            return _gels;
        }
    }
}
#endif
