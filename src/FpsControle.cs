using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace BTOptimizer
{
    /// <summary>
    /// Contrôle du compteur de FPS sur la machine (ligne de commande -fps, administrateur) : relève
    /// le compteur chaque seconde comme le fait l'affichage en jeu, puis le compare au compte exact
    /// des images reçues sur toute la durée. Lecture seule : rien n'est injecté ni modifié.
    /// </summary>
    internal static class FpsControle
    {
        public static List<string> Mesure(string exe, int secondes)
        {
            var l = new List<string>();
            secondes = Math.Max(5, Math.Min(300, secondes));
            // Nom de session à part : démarrer une session du même nom couperait le compteur en jeu.
            using (var fps = new FpsEtw("BTOptimizer-FPS-Controle"))
            {
                if (!fps.Start())
                {
                    l.Add("Mesure impossible : " + (fps.LastError ?? "session ETW refusée") + ". Lance la commande en administrateur.");
                    return l;
                }
                FpsEtw.ProcStat jeu = null;
                for (int i = 0; i < 20 && jeu == null; i++)
                {
                    Thread.Sleep(1000);
                    jeu = Choisit(fps.Snapshot(1000), exe);
                }
                if (jeu == null)
                {
                    l.Add(exe == null ? "Aucun jeu au premier plan en 20 s. Passe sur le jeu juste après avoir lancé la commande, ou donne son nom (-fps Jeu.exe)."
                                      : "« " + exe + " » n'affiche aucune image depuis 20 s.");
                    return l;
                }
                int pid = jeu.Pid;
                l.Add("Jeu mesuré : " + jeu.Name + " (PID " + pid + "), " + secondes + " s.");

                long total0, total1, horsOrdre; double f0, f1, retard;
                fps.Compte(pid, out total0, out f0, out retard, out horsOrdre);
                total1 = total0; f1 = f0;
                double min = double.MaxValue, max = 0, somme = 0;
                for (int s = 1; s <= secondes; s++)
                {
                    Thread.Sleep(1000);
                    double v = 0;
                    foreach (FpsEtw.ProcStat p in fps.Snapshot(1000)) if (p.Pid == pid) v = p.Fps;
                    fps.Compte(pid, out total1, out f1, out retard, out horsOrdre);
                    if (v < min) min = v;
                    if (v > max) max = v;
                    somme += v;
                    l.Add(s.ToString("00") + " s : " + N(v) + " FPS (dernier paquet d'images reçu il y a " + N(retard) + " ms)");
                }
                double duree = f1 - f0;
                l.Add("Compteur : " + N(min) + " au plus bas, " + N(max) + " au plus haut, " + N(somme / secondes) + " en moyenne.");
                if (duree > 0)
                    l.Add("Compte exact : " + (total1 - total0) + " images en " + (duree / 1000).ToString("0.0", CultureInfo.InvariantCulture)
                        + " s, soit " + N((total1 - total0) * 1000.0 / duree) + " FPS en moyenne.");
                l.Add("Images reçues dans le désordre : " + horsOrdre + ".");
            }
            return l;
        }

        private static FpsEtw.ProcStat Choisit(List<FpsEtw.ProcStat> stats, string exe)
        {
            if (exe == null) return FpsEtw.ChoisirJeu(stats, FpsEtw.PidPremierPlan());
            string voulu = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe : exe + ".exe";
            foreach (FpsEtw.ProcStat p in stats)
                if (string.Equals(p.Name, voulu, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        private static string N(double v) { return v.ToString("0", CultureInfo.InvariantCulture); }
    }
}
