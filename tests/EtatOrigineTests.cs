using System;
using System.Linq;
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
            Regles();
        }

        private static void Regles()
        {
            Banc.Titre("État d'origine : règles");
            Banc.Verifie("ancienne version d'ONYX : pas de capture", false, EtatOrigine.DoitCapturer(false, true));
            Banc.Verifie("premier passage : capture", true, EtatOrigine.DoitCapturer(false, false));
            Banc.Verifie("entrée existante : on la complète", true, EtatOrigine.DoitCapturer(true, true));

            EtatOrigine.Item R(string v) => new EtatOrigine.Item { Genre = "reg", Cible = "HKEY_LOCAL_MACHINE\\K", Nom = "x", Existe = v != null, Kind = "DWord", Valeur = v };
            // Réapplication : l'origine ne bouge pas
            var j = new EtatOrigine.Journal();
            var a = EtatOrigine.Ouvre(j, "A", new DateTime(2026, 11, 20));
            EtatOrigine.Note(j, a, R("0"));
            EtatOrigine.Note(j, a, R("1"));
            Banc.Egal("réapplication : origine conservée", "0", a.Items.Single().Valeur);
            // Même valeur, A puis B ; retrait de A d'abord
            var b = EtatOrigine.Ouvre(j, "B", new DateTime(2026, 11, 20));
            EtatOrigine.Note(j, b, R("1"));                       // B voit la valeur écrite par A
            var j2 = EtatOrigine.Retire(j, "A", out var r1);
            Banc.Verifie("A retiré pendant que B tient la valeur : rien à restaurer", true, r1.Count == 0);
            Banc.Egal("B hérite de l'origine de A", "0", j2.Entrees.Single().Items.Single().Valeur);
            var j3 = EtatOrigine.Retire(j2, "B", out var r2);
            Banc.Egal("puis B retiré : on restaure l'origine", "0", r2.Single().Valeur);
            // Même valeur, retrait de B d'abord
            var j4 = EtatOrigine.Retire(j, "B", out var r3);
            Banc.Egal("B retiré d'abord : on remet la valeur de A", "1", r3.Single().Valeur);
            // Pureté
            Banc.Verifie("Retire ne modifie pas le journal d'entrée", true, j.Entrees.Count == 2 && j.Entrees[1].Items[0].Valeur == "1");
            // Valeur absente à l'origine
            Banc.Verifie("origine absente vs actuel présent = écart", true, EtatOrigine.Ecarts(new() { R(null) }, new() { R("1") }).Count == 1);
            Banc.Verifie("identiques = aucun écart", true, EtatOrigine.Ecarts(new() { R("0") }, new() { R("0") }).Count == 0);
            // powercfg
            string sortie = "  Paramètre minimal possible : 0x00000000\r\n  Paramètre maximal possible : 0x00000064\r\n  Index AC actuel : 0x00000064\r\n  Index DC actuel : 0x0000000a\r\n";
            Banc.Egal("powercfg : AC et DC", "100;10", string.Join(";", EtatOrigine.LitPowercfg(sortie)));
            Banc.Verifie("powercfg : sortie vide", true, EtatOrigine.LitPowercfg("") == null);
            // Valeurs alim et service
            const string plan = "381b4222-f694-41f0-9685-ff5bb260df2e";
            Banc.Egal("alim : encodage décimal et plan", "100;10;" + plan, EtatOrigine.ValeurAlim(100, 10, plan));
            Banc.Egal("alim : 0xFFFFFFFF non signé", "4294967295;0;" + plan, EtatOrigine.ValeurAlim(-1, 0, plan));
            Banc.Verifie("alim : relecture", true, EtatOrigine.LitAlim("4294967295;10;" + plan, out var ac, out var dc, out var pl) && ac == 4294967295u && dc == 10u && pl == plan);
            Banc.Verifie("alim : texte mal formé refusé", false, EtatOrigine.LitAlim("100;10", out _, out _, out _) || EtatOrigine.LitAlim("-1;2;" + plan, out _, out _, out _) || EtatOrigine.LitAlim(null, out _, out _, out _));
            Banc.Verifie("alim : plan qui n'est pas un GUID refusé", false, EtatOrigine.LitAlim("1;1;SCHEME_CURRENT", out _, out _, out _) || EtatOrigine.LitAlim("1;1;x" + plan, out _, out _, out _) || EtatOrigine.LitAlim("1;1;", out _, out _, out _));
            Banc.Egal("plan actif : sortie française", "e9a42b02-d5df-448d-aa00-03f14749eb62",
                EtatOrigine.LitPlanActif("GUID du mode de gestion de l'alimentation : e9a42b02-d5df-448d-aa00-03f14749eb62  (Performances optimales)"));
            Banc.Egal("plan actif : sortie anglaise", plan,
                EtatOrigine.LitPlanActif("Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)\r\n"));
            Banc.Verifie("plan actif : sortie vide", true, EtatOrigine.LitPlanActif("") == null && EtatOrigine.LitPlanActif(null) == null);
            Banc.Egal("service : encodage", "2;1;0", EtatOrigine.ValeurService(2, true, false));
            Banc.Egal("service : absent", "-1;0;0", EtatOrigine.ValeurService(-1, false, false));
            Banc.Verifie("service : relecture", true, EtatOrigine.LitService("4;0;1", out var st, out var dif, out var run) && st == 4 && !dif && run);
            Banc.Verifie("service : relecture de l'absent", true, EtatOrigine.LitService("-1;0;0", out st, out _, out _) && st == -1);
            Banc.Verifie("service : texte mal formé refusé", false, EtatOrigine.LitService("2;1", out _, out _, out _) || EtatOrigine.LitService("2;x;0", out _, out _, out _) || EtatOrigine.LitService(null, out _, out _, out _));
            Banc.Egal("sc : automatique différé", "delayed-auto", EtatOrigine.TypeDemarrage(2, true));
            Banc.Egal("sc : automatique", "auto", EtatOrigine.TypeDemarrage(2, false));
            Banc.Egal("sc : manuel", "demand", EtatOrigine.TypeDemarrage(3, true));
            Banc.Egal("sc : désactivé", "disabled", EtatOrigine.TypeDemarrage(4, false));
            Banc.Verifie("sc : boot, system et absent non restaurables", true, EtatOrigine.TypeDemarrage(0, false) == null && EtatOrigine.TypeDemarrage(1, false) == null && EtatOrigine.TypeDemarrage(-1, false) == null);
            // Machine de test (le test aller-retour applique 210 réglages pour de vrai : refus hors VM)
            Banc.Verifie("Hyper-V", true, EtatOrigine.EstMachineDeTest("Microsoft Corporation", "Virtual Machine"));
            Banc.Verifie("VMware", true, EtatOrigine.EstMachineDeTest("VMware, Inc.", "VMware20,1"));
            Banc.Verifie("VirtualBox", true, EtatOrigine.EstMachineDeTest("innotek GmbH", "VirtualBox"));
            Banc.Verifie("QEMU, casse ignorée", true, EtatOrigine.EstMachineDeTest("qemu", "Standard PC (Q35 + ICH9, 2009)"));
            Banc.Verifie("KVM", true, EtatOrigine.EstMachineDeTest("Red Hat", "KVM"));
            Banc.Verifie("Xen", true, EtatOrigine.EstMachineDeTest("Xen", "HVM domU"));
            Banc.Verifie("Parallels", true, EtatOrigine.EstMachineDeTest("Parallels Software International Inc.", "Parallels Virtual Platform"));
            Banc.Verifie("PC réel", false, EtatOrigine.EstMachineDeTest("Micro-Star International Co., Ltd.", "MS-7E12"));
            Banc.Verifie("Surface (Microsoft mais réel)", false, EtatOrigine.EstMachineDeTest("Microsoft Corporation", "Surface Laptop 7"));
            Banc.Verifie("Dell réel", false, EtatOrigine.EstMachineDeTest("Dell Inc.", "XPS 15 9530"));
            Banc.Verifie("Lenovo réel", false, EtatOrigine.EstMachineDeTest("LENOVO", "21KC0001FR"));
            Banc.Verifie("ASUS réel", false, EtatOrigine.EstMachineDeTest("ASUSTeK COMPUTER INC.", "ROG Strix G16"));
            Banc.Verifie("Gigabyte réel", false, EtatOrigine.EstMachineDeTest("Gigabyte Technology Co., Ltd.", "B650 AORUS ELITE AX"));
            Banc.Verifie("HP réel", false, EtatOrigine.EstMachineDeTest("HP", "OMEN by HP Laptop 16"));
            Banc.Verifie("valeurs nulles : refus", false, EtatOrigine.EstMachineDeTest(null, null));
            Banc.Verifie("valeurs vides : refus", false, EtatOrigine.EstMachineDeTest("", ""));
        }
    }
}
