using System;
using System.Collections.Generic;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace BTOptimizer
{
    /// <summary>
    /// LATENCE DPC/ISR PAR PILOTE, EN DIRECT — l'équivalent de LatencyMon, sans outil externe.
    ///
    /// Un DPC est un travail que le pilote diffère hors de l'interruption elle-même. Tant qu'il
    /// s'exécute, il monopolise son cœur : rien d'autre ne passe. Un pilote qui tient le cœur 0
    /// pendant 2 ms produit une saccade que ni le processeur ni la carte graphique n'expliquent —
    /// et c'est invisible dans le Gestionnaire des tâches, qui ne montre qu'un pourcentage global.
    ///
    /// L'existant (DpcIsr.cs) lance xperf et lit sa sortie texte : il faut l'outil, une trace sur
    /// disque, et l'analyse arrive après coup. Ici on s'abonne directement aux événements noyau via
    /// TraceEvent — déjà présent dans le projet pour le compteur d'images — et on mesure EN DIRECT,
    /// sans rien installer et sans fichier intermédiaire.
    ///
    /// Ce qu'on rend, comme LatencyMon : par pilote, le nombre d'exécutions, le temps total, et
    /// surtout le PIRE temps d'exécution. C'est ce dernier qui fait les saccades — une moyenne
    /// basse avec un maximum à 3 ms est un mauvais résultat, pas un bon.
    ///
    /// Droits administrateur requis (session noyau ETW).
    /// </summary>
    internal sealed class DpcLive : IDisposable
    {
        private const string NomSession = "ONYX-DpcIsr";

        /// <summary>Plage mémoire d'un module noyau, pour retrouver le pilote d'une adresse.</summary>
        public sealed class Module
        {
            public ulong Base;
            public ulong Fin;
            public string Nom;
        }

        /// <summary>Cumul pour un pilote.</summary>
        public sealed class Pilote
        {
            public string Nom;
            public long Dpc;            // nombre de DPC
            public long Isr;            // nombre d'interruptions
            public double TotalMs;      // temps cumulé
            public double PireMs;       // pire exécution isolée — c'est elle qui fait la saccade
        }

        private readonly object _lock = new object();
        private readonly List<Module> _modules = new List<Module>();
        private readonly Dictionary<string, Pilote> _pilotes = new Dictionary<string, Pilote>(StringComparer.OrdinalIgnoreCase);
        private TraceEventSession _session;
        private System.Threading.Thread _thread;

        public bool EnCours { get; private set; }
        public string DerniereErreur { get; private set; }

        /// <summary>
        /// Résolution PURE d'une adresse vers un nom de pilote (testable sans noyau).
        /// Renvoie null si l'adresse ne tombe dans aucun module connu — on ne devine JAMAIS.
        /// </summary>
        public static string Resout(List<Module> modules, ulong adresse)
        {
            if (modules == null) return null;
            foreach (Module m in modules)
                if (adresse >= m.Base && adresse < m.Fin) return m.Nom;
            return null;
        }

        /// <summary>Classement PUR : pire temps d'exécution d'abord, c'est lui qui compte.</summary>
        public static List<Pilote> Classement(IEnumerable<Pilote> pilotes)
        {
            var l = new List<Pilote>();
            if (pilotes != null) foreach (var p in pilotes) if (p != null) l.Add(p);
            l.Sort(delegate (Pilote a, Pilote b)
            {
                int c = b.PireMs.CompareTo(a.PireMs);
                return c != 0 ? c : b.TotalMs.CompareTo(a.TotalMs);
            });
            return l;
        }

        /// <summary>
        /// Verdict PUR sur le pire temps mesuré, avec les seuils usuels de l'analyse de latence :
        /// sous 0,5 ms rien à signaler ; au-delà de 2 ms, les décrochages audio et les saccades
        /// deviennent audibles et visibles.
        /// </summary>
        public static string Verdict(double pireMs)
        {
            if (pireMs <= 0) return "Aucune mesure exploitable.";
            if (pireMs < 0.5) return "Excellent : aucun pilote ne monopolise son cœur.";
            if (pireMs < 1.0) return "Bon : rien qui se ressente en jeu.";
            if (pireMs < 2.0) return "Moyen : des micro-saccades sont possibles dans les moments chargés.";
            return "Mauvais : à ce niveau, un pilote bloque son cœur assez longtemps pour provoquer "
                 + "des saccades visibles et des décrochages audio.";
        }

        private string NomDe(ulong adresse)
        {
            string n = Resout(_modules, adresse);
            return n ?? "(pilote inconnu)";
        }

        private void Ajoute(ulong routine, double ms, bool estDpc)
        {
            if (ms < 0 || ms > 10000) return;   // valeur aberrante : on ne la compte pas
            string nom = NomDe(routine);
            lock (_lock)
            {
                Pilote p;
                if (!_pilotes.TryGetValue(nom, out p)) { p = new Pilote { Nom = nom }; _pilotes[nom] = p; }
                if (estDpc) p.Dpc++; else p.Isr++;
                p.TotalMs += ms;
                if (ms > p.PireMs) p.PireMs = ms;
            }
        }

        /// <summary>Démarre la mesure. false si la session noyau est refusée (droits insuffisants).</summary>
        public bool Demarre()
        {
            if (EnCours) return true;
            try
            {
                try
                {
                    var vieille = TraceEventSession.GetActiveSession(NomSession);
                    if (vieille != null) vieille.Stop(true);
                }
                catch { }

                _session = new TraceEventSession(NomSession);
                _session.EnableKernelProvider(
                    KernelTraceEventParser.Keywords.DeferedProcedureCalls
                    | KernelTraceEventParser.Keywords.Interrupt
                    | KernelTraceEventParser.Keywords.ImageLoad);

                // Les images déjà chargées sont annoncées au démarrage de la session : sans elles,
                // toutes les adresses tomberaient dans « pilote inconnu ».
                _session.Source.Kernel.ImageLoad += delegate (Microsoft.Diagnostics.Tracing.Parsers.Kernel.ImageLoadTraceData d) { NoteModule(d); };
                _session.Source.Kernel.ImageDCStart += delegate (Microsoft.Diagnostics.Tracing.Parsers.Kernel.ImageLoadTraceData d) { NoteModule(d); };

                _session.Source.Kernel.PerfInfoDPC += delegate (Microsoft.Diagnostics.Tracing.Parsers.Kernel.DPCTraceData d) { Ajoute((ulong)d.Routine, d.ElapsedTimeMSec, true); };
                _session.Source.Kernel.PerfInfoThreadedDPC += delegate (Microsoft.Diagnostics.Tracing.Parsers.Kernel.DPCTraceData d) { Ajoute((ulong)d.Routine, d.ElapsedTimeMSec, true); };
                _session.Source.Kernel.PerfInfoTimerDPC += delegate (Microsoft.Diagnostics.Tracing.Parsers.Kernel.DPCTraceData d) { Ajoute((ulong)d.Routine, d.ElapsedTimeMSec, true); };
                _session.Source.Kernel.PerfInfoISR += delegate (Microsoft.Diagnostics.Tracing.Parsers.Kernel.ISRTraceData d) { Ajoute((ulong)d.Routine, d.ElapsedTimeMSec, false); };

                _thread = new System.Threading.Thread(delegate () { try { _session.Source.Process(); } catch { } });
                _thread.IsBackground = true;
                _thread.Name = "ONYX-DpcIsr";
                _thread.Start();
                EnCours = true;
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                DerniereErreur = "droits administrateur requis pour la session noyau";
                Nettoie();
                return false;
            }
            catch (Exception ex)
            {
                DerniereErreur = ex.Message;
                Nettoie();
                return false;
            }
        }

        private void NoteModule(Microsoft.Diagnostics.Tracing.Parsers.Kernel.ImageLoadTraceData d)
        {
            try
            {
                string f = d.FileName;
                if (string.IsNullOrEmpty(f)) return;
                string nom = System.IO.Path.GetFileName(f);
                ulong b = (ulong)d.ImageBase;
                if (b == 0 || d.ImageSize <= 0) return;
                lock (_lock)
                    _modules.Add(new Module { Base = b, Fin = b + (ulong)d.ImageSize, Nom = nom });
            }
            catch { }
        }

        /// <summary>Photo des cumuls, classée.</summary>
        public List<Pilote> Instantane()
        {
            lock (_lock)
            {
                var copie = new List<Pilote>();
                foreach (var kv in _pilotes)
                    copie.Add(new Pilote { Nom = kv.Value.Nom, Dpc = kv.Value.Dpc, Isr = kv.Value.Isr, TotalMs = kv.Value.TotalMs, PireMs = kv.Value.PireMs });
                return Classement(copie);
            }
        }

        /// <summary>Mise en forme PURE, façon rapport de latence.</summary>
        public static string Texte(List<Pilote> classement, int top, double secondes)
        {
            if (classement == null || classement.Count == 0)
                return "Aucun événement DPC/ISR capturé — mesure trop courte, ou session noyau refusée.";
            double pire = classement[0].PireMs;
            var sb = new System.Text.StringBuilder();
            sb.Append("LATENCE DPC / ISR — ").Append(Math.Round(secondes)).Append(" s de mesure\n\n");
            sb.Append("Pire exécution isolée : ").Append(pire.ToString("0.000")).Append(" ms\n");
            sb.Append(Verdict(pire)).Append("\n\n");
            sb.Append("Pilotes classés par PIRE temps d'exécution (c'est lui qui fait les saccades) :\n\n");
            int n = 0;
            foreach (Pilote p in classement)
            {
                if (n++ >= top) break;
                sb.Append("  ").Append(p.PireMs.ToString("0.000").PadLeft(8)).Append(" ms   ")
                  .Append(p.Nom.PadRight(28))
                  .Append("total ").Append(p.TotalMs.ToString("0.0")).Append(" ms  ")
                  .Append(p.Dpc).Append(" DPC / ").Append(p.Isr).Append(" ISR\n");
            }
            sb.Append("\nUn pilote peut avoir un total élevé sans gêner (beaucoup d'exécutions très courtes). "
                    + "C'est le PIRE temps qui compte : pendant qu'il s'exécute, son cœur ne fait rien d'autre.");
            return sb.ToString();
        }

        private void Nettoie()
        {
            try { if (_session != null) _session.Dispose(); } catch { }
            _session = null;
        }

        public void Dispose()
        {
            EnCours = false;
            Nettoie();
            try { if (_thread != null) _thread.Join(2000); } catch { }
            _thread = null;
        }
    }
}
