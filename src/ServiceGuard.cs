using System;
using System.Collections.Generic;

namespace BTOptimizer
{
    /// <summary>
    /// SERVICES QU'ON NE DOIT JAMAIS DÉSACTIVER — parce qu'une page de Windows en meurt.
    ///
    /// Certains services paraissent inutiles sur un PC fixe et figurent dans toutes les listes de
    /// « services à couper ». Sauf qu'une page des Paramètres les interroge par RPC : désactivés,
    /// ils n'ont pas d'endpoint enregistré, l'appel échoue, et la page s'ouvre sur un rectangle
    /// vide. L'utilisateur ne fait jamais le lien — il a coupé un service il y a trois semaines,
    /// et c'est une page sans rapport apparent qui plante aujourd'hui.
    ///
    /// « MANUEL » règle le problème sans rien coûter : le service ne démarre PAS de lui-même, il
    /// ne consomme donc rien au repos, mais Windows peut le lancer à la demande quand une page en
    /// a besoin. C'est le seul état correct — jamais « Désactivé », et pas besoin d'« Automatique ».
    ///
    /// Ce garde-fou tourne à CHAQUE lancement, quel que soit le chemin par lequel le service a été
    /// coupé : preset Recommandé, eSport, mode Simple, config auto, fenêtre Services, ou même un
    /// autre outil d'optimisation. Corriger le tweak ne suffisait pas — il fallait réparer les
    /// machines déjà touchées.
    /// </summary>
    internal static class ServiceGuard
    {
        public sealed class Regle
        {
            public string Service;
            public string Nom;        // nom lisible
            public string Pourquoi;   // ce qui casse quand il est désactivé
        }

        /// <summary>
        /// Table des services à ne jamais désactiver. Chaque entrée vient d'un cas CONSTATÉ, pas
        /// d'une précaution théorique : on n'annule pas une optimisation sans preuve.
        /// </summary>
        public static readonly Regle[] Regles =
        {
            new Regle {
                Service = "SensorService",
                Nom = "Service de capteurs",
                Pourquoi = "la page « Marche/Arrêt » des Paramètres Windows plante (rectangle vide) : "
                         + "le réglage « Économiseur d'énergie » interroge les capteurs, dont celui de "
                         + "luminosité ambiante"
            }
        };

        /// <summary>Décision PURE : faut-il réparer, connaissant le type de démarrage ?
        /// 4 = désactivé. Tout le reste est acceptable — on ne touche pas à ce qui va bien.</summary>
        public static bool ARepairer(int? start) { return start.HasValue && start.Value == 4; }

        private static int? Demarrage(string service)
        {
            try
            {
                object v = Sys.GetMachine(@"SYSTEM\CurrentControlSet\Services\" + service, "Start");
                if (v == null) return null;   // service absent : rien à réparer
                return Convert.ToInt32(v);
            }
            catch { return null; }
        }

        /// <summary>Règles actuellement violées sur cette machine.</summary>
        public static List<Regle> Violations()
        {
            var l = new List<Regle>();
            foreach (Regle r in Regles)
                if (ARepairer(Demarrage(r.Service))) l.Add(r);
            return l;
        }

        /// <summary>
        /// Réparation silencieuse au lancement : passage en MANUEL, jamais en automatique.
        /// Renvoie le nombre de services réparés. N'échoue jamais bruyamment.
        /// </summary>
        public static int Soigne(Action<string, int> log)
        {
            int n = 0;
            foreach (Regle r in Violations())
            {
                try
                {
                    Sys.ConfigureService(r.Service, "demand", false, false);
                    n++;
                    if (log != null)
                        log("Service « " + r.Nom + " » remis en démarrage MANUEL : désactivé, " + r.Pourquoi
                          + ". En manuel il ne tourne pas non plus au repos — le gain est identique, sans le bug.", 2);
                }
                catch (Exception ex)
                {
                    if (log != null) log("Impossible de réparer " + r.Service + " : " + ex.Message, 3);
                }
            }
            return n;
        }

        /// <summary>Texte du constat, pour le diagnostic. PUR.</summary>
        public static string Texte(List<Regle> violations)
        {
            if (violations == null || violations.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (Regle r in violations)
                sb.Append("« ").Append(r.Nom).Append(" » désactivé — ").Append(r.Pourquoi).Append(". ");
            return sb.ToString().Trim();
        }
    }
}
