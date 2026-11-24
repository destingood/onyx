using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BTOptimizer
{
    /// <summary>VEILLE DE JEU — le relevé machine (processus, plein écran). À appeler sur un fil de fond.</summary>
    internal static partial class VeilleJeu
    {
        /// <summary>Banc d'essai : remplace le relevé (ex. un relevé factice lent). Null en production.</summary>
        internal static Func<Releve> PrendPourTest;

        /// <summary>Un seul Process.GetProcesses() : noms distincts, jeu connu, plein écran.</summary>
        public static Releve Prend()
        {
            var r = new Releve();
            r.Noms = NomsProcessus();
            r.JeuConnu = JeuConnuParmi(r.Noms);
            try { r.PleinEcran = Native.IsGameFullscreen(); }
            catch (Exception ex) { JournalTechnique.Echec("VeilleJeu.Prend/pleinEcran", ex); }
            return r;
        }

        /// <summary>Noms distincts des processus en cours (casse ignorée).</summary>
        public static string[] NomsProcessus()
        {
            var noms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Process[] tous = new Process[0];
            try { tous = Process.GetProcesses(); } catch { }
            foreach (Process p in tous)
            {
                try { noms.Add(p.ProcessName); } catch { }
                finally { try { p.Dispose(); } catch { } }
            }
            var res = new string[noms.Count];
            noms.CopyTo(res);
            return res;
        }
    }
}
