using System;
using System.Collections.Generic;

namespace BTOptimizer
{
    public class EngineResult
    {
        public int Ok;
        public int Ko;
        public bool RebootNeeded;
        public string BackupDir;
        public bool PrepFailed;
        public string PrepError;
    }

    /// <summary>
    /// Moteur appliquer/rétablir : sauvegarde d'abord, puis chaque optimisation
    /// isolément (une qui échoue n'interrompt pas les autres). Le rétablissement
    /// se fait dans l'ordre INVERSE de l'application (ex : défaire l'USB avant
    /// de quitter le plan d'alimentation qui le porte).
    /// Niveaux de log : 0 = info, 1 = OK, 2 = attention, 3 = erreur.
    /// </summary>
    internal static class Engine
    {
        public static EngineResult Run(List<Tweak> selection, bool apply,
                                       bool doBackup, bool doRestorePoint,
                                       Action<string, int> log)
        {
            var result = new EngineResult();
            string verb = apply ? "Appliqué" : "Rétabli";
            try
            {
                if (apply && doBackup)
                {
                    result.BackupDir = Sys.ExportBackup(selection, log);
                    log("Sauvegarde du registre créée : " + result.BackupDir, 1);
                }
                if (apply && doRestorePoint)
                {
                    Sys.CreateRestorePoint(log);
                }
            }
            catch (Exception ex)
            {
                // Échec de la préparation : on n'applique rien, on l'explique.
                result.PrepFailed = true;
                result.PrepError = ex.Message;
                log("ÉCHEC de la préparation : " + ex.Message + " — opération interrompue.", 3);
                return result;
            }

            var ordered = new List<Tweak>(selection);
            if (!apply) ordered.Reverse();

            foreach (Tweak t in ordered)
            {
                try
                {
                    if (apply) t.Apply();
                    else t.Revert();
                    result.Ok++;
                    if (t.Reboot) result.RebootNeeded = true;
                    log(verb + " : " + t.Name, 1);
                }
                catch (Exception ex)
                {
                    result.Ko++;
                    log("ÉCHEC : " + t.Name + " -> " + ex.Message, 3);
                }
            }

            log("Terminé : " + result.Ok + " réussite(s), " + result.Ko + " échec(s).", 0);
            if (result.RebootNeeded)
                log("Un redémarrage est nécessaire pour certaines optimisations.", 2);
            return result;
        }
    }
}
