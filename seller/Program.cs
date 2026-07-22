using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

// Générateur de clés de licence BT Optimizer Pro.
//   Usage : dotnet run -- "Nom du client"        → licence À VIE (127 €)
//           dotnet run -- "Nom du client" 365    → ABONNEMENT, expire dans N jours (49 €/an)
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

    private static void Main(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.WriteLine("Usage : dotnet run -- \"Nom du client\" [jours]");
            Console.WriteLine("  sans [jours] : licence à vie · avec (ex. 365) : abonnement");
            Environment.Exit(1);
            return;
        }
        string name = args[0].Trim();

        string signedPayload = name;
        DateTime? expiry = null;
        if (args.Length >= 2)
        {
            int days;
            if (!int.TryParse(args[1], out days) || days <= 0)
            {
                Console.WriteLine("Durée invalide : nombre de jours attendu (ex. 365), ou rien pour une licence à vie.");
                Environment.Exit(1);
                return;
            }
            expiry = DateTime.Now.Date.AddDays(days);
            signedPayload = name + DateSep + expiry.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
            Console.WriteLine();
            Console.WriteLine("Licence pour : " + name
                + (expiry.HasValue ? " — expire le " + expiry.Value.ToString("dd/MM/yyyy") : " — À VIE"));
            Console.WriteLine("Clé (à envoyer au client) :");
            Console.WriteLine(token);
        }
    }
}
