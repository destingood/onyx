using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BTOptimizer
{
    /// <summary>
    /// État système PARTAGÉ et mis en cache : optimisations actives / total, jeux détectés, santé.
    /// Évite que le dashboard ET la page Collection recalculent chacun la même chose (Check sur
    /// ~173 réglages + scan des jeux) à chaque visite. Recalcul en fond, cache ~45 s ; invalidé
    /// quand des optimisations changent (AppStats.Invalidate).
    /// </summary>
    internal static class AppStats
    {
        public sealed class Snap { public int OptiActive, OptiTotal, GamesDet, Health; }

        private static Snap _cache;
        private static DateTime _at;
        private static bool _busy;
        private static readonly object _lock = new object();
        private static readonly List<Action<Snap>> _pending = new List<Action<Snap>>();

        public static Snap Cached { get { return _cache; } }

        /// <summary>Fournit l'état (recalcule en fond si absent/périmé/forcé). onReady est appelé
        /// depuis un thread de fond — au calleur de marshaler vers l'UI.</summary>
        public static void Get(Action<Snap> onReady, bool force = false)
        {
            Snap c = _cache;
            bool stale = c == null || force;
            try { if (!stale && (DateTime.Now - _at).TotalSeconds > 45) stale = true; } catch { }
            if (!stale) { if (onReady != null) onReady(c); return; }

            lock (_lock)
            {
                if (onReady != null) _pending.Add(onReady);
                if (_busy) return;   // un calcul est déjà lancé : notre callback est en file
                _busy = true;
            }
            Task.Run(() =>
            {
                Snap s = Compute();
                Action<Snap>[] cbs;
                lock (_lock) { _cache = s; try { _at = DateTime.Now; } catch { } _busy = false; cbs = _pending.ToArray(); _pending.Clear(); }
                foreach (var cb in cbs) { try { cb(s); } catch { } }
            });
        }

        public static void Invalidate() { _cache = null; }

        private static Snap Compute()
        {
            var s = new Snap();
            try
            {
                var tw = Catalog.All(); s.OptiTotal = tw.Count;
                foreach (var t in tw) { if (t.Check == null) continue; bool? c = null; try { c = t.Check(); } catch { } if (c == true) s.OptiActive++; }
                s.Health = s.OptiTotal > 0 ? (int)Math.Round(100.0 * s.OptiActive / s.OptiTotal) : 0;
            }
            catch { }
            try { var games = GameScan.Known(); GameScan.Detect(games); foreach (var g in games) if (g.Detected) s.GamesDet++; } catch { }
            return s;
        }
    }
}
