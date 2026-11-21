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
        }

        /// <summary>Une cause rendue en CARTE dans le chat : pastille d'impact colorée
        /// (rouge ≥ 85, orange ≥ 65, jaune sinon), texte, et sa correction sur clic.</summary>
        public sealed class Card
        {
            public int Impact;
            public string Text;
            public ChatAction Fix;
        }

        // Réponse unique à toute demande portant sur l'IA locale.
        // Catalogue des réglages, construit une fois (les actions en un clic s'y réfèrent).
        private static readonly Lazy<List<Tweak>> Catalogue = new Lazy<List<Tweak>>(Catalog.All);

        private const string IaRetiree = "L'IA locale a été retirée d'ONYX : le Copilote répond avec ses règles, ta configuration réelle, la base de connaissances et la recherche web.";

        public static Reply Intro(BadgeCatalog.Stats st)
        {
            string h = st != null ? "  Santé actuelle de ton PC : " + st.Health + " %." : "";
            return new Reply
            {
                Text = "Bonjour, je suis le Copilote — l'assistant de ton PC." + h +
                       "\nDis-moi ce qui cloche — je dépanne ton PC quel que soit le souci : jeux qui rament, plus de "
                     + "son, plus d'internet, Windows corrompu, écran noir, ça crash… je mesure, je répare (gratuitement, "
                     + "avec ton accord), et pour ce que le logiciel ne peut pas faire seul, je te guide pas à pas. "
                     + "Je réponds aussi aux questions : « c'est quoi le DLSS ? »…",
                ShowStarters = true
            };
        }

        /// <summary>Garde-fou VIE PRIVÉE partagé : si le texte destiné à une API TIERCE (traduction, actus,
        /// livre, série, distance, définition…) contient une info perso (IBAN, tél, e-mail, carte, n° sécu),
        /// on REFUSE de l'envoyer et on le dit. Même logique que ChatActions.WebAnswer, appliquée aux
        /// connecteurs directs qui, eux, ne passaient pas par ce filtre.</summary>
        internal static Reply PiiBlock(string text)
        {
            try
            {
                var pii = PrivacyGuard.Detect(text);
                if (pii != null && pii.Count > 0)
                    return new Reply { Text = "⚠️ Ta demande contient une info personnelle (" + string.Join(", ", pii.ToArray())
                        + "). Je ne l'envoie PAS à un service externe (vie privée). Retire l'info sensible et redemande.", ShowStarters = true };
            }
            catch { }
            return null;
        }

        /// <summary>Version PROTÉGÉE de Answer : un bug dans un outil ne doit jamais faire tomber
        /// l'application — le Copilote s'excuse, note l'incident, et la conversation continue.
        /// C'est cette version que l'interface appelle.</summary>
        public static Reply SafeAnswer(string q, BadgeCatalog.Stats st, Action<string, int> log, Reply last = null)
        {
            try
            {
                var r = Answer(q, st, log, last);
                if (r != null) return r;
                return new Reply { Text = "Je n'ai pas su quoi répondre à ça — reformule et je réessaie.", ShowStarters = true };
            }
            catch (Exception ex)
            {
                try { SafetyNet.Record("copilote", ex); } catch { }
                return new Reply
                {
                    Text = "Aïe — j'ai buggé en traitant ta demande (" + ex.GetType().Name + "). Ce n'est pas ta faute et "
                         + "ton PC n'a rien subi : l'incident est noté dans « bt-erreurs.txt ». Reformule autrement, ou "
                         + "demande-moi autre chose — je continue de fonctionner.",
                    ShowStarters = true
                };
            }
        }

        /// <summary>'last' = dernière réponse du Copilote qui portait quelque chose d'actionnable
        /// (outil / correction / plan) : un « oui » ou un « non » de l'utilisateur s'y rapporte.</summary>
        public static Reply Answer(string q, BadgeCatalog.Stats st, Action<string, int> log, Reply last = null)
        {
            var entries = HelpCatalog.Entries(log);
            string s = CopiloteTexte.Prepare(q);   // accents à plat, apostrophes, texto développé
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
                return new Reply { Text = "Salut ! Décris ton souci et j'ouvre le bon outil. Ou choisis ci-dessous.", ShowStarters = true };
            // « ça va PAS » (négatif) : on ne répond pas « ça va ! », on demande ce qui cloche.
            if (Has(s, "ca va pas", "ca va plus", "sa va pas", "ca marche pas") && SplitWords(s).Length <= 5)
                return new Reply { Text = "Ah, qu'est-ce qui ne va pas ? Dis-moi ce qui se passe (ça rame, ça crash, plus de son, plus d'internet…) et je m'en occupe.", ShowStarters = true };
            // « ça va ? » et ses formes familières (cava, sava, cv…). Messages COURTS seulement :
            // « comment va mon pc » doit rester une question de santé, pas de la politesse.
            if (SplitWords(s).Length <= 4 && !Has(s, "pc", "jeu")
                && Has(s, "ca va", "ca roule", "ca gaze", "quoi de neuf", "tu vas bien", "comment vas tu", "bien et toi"))
                return new Reply { Text = "Ça va, merci 🙂 Et toi ? Je suis prêt : dis-moi ce qui cloche sur ton PC, ou pose-moi n'importe quelle question.", ShowStarters = true };
            if (Has(s, "au revoir", "a plus", "bye", "ciao", "a bientot", "bonne journee", "bonne soiree"))
                return new Reply { Text = "À bientôt ! Reviens dès que ton PC fait des siennes. 👋", ShowStarters = false };
            if (Has(s, "merci", "thanks", "top", "parfait", "genial", "super", "nickel", "cool"))
                return new Reply { Text = "Avec plaisir ! Autre chose à diagnostiquer ?", ShowStarters = true };
            if (Has(s, "qui es tu", "tu es qui", "c'est quoi ce chat", "tu es un robot", "tu es une ia", "es tu une ia", "es tu humain"))
                return new Reply { Text = "Je suis le Copilote de ton PC : un assistant qui tourne sur TA machine. Je mesure, je répare, je conseille, et je réponds avec mes règles, ta configuration réelle et ma base de connaissances. Par défaut tout reste local ; si tu actives la recherche web, je vais aussi chercher l'info à jour en ligne (désactivable). Alors, on regarde quoi ?", ShowStarters = true };

            // --- Oubli contextuel (technique anti-hallucination : réduire la fenêtre pour ne pas
            //     être influencé par les échanges précédents). « nouveau sujet », « oublie »… ---
            if (IsForget(s))
            {
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

            // --- HEURE / DATE : donnée LOCALE, exacte, hors-ligne (0 dépendance). ---
            if (IsTimeQuery(s))
                return new Reply { Text = LiveData.TimeNow(), ShowStarters = true };

            // --- MÉTÉO : désormais RÉELLE via Open-Meteo (gratuit, sans clé) + géoloc IP, plutôt que
            //     l'ancien refus honnête. Nécessite internet ; l'heure, elle, marche hors-ligne. ---
            if (IsWeather(s))
            {
                if (WebPrefs.WebOff())
                    return new Reply { Text = "Pour la météo j'ai besoin d'internet (actuellement coupé). Dis « active internet », "
                        + "ou ouvre ton appli Météo. (L'heure, je la donne hors-ligne : demande « quelle heure est-il ».)", ShowStarters = true };
                return new Reply { Text = "Je regarde la météo en direct…", Action = ChatActions.WeatherAction(q, st) };
            }

            // --- MISE À JOUR D'ONYX LUI-MÊME (avant le bilan Windows : « mets à jour ONYX » est plus précis) ---
            if (UtilityTools.IsAppUpdate(s))
                return new Reply { Text = "Je regarde s'il existe une version plus récente d'ONYX…",
                    Action = ChatActions.UpdateCheckAction() };

            // --- BILAN MISES À JOUR : il MESURE (100 % local), puis propose — OU NON — d'installer.
            //     Jamais d'installation automatique : le bouton ouvre la page officielle, l'utilisateur décide. ---
            if (UtilityTools.IsUpdateCheck(s))
                return new Reply { Text = "Je fais le bilan des mises à jour (pilote GPU, Windows, disque)…",
                    Action = ChatActions.UpdatesCheckAction() };

            // --- BATTERIE (mesure locale instantanée ; conseil jeu : jouer sur batterie = perfs bridées) ---
            if (UtilityTools.IsBattery(s))
            {
                try
                {
                    var ps = System.Windows.Forms.SystemInformation.PowerStatus;
                    if ((ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) != 0)
                        return new Reply { Text = "🔌 Pas de batterie détectée : tu es sur un PC FIXE (secteur). "
                            + "C'est la meilleure configuration pour jouer — aucune perf bridée par l'économie d'énergie.", ShowStarters = true };
                    int pct = (int)Math.Round(ps.BatteryLifePercent * 100);
                    bool ac = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
                    string t = "🔋 Batterie : " + pct + " %" + (ac ? " — branché sur secteur." : " — sur batterie.");
                    if (!ac) t += "\n⚠️ Pour JOUER, branche le secteur : sur batterie, Windows et le GPU se brident "
                        + "(FPS en baisse et stutter garantis). C'est mesurable, pas une superstition.";
                    else if (pct < 20) t += "\nLa charge est basse mais tu es branché : rien à faire.";
                    return new Reply { Text = t, ShowStarters = true };
                }
                catch { }
            }

            // --- UPTIME : depuis quand le PC tourne (local, instantané) ---
            if (UtilityTools.IsUptime(s))
            {
                var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
                string dur = (up.Days > 0 ? up.Days + " jour(s) " : "") + up.Hours + " h " + up.Minutes.ToString("00");
                string t2 = "⏱️ Ton PC tourne depuis " + dur + " sans redémarrage complet.";
                if (up.Days >= 7)
                    t2 += "\n⚠️ Plus de 7 jours : redémarre quand tu peux — ça purge les fuites mémoire, applique les "
                        + "mises à jour en attente et re-stabilise les pilotes. (Attention : « Arrêter » avec le "
                        + "démarrage rapide de Windows ne compte PAS comme un vrai redémarrage.)";
                else t2 += "\n✅ C'est raisonnable — rien à faire.";
                return new Reply { Text = t2, ShowStarters = true };
            }

            // --- LIBÉRER DE LA PLACE : hibernation (réversible) et nettoyage profond (DISM officiel) ---
            if (UtilityTools.IsHibernateOff(s))
            {
                double hgb = ChatActions.HibernationSizeGb();
                if (hgb < 0) return new Reply { Text = "L'hibernation semble déjà désactivée (pas de hiberfil.sys) — rien à récupérer ici. "
                    + "Dis « libère de la place » pour le grand bilan stockage.", ShowStarters = true };
                return new Reply { Text = "hiberfil.sys occupe ~" + hgb.ToString("0.0") + " Go. Le bouton le récupère en 5 secondes "
                    + "(et c'est réversible : « réactive l'hibernation »).", Action = ChatActions.HibernateOffAction(hgb) };
            }
            if (UtilityTools.IsHibernateOn(s))
                return new Reply { Text = "Je peux réactiver l'hibernation (et le démarrage rapide) — clic ci-dessous.",
                    Action = ChatActions.HibernateOnAction() };
            if (UtilityTools.IsDeepClean(s))
                return new Reply { Text = "Nettoyage OFFICIEL du magasin de composants Windows (DISM) : souvent 2 à 8 Go récupérés, "
                    + "5 à 20 minutes, aucun risque pour tes fichiers.", Action = ChatActions.ComponentCleanupAction() };
            if (UtilityTools.IsHeavyApps(s))
                return new Reply { Text = "Je mesure le poids RÉEL de chaque application installée (Windows se contente de la taille "
                    + "déclarée, souvent fausse). Quelques secondes la première fois, ensuite c'est en cache…",
                    Action = ChatActions.MeasureHeavyApps() };
            if (UtilityTools.IsBigFiles(s))
                return new Reply { Text = "Je passe tes disques au crible : vidéos, archives (zip, rar, iso…) et autres gros fichiers, "
                    + "hors système et hors dossiers de jeux. Quelques secondes par disque…",
                    Action = ChatActions.MeasureBigFiles() };

            // --- PROFIL ONYX : export / restauration (réinstaller Windows sans rien perdre) ---
            if (UtilityTools.IsExportProfile(s))
                return new Reply { Text = "J'emballe ta mémoire, tes réglages et tes documents dans UN zip sur le Bureau — clic ci-dessous.",
                    Action = ChatActions.ExportProfileAction() };
            if (UtilityTools.IsImportProfile(s))
                return new Reply { Text = "Je peux restaurer le dernier ONYX-profil-*.zip posé sur le Bureau (l'existant sera d'abord sauvegardé).",
                    Action = ChatActions.ImportProfileAction() };

            // --- MÉDECIN DES JOURNAUX WINDOWS : tout ce que Windows a noté, traduit en diagnostic ---
            if (UtilityTools.IsLogDoctor(s))
                return new Reply { Text = "Je lis les journaux Windows (Système + Application) des 14 derniers jours et "
                    + "je traduis ce qu'ils disent…", Action = ChatActions.LogDoctorAction(14) };

            // --- DEFENDER & JEUX : exclure les dossiers de jeux de l'analyse temps réel (réversible) ---
            if (UtilityTools.IsShieldUndo(s))
            {
                var undo = GameShield.SuggestedPaths();
                if (undo.Count == 0) return new Reply { Text = "Je n'ai pas trouvé de dossiers de jeux Steam à remettre sous analyse.", ShowStarters = true };
                return new Reply { Text = "Je peux remettre tes dossiers de jeux sous l'analyse de Defender (retour à l'état d'origine).",
                    Action = ChatActions.ShieldRestoreAction(undo) };
            }
            if (UtilityTools.IsGameShield(s))
            {
                System.Collections.Generic.List<string> miss;
                string shieldTxt;
                try { shieldTxt = GameShield.Text(out miss); }
                catch (Exception ex) { shieldTxt = "Analyse impossible : " + ex.Message; miss = new System.Collections.Generic.List<string>(); }
                return new Reply
                {
                    Text = shieldTxt,
                    Action = (miss != null && miss.Count > 0) ? ChatActions.ShieldExcludeAction(miss) : null,
                    ShowStarters = miss == null || miss.Count == 0
                };
            }

            // --- MES JEUX SONT-ILS SUR SSD ? (le support décide des temps de chargement) ---
            if (UtilityTools.IsGameStorage(s))
            {
                string st2 = null;
                try { st2 = SteamGames.StorageText(); } catch { }
                if (st2 != null) return new Reply { Text = st2, ShowStarters = true };
                return new Reply { Text = "Je n'ai pas trouvé de jeux Steam installés sur ce PC. "
                    + "Pour le reste, « où sont passés mes go » liste les plus gros dossiers de tous tes disques.", ShowStarters = true };
            }

            // --- OÙ SONT PASSÉS MES GO : classement des plus gros dossiers (voit aussi les jeux hors Steam) ---
            if (UtilityTools.IsBigFolders(s))
                return new Reply { Text = "J'analyse tes disques et je classe les plus gros dossiers (30 secondes max)…",
                    Action = ChatActions.BigFoldersAction() };

            // --- JEUX QUI DORMENT : le plus gros levier d'espace disque chez un joueur ---
            if (UtilityTools.IsDormantGames(s))
            {
                string dorm = null;
                try { dorm = SteamGames.DormantText(120); } catch { }
                if (dorm != null) return new Reply { Text = dorm, ShowStarters = true };
                return new Reply { Text = "Je n'ai pas trouvé de bibliothèque Steam sur ce PC (ou elle est vide). "
                    + "Pour le reste, dis « libère de la place » : je fais le grand bilan stockage.", ShowStarters = true };
            }

            // --- GOULOT D'ÉTRANGLEMENT : CPU ou GPU ? (20 s de mesure, jeu lancé de préférence) ---
            if (UtilityTools.IsBottleneck(s))
                return new Reply { Text = "Je mesure la charge CPU et l'utilisation GPU pendant 20 secondes. "
                    + "Idéalement, laisse ton JEU tourner en pleine action pendant la mesure — au bureau, les chiffres ne "
                    + "veulent rien dire (je te le dirai si c'est le cas).", Action = ChatActions.BottleneckAction() };

            // --- SANTÉ SMART des disques (local, lecture seule) : prévenir AVANT la panne ---
            if (UtilityTools.IsDiskHealth(s))
                return new Reply { Text = ChatActions.DiskHealthText(), ShowStarters = true };

            // --- JOURNAL DE BORD : tout ce qu'ONYX a changé, daté — transparence totale ---
            if (UtilityTools.IsJournal(s))
                return new Reply { Text = Journal.TailText(15), ShowStarters = true };

            // --- TENDANCE SANTÉ : le score du cockpit, historisé (1 mesure/jour par le Gardien) ---
            if (UtilityTools.IsHealthTrend(s))
                return new Reply { Text = HealthTrend.TrendText(), ShowStarters = true };

            // --- « ÇA MARCHAIT HIER ! » : diff entre la photo quotidienne du système et maintenant ---
            if (UtilityTools.IsWhatChanged(s))
                return new Reply { Text = StateDiff.DiffText(), ShowStarters = true };

            // --- LE GARDIEN : le résumé des alertes du jour (disque, SMART, reboot, uptime) ---
            if (UtilityTools.IsGuardian(s))
            {
                var al = Guardian.Alerts();
                if (al.Count == 0)
                    return new Reply { Text = "🛡 Gardien : RIEN à signaler aujourd'hui — disque OK, disques sains (SMART), pas de "
                        + "redémarrage en attente, uptime raisonnable. Je re-vérifie à chaque ouverture d'ONYX.", ShowStarters = true };
                var gsb = new System.Text.StringBuilder();
                gsb.Append("🛡 Gardien — ").Append(al.Count).Append(" alerte(s) :\n");
                foreach (var x in al) gsb.Append("• ").Append(x).Append('\n');
                bool disk = false; foreach (var x in al) if (x.Contains("Disque système")) disk = true;
                return new Reply { Text = gsb.ToString().TrimEnd(),
                    Action = disk ? ChatActions.StorageAuditAction() : null };
            }

            // --- UTILITAIRES « pour plein de choses » (public-apis) ---
            // 1) Conversion d'UNITÉS : LOCALE, exacte, hors-ligne (ex. « 100 km en miles »).
            {
                string u = UtilityTools.LocalUnit(q);
                if (u != null) return new Reply { Text = u, ShowStarters = true };
            }
            // 1bis) CALCULATRICE : LOCALE, hors-ligne (« 15% de 240 », « 3+4*2 », « racine de 2 »).
            {
                string calc = UtilityTools.Calc(q);
                if (calc != null) return new Reply { Text = calc, ShowStarters = true };
            }
            // 2) DEVISES (Frankfurter/BCE), 3) CRYPTO (CoinGecko), 4) JOURS FÉRIÉS (Nager.Date) — réseau.
            {
                UtilityTools.Triple cur = UtilityTools.Currency(q);
                if (cur != null)
                {
                    if (WebPrefs.WebOff()) return new Reply { Text = "Pour le taux de change j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                    return new Reply { Text = "Je regarde le taux…", Action = ChatActions.CurrencyAction(cur.Amount, cur.A, cur.B, st) };
                }
            }
            {
                string coin = UtilityTools.CryptoId(s);
                if (coin != null)
                {
                    if (WebPrefs.WebOff()) return new Reply { Text = "Pour le cours j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                    return new Reply { Text = "Je regarde le cours…", Action = ChatActions.CryptoAction(coin, st) };
                }
            }
            if (UtilityTools.IsHoliday(s))
            {
                if (WebPrefs.WebOff()) return new Reply { Text = "Pour les jours fériés j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                UtilityTools.CountryHit ch = UtilityTools.HolidayCountryQuery(s);
                if (ch != null && ch.Iso != "FR")
                    return new Reply { Text = "Je regarde les jours fériés…", Action = ChatActions.HolidaysCountryAction(ch.Iso, ch.Name, st) };
                return new Reply { Text = "Je regarde les jours fériés…", Action = ChatActions.HolidaysAction(st) };
            }
            // 5) TRADUCTION (MyMemory) — si web actif ; sinon la traduction n'est pas disponible.
            if (!WebPrefs.WebOff())
            {
                UtilityTools.TransJob tj = UtilityTools.TranslateJob(q);
                if (tj != null)
                {
                    Reply pb = PiiBlock(tj.Text); if (pb != null) return pb;
                    return new Reply { Text = "Je traduis…", Action = ChatActions.TranslateAction(tj.Text, tj.From, tj.To, st) };
                }
            }
            // 6) MON IP publique (ipwho.is), 7) LEVER/COUCHER DU SOLEIL (Open-Meteo) — réseau.
            if (UtilityTools.IsMyIp(s))
            {
                if (WebPrefs.WebOff()) return new Reply { Text = "Pour ton IP PUBLIQUE j'ai besoin d'internet (coupé). Ton IP LOCALE se voit avec « ipconfig ».", ShowStarters = true };
                return new Reply { Text = "Je regarde ton IP…", Action = ChatActions.MyIpAction(st) };
            }
            if (UtilityTools.IsSun(s))
            {
                if (WebPrefs.WebOff()) return new Reply { Text = "Pour les horaires du soleil j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                return new Reply { Text = "Je regarde le soleil…", Action = ChatActions.SunAction(q, st) };
            }
            // 8) PRODUIT / NUTRITION (Open Food Facts), 9) CODE POSTAL → VILLE (Zippopotam) — réseau.
            {
                string prod = UtilityTools.FoodQuery(q);
                if (prod != null)
                {
                    if (WebPrefs.WebOff()) return new Reply { Text = "Pour les infos produit j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                    Reply pb = PiiBlock(q); if (pb != null) return pb;
                    return new Reply { Text = "Je cherche le produit…", Action = ChatActions.FoodAction(prod, st) };
                }
            }
            {
                string cp = UtilityTools.PostalQuery(s);
                if (cp != null)
                {
                    if (WebPrefs.WebOff()) return new Reply { Text = "Pour la ville d'un code postal j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                    return new Reply { Text = "Je regarde le code postal…", Action = ChatActions.PostalAction(cp, st) };
                }
            }
            // 10) AVIONS EN VOL (OpenSky), 11) PHOTO ASTRO DU JOUR (NASA APOD) — réseau, sans clé.
            if (UtilityTools.IsFlights(s))
            {
                if (WebPrefs.WebOff()) return new Reply { Text = "Pour les avions en vol j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                return new Reply { Text = "Je regarde le ciel…", Action = ChatActions.FlightsAction(st) };
            }
            if (UtilityTools.IsApod(s))
            {
                if (WebPrefs.WebOff()) return new Reply { Text = "Pour la photo astro du jour j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                return new Reply { Text = "Je regarde la photo du jour de la NASA…", Action = ChatActions.ApodAction(st) };
            }
            // 12) DETTE PUBLIQUE DE LA FRANCE en direct (dettedelafrance.fr) — réseau, sans clé.
            if (UtilityTools.IsDebt(s))
            {
                if (WebPrefs.WebOff()) return new Reply { Text = "Pour la dette de la France en direct j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                return new Reply { Text = "Je regarde la dette en direct…", Action = ChatActions.DebtAction(st) };
            }
            // 13) QUALITÉ DE L'AIR (Open-Meteo) — réseau, sans clé.
            if (UtilityTools.IsAir(s))
            {
                if (WebPrefs.WebOff()) return new Reply { Text = "Pour la qualité de l'air j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                return new Reply { Text = "Je regarde la qualité de l'air…", Action = ChatActions.AirAction(q, st) };
            }
            // 14) PHASE DE LA LUNE : calcul LOCAL, hors-ligne (comme l'heure) — réponse immédiate.
            if (UtilityTools.IsMoon(s)) return new Reply { Text = LiveData.MoonPhase(), ShowStarters = true };
            // 15) DISTANCE entre 2 villes, 16) SÉISMES (USGS), 17) POSITION ISS — réseau, sans clé.
            {
                UtilityTools.Pair pr = UtilityTools.DistanceQuery(q);
                if (pr != null)
                {
                    if (WebPrefs.WebOff()) return new Reply { Text = "Pour la distance entre 2 villes j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                    Reply pb = PiiBlock(q); if (pb != null) return pb;
                    return new Reply { Text = "Je calcule la distance…", Action = ChatActions.DistanceAction(pr.A, pr.B, st) };
                }
            }
            if (UtilityTools.IsQuake(s))
            {
                if (WebPrefs.WebOff()) return new Reply { Text = "Pour les séismes récents j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                return new Reply { Text = "Je regarde les séismes récents…", Action = ChatActions.QuakesAction(st) };
            }
            if (UtilityTools.IsIss(s))
            {
                if (WebPrefs.WebOff()) return new Reply { Text = "Pour la position de l'ISS j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                return new Reply { Text = "Je localise l'ISS…", Action = ChatActions.IssAction(st) };
            }
            // 18) ACTUALITÉS (Google Actualités RSS FR) — réseau, sans clé. "" = à la une, sinon recherche.
            {
                string news = UtilityTools.NewsQuery(q);
                if (news != null)
                {
                    if (WebPrefs.WebOff()) return new Reply { Text = "Pour les actualités j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                    Reply pb = PiiBlock(q); if (pb != null) return pb;
                    return new Reply { Text = "Je regarde les actualités…", Action = ChatActions.NewsAction(news, st) };
                }
            }
            // 19) JEUX GRATUITS PC (FreeToGame) — réseau, sans clé. "" = tous, sinon par genre.
            {
                string genre = UtilityTools.FreeGamesQuery(q);
                if (genre != null)
                {
                    if (WebPrefs.WebOff()) return new Reply { Text = "Pour la liste des jeux gratuits j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                    return new Reply { Text = "Je cherche des jeux gratuits…", Action = ChatActions.FreeGamesAction(genre, st) };
                }
            }
            // 20) LIVRES (Open Library) — réseau, sans clé.
            {
                string book = UtilityTools.BookQuery(q);
                if (book != null)
                {
                    if (WebPrefs.WebOff()) return new Reply { Text = "Pour chercher un livre j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                    Reply pb = PiiBlock(q); if (pb != null) return pb;
                    return new Reply { Text = "Je cherche le livre…", Action = ChatActions.BookAction(book, st) };
                }
            }
            // 21) POKÉMON (PokéAPI), 22) SÉRIES TV (TVMaze), 23) PRIX NOBEL — réseau, sans clé.
            {
                string poke = UtilityTools.PokemonQuery(q);
                if (poke != null)
                {
                    if (WebPrefs.WebOff()) return new Reply { Text = "Pour une fiche Pokémon j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                    Reply pb = PiiBlock(q); if (pb != null) return pb;
                    return new Reply { Text = "Je regarde le Pokédex…", Action = ChatActions.PokemonAction(poke, st) };
                }
            }
            {
                string show = UtilityTools.ShowQuery(q);
                if (show != null)
                {
                    if (WebPrefs.WebOff()) return new Reply { Text = "Pour une fiche série j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                    Reply pb = PiiBlock(q); if (pb != null) return pb;
                    return new Reply { Text = "Je cherche la série…", Action = ChatActions.ShowAction(show, st) };
                }
            }
            {
                UtilityTools.NobelHit nob = UtilityTools.NobelQuery(s);
                if (nob != null)
                {
                    if (WebPrefs.WebOff()) return new Reply { Text = "Pour le prix Nobel j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                    return new Reply { Text = "Je regarde le prix Nobel…", Action = ChatActions.NobelAction(nob.Cat, nob.CatFr, nob.Year, st) };
                }
            }

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
            // DÉFINITION d'un mot hors-jeu (« définition de X », « que veut dire X ») → Wikipédia FR.
            {
                string def = UtilityTools.DefineQuery(q);
                if (def != null)
                {
                    if (WebPrefs.WebOff()) return new Reply { Text = "Pour une définition j'ai besoin d'internet (coupé). Dis « active internet ».", ShowStarters = true };
                    Reply pb = PiiBlock(q); if (pb != null) return pb;
                    return new Reply { Text = "Je cherche la définition…", Action = ChatActions.DefineAction(def, st) };
                }
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
            bool iClean  = Has(s, "espace", "disque plein", "nettoy", "liberer", "libere", "place disque", "temporaire",
                                  "saturé", "sature", "de la place", "gros fichiers", "gros dossiers", "prend de la place");
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
                return new Reply { Text = IaRetiree, ShowStarters = true };
            }
            // Repartir de zéro dans la conversation IA (oublie le contexte précédent).
            if (Has(s, "nouvelle conversation", "oublie tout", "oublie la conversation", "on repart de zero", "reset la conversation", "efface la conversation"))
            {
                return new Reply { Text = "C'est oublié — on repart sur une page blanche. Qu'est-ce que je peux faire pour toi ?", ShowStarters = true };
            }
            // Activer / couper la recherche internet (« coupe » testé avant « active »).
            // « coupe la recherche bing » vise le menu Démarrer (réglage du catalogue), pas la recherche du Copilote.
            if (Has(s, "coupe internet", "coupe la recherche", "sans internet", "reste hors ligne", "mode hors ligne", "desactive internet")
                && !s.Contains("bing"))
            {
                WebPrefs.SetWebEnabled(false);
                return new Reply { Text = "Recherche internet coupée — je reste 100 % hors-ligne (mesures, réparations, base de connaissances). Dis « active internet » pour la rétablir.", ShowStarters = true };
            }
            if (Has(s, "active internet", "activer internet", "connecte toi", "connexion internet", "va sur internet", "acces internet"))
            {
                WebPrefs.SetWebEnabled(true);
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
                return new Reply { Text = "Voilà, j'ai tout oublié (matériel, préférences, notes). On repart de zéro.", ShowStarters = true };
            }

            // --- BASE DE CONNAISSANCES (RAG) : gestion ---
            if (Has(s, "recharge mon savoir", "reconstruis ta base", "recharge ta base", "j'ai ajoute des documents",
                       "actualise ta base", "reindexe"))
            {
                return new Reply { Text = "Ta base de connaissances (dossier bt-savoir) est relue à chaque question : tes documents seront pris en compte dès la prochaine.", ShowStarters = false };
            }
            if (Has(s, "que contient ta base", "ta base de connaissances", "que sais tu faire de ta base", "contenu de ta base", "tes documents"))
                return new Reply { Text = "Ma base de connaissances contient :\n\n" + KnowledgeBase.Describe()
                    + "\n\nPour l'enrichir, dépose des .txt/.md dans le dossier bt-savoir (dis « ou mettre mes documents ») puis « recharge mon savoir ».", ShowStarters = false };
            if (Has(s, "ou mettre mes documents", "ou ajouter des documents", "ou deposer mes fiches", "dossier savoir", "ajouter un document"))
            {
                string folder = KnowledgeBase.FolderPath();
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true }); } catch { }
                return new Reply { Text = "Dépose tes fiches (.txt ou .md) ici :\n" + folder + "\n(le dossier vient de s'ouvrir). Puis dis « recharge mon savoir ».", ShowStarters = false };
            }

            // Recherche web EXPLICITE : « cherche sur internet X », « google X »…
            if (Has(s, "cherche sur internet", "cherche sur le web", "recherche internet", "google", "sur internet", "sur le web", "recherche web"))
                return new Reply { Text = "Je cherche ça sur le web…", Action = ChatActions.WebAnswer(q.Trim(), st) };
            if (Has(s, "active l'ia", "activer l'ia", "active ton ia", "ia locale", "mon ia", "cerveau ia", "ollama", "intelligence artificielle"))
                return new Reply { Text = IaRetiree, ShowStarters = true };
            if (iReport)
                return new Reply
                {
                    Text = "Je te prépare l'audit complet — un clic, rien n'est modifié au système :",
                    Action = ChatActions.MakeReport()
                };

            // --- ACTION EN UN CLIC sur un réglage du catalogue : « désactive la transparence »,
            //     « active le mode jeu », « remets les widgets »… Il faut un verbe d'action et un
            //     réglage qui ressort seul ; sinon on continue le routage normal. ---
            {
                CopiloteReglages.Choix choix = CopiloteReglages.Trouve(s, Catalogue.Value);
                if (choix != null) return ChatActions.ReglageReply(choix);
            }

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
                return new Reply
                {
                    Text = "🧭 Voilà TOUT ce que je sais faire — pose ta question, ou tape la commande :\n"
                         + "🎮 Jeu & perfs : « ça rame en jeu » · « fps bas » · « ça crash » · « écran bloqué à 60 » · « prépare ma partie »\n"
                         + "🖥 PC & Windows : « mes fenêtres saccadent » · « le pc chauffe » · « qui bouffe mon cpu » · « répare windows » · « plus de son / d'internet »\n"
                         + "🧰 Entretien : « bilan complet » · « bilan maj » · « libère de la place » · « désactive l'hibernation » · « nettoyage profond » · « état de mes disques » · « gardien » · « journal »\n"
                         + "📦 Profil : « exporte mon profil » · « importe mon profil » (réinstalle Windows sans rien perdre)\n"
                         + "🔧 Infos locales (hors-ligne) : heure · batterie · uptime · calculs (« 15% de 240 ») · unités (« 100 km en miles ») · phase de la lune\n"
                         + "🌍 Monde (gratuit, sans clé) : météo · qualité de l'air · actus · jeux gratuits PC · « c'est quoi X » (Wikipédia) · « définition de X » · distance entre 2 villes · devises · crypto · fériés · pokémon · série TV · prix Nobel · ISS · séismes · dette de la France · photo NASA · « traduis X en anglais » · mon IP · soleil · livres · code postal · avions au-dessus de moi\n"
                         + "Chaque mesure part toute seule ; chaque CHANGEMENT attend ton clic (et se journalise). Et quand je ne sais pas : je le DIS.",
                    ShowStarters = true
                };

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
                    Action = Investigator.Action(q.Trim(), st)
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
                        "Je fais le grand bilan stockage…", ChatActions.StorageAuditAction());
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
                    Action = Investigator.Action(q.Trim(), st)
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
                return new Reply { Text = "Pour « " + best.Symptom + " », le bon outil est « " + best.Tool + " ». Je l'ouvre ?", Tool = best };

            // Signal FAIBLE : plutôt que de balayer d'un « pas compris », on vérifie l'intention.
            if (best != null && bestScore == 1)
                return new Reply
                {
                    Text = "Tu veux dire « " + best.Symptom + " » ? Si oui, j'ouvre « " + best.Tool + " » — sinon reformule.",
                    Tool = best, ShowStarters = true
                };
            return new Reply
            {
                Text = "Pas sûr d'avoir bien compris 🤔. Reformule en quelques mots, ou choisis un souci courant :",
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

        /// <summary>Question HEURE / DATE ? → réponse locale exacte, hors-ligne.</summary>
        internal static bool IsTimeQuery(string s)
        {
            if (s != null && s.Contains("soleil")) return false;   // « à quelle heure se couche le soleil » → route soleil
            return Has(s, "quelle heure", "il est quelle heure", "quel heure", "heure actuelle", "heure qu'il est",
                          "quel jour on est", "quel jour sommes", "on est quel jour", "quelle date", "date du jour",
                          "date d'aujourd'hui", "on est le combien", "jour de la semaine", "on est quelle date");
        }

        /// <summary>Question MÉTÉO (temps réel + localisée) ? Tolère les fautes vues en test.</summary>
        internal static bool IsWeather(string s)
        {
            return Has(s, "quel temps fait", "quel temps il fait", "temps fait il", "temps fait-il",
                          "tempis fait", "temp fait", "quel tempis fait", "temps qu'il fait", "temps qu il fait",
                          "la meteo", "meteo", "il pleut", "va pleuvoir", "va t il pleuvoir", "il neige",
                          "il fait beau", "il fait moche", "fait il beau", "temps dehors");
        }

        /// <summary>L'utilisateur signale-t-il que la réponse était FAUSSE (feedback → auto-amélioration) ?</summary>
        internal static bool IsCorrection(string s)
        {
            return Has(s, "c'est faux", "cest faux", "c'est pas vrai", "c'est pas ca", "c'est pas ça",
                          "tu te trompes", "tu as tort", "t'as tort", "mauvaise reponse", "mauvaise réponse",
                          "reponse fausse", "c'est incorrect", "c'est inexact", "tu dis n'importe quoi",
                          "c'est errone", "c'est erroné", "c'est pas exact");
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
            return r;
        }


        // --- Lexique : notions gaming/PC expliquées simplement, outil lié tendu quand il existe.
        //     Format : { "alias1;alias2", "définition", "outil du catalogue ou null" }.
        private static readonly string[][] Lexicon =
        {
            new[] { "ddu", "DDU (Display Driver Uninstaller) : un outil GRATUIT qui désinstalle ton pilote graphique À FOND (restes compris) pour repartir sur une installation propre — le remède aux pilotes abîmés. Installable en 1 clic depuis les Bibliothèques.", "Bibliothèques de jeu" },
            new[] { "gigue;jitter", "La gigue (jitter), c'est la VARIATION du ping d'une seconde à l'autre. Un 30 ms stable se joue très bien ; un ping qui saute de 20 à 80 ms rend le jeu irrégulier même si la moyenne semble bonne. En jeu, la stabilité compte plus que la moyenne.", "Qualité réseau" },
            new[] { "fenetre;fenêtre;fenetres;fenêtres;saccade;saccades;saccadé;dwm;bureau qui rame;deplacer une fenetre;déplacer une fenêtre;bureau saccade;mpo;compositeur", "Une fenêtre qui saccade quand on la déplace, ce n'est presque jamais la carte graphique : c'est le compositeur de Windows (DWM) mis en difficulté. Les causes, dans l'ordre : des écrans à fréquences MIXTES (un 500 Hz à côté d'un 180 Hz), un écran VIRTUEL fantôme (Parsec, OBS…), le MPO, HAGS, la transparence, un pilote ancien. Le panneau « Fenêtres qui saccadent » les détecte et propose une correction à la fois — parce qu'en changer deux d'un coup empêche de savoir laquelle a agi.", "Fenêtres qui saccadent" },
            new[] { "operateur;opérateur;support;1064;3900;1023;3244;sav;reclamation;réclamation;mediateur;médiateur;dossier;preuve;antenne;saturation;heures de pointe", "Quand le PC est hors de cause, il reste à le PROUVER : l'Assistant opérateur mesure et HORODATE (journal), puis exporte un dossier .txt pour le support — mesures jour/soir, réseau local déjà écarté, questions précises à poser. Il liste aussi les réglages propres à chaque opérateur (Bouygues Box 5G, Orange, SFR, Free). Une app ne répare pas le réseau d'un opérateur : elle règle ce qui est réglable, et elle documente le reste.", "Assistant opérateur" },
            new[] { "5g;4g;box 5g;box 4g;box mobile;cgnat;mtu;bouygues;partage de connexion;fibre;adsl;vdsl;livebox;freebox;bbox;box sfr;ma box;bufferbloat", "Quel que soit le type de box (fibre, ADSL, 4G/5G), le panneau « Ma connexion & ma box » mesure TON lien réel : débit négocié du câble, ping vers la box vs vers Internet (pour savoir si c'est le câble, la box ou l'opérateur), bufferbloat en réception ET en envoi, MTU (corrigeable en 1 clic, réversible), CGNAT, IPv6 — puis il estime le type de lien et rend un verdict. Le schéma de branchement dessiné + la checklist par type de box sont inclus.", "Ma connexion & ma box" },
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
            // Le réglage lui-même dit « sans effet sur les jeux en UDP » ; l'assistant disait
            // « mauvais pour le jeu ». Deux réponses contraires dans la même app, et c'est la
            // vendeuse qui avait tort : Nagle ne concerne QUE TCP, et la quasi-totalité des jeux
            // font passer leur trafic temps réel en UDP, que Nagle ne touche pas.
            new[] { "nagle", "L'algorithme de Nagle regroupe les petits paquets TCP pour économiser de la bande passante, au prix d'un léger retard. Le couper aide les jeux qui passent en TCP (certains MMO), les lanceurs et le bureau à distance. Mais il n'a AUCUN effet sur la grande majorité des jeux, qui envoient leur trafic temps réel en UDP — Nagle ne s'y applique pas. Méfie-toi des guides qui le présentent comme un gain de ping universel. Le réglage existe dans l'app, réversible.", "Réglages TCP/IP" },
            new[] { "islc;standby list", "ISLC (Intelligent Standby List Cleaner) purge la « standby list » : un cache mémoire que Windows vide parfois trop tard, cause de micro-saccades sur certaines configs. Gratuit, installable en 1 clic depuis les Bibliothèques.", "Bibliothèques de jeu" },
            new[] { "markc", "Le « MarkC fix » est la méthode historique pour désactiver TOTALEMENT l'accélération de la souris (déplacement 1:1). L'optimisation souris de l'app fait l'équivalent proprement — et c'est réversible.", "Fréquence de la souris" },
            new[] { "sharpness;nettete;sharpen", "Le filtre de netteté NVIDIA (sharpen) redonne du piqué à l'image, utile avec DLSS/upscaling. L'app propose le réglage communautaire qui ramène l'ANCIEN filtre par jeu (EnableGR535), réversible, dans Optimisations → GPU.", null },
            new[] { "wub;update blocker", "Windows Update Blocker (Wub) coupe le service de mise à jour : plus AUCUN correctif, même de sécurité — le PC accumule des failles connues. L'app le détecte dans « Réglages néfastes » et le répare en un clic.", "Réglages néfastes" },
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

        // Compréhension du texte (normalisation, texto, fautes de frappe) : CopiloteTexte, testé.
        private static bool Has(string s, params string[] ks) { return CopiloteTexte.Has(s, ks); }
        private static string[] SplitWords(string s) { return CopiloteTexte.SplitWords(s); }
        private static bool FuzzyWord(string[] words, string k) { return CopiloteTexte.FuzzyWord(words, k); }
        private static bool FuzzyKey(string[] words, string key) { return CopiloteTexte.FuzzyKey(words, key); }
        private static int Lev(string a, string b, int max) { return CopiloteTexte.Lev(a, b, max); }
        private static bool IsYes(string s) { return CopiloteTexte.IsYes(s); }
        private static bool IsNo(string s) { return CopiloteTexte.IsNo(s); }
        private static string Norm(string x) { return CopiloteTexte.Norm(x); }
    }
}
