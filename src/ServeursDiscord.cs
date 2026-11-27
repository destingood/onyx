using System;
using System.Collections.Generic;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// SERVEURS DISCORD (logique PURE) : liste des serveurs de jeu de l'utilisateur, sous forme de
    /// liens d'invitation qu'il colle lui-même (discord.gg/…, discord.com/invite/…). Aucun compte,
    /// aucun appel réseau : ONYX garde le code d'invitation et ouvre https://discord.gg/code.
    /// Fichier : une ligne « code TAB nom ».
    /// </summary>
    internal static class ServeursDiscord
    {
        public const int Max = 50, NomMax = 60;

        public sealed class Serveur
        {
            public string Nom, Code;
            public string Lien { get { return ServeursDiscord.Lien(Code); } }
        }

        public static string Lien(string code) { return "https://discord.gg/" + code; }

        private static readonly string[] Prefixes =
        {
            "discord.gg/", "discord.com/invite/", "discordapp.com/invite/",
            "ptb.discord.com/invite/", "canary.discord.com/invite/",
        };

        /// <summary>
        /// Code d'invitation tiré d'un lien collé (ou d'un code seul) ; null si ce n'est pas une
        /// invitation Discord. Tout autre domaine est refusé, y compris « x.com/discord.gg/… ».
        /// </summary>
        public static string Code(string saisie)
        {
            if (string.IsNullOrWhiteSpace(saisie)) return null;
            string s = saisie.Trim();
            if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) s = s.Substring(8);
            else if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7);
            if (s.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4);
            bool lien = false;
            foreach (string p in Prefixes)
                if (s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { s = s.Substring(p.Length); lien = true; break; }
            if (lien)
            {
                int fin = s.IndexOfAny(new[] { '?', '#', '/' });
                if (fin >= 0) s = s.Substring(0, fin);
            }
            return CodeValide(s) ? s : null;
        }

        private static bool CodeValide(string c)
        {
            if (c == null || c.Length < 2 || c.Length > 32) return false;
            foreach (char ch in c)
                if (!(ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z' || ch >= '0' && ch <= '9' || ch == '-')) return false;
            return true;
        }

        /// <summary>Nom sur une ligne, sans tabulation, borné ; à défaut, le code.</summary>
        public static string NomPropre(string nom, string code)
        {
            var sb = new StringBuilder();
            foreach (char ch in nom ?? "") sb.Append(char.IsControl(ch) ? ' ' : ch);
            string n = sb.ToString().Trim();
            while (n.Contains("  ")) n = n.Replace("  ", " ");
            if (n.Length > NomMax) n = n.Substring(0, NomMax).TrimEnd();
            return n.Length > 0 ? n : code;
        }

        /// <summary>
        /// Ajoute un serveur. Faux, avec la raison, si le lien n'est pas une invitation, s'il est
        /// déjà dans la liste (les codes Discord respectent la casse) ou si la liste est pleine.
        /// </summary>
        public static bool Ajoute(List<Serveur> liste, string nom, string saisie, out string erreur)
        {
            erreur = null;
            string code = Code(saisie);
            if (code == null) { erreur = "Ce n'est pas un lien d'invitation Discord (exemple : discord.gg/abc123)."; return false; }
            foreach (Serveur s in liste)
                if (string.Equals(s.Code, code, StringComparison.Ordinal)) { erreur = "Ce serveur est déjà dans la liste (« " + s.Nom + " »)."; return false; }
            if (liste.Count >= Max) { erreur = "Liste pleine (" + Max + " serveurs) : retire un serveur d'abord."; return false; }
            liste.Add(new Serveur { Code = code, Nom = NomPropre(nom, code) });
            return true;
        }

        public static List<Serveur> Lire(string texte)
        {
            var l = new List<Serveur>();
            if (string.IsNullOrEmpty(texte)) return l;
            foreach (string brute in texte.Split('\n'))
            {
                string ligne = brute.TrimEnd('\r');
                int tab = ligne.IndexOf('\t');
                string code = tab >= 0 ? ligne.Substring(0, tab) : ligne;
                string err;
                Ajoute(l, tab >= 0 ? ligne.Substring(tab + 1) : "", code, out err);
            }
            return l;
        }

        public static string Ecrire(IEnumerable<Serveur> liste)
        {
            var sb = new StringBuilder();
            if (liste != null)
                foreach (Serveur s in liste)
                    if (s != null && CodeValide(s.Code)) sb.Append(s.Code).Append('\t').Append(NomPropre(s.Nom, s.Code)).Append("\r\n");
            return sb.ToString();
        }
    }
}
