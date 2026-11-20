using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace BTOptimizer
{
    /// <summary>
    /// Validation EN JEU de l'optimisation prouvée : 60 s de partie AVANT la preuve, 60 s APRÈS,
    /// sur le même jeu. Régression confirmée (seconde mesure) → proposer d'annuler les réglages
    /// gardés par la dernière preuve. Le jugement est dans Preuve.JugeJeu (pur, testé).
    /// </summary>
    internal static class PreuveJeu
    {
        public const string FichierAvant = "bt-preuve-jeu-avant.txt";
        public const string FichierDerniere = "bt-preuve-derniere.txt";
        public const int SecondesJeu = 60;
        private const int AttenteJeuMaxS = 300;     // 5 min pour lancer le jeu et entrer en partie
        private const int DureeMaxS = 15 * 60;      // garde-fou : alt-tab à rallonge

        private static string Lit(string nom)
        {
            try { string p = AppPaths.File(nom); return File.Exists(p) ? File.ReadAllText(p) : null; } catch { return null; }
        }

        private static void Ecrit(string nom, string texte)
        {
            File.WriteAllText(AppPaths.File(nom), texte, new UTF8Encoding(false));
        }

        public static Preuve.MesureJeu Avant() { return Preuve.LitJeu(Lit(FichierAvant)); }
        public static Preuve.Attente DernierePreuve() { return Preuve.Lit(Lit(FichierDerniere)); }
        public static void EnregistreAvant(Preuve.MesureJeu m) { Ecrit(FichierAvant, Preuve.EcritJeu(m)); }
        public static void OublieAvant() { try { File.Delete(AppPaths.File(FichierAvant)); } catch { } }

        /// <summary>Faut-il la mesure « après » (une preuve a eu lieu depuis la mesure « avant ») ?</summary>
        public static bool EtapeApres() { return Preuve.MesureApres(Avant(), DernierePreuve()); }

        /// <summary>
        /// Appelé à la fin de chaque preuve : retient les réglages qu'elle a laissés appliqués.
        /// Plusieurs preuves après une même mesure « avant » s'additionnent (la mesure « après »
        /// juge leur effet cumulé) ; sinon la liste repart de zéro.
        /// </summary>
        public static void EnregistreDernierePreuve(DateTime debut, IEnumerable<Tweak> laisses)
        {
            var ids = laisses.Select(t => t.Id).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            if (ids.Count == 0) return;
            var a = new Preuve.Attente { Applique = debut, Reference = new Preuve.Mesure() };
            Preuve.MesureJeu avant = Avant();
            Preuve.Attente ancienne = DernierePreuve();
            if (Preuve.MesureApres(avant, ancienne))
            {
                a.Applique = ancienne.Applique;
                a.Ids.AddRange(ancienne.Ids);
            }
            foreach (string id in ids) if (!a.Ids.Contains(id)) a.Ids.Add(id);
            try { Ecrit(FichierDerniere, Preuve.Ecrit(a)); } catch { }
        }

        /// <summary>
        /// Attend qu'un jeu soit AU PREMIER PLAN, puis capte <see cref="SecondesJeu"/> secondes
        /// d'images de CE processus. Les images présentées pendant un alt-tab ne comptent pas.
        /// Rend null si arrêt demandé, si aucun jeu n'apparaît, ou si le jeu se ferme trop tôt.
        /// </summary>
        public static Preuve.MesureJeu Mesure(Action<string, int> log, Func<bool> stop, string jeuAttendu)
        {
            using (var fps = new FpsEtw())
            {
                if (!fps.Start()) { log("Capture des FPS impossible : " + fps.LastError, 3); return null; }
                log(jeuAttendu == null
                    ? "Lance ton jeu et mets-toi en partie : la mesure démarre dès qu'il est au premier plan."
                    : "Lance " + jeuAttendu + " et mets-toi en partie, dans une scène comparable à la mesure « avant ».", 0);

                using var pres = new Presentation();
                var fts = new List<double>();
                string jeu = null;
                int pid = 0;
                long vu = -1;
                int secondes = 0;
                bool enPause = false, mauvaisJeuSignale = false;
                var horloge = Stopwatch.StartNew();

                while (secondes < SecondesJeu)
                {
                    if (stop()) { log("Mesure en jeu arrêtée.", 2); return null; }
                    if (horloge.Elapsed.TotalSeconds > (jeu == null ? AttenteJeuMaxS : DureeMaxS))
                    {
                        log(jeu == null ? "Aucun jeu détecté au premier plan en 5 minutes." : "Mesure trop longue (jeu rarement au premier plan) : abandon.", 3);
                        return null;
                    }
                    Thread.Sleep(1000);
                    int premierPlan = FpsEtw.PidPremierPlan();

                    if (jeu == null)
                    {
                        FpsEtw.ProcStat p = FpsEtw.ChoisirJeu(fps.Snapshot(2000), premierPlan);
                        if (p == null || p.Pid != premierPlan) continue;
                        if (jeuAttendu != null && !string.Equals(p.Name, jeuAttendu, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!mauvaisJeuSignale) { log(p.Name + " n'est pas le jeu de la mesure « avant » (" + jeuAttendu + ").", 2); mauvaisJeuSignale = true; }
                            continue;
                        }
                        jeu = p.Name; pid = p.Pid;
                        fps.NouvellesImages(pid, ref vu);   // pose le repère : on compte à partir d'ici
                        pres.Demarre(pid, log);
                        log("Jeu détecté : " + jeu + ". Joue normalement pendant " + SecondesJeu + " s.", 1);
                        continue;
                    }

                    bool vivant;
                    try { vivant = !Process.GetProcessById(pid).HasExited; } catch { vivant = false; }
                    if (!vivant) { log(jeu + " s'est fermé avant la fin de la mesure.", 3); return null; }

                    double[] nouvelles = fps.NouvellesImages(pid, ref vu);   // lu à chaque tour : un alt-tab est jeté, pas reporté
                    if (premierPlan != pid)
                    {
                        if (!enPause) { log("Jeu hors du premier plan : mesure en pause.", 2); enPause = true; }
                        pres.HorsPremierPlan();
                        continue;
                    }
                    if (enPause) { log("Reprise de la mesure.", 0); enPause = false; }
                    if (nouvelles.Length == 0) continue;   // jeu figé ou en chargement : seconde non comptée
                    fts.AddRange(nouvelles);
                    secondes++;
                    pres.SecondeComptee(jeu, log);
                    if (secondes % 15 == 0) log(secondes + " / " + SecondesJeu + " s mesurées.", 0);
                }

                Preuve.MesureJeu m = Preuve.CalculeJeu(jeu, fts);
                try { m.Plafond = FrameCap.Pose(); } catch { m.Plafond = -1; }
                m.Presentation = pres.Mode;
                log("Mesure en jeu : " + m.Resume(), 1);
                if (!Preuve.JeuValide(m)) log("Trop peu d'images pour un 1 % low fiable (" + m.Images + ").", 2);
                return m;
            }
        }

        /// <summary>
        /// Relevé du mode de présentation pendant la mesure (session ETW à part, lecture seule).
        /// Libérée par « using » même si la mesure sort tôt. Ne touche pas aux FPS.
        /// </summary>
        private sealed class Presentation : IDisposable
        {
            private const int SecondesReleve = 5;
            private ModePresentationEtw _etw;
            private int _consecutives;
            private bool _fait;
            public ModePresentation.ModeImage Mode = ModePresentation.ModeImage.Inconnu;

            public void Demarre(int pid, Action<string, int> log)
            {
                try
                {
                    var e = new ModePresentationEtw();
                    if (e.Start(pid)) { _etw = e; return; }
                    log("Mode de présentation non relevé : " + e.LastError, 0);
                    e.Dispose();
                }
                catch (Exception ex) { log("Mode de présentation non relevé : " + ex.Message, 0); }
                _fait = true;
            }

            public void HorsPremierPlan()
            {
                _consecutives = 0;
                if (_etw != null) _etw.Reinitialise();
            }

            public void SecondeComptee(string jeu, Action<string, int> log)
            {
                if (_fait || _etw == null) return;
                if (++_consecutives < SecondesReleve) return;
                _fait = true;
                try
                {
                    Mode = ModePresentation.Classe(_etw.Releve());
                    _etw.Dispose();
                    _etw = null;
                    log("Mode de présentation : " + ModePresentation.Libelle(Mode), 0);
                    string constat = ModePresentation.Constat(Mode, jeu, HzPrincipal(), WindowedFlipActif());
                    if (constat != null) log(constat, 2);
                }
                catch (Exception ex) { log("Mode de présentation non relevé : " + ex.Message, 0); }
            }

            private static int HzPrincipal()
            {
                try
                {
                    var ecran = DisplayInfo.Query().Find(d => d.Primary);
                    return ecran == null ? 0 : ecran.CurrentHz;
                }
                catch { return 0; }
            }

            private static bool WindowedFlipActif()
            {
                try
                {
                    Tweak t = Catalog.All().Find(x => x.Id == "windowed_flip");
                    return t != null && t.Check != null && t.Check() == true;
                }
                catch { return false; }
            }

            public void Dispose()
            {
                if (_etw != null) { try { _etw.Dispose(); } catch { } _etw = null; }
            }
        }
    }
}
