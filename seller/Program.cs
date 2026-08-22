using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

// Générateur de clés de licence BT Optimizer Pro.
//   Usage : dotnet run -- "Nom du client"                     → À VIE, utilisable sur tout PC
//           dotnet run -- "Nom du client" 365                 → ABONNEMENT N jours, tout PC
//           dotnet run -- "Nom du client" -   ABCDE-FGHJK-... → À VIE, LIÉE À CE SEUL PC
//           dotnet run -- "Nom du client" 365 ABCDE-FGHJK-... → ABONNEMENT, LIÉE À CE SEUL PC
//   L'ID du PC est affiché dans l'app (Mon compte, ou la fenêtre d'activation) : le client
//   te l'envoie, tu émets la clé pour cet identifiant, elle ne marchera que sur son PC.
// Produit une clé à donner à l'acheteur. La clé encode le nom (+ la date d'expiration
// pour un abonnement) + une signature RSA que seule cette machine (détentrice de
// private.xml) peut créer. La date étant dans la partie signée, elle est infalsifiable.
//
// ⚠️ NE JAMAIS distribuer private.xml ni cet outil : quiconque les possède peut
//    générer des licences valides. Garde-les hors du dossier livré aux clients.
internal static class Keygen
{
    private const char Sep = (char)0x1F;
    private const char DateSep = (char)0x1E; // même convention que src/License.cs
    private const char MachineSep = (char)0x1D; // lie la clé à UN SEUL PC

    private static void Main(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.WriteLine("Usage : dotnet run -- \"Nom du client\" [jours|-] [ID-DU-PC]");
            Console.WriteLine("  [jours] : 365 = abonnement · \"-\" ou rien = licence à vie");
            Console.WriteLine("  [ID-DU-PC] : lie la clé à CE SEUL ordinateur (vide = tout PC)");
            Environment.Exit(1);
            return;
        }
        string name = args[0].Trim();

        string signedPayload = name;
        DateTime? expiry = null;
        string machine = null;

        if (args.Length >= 2 && args[1].Trim().Length > 0 && args[1].Trim() != "-")
        {
            int days;
            if (!int.TryParse(args[1], out days) || days <= 0)
            {
                Console.WriteLine("Durée invalide : nombre de jours attendu (ex. 365), \"-\" ou rien pour une licence à vie.");
                Environment.Exit(1);
                return;
            }
            expiry = DateTime.Now.Date.AddDays(days);
            signedPayload = name + DateSep + expiry.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // VERROU MACHINE : l'ID du PC entre DANS la partie signée -> infalsifiable, et la clé
        // sera refusée sur tout autre ordinateur.
        if (args.Length >= 3 && args[2].Trim().Length > 0)
        {
            machine = args[2].Trim().ToUpperInvariant();
            signedPayload = signedPayload + MachineSep + machine;
        }

        string privPath = Path.Combine(AppContext.BaseDirectory, "private.xml");
        if (!File.Exists(privPath))
            privPath = Path.Combine(Directory.GetCurrentDirectory(), "private.xml");
        if (!File.Exists(privPath))
        {
            Console.WriteLine("private.xml introuvable (place-le à côté du keygen).");
            Environment.Exit(2);
            return;
        }

        using (RSA rsa = RSA.Create())
        {
            rsa.FromXmlString(File.ReadAllText(privPath));
            byte[] sig = rsa.SignData(Encoding.UTF8.GetBytes(signedPayload),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            string payload = signedPayload + Sep + Convert.ToBase64String(sig);
            string token = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));

            // RELECTURE AVANT LIVRAISON — la même que dans le générateur graphique, parce que
            // c'est la même erreur qui passe par les deux portes.
            //
            // Signer ne prouve rien : ce qui compte est que l'APPLICATION sache vérifier. Le
            // commit ab25e7c a remplacé la clé publique de src/License.cs par celle d'une paire
            // dont la moitié privée n'existe nulle part. Ce keygen a continué de produire des clés
            // impeccablement signées et systématiquement refusées, sans que rien ne le signale.
            string refus = Verifie(token, privPath);
            if (refus != null)
            {
                Console.WriteLine();
                Console.WriteLine("CLÉ NON ÉMISE — " + refus);
                Console.WriteLine("Corrige la paire de clés avant d'émettre : une clé émise dans cet état");
                Console.WriteLine("se vend, se colle, et ne marche pas.");
                Environment.Exit(3);
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Licence pour : " + name
                + (expiry.HasValue ? " — expire le " + expiry.Value.ToString("dd/MM/yyyy") : " — À VIE")
                + (machine != null ? " — LIÉE AU PC " + machine : " — utilisable sur tout PC"));
            Console.WriteLine("Clé (à envoyer au client) :");
            Console.WriteLine(token);

            // CE KEYGEN N'ÉCRIT PAS AU JOURNAL. Le générateur graphique, lui, tient
            // licences-emises.csv — c'est lui qui permet de retrouver ou de réémettre la clé d'un
            // client des mois plus tard. Une licence émise ici et vendue n'existe nulle part.
            Console.WriteLine();
            Console.WriteLine("⚠  Cette clé n'a PAS été inscrite au journal (licences-emises.csv).");
            Console.WriteLine("   Pour une vente, utilise le générateur graphique : il journalise et sait réémettre.");
        }
    }

    /// <summary>Refait le trajet de l'application sur la clé produite : décodage, séparation,
    /// vérification avec la clé PUBLIQUE lue dans src/License.cs. Null si tout va bien, sinon la
    /// raison. Ne conclut pas si les sources sont absentes — un poste de vente peut légitimement
    /// ne pas les avoir, et bloquer là-dessus serait pire que le mal.</summary>
    private static string Verifie(string token, string privPath)
    {
        string modApp = null, modPriv = null;
        try
        {
            modPriv = Modulus(privPath);
            string d = Path.GetDirectoryName(privPath);
            for (int i = 0; i < 6 && d != null; i++)
            {
                string c = Path.Combine(d, "src", "License.cs");
                if (File.Exists(c)) { modApp = Modulus(c); break; }
                d = Path.GetDirectoryName(d);
            }
        }
        catch { }
        if (modApp == null || modPriv == null) return null;   // rien à comparer

        try
        {
            string s = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            int i = s.IndexOf(Sep);
            if (i <= 0) return "la clé produite n'a pas la forme attendue.";
            using (RSA v = RSA.Create())
            {
                v.FromXmlString("<RSAKeyValue><Modulus>" + modApp + "</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>");
                if (!v.VerifyData(Encoding.UTF8.GetBytes(s.Substring(0, i)),
                        Convert.FromBase64String(s.Substring(i + 1)),
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    return "la signature est refusée par la clé publique de l'application. "
                         + "La clé privée utilisée ici n'est pas sa moitié.";
            }
            return null;
        }
        catch (Exception ex) { return "vérification impossible : " + ex.Message; }
    }

    private static string Modulus(string fichier)
    {
        try
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                File.ReadAllText(fichier), "<Modulus>([^<]+)</Modulus>");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }
        catch { return null; }
    }
}
