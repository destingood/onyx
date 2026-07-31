using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// JOURNAL DE BORD : chaque action qui CHANGE quelque chose (bouton IsChange du Copilote) est
    /// tracée — date, heure, libellé — dans bt-journal.txt à côté de l'exe. L'utilisateur peut
    /// demander « qu'est-ce que tu as changé ? » et obtenir l'historique daté. La confiance par
    /// la transparence : ONYX n'a rien à cacher de ce qu'il fait.
    /// </summary>
    internal static class Journal
    {
        private static readonly object Gate = new object();
        private static string PathFile
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-journal.txt"); }
        }

        /// <summary>Trace une action effectuée (appelé après un clic « changement » réussi).</summary>
        public static void Add(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return;
            try
            {
                lock (Gate)
                    File.AppendAllText(PathFile, DateTime.Now.ToString("dd/MM/yyyy HH:mm") + " — " + label.Trim() + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>Les n dernières entrées (les plus récentes en premier), ou liste vide.</summary>
        public static List<string> Tail(int n)
        {
            var outp = new List<string>();
            try
            {
                if (!File.Exists(PathFile)) return outp;
                string[] all = File.ReadAllLines(PathFile);
                for (int i = all.Length - 1; i >= 0 && outp.Count < n; i--)
                    if (!string.IsNullOrWhiteSpace(all[i])) outp.Add(all[i].Trim());
            }
            catch { }
            return outp;
        }

        /// <summary>Texte prêt pour le chat : l'historique, ou l'aveu honnête qu'il est vide.</summary>
        public static string TailText(int n)
        {
            var t = Tail(n);
            if (t.Count == 0)
                return "📓 Journal de bord : encore VIDE — je n'ai appliqué aucun changement depuis son ouverture.\n"
                     + "(Chaque bouton « changement » que tu cliques sera tracé ici, daté. Les mesures, elles, ne changent rien.)";
            var sb = new System.Text.StringBuilder();
            sb.Append("📓 Journal de bord — mes ").Append(t.Count).Append(" dernière(s) action(s) sur ce PC :\n");
            foreach (var l in t) sb.Append("• ").Append(l).Append('\n');
            sb.Append("(Tout est réversible — dis-moi ce que tu veux annuler.)");
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// LE GARDIEN : au lancement d'ONYX (1 fois par jour max), vérifie EN SILENCE les signaux
    /// vitaux — disque presque plein, santé SMART des disques, redémarrage Windows en attente,
    /// uptime excessif — et ne se manifeste QUE s'il y a une alerte (notification discrète).
    /// Zéro réseau, zéro pop-up si tout va bien : un gardien, pas une alarme de voiture.
    /// </summary>
    internal static class Guardian
    {
        private static string StampPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-gardien.txt"); }
        }

        /// <summary>true si le contrôle quotidien n'a pas encore tourné aujourd'hui.</summary>
        public static bool DueToday()
        {
            try
            {
                string today = DateTime.Now.ToString("yyyyMMdd");
                if (File.Exists(StampPath) && File.ReadAllText(StampPath).Trim() == today) return false;
                File.WriteAllText(StampPath, today);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Les alertes du moment (liste vide = tout va bien, le Gardien se tait).</summary>
        public static List<string> Alerts()
        {
            var a = new List<string>();
            try
            {
                var di = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                double freeGb = di.AvailableFreeSpace / 1073741824.0;
                if (freeGb < 10) a.Add("Disque système : " + freeGb.ToString("0") + " Go libres — Windows va ralentir (dis « libère de la place »).");
            }
            catch { }
            try
            {
                foreach (var d in Diagnostics.DiskHealth())
                    if (d.Status != 0)
                        a.Add("⚠️ SANTÉ DISQUE : « " + d.Name + " » signale « " + Diagnostics.DiskHealthLabel(d.Status)
                            + " » — SAUVEGARDE tes données importantes MAINTENANT.");
            }
            catch { }
            try { if (ChatActions.RebootPendingPublic()) a.Add("Windows attend un redémarrage pour finaliser des mises à jour."); } catch { }
            try
            {
                var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
                if (up.Days >= 14) a.Add("PC allumé depuis " + up.Days + " jours sans vrai redémarrage — redémarre à l'occasion.");
            }
            catch { }
            try
            {
                int gerr = CrashScan.GpuDriverErrors(7);
                if (gerr >= 50) a.Add("Pilote GPU : " + gerr + " erreurs signalées en 7 jours — instable. Dis « ça crash » au Copilote (réinstallation propre DDU conseillée).");
            }
            catch { }
            try
            {
                var cr = CrashScan.RecentDetailed(2);
                if (cr != null && cr.Count >= 2) a.Add(cr.Count + " crashs d'applications ces 48 h — dis « ça crash » au Copilote pour la cause exacte.");
            }
            catch { }
            return a;
        }
    }
}
