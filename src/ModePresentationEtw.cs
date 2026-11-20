// Règles de classement inspirées de PresentMon, Intel, licence MIT
// (Copyright (C) 2017-2024 Intel Corporation). Version simplifiée réécrite : aucun code copié.
using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace BTOptimizer
{
    /// <summary>
    /// Relevé EN LECTURE SEULE du mode de présentation d'un jeu : session ETW utilisateur
    /// SÉPARÉE de celle des FPS, abonnée à Microsoft-Windows-DxgKrnl (mots-clés Base | Present,
    /// filtrée par identifiant d'événement). Elle ne modifie rien : le classement est dans
    /// ModePresentation (pur, testé). Droits administrateur requis, comme FpsEtw.
    /// </summary>
    internal sealed class ModePresentationEtw : IDisposable
    {
        private const string SessionName = "BTOptimizer-Presentation";
        private static readonly Guid DxgKrnl = new Guid("802EC45A-1E99-4B83-9920-87C98277BA9D");
        private const ulong Mots = 0x8000001;   // Base | Present

        private const int QueuePacketStart = 178, PresentHistoryStart = 171, PresentHistoryDetailedStart = 215,
            BlitInfo = 166, FlipInfo = 168, FlipMpoInfo = 252,
            IndependentFlipInfo = 266, MmioFlipInfo = 116, MmioFlipMpoInfo = 259;

        private static readonly int[] Ids =
        {
            QueuePacketStart, PresentHistoryStart, PresentHistoryDetailedStart, BlitInfo, FlipInfo,
            FlipMpoInfo, IndependentFlipInfo, MmioFlipInfo, MmioFlipMpoInfo
        };

        private TraceEventSession _session;
        private Thread _thread;
        private int _pid;
        private readonly object _lock = new object();
        private readonly ModePresentation.Suivi _suivi = new ModePresentation.Suivi();
        private int _flipsJeu, _copiesJeu, _flipsCompo;

        public bool Running { get; private set; }
        public string LastError { get; private set; }

        public bool Start(int pid)
        {
            if (Running) return true;
            _pid = pid;
            try
            {
                try
                {
                    var old = TraceEventSession.GetActiveSession(SessionName);
                    if (old != null) old.Stop(true);
                }
                catch { }

                _session = new TraceEventSession(SessionName);
                var options = new TraceEventProviderOptions();
                options.EventIDsToEnable = new List<int>(Ids);
                _session.EnableProvider(DxgKrnl, TraceEventLevel.Informational, Mots, options);

                var parser = new RegisteredTraceEventParser(_session.Source);
                parser.All += OnEvent;

                _thread = new Thread(() => { try { _session.Source.Process(); } catch { } });
                _thread.IsBackground = true;
                _thread.Name = "BT-PresentationEtw";
                _thread.Start();

                Running = true;
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                LastError = "droits administrateur requis pour la session ETW";
                Cleanup();
                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Cleanup();
                return false;
            }
        }

        /// <summary>Jeu hors du premier plan : on repart de zéro, ce qui précède ne compte pas.</summary>
        public void Reinitialise()
        {
            lock (_lock)
            {
                _suivi.Reinitialise();
                _flipsJeu = _copiesJeu = _flipsCompo = 0;
            }
        }

        public ModePresentation.Releve Releve()
        {
            lock (_lock)
            {
                ModePresentation.Releve r = _suivi.Releve();
                r.FlipsJeu = _flipsJeu;
                r.CopiesJeu = _copiesJeu;
                r.FlipsCompo = _flipsCompo;
                return r;
            }
        }

        private void OnEvent(TraceEvent data)
        {
            try
            {
                if (data.ProviderGuid != DxgKrnl) return;
                int id = (int)data.ID;
                bool jeu = data.ProcessID == _pid;
                double ms = data.TimeStampRelativeMSec;

                // Flips de jointure : lus QUEL QUE SOIT le processus émetteur (le noyau peut les
                // attribuer au jeu comme au compositeur ; les ignorer côté jeu donnerait un faux « composé »).
                if (id == IndependentFlipInfo || id == MmioFlipInfo || id == MmioFlipMpoInfo)
                {
                    ulong s = id == IndependentFlipInfo ? Lit(data, "SubmitSequence")
                        : id == MmioFlipInfo ? Lit(data, "FlipSubmitSequence")
                        : Lit(data, "FlipSubmitSequence") >> 32;   // MPO : séquence dans les 32 bits hauts
                    lock (_lock) _suivi.FlipDirect(s, ms);
                    return;
                }

                if (jeu)
                {
                    switch (id)
                    {
                        case FlipInfo:
                        case FlipMpoInfo:
                            lock (_lock) _flipsJeu++;
                            break;
                        case BlitInfo:
                            lock (_lock) _copiesJeu++;
                            break;
                        case PresentHistoryStart:
                        case PresentHistoryDetailedStart:
                            ulong model = Lit(data, "Model");
                            lock (_lock)
                            {
                                if (model == 2) _flipsCompo++;
                                else if (model == 3) _copiesJeu++;   // 0 = non initialisé : ignoré (prudence)
                            }
                            break;
                        case QueuePacketStart:
                            if (Lit(data, "bPresent") != 0)
                            {
                                ulong seq = Lit(data, "SubmitSequence");
                                lock (_lock) _suivi.PresentJeu(seq, ms);
                            }
                            break;
                    }
                }
            }
            catch { }
        }

        /// <summary>Champ de charge utile en entier non signé ; absent ou illisible : 0.</summary>
        private static ulong Lit(TraceEvent data, string nom)
        {
            try
            {
                object v = data.PayloadByName(nom);
                if (v == null) return 0;
                if (v is bool) return (bool)v ? 1UL : 0UL;
                if (v is ulong) return (ulong)v;
                if (v is long) return unchecked((ulong)(long)v);
                if (v is uint) return (uint)v;
                if (v is int) return unchecked((ulong)(long)(int)v);
                if (v is IntPtr) return unchecked((ulong)((IntPtr)v).ToInt64());
                if (v is byte) return (byte)v;
                if (v is ushort) return (ushort)v;
                return Convert.ToUInt64(v);
            }
            catch { return 0; }
        }

        private void Cleanup()
        {
            Running = false;
            try { if (_session != null) _session.Dispose(); } catch { }
            _session = null;
        }

        public void Dispose()
        {
            try { if (_session != null) _session.Stop(true); } catch { }
            try { if (_thread != null) _thread.Join(2000); } catch { }
            Cleanup();
        }
    }
}
