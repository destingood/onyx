using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// Mode Jeu : un interrupteur temporaire qui suspend (arrête, sans désactiver) les services de
    /// fond non essentiels, met en mode Efficacité (priorité Idle + EcoQoS, comme le Gestionnaire
    /// des tâches) les processus des applications qui n'ont rien à faire là pendant une partie
    /// (voir ApplisDeFond — AUCUN n'est fermé), nettoie la RAM et force le timer 1 ms.
    ///
    /// « Désactiver » rétablit TOUT : les services sont relancés, chaque processus retrouve sa
    /// priorité et son état EcoQoS d'origine, le timer est rendu.
    ///
    /// UN MODE JEU QUI MEURT NE DOIT PAS LAISSER LA MACHINE AMPUTÉE. Les services suspendus
    /// (bt-gamemode-etat.txt) et l'état d'origine des processus (bt-gamemode-eco.txt) sont écrits
    /// sur le disque AVANT d'être modifiés : si ONYX est tué en cours de partie, le lancement
    /// suivant relit ces marqueurs et remet les choses en place (Soigne). Sans ça, un plantage
    /// laissait l'indexation et le spouleur arrêtés jusqu'au prochain redémarrage — sans que rien
    /// ne le signale.
    /// </summary>
    internal static class GameBoost
    {
        public static bool IsActive { get; private set; }

        private static readonly object _verrou = new object();
        private static readonly List<string> _stopped = new List<string>();
        private static bool _timerWasActive;
        private static readonly List<Efficacite.Entree> _eco = new List<Efficacite.Entree>();   // état d'origine des processus mis en Efficacité

        // Services sûrs à suspendre pendant une partie (tous relançables à la demande).
        private static readonly string[] Socle = Construit();

        /// <summary>
        /// Le socle écrit en dur, PLUS ce que l'inventaire a trouvé sur CETTE machine.
        ///
        /// Le socle ne connaît que sept noms Windows et une poignée de sondes. Il ignore par
        /// construction les trente à soixante services que les logiciels tiers installent ici —
        /// justement ceux qui tournent pendant la partie. <see cref="InventaireServices"/> les
        /// énumère ; « Détecter » les ajoute, une fois, et le choix reste dans un fichier que
        /// l'utilisateur peut vider.
        /// </summary>
        private static string[] Suspendable
        {
            get
            {
                var l = new List<string>(Socle);
                foreach (string s in Decouverts()) if (!l.Contains(s)) l.Add(s);
                return l.ToArray();
            }
        }

        private static string CheminDecouverts { get { return AppPaths.File("bt-gamemode-tiers.txt"); } }

        /// <summary>Services tiers retenus par la détection, sur cette machine.</summary>
        public static List<string> Decouverts()
        {
            var l = new List<string>();
            try
            {
                if (!System.IO.File.Exists(CheminDecouverts)) return l;
                foreach (string ligne in System.IO.File.ReadAllLines(CheminDecouverts))
                {
                    string s = ligne.Trim();
                    if (s.Length > 0 && !l.Contains(s)) l.Add(s);
                }
            }
            catch (Exception ex) { JournalTechnique.Echec("GameBoost.Decouverts", ex); }
            return l;
        }

        /// <summary>
        /// Cherche sur cette machine les services TIERS que le Mode Jeu peut suspendre sans risque,
        /// et les retient. Renvoie le nombre de nouveaux noms.
        ///
        /// Ne retient QUE ce que l'inventaire classe « suspendable » : ni vital, ni anticheat, ni
        /// tiers inconnu. Un service qu'ONYX n'a pas su identifier n'entre pas dans une liste qui
        /// s'appliquera ensuite automatiquement à chaque lancement de jeu.
        /// </summary>
        public static int Decouvre()
        {
            int neufs = 0;
            try
            {
                // Le garde-fou d'abord : un service qu'ONYX répare à chaque lancement n'a rien à
                // faire dans une liste qui l'arrêterait à chaque partie.
                ActionsServices.Amorce();

                List<string> deja = Decouverts();
                var ajouts = new List<string>();
                foreach (InventaireServices.Service s in InventaireServices.Analyse(0))
                {
                    if (s.Verdict != InventaireServices.Verdict.Suspendable) continue;
                    if (!InventaireServices.EstTiers(s.Chemin)) continue;          // le socle couvre déjà Windows
                    if (InventaireServices.EstVital(s.Nom, s.Libelle, s.Chemin)) continue;
                    if (deja.Contains(s.Nom) || Array.IndexOf(Socle, s.Nom) >= 0) continue;
                    ajouts.Add(s.Nom);
                }
                if (ajouts.Count > 0)
                {
                    deja.AddRange(ajouts);
                    System.IO.File.WriteAllLines(CheminDecouverts, deja.ToArray());
                    neufs = ajouts.Count;
                }
            }
            catch (Exception ex) { JournalTechnique.Echec("GameBoost.Decouvre", ex); }
            return neufs;
        }

        /// <summary>
        /// Services suspendables = les services Windows d'arrière-plan, PLUS les services des
        /// suites constructeur qui interrogent les capteurs (Corsair, Logitech…).
        ///
        /// Ces derniers sont la première cause de latence différée sur une machine bien réglée :
        /// lire une température passe par un bus lent et BLOQUANT. Le Mode Jeu les arrête le temps
        /// d'une partie et les relance ensuite — exactement le traitement des autres, et rien n'est
        /// désactivé durablement.
        ///
        /// Les services des applications (IA, dev, Razer…) ne sont PAS ici : ils sont décrits par
        /// catégorie dans ApplisDeFond, avec leurs processus, et l'utilisateur les gouverne famille
        /// par famille.
        /// </summary>
        private static string[] Construit()
        {
            var l = new List<string> { "SysMain", "WSearch", "Spooler", "DiagTrack", "WMPNetworkSvc", "MapsBroker", "dmwappushservice" };
            foreach (string s in SondesMaterielles.ServicesSondes) if (!l.Contains(s)) l.Add(s);
            return l.ToArray();
        }

        /// <summary>Liste des services que le Mode Jeu peut suspendre (pour l'écran d'exclusions).</summary>
        public static IReadOnlyList<string> SuspendableServices { get { return Suspendable; } }

        /// <summary>Alias tableau (BoostConfigForm) des services suspendables.</summary>
        public static string[] AffectedServices { get { return (string[])Suspendable.Clone(); } }

        /// <summary>Libellé lisible d'un service suspendable.</summary>
        public static string FriendlyName(string svc)
        {
            switch (svc)
            {
                case "SysMain": return "SysMain (Superfetch — préchargement)";
                case "WSearch": return "Windows Search (indexation des fichiers)";
                case "Spooler": return "Spouleur d'impression";
                case "DiagTrack": return "Télémétrie / diagnostics (DiagTrack)";
                case "WMPNetworkSvc": return "Partage réseau Windows Media";
                case "MapsBroker": return "Cartes hors ligne (MapsBroker)";
                case "dmwappushservice": return "WAP Push (télémétrie)";
                case "CorsairCpuIdService": return "Corsair iCUE — relevés processeur";
                case "CorsairGamingAudioConfig": return "Corsair — configuration audio";
                case "LGHUBUpdaterService": return "Logitech G HUB — mises à jour";
                case "MSIAfterburnerService": return "MSI Afterburner — service de capteurs";
                // Un nom qui arrive ici n'est dans aucune table : c'est donc une détection faite
                // sur CETTE machine. Le dire évite de faire passer un service que l'utilisateur a
                // lui-même installé pour un service de Windows.
                default: return Decouverts().Contains(svc) ? svc + " (détecté sur cette machine)" : svc;
            }
        }

        private static string ExclPath { get { return AppPaths.File("bt-gamemode-excl.txt"); } }

        // Marqueur de reprise : la liste des services que NOUS avons arrêtés, sur le disque.
        private static string MarqueurPath { get { return AppPaths.File("bt-gamemode-etat.txt"); } }

        // Journal de rétablissement des processus : pid|début|priorité|contrôle|état|nom (voir Efficacite).
        private static string EcoPath { get { return AppPaths.File("bt-gamemode-eco.txt"); } }

        /// <summary>Services EXCLUS du Mode Jeu (laissés tourner) — choix persisté de l'utilisateur.</summary>
        public static HashSet<string> LoadExclusions()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { if (File.Exists(ExclPath)) foreach (string l in File.ReadAllLines(ExclPath)) { string s = l.Trim(); if (s.Length > 0) set.Add(s); } }
            catch { }
            return set;
        }

        public static void SaveExclusions(IEnumerable<string> excluded)
        {
            try { File.WriteAllLines(ExclPath, new List<string>(excluded)); } catch { }
        }

        public static void Activate(Action<string, int> log) { Activate(log, null); }

        /// <summary>
        /// Active le Mode Jeu. <paramref name="jeuProtege"/> = nom du processus du jeu qui a
        /// déclenché le mode automatique : il ne sera ni mis en Efficacité, ni vidé de sa mémoire.
        /// Sans lui, lancer Roblox reviendrait à ralentir Roblox.
        /// </summary>
        public static void Activate(Action<string, int> log, string jeuProtege) { Activate(log, jeuProtege, null); }

        /// <summary>
        /// Idem, avec des processus EN PLUS des catégories actives (le repérage avant-partie du
        /// Copilote). Ils passent eux aussi en Efficacité, notés au même journal : la sortie du
        /// Mode Jeu les rétablit comme les autres, même après un plantage.
        /// </summary>
        public static void Activate(Action<string, int> log, string jeuProtege, IEnumerable<string> processusEnPlus)
        {
            lock (_verrou)   // deux clics rapides, le raccourci et le mode auto peuvent tomber ensemble
            {
                if (IsActive) return;

                _timerWasActive = Native.TimerActive;
                Native.SetTimer1ms(true);

                // --- 1. Services de fond ---------------------------------------------------
                _stopped.Clear();
                HashSet<string> excl = LoadExclusions();
                foreach (string svc in Suspendable)
                {
                    if (excl.Contains(svc)) continue;   // exclu par l'utilisateur : laissé tourner
                    // Chaque service isolé : un service récalcitrant ne doit NI faire échouer le mode
                    // jeu, NI laisser les autres à moitié suspendus.
                    try
                    {
                        int start = Sys.GetServiceStart(svc);
                        if (start < 0) continue;                   // absent
                        if (start == 4) continue;                  // déjà désactivé (on n'y touche pas)
                        if (!Sys.IsServiceRunning(svc)) continue;  // déjà arrêté
                        Suspendre(svc);
                    }
                    catch { }
                }

                // --- 2. Applications de fond -----------------------------------------------
                // Rien n'est fermé : les processus passent en mode Efficacité, leur état d'origine est
                // noté sur le disque AVANT chaque changement (comme le marqueur des services).
                HashSet<int> proteges = ApplisDeFond.PidsProteges(jeuProtege);
                ApplisDeFond.Resultat res = null;
                var tentes = new List<string>();
                // Un journal laissé par un plantage est REPRIS, pas écrasé : ces processus sont encore en
                // Efficacité, la passe suivante les verra « déjà en Efficacité » et ne les noterait plus.
                _eco.Clear();
                try { if (File.Exists(EcoPath)) _eco.AddRange(Efficacite.Lit(File.ReadAllText(EcoPath))); } catch { }
                try
                {
                    // Le marqueur est écrit AVANT chaque arrêt (Retenir), pas après : c'est la seule
                    // façon qu'un plantage entre les deux ne laisse pas un service orphelin.
                    List<ApplisDeFond.Categorie> cats = ApplisDeFond.Actives();
                    var enPlus = new List<string>();
                    if (processusEnPlus != null)
                        foreach (string n in processusEnPlus)
                            if (!string.IsNullOrEmpty(n) && !ApplisDeFond.EstVital(n) && !enPlus.Contains(n)) enPlus.Add(n);
                    if (enPlus.Count > 0)
                        cats.Add(new ApplisDeFond.Categorie { Cle = "avant-partie", Libelle = "Repérage avant-partie", Processus = enPlus.ToArray() });
                    res = ApplisDeFond.MettreEnEfficacite(cats, proteges, log,
                        delegate(Efficacite.Entree e)
                        {
                            _eco.Add(e);
                            if (EcrireEco()) return true;
                            _eco.Remove(e);   // pas noté sur le disque : le processus ne sera pas touché
                            return false;
                        },
                        delegate(string svc) { tentes.Add(svc); Retenir(svc); });
                    // Journalisés puis refusés par Windows : rien n'a changé, ils sortent du journal.
                    if (res.Annules.Count > 0)
                    {
                        foreach (Efficacite.Entree e in res.Annules) _eco.Remove(e);
                        EcrireEco();
                    }
                    // Ce qui a été refusé (droits, dépendances) sort du marqueur : on ne s'attribue
                    // pas l'arrêt d'un service qui tourne toujours.
                    foreach (string svc in tentes)
                        if (!res.ServicesArretes.Contains(svc)) Oublier(svc);
                }
                catch (Exception ex)
                {
                    if (log != null) log("Applications de fond : " + ex.Message, 2);
                }

                // --- 3. Mémoire, en épargnant le jeu ---------------------------------------
                // Isolé : une exception ici laisserait IsActive à faux, et Deactivate ne rétablirait
                // plus les processus déjà mis en Efficacité.
                long freed = 0;
                try { freed = Sys.CleanMemory(log, proteges); } catch { }

                IsActive = true;
                try { BadgeStore.MarkBoostUsed(); BadgeCatalog.EvaluateEvents(); } catch { }   // badge « Mode Jeu » (+ toast)

                if (log != null)
                {
                    log("Mode Jeu ACTIVÉ : timer 1 ms, ~" + Math.Max(0, freed) + " Mo RAM libérés, "
                        + _stopped.Count + " service(s) de fond suspendu(s).", 1);
                    if (res != null)
                        log(Efficacite.Texte(res.Mis, res.Deja, res.Refus, res.ServicesArretes.Count), res.Refus > 0 ? 2 : 1);
                }
            }
        }

        /// <summary>Arrête un service ET l'inscrit au marqueur — dans cet ordre inverse : le
        /// marqueur d'abord. Si ONYX meurt entre les deux, on relancera au prochain lancement un
        /// service qui tournait déjà : sans effet. L'inverse laisserait un service arrêté que plus
        /// personne ne connaît.</summary>
        private static void Suspendre(string svc)
        {
            Retenir(svc);
            if (!Sys.StopService(svc)) Oublier(svc);   // refusé : on ne s'en attribue pas le mérite
        }

        private static void Retenir(string svc)
        {
            if (!_stopped.Contains(svc)) _stopped.Add(svc);
            EcrireMarqueur();
        }

        private static void Oublier(string svc)
        {
            _stopped.Remove(svc);
            EcrireMarqueur();
        }

        /// <summary>Réécrit le journal des processus. Écriture dans un fichier temporaire puis
        /// remplacement : un plantage en pleine écriture laisse l'ancien journal entier, jamais un
        /// journal tronqué. Rend false si le disque a refusé.</summary>
        private static bool EcrireEco()
        {
            try
            {
                if (_eco.Count == 0) { if (File.Exists(EcoPath)) File.Delete(EcoPath); return true; }
                string tmp = EcoPath + ".tmp";
                File.WriteAllText(tmp, Efficacite.Ecrit(_eco));
                File.Move(tmp, EcoPath, true);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Remet chaque processus noté à sa priorité et à son EcoQoS d'origine. Un processus
        /// disparu est ignoré, et un PID réutilisé aussi (l'heure de début ne correspond plus).
        /// Rend le nombre de processus rétablis.</summary>
        private static int Retablit(IEnumerable<Efficacite.Entree> entrees)
        {
            int n = 0;
            if (entrees == null) return 0;
            foreach (Efficacite.Entree e in entrees)
            {
                try
                {
                    if (e == null) continue;
                    using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById(e.Pid))
                    {
                        if (!Efficacite.MemeProcessus(e, p.Id, p.StartTime.ToUniversalTime().Ticks)) continue;
                        bool ok = false;
                        try { p.PriorityClass = (System.Diagnostics.ProcessPriorityClass)e.Priorite; ok = true; } catch { }
                        if (Native.EcritEco(e.Pid, e.Controle, e.Etat)) ok = true;
                        if (ok) n++;
                    }
                }
                catch { }
            }
            return n;
        }

        private static void EcrireMarqueur()
        {
            try
            {
                if (_stopped.Count == 0) { if (File.Exists(MarqueurPath)) File.Delete(MarqueurPath); return; }
                File.WriteAllLines(MarqueurPath, _stopped.ToArray());
            }
            catch { }
        }

        public static void Deactivate(Action<string, int> log)
        {
            lock (_verrou)
            {
                if (!IsActive) return;

                // Restauration ROBUSTE : un service qui refuse de redémarrer ne doit pas empêcher de
                // relancer les autres ni de rendre le timer. On sort TOUJOURS de l'état « mode jeu ».
                int restored = 0, rates = 0;
                foreach (string svc in _stopped)
                {
                    try { if (Sys.StartService(svc)) restored++; else rates++; }
                    catch { rates++; }
                }
                _stopped.Clear();
                try { if (File.Exists(MarqueurPath)) File.Delete(MarqueurPath); } catch { }

                int rendus = Retablit(_eco);
                _eco.Clear();
                try { if (File.Exists(EcoPath)) File.Delete(EcoPath); } catch { }

                try { if (!_timerWasActive) Native.SetTimer1ms(false); } catch { }

                IsActive = false;
                if (log != null)
                    log("Mode Jeu désactivé : " + restored + " service(s) relancé(s)"
                        + (rates > 0 ? ", " + rates + " n'ont pas redémarré (ils repartiront à la demande)" : "")
                        + ", " + rendus + " processus rendus à leur priorité d'origine"
                        + ", timer rendu au système.", rates > 0 ? 2 : 0);
            }
        }

        /// <summary>
        /// AU LANCEMENT : si un marqueur traîne, c'est qu'ONYX a été fermé sans sortir du Mode Jeu.
        /// On relance les services suspendus et on rend leur priorité d'origine aux processus mis en
        /// Efficacité. Rend le nombre de services relancés PLUS le nombre de processus rétablis.
        /// Comme WifiScan, il ne touche JAMAIS un service ni un processus qu'il n'a pas lui-même modifié.
        /// </summary>
        public static int Soigne(Action<string, int> log)
        {
            if (IsActive) return 0;
            int ok = 0, rendus = 0;

            // --- Services
            var noms = new List<string>();
            try
            {
                if (File.Exists(MarqueurPath))
                    foreach (string l in File.ReadAllLines(MarqueurPath))
                    { string s = l.Trim(); if (s.Length > 0 && !noms.Contains(s)) noms.Add(s); }
            }
            catch { }
            foreach (string svc in noms)
            {
                try
                {
                    if (Sys.GetServiceStart(svc) == 4) continue;   // désactivé depuis : ce n'est plus notre affaire
                    if (Sys.IsServiceRunning(svc)) continue;       // déjà reparti tout seul
                    if (Sys.StartService(svc)) ok++;
                }
                catch { }
            }
            try { if (File.Exists(MarqueurPath)) File.Delete(MarqueurPath); } catch { }

            // --- Processus mis en Efficacité
            try
            {
                if (File.Exists(EcoPath))
                    rendus = Retablit(Efficacite.Lit(File.ReadAllText(EcoPath)));
            }
            catch { }
            try { if (File.Exists(EcoPath)) File.Delete(EcoPath); } catch { }

            if (log != null)
            {
                if (ok > 0)
                    log("Mode Jeu interrompu la dernière fois : " + ok + " service(s) de fond relancé(s).", 1);
                if (rendus > 0)
                    log("Mode Jeu interrompu la dernière fois : " + rendus + " processus rendus à leur priorité d'origine.", 1);
            }
            return ok + rendus;
        }
    }
}
