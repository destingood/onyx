using System;
using System.Collections.Generic;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// CATÉGORIES DE NOTIFICATIONS (logique PURE) : chaque notification d'ONYX appartient à une
    /// catégorie que l'utilisateur peut couper. Le fichier bt-notifications.txt ne liste que les
    /// catégories coupées, une par ligne : absent ou vide, tout est actif. Une ligne inconnue est
    /// ignorée (fichier d'une version plus récente).
    /// </summary>
    internal static class NotifCategories
    {
        public const string Gardien = "gardien", MiseAJour = "maj", JeuxSuivis = "suivis", Nettoyage = "nettoyage";

        public static readonly string[] Toutes = { Gardien, MiseAJour, JeuxSuivis, Nettoyage };

        public static string Libelle(string id)
        {
            switch (id)
            {
                case Gardien: return "Alertes du Gardien (santé du PC)";
                case MiseAJour: return "Nouvelle version d'ONYX";
                case JeuxSuivis: return "Jeux suivis : promos, gratuits, sorties";
                case Nettoyage: return "Rappel de nettoyage (poids mort)";
                default: return id ?? "";
            }
        }

        public static string Detail(string id)
        {
            switch (id)
            {
                case Gardien: return "La pastille de la zone de notification reste. Un jeu qui vient de planter est toujours signalé.";
                case MiseAJour: return "Le bandeau dans ONYX reste jusqu'à l'installation.";
                case JeuxSuivis: return "La pastille de Bons plans reste.";
                case Nettoyage: return "Coupé, le contrôle ne tourne plus du tout.";
                default: return "";
            }
        }

        /// <summary>Catégories coupées relues du fichier.</summary>
        public static HashSet<string> Lire(string texte)
        {
            var res = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(texte)) return res;
            foreach (string brute in texte.Split('\n'))
            {
                string l = brute.Trim();
                if (Array.IndexOf(Toutes, l) >= 0) res.Add(l);
            }
            return res;
        }

        /// <summary>Texte du fichier, dans l'ordre de <see cref="Toutes"/>.</summary>
        public static string Ecrire(ICollection<string> coupees)
        {
            var sb = new StringBuilder();
            if (coupees == null) return "";
            foreach (string id in Toutes) if (coupees.Contains(id)) sb.Append(id).Append('\n');
            return sb.ToString();
        }

        public static bool Active(ICollection<string> coupees, string id)
        {
            return coupees == null || id == null || !coupees.Contains(id);
        }
    }
}
