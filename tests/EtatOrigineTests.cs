using Microsoft.Win32;

namespace BTOptimizer.Tests
{
    /// <summary>Journal d'état d'origine : encodage des valeurs et fichier (logique pure).</summary>
    internal static class EtatOrigineTests
    {
        public static void Tout()
        {
            Banc.Titre("État d'origine : encodage et fichier");
            // Aller-retour d'encodage, un cas par type
            Banc.Egal("DWord", "4294967295", EtatOrigine.EncodeValeur(unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord));
            Banc.Verifie("DWord relu", true, (int)EtatOrigine.DecodeValeur("4294967295", RegistryValueKind.DWord) == -1);
            Banc.Verifie("QWord relu", true, (long)EtatOrigine.DecodeValeur(EtatOrigine.EncodeValeur(1L << 40, RegistryValueKind.QWord), RegistryValueKind.QWord) == 1L << 40);
            Banc.Egal("ExpandString non développée", "%SystemRoot%\\x", (string)EtatOrigine.DecodeValeur(EtatOrigine.EncodeValeur("%SystemRoot%\\x", RegistryValueKind.ExpandString), RegistryValueKind.ExpandString));
            Banc.Egal("MultiString", "a|b||c", string.Join("|", (string[])EtatOrigine.DecodeValeur(EtatOrigine.EncodeValeur(new[] { "a", "b", "", "c" }, RegistryValueKind.MultiString), RegistryValueKind.MultiString)));
            Banc.Egal("Binary en hexadécimal", "00FF10", EtatOrigine.EncodeValeur(new byte[] { 0, 255, 16 }, RegistryValueKind.Binary));
            // Fichier
            var j = new EtatOrigine.Journal();
            j.Entrees.Add(new EtatOrigine.Entree { Id = "cpu_unpark_cores", Date = "2026-11-20",
                Items = { new EtatOrigine.Item { Genre = "alim", Cible = "s", Nom = "p", Existe = true, Valeur = "100;100", Seq = 1 } } });
            var relu = EtatOrigine.Lire(EtatOrigine.Ecrire(j));
            Banc.Egal("relecture : valeur", "100;100", relu.Entrees[0].Items[0].Valeur);
            Banc.Verifie("relecture : non corrompu", false, relu.Corrompu);
            Banc.Verifie("texte vide = journal vide, non corrompu", true, EtatOrigine.Lire("").Entrees.Count == 0 && !EtatOrigine.Lire("").Corrompu);
            Banc.Verifie("texte illisible = journal vide marqué corrompu", true, EtatOrigine.Lire("{tronqu").Corrompu);
            // JSON valide mais collections nulles : normalisé, jamais de null pour le code appelant
            var n1 = EtatOrigine.Lire("{\"Entrees\":null}");
            Banc.Verifie("Entrees null = liste vide, non corrompu", true, n1.Entrees != null && n1.Entrees.Count == 0 && !n1.Corrompu);
            var n2 = EtatOrigine.Lire("{\"Entrees\":[{\"Items\":null,\"NonCouvert\":null}]}");
            Banc.Verifie("Items et NonCouvert null = listes vides", true,
                n2.Entrees.Count == 1 && n2.Entrees[0].Items != null && n2.Entrees[0].Items.Count == 0
                && n2.Entrees[0].NonCouvert != null && n2.Entrees[0].NonCouvert.Count == 0 && !n2.Corrompu);
            var n3 = EtatOrigine.Lire("{\"Entrees\":[null]}");
            Banc.Verifie("entrée null écartée", true, n3.Entrees.Count == 0 && !n3.Corrompu);
            var n4 = EtatOrigine.Lire("{\"Entrees\":[{\"Id\":\"x\",\"Items\":[null]}]}");
            Banc.Verifie("item null écarté, entrée gardée", true, n4.Entrees.Count == 1 && n4.Entrees[0].Id == "x" && n4.Entrees[0].Items.Count == 0);
            Banc.Verifie("identité insensible à la casse", true,
                EtatOrigine.Identite(new EtatOrigine.Item { Genre = "reg", Cible = "HKEY_LOCAL_MACHINE\\A", Nom = "x" })
             == EtatOrigine.Identite(new EtatOrigine.Item { Genre = "reg", Cible = "hkey_local_machine\\a", Nom = "X" }));
        }
    }
}
