using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// AUTO-DIAGNOSTIC D'ONYX : l'app vérifie SA PROPRE installation (droits, dossier de données,
    /// WMI, journal d'événements, internet, IA locale, espace disque) et dit ce qui cloche AVANT que
    /// l'utilisateur se demande pourquoi une fonction ne répond pas. Et « Copier les infos de
    /// support » met dans le presse-papiers tout ce qu'il faut pour l'aider — sans donnée perso.
    /// </summary>
    internal static class SelfCheck
    {
        public sealed class Line { public bool Ok; public string What; public string Detail; }

        public static List<Line> Run()
        {
            var l = new List<Line>();

            bool admin = false;
            try
            {
                using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                    admin = new System.Security.Principal.WindowsPrincipal(id)
                        .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { }
            l.Add(new Line { Ok = admin, What = "Droits administrateur",
                Detail = admin ? "accordés — toutes les optimisations sont applicables"
                               : "MANQUANTS : relance ONYX en tant qu'administrateur, sinon la moitié des corrections échouera" });

            bool write = false;
            try { write = AppPaths.IsWritable(AppPaths.DataDir); } catch { }
            l.Add(new Line { Ok = write, What = "Dossier de données (mémoire, journal, profil)",
                Detail = write ? "accessible en écriture — " + AppPaths.Explain()
                               : "ÉCRITURE IMPOSSIBLE, même dans le dossier de repli : vérifie les droits de ton profil Windows" });

            bool wmi = false;
            try { wmi = Diagnostics.DiskHealth().Count > 0 || Diagnostics.GpuDriver() != null; } catch { }
            l.Add(new Line { Ok = wmi, What = "WMI (matériel, santé des disques, pilote GPU)",
                Detail = wmi ? "réponses correctes" : "MUET : le service « Windows Management Instrumentation » est peut-être arrêté" });

            bool evt = false;
            try { CrashScan.GpuDriverErrors(1); evt = true; } catch { }
            l.Add(new Line { Ok = evt, What = "Journal d'événements (crashs, SOS, Gardien)",
                Detail = evt ? "lisible" : "illisible — l'analyse des crashs sera limitée" });

            bool net = false;
            try { using (var p = new System.Net.NetworkInformation.Ping()) net = p.Send("1.1.1.1", 1500).Status == System.Net.NetworkInformation.IPStatus.Success; }
            catch { }
            l.Add(new Line { Ok = net, What = "Connexion internet (outils web du Copilote)",
                Detail = net ? "OK" : "absente — les outils locaux (heure, calcul, lune, nettoyage) marchent quand même" });

            // IA LOCALE RETIRÉE — voir LocalBrain.Retiree. Deux raisons de réécrire cette ligne
            // plutôt que de la laisser : elle SONDAIT 127.0.0.1:11434 à chaque auto-diagnostic
            // (1,2 s d'attente bornée pour une réponse désormais connue), et elle invitait à
            // installer un serveur dont ONYX ne se sert plus. Recommander l'inutile est
            // exactement le défaut corrigé sur le bouton LatencyMon.
            l.Add(new Line { Ok = true, What = "Copilote",
                Detail = "règles + base de connaissances locale — aucun serveur d'IA à installer, "
                       + "rien ne tourne en arrière-plan" });

            double freeGb = -1;
            try { freeGb = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)).AvailableFreeSpace / 1073741824.0; } catch { }
            l.Add(new Line { Ok = freeGb < 0 || freeGb >= 10, What = "Espace disque système",
                Detail = freeGb < 0 ? "inconnu" : freeGb.ToString("0") + " Go libres" + (freeGb < 10 ? " — TROP PEU, dis « libère de la place »" : "") });

            // ÉCHECS SILENCIEUX D'ONYX LUI-MÊME. Zéro est la réponse attendue ; tout le reste
            // désigne une fonction qui a rendu un résultat sans pouvoir le calculer. Voir
            // JournalTechnique : sans lui, une lecture impossible et une machine saine
            // produisaient exactement le même silence.
            int echecs = 0;
            try { echecs = JournalTechnique.EchecsDistincts(); } catch { }
            l.Add(new Line { Ok = echecs == 0, What = "Échecs internes notés depuis le lancement",
                Detail = echecs == 0
                    ? "aucun — tout ce qu'ONYX a tenté a abouti"
                    : echecs + " échec(s) distinct(s), détail dans " + JournalTechnique.Chemin });

            return l;
        }

        /// <summary>Texte lisible du diagnostic (avec ✅ / ❌).</summary>
        public static string Text()
        {
            var sb = new System.Text.StringBuilder();
            int bad = 0;
            foreach (var x in Run())
            {
                sb.Append(x.Ok ? "✅ " : "❌ ").Append(x.What).Append(" : ").Append(x.Detail).Append("\r\n");
                if (!x.Ok) bad++;
            }
            sb.Append("\r\n").Append(bad == 0
                ? "Tout est en ordre : ONYX dispose de tout ce dont il a besoin."
                : bad + " point(s) à corriger ci-dessus — c'est ce qui limite ONYX aujourd'hui.");
            return sb.ToString();
        }

        /// <summary>Bloc « infos de support » : tout ce qu'il faut pour dépanner, RIEN de personnel
        /// (pas de nom d'utilisateur, pas d'IP, pas de chemin perso).</summary>
        public static string SupportInfo()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("--- ONYX : infos de support ---\r\n");
            sb.Append("Projet    : ONYX par destingood (github.com/destingood/onyx)\r\n");
            try { sb.Append("ONYX      : v").Append(typeof(SelfCheck).Assembly.GetName().Version).Append("\r\n"); } catch { }
            try { sb.Append("Windows   : ").Append(Sys.OsDescription()).Append("\r\n"); } catch { }
            try
            {
                var d = Diagnostics.GpuDriver();
                if (d != null) sb.Append("GPU       : ").Append(d.Name).Append(" · pilote v").Append(d.Version)
                                 .Append(" (").Append(d.AgeDays).Append(" j)\r\n");
            }
            catch { }
            try
            {
                var di = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                sb.Append("Disque C: : ").Append((int)(di.AvailableFreeSpace / 1073741824)).Append(" Go libres / ")
                  .Append((int)(di.TotalSize / 1073741824)).Append(" Go\r\n");
            }
            catch { }
            try { sb.Append("Uptime    : ").Append((int)TimeSpan.FromMilliseconds(Environment.TickCount64).TotalHours).Append(" h\r\n"); } catch { }
            try { sb.Append("Crashs 14j: ").Append(CrashScan.RecentDetailed(14).Count).Append(" · erreurs pilote GPU : ").Append(CrashScan.GpuDriverErrors(14)).Append("\r\n"); } catch { }
            try
            {
                int ko = 0; foreach (var x in Run()) if (!x.Ok) ko++;
                sb.Append("Auto-diag : ").Append(ko == 0 ? "tout OK" : ko + " point(s) en échec").Append("\r\n");
            }
            catch { }
            // Les derniers échecs internes, NETTOYÉS. C'est ce qui manquait le plus pour
            // dépanner à distance : sans eux, on ne dispose que du symptôme. Chaque ligne passe
            // par SansDonneesPerso — un message d'exception cite très souvent le chemin du
            // profil, et la promesse de la dernière ligne ne serait plus tenue.
            try
            {
                List<string> derniers = JournalTechnique.Dernieres(8);
                if (derniers.Count > 0)
                {
                    sb.Append("Derniers échecs internes :\r\n");
                    foreach (string ligne in derniers)
                        sb.Append("  ").Append(JournalTechnique.SansDonneesPerso(ligne)).Append("\r\n");
                }
            }
            catch { }

            sb.Append("(Aucune donnée personnelle : ni nom d'utilisateur, ni adresse IP, ni chemin privé.)");
            return sb.ToString();
        }
    }
}
