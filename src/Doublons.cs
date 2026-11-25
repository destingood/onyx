using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace BTOptimizer
{
    /// <summary>
    /// Fichiers et jeux en double (page Stockage).
    ///
    /// Fichiers : même taille à l'octet près, puis même empreinte (16 extraits de 64 Ko répartis
    /// du début à la fin). Le NOM ne compte jamais : « film.mkv » et « sauvegarde_02.bin » au
    /// contenu identique sont des doublons, deux « setup.exe » différents n'en sont pas.
    /// Avant toute suppression, la copie est comparée OCTET PAR OCTET à l'exemplaire gardé
    /// (<see cref="Identiques"/>) : la moindre différence l'épargne.
    ///
    /// Deux chemins vers le même fichier physique (lien physique, lecteur subst) ne sont pas des
    /// doublons : en effacer un ne libère rien. Les fichiers d'un jeu installé servent de
    /// RÉFÉRENCE : ils permettent de reconnaître une copie faite ailleurs, mais ne sont jamais
    /// proposés (supprimer un fichier dans un jeu le casse).
    ///
    /// Jeux : même nom (normalisé) installé dans deux dossiers distincts.
    /// </summary>
    internal static class Doublons
    {
        internal sealed class Copie
        {
            public string Path, Dir, Name, Nature;
            public long Bytes;
            public DateTime Modified;      // MinValue = inconnue
            public bool Reference;         // fichier d'un jeu, ou écarté par l'utilisateur : toujours gardé
            public bool Protege;           // fait partie d'un logiciel ou d'un cache (voir Logiciel) : toujours gardé
            public string Origine;         // nom du jeu auquel appartient une copie de référence
            public bool Garder;            // décidé par Choisir
        }

        /// <summary>Deux dossiers dont les gros fichiers sont les mêmes, rangés pareil : une copie
        /// entière (moteur, jeu sauvegardé, réserve de modèles). Montré, jamais supprimé fichier
        /// par fichier : c'est le dossier entier qui est en trop.</summary>
        internal sealed class DossierDouble
        {
            public string A, B;
            public int Fichiers;
            public long Bytes;             // octets en double (un exemplaire de chaque fichier commun)
        }

        internal sealed class Resultat
        {
            public readonly List<Groupe> Groupes = new List<Groupe>();
            public readonly List<DossierDouble> Dossiers = new List<DossierDouble>();
        }

        internal sealed class Groupe
        {
            public readonly List<Copie> Copies = new List<Copie>();
            public long Bytes;             // taille d'UN exemplaire

            public Copie Gardee
            {
                get { foreach (Copie c in Copies) if (c.Garder) return c; return null; }
            }

            public int EnTrop
            {
                get { int n = 0; foreach (Copie c in Copies) if (!c.Garder) n++; return n; }
            }

            public long RecuperableBytes { get { return Bytes * EnTrop; } }
        }

        internal sealed class Install
        {
            public string Nom, Lanceur, Dossier;
            public long MB;
            public object Tag;
        }

        internal sealed class JeuDouble
        {
            public string Nom;
            public readonly List<Install> Installs = new List<Install>();

            /// <summary>Ce que libérerait la désinstallation de toutes les installations sauf la plus grosse.</summary>
            public long RecuperableMB
            {
                get
                {
                    long total = 0, max = 0;
                    foreach (Install i in Installs) { total += i.MB; if (i.MB > max) max = i.MB; }
                    return total - max;
                }
            }
        }

        private const int Extrait = 64 * 1024;
        private const int NbExtraits = 16;

        // ------------------------------------------------------------------ fichiers

        /// <summary>Regroupe les copies identiques. Lit au plus 16 × 64 Ko par fichier candidat
        /// (seuls ceux qui partagent leur taille exacte avec un autre sont lus).</summary>
        public static List<Groupe> Cherche(IEnumerable<Copie> copies)
        {
            return Analyse(copies).Groupes;
        }

        /// <summary>Comme <see cref="Cherche"/>, plus les dossiers copiés en entier.</summary>
        public static Resultat Analyse(IEnumerable<Copie> copies)
        {
            var res = new Resultat();
            var parTaille = new Dictionary<long, List<Copie>>();
            if (copies != null)
                foreach (Copie c in copies)
                {
                    if (c == null || c.Bytes <= 0 || string.IsNullOrEmpty(c.Path)) continue;
                    if (!c.Reference && (Logiciel(c.Path) || NatureDeLogiciel(c.Nature))) c.Protege = true;
                    List<Copie> l;
                    if (!parTaille.TryGetValue(c.Bytes, out l)) parTaille[c.Bytes] = l = new List<Copie>();
                    l.Add(c);
                }

            var candidats = new List<List<Copie>>();
            var aLire = new List<Copie>();
            foreach (List<Copie> l in parTaille.Values)
                if (l.Count >= 2 && UnProposable(l)) { candidats.Add(l); aLire.AddRange(l); }

            var ident = new ConcurrentDictionary<Copie, string>();
            var emp = new ConcurrentDictionary<Copie, string>();
            try
            {
                Parallel.ForEach(aLire, new ParallelOptions { MaxDegreeOfParallelism = 4 }, delegate (Copie c)
                {
                    ident[c] = Identite(c.Path) ?? "";
                    emp[c] = Empreinte(c.Path, c.Bytes) ?? "";
                });
            }
            catch { }

            List<Groupe> groupes = res.Groupes;
            var dossiers = new Dictionary<string, DossierDouble>(StringComparer.OrdinalIgnoreCase);
            foreach (List<Copie> l in candidats)
            {
                var physiques = new Dictionary<string, Copie>(StringComparer.Ordinal);
                var parEmp = new Dictionary<string, List<Copie>>(StringComparer.Ordinal);
                foreach (Copie c in l)
                {
                    string e, id;
                    if (!emp.TryGetValue(c, out e) || e.Length == 0) continue;   // illisible : jamais proposé
                    if (ident.TryGetValue(c, out id) && id.Length > 0)
                    {
                        Copie deja;
                        if (physiques.TryGetValue(id, out deja))
                        {
                            // Même fichier vu par deux chemins : un seul exemplaire, protégé si l'un des
                            // chemins appartient à un jeu.
                            if (c.Reference) { deja.Reference = true; if (deja.Origine == null) deja.Origine = c.Origine; }
                            continue;
                        }
                        physiques[id] = c;
                    }
                    List<Copie> g;
                    if (!parEmp.TryGetValue(e, out g)) parEmp[e] = g = new List<Copie>();
                    g.Add(c);
                }
                foreach (List<Copie> g in parEmp.Values)
                {
                    if (g.Count < 2) continue;
                    AjouteDossiers(g, dossiers);   // protège aussi les copies rangées dans deux arborescences
                    if (!UnProposable(g)) continue;
                    var gr = new Groupe { Bytes = g[0].Bytes };
                    gr.Copies.AddRange(g);
                    Choisir(gr);
                    if (gr.EnTrop > 0) groupes.Add(gr);
                }
            }
            groupes.Sort(delegate (Groupe a, Groupe b) { return b.RecuperableBytes.CompareTo(a.RecuperableBytes); });
            // Un seul petit fichier en commun, c'est souvent un composant partagé (moteur, navigateur
            // intégré) entre deux logiciels différents, pas une copie du dossier.
            foreach (DossierDouble d in dossiers.Values)
                if (d.Fichiers >= 2 || d.Bytes >= DossierSeul) res.Dossiers.Add(d);
            res.Dossiers.Sort(delegate (DossierDouble a, DossierDouble b) { return b.Bytes.CompareTo(a.Bytes); });
            return res;
        }

        // Candidat à la lecture : au moins un exemplaire hors des jeux installés (sinon rien à dire).
        private static bool UnProposable(List<Copie> l)
        {
            foreach (Copie c in l) if (!c.Reference) return true;
            return false;
        }

        // Seuil pour signaler un couple de dossiers qui n'a qu'un fichier en commun.
        private const long DossierSeul = 1L << 30;

        private static readonly HashSet<string> NaturesDeLogiciel = new HashSet<string>(StringComparer.Ordinal)
        {
            "Bibliothèque de programme", "Données de jeu", "Fichier de développement", "Police"
        };

        /// <summary>DLL, données de jeu, fichiers de développement : toujours le rouage d'un
        /// logiciel, jamais un document qu'on aurait copié soi-même.</summary>
        internal static bool NatureDeLogiciel(string nature)
        {
            return nature != null && NaturesDeLogiciel.Contains(nature);
        }

        /// <summary>Chaque paire de copies rangées au même chemin relatif (au moins dossier\fichier)
        /// sous deux racines différentes compte pour le couple de dossiers, et ces copies deviennent
        /// protégées : supprimer un fichier au milieu d'une arborescence la casse (un jeu installé
        /// deux fois, un moteur copié) — c'est le dossier entier qu'il faut regarder. Deux fichiers
        /// de jeux installés (références) ne font pas un couple : le contrôle par jeu s'en charge.</summary>
        private static void AjouteDossiers(List<Copie> g, Dictionary<string, DossierDouble> dossiers)
        {
            for (int i = 0; i < g.Count; i++)
                for (int j = i + 1; j < g.Count; j++)
                {
                    string a, b;
                    if (!Racines(g[i].Path, g[j].Path, out a, out b)) continue;
                    if (!g[i].Reference) g[i].Protege = true;
                    if (!g[j].Reference) g[j].Protege = true;
                    if (g[i].Reference && g[j].Reference) continue;
                    if (string.Compare(a, b, StringComparison.OrdinalIgnoreCase) > 0) { string t = a; a = b; b = t; }
                    string k = a + "|" + b;
                    DossierDouble d;
                    if (!dossiers.TryGetValue(k, out d)) dossiers[k] = d = new DossierDouble { A = a, B = b };
                    d.Fichiers++;
                    d.Bytes += g[i].Bytes;
                }
        }

        /// <summary>Racines de deux arborescences identiques : « F:\UE_5.8\Engine\lib\x.so » et
        /// « E:\eg\UE_5.8\Engine\lib\x.so » → « F:\UE_5.8 » et « E:\eg\UE_5.8 » (la racine inclut
        /// le premier dossier commun). Faux si moins de deux segments communs en fin de chemin.</summary>
        internal static bool Racines(string pa, string pb, out string ra, out string rb)
        {
            ra = rb = null;
            if (string.IsNullOrEmpty(pa) || string.IsNullOrEmpty(pb)) return false;
            string[] a = pa.TrimEnd('\\').Split('\\'), b = pb.TrimEnd('\\').Split('\\');
            int k = 0;
            while (k < a.Length - 1 && k < b.Length - 1
                   && string.Equals(a[a.Length - 1 - k], b[b.Length - 1 - k], StringComparison.OrdinalIgnoreCase)) k++;
            if (k < 2) return false;
            ra = string.Join("\\", a, 0, a.Length - k + 1);
            rb = string.Join("\\", b, 0, b.Length - k + 1);
            if (ra.Length <= 3 || rb.Length <= 3) return false;   // jamais une racine de disque seule
            return !string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase);
        }

        private static readonly HashSet<string> DossiersDeLogiciel = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "blobs", "snapshots", "node_modules", "site-packages", "dist-packages", ".git", ".svn",
            "thirdparty", "third_party", "engine", "binaries", "lib", "lib64", "bin", "obj", "packages",
            ".nuget", "sdk", "sdks", "runtimes", "toolchains", "envs", "miniconda3", "anaconda3",
            ".cache", "_cache", "cache", "caches", "plugins", "redist", "redistributables"
        };

        private static readonly Regex NomEmpreinte = new Regex(@"^(sha256[-:])?[0-9a-f]{32,}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Fichier qui appartient à un logiciel, un moteur ou un cache (modèles d'IA,
        /// paquets) : le supprimer seul casse ce logiciel. Reconnu à un dossier typique (lib, bin,
        /// Engine, ThirdParty, blobs, snapshots, node_modules…, « models--… » de Hugging Face) ou à
        /// un nom qui est une empreinte (réserves adressées par contenu : Ollama, Docker…).</summary>
        internal static bool Logiciel(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string[] seg = path.Split('\\');
            for (int i = 1; i < seg.Length - 1; i++)
            {
                if (DossiersDeLogiciel.Contains(seg[i])) return true;
                if (seg[i].StartsWith("models--", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return NomEmpreinte.IsMatch(seg[seg.Length - 1]);
        }

        /// <summary>Décide quel exemplaire garder. Les références (fichiers de jeux, écartés) le sont
        /// toutes ; sinon UN seul : pas dans Téléchargements ni Temp, pas nommé « - Copie » ou
        /// « (2) », le plus ancien, puis le chemin le plus court.</summary>
        internal static void Choisir(Groupe g)
        {
            bool aRef = false;
            foreach (Copie c in g.Copies) { c.Garder = c.Reference || c.Protege; if (c.Garder) aRef = true; }
            if (aRef) return;
            Copie best = null;
            foreach (Copie c in g.Copies) if (best == null || Prefere(c, best)) best = c;
            if (best != null) best.Garder = true;
        }

        private static bool Prefere(Copie a, Copie b)
        {
            int pa = Penalite(a), pb = Penalite(b);
            if (pa != pb) return pa < pb;
            if (a.Modified != b.Modified && a.Modified != DateTime.MinValue && b.Modified != DateTime.MinValue)
                return a.Modified < b.Modified;
            int la = (a.Path ?? "").Length, lb = (b.Path ?? "").Length;
            if (la != lb) return la < lb;
            return string.CompareOrdinal(a.Path ?? "", b.Path ?? "") < 0;
        }

        private static readonly Regex NomDeCopie = new Regex(
            @"(\s-\s(copie|copy|kopie|copia)(\s\(\d+\))?$)|(\s?\(\d+\)$)|(^(copie de|copy of|copia de)\s)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex SuffixeNumero = new Regex(@"(_\d{1,2})+$", RegexOptions.CultureInvariant);

        internal static int Penalite(Copie c)
        {
            int p = 0;
            string nom = "";
            try { nom = System.IO.Path.GetFileNameWithoutExtension(c.Name ?? c.Path ?? "") ?? ""; } catch { }
            if (NomDeCopie.IsMatch(nom.Trim())) p += 2;
            else if (SuffixeNumero.IsMatch(nom.Trim())) p += 1;   // « model_1 », « italy_1_1 » : renommage d'un outil de copie
            string d = ((c.Dir ?? "") + "\\").ToLowerInvariant();
            if (d.Contains("\\downloads\\") || d.Contains("\\téléchargements\\")) p += 1;
            if (d.Contains("\\temp\\") || d.Contains("\\tmp\\")) p += 1;
            return p;
        }

        /// <summary>Identité physique (volume + identifiant de fichier). Null si illisible.</summary>
        internal static string Identite(string path)
        {
            try
            {
                using (SafeFileHandle h = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                {
                    FILE_ID_INFO info;
                    if (!GetFileInformationByHandleEx(h, FileIdInfo, out info, (uint)Marshal.SizeOf(typeof(FILE_ID_INFO))))
                        return null;
                    return info.VolumeSerialNumber.ToString("X16") + ":" + info.IdHigh.ToString("X16") + info.IdLow.ToString("X16");
                }
            }
            catch { return null; }
        }

        /// <summary>Empreinte du contenu : SHA-256 de 16 extraits de 64 Ko (tout le fichier s'il est
        /// petit) et de la taille. Null si illisible ou si la taille a changé depuis le balayage.</summary>
        internal static string Empreinte(string path, long bytes)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess))
                using (SHA256 sha = SHA256.Create())
                {
                    if (fs.Length != bytes) return null;
                    var buf = new byte[Extrait];
                    if (bytes <= (long)Extrait * NbExtraits)
                    {
                        int n;
                        while ((n = fs.Read(buf, 0, buf.Length)) > 0) sha.TransformBlock(buf, 0, n, null, 0);
                    }
                    else
                    {
                        long pas = (bytes - Extrait) / (NbExtraits - 1);
                        for (int i = 0; i < NbExtraits; i++)
                        {
                            fs.Position = i == NbExtraits - 1 ? bytes - Extrait : i * pas;
                            fs.ReadExactly(buf, 0, Extrait);
                            sha.TransformBlock(buf, 0, Extrait, null, 0);
                        }
                    }
                    byte[] taille = BitConverter.GetBytes(bytes);
                    sha.TransformFinalBlock(taille, 0, taille.Length);
                    return Convert.ToHexString(sha.Hash);
                }
            }
            catch { return null; }
        }

        /// <summary>Comparaison COMPLÈTE, octet par octet. Faux au moindre écart, si l'un des deux
        /// est illisible, ou si les deux chemins désignent le même fichier physique (l'effacer
        /// ne libérerait rien). <paramref name="lu"/> reçoit les octets déjà comparés.</summary>
        public static bool Identiques(string a, string b, Action<long> lu)
        {
            try
            {
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
                if (string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase)) return false;
                string ia = Identite(a), ib = Identite(b);
                if (ia == null || ib == null || ia == ib) return false;

                using (var fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
                using (var fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
                {
                    if (fa.Length != fb.Length) return false;
                    var ba = new byte[1 << 20];
                    var bb = new byte[1 << 20];
                    long total = 0;
                    while (true)
                    {
                        int na = fa.ReadAtLeast(ba, ba.Length, false);
                        int nb = fb.ReadAtLeast(bb, bb.Length, false);
                        if (na != nb) return false;
                        if (na == 0) return true;
                        if (!ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return false;
                        total += na;
                        if (lu != null) { try { lu(total); } catch { } }
                    }
                }
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ jeux

        /// <summary>Jeux installés plusieurs fois : même nom normalisé, dossiers distincts (un
        /// dossier inclus dans un autre est la même installation vue par deux lanceurs).</summary>
        public static List<JeuDouble> JeuxEnDouble(IEnumerable<Install> installs)
        {
            var parNom = new Dictionary<string, List<Install>>(StringComparer.Ordinal);
            var ordre = new List<string>();
            if (installs != null)
                foreach (Install i in installs)
                {
                    if (i == null || string.IsNullOrWhiteSpace(i.Dossier) || string.IsNullOrWhiteSpace(i.Nom)) continue;
                    string k = NomNormalise(i.Nom);
                    if (k.Length < 2) continue;
                    List<Install> l;
                    if (!parNom.TryGetValue(k, out l)) { parNom[k] = l = new List<Install>(); ordre.Add(k); }
                    l.Add(i);
                }

            var res = new List<JeuDouble>();
            foreach (string k in ordre)
            {
                var j = new JeuDouble { Nom = parNom[k][0].Nom };
                var dossiers = new List<string>();
                foreach (Install i in parNom[k])
                {
                    string d = i.Dossier.Trim().TrimEnd('\\', '/').ToLowerInvariant() + "\\";
                    bool meme = false;
                    foreach (string x in dossiers)
                        if (d.StartsWith(x, StringComparison.Ordinal) || x.StartsWith(d, StringComparison.Ordinal)) { meme = true; break; }
                    if (meme) continue;
                    dossiers.Add(d);
                    j.Installs.Add(i);
                }
                if (j.Installs.Count >= 2) res.Add(j);
            }
            res.Sort(delegate (JeuDouble a, JeuDouble b) { return b.RecuperableMB.CompareTo(a.RecuperableMB); });
            return res;
        }

        /// <summary>Lettres et chiffres seulement, en minuscules, sans accents ni ™ ® © :
        /// « Call of Duty®: Black Ops » = « CALL OF DUTY BLACK OPS ».</summary>
        internal static string NomNormalise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char ch in d)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ Win32

        private const int FileIdInfo = 18;

        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_ID_INFO
        {
            public ulong VolumeSerialNumber;
            public ulong IdLow;
            public ulong IdHigh;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle h, int infoClass, out FILE_ID_INFO info, uint size);
    }
}
