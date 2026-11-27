using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BTOptimizer
{
    /// <summary>
    /// Page Jeux, logique PURE (ni disque ni registre ni réseau) :
    ///   - tri jeu / application (manifestes Epic, indices de jeu d'un dossier, launchers, outils WinGet) ;
    ///   - jaquettes Epic lues dans le cache local du launcher (catcache.bin), sans réseau ni clé ;
    ///   - reconnaissance d'un même jeu possédé sur plusieurs plateformes.
    /// </summary>
    internal static class BibliothequeJeux
    {
        // ---------------------------------------------------------------- Epic

        public sealed class Epic
        {
            public string Nom, Dossier, Exe, Id;
            public bool EstJeu;
        }

        /// <summary>
        /// Manifeste .item d'Epic. Un jeu porte la catégorie « games » ; moteurs, plugins, assets et
        /// extensions d'un autre jeu (MainGameAppName ≠ AppName : éditeur UEFN, contenu LEGO) n'en sont pas.
        /// Un ancien manifeste sans catégories est gardé, sauf s'il s'agit d'une extension.
        /// </summary>
        public static Epic EpicManifeste(string json)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json ?? ""))
                {
                    JsonElement r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object) return null;
                    string nom = Chaine(r, "DisplayName");
                    if (string.IsNullOrWhiteSpace(nom)) return null;
                    string app = Chaine(r, "AppName") ?? "", principal = Chaine(r, "MainGameAppName") ?? "";
                    bool extension = principal.Length > 0 && !string.Equals(principal, app, StringComparison.OrdinalIgnoreCase);
                    bool jeu = true;
                    JsonElement cats;
                    if (r.TryGetProperty("AppCategories", out cats) && cats.ValueKind == JsonValueKind.Array)
                    {
                        jeu = false;
                        foreach (JsonElement c in cats.EnumerateArray())
                            if (c.ValueKind == JsonValueKind.String && string.Equals(c.GetString(), "games", StringComparison.OrdinalIgnoreCase)) jeu = true;
                    }
                    return new Epic
                    {
                        Nom = nom.Trim(), Dossier = Chaine(r, "InstallLocation"), Exe = Chaine(r, "LaunchExecutable"),
                        Id = Chaine(r, "CatalogItemId"), EstJeu = jeu && !extension
                    };
                }
            }
            catch { return null; }
        }

        /// <summary>Types d'images du catalogue Epic, du plus adapté (jaquette verticale) au moins adapté.</summary>
        private static readonly string[] TypesImage = { "DieselGameBoxTall", "OfferImageTall", "DieselGameBox", "OfferImageWide", "Thumbnail" };

        /// <summary>
        /// Cache du catalogue Epic (ProgramData\Epic\EpicGamesLauncher\Data\Catalog\catcache.bin : JSON en base64)
        /// → jaquette par CatalogItemId. Seul le CDN d'Epic en https est accepté ; l'image est demandée réduite.
        /// </summary>
        public static Dictionary<string, string> ImagesEpic(string base64)
        {
            var res = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                byte[] brut = Convert.FromBase64String((base64 ?? "").Trim());
                using (JsonDocument doc = JsonDocument.Parse(brut))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) return res;
                    foreach (JsonElement it in doc.RootElement.EnumerateArray())
                    {
                        if (it.ValueKind != JsonValueKind.Object) continue;
                        string id = Chaine(it, "id");
                        JsonElement imgs;
                        if (string.IsNullOrEmpty(id) || !it.TryGetProperty("keyImages", out imgs) || imgs.ValueKind != JsonValueKind.Array) continue;
                        string choisie = null; int rang = TypesImage.Length;
                        foreach (JsonElement k in imgs.EnumerateArray())
                        {
                            if (k.ValueKind != JsonValueKind.Object) continue;
                            int r = Array.IndexOf(TypesImage, Chaine(k, "type"));
                            string url = Chaine(k, "url");
                            if (r < 0 || r >= rang || !CdnEpic(url)) continue;
                            rang = r; choisie = url;
                        }
                        if (choisie != null) res[id] = choisie + (choisie.IndexOf('?') < 0 ? "?h=480&resize=1" : "");
                    }
                }
            }
            catch { }
            return res;
        }

        private static bool CdnEpic(string url)
        {
            Uri u;
            return url != null && Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps
                && (u.Host.Equals("cdn1.epicgames.com", StringComparison.OrdinalIgnoreCase) || u.Host.Equals("cdn2.unrealengine.com", StringComparison.OrdinalIgnoreCase));
        }

        // ---------------------------------------------------------------- programmes du registre

        /// <summary>Fichiers qui ne se trouvent que dans un jeu (moteurs, vidéos Bink, archives de données, Steamworks).</summary>
        private static readonly string[] FichiersDeJeu =
        {
            "unityplayer.dll", "gameassembly.dll", "steam_api.dll", "steam_api64.dll", "binkw32.dll", "binkw64.dll",
            "bink2w32.dll", "bink2w64.dll", "physxloader.dll", "xinput1_3.dll", "d3dx9_43.dll", "eossdk-win64-shipping.dll",
            "data.win", "globalgamemanagers"
        };

        private static readonly string[] ExtensionsDeJeu = { ".pak", ".vpk", ".bsa", ".ba2", ".big", ".bik", ".bk2", ".upk", ".forge", ".rpf", ".pck", ".wad", ".rgssad", ".u" };

        private static readonly string[] PrefixesDeJeu = { "fmod", "d3dx9_", "physx3" };

        /// <summary>
        /// Vrai si l'un des chemins (relatifs au dossier d'installation) trahit un jeu. Sert à écarter les
        /// applications déclarées dans le registre (Wireshark, PS Remote Play, LuaTools…) sans liste noire.
        /// </summary>
        public static bool IndiceDeJeu(IEnumerable<string> fichiers)
        {
            if (fichiers == null) return false;
            foreach (string f in fichiers)
            {
                if (string.IsNullOrEmpty(f)) continue;
                string nom = Path.GetFileName(f.Replace('/', '\\')).ToLowerInvariant();
                if (Array.IndexOf(FichiersDeJeu, nom) >= 0) return true;
                if (Array.IndexOf(ExtensionsDeJeu, Path.GetExtension(nom)) >= 0) return true;
                if (nom.EndsWith(".dll", StringComparison.Ordinal))
                    foreach (string p in PrefixesDeJeu) if (nom.StartsWith(p, StringComparison.Ordinal)) return true;
                string bas = f.ToLowerInvariant();
                if (bas.Contains("easyanticheat") || bas.Contains("battleye")) return true;
            }
            return false;
        }

        /// <summary>Paquet posé par WinGet (outils en ligne de commande : jq, make, gitleaks…).</summary>
        public static bool CheminOutil(string dossier)
        {
            return dossier != null && dossier.IndexOf(@"\WinGet\Packages\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static readonly string[] Launchers =
        {
            "battle.net", "ea app", "ea desktop", "origin", "epic games launcher", "ubisoft connect", "uplay", "gog galaxy",
            "steam", "riot client", "rockstar games launcher", "minecraft launcher", "xbox", "xbox app"
        };

        /// <summary>Le launcher lui-même (nom exact, pour ne pas écarter « The Seven Deadly Sins: Origin »).</summary>
        public static bool EstLauncher(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom)) return false;
            string n = nom.Trim();
            foreach (string l in Launchers) if (string.Equals(n, l, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Types du store Steam qui ne sont pas des jeux : film, guide, logiciel, vidéo, série, épisode, matériel, musique, outil, publicité.</summary>
        private static readonly int[] TypesNonJeu = { 3, 5, 6, 7, 8, 9, 10, 11, 13, 14 };

        /// <summary>
        /// AppID des logiciels vendus sur Steam (Wallpaper Engine, 3DMark, Lossless Scaling : type 6) dans une
        /// réponse IStoreBrowseService/GetItems. Sans type ou fiche ratée : considéré comme un jeu.
        /// </summary>
        public static HashSet<int> LogicielsSteam(string json)
        {
            var res = new HashSet<int>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json ?? ""))
                {
                    JsonElement r, items;
                    if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("response", out r)
                        || r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("store_items", out items) || items.ValueKind != JsonValueKind.Array) return res;
                    foreach (JsonElement it in items.EnumerateArray())
                    {
                        int id, type, ok;
                        if (Entier(it, "success", out ok) && ok == 1 && Entier(it, "appid", out id) && id > 0
                            && Entier(it, "type", out type) && Array.IndexOf(TypesNonJeu, type) >= 0) res.Add(id);
                    }
                }
            }
            catch { }
            return res;
        }

        private static bool Entier(JsonElement e, string nom, out int v)
        {
            JsonElement x;
            v = 0;
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nom, out x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt32(out v);
        }

        // ---------------------------------------------------------------- catalogue et désinstallation

        private static readonly string[] MotsOutils = { "toolchain", "installer", "sdk", "compiler", "dedicated server", "mod tools", "redistributable" };

        /// <summary>Le nom désigne un outil (serveur dédié, SDK, redistribuable…), pas un jeu.</summary>
        public static bool EstOutil(string nom)
        {
            if (string.IsNullOrEmpty(nom)) return false;
            foreach (string o in MotsOutils) if (nom.IndexOf(o, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Le mot-clé du catalogue apparaît comme mot entier dans le nom d'un programme installé, et ce
        /// programme n'est pas un outil (« Rustup: the Rust toolchain installer » ne veut pas dire Rust le jeu).
        /// </summary>
        public static bool MotCleDans(string nom, string motCle)
        {
            if (string.IsNullOrWhiteSpace(nom) || string.IsNullOrWhiteSpace(motCle)) return false;
            foreach (string o in MotsOutils) if (nom.IndexOf(o, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            string kw = motCle.Trim();
            for (int i = nom.IndexOf(kw, StringComparison.OrdinalIgnoreCase); i >= 0; i = nom.IndexOf(kw, i + 1, StringComparison.OrdinalIgnoreCase))
            {
                bool avant = i == 0 || !char.IsLetterOrDigit(nom[i - 1]);
                int fin = i + kw.Length;
                bool apres = fin >= nom.Length || !char.IsLetterOrDigit(nom[fin]);
                if (avant && apres) return true;
                if (i + 1 >= nom.Length) break;
            }
            return false;
        }

        // ---------------------------------------------------------------- même jeu, plusieurs plateformes

        private static readonly HashSet<string> MotsEdition = new HashSet<string>(StringComparer.Ordinal)
        {
            "edition", "ultimate", "deluxe", "gold", "goty", "definitive", "complete", "standard", "premium", "enhanced"
        };

        /// <summary>
        /// Clé de comparaison : minuscules, lettres et chiffres, sans ™ ® ni mention d'édition finale
        /// (« Control Ultimate Edition » = « CONTROL™ »). Un nom fait seulement de ces mots reste entier.
        /// </summary>
        public static string Cle(string nom)
        {
            if (string.IsNullOrWhiteSpace(nom)) return "";
            var mots = new List<string>();
            var sb = new StringBuilder();
            foreach (char c in nom.ToLowerInvariant() + " ")
            {
                if (char.IsLetterOrDigit(c)) { sb.Append(c); continue; }
                if (c == '™' || c == '®' || c == '©') continue;
                if (sb.Length > 0) { mots.Add(sb.ToString()); sb.Clear(); }
            }
            int n = mots.Count;
            while (n > 0)
            {
                if (MotsEdition.Contains(mots[n - 1])) { n--; continue; }
                if (n >= 4 && mots[n - 4] == "game" && mots[n - 3] == "of" && mots[n - 2] == "the" && mots[n - 1] == "year") { n -= 4; continue; }
                break;
            }
            if (n == 0) n = mots.Count;
            return string.Concat(mots.GetRange(0, n));
        }

        /// <summary>
        /// Deux entrées désignent le même jeu : même AppID Steam ; ou, sans AppID contradictoire, même clé
        /// de nom, ou nom du launcher égal à un mot-clé du catalogue (« Overwatch » = « Overwatch 2 »).
        /// </summary>
        public static bool MemeJeu(string nomA, int idA, string[] motsA, string nomB, int idB)
        {
            if (idA > 0 && idB > 0) return idA == idB;
            string b = Cle(nomB);
            if (b.Length == 0) return false;
            if (Cle(nomA) == b) return true;
            if (motsA != null) foreach (string m in motsA) if (Cle(m) == b) return true;
            return false;
        }

        /// <summary>Ajoute une plateforme (en majuscules) si elle n'y est pas déjà.</summary>
        public static void AjouteStore(List<string> stores, string store)
        {
            if (stores == null || string.IsNullOrWhiteSpace(store)) return;
            string s = store.Trim().ToUpperInvariant();
            if (!stores.Contains(s)) stores.Add(s);
        }

        /// <summary>Texte de la pastille d'une carte : « STEAM + EPIC », ou « Autres » si inconnu.</summary>
        public static string Pastille(List<string> stores)
        {
            return stores == null || stores.Count == 0 ? "Autres" : string.Join(" + ", stores);
        }

        private static string Chaine(JsonElement e, string nom)
        {
            JsonElement v;
            return e.TryGetProperty(nom, out v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
    }
}
