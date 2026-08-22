using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;

namespace BTOptimizer
{
    /// <summary>
    /// « SERVICE HÔTE : SYSTÈME CONSOMME 30 % » — une phrase qui ne veut rien dire.
    ///
    /// svchost.exe n'est pas un programme, c'est un conteneur. Chaque instance héberge un ou
    /// plusieurs services Windows. Voir « Service hôte » en haut du Gestionnaire des tâches
    /// n'apprend donc rien : c'est le SERVICE hébergé qui travaille, et tant qu'on ne l'a pas nommé,
    /// il n'y a rien à faire — sinon fermer le mauvais processus et couper plusieurs services d'un
    /// coup.
    ///
    /// Ce module fait la correspondance : pour chaque instance de svchost qui consomme réellement,
    /// il dit QUELS services elle héberge.
    ///
    /// ET IL SE GARDE D'ACCUSER À TORT. Une charge soutenue de svchost est le plus souvent Windows
    /// Update en train de faire son travail — c'est-à-dire le système qui fonctionne, pas qui
    /// dysfonctionne. Dans ce cas le constat le dit et conseille d'attendre, au lieu de pousser à
    /// désactiver un service dont la coupure durable laisserait la machine sans correctifs de
    /// sécurité.
    /// </summary>
    internal static class SvcHost
    {
        /// <summary>Une instance de svchost et ce qu'elle héberge.</summary>
        public sealed class Groupe
        {
            public int Pid;
            public double Pourcent;              // part du CPU TOTAL de la machine
            public List<string> Services = new List<string>();
        }

        /// <summary>Services dont l'activité soutenue est LÉGITIME et passagère : ils font un
        /// travail qui a une fin. On les nomme au lieu de les dénoncer.</summary>
        private static readonly string[] TravailLegitime =
        {
            "wuauserv",   // Windows Update
            "bits",       // transferts en arrière-plan
            "dosvc",      // livraison des mises à jour
            "trustedinstaller",
            "cryptsvc",
            "usosvc",     // orchestrateur de mise à jour
            "wsearch"     // indexation (a une fin, elle aussi)
        };

        // ------------------------------------------------------------------ pur

        /// <summary>Part PURE du CPU total. <paramref name="coeurs"/> = nombre de processeurs
        /// logiques : sans lui, un processus qui occupe un cœur entier sur seize afficherait 100 %.</summary>
        public static double Pourcent(double msCpu, double msEcoules, int coeurs)
        {
            if (msEcoules <= 0 || coeurs <= 0) return 0;
            double p = msCpu * 100.0 / (msEcoules * coeurs);
            if (p < 0) return 0;
            return p > 100 ? 100 : p;
        }

        /// <summary>PUR : cette liste de services correspond-elle à un travail légitime en cours ?</summary>
        public static bool EstTravailLegitime(List<string> services)
        {
            if (services == null || services.Count == 0) return false;
            foreach (string s in services)
            {
                string n = (s ?? "").ToLowerInvariant();
                foreach (string t in TravailLegitime)
                    if (n == t) return true;
            }
            return false;
        }

        /// <summary>
        /// Verdict PUR. null en dessous du seuil — un svchost à 2 % n'est pas une nouvelle.
        /// </summary>
        public static string Verdict(Groupe g, double seuil)
        {
            if (g == null || g.Pourcent < seuil) return null;

            string liste = g.Services == null || g.Services.Count == 0
                ? "service non identifié"
                : string.Join(", ", g.Services.ToArray());
            string chiffre = g.Pourcent.ToString("0.#") + " % du processeur";

            if (EstTravailLegitime(g.Services))
                return "« Service hôte » consomme " + chiffre + " — mais il héberge " + liste
                     + " : c'est Windows qui télécharge ou installe, pas une panne. Laisse finir "
                     + "avant de jouer ; ne coupe pas ces services, ils rapportent les correctifs "
                     + "de sécurité.";

            return "« Service hôte » consomme " + chiffre + " en continu — services hébergés : "
                 + liste + ". C'est ce nom-là qu'il faut regarder, pas « svchost » : le processus "
                 + "n'est qu'un conteneur, et le fermer couperait tous les services qu'il porte.";
        }

        // ------------------------------------------------------------------ mesure

        /// <summary>Services hébergés, par PID.</summary>
        public static Dictionary<int, List<string>> ServicesParPid()
        {
            var d = new Dictionary<int, List<string>>();
            try
            {
                using (var s = new ManagementObjectSearcher(
                    "SELECT Name, ProcessId FROM Win32_Service WHERE State = 'Running' AND ProcessId <> 0"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        int pid;
                        try { pid = Convert.ToInt32(mo["ProcessId"]); } catch { continue; }
                        string nom = Convert.ToString(mo["Name"]);
                        if (string.IsNullOrEmpty(nom)) continue;
                        if (!d.ContainsKey(pid)) d[pid] = new List<string>();
                        d[pid].Add(nom);
                    }
            }
            catch { }
            return d;
        }

        /// <summary>
        /// Mesure la consommation de chaque instance de svchost sur une courte fenêtre.
        /// Une SEULE mesure instantanée ne veut rien dire : on compare deux relevés du temps
        /// processeur, c'est la seule façon d'obtenir un pourcentage qui ait un sens.
        /// </summary>
        public static List<Groupe> Mesure(int fenetreMs)
        {
            var res = new List<Groupe>();
            try
            {
                Process[] procs = Process.GetProcessesByName("svchost");
                if (procs.Length == 0) return res;

                var avant = new Dictionary<int, TimeSpan>();
                foreach (Process p in procs)
                    try { avant[p.Id] = p.TotalProcessorTime; } catch { }

                var chrono = Stopwatch.StartNew();
                System.Threading.Thread.Sleep(Math.Max(200, fenetreMs));
                chrono.Stop();

                int coeurs = Environment.ProcessorCount;
                Dictionary<int, List<string>> parPid = ServicesParPid();

                foreach (Process p in procs)
                {
                    try
                    {
                        TimeSpan debut;
                        if (!avant.TryGetValue(p.Id, out debut)) continue;
                        p.Refresh();
                        double ms = (p.TotalProcessorTime - debut).TotalMilliseconds;
                        var g = new Groupe
                        {
                            Pid = p.Id,
                            Pourcent = Pourcent(ms, chrono.Elapsed.TotalMilliseconds, coeurs)
                        };
                        List<string> svc;
                        if (parPid.TryGetValue(p.Id, out svc)) g.Services = svc;
                        res.Add(g);
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch { }
            res.Sort(delegate (Groupe a, Groupe b) { return b.Pourcent.CompareTo(a.Pourcent); });
            return res;
        }

        /// <summary>Le groupe le plus gourmand, ou null.</summary>
        public static Groupe PlusGourmand(List<Groupe> l)
        {
            return l == null || l.Count == 0 ? null : l[0];
        }
    }
}
