using System;
using System.Collections.Generic;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// LISTE DU MODE JEU dans un fichier (logique PURE) : ce que l'utilisateur a exclu, pour la
    /// garder de côté ou la reprendre sur un autre PC. Le fichier ne contient QUE des exclusions :
    /// un fichier reçu peut faire laisser tranquille un service, jamais en faire arrêter un nouveau.
    /// Les noms inconnus du catalogue sont ignorés et comptés.
    /// </summary>
    internal static class ListeModeJeu
    {
        public const string Entete = "# ONYX — liste du Mode Jeu (ce qui reste intouché pendant la partie)";
        private const string SecServices = "[services-exclus]";
        private const string SecApplis = "[applis-exclues]";

        public sealed class Contenu
        {
            public List<string> Services = new List<string>();
            public List<string> Applis = new List<string>();
            /// <summary>Lignes ignorées : nom absent du catalogue de cette version.</summary>
            public int Ignores;
        }

        public static string Ecrit(IEnumerable<string> servicesExclus, IEnumerable<string> applisExclues)
        {
            var sb = new StringBuilder();
            sb.Append(Entete).Append('\n');
            sb.Append(SecServices).Append('\n');
            foreach (string s in Tries(servicesExclus)) sb.Append(s).Append('\n');
            sb.Append(SecApplis).Append('\n');
            foreach (string s in Tries(applisExclues)) sb.Append(s).Append('\n');
            return sb.ToString();
        }

        private static List<string> Tries(IEnumerable<string> l)
        {
            var r = new List<string>();
            if (l != null) foreach (string s in l) if (!string.IsNullOrWhiteSpace(s) && !r.Contains(s.Trim())) r.Add(s.Trim());
            r.Sort(StringComparer.OrdinalIgnoreCase);
            return r;
        }

        /// <summary>
        /// Lit un fichier. Rend null s'il ne vient pas d'ONYX (pas d'en-tête ni de section) ;
        /// sinon les exclusions reconnues, écrites avec la casse du catalogue.
        /// </summary>
        public static Contenu Lit(string texte, ICollection<string> servicesConnus, ICollection<string> applisConnues)
        {
            if (string.IsNullOrEmpty(texte)) return null;
            if (texte.Length > 200000) return null;
            var c = new Contenu();
            List<string> cible = null;
            ICollection<string> connus = null;
            bool reconnu = false;
            foreach (string brut in texte.Split('\n'))
            {
                string l = brut.Trim().TrimStart('﻿');
                if (l.Length == 0 || l.StartsWith("#", StringComparison.Ordinal)) continue;
                if (l.Equals(SecServices, StringComparison.OrdinalIgnoreCase)) { cible = c.Services; connus = servicesConnus; reconnu = true; continue; }
                if (l.Equals(SecApplis, StringComparison.OrdinalIgnoreCase)) { cible = c.Applis; connus = applisConnues; reconnu = true; continue; }
                if (cible == null) return null;   // texte avant toute section : pas un fichier ONYX
                string nom = Canonique(l, connus);
                if (nom == null) { c.Ignores++; continue; }
                if (!cible.Contains(nom)) cible.Add(nom);
            }
            return reconnu ? c : null;
        }

        private static string Canonique(string nom, ICollection<string> connus)
        {
            if (connus == null) return null;
            foreach (string k in connus) if (string.Equals(k, nom, StringComparison.OrdinalIgnoreCase)) return k;
            return null;
        }

        public static string Bilan(Contenu c)
        {
            if (c == null) return "Ce fichier n'est pas une liste du Mode Jeu d'ONYX.";
            string s = c.Services.Count + " service(s) et " + c.Applis.Count + " famille(s) d'applications exclus.";
            if (c.Ignores > 0) s += " " + c.Ignores + " nom(s) inconnu(s) de cette version ignoré(s).";
            return s;
        }
    }
}
