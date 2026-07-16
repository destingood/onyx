using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

// Générateur de clés de licence BT Optimizer Pro.
//   Usage : dotnet run -- "Nom du client"
// Produit une clé à donner à l'acheteur. La clé encode le nom + une signature RSA
// que seule cette machine (détentrice de private.xml) peut créer.
//
// ⚠️ NE JAMAIS distribuer private.xml ni cet outil : quiconque les possède peut
//    générer des licences valides. Garde-les hors du dossier livré aux clients.
internal static class Keygen
{
    private const char Sep = (char)0x1F;

    private static void Main(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.WriteLine("Usage : dotnet run -- \"Nom du client\"");
            Environment.Exit(1);
            return;
        }
        string name = args[0].Trim();

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
            byte[] sig = rsa.SignData(Encoding.UTF8.GetBytes(name),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            string payload = name + Sep + Convert.ToBase64String(sig);
            string token = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
            Console.WriteLine();
            Console.WriteLine("Licence pour : " + name);
            Console.WriteLine("Clé (à envoyer au client) :");
            Console.WriteLine(token);
        }
    }
}
