using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// L'enquêteur du Copilote : au lieu d'ouvrir un panneau, il lance TOUTES les mesures
    /// utiles, croise les résultats, classe les causes par impact réel et rend un plan
    /// d'action ordonné. Chaque cause n'apparaît que si elle est vraiment constatée —
    /// jamais de faux problème pour se rendre intéressant.
    /// 100 % local : aucune requête réseau, aucune donnée ne quitte la machine.
    /// </summary>
    internal static class Investigator
    {
        private sealed class Finding
        {
            public int Impact;                       // 0-100, sert au tri
            public string Text;                      // « Ton écran est à 60 Hz… »
            public DocAssistant.ChatAction Fix;      // correction proposée (peut être null)
        }

        /// <summary>Enquête complète. 'focus' oriente le vocabulaire de la conclusion
        /// (« ça rame », « ça crash »…) sans changer les mesures effectuées.</summary>
        public static DocAssistant.Reply Investigate(string focus, BadgeCatalog.Stats st, Action<string, int> log)
        {
            var found = new List<Finding>();
            var ok = new List<string>();

            // --- 1. Écrans sous leur fréquence maximale (le gain le plus spectaculaire) ---
            try
            {
                var screens = DisplayInfo.Query();
                var below = new List<DisplayInfo.DisplayMode>();
                if (screens != null) foreach (var d in screens) if (d.BelowMax) below.Add(d);
                if (below.Count > 0)
                {
                    var f = new Finding { Impact = 95, Fix = ChatActions.FixScreen() };
                    f.Text = below.Count == 1
                        ? "Ton écran tourne à " + below[0].CurrentHz + " Hz alors qu'il peut faire " + below[0].MaxHz + " Hz. "
                          + "C'est de la fluidité que tu as payée et que tu n'utilises pas."
                        : below.Count + " écrans tournent sous leur fréquence maximale.";
                    found.Add(f);
                }
                else if (screens != null && screens.Count > 0) ok.Add("écrans à leur fréquence maximale");
            }
            catch { }

            // --- 2. Température GPU (cause n°1 des chutes de FPS soudaines) ---
            try
            {
                using (var mon = new HwMonitor())
                {
                    HwSample s = mon.Sample();
                    if (s.Gpu != null && s.Gpu.Ok && s.Gpu.TempC >= 85)
                        found.Add(new Finding
                        {
                            Impact = 85,
                            Text = "Ton GPU monte à " + s.Gpu.TempC.ToString("0") + " °C. Au-delà de 85 °C il se bride tout seul : "
                                 + "les FPS s'effondrent en pleine partie. Regarde le flux d'air et la poussière."
                        });
                    else if (s.Gpu != null && s.Gpu.Ok && s.Gpu.TempC > 0) ok.Add("températures GPU sous contrôle");

                    if (s.RamLoad >= 90)
                        found.Add(new Finding
                        {
                            Impact = 65,
                            Text = "Ta mémoire est occupée à " + s.RamLoad.ToString("0") + " %. Quand la RAM sature, "
                                 + "Windows échange sur le disque et le jeu saccade."
                        });
                    else if (s.RamLoad > 0) ok.Add("mémoire disponible");
                }
            }
            catch { }

            // --- 3. Disque système ---
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                var di = new DriveInfo(root);
                double freeGb = di.AvailableFreeSpace / 1073741824.0;
                double totGb = di.TotalSize / 1073741824.0;
                int pct = totGb > 0 ? (int)Math.Round(freeGb / totGb * 100) : 100;
                long recov = ChatActions.RecoverableMb();
                if (pct < 12)
                    found.Add(new Finding
                    {
                        Impact = 80,
                        Text = "Ton disque système est plein à " + (100 - pct) + " % (" + freeGb.ToString("0") + " Go libres). "
                             + "Sous 10 % de libre, Windows ralentit franchement.",
                        Fix = ChatActions.FixDisk()
                    });
                else if (recov >= 3072)
                    found.Add(new Finding
                    {
                        Impact = 45,
                        Text = "" + (recov / 1024.0).ToString("0.0") + " Go de temporaires et de caches traînent sur ton disque.",
                        Fix = ChatActions.FixDisk()
                    });
                else ok.Add("espace disque suffisant");
            }
            catch { }

            // --- 4. Bibliothèques de jeu (cause n°1 d'un jeu qui ne démarre pas) ---
            try
            {
                int missing = LibScan.MissingEssentialCount();
                if (missing > 0)
                    found.Add(new Finding
                    {
                        Impact = 75,
                        Text = "" + missing + " bibliothèque(s) essentielle(s) manquante(s) (Visual C++, DirectX, .NET). "
                             + "C'est la cause classique d'un jeu qui refuse de démarrer."
                    });
                else ok.Add("bibliothèques de jeu complètes");
            }
            catch { }

            // --- 5. Optimisations recommandées non appliquées ---
            try
            {
                if (st != null && st.OptiTotal > 0)
                {
                    int inactive = st.OptiTotal - st.OptiActive;
                    if (st.Health < 70 && inactive > 20)
                        found.Add(new Finding
                        {
                            Impact = 55,
                            Text = "" + inactive + " optimisations sont encore inactives et ton score de santé est à "
                                 + st.Health + " %. Le preset « Recommandé » couvre l'essentiel sans rien risquer."
                        });
                    else if (st.Health >= 70) ok.Add("optimisations bien engagées (" + st.OptiActive + " actives)");
                }
            }
            catch { }

            found.Sort(delegate (Finding a, Finding b) { return b.Impact.CompareTo(a.Impact); });

            // --- Rédaction du compte-rendu ---
            var sb = new StringBuilder();
            if (found.Count == 0)
            {
                sb.Append("J'ai passé ton PC au crible");
                if (!string.IsNullOrEmpty(focus)) sb.Append(" en cherchant ce qui pourrait expliquer « ").Append(focus).Append(" »");
                sb.Append(" : je n'ai rien trouvé d'anormal.\n\n");
                if (ok.Count > 0) sb.Append("Vérifié et sain : ").Append(string.Join(" · ", ok.ToArray())).Append(".\n\n");
                sb.Append("Si le souci persiste, décris-moi précisément quand il arrive (dans un jeu en particulier ? "
                        + "au démarrage ? après un moment ?) et je creuse ailleurs.");
                return new DocAssistant.Reply { Text = sb.ToString() };
            }

            sb.Append("J'ai enquêté sur ton PC");
            if (!string.IsNullOrEmpty(focus)) sb.Append(" (« ").Append(focus).Append(" »)");
            sb.Append(" — ").Append(found.Count).Append(found.Count > 1 ? " causes trouvées" : " cause trouvée")
              .Append(", de la plus lourde à la plus légère :\n\n");
            for (int i = 0; i < found.Count; i++)
                sb.Append(i + 1).Append(". ").Append(found[i].Text).Append("\n\n");
            if (ok.Count > 0)
                sb.Append("Vérifié et sain : ").Append(string.Join(" · ", ok.ToArray())).Append(".");

            var plan = new List<DocAssistant.ChatAction>();
            foreach (var f in found) if (f.Fix != null) plan.Add(f.Fix);

            if (plan.Count > 0)
                sb.Append("\n\nCe que je peux corriger tout de suite — tu gardes la main :");

            return new DocAssistant.Reply
            {
                Text = sb.ToString().TrimEnd(),
                Plan = plan.Count > 0 ? plan : null
            };
        }

        /// <summary>L'action « enquête » telle que le chat la déclenche (mesure : elle part seule).</summary>
        public static DocAssistant.ChatAction Action(string focus, BadgeCatalog.Stats st)
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Enquête complète"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log) { return Investigate(focus, st, log); };
            return a;
        }
    }
}
