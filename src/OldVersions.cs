using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// « ONYX NE LAISSE PAS SES VIEILLES PEAUX DERRIÈRE LUI » — la mise à jour intégrée télécharge
    /// l'installateur officiel dans le dossier temporaire, puis le lance. Sans ménage, CHAQUE mise à
    /// jour abandonne un installateur de ~45 Mo qui ne resservira jamais : au bout d'un an, c'est un
    /// demi-giga de déchets sur le disque de l'utilisateur — le comble pour un outil qui traque le
    /// poids mort ailleurs.
    ///
    /// Règle : un installateur dont la version est INFÉRIEURE OU ÉGALE à celle qui tourne a
    /// forcément déjà été installé, donc il ne sert plus. Une version SUPÉRIEURE est au contraire
    /// une mise à jour téléchargée mais pas encore posée : on n'y touche pas. La décision est une
    /// fonction PURE (testable sans disque) ; seul Sweep() écrit.
    /// </summary>
    internal static class OldVersions
    {
        /// <summary>Motifs d'installateurs produits par le projet (l'app s'est appelée BTOptimizer
        /// avant de devenir ONYX : ses vieux installateurs traînent encore chez les utilisateurs).</summary>
        private static readonly string[] Patterns = { "ONYX-Setup*.exe", "BTOptimizer-Setup*.exe" };

        /// <summary>Un installateur sans numéro de version lisible n'est supprimé qu'au-delà de cet âge.</summary>
        public const int UnknownVersionMaxAgeDays = 14;

        public sealed class Candidate
        {
            public string Path;
            public string Name;
            public long Bytes;
            public Version Ver;          // null si le nom ne porte pas de version lisible
            public DateTime LastWrite;
        }

        /// <summary>
        /// Décision PURE : lesquels de ces installateurs sont périmés ?
        /// — version connue et &lt;= version courante  → périmé (déjà installé) ;
        /// — version connue et &gt; version courante   → GARDÉ (mise à jour en attente) ;
        /// — version illisible                        → périmé seulement s'il est vieux.
        /// Une version courante inconnue fait tout garder : sans référence, on ne supprime rien.
        /// </summary>
        public static List<Candidate> Obsolete(List<Candidate> found, Version current, DateTime now)
        {
            var outp = new List<Candidate>();
            if (found == null || current == null) return outp;
            foreach (var c in found)
            {
                if (c == null || string.IsNullOrEmpty(c.Path)) continue;
                if (c.Ver == null)
                {
                    if ((now - c.LastWrite).TotalDays > UnknownVersionMaxAgeDays) outp.Add(c);
                    continue;
                }
                // Comparaison sur Majeur.Mineur, comme le reste de l'app (on publie en X.YY).
                bool plusRecent = c.Ver.Major != current.Major
                    ? c.Ver.Major > current.Major
                    : c.Ver.Minor > current.Minor;
                if (!plusRecent) outp.Add(c);
            }
            return outp;
        }

        /// <summary>Installateurs présents dans un dossier donné (dossier temporaire par défaut).</summary>
        public static List<Candidate> Find(string dir)
        {
            var list = new List<Candidate>();
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return list;
                foreach (var pattern in Patterns)
                {
                    string[] files;
                    try { files = Directory.GetFiles(dir, pattern); }
                    catch { continue; }
                    foreach (var f in files)
                    {
                        try
                        {
                            var fi = new FileInfo(f);
                            list.Add(new Candidate
                            {
                                Path = fi.FullName,
                                Name = fi.Name,
                                Bytes = fi.Length,
                                Ver = Updater.ParseTag(fi.Name),
                                LastWrite = fi.LastWriteTime
                            });
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return list;
        }

        public static long Total(List<Candidate> list)
        {
            long t = 0;
            if (list != null) foreach (var c in list) t += c.Bytes;
            return t;
        }

        /// <summary>
        /// Ménage silencieux, à faire au lancement : supprime les installateurs devenus inutiles.
        /// Renvoie les octets libérés. N'échoue JAMAIS bruyamment — un fichier verrouillé (mise à
        /// jour en cours) est simplement laissé en place et retenté au prochain démarrage.
        /// </summary>
        public static long Sweep(Action<string, int> log)
        {
            long freed = 0;
            try
            {
                var found = Find(Path.GetTempPath());
                if (found.Count == 0) return 0;
                var dead = Obsolete(found, Updater.CurrentVersion(), DateTime.Now);
                foreach (var c in dead)
                {
                    try
                    {
                        File.Delete(c.Path);
                        freed += c.Bytes;
                    }
                    catch { }   // verrouillé ou déjà parti : on réessaiera au prochain lancement
                }
                if (freed > 0 && log != null)
                    log("Ménage : " + dead.Count + " ancien(s) installateur(s) supprimé(s) ("
                        + SteamGames.Human(freed) + " libérés).", 0);
            }
            catch { }
            return freed;
        }
    }
}
