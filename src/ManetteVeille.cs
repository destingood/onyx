using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace BTOptimizer
{
    /// <summary>
    /// Lecture de la manette pour le mode manette (<see cref="Manette"/>). Un fil d'arrière-plan
    /// lit XInput 30 fois par seconde, et SEULEMENT quand <see cref="Ecoute"/> est vrai (ONYX au
    /// premier plan, page Mes jeux affichée) : en jeu, ONYX ne lit rien et ne réagit à rien. Sinon
    /// le fil dort. Lecture seule : aucun pilote, aucune injection, juste l'API XInput de Windows.
    /// </summary>
    internal static class ManetteVeille
    {
        [DllImport("xinput1_4.dll")] private static extern int XInputGetState(int dwUserIndex, out XINPUT_STATE state);
        [StructLayout(LayoutKind.Sequential)] private struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }
        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons; public byte bLeftTrigger; public byte bRightTrigger;
            public short sThumbLX; public short sThumbLY; public short sThumbRX; public short sThumbRY;
        }

        private const int PeriodeMs = 33;
        // Microsoft déconseille d'interroger à chaque image une place sans manette (appel coûteux) :
        // sans manette, on ne recherche qu'une fois toutes les 2 s.
        private const int RechercheMs = 2000;

        /// <summary>Geste reçu, levé sur le fil de la manette : l'abonné repasse sur le sien.</summary>
        public static event Action<Manette.Geste> Geste;

        private static readonly AutoResetEvent Reveil = new AutoResetEvent(false);
        private static volatile bool _ecoute;
        private static Thread _fil;
        private static bool _absente;   // XInput introuvable (xinput1_4.dll) : on n'insiste pas

        public static bool Ecoute
        {
            get { return _ecoute; }
            set
            {
                if (_ecoute == value) return;
                _ecoute = value;
                if (value && _fil == null && !_absente)
                {
                    _fil = new Thread(Boucle) { IsBackground = true, Name = "ONYX manette", Priority = ThreadPriority.BelowNormal };
                    _fil.Start();
                }
                Reveil.Set();
            }
        }

        private static void Boucle()
        {
            var montre = Stopwatch.StartNew();
            var etat = new Manette.Etat();
            int place = -1;
            long recherche = 0;
            while (true)
            {
                if (!_ecoute)
                {
                    etat = new Manette.Etat();   // au retour, l'état présent sert de référence
                    Reveil.WaitOne();
                    continue;
                }
                long ms = montre.ElapsedMilliseconds;
                XINPUT_STATE s = default(XINPUT_STATE);
                bool lu = false;
                try
                {
                    if (place >= 0) lu = XInputGetState(place, out s) == 0;
                    if (!lu && ms >= recherche)
                    {
                        place = -1; recherche = ms + RechercheMs;
                        for (int i = 0; i < 4 && !lu; i++) if (XInputGetState(i, out s) == 0) { place = i; lu = true; }
                        if (lu) etat = new Manette.Etat();
                    }
                }
                catch (Exception ex)   // DllNotFoundException, EntryPointNotFoundException
                {
                    _absente = true; _fil = null; _ecoute = false;
                    JournalTechnique.Echec("manette XInput", ex);
                    return;
                }
                if (lu)
                {
                    List<Manette.Geste> g = Manette.Avance(etat, s.Gamepad.wButtons, s.Gamepad.sThumbLX, s.Gamepad.sThumbLY, ms);
                    Action<Manette.Geste> h = Geste;
                    if (h != null) foreach (Manette.Geste x in g) { try { h(x); } catch { } }
                }
                Reveil.WaitOne(lu ? PeriodeMs : 250);
            }
        }
    }
}
