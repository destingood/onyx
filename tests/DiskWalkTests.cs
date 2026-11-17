using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Parcours disque rapide (page Stockage) : mêmes règles que l'ancien parcours
    /// (fichiers cachés comptés dans une taille, jonctions jamais suivies, dossiers techniques
    /// sautés), vérifiées sur une vraie arborescence temporaire.
    /// </summary>
    internal static class DiskWalkTests
    {
        public static void Tout()
        {
            Banc.Titre("Parcours disque (stockage)");
            string racine = Path.Combine(Path.GetTempPath(), "onyx-diskwalk-" + Guid.NewGuid().ToString("N"));
            string dehors = racine + "-dehors";
            try
            {
                Construit(racine, dehors);
                bool jonction = Jonction(Path.Combine(racine, "sub", "lien"), dehors);
                Banc.Verifie("jonction de test créée", true, jonction);

                bool partiel;
                // 1000 (a) + 500 (h, caché) + 2000 (sub/b) + 3000 (sub/deep/c) + 2 Mo + 3 Mo + 10 Ko + 2 Mo caché
                // + .git 2 Mo + node_modules 2 Mo + jeu 2 Mo ; la jonction (10 000) n'est jamais suivie.
                long attendu = 1000 + 500 + 2000 + 3000 + Mo(2) + Mo(3) + 10240 + Mo(2) + Mo(2) + Mo(2) + Mo(2);
                long total = DiskWalk.Bytes(racine, 12, 0, out partiel);
                Banc.Verifie("taille : tout compté, cachés compris, jonction ignorée (" + total + " / " + attendu + ")", true, total == attendu);
                Banc.Verifie("taille : non partielle sans plafond", false, partiel);

                long p1 = DiskWalk.Bytes(Path.Combine(racine, "sub"), 0, 0, out partiel);
                Banc.Verifie("taille : profondeur 0 = fichiers du dossier seul (" + p1 + ")", true, p1 == 2000 + Mo(3));

                long rien = DiskWalk.Bytes(Path.Combine(racine, "absent"), 12, 0, out partiel);
                Banc.Verifie("taille : dossier absent = 0", true, rien == 0 && !partiel);

                var exclus = new List<string> { Path.Combine(racine, "jeu") };
                var noms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "node_modules" };
                List<DiskWalk.Fichier> gros = DiskWalk.GrosFichiers(racine, Mo(1), noms, exclus, 0, 5000, out partiel);
                var vus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DiskWalk.Fichier f in gros) vus.Add(f.Name);
                Banc.Verifie("gros fichiers : exactement big.bin et film.mkv (" + string.Join(", ", vus) + ")",
                    true, gros.Count == 2 && vus.Contains("big.bin") && vus.Contains("film.mkv"));
                DiskWalk.Fichier film = gros.Find(f => f.Name == "film.mkv");
                Banc.Verifie("gros fichiers : chemin, dossier, extension et taille exacts", true,
                    film != null && film.Ext == ".mkv" && film.Bytes == Mo(3)
                    && string.Equals(film.Dir, Path.Combine(racine, "sub"), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(film.Path, Path.Combine(racine, "sub", "film.mkv"), StringComparison.OrdinalIgnoreCase)
                    && Math.Abs((film.Modified - File.GetLastWriteTime(film.Path)).TotalSeconds) < 2);
                Banc.Verifie("gros fichiers : non partiel", false, partiel);

                List<DiskWalk.Fichier> un = DiskWalk.GrosFichiers(racine, Mo(1), noms, exclus, 0, 1, out partiel);
                Banc.Verifie("gros fichiers : plafond de nombre respecté et signalé", true, un.Count == 1 && partiel);

                List<DiskWalk.Fichier> absent = DiskWalk.GrosFichiers(Path.Combine(racine, "absent"), Mo(1), noms, exclus, 0, 5000, out partiel);
                Banc.Verifie("gros fichiers : racine absente = liste vide", true, absent.Count == 0);
            }
            finally
            {
                try { Directory.Delete(Path.Combine(racine, "sub", "lien")); } catch { }
                try { Directory.Delete(racine, true); } catch { }
                try { Directory.Delete(dehors, true); } catch { }
            }
        }

        private static long Mo(int n) { return n * 1048576L; }

        private static void Construit(string r, string dehors)
        {
            Directory.CreateDirectory(Path.Combine(r, "sub", "deep"));
            Directory.CreateDirectory(Path.Combine(r, ".git"));
            Directory.CreateDirectory(Path.Combine(r, "node_modules"));
            Directory.CreateDirectory(Path.Combine(r, "jeu"));
            Directory.CreateDirectory(dehors);
            Taille(Path.Combine(r, "a.bin"), 1000);
            Taille(Path.Combine(r, "h.bin"), 500);
            File.SetAttributes(Path.Combine(r, "h.bin"), FileAttributes.Hidden);
            Taille(Path.Combine(r, "sub", "b.bin"), 2000);
            Taille(Path.Combine(r, "sub", "deep", "c.bin"), 3000);
            Taille(Path.Combine(r, "big.bin"), Mo(2));
            Taille(Path.Combine(r, "sub", "film.mkv"), Mo(3));
            Taille(Path.Combine(r, "petit.bin"), 10240);
            Taille(Path.Combine(r, "cache.bin"), Mo(2));
            File.SetAttributes(Path.Combine(r, "cache.bin"), FileAttributes.Hidden);
            Taille(Path.Combine(r, ".git", "pack.bin"), Mo(2));
            Taille(Path.Combine(r, "node_modules", "lib.bin"), Mo(2));
            Taille(Path.Combine(r, "jeu", "data.pak"), Mo(2));
            Taille(Path.Combine(dehors, "loin.bin"), 10000);
            Taille(Path.Combine(dehors, "loin-gros.bin"), Mo(2));
        }

        private static void Taille(string f, long n)
        {
            using (var s = new FileStream(f, FileMode.Create)) s.SetLength(n);
        }

        private static bool Jonction(string lien, string cible)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + lien + "\" \"" + cible + "\"")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using (Process p = Process.Start(psi)) { p.WaitForExit(10000); }
                return Directory.Exists(lien) && (new DirectoryInfo(lien).Attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch { return false; }
        }
    }
}
