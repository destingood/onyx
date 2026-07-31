using System;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// VERDICT « PILOTE GPU INSTABLE » : croise le NOMBRE d'erreurs pilote (journal d'événements)
    /// avec l'ÂGE du pilote et les crashs d'applications, puis CONCLUT — et donne la marche à suivre
    /// dans l'ordre. Une carte récente qui plante n'est presque jamais « morte » : c'est le pilote.
    /// Le calcul est PUR (Verdict) donc testable ; la lecture machine est à part.
    /// </summary>
    internal static class GpuStability
    {
        public sealed class Result
        {
            public int Level;            // 0 = sain, 1 = à surveiller, 2 = instable, 3 = très instable
            public string Title;
            public string Advice;        // marche à suivre, dans l'ordre
        }

        /// <summary>Verdict PUR à partir des chiffres (testable sans machine).</summary>
        public static Result Verdict(int gpuErrors14d, int appCrashes14d, int driverAgeDays)
        {
            var r = new Result();
            bool fresh = driverAgeDays >= 0 && driverAgeDays <= 30;
            bool old = driverAgeDays > 540;

            if (gpuErrors14d >= 100) { r.Level = 3; r.Title = "Pilote GPU TRÈS INSTABLE — " + gpuErrors14d + " erreurs en 14 jours"; }
            else if (gpuErrors14d >= 30) { r.Level = 2; r.Title = "Pilote GPU instable — " + gpuErrors14d + " erreurs en 14 jours"; }
            else if (gpuErrors14d >= 5) { r.Level = 1; r.Title = "Pilote GPU : quelques erreurs (" + gpuErrors14d + " en 14 jours)"; }
            else { r.Level = 0; r.Title = "Pilote GPU stable (" + gpuErrors14d + " erreur(s) en 14 jours)"; }

            var sb = new System.Text.StringBuilder();
            if (r.Level == 0)
            {
                sb.Append("Rien à faire côté pilote. Si un jeu plante quand même, la cause est ailleurs "
                        + "(thermique, alimentation, RAM/XMP, fichiers du jeu) — lance l'enquête du Copilote.");
            }
            else
            {
                sb.Append("À faire DANS CET ORDRE (tout est gratuit) :\n");
                int n = 1;
                if (fresh)
                    sb.Append(n++).Append(". Ce pilote est RÉCENT (").Append(driverAgeDays).Append(" j) et il plante : reviens à la version PRÉCÉDENTE "
                            + "(les « Studio »/anciennes builds sont sur le site du constructeur). Un pilote neuf n'est pas toujours meilleur.\n");
                sb.Append(n++).Append(". Réinstallation PROPRE avec DDU (gratuit, 1 clic depuis Bibliothèques) : "
                        + "il efface les restes des anciens pilotes, cause n°1 des instabilités qui traînent.\n");
                if (old)
                    sb.Append(n++).Append(". Ton pilote a ").Append(driverAgeDays / 30).Append(" mois : après le nettoyage DDU, installe la version actuelle.\n");
                sb.Append(n++).Append(". Coupe TOUT overclock GPU (MSI Afterburner remis à zéro, courbe désactivée) — même un OC « stable » depuis 6 mois peut le devenir avec un nouveau jeu.\n");
                if (r.Level >= 2)
                    sb.Append(n++).Append(". Vérifie l'alimentation et les températures : câbles PCIe bien enfoncés (12VHPWR pour les RTX 40), et le panneau Températures pour surveiller pendant une partie.\n");
                if (appCrashes14d >= 5)
                    sb.Append(n++).Append(". ").Append(appCrashes14d).Append(" applications ont planté en 14 jours : lance aussi « Réparer Windows » (fichiers système) — les deux vont souvent ensemble.\n");
                sb.Append("Après ces étapes, reviens ici : le compteur d'erreurs doit retomber.");
            }
            r.Advice = sb.ToString();
            return r;
        }

        /// <summary>Verdict à partir de la MACHINE (journal d'événements + WMI).</summary>
        public static Result Current()
        {
            int gerr = 0, crashes = 0, age = -1;
            try { gerr = CrashScan.GpuDriverErrors(14); } catch { }
            try { var l = CrashScan.RecentDetailed(14); if (l != null) crashes = l.Count; } catch { }
            try { var d = Diagnostics.GpuDriver(); if (d != null) age = d.AgeDays; } catch { }
            return Verdict(gerr, crashes, age);
        }

        /// <summary>Texte complet (verdict + contexte matériel), prêt pour une fenêtre ou la console.</summary>
        public static string Text()
        {
            var v = Current();
            var sb = new System.Text.StringBuilder();
            sb.Append(v.Level == 0 ? "✅ " : v.Level == 1 ? "⚠️ " : "🚨 ").Append(v.Title).Append("\r\n\r\n");
            try
            {
                var d = Diagnostics.GpuDriver();
                if (d != null) sb.Append("Carte : ").Append(d.Name).Append("\r\nPilote : v").Append(d.Version)
                                 .Append(d.AgeDays >= 0 ? " (installé il y a " + d.AgeDays + " jours)" : "").Append("\r\n\r\n");
            }
            catch { }
            sb.Append(v.Advice.Replace("\n", "\r\n"));
            return sb.ToString();
        }
    }

    /// <summary>
    /// EXPORT DU DIAGNOSTIC COMPLET : un seul fichier texte avec TOUT le contexte (auto-diagnostic,
    /// infos de support, stabilité GPU, ce qui a changé, journal des actions, tendance santé).
    /// Pour un forum, un SAV, un ami qui dépanne — ou soi-même dans trois mois.
    /// </summary>
    internal static class DiagExport
    {
        public static string Build()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("=========== ONYX — DIAGNOSTIC COMPLET ===========\r\n");
            sb.Append("Généré le ").Append(DateTime.Now.ToString("dd/MM/yyyy à HH:mm")).Append("\r\n\r\n");
            Section(sb, "INFOS DE SUPPORT", SafeCall(() => SelfCheck.SupportInfo()));
            Section(sb, "AUTO-DIAGNOSTIC D'ONYX", SafeCall(() => SelfCheck.Text()));
            Section(sb, "STABILITÉ DU PILOTE GPU", SafeCall(() => GpuStability.Text()));
            Section(sb, "SANTÉ DES DISQUES (SMART)", SafeCall(() => ChatActions.DiskHealthText()));
            Section(sb, "CE QUI A CHANGÉ SUR LE PC", SafeCall(() => StateDiff.DiffText()));
            Section(sb, "TENDANCE SANTÉ", SafeCall(() => HealthTrend.TrendText()));
            Section(sb, "JOURNAL DES ACTIONS D'ONYX", SafeCall(() => Journal.TailText(20)));
            sb.Append("Aucune donnée personnelle n'est incluse (ni nom d'utilisateur, ni IP, ni chemin privé).\r\n");
            return sb.ToString();
        }

        private static string SafeCall(Func<string> f)
        {
            try { string s = f(); return string.IsNullOrEmpty(s) ? "(rien à signaler)" : s; }
            catch (Exception ex) { return "(indisponible : " + ex.Message + ")"; }
        }

        private static void Section(System.Text.StringBuilder sb, string title, string body)
        {
            sb.Append("----- ").Append(title).Append(" -----\r\n");
            sb.Append(body.Replace("\n", "\r\n")).Append("\r\n\r\n");
        }

        /// <summary>Écrit ONYX-diagnostic-AAAAMMJJ-HHmm.txt sur le Bureau. Chemin complet, ou null.</summary>
        public static string Save()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "ONYX-diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt");
                File.WriteAllText(path, Build(), new System.Text.UTF8Encoding(true));
                return path;
            }
            catch { return null; }
        }
    }
}
