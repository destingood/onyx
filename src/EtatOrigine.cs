using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Journal d'état d'origine : pour chaque réglage, l'état EXACT du système avant qu'ONYX le
    /// modifie, afin que « Rétablir » le restaure à l'identique. Partie PURE : modèle, encodage
    /// des valeurs de registre en texte, lecture/écriture du fichier. Aucun accès registre ni
    /// disque ici (tout se vérifie au banc : tests/EtatOrigineTests.cs).
    /// </summary>
    internal static class EtatOrigine
    {
        /// <summary>
        /// Un élément capturé. Genre : "reg", "cle", "alim", "service" ou "tache".
        /// reg : Cible = chemin complet (RegistryKey.Name), Nom = valeur ("" = par défaut), Kind = RegistryValueKind, Valeur encodée.
        /// cle : Cible = chemin complet, Existe = la clé existait.
        /// alim : Cible = GUID du sous-groupe, Nom = GUID du paramètre, Valeur = "ac;dc;plan" (décimal ; plan = GUID du plan actif à la capture).
        /// service : Cible = nom du service, Valeur = "start;delayed;running" (start = -1 si absent ; delayed, running = 0/1).
        /// tache : Cible = chemin de la tâche, Valeur = "active", "inactive" ou "inconnue".
        /// </summary>
        public sealed class Item
        {
            public string Genre;
            public string Cible;
            public string Nom;
            public bool Existe;
            public string Kind;
            public string Valeur;
            public long Seq;
        }

        /// <summary>Tout ce qu'un réglage a modifié, avec la date de capture.</summary>
        public sealed class Entree
        {
            public string Id;
            public string Date;
            public bool Complet = true;
            public List<string> NonCouvert = new List<string>();
            public List<Item> Items = new List<Item>();
        }

        public sealed class Journal
        {
            public int Version = 1;
            public long Prochain = 1;
            public List<Entree> Entrees = new List<Entree>();
            /// <summary>Le fichier n'a pas pu être relu : journal vide rendu à la place.</summary>
            public bool Corrompu;
        }

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true
        };

        /// <summary>Clé d'identité d'un élément, insensible à la casse.</summary>
        public static string Identite(Item i)
        {
            return (i.Genre + "|" + i.Cible + "|" + i.Nom).ToUpperInvariant();
        }

        public static string EncodeValeur(object valeur, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord:
                    return unchecked((uint)Convert.ToInt32(valeur, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                case RegistryValueKind.QWord:
                    return unchecked((ulong)Convert.ToInt64(valeur, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    return (string)valeur;
                case RegistryValueKind.MultiString:
                    return JsonSerializer.Serialize((string[])valeur);
                default: // Binary, None, Unknown
                    var octets = (byte[])valeur;
                    var sb = new StringBuilder(octets.Length * 2);
                    foreach (var o in octets) sb.Append(o.ToString("X2", CultureInfo.InvariantCulture));
                    return sb.ToString();
            }
        }

        public static object DecodeValeur(string texte, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord:
                    return unchecked((int)uint.Parse(texte, CultureInfo.InvariantCulture));
                case RegistryValueKind.QWord:
                    return unchecked((long)ulong.Parse(texte, CultureInfo.InvariantCulture));
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    return texte;
                case RegistryValueKind.MultiString:
                    return JsonSerializer.Deserialize<string[]>(texte);
                default:
                    var octets = new byte[texte.Length / 2];
                    for (int k = 0; k < octets.Length; k++)
                        octets[k] = byte.Parse(texte.Substring(k * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    return octets;
            }
        }

        /// <summary>Texte vide : journal vide. Texte illisible : journal vide marqué Corrompu.</summary>
        public static Journal Lire(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new Journal();
            try
            {
                var j = JsonSerializer.Deserialize<Journal>(json, Options);
                return j == null ? new Journal { Corrompu = true } : Normalise(j);
            }
            catch (Exception)
            {
                return new Journal { Corrompu = true };
            }
        }

        /// <summary>JSON valide mais collections nulles ou éléments nuls : le code appelant ne voit jamais de null.</summary>
        private static Journal Normalise(Journal j)
        {
            j.Entrees = (j.Entrees ?? new List<Entree>()).FindAll(e => e != null);
            foreach (var e in j.Entrees)
            {
                e.NonCouvert = e.NonCouvert ?? new List<string>();
                e.Items = (e.Items ?? new List<Item>()).FindAll(i => i != null);
            }
            return j;
        }

        public static string Ecrire(Journal j)
        {
            return JsonSerializer.Serialize(j, Options);
        }

        /// <summary>Capturer si l'entrée existe déjà (on la complète) ou si le réglage n'était pas encore appliqué (premier passage).</summary>
        public static bool DoitCapturer(bool entreeExiste, bool dejaDansAppliques)
        {
            return entreeExiste || !dejaDansAppliques;
        }

        /// <summary>L'entrée existante pour ce réglage, sinon une nouvelle ajoutée au journal.</summary>
        public static Entree Ouvre(Journal j, string id, DateTime quand)
        {
            var e = j.Entrees.Find(x => x.Id == id);
            if (e != null) return e;
            e = new Entree { Id = id, Date = quand.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            j.Entrees.Add(e);
            return e;
        }

        /// <summary>Ajoute l'état « avant » s'il n'est pas déjà noté : une réapplication ne déplace pas l'origine.</summary>
        public static void Note(Journal j, Entree e, Item avant)
        {
            string id = Identite(avant);
            if (e.Items.Exists(x => Identite(x) == id)) return;
            avant.Seq = j.Prochain++;
            e.Items.Add(avant);
        }

        /// <summary>
        /// Journal SANS l'entrée id (j n'est pas modifié). Un item que d'autres entrées postérieures
        /// ont écrasé n'est pas restauré : la plus ancienne d'entre elles hérite de son origine.
        /// </summary>
        public static Journal Retire(Journal j, string id, out List<Item> aRestaurer)
        {
            var copie = Lire(Ecrire(j));
            aRestaurer = new List<Item>();
            var retiree = copie.Entrees.Find(e => e.Id == id);
            if (retiree == null) return copie;
            copie.Entrees.Remove(retiree);
            foreach (var x in retiree.Items)
            {
                string cle = Identite(x);
                Item heritier = null;
                foreach (var e in copie.Entrees)
                    foreach (var y in e.Items)
                        if (y.Seq > x.Seq && Identite(y) == cle && (heritier == null || y.Seq < heritier.Seq))
                            heritier = y;
                if (heritier == null) { aRestaurer.Add(x); continue; }
                heritier.Existe = x.Existe;
                heritier.Kind = x.Kind;
                heritier.Valeur = x.Valeur;
            }
            aRestaurer.Sort((p, q) => q.Seq.CompareTo(p.Seq));
            return copie;
        }

        /// <summary>
        /// Vrai si le fabricant ou le modèle (WMI Win32_ComputerSystem) désigne une machine virtuelle :
        /// VMware, VirtualBox, QEMU, KVM, Xen, Parallels (casse ignorée), ou Hyper-V / Windows Sandbox
        /// (fabricant « Microsoft Corporation » ET modèle « Virtual Machine » ; une Surface reste réelle).
        /// Valeurs nulles ou vides : faux (le test aller-retour applique tous les réglages pour de vrai).
        /// </summary>
        public static bool EstMachineDeTest(string fabricant, string modele)
        {
            if (string.IsNullOrEmpty(fabricant) && string.IsNullOrEmpty(modele)) return false;
            fabricant = fabricant ?? "";
            modele = modele ?? "";
            foreach (string marque in new[] { "VMware", "VirtualBox", "QEMU", "KVM", "Xen", "Parallels" })
            {
                if (fabricant.IndexOf(marque, StringComparison.OrdinalIgnoreCase) >= 0
                    || modele.IndexOf(marque, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return fabricant.IndexOf("Microsoft Corporation", StringComparison.OrdinalIgnoreCase) >= 0
                && modele.IndexOf("Virtual Machine", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Une ligne lisible par item dont l'état actuel diffère de l'origine.</summary>
        public static List<string> Ecarts(List<Item> origine, List<Item> actuel)
        {
            var lignes = new List<string>();
            var parId = new Dictionary<string, Item>();
            foreach (var a in actuel) parId[Identite(a)] = a;
            foreach (var o in origine)
            {
                string nom = o.Genre + " " + o.Cible + (string.IsNullOrEmpty(o.Nom) ? "" : "\\" + o.Nom);
                if (!parId.TryGetValue(Identite(o), out var a)) { lignes.Add(nom + " : non relu"); continue; }
                if (!Pareil(o, a)) lignes.Add(nom + " : origine " + Decrit(o) + ", actuel " + Decrit(a));
            }
            return lignes;
        }

        private static bool Pareil(Item o, Item a)
        {
            switch (o.Genre)
            {
                case "reg":
                    if (o.Existe != a.Existe) return false;
                    return !o.Existe || (o.Kind == a.Kind && o.Valeur == a.Valeur);
                case "cle":
                    return o.Existe == a.Existe;
                case "service": // "start;delayed;running" : running est ignoré
                    return ChampsService(o.Valeur) == ChampsService(a.Valeur);
                default: // alim, tache
                    return o.Valeur == a.Valeur;
            }
        }

        private static string ChampsService(string valeur)
        {
            var p = (valeur ?? "").Split(';');
            return p[0] + ";" + (p.Length > 1 ? p[1] : "");
        }

        private static string Decrit(Item i)
        {
            if ((i.Genre == "reg" || i.Genre == "cle") && !i.Existe) return "absent";
            if (i.Genre == "cle") return "présente";
            return i.Valeur ?? "(vide)";
        }

        /// <summary>Valeur d'un item alim : "ac;dc;plan" (décimal non signé ; plan = GUID du plan actif à la capture).</summary>
        public static string ValeurAlim(int ac, int dc, string plan)
        {
            return unchecked((uint)ac).ToString(CultureInfo.InvariantCulture) + ";"
                 + unchecked((uint)dc).ToString(CultureInfo.InvariantCulture) + ";" + plan;
        }

        /// <summary>Relit "ac;dc;plan". false si le texte est mal formé ou si le plan n'est pas un GUID.</summary>
        public static bool LitAlim(string valeur, out uint ac, out uint dc, out string plan)
        {
            ac = 0; dc = 0; plan = null;
            var p = (valeur ?? "").Split(';');
            if (p.Length != 3
                || !uint.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out ac)
                || !uint.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out dc)
                || !GuidSeulRx.IsMatch(p[2])) return false;
            plan = p[2];
            return true;
        }

        private const string GuidMotif = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";
        private static readonly System.Text.RegularExpressions.Regex GuidRx = new System.Text.RegularExpressions.Regex(GuidMotif);
        private static readonly System.Text.RegularExpressions.Regex GuidSeulRx = new System.Text.RegularExpressions.Regex("^" + GuidMotif + "$");

        /// <summary>GUID du plan actif d'après « powercfg /getactivescheme » (premier GUID, indépendant de la langue) ; null s'il n'y en a pas.</summary>
        public static string LitPlanActif(string sortie)
        {
            var m = GuidRx.Match(sortie ?? "");
            return m.Success ? m.Value : null;
        }

        /// <summary>Valeur d'un item service : "start;delayed;running" (start = -1 si le service est absent).</summary>
        public static string ValeurService(int start, bool differe, bool enMarche)
        {
            return start.ToString(CultureInfo.InvariantCulture) + ";" + (differe ? "1" : "0") + ";" + (enMarche ? "1" : "0");
        }

        /// <summary>Relit "start;delayed;running". false si le texte est mal formé.</summary>
        public static bool LitService(string valeur, out int start, out bool differe, out bool enMarche)
        {
            start = -1; differe = false; enMarche = false;
            var p = (valeur ?? "").Split(';');
            if (p.Length != 3 || !int.TryParse(p[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out start)) return false;
            if ((p[1] != "0" && p[1] != "1") || (p[2] != "0" && p[2] != "1")) return false;
            differe = p[1] == "1";
            enMarche = p[2] == "1";
            return true;
        }

        /// <summary>
        /// Mot clé de « sc config start= » pour une valeur Start : 2 (+ différé) → delayed-auto, 2 → auto,
        /// 3 → demand, 4 → disabled. null pour 0 (boot), 1 (system), -1 (absent) ou autre : non restaurable.
        /// </summary>
        public static string TypeDemarrage(int start, bool differe)
        {
            switch (start)
            {
                case 2: return differe ? "delayed-auto" : "auto";
                case 3: return "demand";
                case 4: return "disabled";
                default: return null;
            }
        }

        /// <summary>
        /// [ac, dc] d'après la sortie de « powercfg /query » : les deux DERNIÈRES lignes « …: 0x&lt;hex&gt; »
        /// (indépendant de la langue). null s'il y en a moins de deux.
        /// </summary>
        public static int[] LitPowercfg(string sortie)
        {
            if (string.IsNullOrEmpty(sortie)) return null;
            var m = System.Text.RegularExpressions.Regex.Matches(sortie, @":\s*0x([0-9a-fA-F]+)\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);
            if (m.Count < 2) return null;
            return new[]
            {
                (int)uint.Parse(m[m.Count - 2].Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                (int)uint.Parse(m[m.Count - 1].Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            };
        }
    }
}
