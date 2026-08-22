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
        /// <summary>
        /// LA CLÉ PUBLIQUE DU VENDEUR. Elle doit correspondre à <c>seller/private.xml</c>, sinon
        /// PLUS AUCUNE clé ne s'active — ni les anciennes, ni celles que le générateur produira.
        ///
        /// Ça s'est produit. Le commit ab25e7c (« Centre de stockage », v15.34) a remplacé cette
        /// constante par le modulus d'une paire dont la moitié privée n'existe NULLE PART dans le
        /// dépôt. Son message ne mentionne pas les licences : la rotation n'était pas voulue, elle
        /// est passée avec le reste. Effet : les 7 licences de seller/licences-emises.csv sont
        /// devenues invalides d'un coup, et le générateur — qui signe toujours avec l'ancienne clé
        /// privée — n'aurait pas pu en émettre une seule qui fonctionne.
        ///
        /// Personne ne l'a vu tout de suite parce que l'échec de signature s'affiche « Clé
        /// invalide. Vérifie qu'elle est collée en entier. » : le message envoie chercher une
        /// faute de copier-coller. Il dit maintenant ce qui s'est réellement passé.
        ///
        /// RÈGLE : cette constante ne se modifie QUE si seller/private.xml est remplacé dans le
        /// même geste, et si toutes les clés déjà émises sont réémises. Une rotation de clé n'est
        /// pas un détail d'implémentation, c'est une invalidation de tout le parc.
        /// </summary>
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

        /// <summary>
        /// OÙ VIT LA CLÉ — ET POURQUOI PAS À CÔTÉ DE L'EXÉCUTABLE.
        ///
        /// <see cref="AppPaths"/> range les données dans le dossier de l'exe tant qu'il est
        /// inscriptible. C'est raisonnable pour un journal ou un cache. Ça ne l'est pas pour une
        /// licence : une clé appartient à la MACHINE et à la personne, pas à une copie du binaire.
        ///
        /// Conséquence constatée sur la machine de développement : <c>bin\Debug\</c>,
        /// <c>bin\Release\</c>, <c>dist\</c> et la version installée sont quatre dossiers
        /// inscriptibles, donc QUATRE licences séparées. Activer dans l'un laisse les trois autres
        /// en édition gratuite — d'où la clé qui « se désactive » à chaque compilation. Relevé
        /// ici : un seul bt-license.txt, dans dist\, et rien ailleurs.
        ///
        /// La clé quitte donc le dossier de l'exe. Elle est écrite dans PLUSIEURS emplacements
        /// indépendants (voir Emplacements) et une activation déjà faite à côté d'un exe est
        /// remontée au premier lancement : personne ne doit ressaisir sa clé parce qu'on a
        /// corrigé notre rangement.
        /// </summary>
        private static string StorePath
        {
            get
            {
                try
                {
                    string dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ONYX");
                    Directory.CreateDirectory(dir);
                    return Path.Combine(dir, "bt-license.txt");
                }
                catch
                {
                    // Profil illisible : on ne perd pas la licence pour autant, on retombe sur
                    // l'ancien emplacement plutôt que de ne rien pouvoir lire.
                    return AppPaths.File("bt-license.txt");
                }
            }
        }

        /// <summary>Nombre de PC distincts sur lesquels cette clé a été activée. 0 si inconnue.</summary>
        public static int MachinesVues { get; private set; }

        /// <summary>Date de la première activation de la clé, tous PC confondus.</summary>
        public static DateTime? LieeDepuis { get; private set; }

        /// <summary>Emplacement machine, hors du profil utilisateur.</summary>
        private static string SharedStorePath
        {
            get
            {
                return Path.Combine(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ONYX"),
                    "bt-license.txt");
            }
        }

        /// <summary>Ancien emplacement : à côté de l'exécutable. Encore lu, pour remonter une
        /// activation faite avant la correction.</summary>
        private static string LegacyStorePath { get { return AppPaths.File("bt-license.txt"); } }

        private const string RegLicenseValue = "License";

        /// <summary>
        /// LES QUATRE ENDROITS OÙ LA LICENCE VIT, ET POURQUOI QUATRE.
        ///
        /// L'installeur supprime <c>{localappdata}\ONYX</c> à la désinstallation — ligne 190 de
        /// BTOptimizer.iss. Une licence rangée là seule DISPARAÎT quand quelqu'un désinstalle puis
        /// réinstalle, et il doit ressaisir sa clé alors qu'il n'a rien perdu d'autre.
        ///
        /// Elle est donc écrite aussi dans %PROGRAMDATA%\ONYX et dans HKLM — deux endroits que le
        /// désinstalleur ne touche pas — et à côté de l'exe pour les versions antérieures. La
        /// lecture prend le PREMIER survivant et RESSÈME les autres : effacer un emplacement ne
        /// coûte donc plus rien, et la licence se répare toute seule au lancement suivant.
        ///
        /// Ce n'est pas une protection anti-copie — c'en serait une très mauvaise, tout est en
        /// clair. C'est l'inverse : une assurance contre la perte d'une licence PAYÉE.
        /// </summary>
        private static string[] Emplacements()
        {
            return new[] { StorePath, SharedStorePath, LegacyStorePath };
        }

        /// <summary>Lit le premier emplacement survivant, puis ressème les autres. Null si aucun.</summary>
        private static LicenceLiaison.Enregistrement ChargeEnregistrement()
        {
            LicenceLiaison.Enregistrement e = null;
            foreach (string p in Emplacements())
            {
                try { if (File.Exists(p)) { e = LicenceLiaison.Lit(File.ReadAllText(p)); if (e != null) break; } }
                catch { }
            }
            if (e == null)
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(TrialRegPath))
                        if (k != null) e = LicenceLiaison.Lit(k.GetValue(RegLicenseValue) as string);
                }
                catch { }
            }
            if (e != null) Enregistre(e);   // ressème : la licence se répare d'elle-même
            return e;
        }

        /// <summary>Écrit partout. Chaque emplacement est isolé : un dossier protégé ne doit pas
        /// empêcher d'écrire les autres.</summary>
        private static void Enregistre(LicenceLiaison.Enregistrement e)
        {
            if (e == null || string.IsNullOrEmpty(e.Token)) return;
            string texte = LicenceLiaison.Rend(e);
            foreach (string p in Emplacements())
            {
                try
                {
                    string d = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
                    File.WriteAllText(p, texte);
                }
                catch { }
            }
            try
            {
                using (RegistryKey k = Registry.LocalMachine.CreateSubKey(TrialRegPath))
                    if (k != null) k.SetValue(RegLicenseValue, texte, RegistryValueKind.String);
            }
            catch { }
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
            // Le chargement RELIE aussi : une clé déjà activée doit se lier au PC où elle
            // tourne, même si elle a été saisie avant que la liaison automatique existe.
            try
            {
                LicenceLiaison.Enregistrement e = ChargeEnregistrement();
                if (e != null && Activate(e.Token, false)) AppliqueLiaison(e, false);
            }
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
                    if (!ok)
                    {
                        // La clé est bien formée, mais elle n'a pas été signée par la clé privée
                        // que cette version connaît. Dire « vérifie ton copier-coller » enverrait
                        // chercher pendant des heures une faute de frappe qui n'existe pas —
                        // c'est exactement ce qui est arrivé après la rotation accidentelle.
                        ActivateError = "Cette clé n'a pas été signée par la clé de cette version d'ONYX."
                            + "\r\nLe copier-coller n'est pas en cause : la clé est lisible, "
                            + "c'est la signature qui est refusée."
                            + "\r\nDemande une clé réémise avec la version actuelle.";
                        return false;
                    }
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
                if (persist)
                {
                    // LIAISON AUTOMATIQUE : la clé se lie au PC dès qu'elle est saisie. Plus rien
                    // à demander au vendeur, et plus d'identifiant à recopier à la main.
                    LicenceLiaison.Enregistrement e = ChargeEnregistrement();
                    if (e == null || !string.Equals(e.Token, token.Trim(), StringComparison.Ordinal))
                        e = new LicenceLiaison.Enregistrement { Token = token.Trim() };
                    AppliqueLiaison(e, true);
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Relie la clé à CE PC et publie ce qu'on en sait. <paramref name="ecrire"/> à faux
        /// pendant le chargement : on ne réécrit pas les quatre emplacements à chaque lancement
        /// tant que rien n'a changé.
        /// </summary>
        private static void AppliqueLiaison(LicenceLiaison.Enregistrement e, bool ecrire)
        {
            if (e == null) return;
            string ici = MachineId.Current;
            LicenceLiaison.Liaison d = LicenceLiaison.Decide(e, ici);
            LicenceLiaison.Relie(e, ici, DateTime.Now);

            MachinesVues = e.Machines.Count;
            LieeDepuis = e.Depuis == DateTime.MinValue ? (DateTime?)null : e.Depuis;
            if (BoundMachine == null) BoundMachine = e.Machine;   // liaison douce, pas le verrou signé

            if (ecrire || d != LicenceLiaison.Liaison.Meme) Enregistre(e);
        }

        public static string Status()
        {
            if (IsPro)
                return "Pro — licence : " + Licensee
                     + (Expiry.HasValue ? " (jusqu'au " + Expiry.Value.ToString("dd/MM/yyyy") + ")" : "")
                     + (LieeDepuis.HasValue
                        ? " — liée à ce PC depuis le " + LieeDepuis.Value.ToString("dd/MM/yyyy")
                        : "");
            if (TrialActive) return "Essai Pro — " + TrialDaysLeft + " jour(s) restant(s)";
            if (_expiredOn.HasValue) return "Édition gratuite (licence expirée le " + _expiredOn.Value.ToString("dd/MM/yyyy") + ")";
            if (TrialUsed) return "Édition gratuite (essai Pro expiré)";
            return "Édition gratuite";
        }
    }
}
