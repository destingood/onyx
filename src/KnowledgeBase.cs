using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Base de connaissances LOCALE (RAG) du Copilote : une base PC/gaming intégrée + les
    /// documents que l'utilisateur dépose dans le dossier « bt-savoir\ », vectorisés via Ollama
    /// (nomic-embed-text). À chaque question, on récupère les extraits les plus pertinents et on
    /// les donne au modèle AVANT qu'il réponde → réponses ancrées et précises. 100 % local,
    /// gratuit ; l'index est mis en cache (bt-kb-index.txt) et reconstruit si le savoir change.
    /// </summary>
    internal static class KnowledgeBase
    {
        private sealed class Chunk { public string Source; public string Text; public float[] Vec; public DateTime When; }

        // Fraîcheur : au-delà de ce délai, un document de bt-savoir est signalé « peut-être daté »
        // (le savoir périmé est LA cause n°1 d'échec RAG en entreprise — on le rend visible).
        private const int StaleMonths = 18;
        internal static bool IsStale(DateTime when) { return when != DateTime.MinValue && when < DateTime.Now.AddMonths(-StaleMonths); }
        /// <summary>Étiquette de fraîcheur d'une source (« · maj 03/2024 · ⚠ peut-être daté »), ou "" si intégré.</summary>
        internal static string FreshTag(DateTime when)
        {
            if (when == DateTime.MinValue) return "";
            return " · maj " + when.ToString("MM/yyyy") + (IsStale(when) ? " · ⚠ peut-être daté" : "");
        }

        private static readonly object Gate = new object();
        private static List<Chunk> _index;          // en mémoire (chargé/reconstruit une fois)
        private static string _signature;           // empreinte du savoir source (détecte un changement)

        private static string Dir { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-savoir"); } }
        private static string IndexPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bt-kb-index.txt"); } }

        // --- Base PC/gaming INTÉGRÉE (chaque ligne = un fait interrogeable) ---
        private static readonly string[] Builtin =
        {
            "Un crash dont le module fautif est nvlddmkm, nvwgf2umx, amdkmdag ou igdkmd vient du PILOTE GRAPHIQUE : le réinstaller proprement avec DDU (gratuit) et mettre le pilote à jour.",
            "Le code d'exception c0000005 est une violation d'accès mémoire : souvent une RAM instable (profil XMP/EXPO trop agressif), un overclock, ou des fichiers Windows abîmés (réparer avec DISM puis SFC).",
            "Le code c0000409 est un dépassement de tampon (stack buffer overrun) : mettre l'application à jour et vérifier l'intégrité de ses fichiers.",
            "Une erreur msvcp140.dll, vcruntime140.dll ou msvcr signale un Visual C++ Redistributable manquant : l'installer (gratuit) résout la plupart des jeux qui ne démarrent pas.",
            "L'exception e0434352 est une exception .NET non gérée : installer ou réparer le .NET Desktop Runtime.",
            "Un jeu bloqué à 60 FPS alors que l'écran est 144/240 Hz : passer l'écran à sa fréquence max (Paramètres d'affichage) — gain de fluidité immédiat et gratuit.",
            "En jeu en ligne, ce qui compte n'est pas le débit mais la stabilité : un ping bas ET régulier. La gigue (variation du ping) et la perte de paquets causent des à-coups ; préférer un câble Ethernet au Wi-Fi.",
            "Un GPU au-dessus de 85 °C se bride tout seul (throttling) et les FPS s'effondrent : nettoyer la poussière, améliorer le flux d'air, régler une courbe de ventilation (Fan Control, gratuit).",
            "Activer le profil XMP/EXPO dans le BIOS fait tourner la RAM à sa vraie vitesse : des FPS gratuits déjà payés. Sans lui, la RAM tourne bridée.",
            "DLSS (NVIDIA) et FSR (AMD) calculent l'image en plus petit puis l'agrandissent : beaucoup de FPS gagnés pour une perte visuelle minime, à activer dans les options du jeu.",
            "HAGS (planification GPU accélérée matériel) aide ou gêne selon les jeux et pilotes : c'est un réglage à tester, réversible.",
            "Un DNS lent ralentit chaque connexion à un serveur ou une boutique en jeu : basculer vers Cloudflare (1.1.1.1) ou Google (8.8.8.8), gratuit et réversible.",
            "Windows Update qui remplace un pilote GPU fraîchement installé est une cause classique du pilote qui « revient tout seul » après un DDU : empêcher Windows d'installer des pilotes.",
            "Les fonds d'écran animés (Wallpaper Engine, Lively) sont de vrais tueurs de FPS : les couper avant une session de jeu.",
            "Un anti-triche (EasyAntiCheat, BattlEye, Vanguard/vgc) qui plante : le réparer/réinstaller depuis le dossier du jeu et s'assurer qu'aucun autre anti-triche ne tourne.",
            "Le TRIM garde un SSD rapide dans la durée ; certains « optimiseurs » le coupent à tort. Le fichier d'échange (pagefile) ne doit pas être désactivé, sinon plantages « out of memory ».",
            "Un PC qui n'a pas VRAIMENT redémarré depuis longtemps (le démarrage rapide de Windows endort au lieu d'éteindre) accumule fuites et états bancals : un vrai redémarrage règle beaucoup de soucis, gratuitement.",
            "Écran noir au démarrage : vérifier le câble et la bonne entrée, brancher l'écran sur la CARTE GRAPHIQUE (pas la carte mère), faire un reset d'alimentation (bouton power 15 s débranché), et passer par le mode sans échec (3 arrêts forcés).",
            "Plus d'internet alors que tout est branché : réinitialiser la pile réseau (Winsock + TCP/IP + DNS), souvent cassée par un VPN ou un antivirus, puis redémarrer.",
            "Plus de son : relancer le moteur audio de Windows (services), vérifier le bon périphérique de sortie et le volume de l'application.",
            "La réparation d'intégrité de Windows (DISM /RestoreHealth puis SFC /scannow) répare les fichiers système corrompus : le remède aux soucis « impossibles à régler » qui persistent malgré tout.",
            "Trop de programmes au démarrage ralentissent l'allumage et restent en fond : en couper (réversible) accélère le PC sans rien désinstaller.",
        };

        /// <summary>Empreinte du savoir SOURCE (intégré + fichiers utilisateur + modèle) : sert à
        /// savoir s'il faut reconstruire l'index.</summary>
        private static string Signature(List<Chunk> source)
        {
            var sb = new StringBuilder(LocalBrain.EmbedModelName() ?? "none").Append('|');
            foreach (var c in source) sb.Append(c.Source).Append(':').Append(c.Text.Length).Append(';');
            int h = sb.ToString().GetHashCode();
            return h.ToString();
        }

        // Assemble les morceaux SOURCE (intégré + dossier utilisateur), sans vecteurs.
        private static List<Chunk> Collect()
        {
            var list = new List<Chunk>();
            foreach (string t in Builtin) list.Add(new Chunk { Source = "PC/gaming (intégré)", Text = t, When = DateTime.MinValue });
            try
            {
                if (Directory.Exists(Dir))
                    // Récursif : les SOUS-DOSSIERS organisent le savoir (livres/chapitres, façon
                    // wiki BookStack). Formats des exports BookStack : Markdown, texte, HTML.
                    foreach (string f in Directory.GetFiles(Dir, "*", SearchOption.AllDirectories))
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        bool img = ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".tif" || ext == ".tiff";
                        if (ext != ".txt" && ext != ".md" && ext != ".markdown" && ext != ".html" && ext != ".htm"
                            && ext != ".pdf" && ext != ".docx" && !img) continue;
                        string raw;
                        if (ext == ".pdf") { raw = ExtractPdf(f); if (string.IsNullOrEmpty(raw)) continue; }
                        else if (ext == ".docx") { raw = ExtractDocx(f); if (string.IsNullOrEmpty(raw)) continue; }
                        else if (img) { raw = Ocr.Read(f); if (string.IsNullOrEmpty(raw)) continue; }   // OCR (captures, photos de manuel)
                        else { try { raw = File.ReadAllText(f); } catch { continue; } }
                        if (ext == ".html" || ext == ".htm") raw = StripHtml(raw);
                        string src = RelSource(f);
                        DateTime when; try { when = File.GetLastWriteTime(f); } catch { when = DateTime.Now; }
                        foreach (string ch in SplitChunks(raw, 600))
                            list.Add(new Chunk { Source = src, Text = ch, When = when });
                    }
            }
            catch { }
            return list;
        }

        // Source lisible = chemin RELATIF sous bt-savoir (ex. « Reseau/DNS.md ») → montre l'arbo.
        private static string RelSource(string full)
        {
            try
            {
                string rel = full.Substring(Dir.Length).TrimStart('\\', '/');
                return rel.Length > 0 ? rel : Path.GetFileName(full);
            }
            catch { return Path.GetFileName(full); }
        }

        // Texte d'un PDF (manuels, fiches, articles…) via PdfPig — 100 % local, aucun OCR
        // (les PDF scannés-image ne rendent rien : c'est attendu).
        private static string ExtractPdf(string path)
        {
            try
            {
                var sb = new StringBuilder();
                using (var doc = UglyToad.PdfPig.PdfDocument.Open(path))
                    foreach (var page in doc.GetPages())
                    {
                        sb.Append(page.Text).Append('\n');
                        if (sb.Length > 200000) break;   // garde-fou sur un très gros PDF
                    }
                return sb.ToString();
            }
            catch { return ""; }
        }

        // Texte d'un .docx (Word) : c'est un ZIP ; on lit word/document.xml et on retire le balisage.
        // Sans dépendance (System.IO.Compression). Les paragraphes deviennent des sauts de ligne.
        private static string ExtractDocx(string path)
        {
            try
            {
                using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
                {
                    var entry = zip.GetEntry("word/document.xml");
                    if (entry == null) return "";
                    string xml;
                    using (var sr = new StreamReader(entry.Open())) xml = sr.ReadToEnd();
                    xml = System.Text.RegularExpressions.Regex.Replace(xml, "(?i)</w:p>", "\n");   // fin de paragraphe → saut
                    xml = System.Text.RegularExpressions.Regex.Replace(xml, "(?s)<[^>]+>", "");     // retire les balises
                    return System.Net.WebUtility.HtmlDecode(xml);
                }
            }
            catch { return ""; }
        }

        // Texte lisible d'un HTML (export BookStack, page web enregistrée…).
        private static string StripHtml(string html)
        {
            try
            {
                html = System.Text.RegularExpressions.Regex.Replace(html, "(?is)<(script|style|head|nav|footer)[^>]*>.*?</\\1>", " ");
                html = System.Text.RegularExpressions.Regex.Replace(html, "(?s)<[^>]+>", " ");
                html = System.Net.WebUtility.HtmlDecode(html);
            }
            catch { }
            return html;
        }

        private static IEnumerable<string> SplitChunks(string text, int max)
        {
            text = System.Text.RegularExpressions.Regex.Replace(text ?? "", "\\s+", " ").Trim();
            for (int i = 0; i < text.Length; i += max)
            {
                string c = text.Substring(i, Math.Min(max, text.Length - i)).Trim();
                if (c.Length >= 20) yield return c;
            }
        }

        /// <summary>Construit l'index (vectorise ce qui manque) si le serveur + le modèle d'embed
        /// sont là. Rapide si rien n'a changé (cache). À appeler en tâche de fond.</summary>
        public static void EnsureIndex()
        {
            try
            {
                if (!LocalBrain.ServerUp(1200) || !LocalBrain.HasEmbedModel()) return;
                var source = Collect();
                string sig = Signature(source);
                lock (Gate) { if (_index != null && _signature == sig) return; }

                var cached = LoadCache();   // texte → vecteur (réutilise ce qui existe déjà)
                var built = new List<Chunk>();
                foreach (var c in source)
                {
                    float[] v;
                    if (cached.TryGetValue(c.Text, out v)) { c.Vec = v; built.Add(c); continue; }
                    v = LocalBrain.Embed(c.Text, false);
                    if (v != null) { c.Vec = v; built.Add(c); }
                }
                SaveCache(built);
                lock (Gate) { _index = built; _signature = sig; }
            }
            catch { }
        }

        /// <summary>Extraits les plus pertinents pour la question (top-k), ou "" si la base n'est
        /// pas prête. Le modèle lit ces extraits pour ancrer sa réponse.</summary>
        public static string Search(string query, int k)
        {
            try
            {
                List<Chunk> idx;
                lock (Gate) idx = _index;
                if (idx == null) { EnsureIndex(); lock (Gate) idx = _index; }
                if (idx == null || idx.Count == 0) return "";
                float[] q = LocalBrain.Embed(query, true);
                if (q == null) return "";

                var scored = new List<KeyValuePair<double, Chunk>>();
                foreach (var c in idx) if (c.Vec != null) scored.Add(new KeyValuePair<double, Chunk>(Cosine(q, c.Vec), c));
                scored.Sort(delegate (KeyValuePair<double, Chunk> a, KeyValuePair<double, Chunk> b) { return b.Key.CompareTo(a.Key); });

                var sb = new StringBuilder("Base de connaissances (extraits pertinents) :\n");
                int n = Math.Min(k, scored.Count); int kept = 0;
                for (int i = 0; i < n; i++)
                {
                    if (scored[i].Key < 0.35) break;   // trop peu pertinent : on n'encombre pas
                    var c = scored[i].Value;
                    sb.Append("- ").Append(c.Text).Append(" [").Append(c.Source).Append(FreshTag(c.When)).Append("]\n");
                    kept++;
                }
                return kept > 0 ? sb.ToString() : "";
            }
            catch { return ""; }
        }

        /// <summary>Sources actuellement indexées (pour « que contient ta base »).</summary>
        public static string Describe()
        {
            var source = Collect();
            var bySrc = new Dictionary<string, int>();
            var whenOf = new Dictionary<string, DateTime>();
            foreach (var c in source)
            {
                int n; bySrc.TryGetValue(c.Source, out n); bySrc[c.Source] = n + 1;
                if (!whenOf.ContainsKey(c.Source)) whenOf[c.Source] = c.When;
            }
            var sb = new StringBuilder();
            foreach (var kv in bySrc) sb.Append("• ").Append(kv.Key).Append(FreshTag(whenOf[kv.Key])).Append(" : ").Append(kv.Value).Append(" passage(s)\n");
            return sb.ToString().TrimEnd();
        }

        /// <summary>Force la reconstruction au prochain EnsureIndex (ex. après ajout de documents).</summary>
        public static void Invalidate() { lock (Gate) { _index = null; _signature = null; } try { if (File.Exists(IndexPath)) File.Delete(IndexPath); } catch { } }

        /// <summary>Crée le dossier bt-savoir et y dépose un mode d'emploi si vide.</summary>
        public static string FolderPath()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string readme = Path.Combine(Dir, "_lisez-moi.txt");
                if (!File.Exists(readme))
                    File.WriteAllText(readme,
                        "Dépose ici tes documents : .txt, .md, .html, .pdf, .docx (Word), et images (.png/.jpg — lues par OCR).\n" +
                        "Notes de dépannage, config, manuels, captures d'écran, procédures…\n" +
                        "Les SOUS-DOSSIERS organisent le savoir (ex. Reseau\\, Jeux\\) — façon wiki.\n" +
                        "Le Copilote les lit et s'en sert pour te répondre plus précisément.\n\n" +
                        "PDF : texte extrait automatiquement (un PDF scanné-image passe par l'OCR si tu l'exportes en .png).\n" +
                        "Images : le texte est reconnu par l'OCR intégré de Windows (aucun téléchargement).\n" +
                        "Astuce BookStack : exporte un livre/une page en Markdown, HTML ou PDF et dépose le fichier ici.\n\n" +
                        "Pour une recherche encore plus précise (surtout en français), dis « installe bge-m3 ».\n" +
                        "Après tout ajout, dis « recharge mon savoir ».\n");
            }
            catch { }
            return Dir;
        }

        // ---- cache disque (texte + vecteur) : 1 ligne par morceau ----
        private static Dictionary<string, float[]> LoadCache()
        {
            var map = new Dictionary<string, float[]>();
            try
            {
                if (!File.Exists(IndexPath)) return map;
                string want = LocalBrain.EmbedModelName() ?? "none";
                foreach (string line in File.ReadAllLines(IndexPath))
                {
                    // En-tête « #MODEL\t<nom> » : vecteurs d'un AUTRE modèle → cache incompatible, on rejette.
                    if (line.StartsWith("#MODEL\t", StringComparison.Ordinal))
                    {
                        if (line.Substring(7).Trim() != want) return new Dictionary<string, float[]>();
                        continue;
                    }
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    string text = line.Substring(0, tab).Replace("\\n", "\n");
                    string[] parts = line.Substring(tab + 1).Split(',');
                    var v = new float[parts.Length];
                    bool ok = true;
                    for (int i = 0; i < parts.Length; i++) if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])) { ok = false; break; }
                    if (ok) map[text] = v;
                }
            }
            catch { }
            return map;
        }

        private static void SaveCache(List<Chunk> chunks)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("#MODEL\t").Append(LocalBrain.EmbedModelName() ?? "none").Append('\n');
                foreach (var c in chunks)
                {
                    if (c.Vec == null) continue;
                    sb.Append(c.Text.Replace("\n", "\\n")).Append('\t');
                    for (int i = 0; i < c.Vec.Length; i++) { if (i > 0) sb.Append(','); sb.Append(c.Vec[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
                    sb.Append('\n');
                }
                File.WriteAllText(IndexPath, sb.ToString());
            }
            catch { }
        }

        private static double Cosine(float[] a, float[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            double d = 0, na = 0, nb = 0;
            for (int i = 0; i < n; i++) { d += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
            return (na > 0 && nb > 0) ? d / (Math.Sqrt(na) * Math.Sqrt(nb)) : 0;
        }
    }
}
