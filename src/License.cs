using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

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

        // PHASE GRATUITE (beta) : tout est débloqué, aucun mur Pro, aucun compte à rebours.
        // Le code Free/Pro reste intact et dormant : passer ce booléen à false rebranche le
        // mur d'un coup le jour de la monétisation (produit signé). Voir README « beta ».
        public static readonly bool FreePhase = true;

        private static string StorePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-license.txt"); }
        }

        // ---- Essai gratuit ----
        private const int TrialDays = 7;
        private const string TrialRegPath = @"SOFTWARE\BTOptimizer";
        private static string TrialFile
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-trial.txt"); }
        }
        public static DateTime? TrialStart { get; private set; }

        public static bool TrialUsed { get { return TrialStart.HasValue; } }
        public static bool TrialActive { get { return TrialStart.HasValue && DateTime.Now < TrialStart.Value.AddDays(TrialDays); } }
        public static bool CanStartTrial { get { return !IsPro && !TrialStart.HasValue; } }
        public static bool ProUnlocked { get { return FreePhase || IsPro || TrialActive; } }
        public static int TrialDaysLeft
        {
            get
            {
                if (!TrialActive) return 0;
                double d = (TrialStart.Value.AddDays(TrialDays) - DateTime.Now).TotalDays;
                return Math.Max(1, (int)Math.Ceiling(d));
            }
        }

        static License()
        {
            Licensee = "";
            try { if (File.Exists(StorePath)) Activate(File.ReadAllText(StorePath).Trim(), false); }
            catch { }
            LoadTrial();
        }

        private static DateTime? ParseDate(string s)
        {
            DateTime d;
            if (!string.IsNullOrWhiteSpace(s) &&
                DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                return d;
            return null;
        }

        private static void LoadTrial()
        {
            DateTime? f = null, r = null;
            try { if (File.Exists(TrialFile)) f = ParseDate(File.ReadAllText(TrialFile)); } catch { }
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(TrialRegPath))
                    if (k != null) r = ParseDate(k.GetValue("TrialStart") as string);
            }
            catch { }
            // On retient la date la PLUS ANCIENNE (supprimer un marqueur ne prolonge pas l'essai).
            if (f.HasValue && r.HasValue) TrialStart = (f < r) ? f : r;
            else TrialStart = f ?? r;
        }

        public static bool StartTrial()
        {
            if (!CanStartTrial) return false;
            DateTime now = DateTime.Now;
            string s = now.ToString("o", CultureInfo.InvariantCulture);
            try { File.WriteAllText(TrialFile, s); } catch { }
            try
            {
                using (RegistryKey k = Registry.LocalMachine.CreateSubKey(TrialRegPath))
                    if (k != null) k.SetValue("TrialStart", s, RegistryValueKind.String);
            }
            catch { }
            TrialStart = now;
            return true;
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
            if (IsPro) return "Pro — licence : " + Licensee;
            if (FreePhase) return "Version gratuite (beta) — toutes les fonctions débloquées";
            if (TrialActive) return "Essai Pro — " + TrialDaysLeft + " jour(s) restant(s)";
            if (TrialUsed) return "Édition gratuite (essai Pro expiré)";
            return "Édition gratuite";
        }
    }
}
