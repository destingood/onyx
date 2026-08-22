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
            public double PireDpcMs;    // pire DPC seul
            public double PireIsrMs;    // pire interruption seule
        }

        private readonly object _lock = new object();
        private readonly List<Module> _modules = new List<Module>();
        private readonly Dictionary<string, Pilote> _pilotes = new Dictionary<string, Pilote>(StringComparer.OrdinalIgnoreCase);
        // Instantané IMMUABLE de la table de résolution, remplacé d'un bloc à chaque nouveau
        // module. Les rappels ETW le lisent sans verrou : jamais de liste modifiée en cours de
        // lecture, et aucune attente sur le chemin le plus chaud du programme.
        private volatile Module[] _index = new Module[0];
        private TraceEventSession _session;
        private System.Threading.Thread _thread;

        public bool EnCours { get; private set; }
        public string DerniereErreur { get; private set; }

        /// <summary>
        /// Événements JETÉS par le noyau parce que le consommateur ne suivait pas.
        ///
        /// C'est le chiffre que tout outil de mesure doit publier et qu'aucun ne publie. Quand ETW
        /// déborde, il n'attend pas : il jette. Les DPC perdus sont invisibles dans le résultat, et
        /// l'erreur va TOUJOURS dans le même sens — la latence paraît meilleure qu'elle n'est.
        /// Rendre un beau rapport bâti sur une mesure trouée serait le pire service à rendre.
        /// </summary>
        public int EvenementsPerdus
        {
            get { try { return _session == null ? 0 : _session.EventsLost; } catch { return 0; } }
        }

        /// <summary>
        /// Résolution PURE d'une adresse vers un nom de pilote (testable sans noyau).
        /// Renvoie null si l'adresse ne tombe dans aucun module connu — on ne devine JAMAIS.
        /// Balayage linéaire : sert de RÉFÉRENCE aux tests, pas au chemin chaud.
        /// </summary>
        public static string Resout(List<Module> modules, ulong adresse)
        {
            if (modules == null) return null;
            foreach (Module m in modules)
                if (adresse >= m.Base && adresse < m.Fin) return m.Nom;
            return null;
        }

        /// <summary>
        /// Prépare PUREMENT la table de résolution : triée par adresse de base, et DÉDOUBLONNÉE.
        ///
        /// Le dédoublonnage n'est pas cosmétique. Un pilote déchargé puis rechargé laisse son
        /// ancienne plage dans la liste, à une adresse différente. Le balayage linéaire rendait
        /// alors le PREMIER module trouvé — c'est-à-dire potentiellement le périmé. On garde la
        /// dernière plage connue pour chaque nom.
        /// </summary>
        public static Module[] Indexe(List<Module> modules)
        {
            if (modules == null || modules.Count == 0) return new Module[0];
            var dernier = new Dictionary<string, Module>(StringComparer.OrdinalIgnoreCase);
            foreach (Module m in modules)
            {
                if (m == null || string.IsNullOrEmpty(m.Nom) || m.Fin <= m.Base) continue;
                dernier[m.Nom] = m;   // le plus récent l'emporte
            }
            var arr = new List<Module>(dernier.Values).ToArray();
            Array.Sort(arr, delegate (Module a, Module b) { return a.Base.CompareTo(b.Base); });
            return arr;
        }

        /// <summary>
        /// Résolution PURE par recherche dichotomique sur la table préparée.
        ///
        /// POURQUOI CE N'EST PAS UNE COQUETTERIE : ce code tourne sur CHAQUE DPC et CHAQUE
        /// interruption, soit des dizaines de milliers de fois par seconde. À 300 modules noyau, le
        /// balayage linéaire faisait des millions de comparaisons par seconde — et quand le
        /// consommateur ETW ne suit plus, le noyau JETTE des événements. L'outil de mesure faussait
        /// alors sa propre mesure, silencieusement et toujours dans le même sens : vers le bas.
        /// </summary>
        public static string ResoutRapide(Module[] tries, ulong adresse)
        {
            if (tries == null || tries.Length == 0) return null;
            int lo = 0, hi = tries.Length - 1, trouve = -1;
            while (lo <= hi)
            {
                int mid = (int)(((uint)lo + (uint)hi) >> 1);
                if (tries[mid].Base <= adresse) { trouve = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            if (trouve < 0) return null;
            Module m = tries[trouve];
            return adresse < m.Fin ? m.Nom : null;
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
            // Lecture SANS VERROU d'un instantané IMMUABLE. L'ancien code parcourait la liste
            // vivante pendant qu'un chargement de pilote y ajoutait une entrée : une énumération
            // concurrente lève « Collection was modified », l'exception remontait hors du rappel
            // ETW, et le try/catch autour de Process() avalait tout — la mesure s'arrêtait sans
            // un mot, en laissant croire que le PC n'avait plus de DPC.
            Module[] snap = _index;
            string n = ResoutRapide(snap, adresse);
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
                // Séparer DPC et interruptions : ce ne sont pas les mêmes causes ni les mêmes
                // remèdes, et les confondre masque lequel des deux fait la saccade.
                if (estDpc) { if (ms > p.PireDpcMs) p.PireDpcMs = ms; }
                else { if (ms > p.PireIsrMs) p.PireIsrMs = ms; }
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
                {
                    _modules.Add(new Module { Base = b, Fin = b + (ulong)d.ImageSize, Nom = nom });
                    _index = Indexe(_modules);   // publication atomique du nouvel instantané
                }
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
                    copie.Add(new Pilote { Nom = kv.Value.Nom, Dpc = kv.Value.Dpc, Isr = kv.Value.Isr,
                        TotalMs = kv.Value.TotalMs, PireMs = kv.Value.PireMs,
                        PireDpcMs = kv.Value.PireDpcMs, PireIsrMs = kv.Value.PireIsrMs });
                return Classement(copie);
            }
        }

        /// <summary>Mise en forme, façon rapport de latence.</summary>
        public static string Texte(List<Pilote> classement, int top, double secondes)
        {
            return Texte(classement, top, secondes, 0);
        }

        /// <summary>
        /// Mise en forme PURE. <paramref name="perdus"/> = événements jetés par le noyau : s'il y
        /// en a, le rapport le dit EN PREMIER, avant les chiffres qu'il rend douteux.
        /// </summary>
        public static string Texte(List<Pilote> classement, int top, double secondes, int perdus)
        {
            if (classement == null || classement.Count == 0)
                return "Aucun événement DPC/ISR capturé — mesure trop courte, ou session noyau refusée.";
            double pire = classement[0].PireMs;
            var sb = new System.Text.StringBuilder();
            sb.Append("LATENCE DPC / ISR — ").Append(Math.Round(secondes)).Append(" s de mesure\n\n");
            if (perdus > 0)
                sb.Append("⚠ ").Append(perdus).Append(" événement(s) JETÉ(S) par le noyau pendant la mesure.\n")
                  .Append("Les chiffres ci-dessous sont donc SOUS-ESTIMÉS : la latence réelle est pire.\n")
                  .Append("Ferme ce qui charge la machine, puis recommence.\n\n");
            sb.Append("Pire exécution isolée : ").Append(pire.ToString("0.000")).Append(" ms\n");
            sb.Append(Verdict(pire)).Append("\n\n");
            sb.Append("Pilotes classés par PIRE temps d'exécution (c'est lui qui fait les saccades) :\n\n");
            int n = 0;
            foreach (Pilote p in classement)
            {
                if (n++ >= top) break;
                sb.Append("  ").Append(p.PireMs.ToString("0.000").PadLeft(8)).Append(" ms   ")
                  .Append(p.Nom.PadRight(28))
                  // Le total BRUT est trompeur : il grandit avec la durée de la mesure. On donne
                  // donc à côté ce qu'il représente vraiment — une part d'un cœur — et un débit
                  // d'exécutions comparable d'une mesure à l'autre.
                  .Append(PartUnCoeur(p.TotalMs, secondes).ToString("0.00")).Append(" % d'un cœur  ")
                  .Append(ParSeconde(p.Dpc + p.Isr, secondes).ToString("0")).Append("/s");
                // Dire LEQUEL des deux fait le pire temps quand le pilote produit les deux : un DPC
                // trop long et une interruption trop longue n'ont ni la même cause ni le même
                // remède, et les confondre envoie chercher au mauvais endroit.
                if (p.PireDpcMs > 0 && p.PireIsrMs > 0)
                    sb.Append("   (pire DPC ").Append(p.PireDpcMs.ToString("0.000"))
                      .Append(" / pire ISR ").Append(p.PireIsrMs.ToString("0.000")).Append(")");
                sb.Append("\n");
            }
            // Somme de ce que TOUT le travail différé a coûté : le chiffre qui dit s'il vaut la
            // peine de chercher à le réduire, ou s'il faut regarder ailleurs.
            double totalTout = 0;
            foreach (Pilote p in classement) totalTout += p.TotalMs;
            sb.Append("\nCoût total du travail différé : ")
              .Append(PartUnCoeur(totalTout, secondes).ToString("0.00"))
              .Append(" % d'un cœur (sur ").Append(Environment.ProcessorCount).Append(" disponibles).\n");
            sb.Append("Un pilote peut avoir un total élevé sans gêner (beaucoup d'exécutions très courtes). "
                    + "C'est le PIRE temps qui compte : pendant qu'il s'exécute, son cœur ne fait rien d'autre. "
                    + "Le total, lui, ne dit que la charge de fond — et il grandit avec la durée de la mesure, "
                    + "d'où le pourcentage à côté.");
            // Wdf01000.sys n'est pas un pilote de périphérique : c'est le cadre d'exécution dans
            // lequel tournent la plupart des pilotes modernes (Wi-Fi, USB, contrôleurs…). Leurs DPC
            // lui sont attribués, à lui et pas à eux. Voir ce nom en tête n'accuse donc PERSONNE en
            // particulier — le taire laisserait chercher un coupable qui n'existe pas.
            if (Contient(classement, top, "Wdf01000.sys"))
                sb.Append("\n\nWdf01000.sys n'est pas un périphérique : c'est le cadre d'exécution des pilotes "
                        + "modernes (Wi-Fi, USB, contrôleurs). Les leurs y sont comptés, donc ce nom ne "
                        + "désigne aucun matériel précis — il faut débrancher ou désactiver pour trancher.");
            return sb.ToString();
        }

        /// <summary>
        /// PUR : part d'UN cœur qu'a réellement consommée ce pilote, en pourcentage.
        ///
        /// C'est la seule lecture honnête du « temps total ». Brut, ce nombre ne veut rien dire :
        /// il grandit avec la durée de la mesure. Un pilote à 1 300 ms paraît catastrophique — sur
        /// cinq minutes de mesure, c'est 0,43 % d'un cœur, c'est-à-dire rien. Sans cette division,
        /// on part chasser un problème qui n'existe pas.
        /// </summary>
        public static double PartUnCoeur(double totalMs, double secondes)
        {
            if (secondes <= 0 || totalMs <= 0) return 0;
            return totalMs / (secondes * 1000.0) * 100.0;
        }

        /// <summary>PUR : nombre d'exécutions par seconde — comparable d'une mesure à l'autre,
        /// contrairement au compte brut.</summary>
        public static double ParSeconde(long nombre, double secondes)
        {
            if (secondes <= 0 || nombre <= 0) return 0;
            return nombre / secondes;
        }

        /// <summary>PUR : ce nom figure-t-il dans les <paramref name="top"/> premiers du classement ?</summary>
        public static bool Contient(List<Pilote> classement, int top, string nom)
        {
            if (classement == null || string.IsNullOrEmpty(nom)) return false;
            int n = 0;
            foreach (Pilote p in classement)
            {
                if (n++ >= top) break;
                if (string.Equals(p.Nom, nom, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
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
