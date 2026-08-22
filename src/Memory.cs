using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Mémoire LONGUE DURÉE du Copilote (bt-memoire.txt) : des faits sur l'utilisateur et sa
    /// machine, conservés d'une session à l'autre et injectés dans le contexte de l'IA → des
    /// réponses de plus en plus précises et personnalisées. 100 % local (simple fichier texte).
    /// L'utilisateur reste maître : « retiens que… », « que sais-tu sur moi », « oublie tout ».
    /// </summary>
    internal static class Memory
    {
        private const int MaxFacts = 60;

        private static string Path_
        {
            get { return AppPaths.File("bt-memoire.txt"); }
        }

        /// <summary>Faits mémorisés (lignes non vides, hors commentaires), du plus ancien au récent.</summary>
        public static List<string> All()
        {
            var list = new List<string>();
            try
            {
                if (!File.Exists(Path_)) return list;
                foreach (string line in File.ReadAllLines(Path_))
                {
                    string l = line.Trim();
                    if (l.Length == 0 || l.StartsWith("#", StringComparison.Ordinal)) continue;
                    list.Add(l);
                }
            }
            catch { }
            return list;
        }

        /// <summary>Ajoute un fait (ignore les vides et les doublons ; plafonné, on oublie le plus vieux).</summary>
        public static void Add(string fact)
        {
            if (string.IsNullOrWhiteSpace(fact)) return;
            fact = fact.Trim();
            if (fact.Length > 200) fact = fact.Substring(0, 200);
            try
            {
                var all = All();
                foreach (string f in all) if (string.Equals(f, fact, StringComparison.OrdinalIgnoreCase)) return;
                all.Add(fact);
                while (all.Count > MaxFacts) all.RemoveAt(0);
                Save(all);
            }
            catch { }
        }

        public static void Clear() { try { if (File.Exists(Path_)) File.Delete(Path_); } catch { } }

        private static void Save(List<string> facts)
        {
            var sb = new StringBuilder("# Mémoire du Copilote — faits sur l'utilisateur et sa machine (100 % local).\n");
            foreach (string f in facts) sb.Append(f).Append('\n');
            try { File.WriteAllText(Path_, sb.ToString()); } catch { }
        }

        /// <summary>Bloc prêt à injecter dans le contexte de l'IA (vide si rien de mémorisé).</summary>
        public static string ForPrompt()
        {
            var all = All();
            if (all.Count == 0) return "";
            var sb = new StringBuilder("Ce que tu sais déjà sur l'utilisateur et son PC (mémoire) :\n");
            foreach (string f in all) sb.Append("- ").Append(f).Append('\n');
            return sb.ToString();
        }

        /// <summary>Renseigne UNE FOIS le profil matériel (CPU, RAM, GPU) : l'IA peut alors donner
        /// des conseils adaptés à la config, dès la première conversation.</summary>
        public static void SeedHardwareOnce()
        {
            try
            {
                if (File.Exists(Path_)) { foreach (string f in All()) if (f.StartsWith("PC :", StringComparison.Ordinal)) return; }
                var parts = new List<string>();
                try { var cpu = Sys.QueryCpu(); if (cpu != null && cpu.Name != null && cpu.Name != "-") parts.Add(cpu.Name.Trim()); } catch { }
                try { int ram = LocalBrain.DetectRamMB(); if (ram > 0) parts.Add((ram / 1024) + " Go de RAM"); } catch { }
                try { int vram = LocalBrain.DetectVramMB(); if (vram > 0) parts.Add("carte graphique " + (vram / 1024) + " Go de VRAM"); } catch { }
                if (parts.Count > 0) Add("PC : " + string.Join(", ", parts.ToArray()));
            }
            catch { }
        }
    }
}
