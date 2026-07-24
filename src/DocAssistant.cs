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
        /// <summary>Ce que le Copilote peut EXÉCUTER depuis la conversation.
        /// IsChange = true → jamais sans un clic explicite (promesse fondatrice de Fluide).
        /// AutoRun = true → mesure en lecture seule, lancée d'elle-même en tâche de fond.</summary>
        public sealed class ChatAction
        {
            public string Label;                            // libellé du bouton / de l'étape
            public string Warning;                          // ce qui va changer (sous le bouton)
            public bool IsChange;
            public bool AutoRun;
            /// <summary>Exécute et renvoie la RÉPONSE à afficher : elle peut elle-même porter
            /// la correction qui découle de la mesure, ou tout un plan (voir Reply.Plan).</summary>
            public Func<Action<string, int>, Reply> Run;
        }

        public sealed class Reply
        {
            public string Text;
            public HelpCatalog.Entry Tool;   // outil proposé à l'ouverture (bouton), ou null
            public bool OpenToolNow;          // le « oui » de l'utilisateur vaut clic : la page ouvre l'outil elle-même
            public bool ShowStarters;         // affiche des suggestions cliquables
            public ChatAction Action;         // mesure lancée seule, ou changement à confirmer
            public List<ChatAction> Plan;     // plusieurs corrections classées par impact
        }

        public static Reply Intro(BadgeCatalog.Stats st)
        {
            string h = st != null ? "  Santé actuelle de ton PC : " + st.Health + " %." : "";
            return new Reply
            {
                Text = "Bonjour, je suis le Copilote — l'assistant de ton PC." + h +
                       "\nDis-moi ce qui cloche (ça rame, ça crash, ping élevé, écran bloqué à 60 Hz, FPS bas…) : "
                     + "je mesure en direct, je trouve les causes et je corrige — toujours avec ton accord.",
                ShowStarters = true
            };
        }

        /// <summary>'last' = dernière réponse du Copilote qui portait quelque chose d'actionnable
        /// (outil / correction / plan) : un « oui » ou un « non » de l'utilisateur s'y rapporte.</summary>
        public static Reply Answer(string q, BadgeCatalog.Stats st, Action<string, int> log, Reply last = null)
        {
            var entries = HelpCatalog.Entries(log);
            string s = Norm(q);
            if (s.Length == 0) return Intro(st);

            // --- Suivi de conversation : « oui » / « non » répond à la DERNIÈRE proposition ---
            if (IsYes(s))
            {
                if (last != null && last.Plan != null && last.Plan.Count > 0)
                    return new Reply { Text = "C'est parti — dans l'ordre d'impact, chaque bouton lance sa correction :", Plan = last.Plan };
                if (last != null && last.Action != null)
                    return last.Action.IsChange
                        ? new Reply { Text = "C'est parti — un clic sur le bouton et je lance :", Action = last.Action }
                        : new Reply { Text = "Je relance la mesure…", Action = last.Action };
                if (last != null && last.Tool != null)
                    return new Reply { Text = "J'ouvre « " + last.Tool.Tool + " » tout de suite.", Tool = last.Tool, OpenToolNow = true };
                return new Reply { Text = "Volontiers — mais dis-moi d'abord ce qui cloche :", ShowStarters = true };
            }
            if (IsNo(s))
                return new Reply { Text = "Pas de souci, on laisse ça de côté. Autre chose à vérifier ?", ShowStarters = true };

            if (Has(s, "bonjour", "salut", "coucou", "hello", "hey", "bonsoir"))
                return new Reply { Text = "Salut ! Décris ton souci et j'ouvre le bon outil. Ou choisis ci-dessous.", ShowStarters = true };
            if (Has(s, "merci", "thanks", "top", "parfait", "genial", "super"))
                return new Reply { Text = "Avec plaisir ! Autre chose à diagnostiquer ?", ShowStarters = true };
            if (Has(s, "aide", "help", "comment", "que fais", "que peux", "sais tu faire", "tu fais quoi"))
                return new Reply { Text = "Je diagnostique et je corrige : je mesure ton PC en direct (écrans, ping, capteurs, processus en fond, disque…), je classe les causes par impact et chaque correction attend TON clic. Dis-moi ce qui cloche.", ShowStarters = true };

            // Questions sur l'état réel du PC (vraies données). « bilan » va à l'ENQUÊTE, plus bas.
            if (st != null && Has(s, "sante", "score", "va mon pc", "comment va", "etat de mon pc"))
                return WithTool(entries, "Santé de mon PC",
                    "Ton PC est à " + st.Health + " % de santé, avec " + st.OptiActive + " optimisation(s) active(s). " +
                    (st.Health >= 80 ? "C'est du bon état clinique ! ✅" : "On peut clairement mieux faire — j'ouvre le bilan complet ?"));
            if (st != null && Has(s, "combien de jeu", "mes jeux", "jeux detect", "jeux install"))
                return new Reply { Text = st.GamesDet + " jeu(x) détecté(s) sur ce PC. Ouvre l'onglet Jeux 🎮 pour les optimiser un par un (clic sur une jaquette).", ShowStarters = false };
            if (st != null && Has(s, "combien d'opti", "optimisation active", "mes opti"))
                return WithTool(entries, "Santé de mon PC", st.OptiActive + " optimisation(s) active(s) sur " + st.OptiTotal + ". Va dans Optimisations pour en activer d'autres (preset « Recommandé »).");

            // --- Intentions PRÉCISES d'abord : le Copilote MESURE puis AGIT. Elles passent AVANT
            //     l'enquête large, sinon « mon écran a un problème » partirait dans le bilan général. ---
            if (Has(s, "ping", "en ligne", "jitter", "gigue", "paquet", "serveur", "internet", "connexion", "wifi", "deco", "deconnect"))
                return WithAction(entries, "Qualité réseau",
                    "Je teste ta connexion en direct (échos réels)…", ChatActions.MeasurePing());
            if (Has(s, "qui ralentit", "processus", "en fond", "arriere plan", "arriere-plan", "quel programme", "quelle appli", "gourmand", "bouffe", "consomme"))
                return WithAction(entries, "Qui ralentit mon PC",
                    "Je mesure ce qui travaille en ce moment (1 seconde)…", ChatActions.MeasureHogs());
            if (Has(s, "ecran", "hz", "hertz", "rafraich", "moniteur", "144", "165", "240", "bloque a 60"))
                return WithAction(entries, "Réglages d'écran",
                    "Je regarde tes écrans et leur fréquence réelle…", ChatActions.MeasureScreen());
            if (Has(s, "chauffe", "temperature", "chaud", "throttl", "bride", "capteur", "charge cpu", "charge gpu", "surchauff"))
                return WithAction(entries, "Températures & throttling",
                    "Je prends une mesure en direct…", ChatActions.MeasureSensors());
            if (Has(s, "espace", "disque plein", "nettoy", "liberer", "place disque", "temporaire", "saturé", "sature"))
                return WithAction(entries, "Nettoyage disque",
                    "J'analyse ton disque système…", ChatActions.MeasureDisk());
            if (Has(s, "dll", "manquante", "demarre pas", "refuse de demarrer", "visual c", "directx", "redist", "bibliotheque"))
                return WithAction(entries, "Bibliothèques de jeu",
                    "Je vérifie les bibliothèques essentielles…", ChatActions.MeasureLibs());
            if (Has(s, "point de restau", "restauration", "sauvegarde", "backup", "avant de toucher", "filet"))
                return WithAction(entries, "Points de restauration",
                    "Je peux poser un filet de sécurité avant toute manipulation.", ChatActions.MakeRestorePoint());

            // --- ENQUÊTE : symptôme large ou demande de bilan → il cherche TOUTES les causes ---
            if (Has(s, "rame", "saccade", "lent", "ralenti", "stutter", "lag", "freeze",
                       "bilan", "diagnostic", "analyse", "enquete", "verifie", "controle", "passe au crible",
                       "check up", "checkup", "audit",
                       "fps bas", "perd des fps", "chute de fps", "probleme", "ca marche pas"))
                return new Reply
                {
                    Text = "Je lance l'enquête complète : écrans, capteurs, connexion, processus en fond, "
                         + "disque, bibliothèques, crashs pilote et optimisations. Quelques secondes…",
                    Action = Investigator.Action(q.Trim(), st)
                };

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

        /// <summary>Réponse qui EXÉCUTE (mesure lancée seule, ou changement à confirmer),
        /// tout en gardant sous la main l'outil complet correspondant.</summary>
        private static Reply WithAction(List<HelpCatalog.Entry> entries, string toolName, string text, ChatAction action)
        {
            Reply r = WithTool(entries, toolName, text);
            r.Action = action;
            return r;
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

        // --- « oui » / « non » : formes courtes uniquement, comparées lettres seules (« Vas-y ! »,
        //     « d'accord », « ok stp »…). Une vraie phrase ne matche pas et suit le routage normal. ---
        private static string Squash(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) if (char.IsLetter(c)) sb.Append(c);
            return sb.ToString();
        }

        private static readonly System.Text.RegularExpressions.Regex YesRx = new System.Text.RegularExpressions.Regex(
            "^(oui|ouais|ouaip|yes|yep|yeah|ok|okay|oki|daccord|dacc|vasy|go|fonce|lance|faisle|fais|ouvre|carrement|volontiers|banco|camarche|cestparti|allonsy|allezy|allez|montre|montremoi|jeveuxbien|pourquoipas|evidemment|grave)"
          + "(oui|ouais|ok|vasy|go|lance|faisle|fais|ouvre|montre|stp|silteplait|svp|merci|le|la|ca|moi|donc|maintenant|toutdesuite)*$");
        private static readonly System.Text.RegularExpressions.Regex NoRx = new System.Text.RegularExpressions.Regex(
            "^(non|nan|nope|no|pasmaintenant|pastoutdesuite|pasencore|plustard|laisse|laissetomber|annule|annuler|stop|arrete|surtoutpas)"
          + "(non|nan|laisse|laissetomber|tomber|merci|stp|silteplait|svp)*$");

        private static bool IsYes(string s) { string x = Squash(s); return x.Length > 0 && x.Length <= 40 && YesRx.IsMatch(x); }
        private static bool IsNo(string s) { string x = Squash(s); return x.Length > 0 && x.Length <= 40 && NoRx.IsMatch(x); }

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
