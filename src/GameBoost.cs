using System;
using System.Collections.Generic;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// Mode Jeu : un interrupteur temporaire qui suspend (arrête, sans désactiver) les services de
    /// fond non essentiels, FERME LES APPLICATIONS qui n'ont rien à faire là pendant une partie
    /// (voir ApplisDeFond), nettoie la RAM et force le timer 1 ms.
    ///
    /// « Désactiver » restaure l'état des SERVICES et rend le timer. Les applications, elles, ne
    /// sont pas relancées : les rouvrir vides de leurs fichiers et de leurs conversations ne
    /// rendrait rien à personne.
    ///
    /// UN MODE JEU QUI MEURT NE DOIT PAS LAISSER LA MACHINE AMPUTÉE. Les services suspendus sont
    /// écrits dans un marqueur sur le disque AVANT d'être arrêtés : si ONYX est tué en cours de
    /// partie, le lancement suivant lit ce marqueur et relance ce qui traînait (Soigne). Sans ça,
    /// un plantage laissait l'indexation et le spouleur arrêtés jusqu'au prochain redémarrage —
    /// sans que rien ne le signale.
    /// </summary>
    internal static class GameBoost
    {
        public static bool IsActive { get; private set; }

        private static readonly object _verrou = new object();
        private static readonly List<string> _stopped = new List<string>();
        private static bool _timerWasActive;

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
        /// déclenché le mode automatique : il ne sera ni fermé, ni vidé de sa mémoire. Sans lui,
        /// lancer Roblox reviendrait à faire fermer Roblox par ONYX.
        /// </summary>
        public static void Activate(Action<string, int> log, string jeuProtege)
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
                // AVANT le nettoyage mémoire : fermer trois gigaoctets d'éditeur puis mesurer, c'est
                // annoncer un gain réel. L'inverse annonçait un chiffre qui ne comptait rien de tout ça.
                HashSet<int> proteges = ApplisDeFond.PidsProteges(jeuProtege);
                ApplisDeFond.Bilan bilan = null;
                var tentes = new List<string>();
                try
                {
                    // Le marqueur est écrit AVANT chaque arrêt (Retenir), pas après : c'est la seule
                    // façon qu'un plantage entre les deux ne laisse pas un service orphelin.
                    bilan = ApplisDeFond.Fermer(ApplisDeFond.Actives(), proteges, log,
                        delegate(string svc) { tentes.Add(svc); Retenir(svc); });
                    // Ce qui a été refusé (droits, dépendances) sort du marqueur : on ne s'attribue
                    // pas l'arrêt d'un service qui tourne toujours.
                    foreach (string svc in tentes)
                        if (!bilan.ServicesArretes.Contains(svc)) Oublier(svc);
                }
                catch (Exception ex)
                {
                    if (log != null) log("Applications de fond : " + ex.Message, 2);
                }

                // --- 3. Mémoire, en épargnant le jeu ---------------------------------------
                long freed = Sys.CleanMemory(log, proteges);

                IsActive = true;
                try { BadgeStore.MarkBoostUsed(); BadgeCatalog.EvaluateEvents(); } catch { }   // badge « Mode Jeu » (+ toast)

                if (log != null)
                {
                    log("Mode Jeu ACTIVÉ : timer 1 ms, ~" + Math.Max(0, freed) + " Mo RAM libérés, "
                        + _stopped.Count + " service(s) de fond suspendu(s).", 1);
                    if (bilan != null) log(ApplisDeFond.Texte(bilan), bilan.TousLesRefus().Count > 0 ? 2 : 1);
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

                try { if (!_timerWasActive) Native.SetTimer1ms(false); } catch { }

                IsActive = false;
                if (log != null)
                    log("Mode Jeu désactivé : " + restored + " service(s) relancé(s)"
                        + (rates > 0 ? ", " + rates + " n'ont pas redémarré (ils repartiront à la demande)" : "")
                        + ", timer rendu au système.", rates > 0 ? 2 : 0);
            }
        }

        /// <summary>
        /// AU LANCEMENT : si un marqueur traîne, c'est qu'ONYX a été fermé sans sortir du Mode Jeu.
        /// On relance ce qui avait été suspendu. Rend le nombre de services réparés.
        /// Comme WifiScan, il ne touche JAMAIS un service qu'il n'a pas lui-même arrêté.
        /// </summary>
        public static int Soigne(Action<string, int> log)
        {
            if (IsActive) return 0;
            var noms = new List<string>();
            try
            {
                if (!File.Exists(MarqueurPath)) return 0;
                foreach (string l in File.ReadAllLines(MarqueurPath))
                { string s = l.Trim(); if (s.Length > 0 && !noms.Contains(s)) noms.Add(s); }
            }
            catch { return 0; }

            int ok = 0;
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
            try { File.Delete(MarqueurPath); } catch { }

            if (ok > 0 && log != null)
                log("Mode Jeu interrompu la dernière fois : " + ok + " service(s) de fond relancé(s).", 1);
            return ok;
        }
    }
}
