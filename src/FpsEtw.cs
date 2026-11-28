using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace BTOptimizer
{
    /// <summary>
    /// Compteur de FPS façon PresentMon : session ETW utilisateur abonnée aux événements
    /// que Windows émet à CHAQUE Present() DirectX (fournisseurs Microsoft-Windows-DXGI
    /// et Microsoft-Windows-D3D9). Aucun overlay, aucune injection, zéro impact sur le
    /// jeu : on compte les images réellement présentées et l'écart entre elles
    /// (frametimes exacts, horloge QPC). Droits administrateur requis.
    /// </summary>
    internal sealed partial class FpsEtw : IDisposable
    {
        // Une session ETW est désignée par son nom : en démarrer une du même nom arrête la
        // précédente. Le journal de jeu mesure donc sous son propre nom, sans couper l'overlay.
        private readonly string SessionName;

        public FpsEtw() : this("BTOptimizer-FPS") { }
        public FpsEtw(string nomSession) { SessionName = nomSession; }

        // Fournisseurs manifestés de Windows (mêmes IDs que PresentMon).
        private static readonly Guid DxgiProvider = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        private static readonly Guid D3D9Provider = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");
        private const int DxgiPresentStart = 42;   // IDXGISwapChain::Present (début)
        private const int D3D9PresentStart = 1;    // IDirect3DDevice9::Present (début)

        private TraceEventSession _session;
        private Thread _thread;
        public bool Running { get; private set; }
        public string LastError { get; private set; }

        public bool Start()
        {
            if (Running) return true;
            try
            {
                try
                {
                    var old = TraceEventSession.GetActiveSession(SessionName);
                    if (old != null) old.Stop(true);
                }
                catch { }

                _session = new TraceEventSession(SessionName);
                _session.EnableProvider(DxgiProvider, TraceEventLevel.Informational);
                _session.EnableProvider(D3D9Provider, TraceEventLevel.Informational);

                _session.Source.Dynamic.All += OnEvent;

                _thread = new Thread(() => { try { _session.Source.Process(); } catch { } });
                _thread.IsBackground = true;
                _thread.Name = "BT-FpsEtw";
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

        private void OnEvent(TraceEvent data)
        {
            int id = (int)data.ID;
            bool present =
                (data.ProviderGuid == DxgiProvider && id == DxgiPresentStart) ||
                (data.ProviderGuid == D3D9Provider && id == D3D9PresentStart);
            if (!present) return;

            int pid = data.ProcessID;
            if (pid <= 0) return;
            double ts = data.TimeStampRelativeMSec;

            Recoit(pid, ts);
        }

        private void Cleanup()
        {
            try { if (_session != null) _session.Dispose(); } catch { }
            _session = null;
        }

        public void Dispose()
        {
            Running = false;
            Cleanup();
            try { if (_thread != null) _thread.Join(2000); } catch { }
            _thread = null;
        }
    }
}
