using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// REGISTRE DES PROBLÈMES RENCONTRÉS — ce que l'utilisateur constate, et qui se perd sinon.
    ///
    /// Un souci arrive en pleine partie : on se dit qu'on le notera « plus tard », et il est oublié.
    /// Quand il revient trois semaines après, plus personne ne sait quand il a commencé, ni ce qui
    /// avait changé ce jour-là. Ce registre garde la trace, avec le contexte qui compte : la version
    /// d'ONYX du jour, la date, et l'état (à régler / réglé).
    ///
    /// Format ligne : date | version | etat | description
    /// La description est nettoyée de ses séparateurs et de ses retours à la ligne pour que le
    /// fichier reste lisible et relisible — une seule entrée par ligne, toujours.
    ///
    /// PRIVÉ PAR DÉFAUT : ce fichier reste sur la machine. Rien n'est envoyé nulle part ; l'export
    /// est une action manuelle, à l'initiative de l'utilisateur.
    /// </summary>
    internal static class ProblemLog
    {
        public enum Etat { ARegler, Regle }

        public sealed class Entree
        {
            public DateTime Date;
            public string Version;
            public Etat Etat;
            public string Description;
        }

        public static string Chemin { get { return AppPaths.File("bt-problemes.txt"); } }

        /// <summary>Une description tient sur UNE ligne : on neutralise séparateurs et sauts.</summary>
        public static string Nettoie(string texte)
        {
            if (string.IsNullOrEmpty(texte)) return "";
            string s = texte.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            s = s.Trim();
            if (s.Length > 500) s = s.Substring(0, 500);   // un registre, pas un roman
            return s;
        }

        /// <summary>Lecture PURE. Les lignes illisibles sont ignorées, jamais devinées.</summary>
        public static List<Entree> Parse(string contenu)
        {
            var list = new List<Entree>();
            if (string.IsNullOrEmpty(contenu)) return list;
            foreach (var raw in contenu.Replace("\r\n", "\n").Split('\n'))
            {
                string ligne = (raw ?? "").Trim();
                if (ligne.Length == 0 || ligne[0] == '#') continue;
                string[] p = ligne.Split(new[] { '|' }, 4);
                if (p.Length < 4) continue;
                DateTime d;
                if (!DateTime.TryParseExact(p[0].Trim(), "yyyy-MM-dd HH:mm",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                string desc = p[3].Trim();
                if (desc.Length == 0) continue;
                list.Add(new Entree
                {
                    Date = d,
                    Version = p[1].Trim(),
                    Etat = string.Equals(p[2].Trim(), "regle", StringComparison.OrdinalIgnoreCase) ? Etat.Regle : Etat.ARegler,
                    Description = desc
                });
            }
            return list;
        }

        /// <summary>Écriture PURE.</summary>
        public static string Render(List<Entree> entrees)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("# ONYX — problèmes rencontrés (date | version | etat | description). Fichier LOCAL, rien n'est envoyé.\n");
            if (entrees != null)
                foreach (var e in entrees)
                {
                    if (e == null || string.IsNullOrEmpty(e.Description)) continue;
                    sb.Append(e.Date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(" | ")
                      .Append(string.IsNullOrEmpty(e.Version) ? "?" : e.Version).Append(" | ")
                      .Append(e.Etat == Etat.Regle ? "regle" : "a-regler").Append(" | ")
                      .Append(Nettoie(e.Description)).Append('\n');
                }
            return sb.ToString();
        }

        public static List<Entree> Charger()
        {
            try { return Parse(File.Exists(Chemin) ? File.ReadAllText(Chemin) : null); }
            catch { return new List<Entree>(); }
        }

        private static bool Enregistrer(List<Entree> entrees)
        {
            try { File.WriteAllText(Chemin, Render(entrees)); return true; }
            catch { return false; }
        }

        /// <summary>Ajoute un problème. false si la description est vide ou l'écriture impossible.</summary>
        public static bool Ajouter(string description, Action<string, int> log)
        {
            string d = Nettoie(description);
            if (d.Length == 0) return false;
            var list = Charger();
            // Le plus récent en tête : c'est celui qu'on vient de vivre qu'on veut relire.
            list.Insert(0, new Entree
            {
                Date = DateTime.Now,
                Version = VersionCourante(),
                Etat = Etat.ARegler,
                Description = d
            });
            bool ok = Enregistrer(list);
            if (ok && log != null) log("Problème noté dans le registre : " + d, 1);
            return ok;
        }

        /// <summary>Bascule l'état d'une entrée (index dans la liste chargée).</summary>
        public static bool Basculer(int index)
        {
            var list = Charger();
            if (index < 0 || index >= list.Count) return false;
            list[index].Etat = list[index].Etat == Etat.Regle ? Etat.ARegler : Etat.Regle;
            return Enregistrer(list);
        }

        public static bool Supprimer(int index)
        {
            var list = Charger();
            if (index < 0 || index >= list.Count) return false;
            list.RemoveAt(index);
            return Enregistrer(list);
        }

        public static int Ouverts(List<Entree> entrees)
        {
            int n = 0;
            if (entrees != null) foreach (var e in entrees) if (e != null && e.Etat == Etat.ARegler) n++;
            return n;
        }

        private static string VersionCourante()
        {
            try
            {
                Version v = Updater.CurrentVersion();
                return v == null ? "?" : v.Major + "." + v.Minor.ToString("00");
            }
            catch { return "?"; }
        }

        /// <summary>Texte prêt à copier (rapport de bug, message au support). PUR.</summary>
        public static string Export(List<Entree> entrees)
        {
            if (entrees == null || entrees.Count == 0) return "Aucun problème enregistré.";
            var sb = new System.Text.StringBuilder();
            sb.Append("PROBLÈMES RENCONTRÉS — ONYX\n");
            sb.Append(Ouverts(entrees)).Append(" à régler sur ").Append(entrees.Count).Append(" au total.\n\n");
            foreach (var e in entrees)
                sb.Append(e.Etat == Etat.Regle ? "[réglé]    " : "[à régler] ")
                  .Append(e.Date.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))
                  .Append("  (v").Append(e.Version).Append(")  ")
                  .Append(e.Description).Append('\n');
            return sb.ToString();
        }
    }
}
