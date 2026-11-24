using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// Logique PURE de la mise à jour (sans réseau ni interface), testée hors ligne :
    /// fréquence de vérification, empreinte SHA256, notes lisibles, notification unique par version.
    /// </summary>
    internal static class MajLogique
    {
        /// <summary>ONYX reste ouvert toute la journée dans la zone de notification : une seule
        /// vérification par jour faisait attendre jusqu'au lendemain une version publiée le soir.
        /// 4 requêtes par jour restent très loin de la limite de GitHub (60 par heure).</summary>
        public static readonly TimeSpan Intervalle = TimeSpan.FromHours(6);

        /// <summary>Paramètres de l'installateur Inno Setup lancé par ONYX : pas d'assistant (une
        /// simple barre de progression), pas de question, pas de redémarrage, fermeture des
        /// fichiers encore ouverts, et RELANCE=1 pour que l'installateur rouvre ONYX à la fin.
        /// RELANCE n'est pas passé par winget : une installation silencieuse de winget ne doit
        /// pas ouvrir l'application.</summary>
        public const string ArgsSilencieux = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELANCE=1";

        private const string Format = "yyyy-MM-dd HH:mm";

        public static string Horodatage(DateTime t) { return t.ToString(Format, CultureInfo.InvariantCulture); }

        /// <summary>Une vérification est-elle due ? Accepte aussi l'ancien tampon « yyyyMMdd ».
        /// Un tampon dans le futur (horloge reculée) ne doit pas bloquer les vérifications.</summary>
        public static bool EstDue(string tampon, DateTime maintenant)
        {
            if (string.IsNullOrWhiteSpace(tampon)) return true;
            string s = tampon.Trim();
            DateTime t;
            if (!DateTime.TryParseExact(s, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)
                && !DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                return true;
            if (t > maintenant.AddHours(1)) return true;
            return maintenant - t >= Intervalle;
        }

        /// <summary>« sha256:&lt;64 hex&gt; » (champ digest de GitHub) ou 64 hex bruts → hex en
        /// minuscules ; null pour tout le reste.</summary>
        public static string Sha256DuDigest(string digest)
        {
            if (string.IsNullOrWhiteSpace(digest)) return null;
            string s = digest.Trim();
            if (s.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7);
            else if (s.Contains(":")) return null;
            if (s.Length != 64) return null;
            foreach (char c in s)
                if (!Uri.IsHexDigit(c)) return null;
            return s.ToLowerInvariant();
        }

        /// <summary>Le fichier a-t-il l'empreinte annoncée ? Sans empreinte annoncée (manifeste
        /// personnel qui n'en donne pas), il n'y a rien à contrôler : true.</summary>
        public static bool EmpreinteOk(string chemin, string attendu)
        {
            if (string.IsNullOrEmpty(attendu)) return true;
            try
            {
                using (var fs = File.OpenRead(chemin))
                {
                    string h = Convert.ToHexString(SHA256.HashData(fs));
                    return string.Equals(h, attendu.Trim(), StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        /// <summary>Notes de version (Markdown de GitHub) → texte brut pour une fenêtre Windows.
        /// La section « Installation » (consignes de téléchargement manuel, nombre de tests) ne
        /// concerne pas quelqu'un qui met à jour depuis ONYX : elle est retirée.</summary>
        public static string NotesLisibles(string md, int max)
        {
            if (string.IsNullOrWhiteSpace(md)) return "";
            var sb = new StringBuilder();
            bool vide = true;   // la dernière ligne écrite est vide (ou rien encore)
            foreach (string brute in md.Replace("\r", "").Split('\n'))
            {
                string l = brute.Trim();
                bool titre = l.StartsWith("#");
                if (titre) l = l.TrimStart('#').Trim();
                if (titre && l.StartsWith("Installation", StringComparison.OrdinalIgnoreCase)) break;
                if (l.StartsWith("- ") || l.StartsWith("* ")) l = "• " + l.Substring(2).Trim();
                l = Regex.Replace(l, @"\[([^\]]*)\]\([^)]*\)", "$1");
                l = l.Replace("**", "").Replace("__", "").Replace("`", "");
                if (l.Length == 0)
                {
                    if (!vide) { sb.Append('\n'); vide = true; }
                    continue;
                }
                sb.Append(l).Append('\n');
                vide = false;
            }
            string s = sb.ToString().TrimEnd('\n');
            if (s.Length <= max) return s;
            int coupe = s.LastIndexOf('\n', Math.Max(0, max - 1));
            if (coupe <= 0) coupe = Math.Max(0, max - 1);
            return s.Substring(0, coupe).TrimEnd() + "\n…";
        }

        /// <summary>La notification Windows se montre UNE fois par version : la vérification
        /// toutes les 6 heures ne doit pas la répéter. Le bandeau dans l'app reste, lui.</summary>
        public static bool ANotifier(string dispo, string dejaNotifiee)
        {
            if (string.IsNullOrWhiteSpace(dispo)) return false;
            return !string.Equals(dispo.Trim(), (dejaNotifiee ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>« 12,3 / 55,3 Mo — 22 % », ou « 5,0 Mo » si la taille totale est inconnue.</summary>
        public static string Progression(long fait, long total)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            string f = (fait / 1048576.0).ToString("0.0", fr);
            if (total <= 0) return f + " Mo";
            int pct = (int)Math.Min(100, fait * 100 / total);
            return f + " / " + (total / 1048576.0).ToString("0.0", fr) + " Mo — " + pct + " %";
        }
    }
}
