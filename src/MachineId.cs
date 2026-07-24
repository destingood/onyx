using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Empreinte STABLE de l'ordinateur, utilisée pour lier une clé de licence à UN seul PC.
    /// Basée sur le MachineGuid de Windows (unique par installation, lisible sans droits admin,
    /// stable au fil des redémarrages et des mises à jour), haché en SHA-256 puis rendu en
    /// 20 caractères lisibles/dictables (pas de I, O, 0, 1 pour éviter les confusions).
    ///
    /// Change si Windows est réinstallé (ou disque système remplacé) → il faut alors ré-émettre
    /// la clé pour le client. C'est le comportement normal d'une licence liée à une machine.
    /// </summary>
    internal static class MachineId
    {
        private static string _cache;

        /// <summary>Ex. « A3F7K-M2NPQ-R8TUV-W9XYZ ». Calculé une fois puis mis en cache.</summary>
        public static string Current
        {
            get
            {
                if (_cache == null) _cache = Compute();
                return _cache;
            }
        }

        private static string Compute()
        {
            string raw = null;
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = b.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                    if (k != null) raw = k.GetValue("MachineGuid") as string;
            }
            catch { }

            if (string.IsNullOrEmpty(raw))
            {
                // Repli : jamais vide, pour ne pas produire deux PC avec le même identifiant.
                try { raw = Environment.MachineName + "|" + Environment.ProcessorCount + "|" + Environment.OSVersion.Version; }
                catch { raw = "inconnu"; }
            }

            byte[] h;
            using (SHA256 sha = SHA256.Create())
                h = sha.ComputeHash(Encoding.UTF8.GetBytes("ONYX-machine|" + raw));
            return Format(h);
        }

        // Alphabet sans I, O, 0, 1 : lisible et dictable au téléphone / par mail.
        private const string Alpha = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

        private static string Format(byte[] h)
        {
            var sb = new StringBuilder(23);
            for (int i = 0; i < 20; i++)
            {
                if (i > 0 && i % 5 == 0) sb.Append('-');
                sb.Append(Alpha[h[i] % Alpha.Length]);
            }
            return sb.ToString();
        }
    }
}
