using System;
using System.Collections.Generic;

namespace BTOptimizer
{
    /// <summary>
    /// PLANS D'ALIMENTATION EN DOUBLE — les traces que laissent les optimiseurs successifs.
    ///
    /// Beaucoup de scripts « boost » font `powercfg -duplicatescheme` pour activer le plan
    /// Performances optimales. Chaque exécution en crée un NOUVEAU, avec le même nom et un GUID
    /// différent. Au bout de quelques outils et de quelques réinstallations, on se retrouve avec
    /// cinq ou six « Performances optimales » empilés (constaté sur une machine réelle).
    ///
    /// Ce n'est pas un problème de performances : c'est de l'encombrement, et surtout une source de
    /// confusion — impossible de savoir lequel on modifie quand on règle un paramètre fin.
    ///
    /// Prudence absolue : on ne supprime JAMAIS le plan actif, ni les plans intégrés de Windows.
    /// Un plan au nom unique (Atlas, Bitsum…) n'est pas un doublon et n'est jamais touché.
    /// </summary>
    internal static class PowerPlans
    {
        public sealed class Plan
        {
            public string Guid;
            public string Nom;
            public bool Actif;
        }

        /// <summary>Plans intégrés à Windows : jamais proposés à la suppression, même en double.</summary>
        private static readonly string[] Integres =
        {
            "381b4222-f694-41f0-9685-ff5bb260df2e",   // Utilisation normale
            "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",   // Haute performance
            "a1841308-3541-4fab-bc81-f71556f20b4a",   // Économie d'énergie
            "e9a42b02-d5df-448d-aa00-03f14749eb61"    // Performances optimales (modèle caché)
        };

        public static bool EstIntegre(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return false;
            foreach (var g in Integres)
                if (string.Equals(g, guid, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Analyse PURE de la sortie de `powercfg -list` (FR et EN). Le plan actif est marqué d'une
        /// étoile en fin de ligne.
        /// </summary>
        public static List<Plan> Parse(string sortie)
        {
            var list = new List<Plan>();
            if (string.IsNullOrEmpty(sortie)) return list;
            var rx = new System.Text.RegularExpressions.Regex(
                @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\(([^)]*)\)\s*(\*?)");
            foreach (var raw in sortie.Replace("\r\n", "\n").Split('\n'))
            {
                var m = rx.Match(raw ?? "");
                if (!m.Success) continue;
                list.Add(new Plan
                {
                    Guid = m.Groups[1].Value.ToLowerInvariant(),
                    Nom = m.Groups[2].Value.Trim(),
                    Actif = m.Groups[3].Value == "*"
                });
            }
            return list;
        }

        /// <summary>
        /// Décision PURE : quels plans sont des doublons supprimables ?
        /// On groupe par NOM ; dans chaque groupe de plus d'un, on GARDE le plan actif s'il y est,
        /// sinon un plan intégré, sinon le premier. Tout intégré et le plan actif sont exclus.
        /// </summary>
        public static List<Plan> Doublons(List<Plan> plans)
        {
            var sortie = new List<Plan>();
            if (plans == null) return sortie;

            var groupes = new Dictionary<string, List<Plan>>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in plans)
            {
                if (p == null || string.IsNullOrEmpty(p.Nom)) continue;
                List<Plan> g;
                if (!groupes.TryGetValue(p.Nom, out g)) { g = new List<Plan>(); groupes[p.Nom] = g; }
                g.Add(p);
            }

            foreach (var kv in groupes)
            {
                List<Plan> g = kv.Value;
                if (g.Count < 2) continue;               // nom unique : ce n'est pas un doublon

                Plan garde = null;
                foreach (var p in g) if (p.Actif) { garde = p; break; }
                if (garde == null) foreach (var p in g) if (EstIntegre(p.Guid)) { garde = p; break; }
                if (garde == null) garde = g[0];

                foreach (var p in g)
                {
                    if (ReferenceEquals(p, garde)) continue;
                    if (p.Actif || EstIntegre(p.Guid)) continue;   // double sécurité
                    sortie.Add(p);
                }
            }
            return sortie;
        }

        /// <summary>Plans réellement présents sur la machine.</summary>
        public static List<Plan> Lister()
        {
            try
            {
                NativeResult r = Sys.Run(Sys.Sys32("powercfg.exe"), "-list");
                return Parse(r.Output);
            }
            catch { return new List<Plan>(); }
        }

        /// <summary>Supprime les doublons fournis. Renvoie le nombre réellement supprimé.</summary>
        public static int Supprimer(List<Plan> doublons, Action<string, int> log)
        {
            int n = 0;
            if (doublons == null) return 0;
            foreach (var p in doublons)
            {
                if (p == null || p.Actif || EstIntegre(p.Guid)) continue;   // triple sécurité
                try
                {
                    NativeResult r = Sys.Run(Sys.Sys32("powercfg.exe"), "-delete " + p.Guid);
                    if (r.ExitCode == 0)
                    {
                        n++;
                        if (log != null) log("Plan d'alimentation en double supprimé : " + p.Nom + " (" + p.Guid + ").", 1);
                    }
                    else if (log != null)
                        log("Suppression refusée pour " + p.Nom + " (" + p.Guid + ") — code " + r.ExitCode + ".", 2);
                }
                catch (Exception ex)
                {
                    if (log != null) log("Suppression impossible (" + ex.Message + ").", 3);
                }
            }
            return n;
        }
    }
}
