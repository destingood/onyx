using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// « Le Copilote » — assistant LOCAL (aucun réseau, aucune clé) : comprend la demande par mots-clés,
    /// s'appuie sur les VRAIES données du PC (AppStats) et sur le catalogue symptôme→outil
    /// (HelpCatalog), puis répond et propose d'ouvrir le bon outil. Remplace le chat IA de FPS Doctor.
    /// </summary>
    internal static class DocAssistant
    {
        public sealed class Reply
        {
            public string Text;
            public HelpCatalog.Entry Tool;   // outil proposé à l'ouverture (bouton), ou null
            public bool ShowStarters;         // affiche des suggestions cliquables
        }

        public static Reply Intro(BadgeCatalog.Stats st)
        {
            string h = st != null ? "  Santé actuelle de ton PC : " + st.Health + " %." : "";
            return new Reply
            {
                Text = "Bonjour, je suis le Copilote — l'assistant de ton PC." + h +
                       "\nDis-moi ce qui cloche (ça rame, ça crash, ping élevé, écran bloqué à 60 Hz, FPS bas…) et j'ouvre le bon outil.",
                ShowStarters = true
            };
        }

        public static Reply Answer(string q, BadgeCatalog.Stats st, Action<string, int> log)
        {
            var entries = HelpCatalog.Entries(log);
            string s = Norm(q);
            if (s.Length == 0) return Intro(st);

            if (Has(s, "bonjour", "salut", "coucou", "hello", "hey", "bonsoir"))
                return new Reply { Text = "Salut ! Décris ton souci et j'ouvre le bon outil. Ou choisis ci-dessous.", ShowStarters = true };
            if (Has(s, "merci", "thanks", "top", "parfait", "genial", "super"))
                return new Reply { Text = "Avec plaisir ! Autre chose à diagnostiquer ?", ShowStarters = true };
            if (Has(s, "aide", "help", "comment", "que fais", "que peux", "sais tu faire", "tu fais quoi"))
                return new Reply { Text = "Je diagnostique et j'ouvre les bons outils : santé du PC, FPS, latence, réseau/ping, crashs, écran/souris, disque, démarrage, nettoyage, sauvegarde… Dis-moi ce qui cloche.", ShowStarters = true };

            // Questions sur l'état réel du PC (vraies données).
            if (st != null && Has(s, "sante", "etat", "bilan", "score", "va mon pc", "comment va"))
                return WithTool(entries, "Santé de mon PC",
                    "Ton PC est à " + st.Health + " % de santé, avec " + st.OptiActive + " optimisation(s) active(s). " +
                    (st.Health >= 80 ? "C'est du bon état clinique ! ✅" : "On peut clairement mieux faire — j'ouvre le bilan complet ?"));
            if (st != null && Has(s, "combien de jeu", "mes jeux", "jeux detect", "jeux install"))
                return new Reply { Text = st.GamesDet + " jeu(x) détecté(s) sur ce PC. Ouvre l'onglet Jeux 🎮 pour les optimiser un par un (clic sur une jaquette).", ShowStarters = false };
            if (st != null && Has(s, "combien d'opti", "optimisation active", "mes opti"))
                return WithTool(entries, "Santé de mon PC", st.OptiActive + " optimisation(s) active(s) sur " + st.OptiTotal + ". Va dans Optimisations pour en activer d'autres (preset « Recommandé »).");

            // Correspondance symptôme (score par mots-clés).
            HelpCatalog.Entry best = null; int bestScore = 0;
            foreach (var e in entries) { int sc = Score(s, e); if (sc > bestScore) { bestScore = sc; best = e; } }
            if (best != null && bestScore >= 2)
                return new Reply { Text = "Pour « " + best.Symptom + " », le bon outil est « " + best.Tool + " ». Je l'ouvre ?", Tool = best };

            return new Reply { Text = "Pas sûr d'avoir bien compris 🤔. Reformule en quelques mots, ou choisis un souci courant :", ShowStarters = true };
        }

        private static Reply WithTool(List<HelpCatalog.Entry> entries, string toolName, string text)
        {
            HelpCatalog.Entry t = null; foreach (var e in entries) if (e.Tool == toolName) { t = e; break; }
            return new Reply { Text = text, Tool = t };
        }

        private static int Score(string q, HelpCatalog.Entry e)
        {
            int sc = 0;
            foreach (var kw in Keywords(e)) { string k = Norm(kw); if (k.Length >= 3 && q.Contains(k)) sc += k.Length >= 5 ? 2 : 1; }
            return sc;
        }

        private static IEnumerable<string> Keywords(HelpCatalog.Entry e)
        {
            foreach (var w in Norm(e.Symptom).Split(' ')) if (w.Length >= 4 && !Stop(w)) yield return w;
            foreach (var w in Syn(e.Tool)) yield return w;
        }

        private static string[] Syn(string tool)
        {
            switch (tool)
            {
                case "Santé de mon PC": return new[] { "bilan", "sante", "diagnostic", "commencer", "general" };
                case "Stabilité (14 j)": return new[] { "crash", "crashe", "plante", "ferme tout seul", "instab", "ferme" };
                case "Boutiques / crashs": return new[] { "boutique", "magasin", "rendu perdu", "dispositif", "freeze", "gel", "fige" };
                case "Températures & throttling": return new[] { "chauffe", "temperature", "chaud", "throttl", "bride", "surchauff" };
                case "Réglages néfastes": return new[] { "optimiseur", "casse", "nefaste", "ancien", "reglage casse" };
                case "Qui ralentit mon PC": return new[] { "rame", "saccade", "lag", "lent", "ralenti", "stutter", "freeze court" };
                case "FPS en direct": return new[] { "fps", "image par seconde", "framerate", "images" };
                case "Benchmark rapide": return new[] { "benchmark", "bench", "puissance", "mesure", "tester mon pc" };
                case "Jeux & disques": return new[] { "disque", "chargement", "charge long", "ssd", "hdd", "loading" };
                case "Latence en direct": return new[] { "latence", "input lag", "reactivite", "delai", "retard" };
                case "Qualité réseau": return new[] { "ping", "gigue", "jitter", "en ligne", "decrochage", "deco", "lag en ligne" };
                case "Trajet réseau": return new[] { "trajet", "route reseau", "traceroute", "saut", "ou lag" };
                case "Réglages TCP/IP": return new[] { "telechargement", "download", "steam charge", "tcp", "debit", "lent a telecharger" };
                case "Carte réseau": return new[] { "carte reseau", "ethernet", "adaptateur reseau" };
                case "DNS rapide": return new[] { "dns", "resolution", "cloudflare", "changer dns" };
                case "Bibliothèques de jeu": return new[] { "dll", "manquante", "demarre pas", "visual c", "redist", "directx", "refuse de demarrer" };
                case "Priorité par jeu": return new[] { "priorite", "booster mon jeu", "priorite cpu", "principal" };
                case "Exclusions antivirus": return new[] { "antivirus", "defender", "exclusion", "scan des jeux" };
                case "Réglages d'écran": return new[] { "ecran", "hz", "hertz", "rafraich", "moniteur", "bloque a 60", "144" };
                case "Fréquence de la souris": return new[] { "souris", "polling", "hz souris", "dpi", "1000 hz" };
                case "Programmes au démarrage": return new[] { "demarrage", "boot", "startup", "lent a demarrer", "allumage", "programmes au boot" };
                case "Nettoyage disque": return new[] { "espace", "nettoy", "disque plein", "temporaire", "liberer", "place disque" };
                case "Points de restauration": return new[] { "sauvegarde", "restauration", "point de restau", "backup", "avant de" };
            }
            return new string[0];
        }

        private static bool Stop(string w)
        {
            switch (w) { case "mon": case "mes": case "pour": case "avec": case "dans": case "tout": case "tous": case "peut": case "être": case "sont": case "cette": case "quand": return true; default: return false; }
        }

        private static bool Has(string s, params string[] ks) { foreach (var k in ks) if (s.Contains(k)) return true; return false; }

        // minuscule + sans accents (l'utilisateur tape souvent sans accents).
        private static string Norm(string x)
        {
            if (string.IsNullOrEmpty(x)) return "";
            var sb = new StringBuilder(x.Length);
            foreach (char c0 in x.ToLowerInvariant().Trim())
            {
                char c = c0;
                switch (c)
                {
                    case 'é': case 'è': case 'ê': case 'ë': c = 'e'; break;
                    case 'à': case 'â': case 'ä': c = 'a'; break;
                    case 'î': case 'ï': c = 'i'; break;
                    case 'ô': case 'ö': c = 'o'; break;
                    case 'û': case 'ü': case 'ù': c = 'u'; break;
                    case 'ç': c = 'c'; break;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
