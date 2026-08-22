using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// COMBIEN DE GENS UTILISENT ONYX — le strict minimum pour répondre à cette question.
    ///
    /// Une fois par jour au plus, ONYX envoie trois choses : un identifiant de machine, la version
    /// installée, et le numéro de version de Windows. Rien d'autre. Pas de nom d'utilisateur, pas
    /// de nom de machine, pas d'adresse, pas de liste de matériel, pas de jeux, pas de réglages.
    ///
    /// HONNÊTETÉ SUR LE MOT « ANONYME ». L'identifiant est PSEUDONYME, pas anonyme. Il est stable
    /// (c'est ce qui permet de distinguer un utilisateur qui revient d'un nouvel utilisateur), donc
    /// il désigne toujours la même machine. On ne peut pas remonter de cet identifiant vers la
    /// machine — il est haché — mais deux envois de la même machine restent reconnaissables entre
    /// eux. Le RGPD considère cela comme une donnée personnelle pseudonymisée, pas comme une
    /// donnée anonyme. C'est pour ça que le réglage est visible et se coupe en un clic.
    ///
    /// Ce que ça permet de savoir : combien de machines distinctes lancent ONYX par jour et par
    /// mois, et quelles versions sont installées. Ce que ça ne permet PAS de savoir : qui, où, ni
    /// ce que la personne fait dans l'application.
    ///
    /// Sans point de collecte configuré, ce module ne fait RIEN — pas de requête, pas d'erreur.
    /// </summary>
    internal static class Audience
    {
        /// <summary>
        /// Adresse du point de collecte. VIDE = fonction inerte.
        ///
        /// Elle est délibérément séparée du code : le service se déploie à part (voir
        /// tools/audience-worker.js), et tant que cette valeur est vide, ONYX n'envoie rien.
        /// </summary>
        public static string PointDeCollecte
        {
            get
            {
                try
                {
                    string f = AppPaths.File("bt-audience-url.txt");
                    if (File.Exists(f))
                    {
                        string s = File.ReadAllText(f).Trim();
                        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return s;
                    }
                }
                catch { }
                return "";
            }
        }

        private static string CheminReglage { get { return AppPaths.File("bt-audience.txt"); } }
        private static string CheminDernier { get { return AppPaths.File("bt-audience-dernier.txt"); } }

        /// <summary>Actif par défaut ; « 0 » dans le fichier = coupé par l'utilisateur.</summary>
        public static bool Active
        {
            get { try { return !File.Exists(CheminReglage) || File.ReadAllText(CheminReglage).Trim() != "0"; } catch { return true; } }
            set { try { File.WriteAllText(CheminReglage, value ? "1" : "0"); } catch { } }
        }

        // ------------------------------------------------------------------ parties pures

        /// <summary>
        /// PUR : l'identifiant envoyé, dérivé d'une valeur propre à l'installation de Windows.
        ///
        /// Haché avec un sel fixe et tronqué à 32 caractères : on ne peut pas revenir à la valeur
        /// d'origine, et elle ne quitte donc jamais la machine telle quelle. Rend une chaîne vide
        /// si l'entrée est vide — mieux vaut ne rien envoyer qu'un identifiant bidon qui gonflerait
        /// artificiellement le compte.
        /// </summary>
        public static string Identifiant(string valeurMachine)
        {
            if (string.IsNullOrWhiteSpace(valeurMachine)) return "";
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("onyx-audience-v1:" + valeurMachine.Trim()));
                var sb = new StringBuilder(32);
                for (int i = 0; i < 16; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// PUR : faut-il envoyer ? Une fois par jour au plus.
        ///
        /// Une date de dernier envoi DANS LE FUTUR (horloge remise à l'heure, fuseau changé, fichier
        /// bricolé) doit autoriser l'envoi : sinon une machine mal réglée cesserait d'être comptée
        /// pour toujours.
        /// </summary>
        public static bool DoitEnvoyer(DateTime dernier, DateTime maintenant)
        {
            if (dernier == DateTime.MinValue) return true;
            if (dernier > maintenant) return true;
            return (maintenant - dernier).TotalHours >= 24;
        }

        /// <summary>PUR : le corps envoyé. Trois champs, et rien de plus — c'est vérifiable ici.</summary>
        public static string Charge(string identifiant, string version, string windows)
        {
            return "{\"id\":\"" + Echappe(identifiant) + "\","
                 + "\"version\":\"" + Echappe(version) + "\","
                 + "\"windows\":\"" + Echappe(windows) + "\"}";
        }

        /// <summary>PUR : échappement JSON minimal — et surtout, tout ce qui n'est pas
        /// alphanumérique simple est écarté, pour qu'aucune donnée inattendue ne parte.</summary>
        public static string Echappe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_') sb.Append(c);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ entrées-sorties

        /// <summary>Valeur propre à l'installation de Windows, jamais envoyée telle quelle.</summary>
        private static string ValeurMachine()
        {
            try
            {
                object v = Sys.GetMachine(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid");
                return Convert.ToString(v) ?? "";
            }
            catch { return ""; }
        }

        private static DateTime DernierEnvoi()
        {
            try
            {
                if (!File.Exists(CheminDernier)) return DateTime.MinValue;
                DateTime d;
                if (DateTime.TryParse(File.ReadAllText(CheminDernier).Trim(), CultureInfo.InvariantCulture,
                                      DateTimeStyles.RoundtripKind, out d)) return d;
            }
            catch { }
            return DateTime.MinValue;
        }

        /// <summary>
        /// Envoie au plus une fois par jour, en tâche de fond, sans jamais retarder le démarrage
        /// ni signaler quoi que ce soit en cas d'échec — une mesure d'audience ne doit gêner
        /// personne, surtout pas dans une application dont le sujet est la latence.
        /// </summary>
        public static void PingSiActive()
        {
            if (!Active) return;
            string url = PointDeCollecte;
            if (url.Length == 0) return;                      // aucun point de collecte : on ne fait rien
            if (!DoitEnvoyer(DernierEnvoi(), DateTime.UtcNow)) return;

            var t = new System.Threading.Thread(() =>
            {
                try
                {
                    string id = Identifiant(ValeurMachine());
                    if (id.Length == 0) return;
                    string ver = "";
                    try { var v = typeof(Audience).Assembly.GetName().Version; ver = v.Major + "." + v.Minor.ToString("00"); } catch { }
                    string win = "";
                    try { win = Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture); } catch { }

                    using (var http = new System.Net.Http.HttpClient())
                    {
                        http.Timeout = TimeSpan.FromSeconds(8);
                        var contenu = new System.Net.Http.StringContent(
                            Charge(id, ver, win), Encoding.UTF8, "application/json");
                        var rep = http.PostAsync(url, contenu).GetAwaiter().GetResult();
                        if (!rep.IsSuccessStatusCode) return;   // on ne note PAS la date : on réessaiera
                    }
                    try { File.WriteAllText(CheminDernier, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)); } catch { }
                }
                catch { }
            });
            t.IsBackground = true;
            t.Name = "OnyxAudience";
            t.Start();
        }
    }
}
