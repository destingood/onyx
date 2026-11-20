using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Mode Efficacité du Mode Jeu (logique PURE) : priorité Idle + EcoQoS, comme le Gestionnaire
    /// des tâches. Rien n'est fermé : l'état d'origine de chaque processus est noté AVANT le
    /// changement, dans un journal sur disque, puis rendu à l'identique à la sortie (ou au
    /// lancement suivant si ONYX a planté). La machine vit dans ApplisDeFond et GameBoost.
    /// </summary>
    internal static class Efficacite
    {
        public const int PrioriteIdle = 0x40;          // ProcessPriorityClass.Idle
        public const uint VitesseExecution = 1;        // PROCESS_POWER_THROTTLING_EXECUTION_SPEED

        /// <summary>Une ligne du journal : de quoi retrouver LE MÊME processus et le remettre à l'identique.</summary>
        public sealed class Entree
        {
            public int Pid; public long DebutTicks;     // Process.StartTime.ToUniversalTime().Ticks
            public string Nom = "";
            public int Priorite;                        // (int)ProcessPriorityClass d'origine
            public uint Controle, Etat;                 // PROCESS_POWER_THROTTLING_STATE d'origine (ControlMask, StateMask)
        }

        /// <summary>Vrai si le processus est déjà en Efficacité : rien à écrire, rien à rétablir.</summary>
        public static bool DejaEnEfficacite(int priorite, uint controle, uint etat)
        {
            return priorite == PrioriteIdle
                && (controle & VitesseExecution) != 0
                && (etat & VitesseExecution) != 0;
        }

        /// <summary>Même processus (et non un PID réutilisé depuis) : même PID, même heure de début.</summary>
        public static bool MemeProcessus(Entree e, int pid, long debutTicks)
        {
            return e != null && e.Pid == pid && e.DebutTicks == debutTicks && debutTicks != 0;
        }

        /// <summary>Journal : pid|debutTicks|priorite|controle|etat|nom, une ligne par entrée.</summary>
        public static string Ecrit(IEnumerable<Entree> entrees)
        {
            var sb = new StringBuilder();
            if (entrees == null) return "";
            foreach (Entree e in entrees)
            {
                if (e == null) continue;
                string nom = (e.Nom ?? "").Replace("|", "").Replace("\r", "").Replace("\n", "");
                sb.Append(e.Pid.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(e.DebutTicks.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(e.Priorite.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(e.Controle.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(e.Etat.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(nom).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Relit le journal. Tolérant : une ligne abîmée est ignorée.</summary>
        public static List<Entree> Lit(string texte)
        {
            var l = new List<Entree>();
            if (string.IsNullOrEmpty(texte)) return l;
            foreach (string brut in texte.Split('\n'))
            {
                string ligne = brut.TrimEnd('\r');
                if (ligne.Length == 0) continue;
                string[] p = ligne.Split(new[] { '|' }, 6);
                if (p.Length < 6) continue;
                int pid, prio; long debut; uint ctl, etat;
                if (!int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out pid)) continue;
                if (!long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out debut)) continue;
                if (!int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out prio)) continue;
                if (!uint.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out ctl)) continue;
                if (!uint.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out etat)) continue;
                l.Add(new Entree { Pid = pid, DebutTicks = debut, Priorite = prio, Controle = ctl, Etat = etat, Nom = p[5] });
            }
            return l;
        }

        /// <summary>La ligne de journal : ce qui a été mis en Efficacité, laissé, refusé.</summary>
        public static string Texte(int mis, int deja, int refus, int servicesArretes)
        {
            if (mis == 0 && deja == 0 && refus == 0 && servicesArretes == 0)
                return "Applications de fond : rien à mettre en Efficacité.";
            var sb = new StringBuilder("Applications de fond : ");
            sb.Append(mis).Append(" processus en mode Efficacité (rien n'est fermé, tout est rétabli à la sortie)");
            if (deja > 0) sb.Append(", ").Append(deja).Append(" déjà en Efficacité (non touchés)");
            if (refus > 0) sb.Append(", ").Append(refus).Append(" refusé(s) par Windows (laissés tels quels)");
            if (servicesArretes > 0) sb.Append(", ").Append(servicesArretes).Append(" service(s) suspendu(s)");
            sb.Append('.');
            return sb.ToString();
        }
    }
}
