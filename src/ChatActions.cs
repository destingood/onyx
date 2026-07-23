using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Ce que le Copilote sait FAIRE, et pas seulement dire. Deux familles :
    ///   • MESURES  (IsChange = false, AutoRun = true)  : lecture seule, lancées toutes seules.
    ///   • CHANGEMENTS (IsChange = true)                : jamais sans un clic explicite, avec
    ///     l'annonce de ce qui va changer — c'est la promesse fondatrice de Fluide.
    /// Tout tourne en tâche de fond (voir PageConsultation) : rien ne fige la fenêtre.
    /// </summary>
    internal static class ChatActions
    {
        // ------------------------------------------------------------------
        //  Écran — le piège classique du 144 Hz resté à 60
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MeasureScreen()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Lecture de tes écrans"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                List<DisplayInfo.DisplayMode> list = DisplayInfo.Query();
                if (list == null || list.Count == 0) return "Je n'arrive pas à lire tes écrans sur ce PC.";
                var sb = new StringBuilder();
                int below = 0;
                foreach (var d in list)
                {
                    sb.Append("• ").Append(d.Name).Append(" : ").Append(d.CurrentHz).Append(" Hz");
                    if (d.BelowMax) { sb.Append("  ⚠ il peut monter à ").Append(d.MaxHz).Append(" Hz"); below++; }
                    else sb.Append("  ✅ c'est déjà son maximum");
                    sb.Append('\n');
                }
                sb.Append(below > 0
                    ? "\nTu perds de la fluidité sans le savoir — je peux corriger ça tout de suite."
                    : "\nRien à corriger de ce côté.");
                return sb.ToString().TrimEnd();
            };
            return a;
        }

        /// <summary>Action de correction — proposée seulement si un écran est réellement bridé.</summary>
        public static DocAssistant.ChatAction FixScreen()
        {
            List<DisplayInfo.DisplayMode> list;
            try { list = DisplayInfo.Query(); } catch { return null; }
            if (list == null) return null;
            var todo = new List<DisplayInfo.DisplayMode>();
            foreach (var d in list) if (d.BelowMax) todo.Add(d);
            if (todo.Count == 0) return null;

            var a = new DocAssistant.ChatAction();
            a.Label = todo.Count == 1
                ? "Passer l'écran à " + todo[0].MaxHz + " Hz"
                : "Passer les " + todo.Count + " écrans à leur maximum";
            a.IsChange = true;
            a.Warning = "Change la fréquence de rafraîchissement. Réversible, et Windows revient seul en arrière si l'écran ne suit pas.";
            a.Run = delegate (Action<string, int> log)
            {
                int ok = 0;
                var sb = new StringBuilder();
                foreach (var d in todo)
                {
                    bool done = false;
                    try { done = DisplayInfo.SetHz(d.Device, d.MaxHz); } catch { }
                    if (done) { ok++; sb.Append("✅ ").Append(d.Name).Append(" : ").Append(d.CurrentHz).Append(" → ").Append(d.MaxHz).Append(" Hz\n"); }
                    else sb.Append("⚠ ").Append(d.Name).Append(" : refusé par le pilote\n");
                }
                sb.Append(ok > 0 ? "\nRelance ton jeu pour en profiter." : "\nAucun changement appliqué.");
                return sb.ToString().TrimEnd();
            };
            return a;
        }

        // ------------------------------------------------------------------
        //  Capteurs — charge et températures en direct
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MeasureSensors()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Mesure en direct"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                var sb = new StringBuilder();
                try
                {
                    using (var mon = new HwMonitor())
                    {
                        HwSample s = mon.Sample();
                        sb.Append("• Processeur : ").Append(s.CpuLoad < 0 ? "n/d" : s.CpuLoad.ToString("0") + " % de charge");
                        if (!double.IsNaN(s.CpuTempC)) sb.Append(" · ").Append(s.CpuTempC.ToString("0")).Append(" °C");
                        sb.Append('\n');
                        sb.Append("• Mémoire : ").Append(s.RamLoad.ToString("0")).Append(" % utilisée\n");
                        if (s.Gpu != null && s.Gpu.Ok)
                        {
                            sb.Append("• Carte graphique : ").Append(s.Gpu.Util.ToString("0")).Append(" % de charge");
                            if (s.Gpu.TempC > 0) sb.Append(" · ").Append(s.Gpu.TempC.ToString("0")).Append(" °C");
                            sb.Append('\n');
                            if (s.Gpu.TempC >= 85) sb.Append("\n⚠ Ton GPU est très chaud : c'est la cause n°1 des chutes de FPS soudaines.");
                            else if (s.Gpu.TempC > 0) sb.Append("\nTempératures sous contrôle.");
                        }
                        else sb.Append("• Carte graphique : capteurs non lisibles ici\n");
                    }
                }
                catch { return "Je n'ai pas réussi à lire les capteurs à l'instant."; }
                return sb.ToString().TrimEnd();
            };
            return a;
        }

        // ------------------------------------------------------------------
        //  Disque — espace libre et espace récupérable
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MeasureDisk()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Analyse du disque"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                var sb = new StringBuilder();
                try
                {
                    string root = Path.GetPathRoot(Environment.SystemDirectory);
                    var di = new DriveInfo(root);
                    double freeGb = di.AvailableFreeSpace / 1073741824.0;
                    double totGb = di.TotalSize / 1073741824.0;
                    int pct = totGb > 0 ? (int)Math.Round(freeGb / totGb * 100) : 0;
                    sb.Append("• Disque système ").Append(root.TrimEnd('\\')).Append(" : ")
                      .Append(freeGb.ToString("0")).Append(" Go libres sur ").Append(totGb.ToString("0"))
                      .Append(" Go (").Append(pct).Append(" %)\n");
                    if (pct < 10) sb.Append("⚠ Sous 10 % de libre, Windows ralentit franchement.\n");
                }
                catch { }
                try
                {
                    long mb = 0;
                    foreach (var t in Sys.CleanTargets()) mb += t.SizeMB;
                    sb.Append("• Récupérable sans risque : ~")
                      .Append(mb >= 1024 ? (mb / 1024.0).ToString("0.0") + " Go" : mb + " Mo")
                      .Append(" (temporaires, caches — ça se régénère)");
                }
                catch { }
                string txt = sb.ToString().TrimEnd();
                return txt.Length == 0 ? "Je n'ai pas pu analyser le disque." : txt;
            };
            return a;
        }

        public static DocAssistant.ChatAction FixDisk()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Libérer l'espace maintenant";
            a.IsChange = true;
            a.Warning = "Supprime des fichiers temporaires et des caches qui se régénèrent. Tes fichiers personnels, jeux et sauvegardes ne sont pas touchés.";
            a.Run = delegate (Action<string, int> log)
            {
                long before = 0, after = 0;
                int n = 0;
                try
                {
                    List<Sys.CleanTarget> targets = Sys.CleanTargets();
                    foreach (var t in targets) before += t.SizeMB;
                    foreach (var t in targets) { try { n += Sys.CleanTargetNow(t, log); } catch { } }
                    foreach (var t in Sys.CleanTargets()) after += t.SizeMB;
                }
                catch { return "Le nettoyage n'a pas pu aller au bout (fichiers verrouillés par Windows)."; }
                long freed = Math.Max(0, before - after);
                return "✅ Nettoyage terminé — "
                     + (freed >= 1024 ? (freed / 1024.0).ToString("0.0") + " Go" : freed + " Mo")
                     + " libérés (" + n + " élément(s)).";
            };
            return a;
        }

        // ------------------------------------------------------------------
        //  Bibliothèques de jeu manquantes (cause n°1 d'un jeu qui ne démarre pas)
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MeasureLibs()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Vérification des bibliothèques"; a.AutoRun = true; a.IsChange = false;
            a.Run = delegate (Action<string, int> log)
            {
                int missing;
                try { missing = LibScan.MissingEssentialCount(); }
                catch { return "Je n'ai pas pu vérifier les bibliothèques."; }
                if (missing <= 0)
                    return "✅ Toutes les bibliothèques essentielles sont là (Visual C++, DirectX, .NET).\nSi un jeu refuse quand même de démarrer, le problème est ailleurs — dis-le-moi.";
                return "⚠ " + missing + " bibliothèque(s) essentielle(s) manquante(s) (Visual C++, DirectX, .NET…).\n"
                     + "C'est LA cause classique d'un jeu qui ne se lance pas ou qui plante au démarrage.\n"
                     + "Le panneau Bibliothèques les installe en un clic, elles sont déjà pré-cochées.";
            };
            return a;
        }

        // ------------------------------------------------------------------
        //  Filet de sécurité
        // ------------------------------------------------------------------
        public static DocAssistant.ChatAction MakeRestorePoint()
        {
            var a = new DocAssistant.ChatAction();
            a.Label = "Créer un point de restauration";
            a.IsChange = true;
            a.Warning = "Ajoute une photo du système Windows. N'efface rien et ne touche pas à tes fichiers. Peut prendre une minute.";
            a.Run = delegate (Action<string, int> log)
            {
                try { Sys.CreateRestorePoint("Fluide — avant modification", log); }
                catch { return "Le point de restauration n'a pas pu être créé (la restauration système est peut-être désactivée)."; }
                return "✅ Point de restauration créé. Tu peux manipuler l'esprit tranquille : Windows sait revenir ici.";
            };
            return a;
        }
    }
}
