using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// RAPPEL HEBDOMADAIRE — branchement : 5 minutes après le démarrage, si la case est cochée,
    /// si l'analyse est due et si aucun jeu ne tourne, JunkScan sur un fil de fond puis une
    /// notification. Reporté minute par minute pendant une partie. Rien n'est supprimé.
    /// </summary>
    internal static partial class RappelNettoyage
    {
        public const string Fichier = "bt-rappel-nettoyage.txt";
        private static readonly TimeSpan PremierDelai = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan Ensuite = TimeSpan.FromHours(6);

        private static DashboardForm _host;
        private static Timer _minuteur;
        private static DateTime _prochainUtc;
        private static volatile bool _enCours;

        public static Etat Charge()
        {
            try { string p = AppPaths.File(Fichier); return Lit(File.Exists(p) ? File.ReadAllText(p) : null); }
            catch { return new Etat(); }
        }

        public static void Sauve(Etat e)
        {
            try { File.WriteAllText(AppPaths.File(Fichier), Ecrit(e), new System.Text.UTF8Encoding(false)); } catch { }
        }

        /// <summary>Coche ou décoche la case sans toucher à la date de la dernière analyse.</summary>
        public static void Active(bool oui)
        {
            Etat e = Charge();
            if (e.Active == oui) return;
            e.Active = oui;
            Sauve(e);
        }

        /// <summary>Appelé une fois au démarrage, sur le thread d'interface.</summary>
        public static void Demarre(DashboardForm host)
        {
            if (_minuteur != null || host == null) return;
            _host = host;
            _prochainUtc = DateTime.UtcNow + PremierDelai;
            _minuteur = new Timer { Interval = 60 * 1000 };
            _minuteur.Tick += (s, e) => Tick();
            _minuteur.Start();
        }

        private static void Tick()
        {
            try
            {
                if (_enCours || DateTime.UtcNow < _prochainUtc) return;
                Etat e = Charge();
                if (!e.Active || !NotifReglages.Active(NotifCategories.Nettoyage) || !Du(e.Derniere, DateTime.Now)) { _prochainUtc = DateTime.UtcNow + Ensuite; return; }
                // Reporté (reste dû) tant qu'un jeu tourne ou que le Mode Jeu est actif.
                if (GameBoost.IsActive || Native.IsGameFullscreen() || GameScan.RunningKnownGame() != null) return;
                _enCours = true;
                Task.Run(() =>
                {
                    try
                    {
                        var items = JunkScan.Scan(JunkScan.DefaultMinBytes, 20000, (m, n) => { });
                        StorageSettings reglages = StorageSettings.Load();
                        items.RemoveAll(i => reglages.Exclu(i.Path));   // dossiers exclus du nettoyage
                        Etat f = Charge();
                        f.Derniere = DateTime.Now;
                        Sauve(f);
                        string texte = Texte(JunkScan.Total(items), items.Count);
                        if (texte != null && _host != null && !_host.IsDisposed) _host.Notifie("ONYX — Poids mort", texte);
                    }
                    catch { }
                    finally { _prochainUtc = DateTime.UtcNow + Ensuite; _enCours = false; }
                });
            }
            catch { }
        }
    }
}
