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
            Console.WriteLine();
            Console.WriteLine("Licence pour : " + name
                + (expiry.HasValue ? " — expire le " + expiry.Value.ToString("dd/MM/yyyy") : " — À VIE")
                + (machine != null ? " — LIÉE AU PC " + machine : " — utilisable sur tout PC"));
            Console.WriteLine("Clé (à envoyer au client) :");
            Console.WriteLine(token);
        }
    }
}
