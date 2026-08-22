using System;
using System.Collections.Generic;

namespace BTOptimizer
{
    /// <summary>
    /// Mode Jeu : un interrupteur temporaire qui nettoie la RAM, suspend (arrête, sans
    /// désactiver) des services de fond non essentiels et force le timer 1 ms.
    /// « Désactiver » restaure exactement l'état précédent (services relancés, timer rendu).
    /// </summary>
    internal static class GameBoost
    {
        public static bool IsActive { get; private set; }

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
        /// suites constructeur qui interrogent les capteurs (Corsair, Logitech, NVIDIA…).
        ///
        /// Ces derniers sont la première cause de latence différée sur une machine bien réglée :
        /// lire une température passe par un bus lent et BLOQUANT. Le Mode Jeu les arrête le temps
        /// d'une partie et les relance ensuite — exactement le traitement des autres, et rien n'est
        /// désactivé durablement. Les APPLICATIONS visibles (iCUE, Afterburner…) ne sont PAS
        /// touchées : les arrêter aurait des effets que l'utilisateur n'a pas demandés.
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
                // Un nom détecté ici n'est pas dans le socle : le dire évite de faire passer un
                // service que l'utilisateur a lui-même installé pour un service de Windows.
                default: return Decouverts().Contains(svc) ? svc + " (détecté sur cette machine)" : svc;
            }
        }

        private static string ExclPath { get { return AppPaths.File("bt-gamemode-excl.txt"); } }

        /// <summary>Services EXCLUS du Mode Jeu (laissés tourner) — choix persisté de l'utilisateur.</summary>
        public static HashSet<string> LoadExclusions()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { if (System.IO.File.Exists(ExclPath)) foreach (string l in System.IO.File.ReadAllLines(ExclPath)) { string s = l.Trim(); if (s.Length > 0) set.Add(s); } }
            catch { }
            return set;
        }

        public static void SaveExclusions(IEnumerable<string> excluded)
        {
            try { System.IO.File.WriteAllLines(ExclPath, new List<string>(excluded)); } catch { }
        }

        public static void Activate(Action<string, int> log)
        {
            if (IsActive) return;

            _timerWasActive = Native.TimerActive;
            Native.SetTimer1ms(true);

            long freed = Sys.CleanMemory(log);

            _stopped.Clear();
            HashSet<string> excl = LoadExclusions();
            foreach (string svc in Suspendable)
            {
                if (excl.Contains(svc)) continue;   // exclu par l'utilisateur : laissé tourner
                // Chaque service isolé : un service récalcitrant ne doit NI faire échouer le mode
                // jeu, NI laisser les autres à moitié suspendus. Un service n'est enregistré dans
                // _stopped que si son arrêt a réussi (sinon on n'essaiera pas de le relancer).
                try
                {
                    int start = Sys.GetServiceStart(svc);
                    if (start < 0) continue;                   // absent
                    if (start == 4) continue;                  // déjà désactivé (on n'y touche pas)
                    if (!Sys.IsServiceRunning(svc)) continue;  // déjà arrêté
                    Sys.StopService(svc);
                    _stopped.Add(svc);
                }
                catch { }
            }

            IsActive = true;
            try { BadgeStore.MarkBoostUsed(); BadgeCatalog.EvaluateEvents(); } catch { }   // badge « Mode Jeu » (+ toast)
            log("Mode Jeu ACTIVÉ : timer 1 ms, ~" + Math.Max(0, freed) + " Mo RAM libérés, "
                + _stopped.Count + " service(s) de fond suspendu(s).", 1);
        }

        public static void Deactivate(Action<string, int> log)
        {
            if (!IsActive) return;

            // Restauration ROBUSTE : un service qui refuse de redémarrer ne doit pas empêcher de
            // relancer les autres ni de rendre le timer. On sort TOUJOURS de l'état « mode jeu ».
            int restored = 0;
            foreach (string svc in _stopped)
            {
                try { Sys.StartService(svc); restored++; }
                catch { }
            }
            _stopped.Clear();

            try { if (!_timerWasActive) Native.SetTimer1ms(false); } catch { }

            IsActive = false;
            log("Mode Jeu désactivé : " + restored + " service(s) relancé(s), timer rendu au système.", 0);
        }
    }
}
