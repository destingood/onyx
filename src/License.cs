using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Licence Free/Pro honnête et hors-ligne : une clé = le nom du licencié signé
    /// (RSA-2048). L'app vérifie la signature avec la clé publique embarquée ; seul le
    /// vendeur, qui détient la clé privée, peut générer des clés valides.
    /// Ce n'est pas un DRM incassable (aucun DRM hors-ligne ne l'est) : c'est un
    /// contrôle de licence standard pour un produit indépendant.
    /// </summary>
    internal static class License
    {
        private const string PublicKeyXml =
            "<RSAKeyValue><Modulus>wb0N0QieE+SPCM3Iu0xDQFG3TjN9dHuv7a4FIDknN5FMr9sSQ6hk8wEcODgtor22h9Go91vTzhs/FFUccSIwGrKlYqHvirMNiIaGXzEo688WBbLhLxegrWrf9uwN8I679rZK7JmjBAowawEjcV3SIGrapSeBQP2BoKWho2/6x6E3VWAXUSjgrxG2V//6QDGBFk9fuMTLrvAlMd6EyiGSTA0KfPrzx4vSm1pvCtvQAsHVnDC9aEm2Q1SWVjWbzu9h8epCt9Q6EiRiqw1NCJD0t6dOymsAqoyCcoZzbh9yNZyncH1hZ1HQUmqAGbnY0/KSZ/jzcRRcysYh5mEANZWktQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        private const char Sep = (char)0x1F; // séparateur d'unité entre le nom et la signature

        public static bool IsPro { get; private set; }
        public static string Licensee { get; private set; }

        private static string StorePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-license.txt"); }
        }

        static License()
        {
            Licensee = "";
            try { if (File.Exists(StorePath)) Activate(File.ReadAllText(StorePath).Trim(), false); }
            catch { }
        }

        /// <summary>Valide une clé ; si valide, passe en Pro (et l'enregistre si persist=true).</summary>
        public static bool Activate(string token, bool persist)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            try
            {
                byte[] raw = Convert.FromBase64String(token.Trim());
                string s = Encoding.UTF8.GetString(raw);
                int i = s.IndexOf(Sep);
                if (i <= 0) return false;
                string name = s.Substring(0, i);
                byte[] sig = Convert.FromBase64String(s.Substring(i + 1));
                using (RSA rsa = RSA.Create())
                {
                    rsa.FromXmlString(PublicKeyXml);
                    bool ok = rsa.VerifyData(Encoding.UTF8.GetBytes(name), sig,
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    if (!ok) return false;
                }
                IsPro = true;
                Licensee = name;
                if (persist) { try { File.WriteAllText(StorePath, token.Trim()); } catch { } }
                return true;
            }
            catch { return false; }
        }

        public static string Status()
        {
            return IsPro ? ("Pro — licence : " + Licensee) : "Édition gratuite";
        }
    }
}
