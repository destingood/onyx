using System;
using System.IO;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Smart App Control (SAC) — le filtre de réputation de Windows 11 22H2+. Il bloque au
    /// lancement les exécutables non signés ou « sans réputation » : c'est LUI qui refuse une
    /// application fraîchement compilée ou téléchargée sur un petit site (« stratégie de contrôle
    /// d'application », 0x800711C7).
    ///
    /// Lecture seule par défaut. Un changement écrit UNE valeur DWORD documentée
    /// (HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState) —
    /// exactement ce que font les .reg qui circulent — après sauvegarde .reg de l'état d'origine.
    ///
    /// HONNÊTETÉ (position officielle de Microsoft) : ÉTEINDRE SAC EST À SENS UNIQUE. Une fois
    /// coupé, Windows ne le rallume plus tant que le système n'est pas réinstallé/réinitialisé ;
    /// réécrire 1 ou 2 dans le registre ne le ré-arme pas. L'app le dit au lieu de le cacher.
    /// </summary>
    internal static class SmartAppControl
    {
        public enum State { Unknown = -1, Off = 0, On = 1, Evaluation = 2 }

        private const string Key = @"SYSTEM\CurrentControlSet\Control\CI\Policy";
        private const string ValueName = "VerifiedAndReputablePolicyState";
        private const int MinBuild = 22621;   // Windows 11 22H2 : première version à embarquer SAC

        /// <summary>Windows expose-t-il SAC sur cette machine (version ET valeur présente) ?</summary>
        public static bool Available
        {
            get
            {
                try
                {
                    if (Environment.OSVersion.Version.Build < MinBuild) return false;
                    return Sys.GetMachine(Key, ValueName) is int;
                }
                catch { return false; }
            }
        }

        public static State Read()
        {
            try
            {
                object v = Sys.GetMachine(Key, ValueName);
                if (!(v is int)) return State.Unknown;
                int i = (int)v;
                return i == 0 ? State.Off : i == 1 ? State.On : i == 2 ? State.Evaluation : State.Unknown;
            }
            catch { return State.Unknown; }
        }

        public static string Label(State s)
        {
            switch (s)
            {
                case State.On: return "ACTIVÉ";
                case State.Evaluation: return "MODE ÉVALUATION";
                case State.Off: return "DÉSACTIVÉ";
                default: return "NON DISPONIBLE";
            }
        }

        /// <summary>Ce que l'état veut dire concrètement pour le lancement des applications.</summary>
        public static string Explain(State s)
        {
            switch (s)
            {
                case State.On:
                    return "Windows bloque les applications non signées ou sans réputation établie. "
                         + "C'est l'état le plus sûr — et c'est lui qui refuse une application que tu viens de compiler.";
                case State.Evaluation:
                    return "Windows observe ton usage sans rien bloquer, pour décider s'il peut s'activer tout seul. "
                         + "Rien n'est refusé au lancement dans cet état.";
                case State.Off:
                    return "Aucun filtrage de réputation : les applications se lancent normalement, y compris "
                         + "celles que tu compiles. Ton antivirus, lui, continue de travailler normalement.";
                default:
                    return "Smart App Control n'existe pas sur cette version de Windows (il demande Windows 11 22H2 "
                         + "ou plus récent), ou Windows ne l'a pas initialisé sur ce PC. Rien à régler ici.";
            }
        }

        /// <summary>Vrai si une réactivation a une chance d'aboutir : uniquement quand SAC n'a PAS
        /// déjà été coupé (Microsoft : la coupure est définitive jusqu'à réinstallation).</summary>
        public static bool CanReEnable { get { return Read() != State.Off; } }

        /// <summary>Sauvegarde .reg de l'état courant sur le Bureau (même filet que les autres
        /// modifications de l'app). Renvoie le chemin écrit, ou null.</summary>
        public static string Backup()
        {
            try
            {
                State s = Read();
                if (s == State.Unknown) return null;
                string path = Path.Combine(Sys.BackupDesktop,
                    "ONYX-smart-app-control-avant-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".reg");
                string body = "Windows Registry Editor Version 5.00\r\n\r\n"
                            + "[HKEY_LOCAL_MACHINE\\" + Key + "]\r\n"
                            + "\"" + ValueName + "\"=dword:" + ((int)s).ToString("x8") + "\r\n";
                File.WriteAllText(path, body, new System.Text.UTF8Encoding(false));
                return path;
            }
            catch { return null; }
        }

        /// <summary>Écrit l'état demandé (après sauvegarde). Le changement ne prend effet qu'au
        /// REDÉMARRAGE. Renvoie vrai si l'écriture a réussi.</summary>
        public static bool Apply(State target, Action<string, int> log)
        {
            if (target == State.Unknown) return false;
            Action<string, int> L = log ?? delegate { };
            State before = Read();
            if (before == target) { L("Smart App Control est déjà sur « " + Label(target) + " ».", 0); return true; }

            string bak = Backup();
            if (bak != null) L("Sauvegarde de l'état d'origine : " + bak, 0);

            try
            {
                Sys.SetMachine(Key, ValueName, (int)target, RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                L("Échec de l'écriture (droits administrateur requis) : " + ex.Message, 2);
                return false;
            }

            State after = Read();
            if (after != target)
            {
                L("Windows a refusé le changement (la valeur est revenue à « " + Label(after) + " »).", 2);
                return false;
            }

            L("Smart App Control → « " + Label(target) + " ». Le changement prend effet au REDÉMARRAGE.", 1);
            if (target == State.Off)
                L("Rappel : d'après Microsoft, une fois coupé il ne se rallume qu'en réinstallant Windows.", 2);
            return true;
        }
    }
}
