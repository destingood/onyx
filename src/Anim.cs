using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Petit moteur d'animation TRANSITOIRE, partagé par toute l'UI. UN seul timer
    /// (~60 fps) qui ne tourne QUE tant qu'une animation est en cours, puis s'arrête :
    /// zéro coût processeur au repos (cohérent avec un optimiseur). Chaque animation est
    /// une interpolation à durée fixe avec accélération (easing). Quand les animations
    /// sont coupées (réglage utilisateur, jeu en cours, « Afficher les animations »
    /// désactivé dans Windows, ou harnais de test), tout saute directement à l'état final —
    /// aucune logique ne dépend de la FIN d'une animation.
    /// </summary>
    internal static class Anim
    {
        /// <summary>Harnais de test : force l'absence totale d'animation (captures déterministes).</summary>
        /// <remarks>Initialisé explicitement : seul le harnais l'assigne (Program.cs, sous
        /// compilation conditionnelle), le compilateur le croyait donc jamais assigné (CS0649).</remarks>
        public static bool ForceOff = false;

        /// <summary>Recalculé par AnimSettings (choix utilisateur ET Windows ET pas en jeu).</summary>
        public static bool Enabled = true;

        /// <summary>Vrai si l'on doit réellement animer.</summary>
        public static bool On { get { return Enabled && !ForceOff; } }

        /// <summary>Jeton d'annulation : mettre Cancelled=true stoppe l'animation en cours
        /// (utilisé pour re-cibler une valeur, ex. survol entré puis ressorti avant la fin).</summary>
        /// <remarks>Initialisé explicitement : ce drapeau est destiné à être mis à true par
        /// l'APPELANT qui détient le Handle, jamais depuis cette classe (d'où CS0649).</remarks>
        internal sealed class Handle { public bool Cancelled = false; }

        private sealed class Item
        {
            public int Dur, Elapsed;
            public Func<float, float> Ease;
            public Action<float> Step;
            public Action Done;
            public Handle H;
        }

        private static readonly Timer _timer;
        private static readonly List<Item> _items = new List<Item>();
        private static int _last;

        static Anim()
        {
            _timer = new Timer();
            _timer.Interval = 16;   // ~60 images/s
            _timer.Tick += Tick;
        }

        /// <summary>Lance une interpolation (durée en ms). Renvoie un handle annulable, ou null
        /// si les animations sont coupées (dans ce cas l'état final est appliqué immédiatement).</summary>
        public static Handle Tween(int ms, Func<float, float> ease, Action<float> step, Action done)
        {
            if (!On || ms <= 0)
            {
                try { if (step != null) step(1f); } catch { }
                try { if (done != null) done(); } catch { }
                return null;
            }
            var it = new Item { Dur = ms, Ease = ease ?? BTOptimizer.Ease.OutCubic, Step = step, Done = done, H = new Handle() };
            _items.Add(it);
            if (!_timer.Enabled) { _last = Environment.TickCount; _timer.Start(); }
            return it.H;
        }

        public static Handle Tween(int ms, Action<float> step, Action done) { return Tween(ms, BTOptimizer.Ease.OutCubic, step, done); }
        public static Handle Tween(int ms, Action<float> step) { return Tween(ms, BTOptimizer.Ease.OutCubic, step, null); }

        private static void Tick(object sender, EventArgs e)
        {
            int now = Environment.TickCount;
            int dt = now - _last; _last = now;
            if (dt < 1) dt = 1;
            if (dt > 250) dt = 250;   // après un gros à-coup système, ne saute pas la fin

            // Copie défensive : un callback peut lancer/annuler d'autres animations.
            Item[] snap = _items.ToArray();
            foreach (Item it in snap)
            {
                if (it.H.Cancelled) { _items.Remove(it); continue; }
                it.Elapsed += dt;
                float p = it.Dur <= 0 ? 1f : (float)it.Elapsed / it.Dur;
                if (p > 1f) p = 1f;
                float v; try { v = it.Ease(p); } catch { v = p; }
                try { if (it.Step != null) it.Step(v); } catch { }
                if (p >= 1f)
                {
                    _items.Remove(it);
                    try { if (it.Done != null) it.Done(); } catch { }
                }
            }
            if (_items.Count == 0) _timer.Stop();
        }
    }

    /// <summary>Fonctions d'accélération (entrée 0..1 → sortie 0..1).</summary>
    internal static class Ease
    {
        public static float Linear(float t) { return t; }
        public static float OutCubic(float t) { float u = 1f - t; return 1f - u * u * u; }
        public static float InOutCubic(float t) { return t < 0.5f ? 4f * t * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 3) / 2f; }
    }

    /// <summary>
    /// État global de l'interrupteur « Animations » (Réglages système). Activé par défaut ;
    /// se coupe automatiquement quand un jeu tourne ; respecte le réglage Windows
    /// « Afficher les animations ». Choix persisté dans bt-anim.txt.
    /// </summary>
    internal static class AnimSettings
    {
        private static bool _game;

        private static string StorePath
        {
            get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-anim.txt"); }
        }

        /// <summary>Choix utilisateur (« 0 » = désactivé ; activé par défaut).</summary>
        public static bool UserEnabled
        {
            get { try { return !System.IO.File.Exists(StorePath) || System.IO.File.ReadAllText(StorePath).Trim() != "0"; } catch { return true; } }
            set { try { System.IO.File.WriteAllText(StorePath, value ? "1" : "0"); } catch { } Recompute(); }
        }

        /// <summary>Signalé par la boucle système du QG (toutes les 2 s).</summary>
        public static void SetGameRunning(bool g) { if (_game == g) return; _game = g; Recompute(); }

        /// <summary>Anim.Enabled = choix utilisateur ET effets Windows actifs ET pas en jeu.</summary>
        public static void Recompute()
        {
            bool win = true;
            try { win = SystemInformation.UIEffectsEnabled; } catch { }
            Anim.Enabled = UserEnabled && win && !_game;
        }
    }
}
