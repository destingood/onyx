using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// Fiche et désinstallation d'une appli du panneau « Bibliothèques &amp; applis de jeu » (pur).
    /// La fiche vient de « winget show » (version, éditeur, description, licence) : ONYX n'héberge
    /// aucun catalogue. La désinstallation passe par « winget uninstall », après confirmation.
    /// </summary>
    internal static class FicheAppli
    {
        private static readonly Regex Id = new Regex(@"^[A-Za-z0-9][A-Za-z0-9.+\-]{2,99}$", RegexOptions.CultureInvariant);
        private static readonly Regex Date = new Regex(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant);

        /// <summary>Identifiant winget acceptable sur une ligne de commande (lettres, chiffres, point, +, -).</summary>
        public static bool IdValide(string id) { return id != null && Id.IsMatch(id); }

        public static string ArgsFiche(string id)
        {
            if (!IdValide(id)) throw new ArgumentException("Identifiant winget invalide.");
            return "show --id " + id + " -e --source winget --accept-source-agreements --disable-interactivity";
        }

        public static string ArgsDesinstalle(string id)
        {
            if (!IdValide(id)) throw new ArgumentException("Identifiant winget invalide.");
            return "uninstall --id " + id + " -e --silent --accept-source-agreements --disable-interactivity";
        }

        /// <summary>
        /// Une bibliothèque essentielle (Visual C++, DirectX, .NET, OpenAL) ne se désinstalle pas
        /// d'ici : des jeux déjà installés en dépendent et ne se lanceraient plus.
        /// </summary>
        public static bool PeutDesinstaller(bool essentielle, bool installee) { return installee && !essentielle; }

        /// <summary>Pourquoi le bouton « Désinstaller » ne fait rien pour cet élément ; null s'il peut agir.</summary>
        public static string Refus(bool essentielle, bool installee)
        {
            if (!installee) return "Cet élément n'est pas installé.";
            if (essentielle) return "Bibliothèque essentielle : des jeux en dépendent, ONYX ne la désinstalle pas.";
            return null;
        }

        /// <summary>
        /// Sortie de « winget show » rendue lisible : à partir de la ligne du paquet, sans les lignes
        /// d'attente, sans la liste de mots-clés ni les détails techniques de l'installeur (adresse,
        /// empreinte) — la date de publication est gardée.
        /// </summary>
        public static string Nettoie(string sortie, string id)
        {
            if (string.IsNullOrEmpty(sortie)) return "";
            var lignes = new List<string>();
            foreach (string brute in sortie.Replace("\r", "").Split('\n'))
            {
                string l = brute.Replace("\b", "").TrimEnd();
                if (l.Trim().Trim('-', '\\', '|', '/', ' ').Length == 0) continue;   // vide ou roue d'attente
                if (l.IndexOf('█') >= 0 || l.IndexOf('▒') >= 0) continue;           // barre de progression
                lignes.Add(l);
            }
            int debut = 0;
            if (id != null)
                for (int i = 0; i < lignes.Count; i++)
                    if (lignes[i].IndexOf("[" + id + "]", StringComparison.OrdinalIgnoreCase) >= 0) { debut = i; break; }

            var res = new List<string>();
            int bloc = 0;   // 0 = gardé · 1 = mots-clés (jeté) · 2 = installeur (seule la date est gardée)
            for (int i = debut; i < lignes.Count; i++)
            {
                string l = lignes[i];
                bool retrait = l.Length > 0 && l[0] == ' ';
                if (!retrait)
                {
                    string cle = l.TrimEnd(':').Trim().ToLowerInvariant();
                    bloc = (cle == "tags" || cle == "balises" || cle == "mots clés" || cle == "mots-clés") ? 1
                         : (cle == "installer" || cle == "installateur" || cle == "programme d'installation") ? 2 : 0;
                    if (bloc != 0) continue;
                }
                if (bloc == 1) continue;
                if (bloc == 2) { if (Date.IsMatch(l)) res.Add(l.Trim()); continue; }
                res.Add(l);
            }
            return string.Join("\r\n", res.ToArray());
        }
    }
}
