using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// CAPTURE DES 30 DERNIÈRES SECONDES — branchement : lecture des trois valeurs du registre, et
    /// « est-ce ONYX qui l'a coupée ? » d'après le journal des réglages appliqués (bt-appliques.txt).
    /// L'écriture passe par le réglage du catalogue clips_fond_on (état d'origine, « Rétablir »).
    /// </summary>
    internal static partial class CaptureFond
    {
        public const string IdReglage = "clips_fond_on";
        public static readonly string[] Bloqueurs = { "gamedvr_off", "gamedvr_policy_off" };

        private static int? Entier(object v)
        {
            if (v is int i) return i;
            if (v is long l) return (int)l;
            return null;
        }

        public static Etat Lit()
        {
            int? strategie = null, applis = null, historique = null;
            try { strategie = Entier(Sys.GetMachine(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR")); } catch { }
            try { applis = Entier(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled")); } catch { }
            try { historique = Entier(Sys.GetUser(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled")); } catch { }
            return Juge(strategie, applis, historique);
        }

        /// <summary>Réglages d'ONYX appliqués qui coupent la capture (gamedvr_off, gamedvr_policy_off).</summary>
        public static List<string> BloqueursAppliques()
        {
            var res = new List<string>();
            foreach (TweakDrift.Entry e in TweakDrift.Load())
                if (Array.IndexOf(Bloqueurs, e.Id) >= 0 && !res.Contains(e.Id)) res.Add(e.Id);
            return res;
        }

        public static bool AppliqueParOnyx()
        {
            return TweakDrift.Load().Exists(e => e.Id == IdReglage);
        }

        /// <summary>Raison de ne pas allumer la capture ; null si rien ne s'y oppose.</summary>
        public static string Refus()
        {
            if (BloqueursAppliques().Count > 0)
                return "« Désactiver Game DVR » est appliqué et coupe toute capture : rétablis-le d'abord.";
            if (Lit() == Etat.BloqueeParStrategie)
                return "une stratégie de Windows (AllowGameDVR = 0) bloque la capture ; ONYX ne l'a pas posée et n'y touche pas.";
            return null;
        }

        /// <summary>L'appli NVIDIA est installée (pour rappeler sa relecture instantanée, sans la piloter).</summary>
        public static bool NvidiaApp()
        {
            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return Directory.Exists(Path.Combine(pf, "NVIDIA Corporation", "NVIDIA app"))
                    || Directory.Exists(Path.Combine(pf, "NVIDIA Corporation", "NVIDIA GeForce Experience"));
            }
            catch { return false; }
        }
    }
}
