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
        private const char DateSep = (char)0x1E; // sépare, DANS la partie signée, le nom de la date d'expiration
        private const char MachineSep = (char)0x1D; // sépare, DANS la partie signée, l'ID du SEUL PC autorisé

        public static bool IsPro { get; private set; }
        public static string Licensee { get; private set; }
        /// <summary>ID de l'ordinateur auquel la licence active est liée (null = clé valable sur tout PC).</summary>
        public static string BoundMachine { get; private set; }
        /// <summary>Fin de validité de la licence active (abonnement annuel) ; null = licence à vie.</summary>
        public static DateTime? Expiry { get; private set; }
        /// <summary>Raison lisible du dernier refus d'activation ("" si rien de plus utile que « clé invalide »).</summary>
        public static string ActivateError { get; private set; }
        private static DateTime? _expiredOn; // clé (stockée ou collée) refusée car expirée — pour Status()

        private static string StorePath
        {
            get { return AppPaths.File("bt-license.txt"); }
        }

        // ---- Essai gratuit ----
        private const int TrialDays = 7;
        private const string TrialRegPath = @"SOFTWARE\BTOptimizer";
        private static string TrialFile
        {
            get { return AppPaths.File("bt-trial.txt"); }
        }
        public static DateTime? TrialStart { get; private set; }

        public static bool TrialUsed { get { return TrialStart.HasValue; } }
        public static bool TrialActive { get { return TrialStart.HasValue && DateTime.Now < TrialStart.Value.AddDays(TrialDays); } }
        public static bool CanStartTrial { get { return !IsPro && !TrialStart.HasValue; } }
        public static bool ProUnlocked { get { return IsPro || TrialActive; } }
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
            ActivateError = "";
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

        /// <summary>Valide une clé ; si valide, passe en Pro (et l'enregistre si persist=true).
        /// Partie signée : « Nom [␞AAAA-MM-JJ] [␝ID-MACHINE] » — le nom, la date d'expiration
        /// (abonnement) et l'ID de l'ordinateur autorisé sont TOUS dans la chaîne signée RSA,
        /// donc infalsifiables sans la clé privée du vendeur.
        /// • sans date → licence à vie ; • sans ID machine → clé utilisable sur n'importe quel PC
        /// (clés historiques, restées valides) ; • AVEC ID machine → la clé ne s'active QUE sur
        /// cet ordinateur précis (une clé = un seul PC).</summary>
        public static bool Activate(string token, bool persist)
        {
            ActivateError = "";
            if (string.IsNullOrWhiteSpace(token)) return false;
            try
            {
                byte[] raw = Convert.FromBase64String(token.Trim());
                string s = Encoding.UTF8.GetString(raw);
                int i = s.IndexOf(Sep);
                if (i <= 0) return false;
                string signed = s.Substring(0, i);
                byte[] sig = Convert.FromBase64String(s.Substring(i + 1));
                using (RSA rsa = RSA.Create())
                {
                    rsa.FromXmlString(PublicKeyXml);
                    bool ok = rsa.VerifyData(Encoding.UTF8.GetBytes(signed), sig,
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    if (!ok) return false;
                }
                // Partie signée : « Nom [␞AAAA-MM-JJ] [␝ID-MACHINE] ». Date ET ID machine sont
                // DANS la signature RSA : impossible de les modifier sans la clé privée.
                string body = signed;
                string machine = null;
                int k = body.IndexOf(MachineSep);
                if (k > 0)
                {
                    machine = body.Substring(k + 1).Trim();
                    body = body.Substring(0, k);
                }

                string name = body;
                DateTime? until = null;
                int j = body.IndexOf(DateSep);
                if (j > 0)
                {
                    name = body.Substring(0, j);
                    DateTime d;
                    if (!DateTime.TryParseExact(body.Substring(j + 1), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                        return false; // date illisible = clé refusée (jamais « à vie » par accident)
                    if (DateTime.Now.Date > d.Date)
                    {
                        _expiredOn = d;
                        ActivateError = "Clé expirée le " + d.ToString("dd/MM/yyyy")
                            + " — renouvelle l'abonnement pour recevoir une nouvelle clé.";
                        return false;
                    }
                    until = d;
                }

                // VERROU MACHINE : une clé émise pour un PC précis ne s'active QUE sur ce PC.
                // (Les clés sans ID machine restent valables partout — compatibilité ascendante.)
                if (!string.IsNullOrEmpty(machine)
                    && !string.Equals(machine, MachineId.Current, StringComparison.OrdinalIgnoreCase))
                {
                    ActivateError = "Cette clé est liée à un autre ordinateur."
                        + "\r\nID de CE PC : " + MachineId.Current
                        + "\r\nDemande une clé émise pour cet identifiant.";
                    return false;
                }

                IsPro = true;
                Licensee = name;
                Expiry = until;
                BoundMachine = string.IsNullOrEmpty(machine) ? null : machine;
                _expiredOn = null;
                if (persist) { try { File.WriteAllText(StorePath, token.Trim()); } catch { } }
                return true;
            }
            catch { return false; }
        }

        public static string Status()
        {
            if (IsPro)
                return "Pro — licence : " + Licensee
                     + (Expiry.HasValue ? " (jusqu'au " + Expiry.Value.ToString("dd/MM/yyyy") + ")" : "");
            if (TrialActive) return "Essai Pro — " + TrialDaysLeft + " jour(s) restant(s)";
            if (_expiredOn.HasValue) return "Édition gratuite (licence expirée le " + _expiredOn.Value.ToString("dd/MM/yyyy") + ")";
            if (TrialUsed) return "Édition gratuite (essai Pro expiré)";
            return "Édition gratuite";
        }
    }
}
