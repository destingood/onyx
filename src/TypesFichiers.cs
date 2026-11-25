using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// RECONNAÎTRE N'IMPORTE QUEL FICHIER — pour la page Stockage. Deux étages :
    ///   1. l'extension, sur une table large (vidéo, audio, photo, archive, image disque, machine
    ///      virtuelle, installeur, document, sauvegarde, base de données, 3D, projet…) ;
    ///   2. le CONTENU (signature des premiers octets) quand l'extension est absente, inconnue ou
    ///      ambiguë (« .ts » = vidéo MPEG-TS ou code TypeScript, « .bin », « .dat », « .img »…).
    /// La lecture se limite à 512 octets (plus 6 octets à 32 Ko pour les ISO) et n'est faite que
    /// pour les gros fichiers déjà retenus : quelques centaines au plus, jamais tout le disque.
    /// </summary>
    internal static class TypesFichiers
    {
        /// <summary>Catégorie = Id du module de la page Stockage ; Nature = libellé précis.</summary>
        public struct Type
        {
            public string Cat, Nature;
            public Type(string cat, string nature) { Cat = cat; Nature = nature; }
            public bool Connu { get { return Cat != null; } }
        }

        public const string Videos = "videos", Audio = "audio", Images = "images", Archives = "archives",
                            Installeurs = "installers", Autres = "bigfiles";

        private static readonly Dictionary<string, Type> ParExt = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        // Extensions dont le nom ne dit rien de sûr : on lit le contenu pour trancher.
        private static readonly HashSet<string> Ambigues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "", ".ts", ".bin", ".dat", ".img", ".raw", ".tmp", ".part", ".crdownload", ".download",
            ".partial", ".!ut", ".bak", ".old", ".000", ".001", ".data", ".blob", ".file", ".out"
        };

        static TypesFichiers()
        {
            Ajoute(Videos, "Vidéo", ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v", ".mpg", ".mpeg",
                ".m2ts", ".mts", ".vob", ".3gp", ".3g2", ".ogv", ".divx", ".xvid", ".asf", ".rm", ".rmvb", ".f4v",
                ".mxf", ".dv", ".m2v", ".mpv", ".h264", ".h265", ".hevc", ".264", ".265", ".y4m", ".braw", ".r3d");
            Ajoute(Videos, "Projet vidéo", ".prproj", ".aep", ".veg", ".drp", ".wlmp", ".kdenlive", ".mlt", ".fcpbundle");
            Ajoute(Videos, "Rediffusion / capture", ".dem", ".replay", ".rofl", ".obs");

            Ajoute(Audio, "Audio", ".mp3", ".flac", ".wav", ".aac", ".m4a", ".ogg", ".oga", ".opus", ".wma", ".aiff",
                ".aif", ".alac", ".ape", ".wv", ".dsf", ".dff", ".mka", ".ac3", ".dts", ".amr", ".mid", ".midi", ".caf");
            Ajoute(Audio, "Projet audio", ".als", ".flp", ".logicx", ".ptx", ".cpr", ".rpp", ".aup", ".aup3", ".sesx");
            Ajoute(Audio, "Banque de sons", ".nki", ".nkx", ".nkc", ".ncw", ".sf2", ".sfz", ".exs");

            Ajoute(Images, "Photo / image", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic",
                ".heif", ".avif", ".jxl", ".ico", ".tga", ".dds", ".exr", ".hdr", ".jp2");
            Ajoute(Images, "Photo RAW", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2", ".raf", ".pef", ".srw", ".x3f", ".3fr", ".iiq");
            Ajoute(Images, "Projet graphique", ".psd", ".psb", ".ai", ".xcf", ".kra", ".clip", ".afphoto", ".afdesign",
                ".sketch", ".fig", ".indd", ".cdr", ".svg", ".eps");

            Ajoute(Archives, "Archive", ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".tgz", ".tbz2", ".txz",
                ".zst", ".lz", ".lzma", ".lz4", ".cab", ".arj", ".z", ".zipx", ".sitx", ".r00", ".r01", ".z01");
            Ajoute(Archives, "Image disque", ".iso", ".img", ".nrg", ".mdf", ".mds", ".cue", ".ccd", ".toast", ".dmg", ".wim", ".esd", ".swm");
            Ajoute(Archives, "Disque de machine virtuelle", ".vhd", ".vhdx", ".vmdk", ".vdi", ".qcow", ".qcow2",
                ".avhd", ".avhdx", ".vmem", ".vmsn", ".vsv", ".vmrs", ".vmcx", ".vmgs", ".hds", ".ova", ".ovf");
            Ajoute(Archives, "Sauvegarde", ".tib", ".tibx", ".mrimg", ".mrbak", ".gho", ".adi", ".pbd", ".bkf", ".vbk", ".vib", ".spf");

            Ajoute(Installeurs, "Installeur", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".msp", ".msu", ".pkg", ".deb", ".rpm", ".apk", ".xapk", ".aab", ".ipa");
            Ajoute(Installeurs, "Programme", ".exe", ".com", ".scr");
            // Les bibliothèques ont la même signature « MZ » qu'un .exe, mais une appli en dépend :
            // elles ne doivent JAMAIS atterrir parmi les installeurs à jeter.
            Ajoute(Autres, "Bibliothèque de programme", ".dll", ".sys", ".ocx", ".drv", ".so", ".dylib", ".pyd", ".node",
                ".lib", ".a", ".jar", ".winmd", ".mui");
            Ajoute(Autres, "Fichier de développement", ".pdb", ".pch", ".ipch", ".ilk", ".idb", ".exp", ".tlog", ".vc.db", ".pack", ".idx");

            Ajoute(Autres, "Document", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp",
                ".epub", ".mobi", ".azw3", ".djvu", ".cbz", ".cbr", ".pages", ".key", ".numbers", ".rtf", ".xps");
            Ajoute(Autres, "Texte / journal", ".txt", ".log", ".csv", ".tsv", ".json", ".xml", ".jsonl", ".ndjson", ".sql", ".etl", ".evtx");
            Ajoute(Autres, "Base de données", ".db", ".sqlite", ".sqlite3", ".mdb", ".accdb", ".mdf", ".ldf", ".ndf", ".frm", ".ibd", ".kdbx", ".pst", ".ost", ".nsf", ".edb");
            Ajoute(Autres, "Modèle 3D", ".blend", ".fbx", ".obj", ".stl", ".3mf", ".max", ".ma", ".mb", ".c4d", ".usd",
                ".usdz", ".gltf", ".glb", ".abc", ".ztl", ".spp", ".uasset", ".umap");
            Ajoute(Autres, "Modèle d'IA", ".gguf", ".ggml", ".safetensors", ".ckpt", ".pt", ".pth", ".onnx", ".pb", ".tflite", ".h5", ".npz", ".npy", ".parquet", ".arrow");
            Ajoute(Autres, "Vidage mémoire", ".dmp", ".mdmp", ".hdmp", ".core");
            Ajoute(Autres, "Données de jeu", ".pak", ".ucas", ".utoc", ".ubulk", ".uexp", ".vpk", ".bsa", ".ba2", ".forge",
                ".rpf", ".wad", ".big", ".esm", ".esp", ".sav", ".pck", ".usm", ".cpk", ".awb", ".acb", ".ipak", ".xpak",
                ".sabs", ".sabl", ".cas", ".archive", ".bundle", ".assets", ".ress", ".resource", ".rpkg", ".bnk", ".wem",
                ".blk", ".umeta", ".gcf", ".ncf", ".dcp", ".arc", ".bdt", ".bhd", ".xxx", ".pkg2", ".ff", ".fsb", ".bik", ".bk2");
            Ajoute(Autres, "Police", ".ttf", ".otf", ".ttc", ".woff", ".woff2");
            Ajoute(Autres, "Torrent / téléchargement inachevé", ".torrent", ".!qb", ".!ut", ".crdownload", ".part", ".partial", ".download");
            Ajoute(Autres, "Copie de sauvegarde", ".bak", ".old", ".backup", ".bkp");
        }

        private static void Ajoute(string cat, string nature, params string[] exts)
        {
            foreach (string e in exts) if (!ParExt.ContainsKey(e)) ParExt[e] = new Type(cat, nature);
        }

        /// <summary>Type d'après l'extension seule (« .mkv » ou « mkv »). Non connu si absente de la table.</summary>
        public static Type ParExtension(string ext)
        {
            ext = Norm(ext);
            Type t;
            return ParExt.TryGetValue(ext, out t) ? t : default(Type);
        }

        /// <summary>Faut-il lire le contenu pour trancher ?</summary>
        public static bool DoitSonder(string ext)
        {
            ext = Norm(ext);
            return Ambigues.Contains(ext) || !ParExt.ContainsKey(ext);
        }

        /// <summary>Détection complète d'un fichier : l'extension quand elle est fiable, sinon le
        /// contenu, sinon l'extension faute de mieux, sinon « Autre ». Ne lève jamais.</summary>
        public static Type Detecte(string chemin, string ext)
        {
            Type parNom = ParExtension(ext);
            if (!DoitSonder(ext)) return parNom;
            Type parContenu = Sonde(chemin);
            if (parContenu.Connu) return parContenu;
            if (parNom.Connu) return parNom;
            return Inconnu(ext);
        }

        /// <summary>Lit l'en-tête du fichier (partage complet : un fichier ouvert ailleurs reste lisible).</summary>
        public static Type Sonde(string chemin)
        {
            try
            {
                if (string.IsNullOrEmpty(chemin)) return default(Type);
                using (var fs = new FileStream(chemin, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.None))
                {
                    var tete = new byte[512];
                    int n = Lire(fs, tete);
                    byte[] iso = null;
                    if (fs.Length > 32774)
                    {
                        fs.Seek(32769, SeekOrigin.Begin);
                        iso = new byte[5];
                        if (Lire(fs, iso) < 5) iso = null;
                    }
                    if (n < tete.Length) Array.Resize(ref tete, n);
                    return Signature(tete, iso);
                }
            }
            catch { return default(Type); }
        }

        private static int Lire(Stream s, byte[] buf)
        {
            int total = 0;
            while (total < buf.Length)
            {
                int r = s.Read(buf, total, buf.Length - total);
                if (r <= 0) break;
                total += r;
            }
            return total;
        }

        /// <summary>Signature des premiers octets. <paramref name="iso"/> = 5 octets lus à 32 769
        /// (« CD001 » d'une image ISO 9660), ou null.</summary>
        public static Type Signature(byte[] b, byte[] iso)
        {
            if (b == null) b = new byte[0];

            // --- conteneurs ISO BMFF (mp4, mov, m4a, heic, avif…) : « ftyp » à l'octet 4 ---
            if (A(b, 4, "ftyp") && b.Length >= 12)
            {
                string marque = Encoding.ASCII.GetString(b, 8, 4).ToLowerInvariant();
                if (marque.StartsWith("m4a") || marque.StartsWith("m4b") || marque.StartsWith("m4p")) return new Type(Audio, "Audio");
                if (marque == "heic" || marque == "heix" || marque == "mif1" || marque == "msf1" || marque == "avif" || marque == "heim" || marque == "hevc")
                    return new Type(Images, "Photo / image");
                if (marque.StartsWith("crx")) return new Type(Images, "Photo RAW");
                return new Type(Videos, "Vidéo");
            }
            if (A(b, 0, 0x1A, 0x45, 0xDF, 0xA3)) return new Type(Videos, "Vidéo");                      // Matroska / WebM
            if (A(b, 0, "RIFF") && b.Length >= 12)
            {
                if (A(b, 8, "AVI ")) return new Type(Videos, "Vidéo");
                if (A(b, 8, "WAVE")) return new Type(Audio, "Audio");
                if (A(b, 8, "WEBP")) return new Type(Images, "Photo / image");
            }
            if (A(b, 0, "FLV")) return new Type(Videos, "Vidéo");
            if (A(b, 0, 0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11)) return new Type(Videos, "Vidéo");   // ASF / WMV
            if (A(b, 0, 0x00, 0x00, 0x01, 0xBA) || A(b, 0, 0x00, 0x00, 0x01, 0xB3)) return new Type(Videos, "Vidéo");   // MPEG-PS
            if (b.Length > 376 && b[0] == 0x47 && b[188] == 0x47 && b[376] == 0x47) return new Type(Videos, "Vidéo");  // MPEG-TS
            if (b.Length > 196 && b[4] == 0x47 && b[196] == 0x47) return new Type(Videos, "Vidéo");                    // M2TS (192 o)

            if (A(b, 0, "ID3") || (b.Length > 1 && b[0] == 0xFF && (b[1] & 0xE0) == 0xE0 && (b[1] & 0x06) != 0))
                return new Type(Audio, "Audio");
            if (A(b, 0, "fLaC") || A(b, 0, "MThd") || A(b, 0, "FORM") || A(b, 0, "wvpk") || A(b, 0, "MAC ")) return new Type(Audio, "Audio");
            if (A(b, 0, "OggS")) return Cherche(b, "theora") ? new Type(Videos, "Vidéo") : new Type(Audio, "Audio");

            if (A(b, 0, 0xFF, 0xD8, 0xFF)) return new Type(Images, "Photo / image");
            if (A(b, 0, 0x89, (byte)'P', (byte)'N', (byte)'G')) return new Type(Images, "Photo / image");
            if (A(b, 0, "GIF8")) return new Type(Images, "Photo / image");
            if (A(b, 0, "8BPS")) return new Type(Images, "Projet graphique");
            if (A(b, 0, "II*") || A(b, 0, 0x4D, 0x4D, 0x00, 0x2A)) return new Type(Images, "Photo / image");   // TIFF (et la plupart des RAW)
            if (A(b, 0, "BM") && b.Length >= 14 && b[6] == 0 && b[7] == 0 && b[8] == 0 && b[9] == 0) return new Type(Images, "Photo / image");
            if (A(b, 0, 0x76, 0x2F, 0x31, 0x01)) return new Type(Images, "Photo / image");   // OpenEXR

            if (A(b, 0, "PK", 0x03, 0x04) || A(b, 0, "PK", 0x05, 0x06)) return Zip(b);
            if (A(b, 0, "Rar!", 0x1A, 0x07)) return new Type(Archives, "Archive");
            if (A(b, 0, 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C)) return new Type(Archives, "Archive");      // 7z
            if (A(b, 0, 0x1F, 0x8B)) return new Type(Archives, "Archive");                              // gzip
            if (A(b, 0, "BZh")) return new Type(Archives, "Archive");
            if (A(b, 0, 0xFD, (byte)'7', (byte)'z', (byte)'X', (byte)'Z', 0x00)) return new Type(Archives, "Archive");
            if (A(b, 0, 0x28, 0xB5, 0x2F, 0xFD)) return new Type(Archives, "Archive");                  // zstd
            if (A(b, 0, 0x04, 0x22, 0x4D, 0x18)) return new Type(Archives, "Archive");                  // lz4
            if (A(b, 0, "MSCF")) return new Type(Archives, "Archive");                                  // cab
            if (A(b, 257, "ustar")) return new Type(Archives, "Archive");                               // tar
            if (A(b, 0, "MSWIM")) return new Type(Archives, "Image disque");
            if (iso != null && A(iso, 0, "CD001")) return new Type(Archives, "Image disque");
            if (A(b, 0, "conectix") || A(b, 0, "vhdxfile") || A(b, 0, "KDMV") || A(b, 0, "QFI", 0xFB)
                || A(b, 0, "# Disk DescriptorFile") || A(b, 64, 0x7F, 0x10, 0xDA, 0xBE))
                return new Type(Archives, "Disque de machine virtuelle");

            if (A(b, 0, "MZ")) return Pe(b);
            if (A(b, 0, 0x7F, (byte)'E', (byte)'L', (byte)'F')) return new Type(Autres, "Bibliothèque de programme");
            if (A(b, 0, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1)) return new Type(Autres, "Document Office ou installeur MSI");
            if (A(b, 0, "%PDF")) return new Type(Autres, "Document");
            if (A(b, 0, "SQLite format 3")) return new Type(Autres, "Base de données");
            if (A(b, 0, "!BDN")) return new Type(Autres, "Base de données");                            // pst / ost
            if (A(b, 0, "MDMP") || A(b, 0, "PAGEDU64") || A(b, 0, "PAGEDUMP")) return new Type(Autres, "Vidage mémoire");
            if (A(b, 0, "GGUF")) return new Type(Autres, "Modèle d'IA");
            if (A(b, 0, "BLENDER")) return new Type(Autres, "Modèle 3D");
            if (A(b, 0, "Kaydara FBX")) return new Type(Autres, "Modèle 3D");
            if (A(b, 0, "d8:announce") || A(b, 0, "d4:info")) return new Type(Autres, "Torrent / téléchargement inachevé");
            // Début entièrement à zéro : réservé d'avance (jeu, téléchargement) — on ne devine pas plus.
            if (b.Length >= 64 && ToutZero(b)) return new Type(Autres, "Fichier préalloué (début vide)");
            return default(Type);
        }

        // « MZ » = exécutable OU bibliothèque : l'en-tête PE (offset à 0x3C) porte le drapeau DLL
        // (0x2000 dans Characteristics). Une DLL n'est jamais un installeur.
        private static Type Pe(byte[] b)
        {
            if (b.Length >= 0x40)
            {
                int pe = b[0x3C] | (b[0x3D] << 8) | (b[0x3E] << 16) | (b[0x3F] << 24);
                if (pe > 0 && pe + 24 <= b.Length && A(b, pe, "PE", 0, 0))
                {
                    int car = b[pe + 22] | (b[pe + 23] << 8);
                    if ((car & 0x2000) != 0) return new Type(Autres, "Bibliothèque de programme");
                }
            }
            return new Type(Installeurs, "Programme");
        }

        // Un ZIP peut être une archive… ou un document Office, un paquet d'appli, un mod. On lit le
        // nom de la première entrée (en-tête local, octet 30).
        private static Type Zip(byte[] b)
        {
            string nom = "";
            if (b.Length >= 30)
            {
                int len = b[26] | (b[27] << 8);
                if (len > 0 && 30 + len <= b.Length) nom = Encoding.ASCII.GetString(b, 30, len);
            }
            if (nom == "mimetype" && Cherche(b, "epub")) return new Type(Autres, "Document");
            if (nom == "mimetype") return new Type(Autres, "Document");                                  // OpenDocument
            if (nom.StartsWith("[Content_Types].xml") || nom.StartsWith("word/") || nom.StartsWith("xl/") || nom.StartsWith("ppt/"))
                return new Type(Autres, "Document");
            if (nom.StartsWith("AndroidManifest.xml") || nom.StartsWith("META-INF/") || nom.StartsWith("classes.dex"))
                return new Type(Installeurs, "Installeur");
            if (nom.StartsWith("AppxManifest.xml") || nom.StartsWith("AppxMetadata/") || nom.StartsWith("AppxBlockMap.xml"))
                return new Type(Installeurs, "Installeur");
            return new Type(Archives, "Archive");
        }

        private static bool A(byte[] b, int at, params object[] parts)
        {
            int p = at;
            foreach (object o in parts)
            {
                if (o is string)
                {
                    foreach (char c in (string)o) { if (p >= b.Length || b[p] != (byte)c) return false; p++; }
                }
                else
                {
                    byte v = Convert.ToByte(o);
                    if (p >= b.Length || b[p] != v) return false;
                    p++;
                }
            }
            return true;
        }

        private static bool Cherche(byte[] b, string mot)
        {
            return Encoding.ASCII.GetString(b).IndexOf(mot, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ToutZero(byte[] b)
        {
            foreach (byte x in b) if (x != 0) return false;
            return true;
        }

        /// <summary>Libellé honnête d'un fichier non identifié. Extension numérique (« .001 »,
        /// « .155 ») = morceau d'un ensemble : archive découpée ou données d'application.</summary>
        public static Type Inconnu(string ext)
        {
            string e = Norm(ext);
            if (e.Length <= 1) return new Type(Autres, "Sans extension");
            bool chiffres = true;
            for (int i = 1; i < e.Length; i++) if (e[i] < '0' || e[i] > '9') { chiffres = false; break; }
            if (chiffres) return new Type(Autres, "Fragment numéroté (archive découpée ou données d'appli)");
            return new Type(Autres, "Fichier " + e.Substring(1).ToUpperInvariant());
        }

        private static string Norm(string ext)
        {
            ext = (ext ?? "").Trim();
            if (ext.Length > 0 && ext[0] != '.') ext = "." + ext;
            return ext.ToLowerInvariant();
        }
    }
}
