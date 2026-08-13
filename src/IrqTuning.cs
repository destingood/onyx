using System;
using System.Collections.Generic;
using System.Management;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// INTERRUPTIONS EMPILÉES SUR UN SEUL CŒUR — la latence qu'aucun réglage de jeu ne corrige.
    ///
    /// Mesure faite sur une machine réelle : cœur 0 à 15,8 % de temps DPC et 8 % d'interruptions,
    /// pendant que les quinze autres cœurs étaient à zéro. Or le cœur 0 est aussi celui où tourne
    /// le thread principal du jeu. Le rendu et les pilotes se disputaient le même cœur — ça ne se
    /// voit pas sur la moyenne d'utilisation, ça se voit sur les à-coups.
    ///
    /// Deux leviers, de risque très différent :
    ///
    ///  • RÉPARTIR (politique d'affinité 5 = « étaler les messages sur tous les processeurs »).
    ///    Sans risque : on ne change pas le mécanisme d'interruption, on demande seulement à
    ///    Windows de ne pas tout poser sur le même cœur. C'est déjà ce que fait le stockage de la
    ///    machine de test.
    ///
    ///  • PASSER EN MSI (interruptions par message au lieu de par ligne). Gain supérieur, mais
    ///    RISQUE RÉEL sur l'audio : certains pilotes gèrent mal MSI et démarrent sans son. C'est
    ///    réversible, mais la manœuvre se fait à l'aveugle si le son a disparu. Jamais dans un
    ///    preset, et l'avertissement est explicite.
    ///
    /// Comme pour la veille USB, ces valeurs vivent sous HKLM\SYSTEM\CurrentControlSet\Enum, dont
    /// les permissions appartiennent souvent à SYSTEM : on TENTE, on COMPTE, et on rapporte le vrai
    /// résultat — jamais un succès supposé.
    /// </summary>
    internal static class IrqTuning
    {
        private const string EnumBase = @"SYSTEM\CurrentControlSet\Enum\";
        private const string SousCleAffinite = @"\Device Parameters\Interrupt Management\Affinity Policy";
        private const string SousCleMsi = @"\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";

        /// <summary>« Étaler les messages sur tous les processeurs » — la valeur qui répartit.</summary>
        public const int PolitiqueRepartie = 5;

        public sealed class Peripherique
        {
            public string Nom;
            public string InstanceId;
            public int? Msi;          // 1 = MSI, 0 = ligne, null = non défini
            public int? Politique;    // DevicePolicy, null si absente
        }

        /// <summary>Un nom désigne-t-il un contrôleur audio ? PUR (FR et EN).</summary>
        public static bool EstAudio(string nom)
        {
            if (string.IsNullOrEmpty(nom)) return false;
            string n = nom.ToLowerInvariant();
            return n.Contains("high definition audio") || n.Contains("haute définition audio")
                || n.Contains("audio controller") || n.Contains("contrôleur audio");
        }

        /// <summary>Contrôleurs audio PCI présents et leur état d'interruption.</summary>
        public static List<Peripherique> ControleursAudio()
        {
            var list = new List<Peripherique>();
            try
            {
                using (var s = new ManagementObjectSearcher(
                    "SELECT Name, PNPDeviceID, Status FROM Win32_PnPEntity WHERE Status = 'OK'"))
                    foreach (ManagementObject mo in s.Get())
                    {
                        string nom = Convert.ToString(mo["Name"]) ?? "";
                        string id = Convert.ToString(mo["PNPDeviceID"]) ?? "";
                        if (!id.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!EstAudio(nom)) continue;
                        list.Add(new Peripherique
                        {
                            Nom = nom,
                            InstanceId = id,
                            Msi = LitDword(EnumBase + id + SousCleMsi, "MSISupported"),
                            Politique = LitDword(EnumBase + id + SousCleAffinite, "DevicePolicy")
                        });
                    }
            }
            catch { }
            return list;
        }

        private static int? LitDword(string sousCle, string nom)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(sousCle, false))
                {
                    if (k == null) return null;
                    object v = k.GetValue(nom);
                    if (v == null) return null;
                    return Convert.ToInt32(v);
                }
            }
            catch { return null; }
        }

        /// <summary>Résultat honnête d'une écriture sur plusieurs périphériques.</summary>
        public sealed class Resultat
        {
            public int Total;
            public int Ok;
            public int Refuse;
        }

        private static Resultat Ecrit(List<Peripherique> cibles, string sousCle, string nom, int? valeur, Action<string, int> log)
        {
            var r = new Resultat();
            foreach (Peripherique p in cibles)
            {
                r.Total++;
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.CreateSubKey(EnumBase + p.InstanceId + sousCle, true))
                    {
                        if (k == null) { r.Refuse++; continue; }
                        if (valeur.HasValue) k.SetValue(nom, valeur.Value, RegistryValueKind.DWord);
                        else { try { k.DeleteValue(nom, false); } catch { } }
                        r.Ok++;
                    }
                }
                catch { r.Refuse++; }
            }
            if (log != null && r.Total > 0)
            {
                if (r.Refuse == 0) log(r.Ok + " contrôleur(s) audio traité(s). Effet au prochain redémarrage.", 1);
                else log(r.Ok + "/" + r.Total + " contrôleur(s) traités — " + r.Refuse
                       + " ont refusé l'écriture (clés protégées par le système).", 2);
            }
            return r;
        }

        /// <summary>Demande à Windows de répartir les interruptions sur tous les cœurs. SANS RISQUE :
        /// le mécanisme d'interruption n'est pas modifié.</summary>
        public static Resultat Repartir(Action<string, int> log)
        {
            return Ecrit(ControleursAudio(), SousCleAffinite, "DevicePolicy", PolitiqueRepartie, log);
        }

        /// <summary>Rend la main à Windows (suppression de la politique imposée).</summary>
        public static Resultat NePlusRepartir(Action<string, int> log)
        {
            return Ecrit(ControleursAudio(), SousCleAffinite, "DevicePolicy", null, log);
        }

        /// <summary>Passe les contrôleurs audio en interruptions par message. RISQUÉ : certains
        /// pilotes audio démarrent sans son ensuite.</summary>
        public static Resultat ActiverMsi(Action<string, int> log)
        {
            return Ecrit(ControleursAudio(), SousCleMsi, "MSISupported", 1, log);
        }

        public static Resultat DesactiverMsi(Action<string, int> log)
        {
            return Ecrit(ControleursAudio(), SousCleMsi, "MSISupported", 0, log);
        }

        /// <summary>
        /// Décision PURE : l'état est-il celui demandé ? true = tous conformes, false = au moins un
        /// ne l'est pas, null = rien de lisible — dans ce cas on ne conclut RIEN.
        /// </summary>
        public static bool? Etat(List<Peripherique> liste, Func<Peripherique, bool> conforme)
        {
            if (liste == null || liste.Count == 0 || conforme == null) return null;
            foreach (Peripherique p in liste)
                if (!conforme(p)) return false;
            return true;
        }

        public static bool? EtatRepartition()
        {
            return Etat(ControleursAudio(), delegate (Peripherique p) { return p.Politique == PolitiqueRepartie; });
        }

        public static bool? EtatMsi()
        {
            return Etat(ControleursAudio(), delegate (Peripherique p) { return p.Msi == 1; });
        }
    }
}
