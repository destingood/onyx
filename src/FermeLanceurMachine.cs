using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace BTOptimizer
{
    /// <summary>Partie machine de <see cref="FermeLanceur"/> : réglage, processus Steam, commande de fermeture.</summary>
    internal static partial class FermeLanceur
    {
        private static readonly object _verrou = new object();
        private static Lancement _lancement;
        private static bool? _actif;

        private static string Chemin { get { return AppPaths.File("bt-fermelanceur.txt"); } }

        /// <summary>Message pour l'utilisateur après une fermeture (fil de fond).</summary>
        public static event Action<string> Ferme;

        /// <summary>Coupé par défaut ; persistant d'un lancement à l'autre.</summary>
        public static bool Actif
        {
            get
            {
                if (_actif.HasValue) return _actif.Value;
                try { _actif = File.Exists(Chemin) && File.ReadAllText(Chemin).Trim() == "1"; }
                catch (Exception ex) { JournalTechnique.Echec("FermeLanceur.Actif", ex); _actif = false; }
                return _actif.Value;
            }
            set
            {
                _actif = value;
                try { File.WriteAllText(Chemin, value ? "1" : "0"); }
                catch (Exception ex) { JournalTechnique.Echec("FermeLanceur.Actif=", ex); }
            }
        }

        private static bool SteamTourne()
        {
            Process[] p = null;
            try { p = Process.GetProcessesByName("steam"); return p.Length > 0; }
            catch { return true; }   // dans le doute : Steam ouvert par l'utilisateur, on n'y touche pas
            finally { if (p != null) foreach (Process x in p) x.Dispose(); }
        }

        /// <summary>Appelé juste avant de lancer un jeu Steam depuis ONYX.</summary>
        public static void NoteLancement(string jeu)
        {
            var l = new Lancement { Jeu = jeu, Quand = DateTime.Now, SteamTournait = SteamTourne() };
            lock (_verrou) _lancement = l;
        }

        /// <summary>Abonné à <see cref="JournalJeu.PartieClose"/> (fil de fond).</summary>
        public static void PartieClose(JournalJeu.Partie p)
        {
            if (p == null || !Actif) return;
            Lancement l;
            lock (_verrou)
            {
                l = _lancement;
                if (!Concerne(l, p.Jeu, DateTime.Now)) return;
                _lancement = null;
            }
            Task.Run(async () =>
            {
                await Task.Delay(Delai).ConfigureAwait(false);
                string m = FermeSteam();
                var h = Ferme;
                if (m != null && h != null) try { h(m); } catch { }
            });
        }

        private static string FermeSteam()
        {
            if (!Actif) return null;
            if (JournalJeu.JeuEnCours != null) return null;   // un autre jeu a démarré entre-temps
            if (!SteamTourne()) return null;
            string dossier = SteamGames.SteamPath();
            string exe = dossier != null ? Path.Combine(dossier, "steam.exe") : null;
            if (exe == null || !File.Exists(exe)) return "Steam non fermé : steam.exe introuvable.";
            try
            {
                using (Process.Start(new ProcessStartInfo(exe, "-shutdown") { UseShellExecute = false, CreateNoWindow = true })) { }
                return "Partie terminée : Steam se ferme (comme « Steam > Quitter »).";
            }
            catch (Exception ex) { JournalTechnique.Echec("FermeLanceur.FermeSteam", ex); return "Steam non fermé : " + ex.Message; }
        }
    }
}
