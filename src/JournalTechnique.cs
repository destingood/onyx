using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// CE QUI A ÉCHOUÉ SANS QUE PERSONNE NE LE VOIE.
    ///
    /// Journal.cs enregistre les actions qui RÉUSSISSENT : « qu'est-ce que tu as changé ? ».
    /// Ce module-ci enregistre l'inverse, et le trou était béant — le dépôt compte 1318 blocs
    /// `catch { }` vides répartis sur 185 fichiers.
    ///
    /// CES CATCH VIDES SONT VOULUS, ET C'EST PRÉCISÉMENT LE PROBLÈME :
    ///
    ///   Un module de lecture machine n'a pas le droit de lever — une exception dans un polleur
    ///   de capteurs ne doit pas emporter la fenêtre. La règle est bonne. Mais « ne pas lever »
    ///   a été confondu avec « ne rien dire », et le résultat est un module qui rend une liste
    ///   vide indistinguable d'une machine où il n'y avait rien à trouver.
    ///
    ///   C'est exactement le défaut qu'on vient de corriger chez Windows : le refus de charger
    ///   un pilote était écrit dans un journal que personne ne lisait. Le reproduire dans notre
    ///   propre code serait difficile à défendre.
    ///
    /// CE QUE CE JOURNAL N'EST PAS :
    ///
    ///   Une télémétrie. Rien ne part d'ici. Le fichier reste à côté de l'exécutable, et son
    ///   contenu ne sort que si l'utilisateur clique lui-même sur « copier les infos de support ».
    ///
    ///   Un journal de débogage. On n'y écrit pas le déroulement normal. Une ligne = quelque
    ///   chose qui aurait dû marcher et n'a pas marché. Un journal qui grossit quand tout va
    ///   bien ne se lit jamais.
    ///
    /// LA RÉPÉTITION EST COMPTÉE, PAS RECOPIÉE. Une boucle de mesure qui échoue toutes les
    /// 500 ms produirait des dizaines de milliers de lignes identiques et remplirait le disque
    /// pour ne rien apprendre de plus. Un même échec au même endroit est donc compté, et n'est
    /// réécrit qu'une fois son compteur franchi une puissance de dix.
    /// </summary>
    internal static class JournalTechnique
    {
        /// <summary>Au-delà, le fichier est archivé en .1 et un neuf commence. Deux générations
        /// suffisent : au-delà on garde des mois de bruit qu'aucun humain ne relira.</summary>
        public const long TailleMax = 512 * 1024;

        private static readonly object Gate = new object();

        /// <summary>Compteur par échec distinct, pour ne pas recopier la même ligne mille fois.</summary>
        private static readonly Dictionary<string, long> Repetitions =
            new Dictionary<string, long>(StringComparer.Ordinal);

        private static string Fichier { get { return AppPaths.File("bt-technique.txt"); } }
        private static string Archive { get { return AppPaths.File("bt-technique.1.txt"); } }

        // ==================================================================
        //  Décisions PURES — testables sans fichier ni machine
        // ==================================================================

        /// <summary>
        /// PUR : cette occurrence mérite-t-elle une ligne ?
        ///
        /// Les dix premières, puis les puissances de dix. On garde ainsi la trace d'un échec
        /// rare ET l'ordre de grandeur d'un échec massif, sans jamais écrire proportionnellement
        /// à la casse.
        /// </summary>
        public static bool MeriteUneLigne(long occurrence)
        {
            if (occurrence <= 10) return true;
            for (long p = 100; p > 0 && p <= occurrence; p *= 10)
                if (p == occurrence) return true;
            return false;
        }

        /// <summary>PUR : une ligne de journal tient sur UNE ligne. Les messages d'exception
        /// contiennent des retours chariot qui casseraient la relecture.</summary>
        public static string SurUneLigne(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = s.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            t = t.Trim();
            return t.Length > 300 ? t.Substring(0, 300) : t;
        }

        /// <summary>
        /// PUR : retire d'une ligne ce qui identifie le propriétaire de la machine.
        ///
        /// Indispensable avant tout export. Un message d'exception dit très souvent « Accès au
        /// chemin C:\Users\Prenom\... refusé », et le bloc « infos de support » promet noir sur
        /// blanc qu'il ne contient ni nom d'utilisateur ni chemin privé. Tenir cette promesse se
        /// fait ici, une fois, et pas dans chaque appelant.
        ///
        /// Le profil AVANT le nom : le chemin contient le nom, et remplacer le nom d'abord
        /// laisserait un « C:\Users\&lt;utilisateur&gt; » à moitié nettoyé.
        /// </summary>
        public static string SansDonneesPerso(string ligne, string utilisateur, string profil)
        {
            if (string.IsNullOrEmpty(ligne)) return "";
            string s = ligne;
            if (!string.IsNullOrEmpty(profil))
                s = Remplace(s, profil, "%PROFIL%");
            if (!string.IsNullOrEmpty(utilisateur) && utilisateur.Length >= 3)
                s = Remplace(s, utilisateur, "%UTILISATEUR%");
            return s;
        }

        /// <summary>Version machine : lit le nom et le profil courants.</summary>
        public static string SansDonneesPerso(string ligne)
        {
            string u = "", p = "";
            try { u = Environment.UserName; } catch { }
            try { p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch { }
            return SansDonneesPerso(ligne, u, p);
        }

        /// <summary>Remplacement insensible à la casse : Windows écrit tantôt « C:\Users »,
        /// tantôt « c:\users », et un remplacement sensible à la casse en laisserait passer.</summary>
        private static string Remplace(string source, string cherche, string par)
        {
            if (string.IsNullOrEmpty(cherche)) return source;
            var sb = new System.Text.StringBuilder();
            int i = 0;
            while (true)
            {
                int j = source.IndexOf(cherche, i, StringComparison.OrdinalIgnoreCase);
                if (j < 0) { sb.Append(source, i, source.Length - i); break; }
                sb.Append(source, i, j - i).Append(par);
                i = j + cherche.Length;
            }
            return sb.ToString();
        }

        /// <summary>PUR : la ligne écrite pour un échec.</summary>
        public static string Ligne(DateTime quand, string zone, string type, string message,
                                  long occurrence)
        {
            string s = quand.ToString("dd/MM/yyyy HH:mm:ss") + " | " + SurUneLigne(zone)
                     + " | " + SurUneLigne(type);
            string m = SurUneLigne(message);
            if (m.Length > 0) s += " | " + m;
            if (occurrence > 1) s += "   (×" + occurrence + ")";
            return s;
        }

        // ==================================================================
        //  Écriture — IMPURE, et qui n'a surtout pas le droit de lever
        // ==================================================================

        /// <summary>
        /// Note un échec. À appeler DANS un catch, à la place du silence.
        ///
        /// Ne lève jamais, quoi qu'il arrive : un journal qui fait tomber l'application qu'il
        /// observe est pire que pas de journal. C'est la seule règle absolue de ce fichier.
        /// </summary>
        public static void Echec(string zone, Exception ex)
        {
            try
            {
                string type = ex == null ? "échec" : ex.GetType().Name;
                string msg = ex == null ? "" : ex.Message;
                Ecrit(zone, type, msg);
            }
            catch { }
        }

        /// <summary>Note un échec qui n'a pas produit d'exception — un code de retour ignoré,
        /// une valeur attendue et absente. Ce sont les plus difficiles à retrouver après coup.</summary>
        public static void Echec(string zone, string quoi)
        {
            try { Ecrit(zone, "échec", quoi); }
            catch { }
        }

        private static void Ecrit(string zone, string type, string message)
        {
            string cle = zone + "|" + type + "|" + SurUneLigne(message);
            long n;

            lock (Gate)
            {
                long dejaVu;
                n = Repetitions.TryGetValue(cle, out dejaVu) ? dejaVu + 1 : 1;
                Repetitions[cle] = n;
                if (!MeriteUneLigne(n)) return;

                try
                {
                    Tourne();
                    File.AppendAllText(Fichier,
                        Ligne(DateTime.Now, zone, type, message, n) + Environment.NewLine);
                }
                catch { }
            }
        }

        /// <summary>Archive le fichier courant s'il a atteint la taille maximale. Appelé sous
        /// le verrou : deux rotations simultanées perdraient des lignes.</summary>
        private static void Tourne()
        {
            try
            {
                var fi = new FileInfo(Fichier);
                if (!fi.Exists || fi.Length < TailleMax) return;
                try { if (File.Exists(Archive)) File.Delete(Archive); } catch { }
                File.Move(Fichier, Archive);
            }
            catch { }
        }

        // ==================================================================
        //  Relecture
        // ==================================================================

        /// <summary>Les n dernières lignes, les plus récentes d'abord. Liste vide si le journal
        /// n'existe pas — ce qui est le cas normal sur une machine où rien n'a échoué.</summary>
        public static List<string> Dernieres(int n)
        {
            var l = new List<string>();
            try
            {
                lock (Gate)
                {
                    if (!File.Exists(Fichier)) return l;
                    string[] tout = File.ReadAllLines(Fichier);
                    for (int i = tout.Length - 1; i >= 0 && l.Count < n; i--)
                        if (!string.IsNullOrWhiteSpace(tout[i])) l.Add(tout[i].Trim());
                }
            }
            catch { }
            return l;
        }

        /// <summary>Nombre d'échecs DISTINCTS notés depuis le lancement. Sert à l'auto-diagnostic :
        /// zéro est la réponse attendue, et tout le reste mérite un regard.</summary>
        public static int EchecsDistincts()
        {
            lock (Gate) return Repetitions.Count;
        }

        /// <summary>Chemin du journal, pour l'afficher à l'utilisateur.</summary>
        public static string Chemin { get { return Fichier; } }
    }
}
