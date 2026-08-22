using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// GARDE-FOU DE PUBLICATION : avant de fabriquer l'installateur, on vérifie que le dossier livré
    /// ne contient RIEN d'autre que l'exécutable. Le développeur teste l'app sur sa propre machine :
    /// le dossier de publication se remplit donc de SES données (mémoire du Copilote, faits appris,
    /// journal, jeton de mise à jour) et de symboles de débogage. Sans contrôle, tout cela part chez
    /// chaque utilisateur — c'est exactement ce qui a été trouvé avec « bt-appris.md », qui contenait
    /// de vraies conversations.
    ///
    /// Le classement est PUR (une liste de noms → un verdict) : il se teste sans toucher au disque.
    /// </summary>
    internal static class ReleaseGuard
    {
        public sealed class Leak { public string Name; public int Level; public string Why; }   // 2 = grave, 1 = à retirer

        /// <summary>Classe un nom de fichier du dossier de publication. null = légitime. PUR.</summary>
        public static Leak Inspect(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string n = name.Trim();
            string low = n.ToLowerInvariant();

            // Le binaire lui-même, et ce qui accompagne légitimement un déploiement .NET.
            if (low == "btoptimizer.exe") return null;
            if (low.EndsWith(".dll") || low.EndsWith(".json") && low.Contains("runtimeconfig")) return null;
            if (low == "tools" || low == "npi") return null;

            // GRAVE : secrets et données personnelles du développeur.
            if (low.Contains("token") || low.Contains("secret") || low.Contains("password") || low.Contains(".pfx")
                || low.Contains("credential") || low.Contains("apikey") || low.Contains("api-key"))
                return new Leak { Name = n, Level = 2, Why = "secret ou identifiant — ne doit JAMAIS être distribué" };
            if (low == "bt-appris.md" || low == "bt-memoire.txt" || low == "bt-journal.txt"
                || low == "bt-sante.csv" || low == "bt-erreurs.txt" || low == "bt-license.txt")
                return new Leak { Name = n, Level = 2, Why = "données personnelles du développeur (mémoire, journal, licence)" };
            if (low == "bt-etat" || low == "bt-savoir")
                return new Leak { Name = n, Level = 2, Why = "dossier de données personnelles (photos système / documents)" };

            // À RETIRER : symboles, sources, journaux, états divers.
            if (low.EndsWith(".pdb")) return new Leak { Name = n, Level = 1, Why = "symboles de débogage (structure interne du programme)" };
            if (low.EndsWith(".cs") || low.EndsWith(".csproj") || low.EndsWith(".sln"))
                return new Leak { Name = n, Level = 2, Why = "code source" };
            if (low.EndsWith(".etl") || low.EndsWith(".log")) return new Leak { Name = n, Level = 1, Why = "trace/journal technique" };
            if (low.StartsWith("bt-")) return new Leak { Name = n, Level = 1, Why = "fichier d'état généré par l'app en cours d'utilisation" };

            return null;
        }

        /// <summary>Verdict lisible pour une liste de noms. PUR → testable.</summary>
        public static string Verdict(IEnumerable<string> names, out int grave, out int minor)
        {
            grave = 0; minor = 0;
            var leaks = new List<Leak>();
            if (names != null)
                foreach (var n in names)
                {
                    var l = Inspect(n);
                    if (l == null) continue;
                    leaks.Add(l);
                    if (l.Level >= 2) grave++; else minor++;
                }
            var sb = new System.Text.StringBuilder();
            if (leaks.Count == 0)
                return "✅ Dossier de publication PROPRE : seul l'exécutable (et ses composants) sera livré.";
            sb.Append(grave > 0 ? "🚨 " : "⚠️ ").Append(leaks.Count).Append(" fichier(s) à NE PAS distribuer :\n");
            foreach (var l in leaks)
                sb.Append(l.Level >= 2 ? "  🚨 " : "  ⚠️ ").Append(l.Name).Append("  —  ").Append(l.Why).Append('\n');
            sb.Append(grave > 0
                ? "\nCes fichiers contiennent des données du développeur : ils sont exclus par l'installateur, "
                + "mais NE les publie jamais à la main (ni en pièce jointe de Release)."
                : "\nSans gravité, mais inutile de les livrer : l'installateur les exclut déjà.");
            return sb.ToString();
        }

        /// <summary>Contrôle réel d'un dossier de publication.</summary>
        public static string Check(string dir, out int grave)
        {
            grave = 0; int minor;
            var names = new List<string>();
            try
            {
                if (!Directory.Exists(dir)) { return "Dossier introuvable : " + dir; }
                foreach (var f in Directory.GetFiles(dir)) names.Add(Path.GetFileName(f));
                foreach (var d in Directory.GetDirectories(dir)) names.Add(Path.GetFileName(d));
            }
            catch (Exception ex) { return "Lecture impossible : " + ex.Message; }
            return Verdict(names, out grave, out minor);
        }
    }
}
