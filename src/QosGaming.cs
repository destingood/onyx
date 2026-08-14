using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// MARQUAGE DSCP DES PAQUETS DE JEU — dire au routeur lesquels sont urgents.
    ///
    /// Chaque paquet IP porte six bits libres, le champ DSCP. Un équipement réseau qui les regarde
    /// peut servir les paquets marqués « EF » (Expedited Forwarding, valeur 46) avant les autres.
    /// Windows sait poser cette marque par application ; il faut juste le lui demander.
    ///
    /// DEUX PIÈGES QUE LES GUIDES OUBLIENT, ET QUI RENDENT LA MANŒUVRE INUTILE :
    ///
    ///  • MARQUER TOUT REVIENT À NE RIEN MARQUER. La recette qui circule vise « *.exe », donc tous
    ///    les programmes de la machine. Si le navigateur, le téléchargement Steam et le jeu portent
    ///    la même marque de priorité maximale, il n'y a plus de priorité — juste une file d'attente
    ///    avec un autocollant. ONYX ne marque QUE les exécutables des jeux réellement installés.
    ///
    ///  • SUR UN RÉSEAU DOMESTIQUE, WINDOWS IGNORE LA POLITIQUE PAR DÉFAUT. Le marquage est lié à la
    ///    reconnaissance du réseau (NLA) : hors domaine d'entreprise — c'est-à-dire chez tout le
    ///    monde — la politique peut n'être jamais appliquée. Microsoft documente le réglage qui lève
    ///    ça, et il faut l'écrire aussi. Vérifié : sur la machine de test, réseau « Public », clé
    ///    absente, aucune politique active. La recette du tutoriel n'y aurait rien marqué du tout.
    ///
    /// CE QUE ÇA NE FAIT PAS, DIT FRANCHEMENT. La marque est une DEMANDE, pas un ordre. Beaucoup de
    /// box domestiques l'ignorent, et la plupart des opérateurs la réécrivent en sortie de réseau
    /// local. Le gain est donc réel sur un routeur configuré pour en tenir compte, et NUL ailleurs —
    /// jamais négatif. C'est gratuit, ça ne se voit pas sur un test de débit, et ça ne remplace pas
    /// un routeur qui gère sa file d'attente (SQM).
    /// </summary>
    internal static class QosGaming
    {
        /// <summary>Là où Windows lit les politiques QoS de la machine.</summary>
        public const string Racine = @"SOFTWARE\Policies\Microsoft\Windows\QoS";

        /// <summary>Réglage documenté par Microsoft : appliquer les politiques même quand le réseau
        /// n'est pas reconnu comme un domaine — donc sur toutes les connexions domestiques.</summary>
        private const string CleNla = @"SYSTEM\CurrentControlSet\services\Tcpip\QoS";

        /// <summary>Expedited Forwarding : la classe des flux temps réel (voix, jeu).</summary>
        public const int Dscp = 46;

        /// <summary>Préfixe des politiques posées par ONYX — pour ne jamais toucher à celles des
        /// autres (une politique d'entreprise ne doit pas disparaître parce qu'on nettoie).</summary>
        public const string Prefixe = "ONYX-Jeu-";

        // ------------------------------------------------------------------ pur

        /// <summary>Nom de politique PUR pour un exécutable. Vide si le nom est inexploitable.</summary>
        public static string NomPolitique(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return "";
            string n = Path.GetFileName(exe.Trim());
            if (n.Length == 0 || n.Length > 60) return "";
            foreach (char c in n)
                if (c == '\\' || c == '/' || c == '*' || c == '?') return "";
            return Prefixe + n;
        }

        /// <summary>PUR : cette politique appartient-elle à ONYX ?</summary>
        public static bool EstANous(string nom)
        {
            return !string.IsNullOrEmpty(nom) && nom.StartsWith(Prefixe, StringComparison.Ordinal);
        }

        /// <summary>
        /// PUR : un marquage sur CET ensemble d'exécutables a-t-il un sens ?
        ///
        /// Marquer zéro programme ne sert à rien, et en marquer trop revient à n'en marquer aucun :
        /// la priorité n'existe que par contraste. Au-delà d'une trentaine de jeux, on refuse —
        /// mieux vaut ne rien faire que de fabriquer une fausse priorité.
        /// </summary>
        public static bool EstUtile(int nombre)
        {
            return nombre > 0 && nombre <= 30;
        }

        /// <summary>Exécutables de jeu trouvés dans un dossier d'installation. PUR sur la liste de
        /// fichiers fournie : les lanceurs et outils annexes sont écartés, ils ne portent pas le
        /// trafic de jeu et diluerait la priorité.</summary>
        public static bool EstExeDeJeu(string chemin)
        {
            if (string.IsNullOrEmpty(chemin)) return false;
            string n = Path.GetFileNameWithoutExtension(chemin).ToLowerInvariant();
            string[] annexes =
            {
                "launcher", "unins", "uninstall", "setup", "install", "crashreport", "crashhandler",
                "redist", "vcredist", "dxsetup", "eac", "easyanticheat", "battleye", "beservice",
                "helper", "updater", "patcher", "config", "settings", "benchmark", "editor", "server"
            };
            foreach (string a in annexes)
                if (n.Contains(a)) return false;
            return true;
        }

        // ------------------------------------------------------------------ matériel

        /// <summary>Politiques posées par ONYX actuellement en place.</summary>
        public static List<string> Existantes()
        {
            var l = new List<string>();
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(Racine, false))
                {
                    if (k == null) return l;
                    foreach (string n in k.GetSubKeyNames())
                        if (EstANous(n)) l.Add(n);
                }
            }
            catch { }
            return l;
        }

        /// <summary>true si Windows appliquera les politiques hors domaine.</summary>
        public static bool NlaLevee()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(CleNla, false))
                    return k != null && Convert.ToString(k.GetValue("Do not use NLA")) == "1";
            }
            catch { return false; }
        }

        /// <summary>
        /// Pose une politique par exécutable. Rend le nombre réellement écrit.
        /// Les valeurs sont des chaînes : c'est le format qu'attend le service QoS, un DWORD y est
        /// ignoré en silence — le genre d'erreur qui donne une politique visible mais inerte.
        /// </summary>
        public static int Appliquer(List<string> exes, Action<string, int> log)
        {
            if (exes == null || !EstUtile(exes.Count))
            {
                if (log != null)
                    log(exes == null || exes.Count == 0
                        ? "Aucun jeu détecté : aucune politique de priorité n'a été créée."
                        : exes.Count + " exécutables — c'est trop pour une priorité qui veut dire "
                          + "quelque chose. Rien n'a été fait.", 2);
                return 0;
            }

            int n = 0;
            foreach (string exe in exes)
            {
                string nom = NomPolitique(exe);
                if (nom.Length == 0) continue;
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.CreateSubKey(Racine + "\\" + nom))
                    {
                        if (k == null) continue;
                        k.SetValue("Version", "1.0", RegistryValueKind.String);
                        k.SetValue("Application Name", Path.GetFileName(exe), RegistryValueKind.String);
                        k.SetValue("Protocol", "*", RegistryValueKind.String);
                        k.SetValue("Local Port", "*", RegistryValueKind.String);
                        k.SetValue("Local IP", "*", RegistryValueKind.String);
                        k.SetValue("Local IP Prefix Length", "*", RegistryValueKind.String);
                        k.SetValue("Remote Port", "*", RegistryValueKind.String);
                        k.SetValue("Remote IP", "*", RegistryValueKind.String);
                        k.SetValue("Remote IP Prefix Length", "*", RegistryValueKind.String);
                        k.SetValue("DSCP Value", Dscp.ToString(), RegistryValueKind.String);
                        k.SetValue("Throttle Rate", "-1", RegistryValueKind.String);   // aucune limite de débit
                        n++;
                    }
                }
                catch { }
            }

            // Sans ça, la politique existe et ne s'applique jamais sur une connexion domestique.
            try
            {
                using (RegistryKey k = Registry.LocalMachine.CreateSubKey(CleNla))
                    if (k != null) k.SetValue("Do not use NLA", "1", RegistryValueKind.String);
            }
            catch { }

            if (log != null && n > 0)
                log(n + " jeu(x) marqué(s) prioritaires (DSCP " + Dscp + "). Effet au prochain "
                  + "redémarrage, et SEULEMENT si ta box tient compte du marquage — beaucoup "
                  + "l'ignorent, et les opérateurs le réécrivent souvent. C'est gratuit, jamais "
                  + "négatif, mais ça ne remplace pas un routeur qui gère sa file d'attente.", 2);
            return n;
        }

        /// <summary>Retire les politiques d'ONYX — et elles seules.</summary>
        public static int Retirer(Action<string, int> log)
        {
            int n = 0;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(Racine, true))
                {
                    if (k == null) return 0;
                    foreach (string nom in Existantes())
                    {
                        try { k.DeleteSubKeyTree(nom, false); n++; }
                        catch { }
                    }
                }
            }
            catch { }
            // La levée NLA n'est PAS retirée : elle ne fait rien à elle seule (sans politique, rien
            // n'est marqué) et d'autres logiciels — Teams, softphones — en dépendent peut-être.
            if (n > 0 && log != null) log(n + " politique(s) de priorité retirée(s).", 1);
            return n;
        }

        /// <summary>Exécutables des jeux détectés sur la machine, prêts à être marqués.</summary>
        public static List<string> ExesDesJeuxDetectes()
        {
            var l = new List<string>();
            try
            {
                var jeux = GameScan.Known();
                GameScan.Detect(jeux);
                foreach (var g in jeux)
                {
                    if (!g.Detected || string.IsNullOrEmpty(g.InstallPath)) continue;
                    try
                    {
                        foreach (string f in Directory.GetFiles(g.InstallPath, "*.exe", SearchOption.TopDirectoryOnly))
                            if (EstExeDeJeu(f) && !l.Contains(Path.GetFileName(f)))
                                l.Add(Path.GetFileName(f));
                    }
                    catch { }
                }
            }
            catch { }
            return l;
        }
    }
}
