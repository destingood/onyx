using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Mode Compétition : un bouton qui pose d'un coup le profil Auto sur les jeux sans profil,
    /// le Mode Jeu auto, le plafond FPS recommandé et les réglages eSport prouvés — puis les
    /// retire EXACTEMENT, sans toucher à ce que l'utilisateur a réglé entre-temps.
    ///
    /// Cette moitié est PURE (testée au banc) ; le geste vit dans CompetitionMachine.cs.
    /// </summary>
    internal static partial class Competition
    {
        public const string Fichier = "bt-competition.txt";

        public sealed class Etat
        {
            public DateTime Active;
            public bool ModeJeuPose;                           // Mode Jeu auto allumé PAR la Compétition
            public int PlafondAvant = -1;                       // -1 : plafond non touché
            public List<string> Profils = new List<string>();  // profils Auto posés par la Compétition
            public List<string> Reglages = new List<string>(); // réglages eSport gardés par la preuve
        }

        /// <summary>Une compétition déjà active se désactive d'abord : sinon l'état d'avant serait
        /// écrasé par un état « avant » qui est déjà celui de la compétition.</summary>
        public static bool PeutActiver(Etat existant) { return existant == null; }

        public static List<string> JeuxSansProfil(IEnumerable<string> installes, IDictionary<string, Tuple<string, long>> profils)
        {
            var avec = new HashSet<string>(profils.Keys, StringComparer.OrdinalIgnoreCase);
            return installes.Where(j => !string.IsNullOrEmpty(j) && !avec.Contains(j)).ToList();
        }

        /// <summary>Seulement les profils encore présents ET encore en « Auto » : un profil que
        /// l'utilisateur a repassé en manuel ou supprimé lui appartient désormais.</summary>
        public static List<string> ProfilsARetirer(Etat e, IDictionary<string, Tuple<string, long>> profils)
        {
            var l = new List<string>();
            foreach (string jeu in e.Profils)
            {
                Tuple<string, long> p;
                if (profils.TryGetValue(jeu, out p) && string.Equals(p.Item1, "Auto", StringComparison.OrdinalIgnoreCase))
                    l.Add(jeu);
            }
            return l;
        }

        public static string Ecrit(Etat e)
        {
            var sb = new StringBuilder();
            sb.Append("active=").Append(e.Active.ToString("o", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("modejeu=").Append(e.ModeJeuPose ? "1" : "0").Append('\n');
            sb.Append("plafond=").Append(e.PlafondAvant.ToString(CultureInfo.InvariantCulture)).Append('\n');
            // « | » et non « , » : des noms de jeux portent une virgule (« Warhammer 40,000 »),
            // et « | » est déjà le séparateur de bt-game-affinity.txt, donc absent des noms.
            sb.Append("profils=").Append(string.Join("|", e.Profils)).Append('\n');
            sb.Append("reglages=").Append(string.Join("|", e.Reglages)).Append('\n');
            return sb.ToString();
        }

        /// <summary>Null si le texte est vide ou n'a pas de date d'activation lisible.</summary>
        public static Etat Lit(string texte)
        {
            if (string.IsNullOrWhiteSpace(texte)) return null;
            var e = new Etat();
            bool date = false;
            foreach (string brute in texte.Split('\n'))
            {
                string l = brute.Trim();
                int i = l.IndexOf('=');
                if (i <= 0) continue;
                string cle = l.Substring(0, i), val = l.Substring(i + 1).Trim();
                switch (cle)
                {
                    case "active":
                        date = DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out e.Active);
                        break;
                    case "modejeu": e.ModeJeuPose = val == "1"; break;
                    case "plafond":
                        int p;
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out p)) e.PlafondAvant = p;
                        break;
                    case "profils": e.Profils = Liste(val); break;
                    case "reglages": e.Reglages = Liste(val); break;
                }
            }
            return date ? e : null;
        }

        private static List<string> Liste(string val)
        {
            return val.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }
    }
}
