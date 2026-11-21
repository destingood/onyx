using System;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// Range dans Documents\ONYX\Sauvegardes ce qu'ONYX avait laissé sur le Bureau (dossiers
    /// bt-optimizer-backup-AAAAMMJJ-HHMMSS et historique des mesures). Déplace, n'efface rien :
    /// une copie qui échoue laisse l'original en place. Ne lève jamais.
    /// </summary>
    internal static partial class Sauvegardes
    {
        public static void RangerBureau()
        {
            try
            {
                string bureau = Sys.BackupDesktop, dest = Sys.BackupDir;
                if (string.IsNullOrEmpty(bureau) || string.IsNullOrEmpty(dest) || !Directory.Exists(bureau)) return;
                int ranges = 0, restes = 0;

                foreach (string d in Directory.GetDirectories(bureau))
                {
                    if (!EstSauvegarde(Path.GetFileName(d))) continue;
                    if (Deplacer(d, Path.Combine(dest, Path.GetFileName(d)))) ranges++; else restes++;
                }

                string hist = Path.Combine(bureau, Historique);
                if (File.Exists(hist))
                {
                    try
                    {
                        Directory.CreateDirectory(dest);
                        string cible = Path.Combine(dest, Historique);
                        if (!File.Exists(cible)) File.Move(hist, cible);
                        else
                        {
                            // Les deux existent : l'ancien historique passe devant le nouveau.
                            File.WriteAllText(cible, File.ReadAllText(hist) + File.ReadAllText(cible), System.Text.Encoding.UTF8);
                            File.Delete(hist);
                        }
                        ranges++;
                    }
                    catch { restes++; }
                }

                if (ranges > 0)
                    try { Journal.Add(ranges + " sauvegarde(s) d'ONYX déplacée(s) du Bureau vers " + dest + "." +
                                      (restes > 0 ? " " + restes + " n'ont pas pu l'être et restent sur le Bureau." : "")); } catch { }
            }
            catch (Exception ex)
            {
                try { JournalTechnique.Echec("Rangement des sauvegardes du Bureau", ex); } catch { }
            }
        }

        private static bool Deplacer(string source, string cible)
        {
            bool copieFaite = false;
            try
            {
                if (Directory.Exists(cible)) return false;     // même horodatage déjà rangé : on ne mélange pas
                Directory.CreateDirectory(Path.GetDirectoryName(cible));
                try { Directory.Move(source, cible); return true; }
                catch (IOException)
                {
                    // Autre volume (Bureau dans OneDrive, par exemple) : copie complète, puis retrait de l'original.
                    Copier(new DirectoryInfo(source), cible);
                    copieFaite = true;
                    Directory.Delete(source, true);
                    return true;
                }
            }
            catch
            {
                // Copie partielle : on la retire, l'original reste intact sur le Bureau. Copie complète
                // mais original pas entièrement retiré : la copie est gardée, c'est elle qui est entière.
                if (!copieFaite)
                    try { if (Directory.Exists(source) && Directory.Exists(cible)) Directory.Delete(cible, true); } catch { }
                return false;
            }
        }

        private static void Copier(DirectoryInfo source, string cible)
        {
            Directory.CreateDirectory(cible);
            foreach (FileInfo f in source.GetFiles()) f.CopyTo(Path.Combine(cible, f.Name), false);
            foreach (DirectoryInfo d in source.GetDirectories()) Copier(d, Path.Combine(cible, d.Name));
        }
    }
}
