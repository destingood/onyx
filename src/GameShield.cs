using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// DEFENDER PENDANT LE JEU : la protection en temps réel analyse les fichiers à chaque accès.
    /// Sur des jeux qui streament des Go de textures, ça provoque des micro-freezes. Exclure les
    /// DOSSIERS DE JEUX de l'analyse est le remède documenté — mais personne ne le fait, car c'est
    /// enfoui dans les réglages Windows.
    ///
    /// ONYX est honnête sur le compromis : Defender reste ACTIF (rien n'est désactivé), seuls les
    /// dossiers de jeux issus de boutiques officielles sortent de l'analyse temps réel. C'est
    /// réversible en un clic, et jamais appliqué automatiquement.
    /// </summary>
    internal static class GameShield
    {
        /// <summary>Exécute une commande PowerShell et rend sa sortie (null si échec).</summary>
        private static string Ps(string command, int timeoutMs)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("powershell",
                    "-NoProfile -NonInteractive -Command \"" + command.Replace("\"", "\\\"") + "\"")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return null; }
                    return outp;
                }
            }
            catch { return null; }
        }

        /// <summary>La protection temps réel de Defender est-elle active ? (sinon, rien à optimiser)</summary>
        public static bool RealtimeOn()
        {
            string s = Ps("(Get-MpComputerStatus).RealTimeProtectionEnabled", 8000);
            return s != null && s.Trim().StartsWith("True", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Exclusions actuelles. null = illisible (droits administrateur manquants).</summary>
        public static List<string> CurrentExclusions()
        {
            string s = Ps("$e=(Get-MpPreference).ExclusionPath; if ($e) { $e -join '|' } else { 'AUCUNE' }", 10000);
            if (s == null) return null;
            s = s.Trim();
            if (s.Length == 0 || s.IndexOf("administrator", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("administrateur", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            var outp = new List<string>();
            if (s == "AUCUNE") return outp;
            foreach (var p in s.Split('|'))
                if (p.Trim().Length > 0) outp.Add(p.Trim());
            return outp;
        }

        /// <summary>Dossiers de jeux à proposer : les « common » de chaque bibliothèque Steam.</summary>
        public static List<string> SuggestedPaths()
        {
            var outp = new List<string>();
            try
            {
                string steam = SteamGames.SteamPath();
                if (steam == null) return outp;
                string vdf = null;
                try
                {
                    string f = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                    if (File.Exists(f)) vdf = File.ReadAllText(f);
                }
                catch { }
                foreach (var lib in SteamGames.ParseLibraryPaths(vdf, steam))
                {
                    string common = Path.Combine(lib, "common");
                    if (Directory.Exists(common) && !outp.Contains(common)) outp.Add(common);
                }
            }
            catch { }
            return outp;
        }

        /// <summary>Ce qui MANQUE dans les exclusions (comparaison insensible à la casse et au « \ »
        /// final, et un dossier couvert par un parent déjà exclu compte comme protégé). PUR.</summary>
        public static List<string> Missing(List<string> current, List<string> suggested)
        {
            var outp = new List<string>();
            if (suggested == null) return outp;
            if (current == null) return new List<string>(suggested);
            foreach (var s in suggested)
            {
                string a = Norm(s);
                bool covered = false;
                foreach (var c in current)
                {
                    string b = Norm(c);
                    if (b.Length == 0) continue;
                    if (a == b || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { covered = true; break; }
                }
                if (!covered) outp.Add(s);
            }
            return outp;
        }

        private static string Norm(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            string s = p.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return s.ToUpperInvariant();
        }

        /// <summary>Ajoute (ou retire) des dossiers aux exclusions Defender. true si la commande a passé.</summary>
        public static bool SetExclusions(List<string> paths, bool add)
        {
            if (paths == null || paths.Count == 0) return false;
            var sb = new System.Text.StringBuilder();
            foreach (var p in paths)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append('\'').Append(p.Replace("'", "''")).Append('\'');
            }
            string cmd = (add ? "Add-MpPreference -ExclusionPath " : "Remove-MpPreference -ExclusionPath ") + sb + "; 'OK'";
            string r = Ps(cmd, 30000);
            return r != null && r.IndexOf("OK", StringComparison.Ordinal) >= 0;
        }

        /// <summary>Diagnostic complet, prêt pour le chat.</summary>
        public static string Text(out List<string> missing)
        {
            missing = new List<string>();
            var sugg = SuggestedPaths();
            if (sugg.Count == 0)
                return "🛡 Aucune bibliothèque Steam trouvée : rien à exclure de l'analyse Defender de ce côté.";
            if (!RealtimeOn())
                return "🛡 La protection en temps réel de Defender est DÉSACTIVÉE : elle ne peut donc pas ralentir tes "
                     + "jeux. (Rien à faire ici — et je ne te conseillerai jamais de désactiver ton antivirus.)";
            var cur = CurrentExclusions();
            if (cur == null)
                return "🛡 Je ne peux pas lire les exclusions de Defender : il faut lancer ONYX en tant "
                     + "qu'ADMINISTRATEUR (clic droit sur l'exe → « Exécuter en tant qu'administrateur »).";
            missing = Missing(cur, sugg);
            var sb = new System.Text.StringBuilder();
            sb.Append("🛡 Defender & tes jeux :\n");
            sb.Append("Protection temps réel : ACTIVE (c'est bien — on n'y touche pas).\n");
            sb.Append("Exclusions déjà en place : ").Append(cur.Count).Append('\n');
            if (missing.Count == 0)
            {
                sb.Append("✅ Tes ").Append(sugg.Count).Append(" dossier(s) de jeux Steam sont DÉJÀ exclus de l'analyse : "
                        + "aucun micro-freeze causé par l'antivirus pendant tes parties.");
                return sb.ToString();
            }
            sb.Append("\n⚠️ ").Append(missing.Count).Append(" dossier(s) de jeux ne sont PAS exclus :\n");
            foreach (var m in missing) sb.Append("• ").Append(m).Append('\n');
            sb.Append("\nÀ chaque fois qu'un jeu lit un fichier (textures, shaders), Defender l'analyse : c'est une cause "
                    + "connue de micro-saccades. Les exclure supprime ce surcoût.\n")
              .Append("⚖️ LE COMPROMIS, en clair : Defender reste ACTIF partout ailleurs ; seuls ces dossiers de jeux "
                    + "(issus d'une boutique officielle) sortent de l'analyse temps réel. N'accepte que si tu n'y installes "
                    + "pas de fichiers douteux — et c'est réversible en un clic.");
            return sb.ToString();
        }
    }
}
