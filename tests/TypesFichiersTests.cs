using System;
using System.IO;
using System.Text;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Détection des types de fichiers (page Stockage) : par l'extension quand elle est fiable,
    /// par le contenu quand elle manque, ment ou reste ambiguë.
    /// </summary>
    internal static class TypesFichiersTests
    {
        public static void Tout()
        {
            Banc.Titre("Types de fichiers (stockage)");

            // --- extensions ---
            Ext(".mkv", "videos", "Vidéo");
            Ext("FLAC", "audio", "Audio");
            Ext(".cr3", "images", "Photo RAW");
            Ext(".psd", "images", "Projet graphique");
            Ext(".vhdx", "archives", "Disque de machine virtuelle");
            Ext(".iso", "archives", "Image disque");
            Ext(".msi", "installers", "Installeur");
            Ext(".gguf", "bigfiles", "Modèle d'IA");
            Ext(".dmp", "bigfiles", "Vidage mémoire");
            Banc.Verifie("extension inconnue : non classée", false, TypesFichiers.ParExtension(".zzzz").Connu);
            Banc.Verifie("« .ts » ambigu : on lit le contenu", true, TypesFichiers.DoitSonder(".ts"));
            Banc.Verifie("« .mkv » fiable : pas de lecture", false, TypesFichiers.DoitSonder(".mkv"));
            Banc.Verifie("sans extension : on lit le contenu", true, TypesFichiers.DoitSonder(""));

            // --- signatures ---
            Sig("MP4 (ftyp isom)", Octets(0, 0, 0, 0x20).Concat("ftypisom"), "videos");
            Sig("M4A (ftyp M4A)", Octets(0, 0, 0, 0x20).Concat("ftypM4A "), "audio");
            Sig("HEIC (ftyp heic)", Octets(0, 0, 0, 0x18).Concat("ftypheic"), "images");
            Sig("Matroska", Octets(0x1A, 0x45, 0xDF, 0xA3), "videos");
            Sig("AVI", Ascii("RIFF").Concat(Octets(0, 0, 0, 0)).Concat("AVI "), "videos");
            Sig("WAV", Ascii("RIFF").Concat(Octets(0, 0, 0, 0)).Concat("WAVE"), "audio");
            Sig("MP3 (ID3)", Ascii("ID3"), "audio");
            Sig("JPEG", Octets(0xFF, 0xD8, 0xFF, 0xE0), "images");
            Sig("PNG", Octets(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A), "images");
            Sig("7z", Octets(0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C), "archives");
            Sig("RAR", Ascii("Rar!").Concat(Octets(0x1A, 0x07, 0x00)), "archives");
            Sig("VHDX", Ascii("vhdxfile"), "archives");
            Sig("exécutable PE", Ascii("MZ").Concat(Octets(0x90, 0)), "installers");
            Sig("DLL (drapeau PE 0x2000)", Pe(0x2022), "bigfiles");
            Sig("EXE (sans drapeau DLL)", Pe(0x0022), "installers");
            Ext(".dll", "bigfiles", "Bibliothèque de programme");
            Ext(".pck", "bigfiles", "Données de jeu");
            Banc.Egal("extension numérique = fragment", "Fragment numéroté (archive découpée ou données d'appli)", TypesFichiers.Inconnu(".155").Nature);
            Sig("PDF", Ascii("%PDF-1.7"), "bigfiles");
            Sig("SQLite", Ascii("SQLite format 3").Concat(Octets(0)), "bigfiles");
            Banc.Verifie("octets quelconques : non reconnus", false, TypesFichiers.Signature(Ascii("bonjour le monde"), null).Connu);

            var ts = new byte[400];
            ts[0] = ts[188] = ts[376] = 0x47;
            Sig("MPEG-TS (synchro tous les 188 octets)", ts, "videos");
            Banc.Egal("ZIP d'un .docx = document, pas archive", "bigfiles",
                TypesFichiers.Signature(Zip("[Content_Types].xml"), null).Cat);
            Banc.Egal("ZIP d'un .apk = installeur", "installers",
                TypesFichiers.Signature(Zip("AndroidManifest.xml"), null).Cat);
            Banc.Egal("ZIP ordinaire = archive", "archives", TypesFichiers.Signature(Zip("photos/a.jpg"), null).Cat);
            Banc.Egal("ISO 9660 (CD001 à 32 769)", "archives", TypesFichiers.Signature(new byte[512], Ascii("CD001")).Cat);

            // --- sur disque : extension absente ou trompeuse ---
            string dir = Path.Combine(Path.GetTempPath(), "onyx-types-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                string sansExt = Path.Combine(dir, "video_sans_extension");
                File.WriteAllBytes(sansExt, Octets(0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x02));
                TypesFichiers.Type t = TypesFichiers.Detecte(sansExt, "");
                Banc.Egal("fichier sans extension reconnu par son contenu (" + t.Nature + ")", "videos", t.Cat);

                string tsCode = Path.Combine(dir, "app.ts");
                File.WriteAllText(tsCode, "export const x: number = 1;\n");
                t = TypesFichiers.Detecte(tsCode, ".ts");
                Banc.Egal("« .ts » TypeScript ≠ vidéo (" + t.Nature + ")", "bigfiles", t.Cat);

                string iso = Path.Combine(dir, "disque.bin");
                var buf = new byte[40000];
                Array.Copy(Ascii("CD001"), 0, buf, 32769, 5);
                File.WriteAllBytes(iso, buf);
                t = TypesFichiers.Detecte(iso, ".bin");
                Banc.Egal("« .bin » qui est une ISO (" + t.Nature + ")", "Image disque", t.Nature);

                string inconnu = Path.Combine(dir, "export.qqq");
                File.WriteAllText(inconnu, "rien de connu ici, juste du texte");
                t = TypesFichiers.Detecte(inconnu, ".qqq");
                Banc.Verifie("extension inconnue, contenu inconnu : « Fichier QQQ » dans Autres", true,
                    t.Cat == "bigfiles" && t.Nature == "Fichier QQQ");

                t = TypesFichiers.Detecte(Path.Combine(dir, "absent.bin"), ".bin");
                Banc.Verifie("fichier illisible : pas d'exception, classé quand même", true, t.Connu);

                using (var fs = new FileStream(sansExt, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                {
                    // Ouvert ailleurs en écriture (partage lecture seule) : la sonde ne doit pas planter.
                    TypesFichiers.Type ouvert = TypesFichiers.Detecte(sansExt, "");
                    Banc.Verifie("fichier ouvert ailleurs : pas d'exception", true, ouvert.Connu);
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static void Ext(string ext, string cat, string nature)
        {
            TypesFichiers.Type t = TypesFichiers.ParExtension(ext);
            Banc.Verifie("extension " + ext + " → " + nature, true, t.Cat == cat && t.Nature == nature);
        }

        private static void Sig(string nom, byte[] b, string cat)
        {
            Banc.Egal("signature " + nom, cat, TypesFichiers.Signature(b, null).Cat);
        }

        private static byte[] Octets(params int[] v)
        {
            var b = new byte[v.Length];
            for (int i = 0; i < v.Length; i++) b[i] = (byte)v[i];
            return b;
        }

        private static byte[] Ascii(string s) { return Encoding.ASCII.GetBytes(s); }

        private static byte[] Concat(this byte[] a, string s) { return a.Concat(Ascii(s)); }

        private static byte[] Concat(this byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            Array.Copy(a, r, a.Length);
            Array.Copy(b, 0, r, a.Length, b.Length);
            return r;
        }

        // MZ + en-tête PE à 0x80 avec le champ Characteristics demandé.
        private static byte[] Pe(int caracteristiques)
        {
            var b = new byte[0x80 + 24];
            b[0] = (byte)'M'; b[1] = (byte)'Z'; b[0x3C] = 0x80;
            b[0x80] = (byte)'P'; b[0x81] = (byte)'E';
            b[0x80 + 22] = (byte)(caracteristiques & 0xFF); b[0x80 + 23] = (byte)(caracteristiques >> 8);
            return b;
        }

        // En-tête local ZIP minimal : signature, champs à zéro, longueur du nom (octets 26-27), nom.
        private static byte[] Zip(string premierNom)
        {
            var b = new byte[30 + premierNom.Length];
            b[0] = 0x50; b[1] = 0x4B; b[2] = 0x03; b[3] = 0x04;
            b[26] = (byte)premierNom.Length;
            Array.Copy(Ascii(premierNom), 0, b, 30, premierNom.Length);
            return b;
        }
    }
}
