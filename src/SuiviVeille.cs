using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Jeux suivis et favoris, sur disque (dossier de données d'ONYX). Chargés à la première
    /// lecture ; lus et modifiés sur le thread d'interface seulement. Chaque sauvegarde passe par
    /// un fichier temporaire puis un remplacement : une coupure ne laisse jamais un fichier à moitié écrit.
    /// </summary>
    internal static class Magasin
    {
        private const string FichierSuivis = "bt-suivis.txt", FichierJeux = "bt-favoris-jeux.txt", FichierActus = "bt-favoris-actus.txt", FichierBoutiques = "bt-boutiques.txt", FichierMasques = "bt-bons-plans-masques.txt";
        private static List<JeuSuivi> _suivis;
        private static List<Favori> _favorisJeux, _favorisActus;
        private static HashSet<string> _boutiques;
        private static List<OffresMasquees.Masque> _masques;

        /// <summary>Jeux masqués des bons plans (« J'ai déjà ce jeu » / « Pas pour moi »).</summary>
        public static List<OffresMasquees.Masque> Masques { get { return _masques ?? (_masques = OffresMasquees.Lire(Lit(FichierMasques))); } }
        public static void SauveMasques() { Ecrit(FichierMasques, OffresMasquees.Ecrire(Masques)); }

        /// <summary>Levé après chaque sauvegarde (thread d'interface).</summary>
        public static event Action Change;

        public static List<JeuSuivi> Suivis { get { return _suivis ?? (_suivis = Suivi.Lire(Lit(FichierSuivis))); } }
        public static List<Favori> FavorisJeux { get { return _favorisJeux ?? (_favorisJeux = Favoris.Lire(Lit(FichierJeux))); } }
        public static List<Favori> FavorisActus { get { return _favorisActus ?? (_favorisActus = Favoris.Lire(Lit(FichierActus))); } }

        /// <summary>Stores cochés dans « Stores » (Bons plans) ; tous par défaut.</summary>
        public static HashSet<string> Boutiques { get { return _boutiques ?? (_boutiques = OffresSources.LitChoixBoutiques(Lit(FichierBoutiques))); } }
        public static void SauveBoutiques() { Ecrit(FichierBoutiques, OffresSources.EcritChoixBoutiques(Boutiques)); }

        public static void SauveSuivis() { Ecrit(FichierSuivis, Suivi.Ecrire(Suivis)); }
        public static void SauveFavorisJeux() { Ecrit(FichierJeux, Favoris.Ecrire(FavorisJeux)); }
        public static void SauveFavorisActus() { Ecrit(FichierActus, Favoris.Ecrire(FavorisActus)); }

        private static string Lit(string nom)
        {
            try
            {
                string chemin = AppPaths.File(nom);
                return File.Exists(chemin) ? File.ReadAllText(chemin, Encoding.UTF8) : "";
            }
            catch { return ""; }
        }

        private static void Ecrit(string nom, string texte)
        {
            try
            {
                string chemin = AppPaths.File(nom);
                string tmp = chemin + ".tmp";
                File.WriteAllText(tmp, texte ?? "", new UTF8Encoding(false));
                File.Move(tmp, chemin, true);
            }
            catch { }
            try { var h = Change; if (h != null) h(); } catch { }
        }
    }

    /// <summary>
    /// Veille des jeux suivis : un contrôle 90 s après le démarrage, puis toutes les 6 h,
    /// jamais pendant une partie (reporté tant qu'un jeu tourne ou que le Mode Jeu est actif),
    /// jamais sans jeu suivi (aucun appel réseau). Notification Windows et pastille sur le rail.
    /// </summary>
    internal static class SuiviVeille
    {
        private static readonly TimeSpan PremierDelai = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan Intervalle = TimeSpan.FromHours(6);
        private static readonly TimeSpan ApresEchec = TimeSpan.FromMinutes(15);

        private static DashboardForm _host;
        private static Timer _minuteur;
        private static DateTime _prochainUtc;
        private static Task _enCours;
        private static List<Offre> _dernieresFiches = new List<Offre>();

        /// <summary>Fiches Steam du dernier contrôle abouti, pour le sous-onglet Suivis.</summary>
        public static List<Offre> DernieresFiches { get { return _dernieresFiches; } }

        public static int AlertesNonVues { get; private set; }

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

        /// <summary>Remet la pastille à 0.</summary>
        public static void MarqueVues()
        {
            AlertesNonVues = 0;
            if (_host != null) _host.SignaleBonsPlans(0);
        }

        private static void Tick()
        {
            try
            {
                if (_enCours != null || DateTime.UtcNow < _prochainUtc) return;
                // Reporté (le contrôle reste dû et repart à la minute où la condition tombe).
                if (Magasin.Suivis.Count == 0) return;
                if (GameBoost.IsActive || Native.IsGameFullscreen() || GameScan.RunningKnownGame() != null) return;
                Controle();
            }
            catch { }
        }

        /// <summary>
        /// Un contrôle tout de suite (thread d'interface). Un seul à la fois : pendant un contrôle,
        /// renvoie la tâche de celui en cours.
        /// </summary>
        public static Task Controle()
        {
            if (_enCours != null) return _enCours;
            DashboardForm host = _host;
            if (host == null) return Task.CompletedTask;
            List<int> ids = Magasin.Suivis.Where(j => j != null && j.AppId > 0).Select(j => j.AppId).ToList();
            if (ids.Count == 0) return Task.CompletedTask;

            var fin = new TaskCompletionSource<bool>();
            _enCours = fin.Task;
            Task.Run(async () =>
            {
                List<Offre> fiches = null, gratuits = null;
                bool echecSteam = true, echecGratuits = true;
                try
                {
                    fiches = await OffresReseau.FichesSteam(ids, 0).ConfigureAwait(false);
                    echecSteam = OffresReseau.DerniereErreur("steam");
                    gratuits = await OffresReseau.Gratuits().ConfigureAwait(false);
                    var enEchec = new List<string>();
                    if (OffresReseau.DerniereErreur("epic")) enEchec.Add("epic");
                    if (OffresReseau.DerniereErreur("gamerpower")) enEchec.Add("gamerpower");
                    echecGratuits = enEchec.Count == 2;
                    // Une source en échec rend son cache, parfois vieux de plusieurs jours : on ne notifie pas dessus.
                    gratuits = Offres.SansSources(gratuits, enEchec);
                }
                catch { }
                try
                {
                    host.BeginInvoke((Action)(() =>
                    {
                        try { Applique(host, echecSteam ? null : fiches, echecGratuits ? null : gratuits, echecSteam && echecGratuits); }
                        catch { }
                        finally { _enCours = null; fin.TrySetResult(true); }
                    }));
                }
                catch { _enCours = null; fin.TrySetResult(true); }   // fenêtre détruite
            });
            return fin.Task;
        }

        // Thread d'interface. Une source en échec arrive à null : son cache périmé ne déclenche jamais d'alerte.
        private static void Applique(DashboardForm host, List<Offre> fiches, List<Offre> gratuits, bool echec)
        {
            if (echec) { _prochainUtc = DateTime.UtcNow + ApresEchec; return; }
            _prochainUtc = DateTime.UtcNow + Intervalle;
            if (fiches != null) _dernieresFiches = fiches;

            List<Suivi.Alerte> alertes = Suivi.Decide(Magasin.Suivis, fiches, gratuits);
            Magasin.SauveSuivis();
            if (alertes.Count == 0) return;
            if (NotifReglages.Active(NotifCategories.JeuxSuivis))   // coupé : la pastille de Bons plans reste
                foreach (Suivi.Alerte a in Suivi.Regroupe(alertes, 3)) host.Notifie(a.Titre, a.Texte);
            AlertesNonVues += alertes.Count;
            host.SignaleBonsPlans(AlertesNonVues);
        }
    }
}
