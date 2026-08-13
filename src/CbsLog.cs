using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// POURQUOI SFC A ÉCHOUÉ — la question à laquelle personne ne répond.
    ///
    /// « Windows Resource Protection a trouvé des fichiers endommagés mais n'a pas pu en réparer
    /// certains » : le message s'arrête là, et l'utilisateur relance SFC en boucle sans rien
    /// apprendre. La réponse est pourtant écrite noir sur blanc dans C:\Windows\Logs\CBS\CBS.log —
    /// un fichier de plusieurs dizaines de mégaoctets, en anglais, illisible sans méthode.
    ///
    /// On lit la FIN du fichier (les dernières interventions), on ne garde que les lignes [SR]
    /// (celles de SFC) et les codes d'erreur connus, et on traduit en français ce que ça implique.
    /// LECTURE SEULE : ce module ne répare rien et ne modifie rien.
    /// </summary>
    internal static class CbsLog
    {
        public static string CheminDefaut
        {
            get
            {
                try { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Logs\CBS\CBS.log"); }
                catch { return @"C:\Windows\Logs\CBS\CBS.log"; }
            }
        }

        /// <summary>Un motif reconnu dans le journal, et ce qu'il signifie vraiment.</summary>
        public sealed class Constat
        {
            public string Motif;        // ce qui a été repéré
            public int Occurrences;
            public string Explication;  // ce que ça veut dire, en français
            public int Gravite;         // 0 = information, 1 = attention, 2 = problème
        }

        // Motifs classés du plus grave au plus anodin. La recherche est insensible à la casse.
        private sealed class Regle
        {
            public string Cle; public string Motif; public string Explication; public int Gravite;
        }

        private static readonly Regle[] Regles =
        {
            new Regle { Cle = "cannot repair member file", Motif = "Fichier système non réparable",
                Explication = "SFC a trouvé des fichiers abîmés qu'il n'a PAS pu remplacer : le magasin de composants "
                            + "ne contient pas de copie saine. C'est le cas typique où il faut réparer l'image avec "
                            + "DISM /RestoreHealth AVANT de relancer SFC.", Gravite = 2 },

            new Regle { Cle = "0x80073712", Motif = "Magasin de composants endommagé (0x80073712)",
                Explication = "Le magasin de composants (WinSxS) est lui-même abîmé : un fichier de manifeste manque. "
                            + "SFC ne peut rien faire tant que DISM n'a pas restauré la référence.", Gravite = 2 },

            new Regle { Cle = "0x800f081f", Motif = "Source de réparation introuvable (0x800f081f)",
                Explication = "DISM n'a pas trouvé les fichiers de remplacement : pas d'accès à Windows Update, ou "
                            + "source hors ligne absente. Vérifie la connexion, ou fournis une image ISO de la MÊME "
                            + "version de Windows.", Gravite = 2 },

            new Regle { Cle = "0x800f0906", Motif = "Téléchargement des sources impossible (0x800f0906)",
                Explication = "DISM n'a pas pu télécharger les fichiers sources (réseau, proxy, ou stratégie "
                            + "d'entreprise qui redirige Windows Update).", Gravite = 2 },

            new Regle { Cle = "do not match the actual file", Motif = "Empreinte de fichier incohérente",
                Explication = "L'empreinte d'un fichier système ne correspond pas à celle attendue. Souvent un "
                            + "fichier remplacé par un pilote, un antivirus ou un « optimiseur » tiers.", Gravite = 1 },

            new Regle { Cle = "repairing corrupted file", Motif = "Fichier réparé avec succès",
                Explication = "SFC a bien remplacé des fichiers abîmés. Si le problème persiste après un "
                            + "redémarrage, relance une passe : certaines réparations en découvrent d'autres.", Gravite = 0 },

            new Regle { Cle = "verify complete", Motif = "Vérification terminée",
                Explication = "Une passe de vérification est allée à son terme.", Gravite = 0 }
        };

        /// <summary>
        /// Analyse PURE d'un contenu de journal (testable sans Windows). Les constats sont rendus
        /// du plus grave au plus anodin, puis par nombre d'occurrences.
        /// </summary>
        public static List<Constat> Analyse(string contenu)
        {
            var sortie = new List<Constat>();
            if (string.IsNullOrEmpty(contenu)) return sortie;
            string bas = contenu.ToLowerInvariant();

            foreach (Regle r in Regles)
            {
                int n = 0, i = 0;
                while ((i = bas.IndexOf(r.Cle, i, StringComparison.Ordinal)) >= 0) { n++; i += r.Cle.Length; }
                if (n > 0)
                    sortie.Add(new Constat { Motif = r.Motif, Occurrences = n, Explication = r.Explication, Gravite = r.Gravite });
            }

            sortie.Sort(delegate (Constat a, Constat b)
            {
                if (a.Gravite != b.Gravite) return b.Gravite.CompareTo(a.Gravite);
                return b.Occurrences.CompareTo(a.Occurrences);
            });
            return sortie;
        }

        /// <summary>Noms des fichiers que SFC n'a PAS pu réparer (extraits des lignes [SR]).</summary>
        public static List<string> FichiersNonReparables(string contenu, int max)
        {
            var noms = new List<string>();
            if (string.IsNullOrEmpty(contenu)) return noms;
            var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Forme réelle : Cannot repair member file [l:24]'msvcp_win.dll' of Microsoft-Windows-...
            var rx = new System.Text.RegularExpressions.Regex(
                @"[Cc]annot repair member file \[l:\d+\]'([^']+)'");
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(contenu))
            {
                string f = m.Groups[1].Value;
                if (f.Length == 0 || !vus.Add(f)) continue;
                noms.Add(f);
                if (noms.Count >= max) break;
            }
            return noms;
        }

        /// <summary>
        /// Dernier tronçon d'un fichier volumineux (CBS.log dépasse souvent 50 Mo). On lit la FIN :
        /// c'est là que se trouvent les interventions récentes. Chaîne vide si illisible.
        /// </summary>
        public static string Fin(string chemin, int maxOctets)
        {
            try
            {
                if (string.IsNullOrEmpty(chemin) || !File.Exists(chemin)) return "";
                // FileShare.ReadWrite : le service TrustedInstaller garde souvent le fichier ouvert.
                using (var fs = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long debut = Math.Max(0, fs.Length - maxOctets);
                    fs.Seek(debut, SeekOrigin.Begin);
                    using (var sr = new StreamReader(fs))
                    {
                        if (debut > 0) sr.ReadLine();   // la 1re ligne est tronquée : on la jette
                        return sr.ReadToEnd();
                    }
                }
            }
            catch { return ""; }
        }

        /// <summary>Analyse du journal réel de la machine (2 Mo de fin par défaut).</summary>
        public static List<Constat> AnalyseMachine() { return Analyse(Fin(CheminDefaut, 2 * 1024 * 1024)); }

        /// <summary>Y a-t-il au moins un constat GRAVE (fichiers non réparés, magasin abîmé) ?</summary>
        public static bool Grave(List<Constat> constats)
        {
            if (constats == null) return false;
            foreach (var c in constats) if (c != null && c.Gravite >= 2) return true;
            return false;
        }

        /// <summary>Mise en forme PURE, prête à afficher.</summary>
        public static string Texte(List<Constat> constats, List<string> fichiers)
        {
            if (constats == null || constats.Count == 0)
                return "Rien d'exploitable dans le journal CBS : aucune trace récente de réparation "
                     + "(ou le journal a été vidé).";
            var sb = new System.Text.StringBuilder();
            sb.Append("Ce que dit le journal de réparation de Windows (CBS) :\n\n");
            foreach (var c in constats)
            {
                sb.Append(c.Gravite == 2 ? "✗ " : (c.Gravite == 1 ? "! " : "✓ "));
                sb.Append(c.Motif).Append("  (").Append(c.Occurrences).Append("×)\n   ")
                  .Append(c.Explication).Append('\n');
            }
            if (fichiers != null && fichiers.Count > 0)
            {
                sb.Append("\nFichiers que SFC n'a pas pu réparer :\n");
                foreach (var f in fichiers) sb.Append("   • ").Append(f).Append('\n');
            }
            return sb.ToString();
        }
    }
}
