using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// QUEL DISQUE EST VRAIMENT LE PLUS RAPIDE POUR TES JEUX — mesuré, pas supposé.
    ///
    /// Tout le monde suppose la même hiérarchie : NVMe plus rapide que SATA, point. Pour le débit
    /// séquentiel, c'est vrai. Pour ce que fait un jeu, ça ne l'est pas forcément.
    ///
    /// Un jeu qui charge des textures ne lit pas un gros fichier d'un trait : il pioche des milliers
    /// de petits blocs à des endroits imprévisibles. Ce qui compte alors n'est pas le débit mais le
    /// TEMPS D'UN ACCÈS ISOLÉ. Et là, un NVMe d'entrée de gamme SANS MÉMOIRE CACHE peut être plus
    /// lent qu'un bon SATA : sans cache, retrouver où vit un bloc demande une lecture supplémentaire
    /// dans la mémoire flash, à chaque fois. Un disque presque plein aggrave encore le phénomène.
    ///
    /// Mesuré sur une machine réelle, trois passes concordantes :
    ///     NVMe d'entrée de gamme, rempli à 89 %  →  0,47 à 0,60 ms par accès
    ///     SATA correct, rempli à 85 %            →  0,31 à 0,34 ms par accès
    /// Le NVMe était 50 % PLUS LENT. Le joueur avait installé ses gros jeux dessus, en toute
    /// logique apparente.
    ///
    /// LES LIMITES DE CETTE MESURE, DITES FRANCHEMENT. Elle lit un bloc à la fois et attend la
    /// réponse : elle mesure la LATENCE, pas le débit. Un jeu qui empile plusieurs demandes en
    /// parallèle exploite mieux un NVMe, et l'écart se resserre. Ce test répond à « lequel répond le
    /// plus vite », pas à « lequel copie le plus vite ». Les deux questions comptent ; celle-ci est
    /// celle qui produit les micro-saccades.
    ///
    /// STRICTEMENT EN LECTURE : aucun octet n'est écrit. Le cache de Windows est contourné, sans
    /// quoi on mesurerait la mémoire vive.
    /// </summary>
    internal static class DiskLatency
    {
        /// <summary>Contourne le cache de Windows (FILE_FLAG_NO_BUFFERING).</summary>
        private const FileOptions SansCache = (FileOptions)0x20000000;

        /// <summary>Taille d'un accès : celle d'un secteur logique, et l'ordre de grandeur d'une
        /// lecture de texture.</summary>
        public const int Bloc = 4096;

        public sealed class Resultat
        {
            public string Lecteur = "";      // « F: »
            public string Disque = "";       // modèle
            public string Bus = "";          // NVMe / SATA / USB
            public double MsParAcces;        // le chiffre qui compte
            public double AccesParSeconde;
            public long LibreGo, TotalGo;
            public string Motif;             // pourquoi la mesure a échoué, le cas échéant

            public int RemplissagePourcent
            {
                get { return TotalGo <= 0 ? 0 : (int)Math.Round((TotalGo - LibreGo) * 100.0 / TotalGo); }
            }
        }

        // ------------------------------------------------------------------ pur

        /// <summary>Conversion PURE : temps total et nombre d'accès → millisecondes par accès.</summary>
        public static double MsParAcces(double msTotal, int acces)
        {
            if (acces <= 0 || msTotal <= 0) return 0;
            return msTotal / acces;
        }

        /// <summary>
        /// Verdict PUR sur un temps d'accès. Les seuils viennent de ce que produit chaque famille de
        /// disques au repos, pas d'une étiquette commerciale.
        /// </summary>
        public static string Verdict(double msParAcces)
        {
            if (msParAcces <= 0) return "";
            if (msParAcces < 0.15) return "Excellent";
            if (msParAcces < 0.35) return "Bon";
            if (msParAcces < 0.70) return "Moyen";
            if (msParAcces < 2.00) return "Lent";
            return "Très lent";
        }

        /// <summary>
        /// PUR : un disque presque plein perd ses réserves d'écriture rapide et sa table de
        /// correspondance devient plus coûteuse à parcourir. Au-delà de 85 %, ça se mesure.
        /// </summary>
        public static bool TropPlein(int remplissagePourcent)
        {
            return remplissagePourcent >= 85;
        }

        /// <summary>
        /// Compare PUREMENT deux disques et rend le conseil. null s'il n'y a rien à dire — c'est le
        /// cas le plus fréquent, et l'inventer serait pire que se taire.
        /// </summary>
        public static string Conseil(Resultat rapide, Resultat lent)
        {
            if (rapide == null || lent == null) return null;
            if (rapide.MsParAcces <= 0 || lent.MsParAcces <= 0) return null;
            double gain = lent.MsParAcces / rapide.MsParAcces;
            if (gain < 1.3) return null;   // moins de 30 % d'écart : pas de quoi déplacer 150 Go

            string s = "Ton lecteur " + lent.Lecteur + " répond en " + lent.MsParAcces.ToString("0.00")
                     + " ms par accès, contre " + rapide.MsParAcces.ToString("0.00") + " ms pour "
                     + rapide.Lecteur + " — soit " + gain.ToString("0.#") + " fois plus lent sur le "
                     + "genre d'accès que fait un jeu qui charge ses textures.";
            if (TropPlein(lent.RemplissagePourcent))
                s += " Et il est rempli à " + lent.RemplissagePourcent + " % : au-delà de 85 %, un SSD "
                   + "perd ses réserves et ralentit. Libérer de la place peut suffire, avant même de "
                   + "déplacer quoi que ce soit.";
            return s;
        }

        /// <summary>Classement PUR : le plus rapide d'abord.</summary>
        public static List<Resultat> Classement(List<Resultat> l)
        {
            var r = new List<Resultat>();
            if (l != null) foreach (Resultat x in l) if (x != null && x.MsParAcces > 0) r.Add(x);
            r.Sort(delegate (Resultat a, Resultat b) { return a.MsParAcces.CompareTo(b.MsParAcces); });
            return r;
        }

        // ------------------------------------------------------------------ mesure

        /// <summary>Plus gros fichier trouvé sous <paramref name="dossier"/>, ou null. On s'arrête
        /// dès qu'on en tient un assez gros : parcourir tout un disque coûterait plus cher que la
        /// mesure elle-même.</summary>
        public static string GrosFichier(string dossier, long minOctets)
        {
            try
            {
                string meilleur = null; long taille = 0;
                foreach (string f in Directory.EnumerateFiles(dossier, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.Length > taille) { taille = fi.Length; meilleur = f; }
                    }
                    catch { }
                    if (taille >= minOctets * 8) break;
                }
                return taille >= minOctets ? meilleur : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Mesure tous les lecteurs fixes, en cherchant la sonde là où vivent les gros fichiers :
        /// les bibliothèques de jeux d'abord, la racine ensuite. Un lecteur sans gros fichier ne
        /// peut pas être mesuré honnêtement — il est rendu avec son motif, pas avec un zéro.
        /// </summary>
        public static List<Resultat> Scan(int tirages, int limiteMsParLecteur)
        {
            var res = new List<Resultat>();
            DriveInfo[] lecteurs;
            try { lecteurs = DriveInfo.GetDrives(); }
            catch { return res; }

            foreach (DriveInfo d in lecteurs)
            {
                Resultat r;
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    string racine = d.RootDirectory.FullName;
                    string sonde = PremierDossierExistant(racine);
                    r = Mesure(d.Name.TrimEnd('\\'), sonde ?? racine, tirages, limiteMsParLecteur);
                    r.LibreGo = (long)Math.Round(d.AvailableFreeSpace / 1073741824.0);
                    r.TotalGo = (long)Math.Round(d.TotalSize / 1073741824.0);
                }
                catch { continue; }
                res.Add(r);
            }
            RenseigneDisques(res);
            return res;
        }

        /// <summary>Dossiers où chercher un gros fichier : les bibliothèques de jeux tiennent les
        /// plus gros, et ce sont justement elles qui nous intéressent.</summary>
        private static string PremierDossierExistant(string racine)
        {
            string[] pistes =
            {
                @"SteamLibrary\steamapps\common", @"Steam\steamapps\common",
                @"Program Files (x86)\Steam\steamapps\common",
                @"Games", @"Epic Games", @"Program Files\Epic Games", @"Windows\System32"
            };
            foreach (string p in pistes)
            {
                try { string c = Path.Combine(racine, p); if (Directory.Exists(c)) return c; }
                catch { }
            }
            return null;
        }

        /// <summary>Associe modèle et type de bus à chaque lettre. Sans ça, l'utilisateur lit
        /// « F: est plus lent » sans savoir que F: est justement son NVMe.</summary>
        private static void RenseigneDisques(List<Resultat> res)
        {
            try
            {
                using (var part = new System.Management.ManagementObjectSearcher(
                    "SELECT DeviceID, Index FROM Win32_DiskDrive"))
                {
                    var modeles = new Dictionary<uint, string[]>();
                    using (var dd = new System.Management.ManagementObjectSearcher(
                        "SELECT Index, Model, InterfaceType FROM Win32_DiskDrive"))
                        foreach (System.Management.ManagementObject mo in dd.Get())
                            try
                            {
                                modeles[Convert.ToUInt32(mo["Index"])] = new[]
                                {
                                    Convert.ToString(mo["Model"]) ?? "",
                                    Convert.ToString(mo["InterfaceType"]) ?? ""
                                };
                            }
                            catch { }

                    // Association lettre → disque via les partitions.
                    using (var lien = new System.Management.ManagementObjectSearcher(
                        "SELECT * FROM Win32_LogicalDiskToPartition"))
                        foreach (System.Management.ManagementObject mo in lien.Get())
                            try
                            {
                                string dep = Convert.ToString(mo["Dependent"]);
                                string ant = Convert.ToString(mo["Antecedent"]);
                                int i1 = dep.LastIndexOf('"'); int i0 = dep.LastIndexOf('"', i1 - 1);
                                string lettre = dep.Substring(i0 + 1, i1 - i0 - 1);
                                var m = System.Text.RegularExpressions.Regex.Match(ant, @"Disk #(\d+)");
                                if (!m.Success) continue;
                                uint idx = uint.Parse(m.Groups[1].Value);
                                string[] info;
                                if (!modeles.TryGetValue(idx, out info)) continue;
                                foreach (Resultat r in res)
                                    if (string.Equals(r.Lecteur, lettre, StringComparison.OrdinalIgnoreCase))
                                    { r.Disque = info[0]; r.Bus = info[1]; }
                            }
                            catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Mesure le temps d'accès d'un lecteur. Rend un Resultat dont Motif est renseigné si la
        /// mesure n'a pas pu se faire — jamais un chiffre inventé.
        /// </summary>
        public static Resultat Mesure(string lecteur, string dossierSonde, int tirages, int limiteMs)
        {
            var r = new Resultat { Lecteur = lecteur };
            string f = GrosFichier(dossierSonde, 512L * 1024 * 1024);
            if (f == null) { r.Motif = "aucun fichier assez gros pour une mesure fiable"; return r; }
            try
            {
                var buf = new byte[Bloc];
                using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, Bloc, SansCache))
                {
                    long blocs = fs.Length / Bloc;
                    if (blocs < 1000) { r.Motif = "fichier trop petit"; return r; }
                    var rnd = new Random(unchecked(Environment.TickCount ^ lecteur.GetHashCode()));
                    var chrono = Stopwatch.StartNew();
                    int lus = 0;
                    for (int i = 0; i < tirages; i++)
                    {
                        long pos = (long)(rnd.NextDouble() * (blocs - 1)) * Bloc;
                        fs.Seek(pos, SeekOrigin.Begin);
                        if (fs.Read(buf, 0, Bloc) == Bloc) lus++;
                        if (chrono.ElapsedMilliseconds > limiteMs) break;
                    }
                    chrono.Stop();
                    if (lus == 0) { r.Motif = "aucune lecture aboutie"; return r; }
                    r.MsParAcces = MsParAcces(chrono.Elapsed.TotalMilliseconds, lus);
                    r.AccesParSeconde = lus / Math.Max(0.001, chrono.Elapsed.TotalSeconds);
                }
            }
            catch (Exception ex) { r.Motif = "lecture refusée (" + ex.GetType().Name + ")"; }
            return r;
        }
    }
}
