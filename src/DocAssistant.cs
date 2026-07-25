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
        /// IsChange = true → jamais sans un clic explicite (promesse fondatrice de ONYX).
        /// AutoRun = true → mesure en lecture seule, lancée d'elle-même en tâche de fond.</summary>
        public sealed class ChatAction
        {
            public string Label;                            // libellé du bouton / de l'étape
            public string Warning;                          // ce qui va changer (sous le bouton)
            public bool IsChange;
            public bool AutoRun;
            public bool NoChain;                            // exclu de « TOUT réparer » (ex. redémarrage)
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
            public List<Card> Cards;          // diagnostic STRUCTURÉ : cartes d'impact colorées (enquête)
            public string Footer;             // texte affiché APRÈS les cartes (« vérifié et sain… »)
            public string Explain;            // le raisonnement complet, servi si on demande « pourquoi ? »
            public bool Exportable;           // propose « Enregistrer ce diagnostic » (.txt sur le Bureau)
            public bool Dynamic;              // accroche PURE (sans donnée) → l'IA la reformule à chaque fois
        }

        /// <summary>Une cause rendue en CARTE dans le chat : pastille d'impact colorée
        /// (rouge ≥ 85, orange ≥ 65, jaune sinon), texte, et sa correction sur clic.</summary>
        public sealed class Card
        {
            public int Impact;
            public string Text;
            public ChatAction Fix;
        }

        public static Reply Intro(BadgeCatalog.Stats st)
        {
            string h = st != null ? "  Santé actuelle de ton PC : " + st.Health + " %." : "";
            string ia = LocalBrain.SetupStatus;
            string brain = ia != null
                ? "\n🧠 Mon cerveau IA local s'installe en arrière-plan (" + ia + ") — gratuit, 100 % sur ta machine. « désactive l'ia » pour annuler."
                : LocalBrain.Enabled ? "\n🧠 IA locale active : je réponds aussi à tout le reste."
                : "";
            return new Reply
            {
                Text = "Bonjour, je suis le Copilote — l'assistant de ton PC." + h +
                       "\nDis-moi ce qui cloche — je dépanne ton PC quel que soit le souci : jeux qui rament, plus de "
                     + "son, plus d'internet, Windows corrompu, écran noir, ça crash… je mesure, je répare (gratuitement, "
                     + "avec ton accord), et pour ce que le logiciel ne peut pas faire seul, je te guide pas à pas. "
                     + "Je réponds aussi aux questions : « c'est quoi le DLSS ? »…" + brain,
                ShowStarters = true
            };
        }

        /// <summary>'last' = dernière réponse du Copilote qui portait quelque chose d'actionnable
        /// (outil / correction / plan) : un « oui » ou un « non » de l'utilisateur s'y rapporte.</summary>
        public static Reply Answer(string q, BadgeCatalog.Stats st, Action<string, int> log, Reply last = null)
        {
            var entries = HelpCatalog.Entries(log);
            string s = Expand(Norm(q));   // accents à plat + abréviations texto développées
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

            // Salutation SEULE (≤ 3 mots) → on salue. Sinon (« salut j'ai un souci… ») c'est juste
            // un préambule : on ignore la politesse et on traite le vrai message plus bas.
            if (SplitWords(s).Length <= 3 && Has(s, "bonjour", "salut", "coucou", "hello", "hey", "bonsoir", "bonne nuit"))
                return new Reply { Text = "Salut ! Décris ton souci et j'ouvre le bon outil. Ou choisis ci-dessous.", ShowStarters = true, Dynamic = true };
            // « ça va PAS » (négatif) : on ne répond pas « ça va ! », on demande ce qui cloche.
            if (Has(s, "ca va pas", "ca va plus", "sa va pas", "ca marche pas") && SplitWords(s).Length <= 5)
                return new Reply { Text = "Ah, qu'est-ce qui ne va pas ? Dis-moi ce qui se passe (ça rame, ça crash, plus de son, plus d'internet…) et je m'en occupe.", ShowStarters = true, Dynamic = true };
            // « ça va ? » et ses formes familières (cava, sava, cv…). Messages COURTS seulement :
            // « comment va mon pc » doit rester une question de santé, pas de la politesse.
            if (SplitWords(s).Length <= 4 && !Has(s, "pc", "jeu")
                && Has(s, "ca va", "ca roule", "ca gaze", "quoi de neuf", "tu vas bien", "comment vas tu", "bien et toi"))
                return new Reply { Text = "Ça va, merci 🙂 Et toi ? Je suis prêt : dis-moi ce qui cloche sur ton PC, ou pose-moi n'importe quelle question.", ShowStarters = true, Dynamic = true };
            if (Has(s, "au revoir", "a plus", "bye", "ciao", "a bientot", "bonne journee", "bonne soiree"))
                return new Reply { Text = "À bientôt ! Reviens dès que ton PC fait des siennes. 👋", ShowStarters = false, Dynamic = true };
            if (Has(s, "merci", "thanks", "top", "parfait", "genial", "super", "nickel", "cool"))
                return new Reply { Text = "Avec plaisir ! Autre chose à diagnostiquer ?", ShowStarters = true, Dynamic = true };
            if (Has(s, "qui es tu", "tu es qui", "c'est quoi ce chat", "tu es un robot", "tu es une ia", "es tu une ia", "es tu humain"))
                return new Reply { Text = "Je suis le Copilote de ton PC : un assistant qui tourne sur TA machine. Je mesure, je répare, je conseille — et avec mon cerveau IA local, je réponds à tout. Par défaut tout reste local ; si tu actives la recherche web, je vais aussi chercher l'info à jour en ligne (désactivable). Alors, on regarde quoi ?", ShowStarters = true };

            // --- Oubli contextuel (technique anti-hallucination : réduire la fenêtre pour ne pas
            //     être influencé par les échanges précédents). « nouveau sujet », « oublie »… ---
            if (IsForget(s))
            {
                try { LocalBrain.ResetHistory(); } catch { }
                return new Reply { Text = "Contexte oublié — on repart de zéro. 🧹 Pose ta nouvelle question !", ShowStarters = true };
            }

            // --- Auto-diagnostic : mesurer en direct que les garde-fous anti-hallucination tournent
            //     (« mesurer le succès » / observabilité). « teste ta fiabilité », « diagnostic ia »… ---
            if (IsSelfTest(s))
                return new Reply { Text = ChatActions.SelfDiagnostic(), ShowStarters = true };

            // --- Bouclier anti-injection : tentative DIRECTE de détournement (« ignore tes règles »,
            //     « change de rôle », « montre ton prompt système »…) → on garde fermement le rôle. ---
            if (PromptShield.LooksLikeInjection(q))
                return new Reply { Text = "Je reste le Copilote d'ONYX, avec mes règles (gratuit, prudent, honnête) — "
                    + "je ne change pas de rôle et je ne les contourne pas. En revanche, je t'aide avec plaisir sur ton PC "
                    + "ou n'importe quelle question. On regarde quoi ?", ShowStarters = true };

            // --- Boucle de FEEDBACK (auto-amélioration « essais-erreurs », sans ré-entraînement) :
            //     l'utilisateur corrige → on RETIENT la correction durablement → plus juste ensuite.
            //     C'est l'équivalent local et gratuit de l'adaptation au domaine du fine-tuning. ---
            if (IsCorrection(s))
            {
                string corr = ExtractAfter(q, new[] { "en fait c'est", "en fait c est", "non c'est plutot",
                    "non c'est plutôt", "c'est plutot", "c'est plutôt", "la bonne reponse c'est", "la bonne reponse est",
                    "la bonne réponse c'est", "la bonne réponse est", "en realite c'est", "en réalité c'est",
                    "la verite c'est", "la vérité c'est", "en fait", "correction" });
                if (!string.IsNullOrEmpty(corr) && corr.Length >= 2)
                {
                    try { Memory.Add("Correction de l'utilisateur : " + corr); } catch { }
                    return new Reply { Text = "Merci pour la correction — c'est noté et RETENU durablement : « " + corr
                        + " ». Je m'en servirai pour être plus juste la prochaine fois. 🙏", ShowStarters = true };
                }
                return new Reply { Text = "Désolé pour l'erreur. Dis-moi la bonne réponse (« en fait c'est… ») et je la "
                    + "retiens d'une session à l'autre, ou dis « cherche sur internet » et je vérifie en ligne.", ShowStarters = false };
            }

            // --- Lexique pédagogique : « c'est quoi le DLSS ? » → il explique ET tend l'outil lié ---
            {
                Reply lx = Lexi(s, entries);
                if (lx != null) return lx;
            }

            // --- Lecture d'intentions (tolérante aux fautes de frappe, voir FuzzyWord) -------------
            bool iNet    = Has(s, "ping", "en ligne", "jitter", "gigue", "paquet", "serveur", "internet", "connexion",
                                  "wifi", "deco", "deconnect", "latence reseau", "perte de", "rubber", "teleporte",
                                  "decroche", "coupure reseau");
            bool iHogs   = Has(s, "qui ralentit", "processus", "en fond", "arriere plan", "arriere-plan", "quel programme", "quelle appli", "gourmand", "bouffe", "consomme");
            bool iScreen = Has(s, "ecran", "hz", "hertz", "rafraich", "moniteur", "144", "165", "240", "bloque a 60");
            bool iHeat   = Has(s, "chauffe", "temperature", "chaud", "throttl", "bride", "capteur", "charge cpu",
                                  "charge gpu", "surchauff", "brulant", "fournaise", "cuit", "ventilo", "ventilateur",
                                  "souffle", "bruyant", "degre");
            bool iClean  = Has(s, "espace", "disque plein", "nettoy", "liberer", "place disque", "temporaire", "saturé", "sature");
            bool iLibs   = Has(s, "dll", "manquante", "demarre pas", "refuse de demarrer", "visual c", "directx", "redist", "bibliotheque");
            bool iDns    = Has(s, "dns", "resolution de nom");
            bool iBoot   = Has(s, "demarrage", "boot", "startup", "allumage", "lent a demarrer", "long a demarrer", "s'allume");
            bool iCrash  = Has(s, "crash", "plante", "bsod", "ecran bleu", "ferme tout seul", "rendu perdu", "dispositif de rendu");
            bool iLat    = Has(s, "input lag", "latence", "reactivite", "micro coupure", "micro-coupure", "gresille", "dpc", "delai souris");
            bool iReport = Has(s, "rapport", "audit", "imprime", "livrable", "avant apres", "avant-apres");
            bool iPrep   = Has(s, "je vais jouer", "avant de jouer", "prepare ma partie", "prepare une partie",
                                  "session de jeu", "pregame", "pre-game", "pret a jouer", "checklist");

            // Commandes explicites (pas des symptômes) : traitées avant tout le reste.
            // « désactive » AVANT « active » (l'un contient l'autre).
            if (Has(s, "desactive l'ia", "desactiver l'ia", "coupe l'ia", "coupe ton ia", "sans ia"))
            {
                LocalBrain.SetEnabled(false); LocalBrain.ResetHistory();
                return new Reply { Text = "IA locale désactivée — je reste sur mes règles (toujours 100 % local). Dis « active l'ia » pour la rallumer.", ShowStarters = true };
            }
            // Repartir de zéro dans la conversation IA (oublie le contexte précédent).
            if (Has(s, "nouvelle conversation", "oublie tout", "oublie la conversation", "on repart de zero", "reset la conversation", "efface la conversation"))
            {
                LocalBrain.ResetHistory();
                return new Reply { Text = "C'est oublié — on repart sur une page blanche. Qu'est-ce que je peux faire pour toi ?", ShowStarters = true };
            }
            // Activer / couper la recherche internet (« coupe » testé avant « active »).
            if (Has(s, "coupe internet", "coupe la recherche", "sans internet", "reste hors ligne", "mode hors ligne", "desactive internet"))
            {
                LocalBrain.SetWebEnabled(false);
                return new Reply { Text = "Recherche internet coupée — je reste 100 % hors-ligne (mesures, réparations, IA locale sur ce PC). Dis « active internet » pour la rétablir.", ShowStarters = true };
            }
            if (Has(s, "active internet", "activer internet", "connecte toi", "connexion internet", "va sur internet", "acces internet"))
            {
                LocalBrain.SetWebEnabled(true);
                return new Reply { Text = "Recherche internet activée : pour l'actualité et le temps réel (résultats de match, météo, prix…), je vais chercher en ligne puis je te réponds. Vas-y, demande !", ShowStarters = true };
            }
            // --- MÉMOIRE : le Copilote retient pour être plus précis d'une fois sur l'autre ---
            if (Has(s, "retiens que", "souviens toi que", "souviens-toi que", "note que", "rappelle toi que",
                       "rappelle-toi que", "retiens", "memorise"))
            {
                string fact = ExtractAfter(q, new[] { "retiens que", "souviens toi que", "souviens-toi que", "note que",
                    "rappelle toi que", "rappelle-toi que", "memorise que", "retiens", "memorise" });
                if (fact.Length < 2)
                    return new Reply { Text = "Dis-moi quoi retenir, par exemple « retiens que je joue surtout à Valorant » ou « retiens que mon budget est de 800 € ».", ShowStarters = false };
                Memory.Add(fact);
                return new Reply { Text = "C'est noté : « " + fact + " ». Je m'en servirai pour te répondre plus juste. (Dis « oublie ce que tu sais » pour effacer.)", ShowStarters = false };
            }
            if (Has(s, "que sais tu sur moi", "que sais-tu sur moi", "mes infos", "tu te souviens de quoi", "ta memoire", "qu'est ce que tu sais sur moi"))
            {
                var facts = Memory.All();
                if (facts.Count == 0)
                    return new Reply { Text = "Je ne retiens rien pour l'instant. Dis « retiens que… » et je garderai l'info d'une session à l'autre pour être plus précis.", ShowStarters = false };
                return new Reply { Text = "Voilà ce que je retiens sur toi et ton PC :\n\n• " + string.Join("\n• ", facts.ToArray()) + "\n\n« oublie ce que tu sais » pour tout effacer.", ShowStarters = false };
            }
            if (Has(s, "oublie ce que tu sais", "oublie moi", "efface ta memoire", "efface ce que tu sais",
                       "vide ta memoire", "oublie ce que tu as appris", "efface ce que tu as appris"))
            {
                Memory.Clear();
                LocalBrain.ForgetLearned();   // efface aussi les faits appris/vérifiés persistants
                return new Reply { Text = "Voilà, j'ai tout oublié (matériel, préférences, notes, et les faits que j'avais appris/vérifiés). On repart de zéro.", ShowStarters = true };
            }

            // --- LIRE / RÉSUMER une page web dont l'URL est donnée ---
            {
                string url = FindUrl(q);
                if (url != null && LocalBrain.Enabled && !LocalBrain.WebOff())
                {
                    string focus = Has(s, "resume", "resumer", "resume moi") ? "Résume cette page en français, points clés."
                                 : (Has(s, "lis", "lire", "ouvre", "regarde") ? "" : "");
                    return new Reply { Text = "Je vais lire cette page pour toi…", Action = ChatActions.ReadUrl(url, focus, st) };
                }
            }

            // --- BASE DE CONNAISSANCES (RAG) : gestion ---
            if (Has(s, "recharge mon savoir", "reconstruis ta base", "recharge ta base", "j'ai ajoute des documents",
                       "actualise ta base", "reindexe"))
            {
                KnowledgeBase.Invalidate();
                System.Threading.Tasks.Task.Run(() => { try { KnowledgeBase.EnsureIndex(); } catch { } });
                return new Reply { Text = "Je relis ta base de connaissances (dossier bt-savoir) et je ré-indexe en fond. Tes documents seront pris en compte dès ta prochaine question.", ShowStarters = false };
            }
            if (Has(s, "que contient ta base", "ta base de connaissances", "que sais tu faire de ta base", "contenu de ta base", "tes documents"))
                return new Reply { Text = "Ma base de connaissances contient :\n\n" + KnowledgeBase.Describe()
                    + "\n\nPour l'enrichir, dépose des .txt/.md dans le dossier bt-savoir (dis « ou mettre mes documents ») puis « recharge mon savoir ».", ShowStarters = false };
            if (Has(s, "installe bge", "meilleur modele de recherche", "ameliore ta base", "ameliore la recherche",
                       "modele plus precis", "bge-m3", "bge m3"))
                return new Reply
                {
                    Text = "Je télécharge un modèle de recherche plus précis (bge-m3, ~1,2 Go, meilleur en français) puis "
                         + "je ré-indexe ta base avec — un clic :",
                    Action = ChatActions.UpgradeEmbed()
                };
            if (Has(s, "ou mettre mes documents", "ou ajouter des documents", "ou deposer mes fiches", "dossier savoir", "ajouter un document"))
            {
                string folder = KnowledgeBase.FolderPath();
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); } catch { }
                return new Reply { Text = "Dépose tes fiches (.txt ou .md) ici :\n" + folder + "\n(le dossier vient de s'ouvrir). Puis dis « recharge mon savoir ».", ShowStarters = false };
            }

            // Recherche web EXPLICITE : « cherche sur internet X », « google X »…
            if (Has(s, "cherche sur internet", "cherche sur le web", "recherche internet", "google", "sur internet", "sur le web", "recherche web"))
                return new Reply { Text = "Je cherche ça sur le web…", Action = ChatActions.WebAnswer(q.Trim(), st) };
            // Questions d'ACTUALITÉ / TEMPS RÉEL que l'IA locale ne peut pas connaître → recherche web.
            {
                Reply web = MaybeWeb(s, q, st);
                if (web != null) return web;
            }
            if (Has(s, "active l'ia", "activer l'ia", "active ton ia", "ia locale", "mon ia", "cerveau ia", "ollama", "intelligence artificielle"))
                return new Reply
                {
                    Text = "Je vérifie l'état de mon cerveau IA local (gratuit, 100 % sur ta machine)…",
                    Action = ChatActions.SetupBrain()
                };
            if (iReport)
                return new Reply
                {
                    Text = "Je te prépare l'audit complet — un clic, rien n'est modifié au système :",
                    Action = ChatActions.MakeReport()
                };

            // --- DÉPANNAGE PC (au-delà du gaming) : les grandes réparations gratuites, et des
            //     guides sûrs pour ce que le logiciel ne peut pas faire seul. ---
            {
                Reply fx = RepairRouter(s);
                if (fx != null) return fx;
            }
            if (iPrep)
                return new Reply
                {
                    Text = "Je repère ce qui traîne en fond avant ta session…",
                    Action = ChatActions.PrepGame()
                };
            int hits = (iNet ? 1 : 0) + (iHogs ? 1 : 0) + (iScreen ? 1 : 0) + (iHeat ? 1 : 0) + (iClean ? 1 : 0) + (iLibs ? 1 : 0)
                     + (iDns ? 1 : 0) + (iBoot ? 1 : 0) + (iCrash ? 1 : 0) + (iLat ? 1 : 0);
            // Symptôme ressenti (large) vs demande de bilan (méta) : les deux mènent à l'enquête,
            // mais seul le SYMPTÔME est assez fort pour élargir une intention précise en enquête.
            bool symptom = Has(s, "rame", "saccade", "lent", "ralenti", "stutter", "lag", "freeze", "fps bas",
                                  "perd des fps", "chute de fps", "mouline", "patine", "traine", "poussif",
                                  "a-coups", "acoups", "broute", "gele", "fige", "bug", "buggue", "plante",
                                  "lourd", "ramollo", "au ralenti", "lenteur");
            bool meta = Has(s, "bilan", "diagnostic", "analyse", "enquete", "verifie", "controle", "passe au crible",
                               "check up", "checkup", "probleme", "ca marche pas");
            bool asksWhy = Has(s, "pourquoi", "explique", "comment tu sais", "ca veut dire quoi", "detaille", "justifie");

            // --- « Pourquoi ? » : il justifie son DERNIER diagnostic, mesure par mesure ---
            if (asksWhy && hits == 0 && !symptom && !meta)
            {
                if (last != null && !string.IsNullOrEmpty(last.Explain))
                    return new Reply { Text = last.Explain };
                return new Reply
                {
                    Text = "Je ne devine jamais : je MESURE (écrans, ping, capteurs, processus, disque, crashs pilote…), "
                         + "je compare chaque valeur à son seuil connu et je classe par impact. Donne-moi un symptôme et "
                         + "je te montrerai le raisonnement complet.",
                    ShowStarters = true
                };
            }

            if (Has(s, "aide", "help", "comment", "que fais", "que peux", "sais tu faire", "tu fais quoi"))
                return new Reply { Text = "Je diagnostique et je corrige : je mesure ton PC en direct (écrans, ping, capteurs, processus en fond, disque…), je classe les causes par impact et chaque correction attend TON clic. Tout est gratuit. Dis-moi ce qui cloche.", ShowStarters = true };

            // --- « gratuit » : la règle de la maison, puis la preuve par la mesure ---
            if (Has(s, "gratuit", "gratos", "sans payer", "payant", "payer", "argent", "free"))
                return new Reply
                {
                    Text = "Bonne nouvelle : je ne propose QUE du gratuit. Les optimisations de l'app, les réparations "
                         + "Windows et les outils que j'installe en 1 clic (Fan Control, LatencyMon, DDU…) sont tous "
                         + "gratuits — jamais de logiciel payant, jamais d'achat conseillé avant d'avoir tout tenté à "
                         + "0 €. La méthode la plus intelligente : je mesure ton PC et je ne garde que les corrections "
                         + "gratuites qui changent vraiment quelque chose. Je lance l'enquête…",
                    Action = Investigator.Action(q.Trim(), st)
                };

            // Questions sur l'état réel du PC (vraies données). « bilan » va à l'ENQUÊTE, plus bas.
            if (st != null && Has(s, "sante", "score", "va mon pc", "comment va", "etat de mon pc"))
                return WithTool(entries, "Santé de mon PC",
                    "Ton PC est à " + st.Health + " % de santé, avec " + st.OptiActive + " optimisation(s) active(s). " +
                    (st.Health >= 80 ? "C'est du bon état clinique ! ✅" : "On peut clairement mieux faire — j'ouvre le bilan complet ?"));
            if (st != null && Has(s, "combien de jeu", "mes jeux", "jeux detect", "jeux install"))
                return new Reply { Text = st.GamesDet + " jeu(x) détecté(s) sur ce PC. Ouvre l'onglet Jeux 🎮 pour les optimiser un par un (clic sur une jaquette).", ShowStarters = false };
            if (st != null && Has(s, "combien d'opti", "optimisation active", "mes opti"))
                return WithTool(entries, "Santé de mon PC", st.OptiActive + " optimisation(s) active(s) sur " + st.OptiTotal + ". Va dans Optimisations pour en activer d'autres (preset « Recommandé »).");

            // --- Routage raisonné ------------------------------------------------------------------
            //  1) plusieurs pistes dans la phrase → autant TOUT vérifier d'un coup (enquête) ;
            //  2) le réseau reste prioritaire (« ça lag en ligne » = demande précise, réponse précise) ;
            //  3) une intention précise noyée dans un symptôme large (« une espèce de lag et le disque
            //     plein ») → enquête aussi : elle couvre toutes les pistes ;
            //  4) une intention précise seule → sa mesure dédiée, plus rapide qu'une enquête.
            if (hits >= 2)
                return new Reply
                {
                    Text = "Tu décris plusieurs pistes à la fois — je préfère tout vérifier d'un coup. "
                         + "Enquête complète (une quinzaine de mesures, adaptées à ta plainte). Quelques secondes…",
                    Action = Investigator.Action(q.Trim(), st), Dynamic = true
                };
            // Demandes précises et fortes : leur mesure dédiée, même au milieu d'un symptôme large.
            if (iNet)
                return WithAction(entries, "Qualité réseau",
                    "Je teste ta connexion en direct (échos réels)…", ChatActions.MeasurePing());
            if (iDns)
                return WithAction(entries, "DNS rapide",
                    "Je chronomètre ton DNS contre les références gratuites…", ChatActions.MeasureDns());
            if (iLat)
                return WithAction(entries, "Latence en direct",
                    "Je mesure la réactivité réelle de ta machine (~5 s : timer, régularité, pics pilotes)…",
                    ChatActions.MeasureLatency());
            if (iBoot)
                return WithAction(entries, "Programmes au démarrage",
                    "J'inventorie ce qui se lance à chaque allumage…", ChatActions.MeasureStartup());
            if (hits == 1 && !symptom)
            {
                if (iHogs)
                    return WithAction(entries, "Qui ralentit mon PC",
                        "Je mesure ce qui travaille en ce moment (1 seconde)…", ChatActions.MeasureHogs());
                if (iScreen)
                    return WithAction(entries, "Réglages d'écran",
                        "Je regarde tes écrans et leur fréquence réelle…", ChatActions.MeasureScreen());
                if (iHeat)
                    return WithAction(entries, "Températures & throttling",
                        "Je prends une mesure en direct…", ChatActions.MeasureSensors());
                if (iClean)
                    return WithAction(entries, "Nettoyage disque",
                        "J'analyse ton disque système…", ChatActions.MeasureDisk());
                if (iLibs)
                    return WithAction(entries, "Bibliothèques de jeu",
                        "Je vérifie les bibliothèques essentielles…", ChatActions.MeasureLibs());
            }
            if (hits == 0 && Has(s, "point de restau", "restauration", "sauvegarde", "backup", "avant de toucher", "filet"))
                return WithAction(entries, "Points de restauration",
                    "Je peux poser un filet de sécurité avant toute manipulation.", ChatActions.MakeRestorePoint());

            // CRASH : la CAUSE EXACTE prime (même si « plante »/« bug » comptent aussi comme symptôme).
            if (iCrash)
                return WithAction(entries, "Stabilité (14 j)",
                    "Je cherche la CAUSE EXACTE de tes crashs (module fautif + code d'exception)…",
                    ChatActions.AnalyseCrash(CrashApp(s, q), st));

            // --- ENQUÊTE : symptôme large, demande de bilan, ou intention noyée dans un symptôme ---
            if (symptom || meta || hits == 1)
                return new Reply
                {
                    Text = "Je lance l'enquête complète : écrans, capteurs, connexion, processus en fond, disque, "
                         + "bibliothèques, réglages néfastes, crashs pilote, RAM/XMP, pilote graphique, démarrage, "
                         + "redémarrage en retard… Les mesures s'adaptent à ta plainte. Quelques secondes…",
                    Action = Investigator.Action(q.Trim(), st), Dynamic = true
                };

            // --- CONSEILLER D'OUTILS : pour un BESOIN précis (récupérer un fichier, tester la
            //     RAM, désinstaller proprement, malware, enregistrer l'écran…), propose LE bon
            //     outil — le sien en 1 clic, sinon un gratuit externe AVEC ses risques. Inclut des
            //     mises en garde (outils à éviter). Placé AVANT le match de panneau : ces besoins
            //     spécifiques priment sur une correspondance de symptôme approximative. ---
            {
                ToolAdvisor.Rec rec = ToolAdvisor.Advise(s);
                if (rec != null)
                    return new Reply
                    {
                        Text = rec.Text,
                        Action = rec.WingetId != null ? ChatActions.InstallTool(rec.WingetId, ToolName(rec.WingetId)) : null
                    };
            }

            // Correspondance symptôme (score par mots-clés).
            HelpCatalog.Entry best = null; int bestScore = 0;
            foreach (var e in entries) { int sc = Score(s, e); if (sc > bestScore) { bestScore = sc; best = e; } }
            if (best != null && bestScore >= 2)
                return new Reply { Text = "Pour « " + best.Symptom + " », le bon outil est « " + best.Tool + " ». Je l'ouvre ?", Tool = best, Dynamic = true };

            // --- CERVEAU IA LOCAL (gratuit) : dès qu'il est prêt, il répond à TOUT ce que les
            //     règles ne traitent pas avec certitude — y compris un signal PC faible (bestScore
            //     == 1), où il vaut mieux une vraie réponse qu'un « tu veux dire… ? ». ---
            if (LocalBrain.Enabled)
                return new Reply
                {
                    Text = best != null && bestScore == 1
                        ? "Je regarde ça pour toi…"
                        : "Bonne question — je réfléchis…",
                    Action = ChatActions.AskBrain(q.Trim(), st)
                };
            // Signal FAIBLE sans IA : plutôt que de balayer d'un « pas compris », on vérifie l'intention.
            if (best != null && bestScore == 1)
                return new Reply
                {
                    Text = "Tu veux dire « " + best.Symptom + " » ? Si oui, j'ouvre « " + best.Tool + " » — sinon reformule, "
                         + "ou dis « active l'ia » pour que je réponde à tout.",
                    Tool = best, ShowStarters = true
                };
            if (LocalBrain.SetupStatus != null)
                return new Reply
                {
                    Text = "Mon cerveau IA local s'installe encore en arrière-plan (" + LocalBrain.SetupStatus
                         + ") — repose-moi cette question dans quelques minutes, ou choisis un souci PC :",
                    ShowStarters = true
                };

            return new Reply
            {
                Text = "Pas sûr d'avoir bien compris 🤔. Reformule en quelques mots, choisis un souci courant — "
                     + "ou dis « active l'ia » : un cerveau IA local (gratuit, 100 % sur ta machine) qui répond à tout.",
                ShowStarters = true
            };
        }

        /// <summary>Dépannage PC universel : un souci grave (son, internet, Windows corrompu,
        /// écran noir…) → la grande réparation gratuite quand elle existe, sinon un guide sûr,
        /// pas-à-pas. Renvoie null si ce n'est pas un cas de dépannage (on continue le routage).</summary>
        private static Reply RepairRouter(string s)
        {
            // « Plus de son »
            if (Has(s, "pas de son", "plus de son", "aucun son", "son coupe", "audio ne marche", "pas d'audio", "muet"))
                return new Reply
                {
                    Text = "Pas de son — je commence par le plus efficace : relancer le moteur audio de Windows (le son "
                         + "revient sans redémarrer). Si ça ne suffit pas, on vérifiera le périphérique de sortie.",
                    Action = ChatActions.RepairAudio()
                };

            // « Plus d'internet » (coupé, pas de connexion — distinct de « ça lag »)
            if (Has(s, "plus d'internet", "pas d'internet", "pas de connexion", "aucune connexion", "internet coupe",
                       "connexion coupee", "pas de wifi", "wifi marche pas", "reseau marche pas", "pas de reseau"))
                return new Reply
                {
                    Text = "Connexion coupée alors que tout semble branché — le remède standard : réinitialiser la pile "
                         + "réseau de Windows (Winsock + TCP/IP + DNS). Souvent laissé cassé par un VPN ou un antivirus. "
                         + "Un redémarrage finalise.",
                    Action = ChatActions.RepairNetwork()
                };

            // Windows corrompu / MAJ qui échoue / apps qui ne s'ouvrent plus / réparer Windows
            if (Has(s, "repare windows", "reparer windows", "windows corrompu", "fichiers systeme", "sfc", "dism",
                       "mise a jour echoue", "maj echoue", "windows update marche pas", "0x", "apps s'ouvrent plus",
                       "rien ne s'ouvre", "windows bug", "restaurer windows"))
                return new Reply
                {
                    Text = "Ça sent la corruption système (la cause n°1 des soucis « impossibles à régler »). Je lance les "
                         + "réparateurs officiels de Windows, DISM puis SFC — gratuit, sans risque, mais compte 10-20 min.",
                    Action = ChatActions.RepairWindows()
                };

            // Écran noir / ne démarre pas / ne boote pas → GUIDE (le logiciel ne peut rien faire depuis Windows)
            if (Has(s, "ne demarre pas", "demarre plus", "ne s'allume pas", "ecran noir", "pas d'affichage", "aucun affichage",
                       "boot", "ne boote pas", "reste sur le logo", "bloque au demarrage"))
                return new Reply
                {
                    Text = "Un PC qui ne démarre pas ou reste noir, ça se règle AVANT Windows — voici les gestes sûrs et "
                         + "gratuits, dans l'ordre :\n\n"
                         + "1. Écran : bon câble (HDMI/DP), bonne entrée, et branché sur la CARTE GRAPHIQUE (pas la carte mère).\n"
                         + "2. Courant : teste une autre prise ; sur PC portable, laisse le chargeur 15 min puis rallume.\n"
                         + "3. Reset d'alim : PC éteint, débranche, garde le bouton power appuyé 15 s, rebranche, rallume.\n"
                         + "4. Écran noir APRÈS le logo Windows : force 3 arrêts par le bouton → Windows ouvre la "
                         + "réparation automatique. Choisis « Mode sans échec » et dis-le-moi : de là, je peux agir.\n"
                         + "5. RAM : PC débranché, ré-enfonce bien les barrettes (clic des deux côtés).\n\n"
                         + "Dis-moi à quelle étape ça bloque et ce que tu vois — je continue avec toi.",
                    ShowStarters = false
                };

            // Écran bleu / BSOD
            if (Has(s, "ecran bleu", "bsod", "blue screen", "stop code", "code d'arret", "code arret"))
                return new Reply
                {
                    Text = "Un écran bleu, c'est Windows qui s'arrête pour se protéger — souvent un PILOTE ou la corruption "
                         + "système. Le plan gratuit : d'abord je répare l'intégrité de Windows (DISM + SFC), et je peux "
                         + "relever tes derniers plantages datés. Si tu as noté le « code d'arrêt » (ex. "
                         + "IRQL_NOT_LESS_OR_EQUAL), donne-le-moi, il pointe la cause.",
                    Action = ChatActions.RepairWindows()
                };

            // Périphérique USB / Bluetooth / imprimante → GUIDE court (matériel/pilote)
            if (Has(s, "bluetooth marche pas", "pas de bluetooth", "usb marche pas", "peripherique", "manette marche pas",
                       "clavier marche pas", "souris marche pas", "imprimante", "casque marche pas", "micro marche pas"))
                return new Reply
                {
                    Text = "Périphérique qui ne répond pas — les réflexes gratuits qui marchent 8 fois sur 10 :\n\n"
                         + "1. Débranche/rebranche (autre port USB, de préférence à l'arrière du PC fixe).\n"
                         + "2. Bluetooth : retire l'appareil puis re-apparie-le ; vérifie qu'il est bien en mode appairage.\n"
                         + "3. Pilote : clic droit sur Démarrer → Gestionnaire de périphériques → l'appareil avec un ⚠ → "
                         + "« Désinstaller », puis débranche/rebranche : Windows réinstalle le pilote proprement.\n"
                         + "4. Sans fil : change les piles / recharge, et rapproche le récepteur.\n\n"
                         + "Dis-moi lequel et ce qu'il fait (rien ? clignote ? détecté mais muet ?) et je précise.",
                    ShowStarters = false
                };

            return null;
        }

        /// <summary>Détecte une question d'ACTUALITÉ / temps réel (résultat, météo, prix, news,
        /// date récente…) que le modèle local ne peut pas connaître → recherche web. Renvoie null
        /// si ce n'est pas ce cas (ou si l'IA/web sont indisponibles : on laisse le routage normal).</summary>
        /// <summary>L'utilisateur demande-t-il d'oublier le contexte / repartir de zéro ?
        /// (oubli contextuel = technique anti-hallucination reconnue).</summary>
        internal static bool IsForget(string s)
        {
            return Has(s, "oublie le contexte", "oublie tout", "oublie ce qu'on", "oublie ce que",
                          "nouveau sujet", "change de sujet", "on recommence", "on repart de zero",
                          "reprenons a zero", "efface le contexte", "reinitialise le contexte",
                          "reset le contexte", "vide le contexte", "table rase");
        }

        /// <summary>L'utilisateur demande-t-il un auto-diagnostic des garde-fous anti-hallucination ?
        /// (« mesurer le succès » / observabilité, rendue accessible dans l'app).</summary>
        internal static bool IsSelfTest(string s)
        {
            return Has(s, "teste ta fiabilite", "test de fiabilite", "auto diagnostic", "auto-diagnostic",
                          "diagnostic ia", "diagnostic de l'ia", "verifie tes garde-fous", "test anti hallucination",
                          "test anti-hallucination", "tes garde-fous", "auto test ia", "auto-test");
        }

        /// <summary>L'utilisateur signale-t-il que la réponse était FAUSSE (feedback → auto-amélioration) ?</summary>
        internal static bool IsCorrection(string s)
        {
            return Has(s, "c'est faux", "cest faux", "c'est pas vrai", "c'est pas ca", "c'est pas ça",
                          "tu te trompes", "tu as tort", "t'as tort", "mauvaise reponse", "mauvaise réponse",
                          "reponse fausse", "c'est incorrect", "c'est inexact", "tu dis n'importe quoi",
                          "c'est errone", "c'est erroné", "c'est pas exact");
        }

        private static Reply MaybeWeb(string s, string q, BadgeCatalog.Stats st)
        {
            if (!LocalBrain.Enabled || LocalBrain.WebOff()) return null;

            // Question sur SA machine (« mon GPU plante », « ma config rame ») → local (mesures/
            // réparations), jamais le web. On distingue le personnel du général.
            bool personal = Has(s, "mon pc", "ma config", "ma machine", "chez moi", "mon ordi", "mon setup")
                         || (Has(s, "mon", "ma", "mes") && Has(s, "plante", "crash", "rame", "lag", "bug", "freeze",
                                 "souci", "probleme", "marche pas", "demarre pas", "chauffe", "gele", "fige"));
            if (personal) return null;

            // Signaux « il faut aller chercher » : récence, produits, prix, comparatifs, actualité,
            // culture générale factuelle. Le modèle répond souvent avec assurance MAIS périmé/faux
            // là-dessus → on vérifie sur le web d'emblée.
            bool needsWeb = Has(s,
                // récence / produits / prix / comparatifs
                "dernier", "derniere", "recent", "recente", "actuel", "actuelle", "nouveau", "nouvelle",
                "meilleur", "meilleure", "top ", "prix", "coute", "combien coute", "vaut le coup", "vaut il",
                "comparer", "comparatif", "versus", "sortie", "date de sortie", "quand sort", "quand sortira",
                "2024", "2025", "2026", "classement", "qui a gagne", "qui gagne", "resultat", "score", "match",
                "meteo", "actualite", "actu", "news", "president", "elu", "vainqueur", "champion", "film", "serie",
                "cours de", "bourse", "cotation", "population de", "capitale de", "combien de", "record du",
                // recherche d'ENTITÉ (personne, marque, groupe…) — évite les inventions de l'IA
                "c'est qui", "cest qui", "qui est", "qui sont", "info sur", "infos sur", "info", "infos",
                "renseignement", "biographie", "parle moi de", "parle-moi de", "presente moi", "présente moi",
                "c'est quoi comme", "definition de");
            if (!needsWeb) return null;

            return new Reply { Text = "Je vérifie l'info sur le web plutôt que de deviner…", Action = ChatActions.WebAnswer(q.Trim(), st) };
        }

        /// <summary>Texte qui suit le premier marqueur trouvé, sur la question ORIGINALE (accents
        /// et casse gardés pour la mémoire). Ex. « Retiens que je joue à Valorant » → « je joue à Valorant ».</summary>
        private static string ExtractAfter(string original, string[] markers)
        {
            string low = original.ToLowerInvariant();
            int best = -1, len = 0;
            foreach (string m in markers)
            {
                int k = low.IndexOf(m, StringComparison.Ordinal);
                if (k >= 0 && (best < 0 || k < best)) { best = k; len = m.Length; }
            }
            if (best < 0) return "";
            string tail = original.Substring(best + len).Trim();
            return tail.TrimStart(':', '-', ' ', '"', '«').TrimEnd('"', '»', ' ', '.').Trim();
        }

        /// <summary>Nom d'un jeu/app cité dans le message (pour cibler l'analyse de crash), ou null.
        /// S'appuie sur les exes de jeux connus ; sinon laisse l'analyseur prendre le pire crasheur.</summary>
        private static string CrashApp(string s, string q)
        {
            try
            {
                foreach (string exe in GameScan.PriorityExes())
                {
                    string baseName = exe.ToLowerInvariant().Replace(".exe", "");
                    // nom distinctif d'au moins 4 lettres présent dans le message
                    if (baseName.Length >= 4 && s.Contains(baseName)) return baseName;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Repère une URL (http(s):// ou www.…) dans le message, ou null.</summary>
        private static string FindUrl(string text)
        {
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(text,
                    @"((https?://|www\.)[^\s]+\.[^\s]{2,})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success) return m.Value.TrimEnd('.', ',', ')', ']', '"', '»');
            }
            catch { }
            return null;
        }

        // Nom lisible d'un outil du catalogue à partir de son id winget (pour le bouton).
        private static string ToolName(string wingetId)
        {
            try { foreach (var it in LibScan.Items()) if (string.Equals(it.WingetId, wingetId, StringComparison.OrdinalIgnoreCase)) return it.Name; }
            catch { }
            return wingetId;
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
            r.Dynamic = true;   // « je teste… », « je regarde… » : accroche reformulable (aucune donnée)
            return r;
        }


        // --- Lexique : notions gaming/PC expliquées simplement, outil lié tendu quand il existe.
        //     Format : { "alias1;alias2", "définition", "outil du catalogue ou null" }.
        private static readonly string[][] Lexicon =
        {
            new[] { "ddu", "DDU (Display Driver Uninstaller) : un outil GRATUIT qui désinstalle ton pilote graphique À FOND (restes compris) pour repartir sur une installation propre — le remède aux pilotes abîmés. Installable en 1 clic depuis les Bibliothèques.", "Bibliothèques de jeu" },
            new[] { "gigue;jitter", "La gigue (jitter), c'est la VARIATION du ping d'une seconde à l'autre. Un 30 ms stable se joue très bien ; un ping qui saute de 20 à 80 ms rend le jeu irrégulier même si la moyenne semble bonne. En jeu, la stabilité compte plus que la moyenne.", "Qualité réseau" },
            new[] { "vram", "La VRAM est la mémoire de ta carte graphique (textures, images en préparation). Quand elle déborde, le jeu pioche dans la RAM classique, bien plus lente → grosses saccades. Baisser la qualité des textures est le remède gratuit.", null },
            new[] { "hags", "HAGS (planification GPU accélérée par matériel) : Windows confie la file d'attente du GPU… au GPU lui-même. Selon les jeux et pilotes, ça aide ou ça gêne — c'est l'une des optimisations réversibles de l'app.", null },
            new[] { "dpc", "Les DPC sont des mini-tâches que les PILOTES exécutent en priorité absolue. Un pilote mal écrit y traîne → micro-coupures de son et de souris. Dis « mesure ma latence » et je regarde tes pics DPC en vrai.", "Latence en direct" },
            new[] { "xmp;expo", "XMP (Intel) / EXPO (AMD) : le profil qui fait tourner ta RAM à sa VRAIE vitesse. Sans lui (réglage d'usine), ta RAM tourne bridée. Ça s'active dans le BIOS en 2 minutes — des FPS gratuits que tu as déjà payés.", null },
            new[] { "vsync;v-sync", "La V-Sync synchronise le jeu sur l'écran pour éviter les images déchirées — au prix d'input lag. En compétitif on la coupe, et on préfère G-Sync/FreeSync + une limite de FPS.", null },
            new[] { "gsync;g-sync;freesync", "G-Sync / FreeSync : l'écran s'adapte au rythme du jeu (au lieu de l'inverse) → fluide SANS l'input lag de la V-Sync. Ça s'active dans le panneau NVIDIA/AMD et le menu de l'écran.", "Réglages d'écran" },
            new[] { "dlss;fsr;upscaling", "DLSS (NVIDIA) / FSR (AMD) : le jeu calcule l'image en plus petit et l'algorithme l'agrandit proprement → beaucoup de FPS gagnés pour une perte visuelle minime. GRATUIT — à activer dans les options du jeu.", null },
            new[] { "hpet", "Le HPET est un timer matériel. Le FORCER (vieux « guides boost ») ajoute de la latence — un mythe tenace. L'app détecte et répare ce réglage néfaste gratuitement.", "Réglages néfastes" },
            new[] { "trim", "Le TRIM dit au SSD quelles cases sont libres, pour qu'il reste rapide dans la durée. Certains « optimiseurs » le coupent — l'app le détecte et le réactive gratuitement.", "Réglages néfastes" },
            new[] { "pagefile;fichier d'echange", "Le fichier d'échange est le débordement de la RAM sur le disque. Le désactiver (mauvais conseil courant) fait planter les jeux gourmands (« out of memory »). On le laisse géré par Windows.", "Réglages néfastes" },
            new[] { "throttling;bridage", "Le throttling, c'est ton matériel qui SE BRIDE pour ne pas surchauffer : les FPS s'effondrent d'un coup en pleine partie. Causes classiques : poussière, flux d'air, pâte thermique sèche. Remèdes d'abord gratuits.", "Températures & throttling" },
            new[] { "polling", "Le polling, c'est la fréquence à laquelle ta souris parle au PC : 1000 Hz = toutes les 1 ms. Une souris restée à 125 Hz ajoute ~7 ms d'input lag — vérifie la tienne en direct.", "Fréquence de la souris" },
            new[] { "mpo", "MPO (Multi-Plane Overlay) : Windows compose certaines fenêtres directement dans l'écran. Bugué sur certains pilotes → scintillements et saccades en fenêtré. L'app propose le réglage inverse, réversible.", null },
            new[] { "input lag", "L'input lag est le délai entre ton geste et l'effet à l'écran : souris → jeu → GPU → écran. Chaque maillon compte : polling souris, file d'images, V-Sync, mode plein écran, fréquence de l'écran. Dis « mesure ma latence » pour du concret.", "Latence en direct" },
            new[] { "overlay", "Un overlay est une appli qui se dessine PAR-DESSUS ton jeu (Discord, GeForce Experience, Medal…). Chacun coûte des FPS et peut créer des conflits. En couper est un gain gratuit.", "Qui ralentit mon PC" },
            new[] { "runtime;redist;redistributable", "Les runtimes (Visual C++, DirectX, .NET) sont des briques Microsoft GRATUITES dont les jeux dépendent. Il en manque une → le jeu refuse de démarrer (erreur dll). L'app les installe en 1 clic.", "Bibliothèques de jeu" },
            new[] { "smart;s.m.a.r.t", "Le S.M.A.R.T., c'est l'auto-diagnostic des disques : usure SSD, secteurs défaillants… L'app le lit nativement et te prévient AVANT la panne — sauvegarde tes données au premier ⚠.", "Jeux & disques" },
            new[] { "nagle", "L'algorithme de Nagle regroupe les petits paquets réseau pour économiser la bande passante — bien pour le web, mauvais pour le jeu (il retarde tes actions). L'app propose le réglage anti-Nagle, réversible.", "Réglages TCP/IP" },
            new[] { "islc;standby list", "ISLC (Intelligent Standby List Cleaner) purge la « standby list » : un cache mémoire que Windows vide parfois trop tard, cause de micro-saccades sur certaines configs. Gratuit, installable en 1 clic depuis les Bibliothèques.", "Bibliothèques de jeu" },
            new[] { "markc", "Le « MarkC fix » est la méthode historique pour désactiver TOTALEMENT l'accélération de la souris (déplacement 1:1). L'optimisation souris de l'app fait l'équivalent proprement — et c'est réversible.", "Fréquence de la souris" },
            new[] { "sharpness;nettete;sharpen", "Le filtre de netteté NVIDIA (sharpen) redonne du piqué à l'image, utile avec DLSS/upscaling. L'app propose le réglage communautaire qui ramène l'ANCIEN filtre par jeu (EnableGR535), réversible, dans Optimisations → GPU.", null },
            new[] { "wub;update blocker", "Windows Update Blocker (Wub) coupe le service de mise à jour : plus AUCUN correctif, même de sécurité — le PC accumule des failles connues. L'app le détecte dans « Réglages néfastes » et le répare en un clic.", "Réglages néfastes" },
            new[] { "ollama;ia locale", "Ollama fait tourner des modèles d'IA GRATUITS et open source directement sur ta machine (ta carte graphique fait le travail) : rien n'est envoyé sur internet, aucun abonnement. C'est le cerveau étendu optionnel du Copilote — dis « active l'ia ».", null },
        };

        /// <summary>« C'est quoi X ? » (ou juste « X ? ») → définition claire + l'outil lié.
        /// Un terme seulement CITÉ dans une vraie phrase ne déclenche pas le cours.</summary>
        private static Reply Lexi(string s, List<HelpCatalog.Entry> entries)
        {
            bool asks = Has(s, "c'est quoi", "cest quoi", "c est quoi", "ca veut dire", "sa veut dire",
                               "definition", "explique moi", "a quoi sert", "ca sert a quoi", "kesako", "kezako");
            string[] w = SplitWords(s);
            // Sans question explicite, seul un message d'UN mot (« ddu », « xmp ») vaut demande de
            // définition — un terme cité dans une phrase suit le routage normal (mesures d'abord).
            if (!asks && w.Length != 1) return null;
            foreach (var e in Lexicon)
            {
                foreach (var raw in e[0].Split(';'))
                {
                    string k = raw.Trim();
                    bool hit = k.IndexOf(' ') >= 0 ? FuzzyKey(w, k) : FuzzyWord(w, k);
                    if (!hit) continue;
                    var r = new Reply { Text = e[1] };
                    if (e[2] != null) foreach (var t in entries) if (t.Tool == e[2]) { r.Tool = t; break; }
                    return r;
                }
            }
            return null;
        }

        private static int Score(string q, HelpCatalog.Entry e)
        {
            int sc = 0;
            string[] w = SplitWords(q);
            foreach (var kw in Keywords(e))
            {
                string k = Norm(kw);
                if (k.Length < 3) continue;
                // Même discipline que Has : pas de sous-chaîne globale sur un mot court.
                bool hit = k.IndexOf(' ') >= 0 ? (q.Contains(k) || FuzzyKey(w, k)) : FuzzyWord(w, k);
                if (hit) sc += k.Length >= 5 ? 2 : 1;
            }
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

        // --- Correspondance TOLÉRANTE AUX FAUTES DE FRAPPE --------------------------------------
        //  D'abord la sous-chaîne exacte (comme avant), sinon mot à mot avec une distance
        //  d'édition bornée : « ecrqn » → « ecran », « grqtuit » → « gratuit », « conexion » →
        //  « connexion ». Mots courts (< 5 lettres) : exact seulement, pour éviter les contresens.
        private static bool Has(string s, params string[] ks)
        {
            string[] w = null;
            foreach (var k in ks)
            {
                if (k.IndexOf(' ') >= 0)
                {
                    // Phrase : sous-chaîne exacte, sinon tous les mots présents (ordre libre).
                    if (s.Contains(k)) return true;
                    if (w == null) w = SplitWords(s);
                    if (FuzzyKey(w, k)) return true;
                    continue;
                }
                // Mot simple : JAMAIS de sous-chaîne globale — « blague » contiendrait « lag ».
                if (w == null) w = SplitWords(s);
                if (FuzzyWord(w, k)) return true;
            }
            return false;
        }

        private static string[] SplitWords(string s)
        {
            return s.Split(new[] { ' ', '\'', '-', ',', '.', ';', ':', '!', '?', '(', ')', '"', '/', '\n', '\r', '\t' },
                           StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>Clé simple → un mot du message ressemble ; clé à plusieurs mots → chaque mot
        /// de la clé est présent (peu importe l'ordre : « plein disque » vaut « disque plein »).</summary>
        private static bool FuzzyKey(string[] words, string key)
        {
            if (key.IndexOf(' ') < 0) return FuzzyWord(words, key);
            foreach (var part in key.Split(' '))
                if (part.Length > 0 && !FuzzyWord(words, part)) return false;
            return true;
        }

        private static bool FuzzyWord(string[] words, string k)
        {
            int tol = k.Length >= 8 ? 2 : k.Length >= 5 ? 1 : 0;
            foreach (var w in words)
            {
                if (w == k) return true;
                // Sous-chaîne INTERNE réservée aux clés longues (« surchauffe » ⊃ « chauffe ») :
                // sur les clés courtes elle créait des contresens (« blague » ⊃ « lag »).
                if (k.Length >= 5 && w.Length > k.Length && w.Contains(k)) return true;
                if (k.Length >= 3 && k.Length <= 4 && w.Length > k.Length && w.StartsWith(k, StringComparison.Ordinal)) return true; // « lags », « dlls »
                if (UnitHit(w, k)) return true;                                       // « 60hz », « 144fps », « 1000hz »
                if (tol > 0 && Math.Abs(w.Length - k.Length) <= tol && Lev(w, k, tol) <= tol) return true;
            }
            return false;
        }

        // Nombre collé à son unité : « 60hz » pour la clé « hz », « 240fps » pour « fps ».
        private static bool UnitHit(string w, string k)
        {
            if (k.Length > 3 || w.Length <= k.Length || !w.EndsWith(k, StringComparison.Ordinal)) return false;
            for (int i = 0; i < w.Length - k.Length; i++) if (!char.IsDigit(w[i])) return false;
            return true;
        }

        /// <summary>Distance d'édition (Levenshtein) avec sortie anticipée au-delà de 'max'.</summary>
        private static int Lev(string a, string b, int max)
        {
            int n = a.Length, m = b.Length;
            var prev = new int[m + 1]; var cur = new int[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;
            for (int i = 1; i <= n; i++)
            {
                cur[0] = i; int best = cur[0];
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int v = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                    cur[j] = v; if (v < best) best = v;
                }
                if (best > max) return max + 1;
                var t = prev; prev = cur; cur = t;
            }
            return prev[m];
        }

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

        // --- Développement du langage « texto » : chaque abréviation courante → sa forme pleine,
        //     AVANT tout le routage (les règles, le lexique et les conseils en profitent tous).
        //     Ne remplace QUE des tokens entiers, connus et non ambigus dans un contexte d'aide PC.
        private static readonly System.Collections.Generic.Dictionary<string, string> Abbr =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
        {
            // politesse / salutations
            {"slt","salut"},{"cc","coucou"},{"bjr","bonjour"},{"bsr","bonsoir"},{"wsh","salut"},{"yo","salut"},
            {"stp","s'il te plait"},{"svp","s'il te plait"},{"mrc","merci"},{"dsl","desole"},{"bnj","bonjour"},
            // oui / non
            {"wi","oui"},{"ui","oui"},{"ouai","oui"},{"ouais","oui"},{"nn","non"},{"nan","non"},{"vi","oui"},
            // mots interrogatifs
            {"pk","pourquoi"},{"pq","pourquoi"},{"pkoi","pourquoi"},{"prq","pourquoi"},{"koi","quoi"},{"kwa","quoi"},
            {"ki","qui"},{"kan","quand"},{"kand","quand"},{"cmt","comment"},{"comen","comment"},{"komen","comment"},
            {"komin","comment"},{"cb","combien"},{"cbien","combien"},
            // pronoms / verbes fréquents
            {"g","j'ai"},{"jai","j'ai"},{"j","je"},{"chui","je suis"},{"chuis","je suis"},{"shui","je suis"},
            {"ta","tu as"},{"tas","tu as"},{"ya","il y a"},{"yaa","il y a"},{"jv","je vais"},{"jvai","je vais"},
            {"jvais","je vais"},{"jpe","je peux"},{"jsp","je sais pas"},{"jpp","je n'en peux plus"},{"ta's","tu as"},
            // abréviations courantes
            {"bcp","beaucoup"},{"tjs","toujours"},{"tjr","toujours"},{"tjrs","toujours"},{"tt","tout"},
            {"mtn","maintenant"},{"ct","c'etait"},{"tkt","t'inquiete"},{"askip","a ce qu'il parait"},
            {"qqch","quelque chose"},{"qqn","quelqu'un"},{"qd","quand"},{"ms","mais"},{"pcq","parce que"},
            {"pck","parce que"},{"prkoi","pourquoi"},{"tmp","temps"},{"bg","beau gosse"},
            // problème / négations / tech
            {"pb","probleme"},{"pbm","probleme"},{"pblm","probleme"},{"prob","probleme"},{"probl","probleme"},
            {"souci","souci"},{"ordi","pc"},{"ordinateur","pc"},{"pc","pc"},{"maj","mise a jour"},
            {"dl","telechargement"},{"tel","telecharger"},{"pa","pas"},{"pö","pas"},{"po","pas"},{"pu","plus"},
            {"plu","plus"},{"rien","rien"},{"marche","marche"},{"fonctionne","fonctionne"},
            // ça va & co (renforce la politesse, même isolé)
            {"cava","ca va"},{"sava","ca va"},{"cv","ca va"},{"savapa","ca va pas"},{"cvpa","ca va pas"},
            // --- 2e vague : adverbes, temps, accords ---
            {"vrmt","vraiment"},{"vrm","vraiment"},{"vraimen","vraiment"},{"grv","grave"},{"tro","trop"},
            {"tr","trop"},{"jms","jamais"},{"jame","jamais"},{"tjt","toujours"},{"auj","aujourd'hui"},
            {"ajd","aujourd'hui"},{"dmain","demain"},{"enfait","en fait"},{"enfai","en fait"},{"anfin","enfin"},
            {"fo","faut"},{"fau","faut"},{"vazy","vas y"},{"vazi","vas y"},{"jariv","j'arrive"},
            {"psk","parce que"},{"pask","parce que"},{"parceke","parce que"},{"tfk","tu fais"},{"tufe","tu fais"},
            {"jte","je te"},{"jtai","je t'ai"},{"cetai","c'etait"},{"quand","quand"},{"dak","ok"},{"dac","ok"},
            {"dacc","ok"},{"okey","ok"},{"okay","ok"},{"oke","ok"},{"nikel","nickel"},{"trkl","tranquille"},
            {"pfff","bof"},{"bref","bref"},
            // --- 3e vague : vocabulaire panne (variantes de saisie → mot canonique) ---
            {"lague","lag"},{"laggue","lag"},{"lagge","lag"},{"laggs","lag"},{"lagg","lag"},{"laag","lag"},
            {"freez","freeze"},{"frize","freeze"},{"frise","freeze"},{"fige","freeze"},{"gele","freeze"},
            {"bugg","bug"},{"buggue","bug"},{"bugue","bug"},{"boggue","bug"},{"boque","bug"},{"beug","bug"},
            {"plante","plante"},{"plente","plante"},{"crash","crash"},{"krash","crash"},{"crache","crash"},
            {"mouline","rame"},{"patine","rame"},{"ramme","rame"},{"ramette","rame"},
            {"saccade","saccade"},{"sacade","saccade"},{"stotter","stutter"},{"stutt","stutter"},
            {"reboot","redemarre"},{"restart","redemarre"},{"redemare","redemarre"},{"bloque","bloque"},
            {"lenteur","lent"},{"lag","lag"},{"co","connexion"},{"deco","deconnecte"},{"deconecte","deconnecte"},
            {"chaud","chaud"},{"brulant","chaud"},{"cramme","chaud"},{"fournaise","chaud"},{"bruyant","bruyant"},
            {"ventilo","ventilateur"},{"ventilos","ventilateur"},{"screen","ecran"},{"moniteur","ecran"},
            {"ecran","ecran"},{"clavié","clavier"},{"souri","souris"},{"soury","souris"},{"micro","micro"},
            {"manette","manette"},{"drivers","pilotes"},{"driver","pilote"},
        };

        private static string Expand(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf(' ') < 0 && s.Length > 12) return s;
            string[] w = s.Split(' ');
            var sb = new StringBuilder(s.Length + 16);
            for (int i = 0; i < w.Length; i++)
            {
                if (w[i].Length == 0) continue;
                string rep;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(Abbr.TryGetValue(w[i], out rep) ? rep : w[i]);
            }
            return sb.ToString();
        }

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
