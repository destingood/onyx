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
        /// alim : Cible = GUID du sous-groupe, Nom = GUID du paramètre, Valeur = "ac;dc" en décimal.
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
                return JsonSerializer.Deserialize<Journal>(json, Options) ?? new Journal { Corrompu = true };
            }
            catch (Exception)
            {
                return new Journal { Corrompu = true };
            }
        }

        public static string Ecrire(Journal j)
        {
            return JsonSerializer.Serialize(j, Options);
        }
    }
}
