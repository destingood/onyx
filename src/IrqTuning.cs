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
            public int? Priorite;     // DevicePriority, null si absente
        }

        /// <summary>Un nom désigne-t-il un contrôleur audio ? PUR (FR et EN).</summary>
        public static bool EstAudio(string nom)
        {
            if (string.IsNullOrEmpty(nom)) return false;
            string n = nom.ToLowerInvariant();
            return n.Contains("high definition audio") || n.Contains("haute définition audio")
                || n.Contains("audio controller") || n.Contains("contrôleur audio");
        }

        /// <summary>
        /// PUR : ce nom désigne-t-il un GROS PRODUCTEUR D'INTERRUPTIONS ?
        ///
        /// L'audio est traité à part (réglage dédié). Ici : la carte graphique, le réseau, le
        /// stockage et les contrôleurs USB. Ce ne sont pas des choix arbitraires — ce sont les
        /// quatre familles qui remontent en tête de toutes les mesures de latence noyau. Sur la
        /// machine de référence, le pilote graphique produit à lui seul 129 424 travaux différés,
        /// deux fois plus que le suivant, et il n'avait AUCUNE politique d'affinité : ses
        /// interruptions tombaient donc là où Windows les mettait — en pratique, le cœur 0, celui
        /// où tourne le fil principal du jeu.
        /// </summary>
        public static bool EstGrosProducteur(string nom)
        {
            if (string.IsNullOrEmpty(nom)) return false;
            if (EstAudio(nom)) return false;   // couvert par le réglage audio, on ne double pas
            string n = nom.ToLowerInvariant();
            return n.Contains("nvidia") || n.Contains("radeon") || n.Contains("geforce")
                || n.Contains("intel(r) arc") || n.Contains("graphics")
                || n.Contains("ethernet") || n.Contains("wi-fi") || n.Contains("wifi")
                || n.Contains("wireless") || n.Contains("réseau") || n.Contains("network")
                // « nvme » NE SUFFIT PAS : Windows nomme ces contrôleurs « NVM Express », avec une
                // espace. Sans cette seconde forme, le SSD principal de la machine — souvent le
                // deuxième producteur d'interruptions — était silencieusement ignoré.
                || n.Contains("nvme") || n.Contains("nvm express")
                || n.Contains("ahci") || n.Contains("sata") || n.Contains("raid")
                || n.Contains("xhci") || n.Contains("usb");
        }

        /// <summary>Gros producteurs d'interruptions présents, et leur état.</summary>
        public static List<Peripherique> GrosProducteurs()
        {
            return Recense(EstGrosProducteur);
        }

        /// <summary>Répartit les interruptions des gros producteurs sur tous les cœurs.
        /// SANS RISQUE : le mécanisme d'interruption n'est pas modifié — c'est exactement le même
        /// geste que celui déjà appliqué aux contrôleurs audio.</summary>
        public static Resultat RepartirProducteurs(Action<string, int> log)
        {
            return Ecrit(GrosProducteurs(), SousCleAffinite, "DevicePolicy", PolitiqueRepartie, log);
        }

        /// <summary>Rend la main à Windows sur les gros producteurs.</summary>
        public static Resultat NePlusRepartirProducteurs(Action<string, int> log)
        {
            return Ecrit(GrosProducteurs(), SousCleAffinite, "DevicePolicy", null, log);
        }

        public static bool? EtatRepartitionProducteurs()
        {
            return Etat(GrosProducteurs(), delegate (Peripherique p) { return p.Politique == PolitiqueRepartie; });
        }

        /// <summary>Priorité d'interruption « haute » (IRQ_PRIORITY_HIGH).</summary>
        public const int PrioriteHaute = 3;

        /// <summary>
        /// PRIORITÉ D'INTERRUPTION — l'ordre dans lequel le noyau sert ce qui arrive en même temps.
        ///
        /// Répartir dit OÙ une interruption est traitée ; la priorité dit QUAND, lorsque plusieurs
        /// se présentent ensemble. Mettre la carte graphique en haut de la file réduit le temps
        /// qu'elle passe à attendre son tour derrière un contrôleur qui n'a rien d'urgent à dire.
        ///
        /// Ce n'est pas un tour de passe-passe : la valeur est celle qu'attend le noyau, à côté de
        /// la politique d'affinité, et le pilote n'est pas touché. Mais soyons honnête sur ce qu'on
        /// en sait : c'est un réglage d'ORDONNANCEMENT, pas de durée. Il ne raccourcit aucune
        /// exécution — il ne fait qu'éviter des attentes. Le gain se mesure sur le pire temps, ou ne
        /// se mesure pas du tout.
        /// </summary>
        public static Resultat PrioriserGraphique(Action<string, int> log)
        {
            return Ecrit(Graphiques(), SousCleAffinite, "DevicePriority", PrioriteHaute, log);
        }

        public static Resultat NePlusPrioriserGraphique(Action<string, int> log)
        {
            return Ecrit(Graphiques(), SousCleAffinite, "DevicePriority", null, log);
        }

        /// <summary>PUR : ce nom désigne-t-il une carte graphique ?</summary>
        public static bool EstGraphique(string nom)
        {
            if (string.IsNullOrEmpty(nom)) return false;
            string n = nom.ToLowerInvariant();
            return n.Contains("nvidia") || n.Contains("geforce") || n.Contains("radeon")
                || n.Contains("intel(r) arc") || n.Contains("graphics");
        }

        public static List<Peripherique> Graphiques() { return Recense(EstGraphique); }

        public static bool? EtatPrioriteGraphique()
        {
            return Etat(Graphiques(), delegate (Peripherique p) { return p.Priorite == PrioriteHaute; });
        }

        /// <summary>Contrôleurs audio PCI présents et leur état d'interruption.</summary>
        public static List<Peripherique> ControleursAudio()
        {
            return Recense(EstAudio);
        }

        /// <summary>Recense les périphériques PCI retenus par <paramref name="retenir"/>.</summary>
        private static List<Peripherique> Recense(Func<string, bool> retenir)
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
                        if (retenir == null || !retenir(nom)) continue;
                        list.Add(new Peripherique
                        {
                            Nom = nom,
                            InstanceId = id,
                            Msi = LitDword(EnumBase + id + SousCleMsi, "MSISupported"),
                            Politique = LitDword(EnumBase + id + SousCleAffinite, "DevicePolicy"),
                            Priorite = LitDword(EnumBase + id + SousCleAffinite, "DevicePriority")
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
