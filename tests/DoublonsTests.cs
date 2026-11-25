using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Doublons (page Stockage) : reconnus par le contenu, jamais par le nom ; l'exemplaire gardé
    /// est le bon ; un fichier de jeu n'est jamais proposé ; un lien physique n'est pas une copie ;
    /// la vérification complète rattrape un fichier qui ne diffère qu'entre deux extraits.
    /// </summary>
    internal static class DoublonsTests
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLinkW(string lien, string cible, IntPtr securite);

        public static void Tout()
        {
            Banc.Titre("Doublons (stockage)");
            Choix();
            Jeux();
            SurDisque();
        }

        private static Doublons.Copie C(string path, bool reference, DateTime modif)
        {
            return new Doublons.Copie
            {
                Path = path, Dir = Path.GetDirectoryName(path), Name = Path.GetFileName(path),
                Bytes = 1000, Modified = modif, Reference = reference
            };
        }

        private static void Choix()
        {
            DateTime t = new DateTime(2025, 1, 1);
            var g = new Doublons.Groupe { Bytes = 1000 };
            g.Copies.Add(C(@"D:\Films\film - Copie.mkv", false, t));
            g.Copies.Add(C(@"D:\Films\film.mkv", false, t));
            Doublons.Choisir(g);
            Banc.Egal("« - Copie » part, l'original reste", @"D:\Films\film.mkv", g.Gardee.Path);

            g = new Doublons.Groupe { Bytes = 1000 };
            g.Copies.Add(C(@"C:\Users\x\Downloads\setup.iso", false, t));
            g.Copies.Add(C(@"E:\Images\setup.iso", false, t.AddDays(5)));
            Doublons.Choisir(g);
            Banc.Egal("la copie de Téléchargements part", @"E:\Images\setup.iso", g.Gardee.Path);

            g = new Doublons.Groupe { Bytes = 1000 };
            g.Copies.Add(C(@"E:\a\rush.mov", false, t.AddDays(3)));
            g.Copies.Add(C(@"E:\b\rush.mov", false, t));
            Doublons.Choisir(g);
            Banc.Egal("à égalité, la plus ancienne est gardée", @"E:\b\rush.mov", g.Gardee.Path);

            g = new Doublons.Groupe { Bytes = 1000 };
            g.Copies.Add(C(@"E:\Sauvegarde\pakchunk0.pak", false, t));
            g.Copies.Add(C(@"D:\Jeux\Fortnite\pakchunk0.pak", true, t.AddDays(9)));
            Doublons.Choisir(g);
            Banc.Verifie("le fichier du jeu est gardé, la sauvegarde proposée", true,
                g.Gardee.Reference && g.EnTrop == 1 && !g.Copies[0].Garder);
            Banc.Egal("récupérable = une copie", "1000", g.RecuperableBytes.ToString());

            Banc.Verifie("« (2) » reconnu comme nom de copie", true,
                Doublons.Penalite(C(@"E:\x\photo (2).raw", false, t)) > Doublons.Penalite(C(@"E:\x\photo.raw", false, t)));
            Banc.Verifie("« Copy of » reconnu", true, Doublons.Penalite(C(@"E:\x\Copy of plan.psd", false, t)) >= 2);
            Banc.Verifie("un nom ordinaire n'est pas pénalisé", true, Doublons.Penalite(C(@"E:\x\vacances 2024.mp4", false, t)) == 0);

            g = new Doublons.Groupe { Bytes = 1000 };
            g.Copies.Add(C(@"E:\Archive\model_1.pth", false, t));
            g.Copies.Add(C(@"E:\Archive\model.pth", false, t));
            Doublons.Choisir(g);
            Banc.Egal("« _1 » part, l'original reste", @"E:\Archive\model.pth", g.Gardee.Path);

            // Fichiers de logiciels ou de caches : jamais proposés.
            Banc.Verifie("Unreal Engine (Engine\\ThirdParty\\lib) protégé", true,
                Doublons.Logiciel(@"F:\UE_5.8\Engine\Source\ThirdParty\Intel\lib\libembree4.so.4"));
            Banc.Verifie("réserve Ollama (nom = empreinte) protégée", true,
                Doublons.Logiciel(@"D:\ollama\models\blobs\sha256-2bada8a7450677000f678be90653b85d364de7db25eb5ea54136ada5f3933730"));
            Banc.Verifie("cache Hugging Face protégé", true,
                Doublons.Logiciel(@"E:\x\_cache\hf\hub\models--Systran--faster-whisper\snapshots\abc\model.bin"));
            Banc.Verifie("vidéo perso non protégée", false, Doublons.Logiciel(@"D:\Vidéos\Montages\vacances.mp4"));
            Banc.Verifie("archive perso non protégée", false, Doublons.Logiciel(@"E:\JARVIS\_ARCHIVE\Autres\italy.zip"));

            g = new Doublons.Groupe { Bytes = 1000 };
            g.Copies.Add(new Doublons.Copie { Path = @"E:\cache\x\a.bin", Protege = true, Bytes = 1000 });
            g.Copies.Add(new Doublons.Copie { Path = @"D:\Backup\a.bin", Bytes = 1000 });
            Doublons.Choisir(g);
            Banc.Verifie("le fichier du logiciel est gardé, la copie perso proposée", true, g.Copies[0].Garder && !g.Copies[1].Garder);

            string ra, rb;
            bool ok = Doublons.Racines(@"F:\UE_5.8\Engine\lib\x.so", @"E:\eg\UE_5.8\Engine\lib\x.so", out ra, out rb);
            Banc.Verifie("deux copies d'un moteur : racines F:\\UE_5.8 et E:\\eg\\UE_5.8", true,
                ok && ra == @"F:\UE_5.8" && rb == @"E:\eg\UE_5.8");
            Banc.Verifie("même nom dans deux dossiers sans rapport : pas un dossier en double", false,
                Doublons.Racines(@"E:\Archive\Autres\italy.zip", @"E:\Archive\Corrompus\italy.zip", out ra, out rb));
            ok = Doublons.Racines(@"C:\Jeux\a.pak", @"D:\Jeux\a.pak", out ra, out rb);
            Banc.Verifie("même dossier sur deux disques : C:\\Jeux et D:\\Jeux (jamais la racine seule)", true,
                ok && ra == @"C:\Jeux" && rb == @"D:\Jeux");
            Banc.Verifie("un seul segment commun (le nom) : pas un dossier en double", false,
                Doublons.Racines(@"C:\a.pak", @"D:\a.pak", out ra, out rb));
        }

        private static void Jeux()
        {
            Banc.Egal("nom normalisé (™, ®, ponctuation, accents)", "callofdutyblackops6",
                Doublons.NomNormalise("Call of Duty®: Black Ops 6™"));
            Banc.Egal("accents retirés", "pokemonlegendes", Doublons.NomNormalise("Pokémon Légendes"));

            var l = new List<Doublons.Install>
            {
                new Doublons.Install { Nom = "Fortnite", Lanceur = "EPIC", Dossier = @"C:\Program Files\Epic Games\Fortnite", MB = 90000 },
                new Doublons.Install { Nom = "FORTNITE™", Lanceur = "STEAM", Dossier = @"D:\SteamLibrary\steamapps\common\Fortnite", MB = 85000 },
                // Même jeu Steam déclaré aussi au registre, même dossier : une seule installation.
                new Doublons.Install { Nom = "Rocket League", Lanceur = "STEAM", Dossier = @"D:\SteamLibrary\steamapps\common\rocketleague", MB = 30000 },
                new Doublons.Install { Nom = "Rocket League", Lanceur = "PC", Dossier = @"D:\SteamLibrary\steamapps\common\rocketleague\", MB = 30000 },
                // Dossier inclus dans l'autre : même installation.
                new Doublons.Install { Nom = "Valorant", Lanceur = "RIOT", Dossier = @"C:\Riot Games\VALORANT", MB = 40000 },
                new Doublons.Install { Nom = "VALORANT", Lanceur = "PC", Dossier = @"C:\Riot Games\VALORANT\live", MB = 40000 },
                new Doublons.Install { Nom = "Forza Horizon 4", Lanceur = "XBOX", Dossier = @"E:\XboxGames\FH4", MB = 100000 },
                new Doublons.Install { Nom = "Forza Horizon 5", Lanceur = "XBOX", Dossier = @"E:\XboxGames\FH5", MB = 120000 },
            };
            List<Doublons.JeuDouble> d = Doublons.JeuxEnDouble(l);
            Banc.Egal("un seul jeu vraiment installé deux fois", "1", d.Count.ToString());
            Banc.Verifie("c'est Fortnite, 2 installations", true, d.Count == 1 && d[0].Nom == "Fortnite" && d[0].Installs.Count == 2);
            Banc.Egal("récupérable = la plus petite installation", "85000", d.Count == 1 ? d[0].RecuperableMB.ToString() : "?");
        }

        private static void SurDisque()
        {
            string dir = Path.Combine(Path.GetTempPath(), "onyx-doublons-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                var rnd = new Random(42);
                var octets = new byte[3 * 1024 * 1024 + 123];   // > 16 × 64 Ko : lu par extraits
                rnd.NextBytes(octets);

                string a = Path.Combine(dir, "film.mkv");
                string b = Path.Combine(dir, "sauvegarde_02.bin");     // autre nom, même contenu
                string fin = Path.Combine(dir, "fin_differente.mkv");  // dernier octet différent
                string milieu = Path.Combine(dir, "milieu_different.mkv");   // différent ENTRE deux extraits
                string lien = Path.Combine(dir, "lien_physique.mkv");
                File.WriteAllBytes(a, octets);
                File.WriteAllBytes(b, octets);
                var x = (byte[])octets.Clone(); x[x.Length - 1] ^= 0xFF; File.WriteAllBytes(fin, x);
                // Le pas entre extraits est d'environ 205 Ko : l'octet 100 000 n'est lu par aucun extrait.
                var y = (byte[])octets.Clone(); y[100000] ^= 0xFF; File.WriteAllBytes(milieu, y);
                bool lienOk = CreateHardLinkW(lien, a, IntPtr.Zero);

                var copies = new List<Doublons.Copie>();
                foreach (string p in new[] { a, b, fin, milieu, lien })
                {
                    if (!File.Exists(p)) continue;
                    var fi = new FileInfo(p);
                    copies.Add(new Doublons.Copie { Path = p, Dir = dir, Name = fi.Name, Bytes = fi.Length, Modified = fi.LastWriteTime });
                }
                List<Doublons.Groupe> g = Doublons.Cherche(copies);

                Banc.Egal("un seul groupe de doublons", "1", g.Count.ToString());
                var noms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (g.Count > 0) foreach (Doublons.Copie c in g[0].Copies) noms.Add(c.Name);
                Banc.Verifie("même contenu, noms différents : reconnus", true, noms.Contains("sauvegarde_02.bin"));
                Banc.Verifie("dernier octet différent : écarté dès les extraits", false, noms.Contains("fin_differente.mkv"));
                Banc.Verifie("lien physique créé pour le test", true, lienOk);
                Banc.Verifie("lien physique : même fichier, pas une copie", true,
                    !(noms.Contains("film.mkv") && noms.Contains("lien_physique.mkv")));
                Banc.Verifie("deux exemplaires réels seulement", true, g.Count > 0 && g[0].Copies.Count == 2 + (noms.Contains("milieu_different.mkv") ? 1 : 0));

                Banc.Verifie("vérification complète : vraie copie = identique", true, Doublons.Identiques(a, b, null));
                Banc.Verifie("vérification complète : écart entre deux extraits détecté", false, Doublons.Identiques(a, milieu, null));
                Banc.Verifie("vérification complète : un lien physique n'est jamais « identique »", false, lienOk && Doublons.Identiques(a, lien, null));
                Banc.Verifie("vérification complète : même chemin refusé", false, Doublons.Identiques(a, a, null));
                Banc.Verifie("vérification complète : fichier absent refusé", false, Doublons.Identiques(a, Path.Combine(dir, "absent.mkv"), null));

                // Groupe fait seulement de fichiers de jeu : rien à proposer.
                var refs = new List<Doublons.Copie>
                {
                    new Doublons.Copie { Path = a, Name = "film.mkv", Bytes = octets.Length, Reference = true },
                    new Doublons.Copie { Path = b, Name = "sauvegarde_02.bin", Bytes = octets.Length, Reference = true },
                };
                Banc.Egal("que des fichiers de jeu : aucun groupe", "0", Doublons.Cherche(refs).Count.ToString());

                // Chemin de jeu et chemin ordinaire vers le MÊME fichier : protégé, pas de doublon.
                if (lienOk)
                {
                    var alias = new List<Doublons.Copie>
                    {
                        new Doublons.Copie { Path = a, Name = "film.mkv", Bytes = octets.Length },
                        new Doublons.Copie { Path = lien, Name = "lien_physique.mkv", Bytes = octets.Length, Reference = true },
                    };
                    Banc.Egal("deux chemins vers un fichier de jeu : rien proposé", "0", Doublons.Cherche(alias).Count.ToString());
                }

                // Un dossier de logiciel copié en entier : aucun fichier proposé, le dossier signalé.
                var arbre = new List<Doublons.Copie>();
                foreach (string racine in new[] { Path.Combine(dir, "F", "Moteur"), Path.Combine(dir, "E", "eg", "Moteur") })
                {
                    string lib = Path.Combine(racine, "Engine", "lib");
                    Directory.CreateDirectory(lib);
                    string p = Path.Combine(lib, "gros.so");
                    File.WriteAllBytes(p, octets);
                    arbre.Add(new Doublons.Copie { Path = p, Dir = lib, Name = "gros.so", Bytes = octets.Length });
                    string q = Path.Combine(racine, "Donnees", "monde.dat");
                    Directory.CreateDirectory(Path.GetDirectoryName(q));
                    File.WriteAllBytes(q, x);
                    arbre.Add(new Doublons.Copie { Path = q, Dir = Path.GetDirectoryName(q), Name = "monde.dat", Bytes = x.Length });
                }
                Doublons.Resultat r = Doublons.Analyse(arbre);
                Banc.Egal("moteur copié : aucun fichier proposé", "0", r.Groupes.Count.ToString());
                Banc.Verifie("moteur copié : le dossier est signalé une fois", true,
                    r.Dossiers.Count == 1 && r.Dossiers[0].Fichiers == 2
                    && r.Dossiers[0].A.EndsWith(@"Moteur", StringComparison.OrdinalIgnoreCase)
                    && r.Dossiers[0].B.EndsWith(@"Moteur", StringComparison.OrdinalIgnoreCase));

                // Un seul petit fichier rangé pareil dans deux logiciels (composant partagé) :
                // ni proposé, ni signalé comme dossier en double.
                var partage = new List<Doublons.Copie>();
                foreach (string racine in new[] { Path.Combine(dir, "JeuA"), Path.Combine(dir, "JeuB") })
                {
                    string p = Path.Combine(racine, "Utils", "commun.bin");
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    File.WriteAllBytes(p, octets);
                    partage.Add(new Doublons.Copie { Path = p, Dir = Path.GetDirectoryName(p), Name = "commun.bin", Bytes = octets.Length });
                }
                r = Doublons.Analyse(partage);
                Banc.Egal("composant partagé : aucun fichier proposé", "0", r.Groupes.Count.ToString());
                Banc.Egal("composant partagé : pas de dossier en double", "0", r.Dossiers.Count.ToString());

                // Deux DLL identiques rangées n'importe où : jamais proposées.
                var dll = new List<Doublons.Copie>
                {
                    new Doublons.Copie { Path = a, Name = "film.mkv", Bytes = octets.Length, Nature = "Bibliothèque de programme" },
                    new Doublons.Copie { Path = b, Name = "sauvegarde_02.bin", Bytes = octets.Length, Nature = "Bibliothèque de programme" },
                };
                Banc.Egal("bibliothèques de programme : rien proposé", "0", Doublons.Cherche(dll).Count.ToString());
                Banc.Verifie("nature : données de jeu = logiciel", true, Doublons.NatureDeLogiciel("Données de jeu"));
                Banc.Verifie("nature : vidéo ≠ logiciel", false, Doublons.NatureDeLogiciel("Vidéo"));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
