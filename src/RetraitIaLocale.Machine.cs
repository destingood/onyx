using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace BTOptimizer
{
    /// <summary>
    /// Retrait de l'IA locale, partie qui agit sur la machine (hors du harnais de test) : une seule
    /// fois, en tâche de fond, au premier lancement après la mise à jour. Les règles de décision
    /// sont dans RetraitIaLocale.cs et testées.
    /// </summary>
    internal static partial class RetraitIaLocale
    {
        private const string CleRun = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>Fait le retrait s'il reste à faire. Ne lève jamais : un échec est noté et réessayé au lancement suivant (trois fois au plus).</summary>
        public static void Lancer()
        {
            string chemin = AppPaths.File(Marqueur);
            string marqueur = null;
            try { if (File.Exists(chemin)) marqueur = File.ReadAllText(chemin).Trim(); } catch { }
            if (!AFaire(marqueur)) return;
            try
            {
                string bilan = Retirer();
                Noter(chemin, "fait|" + bilan);
                try { Journal.Add(bilan); } catch { }
            }
            catch (Exception ex)
            {
                int echecs = Echecs(marqueur) + 1;
                Noter(chemin, "echec " + echecs);
                try { JournalTechnique.Echec("Retrait IA locale", ex); } catch { }
                if (echecs >= 3)
                {
                    // Dernier essai raté : on retire au moins ce qui appartient à ONYX et on le dit.
                    EffacerFichiersOnyx();
                    try { Journal.Add("IA locale retirée d'ONYX. Ollama n'a pas pu être désinstallé : Paramètres > Applications > Ollama > Désinstaller."); } catch { }
                }
            }
        }

        private static string Retirer()
        {
            string dossier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Ollama");
            string essai = AppPaths.File("bt-ia-setup.txt");
            // Les dates sont lues AVANT d'effacer bt-ia-setup.txt : c'est la seule preuve de qui a installé Ollama.
            DateTime? premierEssai = File.Exists(essai) ? File.GetCreationTime(essai) : (DateTime?)null;
            DateTime? creation = Directory.Exists(dossier) ? Directory.GetCreationTime(dossier) : (DateTime?)null;
            bool parOnyx = InstalleeParOnyx(premierEssai, creation);

            long liberes = 0;
            if (parOnyx) liberes = Desinstaller(dossier);    // lève si Ollama reste : bt-ia-setup.txt est gardé pour réessayer
            EffacerFichiersOnyx();
            return Bilan(parOnyx, creation != null && !parOnyx, liberes);
        }

        /// <summary>Désinstalle Ollama et ses modèles ; rend la place libérée.</summary>
        private static long Desinstaller(string dossier)
        {
            string maison = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama");
            string modeles = Path.Combine(maison, "models");
            // OLLAMA_MODELS posé à la main : les modèles sont ailleurs, peut-être partagés. On n'y touche pas.
            string autresModeles = Environment.GetEnvironmentVariable("OLLAMA_MODELS", EnvironmentVariableTarget.User)
                                ?? Environment.GetEnvironmentVariable("OLLAMA_MODELS");
            bool effacerModeles = string.IsNullOrWhiteSpace(autresModeles);
            long octets = Octets(new DirectoryInfo(dossier)) + (effacerModeles ? Octets(new DirectoryInfo(modeles)) : 0);

            Arreter(dossier);
            string desinstalleur = Path.Combine(dossier, "unins000.exe");
            if (File.Exists(desinstalleur))
                Sys.Run(desinstalleur, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", 120000);
            else
            {
                string winget = LibScan.WingetPath();
                if (winget == null) throw new InvalidOperationException("désinstalleur d'Ollama introuvable");
                Sys.Run(winget, "uninstall --id Ollama.Ollama --exact --silent --accept-source-agreements", Sys.LongRunTimeoutMs);
            }
            // Le désinstalleur Inno se recopie dans %TEMP% et rend la main aussitôt : on attend que le programme parte.
            string exe = Path.Combine(dossier, "ollama.exe");
            for (int i = 0; i < 180 && File.Exists(exe); i++) Thread.Sleep(1000);
            if (File.Exists(exe)) throw new InvalidOperationException("Ollama toujours présent après la désinstallation");

            if (effacerModeles)
            {
                Supprimer(modeles);
                Supprimer(Path.Combine(maison, "history"));
            }
            else
            {
                try { Journal.Add("IA locale : modèles d'Ollama gardés dans " + autresModeles + " (dossier choisi à la main)."); } catch { }
            }

            // Réglages qu'ONYX avait posés : retirés seulement s'ils sont encore tels qu'ONYX les a laissés.
            try { if (RunDOllama(Sys.GetUser(CleRun, "Ollama") as string, dossier)) Sys.DelUser(CleRun, "Ollama"); } catch { }
            try
            {
                if (KeepAliveDOnyx(Environment.GetEnvironmentVariable("OLLAMA_KEEP_ALIVE", EnvironmentVariableTarget.User)))
                    Environment.SetEnvironmentVariable("OLLAMA_KEEP_ALIVE", null, EnvironmentVariableTarget.User);
            }
            catch { }
            return octets;
        }

        /// <summary>Ferme l'appli de zone de notification et le moteur d'Ollama, s'ils viennent de ce dossier.</summary>
        private static void Arreter(string dossier)
        {
            foreach (string nom in new[] { "ollama app", "ollama" })
            {
                Process[] liste;
                try { liste = Process.GetProcessesByName(nom); } catch { continue; }
                foreach (Process p in liste)
                {
                    try
                    {
                        string chemin = null;
                        try { chemin = p.MainModule != null ? p.MainModule.FileName : null; } catch { }
                        // Chemin illisible = processus élevé ou d'un autre compte : pas celui qu'ONYX a installé, on n'y touche pas.
                        if (chemin != null && chemin.StartsWith(dossier.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                        {
                            p.Kill(true);
                            p.WaitForExit(5000);
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
        }

        private static void EffacerFichiersOnyx()
        {
            foreach (string f in FichiersOnyx)
                try { string p = AppPaths.File(f); if (File.Exists(p)) File.Delete(p); } catch { }
        }

        private static void Supprimer(string chemin)
        {
            try
            {
                if (Directory.Exists(chemin)) Directory.Delete(chemin, true);
                else if (File.Exists(chemin)) File.Delete(chemin);
            }
            catch { }
        }

        private static long Octets(DirectoryInfo d)
        {
            long n = 0;
            try
            {
                if (!d.Exists) return 0;
                foreach (FileInfo f in d.EnumerateFiles("*", SearchOption.AllDirectories))
                    try { n += f.Length; } catch { }
            }
            catch { }
            return n;
        }

        private static void Noter(string chemin, string texte)
        {
            try { File.WriteAllText(chemin, texte); } catch { }
        }
    }
}
