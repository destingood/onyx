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
    ///    réversible, mais la manœuvre se fait à l'aveugle si le son a disparu. Coché par les
    ///    presets depuis la 16.03 (un seul PC vérifié) ; l'avertissement reste explicite.
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

        /// <summary>
        /// NOMBRE DE VECTEURS MSI — ce qui décide si « répartir » veut dire quelque chose.
        ///
        /// La politique 5 étale LES MESSAGES d'un périphérique sur plusieurs cœurs. Un
        /// périphérique qui n'expose qu'UN SEUL message n'a rien à étaler : la valeur est écrite,
        /// Windows l'accepte, et il ne se passe rien.
        ///
        /// Constaté sur une machine réelle : carte graphique NVIDIA et carte Wi-Fi toutes deux à
        /// MessageNumberLimit = 1, avec DevicePolicy = 5 posée par ONYX. Résultat mesuré au bout
        /// d'une minute : 92 % des DPC et 93 % du temps DPC sur le CŒUR 0, qui passait 18 % de son
        /// temps en interruptions contre 1,3 % pour les quinze autres. Les contrôleurs NVMe, eux,
        /// annoncent 2048 vecteurs — et leurs interruptions étaient bien réparties.
        ///
        /// ONYX annonçait donc une répartition qui n'avait pas lieu. On lit désormais le nombre de
        /// vecteurs AVANT d'écrire, et on ne prétend répartir que là où c'est possible.
        ///
        /// Valeur absente = non bornée : le périphérique prend ce que le pilote demande.
        /// </summary>
        public static int? Vecteurs(string instanceId)
        {
            return LitDword(EnumBase + instanceId + SousCleMsi, "MessageNumberLimit");
        }

        /// <summary>PUR : « répartir les messages » peut-il avoir un effet avec ce nombre de
        /// vecteurs ? Une valeur absente (null) signifie « non bornée » — donc oui.</summary>
        public static bool PeutRepartir(int? vecteurs)
        {
            return !vecteurs.HasValue || vecteurs.Value > 1;
        }

        public sealed class Peripherique
        {
            public string Nom;
            public string InstanceId;
            public int? Msi;          // 1 = MSI, 0 = ligne, null = non défini
            public int? Politique;    // DevicePolicy, null si absente
            public int? Priorite;     // DevicePriority, null si absente
            public byte[] Masque;     // AssignmentSetOverride, null si absent
            public bool Epingle { get { return CoeurDedie.Epingle(Politique, Masque); } }
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
            // On ne pose la politique que là où elle peut agir. Les périphériques à vecteur unique
            // sont écartés ET signalés : leur écrire « répartis-toi » ne produirait rien, et le
            // dire serait un succès inventé. Voir Vecteurs().
            var eligibles = new List<Peripherique>();
            var uniques = new List<string>();
            foreach (Peripherique p in GrosProducteurs())
            {
                // Une carte épinglée sur un cœur dédié garde sa politique : voir CoeurDedie.ARepartir.
                if (p.Epingle) continue;
                if (CoeurDedie.ARepartir(Vecteurs(p.InstanceId), false)) eligibles.Add(p);
                else uniques.Add(p.Nom);
            }

            Resultat r = Ecrit(eligibles, SousCleAffinite, "DevicePolicy", PolitiqueRepartie, log);

            if (uniques.Count > 0 && log != null)
            {
                var noms = new System.Text.StringBuilder();
                for (int i = 0; i < uniques.Count && i < 3; i++)
                    noms.Append(noms.Length > 0 ? ", " : "").Append(uniques[i]);
                if (uniques.Count > 3) noms.Append('…');
                log(uniques.Count + " périphérique(s) NON répartis — " + noms
                  + " : ils n'exposent qu'un seul vecteur d'interruption, il n'y a rien à étaler. "
                  + "Leurs interruptions resteront sur un seul cœur, et aucun réglage de Windows "
                  + "n'y changera quoi que ce soit.", 2);
            }
            return r;
        }

        /// <summary>Rend la main à Windows sur les gros producteurs.</summary>
        public static Resultat NePlusRepartirProducteurs(Action<string, int> log)
        {
            // Sauf les cartes épinglées : leur politique appartient au réglage « cœur dédié ».
            var cibles = new List<Peripherique>();
            foreach (Peripherique p in GrosProducteurs())
                if (!p.Epingle) cibles.Add(p);
            return Ecrit(cibles, SousCleAffinite, "DevicePolicy", null, log);
        }

        /// <summary>État jugé sur les SEULS périphériques que la répartition peut traiter : ni les
        /// cartes épinglées, ni les périphériques à vecteur unique (jamais écrits par Apply — les
        /// attendre faisait afficher « non appliqué » pour toujours).</summary>
        public static bool? EtatRepartitionProducteurs()
        {
            var concernes = new List<Peripherique>();
            foreach (Peripherique p in GrosProducteurs())
                if (CoeurDedie.ARepartir(Vecteurs(p.InstanceId), p.Epingle)) concernes.Add(p);
            return Etat(concernes, delegate (Peripherique p) { return p.Politique == PolitiqueRepartie; });
        }

        // ------------------------------------------------------------------
        //  Carte graphique sur un cœur dédié (décision pure : CoeurDedie.cs)
        // ------------------------------------------------------------------

        /// <summary>Cartes graphiques à épingler : les cartes dédiées, sinon la puce intégrée.</summary>
        public static List<Peripherique> CartesAEpingler()
        {
            List<Peripherique> toutes = Graphiques();
            var noms = new List<string>();
            foreach (Peripherique p in toutes) noms.Add(p.Nom);
            var res = new List<Peripherique>();
            foreach (int i in CoeurDedie.Cartes(noms)) res.Add(toutes[i]);
            return res;
        }

        /// <summary>Masque du cœur choisi pour cette machine, null si la topologie ne permet aucun choix.</summary>
        private static byte[] MasqueCoeurDedie(out int logique)
        {
            logique = CoeurDedie.Choisit(Topologie.Lit());
            return CoeurDedie.Masque(logique);
        }

        /// <summary>Envoie les interruptions de la carte graphique sur un seul cœur, choisi d'après
        /// le processeur. Le mécanisme d'interruption et le pilote ne sont pas touchés.</summary>
        public static Resultat EpinglerGraphique(Action<string, int> log)
        {
            var r = new Resultat();
            int logique;
            byte[] masque = MasqueCoeurDedie(out logique);
            if (masque == null)
            {
                if (log != null) log("Processeur non reconnu (un seul cœur, ou plus de 64 processeurs logiques) : "
                                   + "aucun cœur choisi, rien n'est écrit.", 2);
                return r;
            }
            foreach (Peripherique p in CartesAEpingler())
            {
                r.Total++;
                try
                {
                    string sousCle = EnumBase + p.InstanceId + SousCleAffinite;
                    EtatOrigineMachine.AvantCle(Registry.LocalMachine, sousCle);
                    using (RegistryKey k = Registry.LocalMachine.CreateSubKey(sousCle, true))
                    {
                        if (k == null) { r.Refuse++; continue; }
                        EtatOrigineMachine.AvantValeur(k, "AssignmentSetOverride");
                        EtatOrigineMachine.AvantValeur(k, "DevicePolicy");
                        k.SetValue("AssignmentSetOverride", masque, RegistryValueKind.Binary);
                        k.SetValue("DevicePolicy", CoeurDedie.PolitiqueChoisie, RegistryValueKind.DWord);
                        r.Ok++;
                    }
                }
                catch { r.Refuse++; }
            }
            if (log != null && r.Total > 0)
            {
                if (r.Refuse == 0) log(r.Ok + " carte(s) graphique(s) sur le processeur logique " + logique
                                     + ". Effet au prochain redémarrage.", 1);
                else log(r.Ok + "/" + r.Total + " carte(s) traitée(s) — " + r.Refuse
                       + " ont refusé l'écriture (clés protégées par le système).", 2);
            }
            return r;
        }

        /// <summary>Rend la main à Windows sur les cartes épinglées (une carte seulement répartie n'est pas touchée).</summary>
        public static Resultat NePlusEpinglerGraphique(Action<string, int> log)
        {
            var r = new Resultat();
            foreach (Peripherique p in CartesAEpingler())
            {
                if (!p.Epingle) continue;
                r.Total++;
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(EnumBase + p.InstanceId + SousCleAffinite, true))
                    {
                        if (k == null) { r.Refuse++; continue; }
                        EtatOrigineMachine.AvantValeur(k, "AssignmentSetOverride");
                        EtatOrigineMachine.AvantValeur(k, "DevicePolicy");
                        k.DeleteValue("AssignmentSetOverride", false);
                        k.DeleteValue("DevicePolicy", false);
                        r.Ok++;
                    }
                }
                catch { r.Refuse++; }
            }
            if (log != null && r.Total > 0)
                log(r.Ok + "/" + r.Total + " carte(s) graphique(s) rendue(s) à Windows. Effet au prochain redémarrage.", r.Refuse == 0 ? 1 : 2);
            return r;
        }

        /// <summary>true = chaque carte visée est épinglée sur le cœur choisi pour cette machine ;
        /// null = aucune carte, ou processeur non reconnu : on ne conclut rien.</summary>
        public static bool? EtatEpingleGraphique()
        {
            int logique;
            byte[] attendu = MasqueCoeurDedie(out logique);
            if (attendu == null) return null;
            return Etat(CartesAEpingler(), delegate (Peripherique p)
            {
                return p.Epingle && CoeurDedie.MemeMasque(p.Masque, attendu);
            });
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
                            Priorite = LitDword(EnumBase + id + SousCleAffinite, "DevicePriority"),
                            Masque = LitBinaire(EnumBase + id + SousCleAffinite, "AssignmentSetOverride")
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

        private static byte[] LitBinaire(string sousCle, string nom)
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(sousCle, false))
                    return k == null ? null : k.GetValue(nom) as byte[];
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
                    EtatOrigineMachine.AvantCle(Registry.LocalMachine, EnumBase + p.InstanceId + sousCle);
                    using (RegistryKey k = Registry.LocalMachine.CreateSubKey(EnumBase + p.InstanceId + sousCle, true))
                    {
                        if (k == null) { r.Refuse++; continue; }
                        EtatOrigineMachine.AvantValeur(k, nom);
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
