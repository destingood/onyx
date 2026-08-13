using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// « UNE PAGE DES PARAMÈTRES WINDOWS PLANTE » — et personne ne sait pourquoi.
    ///
    /// Symptôme vécu : Paramètres → Système → Marche/Arrêt s'ouvre sur un rectangle vide. Le
    /// journal d'événements ne dit qu'une chose : SystemSettings.exe, module
    /// SystemSettingsViewModel.Desktop.dll, exception 0xc000027b. C'est une « stowed exception »,
    /// c'est-à-dire une enveloppe : le vrai motif est à l'intérieur, et Windows ne le montre nulle
    /// part.
    ///
    /// Or il est écrit noir sur blanc dans le vidage mémoire, sous une forme exploitable :
    ///     id=SystemSettings_PowerAndSleep_EnergySaving, page=SettingsPagePowerAndBattery
    ///     -- Platform::Exception^: Le mappeur de point final n'a plus de point final disponible.
    ///
    /// Ce dernier message est l'erreur RPC 1753 : le réglage a appelé un service qui n'a pas
    /// d'endpoint enregistré — autrement dit un service ARRÊTÉ ou DÉSACTIVÉ. Reste à savoir lequel :
    /// le vidage liste aussi les DLL chargées au moment du plantage, et chaque service Windows
    /// déclare la sienne. On croise les deux, et on ne garde que les services réellement désactivés.
    ///
    /// Réparation VOLONTAIREMENT MINIMALE : on repasse en « Manuel », jamais en « Automatique ».
    /// En manuel le service ne tourne pas — il ne coûte donc rien — mais son endpoint redevient
    /// enregistrable à la demande, ce qui suffit à la page pour ne plus planter. L'utilisateur garde
    /// le bénéfice de son optimisation, sans le bug.
    /// </summary>
    internal static class SettingsCrash
    {
        public sealed class Rapport
        {
            public string Fichier;
            public DateTime Quand;
            public string Reglage;              // ex. SystemSettings_PowerAndSleep_EnergySaving
            public string Page;                 // ex. SettingsPagePowerAndBattery
            public string Erreur;               // message de l'exception interne
            public List<string> Modules = new List<string>();   // DLL de System32 chargées
            public bool EstErreurService;       // le motif désigne-t-il un service absent ?
        }

        public static string DossierVidages
        {
            get
            {
                try { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"); }
                catch { return null; }
            }
        }

        /// <summary>Vidages de l'application Paramètres, du plus récent au plus ancien.</summary>
        public static List<string> Vidages()
        {
            var l = new List<string>();
            try
            {
                string d = DossierVidages;
                if (d == null || !Directory.Exists(d)) return l;
                var f = new List<FileInfo>();
                foreach (var p in Directory.GetFiles(d, "SystemSettings.exe*.dmp"))
                    try { f.Add(new FileInfo(p)); } catch { }
                f.Sort(delegate (FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
                foreach (var x in f) l.Add(x.FullName);
            }
            catch { }
            return l;
        }

        /// <summary>
        /// Un vidage se lit ici comme du texte UTF-16 : les chaînes du processus y sont telles
        /// quelles. On plafonne la taille lue — un vidage complet peut peser 700 Mo.
        /// </summary>
        public static string Texte(string chemin, long maxOctets)
        {
            try
            {
                if (string.IsNullOrEmpty(chemin) || !File.Exists(chemin)) return "";
                using (var fs = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long n = Math.Min(maxOctets, fs.Length);
                    var buf = new byte[n];
                    int lu = 0;
                    while (lu < n)
                    {
                        int r = fs.Read(buf, lu, (int)(n - lu));
                        if (r <= 0) break;
                        lu += r;
                    }
                    return System.Text.Encoding.Unicode.GetString(buf, 0, lu);
                }
            }
            catch { return ""; }
        }

        // Motifs d'erreur qui désignent un service absent plutôt qu'un vrai bug de Windows.
        private static readonly string[] MotifsService =
        {
            "mappeur de point final",          // FR — EPT_S_NOT_REGISTERED
            "endpoint mapper",                 // EN
            "serveur RPC n'est pas disponible",
            "RPC server is unavailable"
        };

        /// <summary>Analyse PURE du contenu d'un vidage (testable sans Windows).</summary>
        public static Rapport Analyse(string contenu)
        {
            var r = new Rapport();
            if (string.IsNullOrEmpty(contenu)) return r;

            var m = System.Text.RegularExpressions.Regex.Match(
                contenu, @"id=(SystemSettings_[A-Za-z0-9_]+),\s*page=([A-Za-z0-9_]+)(.{0,200})",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (m.Success)
            {
                r.Reglage = m.Groups[1].Value;
                r.Page = m.Groups[2].Value;
                string q = m.Groups[3].Value;
                int i = q.IndexOf("Exception", StringComparison.OrdinalIgnoreCase);
                if (i >= 0) q = q.Substring(i);
                r.Erreur = Propre(q);
            }

            string bas = (r.Erreur ?? "").ToLowerInvariant();
            foreach (var mo in MotifsService)
                if (bas.IndexOf(mo.ToLowerInvariant(), StringComparison.Ordinal) >= 0) { r.EstErreurService = true; break; }

            // DLL de System32 présentes dans le vidage : la liste des modules chargés.
            var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Text.RegularExpressions.Match d in System.Text.RegularExpressions.Regex.Matches(
                         contenu, @"[A-Za-z]:\\Windows\\System32\\([A-Za-z0-9_.\-]+\.dll)",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                string nom = d.Groups[1].Value;
                if (vus.Add(nom)) r.Modules.Add(nom);
            }
            return r;
        }

        private static string Propre(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
                sb.Append(c >= ' ' && c != '�' ? c : ' ');
            string t = sb.ToString();
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            t = t.Trim();
            int p = t.IndexOf(". ", StringComparison.Ordinal);   // on coupe à la fin de la phrase
            if (p > 20) t = t.Substring(0, p + 1);
            if (t.Length > 220) t = t.Substring(0, 220);
            return t;
        }

        /// <summary>Table « DLL de service » → nom du service, construite depuis le registre.</summary>
        public static Dictionary<string, string> ServicesParDll()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (RegistryKey racine = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", false))
                {
                    if (racine == null) return map;
                    foreach (string nom in racine.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey p = racine.OpenSubKey(nom + @"\Parameters", false))
                            {
                                if (p == null) continue;
                                string dll = Convert.ToString(p.GetValue("ServiceDll")) ?? "";
                                if (dll.Length == 0) continue;
                                string f = Path.GetFileName(dll);
                                if (f.Length > 0 && !map.ContainsKey(f)) map[f] = nom;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return map;
        }

        public sealed class Suspect
        {
            public string Service;
            public string Dll;
            public string Affichage;
        }

        /// <summary>
        /// Croisement PUR : parmi les modules chargés au moment du plantage, lesquels appartiennent
        /// à un service actuellement DÉSACTIVÉ ? <paramref name="estDesactive"/> est injecté pour
        /// rester testable sans registre.
        /// </summary>
        public static List<Suspect> Suspects(Rapport r, Dictionary<string, string> parDll,
                                             Func<string, bool> estDesactive)
        {
            var l = new List<Suspect>();
            if (r == null || !r.EstErreurService || parDll == null || estDesactive == null) return l;
            var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dll in r.Modules)
            {
                string svc;
                if (!parDll.TryGetValue(dll, out svc)) continue;
                if (!vus.Add(svc)) continue;
                bool off;
                try { off = estDesactive(svc); } catch { continue; }
                if (off) l.Add(new Suspect { Service = svc, Dll = dll });
            }
            return l;
        }

        /// <summary>Suspects réels de la machine.</summary>
        public static List<Suspect> SuspectsMachine(Rapport r)
        {
            var l = Suspects(r, ServicesParDll(), delegate (string s) { return Sys.ServiceDisabled(s); });
            foreach (var s in l)
            {
                try
                {
                    object dn = Sys.GetMachine(@"SYSTEM\CurrentControlSet\Services\" + s.Service, "DisplayName");
                    string t = Convert.ToString(dn) ?? "";
                    s.Affichage = t.StartsWith("@") ? s.Service : t;
                }
                catch { s.Affichage = s.Service; }
            }
            return l;
        }

        /// <summary>
        /// Réparation MINIMALE : « Manuel », jamais « Automatique ». Le service ne tournera pas de
        /// lui-même — donc l'optimisation reste — mais Windows pourra le démarrer à la demande, ce
        /// qui suffit à la page pour ne plus planter.
        /// </summary>
        public static int PasserEnManuel(List<Suspect> suspects, Action<string, int> log)
        {
            int n = 0;
            if (suspects == null) return 0;
            foreach (var s in suspects)
            {
                try
                {
                    Sys.ConfigureService(s.Service, "demand", false, false);
                    n++;
                    if (log != null)
                        log("Service « " + (s.Affichage ?? s.Service) + " » repassé en démarrage MANUEL "
                          + "(il ne tourne pas, mais la page des Paramètres cesse de planter).", 1);
                }
                catch (Exception ex)
                {
                    if (log != null) log("Impossible de modifier " + s.Service + " : " + ex.Message, 3);
                }
            }
            return n;
        }

        // ---- Armement du diagnostic : sans vidage, on ne peut rien conclure ----

        private const string CleDumps = @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\SystemSettings.exe";

        public static bool VidagesArmes()
        {
            try { return Sys.GetMachine(CleDumps, "DumpType") != null; }
            catch { return false; }
        }

        /// <summary>Demande à Windows d'écrire un vidage au prochain plantage des Paramètres.
        /// Type 1 = mini-vidage : quelques Mo, suffisant pour lire le motif — au lieu des 700 Mo
        /// d'un vidage complet.</summary>
        public static bool ArmerVidages(Action<string, int> log)
        {
            try
            {
                Sys.SetMachine(CleDumps, "DumpType", 1, RegistryValueKind.DWord);
                if (log != null) log("Diagnostic armé : le prochain plantage des Paramètres laissera une trace exploitable.", 1);
                return true;
            }
            catch (Exception ex)
            {
                if (log != null) log("Armement impossible : " + ex.Message, 3);
                return false;
            }
        }

        public static void DesarmerVidages(Action<string, int> log)
        {
            try
            {
                Sys.DelMachine(CleDumps, "DumpType");
                if (log != null) log("Diagnostic désarmé : plus de vidage écrit à chaque plantage.", 0);
            }
            catch { }
        }

        /// <summary>Analyse du vidage le plus récent (2 Mo suffisent : le motif est dans l'en-tête).</summary>
        public static Rapport DernierRapport()
        {
            foreach (string v in Vidages())
            {
                Rapport r = Analyse(Texte(v, 4L * 1024 * 1024));
                if (r.Reglage != null)
                {
                    r.Fichier = v;
                    try { r.Quand = File.GetLastWriteTime(v); } catch { }
                    return r;
                }
            }
            return null;
        }

        /// <summary>Mise en forme PURE, prête à afficher.</summary>
        public static string Texte(Rapport r, List<Suspect> suspects)
        {
            if (r == null) return "Aucun plantage des Paramètres exploitable pour le moment.";
            var sb = new System.Text.StringBuilder();
            sb.Append("La page des Paramètres qui plante, et pourquoi :\n\n");
            sb.Append("   Réglage fautif : ").Append(r.Reglage).Append('\n');
            sb.Append("   Page           : ").Append(r.Page).Append('\n');
            if (!string.IsNullOrEmpty(r.Erreur)) sb.Append("   Motif exact    : ").Append(r.Erreur).Append('\n');
            if (r.Quand != DateTime.MinValue) sb.Append("   Plantage du    : ").Append(r.Quand.ToString("dd/MM/yyyy HH:mm")).Append('\n');
            sb.Append('\n');
            if (!r.EstErreurService)
            {
                sb.Append("Le motif ne désigne pas un service arrêté : ONYX ne touchera à rien. "
                        + "Cette panne vient probablement de Windows lui-même — la réparation d'intégrité "
                        + "(DISM puis SFC) est la piste à suivre.");
                return sb.ToString();
            }
            sb.Append("Ce motif est une erreur RPC : le réglage a appelé un service qui n'a pas d'endpoint "
                    + "enregistré, autrement dit un service DÉSACTIVÉ.\n\n");
            if (suspects == null || suspects.Count == 0)
            {
                sb.Append("Aucun service désactivé ne figure parmi les modules chargés au moment du plantage. "
                        + "La cause est donc ailleurs : garde le diagnostic armé et relance l'enquête après "
                        + "le prochain plantage.");
                return sb.ToString();
            }
            sb.Append("Service(s) désactivé(s) chargé(s) au moment du plantage :\n");
            foreach (var s in suspects)
                sb.Append("   • ").Append(s.Affichage ?? s.Service).Append("   (").Append(s.Dll).Append(")\n");
            sb.Append("\nLa réparation les repasse en MANUEL, jamais en automatique : ils ne tourneront pas "
                    + "d'eux-mêmes — ton optimisation est préservée — mais Windows pourra les démarrer à la "
                    + "demande, ce qui suffit à la page pour cesser de planter.");
            return sb.ToString();
        }
    }
}
