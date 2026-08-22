using System;
using System.Runtime.InteropServices;

namespace BTOptimizer
{
    /// <summary>
    /// DÉCALAGE DE FRÉQUENCE GPU (+MHz cœur et mémoire) — le curseur central d'Afterburner.
    ///
    /// nvidia-smi ne sait PAS faire ça : il verrouille des fréquences existantes, il ne décale pas
    /// la courbe. Le décalage passe par NVAPI, la bibliothèque du pilote — c'est le chemin
    /// qu'empruntent Afterburner et Precision X1. La DLL n'exporte qu'une seule fonction,
    /// nvapi_QueryInterface, qui rend un pointeur par identifiant numérique ; les structures ne sont
    /// pas documentées publiquement.
    ///
    /// COMMENT LA DISPOSITION MÉMOIRE A ÉTÉ VÉRIFIÉE, plutôt que supposée. Une sonde en LECTURE
    /// SEULE a été passée sur une RTX 4080 SUPER (pilote 596.49) : le pilote a rendu 5 états de
    /// performance (P0, P2, P3, P5, P8 — exactement le jeu attendu), 2 domaines d'horloge par état,
    /// et des plages de décalage cohérentes (cœur −1000…+1000 MHz, mémoire −1000…+3000 MHz) que
    /// SEULE une structure correctement alignée peut produire. Des tailles fausses donneraient du
    /// bruit, pas ça.
    ///
    /// CE QUI RESTE INCERTAIN, ET QUI EST DIT PLUTÔT QUE CACHÉ. L'ÉCRITURE n'a pas pu être
    /// confirmée : testée hors élévation, elle a été refusée — comme l'a été au même moment
    /// nvidia-smi --power-limit, dont on sait qu'il fonctionne une fois élevé. ONYX tourne en
    /// administrateur, donc le cas normal est le bon ; mais tant qu'un retour de terrain ne l'a pas
    /// montré, le code REND LE CODE D'ERREUR BRUT du pilote au lieu de prétendre savoir pourquoi.
    ///
    /// SUR LE RISQUE. Un décalage de fréquence n'est pas un réglage de confort : trop haut, la carte
    /// produit des artefacts, plante le pilote, ou fait tomber le jeu. Rien ici n'est appliqué sans
    /// action explicite, aucun décalage n'entre dans un préréglage, et la remise à zéro est un seul
    /// clic. Le décalage n'est PAS persistant : il disparaît au redémarrage — c'est une sécurité,
    /// pas un manque.
    /// </summary>
    internal static class NvOverclock
    {
        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr QueryInterface(uint id);

        private delegate int FnInit();
        private delegate int FnEnum([Out] IntPtr[] handles, out int count);
        private delegate int FnPstates(IntPtr gpu, IntPtr info);

        // Identifiants NVAPI (stables d'un pilote à l'autre : c'est un hachage du nom de fonction).
        private const uint IdInitialize = 0x0150E828;
        private const uint IdEnumGpus = 0xE5AC921F;
        private const uint IdGetPstates20 = 0x6FF81213;
        private const uint IdSetPstates20 = 0x0F4DAE6B;

        // Tailles VÉRIFIÉES par la sonde décrite plus haut.
        private const int MaxPstates = 16, MaxClocks = 8, MaxBaseVolt = 4, MaxOv = 4;
        private const int TailleClock = 44;      // domainId, typeId, éditable, delta{val,min,max}, union
        private const int TailleVolt = 24;
        private const int TaillePstate = 8 + TailleClock * MaxClocks + TailleVolt * MaxBaseVolt;   // 456
        private const int Entete = 20;
        private const int TailleV1 = Entete + TaillePstate * MaxPstates;                            // 7316
        private const int TailleV2 = TailleV1 + 4 + (4 + 4 + 4 + 12) * MaxOv;                       // 7416

        private const int DomaineCoeur = 0, DomaineMemoire = 4;
        private const int TypeSimple = 1;

        private static bool _init;
        private static readonly object Verrou = new object();

        /// <summary>Ce que la carte accepte. Tout à zéro + Disponible=false = rien de lisible.</summary>
        public sealed class Etat
        {
            public bool Disponible;        // NVAPI répond et la carte expose des décalages
            public bool Modifiable;        // le pilote déclare la table éditable
            public int CoeurMhz, MemoireMhz;          // décalages actuellement posés
            public int CoeurMin, CoeurMax;            // plage acceptée, lue sur la carte
            public int MemoireMin, MemoireMax;
            public bool TensionExposee;    // « +mV » atteignable ? (mesuré : non sur RTX 40)
            public string Motif;           // pourquoi c'est indisponible, le cas échéant
        }

        // ------------------------------------------------------------------ pur

        /// <summary>Ramène un décalage dans la plage annoncée par la carte. Une plage absurde
        /// (non lue) laisse la valeur passer : on ne fabrique pas de limite.</summary>
        public static int Borne(int demande, int min, int max)
        {
            if (min == 0 && max == 0) return demande;
            if (min > max) return demande;
            if (demande < min) return min;
            if (demande > max) return max;
            return demande;
        }

        /// <summary>Traduction PURE d'un code NVAPI. Les codes NON IDENTIFIÉS AVEC CERTITUDE sont
        /// rendus tels quels : inventer un libellé rassurant sur un code inconnu serait pire que de
        /// montrer le nombre.</summary>
        public static string Message(int code)
        {
            switch (code)
            {
                case 0: return null;
                case -5: return "Argument refusé par le pilote.";
                case -8: return "Carte graphique non reconnue par le pilote.";
                case -9: return "Version de structure incompatible avec ce pilote.";
                case -104: return "Cette carte n'expose pas le décalage de fréquence.";
                case -136: return "Accès refusé par le pilote (droits administrateur nécessaires).";
                default:
                    return "Le pilote a refusé le décalage (code NVAPI " + code + "). "
                         + "Si tu utilises déjà Afterburner ou Precision X1, ferme-le : "
                         + "deux outils ne peuvent pas piloter la courbe en même temps.";
            }
        }

        // ------------------------------------------------------------------ matériel

        private static T Fn<T>(uint id) where T : class
        {
            try
            {
                IntPtr p = QueryInterface(id);
                if (p == IntPtr.Zero) return null;
                return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
            }
            catch { return null; }
        }

        private static bool Init()
        {
            lock (Verrou)
            {
                if (_init) return true;
                var f = Fn<FnInit>(IdInitialize);
                if (f == null) return false;
                try { _init = f() == 0; }
                catch { _init = false; }
                return _init;
            }
        }

        private static IntPtr Carte()
        {
            var f = Fn<FnEnum>(IdEnumGpus);
            if (f == null) return IntPtr.Zero;
            var h = new IntPtr[64];
            int n;
            try { if (f(h, out n) != 0 || n == 0) return IntPtr.Zero; }
            catch { return IntPtr.Zero; }
            return h[0];
        }

        /// <summary>Lit les décalages posés et les plages acceptées.</summary>
        public static Etat Lire()
        {
            var e = new Etat();
            if (!Init()) { e.Motif = "NVAPI indisponible (pas de carte NVIDIA, ou pilote trop ancien)."; return e; }
            IntPtr gpu = Carte();
            if (gpu == IntPtr.Zero) { e.Motif = "Aucune carte NVIDIA détectée."; return e; }
            var get = Fn<FnPstates>(IdGetPstates20);
            if (get == null) { e.Motif = "Ce pilote n'expose pas la table des fréquences."; return e; }

            IntPtr buf = Marshal.AllocHGlobal(TailleV2);
            try
            {
                Efface(buf, TailleV2);
                Marshal.WriteInt32(buf, 0, unchecked((int)((uint)TailleV2 | (2u << 16))));
                int st = get(gpu, buf);
                if (st != 0)
                {
                    Efface(buf, TailleV2);
                    Marshal.WriteInt32(buf, 0, unchecked((int)((uint)TailleV1 | (1u << 16))));
                    st = get(gpu, buf);
                }
                if (st != 0) { e.Motif = Message(st); return e; }

                e.Modifiable = (Marshal.ReadInt32(buf, 4) & 1) != 0;
                int nbP = Marshal.ReadInt32(buf, 8);
                int nbC = Marshal.ReadInt32(buf, 12);
                e.TensionExposee = Marshal.ReadInt32(buf, 16) > 0;
                if (nbP < 1 || nbP > MaxPstates || nbC < 1 || nbC > MaxClocks)
                {
                    e.Motif = "Table des fréquences illisible : aucun décalage ne sera tenté.";
                    return e;   // cohérence FAUSSE → on n'écrit RIEN. Mieux vaut ne rien offrir.
                }

                // Seul P0 (pleine charge) porte des plages modifiables ; c'est celui qui compte en jeu.
                for (int p = 0; p < nbP; p++)
                {
                    int po = Entete + p * TaillePstate;
                    if (Marshal.ReadInt32(buf, po) != 0) continue;     // pstateId != P0
                    for (int c = 0; c < nbC; c++)
                    {
                        int co = po + 8 + c * TailleClock;
                        int domaine = Marshal.ReadInt32(buf, co);
                        int val = Marshal.ReadInt32(buf, co + 12) / 1000;
                        int min = Marshal.ReadInt32(buf, co + 16) / 1000;
                        int max = Marshal.ReadInt32(buf, co + 20) / 1000;
                        if (domaine == DomaineCoeur) { e.CoeurMhz = val; e.CoeurMin = min; e.CoeurMax = max; }
                        else if (domaine == DomaineMemoire) { e.MemoireMhz = val; e.MemoireMin = min; e.MemoireMax = max; }
                    }
                }
                e.Disponible = e.CoeurMax > 0 || e.MemoireMax > 0;
                if (!e.Disponible) e.Motif = "Cette carte n'expose aucune plage de décalage.";
                return e;
            }
            catch (Exception ex)
            {
                e.Motif = "Lecture impossible : " + ex.GetType().Name + ".";
                return e;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        /// <summary>
        /// Pose un décalage sur P0. Les valeurs sont RAMENÉES dans les plages lues sur la carte.
        /// Rend true seulement si le pilote a accepté — jamais sur une supposition.
        /// </summary>
        public static bool Appliquer(int coeurMhz, int memoireMhz, Action<string, int> log)
        {
            Etat e = Lire();
            if (!e.Disponible)
            {
                if (log != null) log(e.Motif ?? "Décalage de fréquence indisponible sur cette carte.", 3);
                return false;
            }

            int coeur = Borne(coeurMhz, e.CoeurMin, e.CoeurMax);
            int memoire = Borne(memoireMhz, e.MemoireMin, e.MemoireMax);
            if ((coeur != coeurMhz || memoire != memoireMhz) && log != null)
                log("Décalage ramené dans les plages de la carte : cœur " + Signe(coeur)
                    + " MHz, mémoire " + Signe(memoire) + " MHz.", 2);

            IntPtr gpu = Carte();
            var set = Fn<FnPstates>(IdSetPstates20);
            if (gpu == IntPtr.Zero || set == null)
            {
                if (log != null) log("Ce pilote n'accepte pas l'écriture de la table des fréquences.", 3);
                return false;
            }

            IntPtr buf = Marshal.AllocHGlobal(TailleV2);
            try
            {
                Efface(buf, TailleV2);
                Marshal.WriteInt32(buf, 0, unchecked((int)((uint)TailleV2 | (2u << 16))));
                Marshal.WriteInt32(buf, 8, 1);    // un seul état visé : P0
                Marshal.WriteInt32(buf, 12, 2);   // deux domaines : cœur et mémoire
                Marshal.WriteInt32(buf, 16, 0);   // aucune tension : mesuré non exposé sur RTX 40

                int po = Entete;
                Marshal.WriteInt32(buf, po, 0);   // pstateId = P0
                EcritHorloge(buf, po + 8, DomaineCoeur, coeur);
                EcritHorloge(buf, po + 8 + TailleClock, DomaineMemoire, memoire);

                int st = set(gpu, buf);
                if (st != 0)
                {
                    if (log != null) log(Message(st), 3);
                    return false;
                }
            }
            catch (Exception ex)
            {
                if (log != null) log("Décalage impossible : " + ex.GetType().Name + ".", 3);
                return false;
            }
            finally { Marshal.FreeHGlobal(buf); }

            if (log != null)
                log("Décalage appliqué : cœur " + Signe(coeur) + " MHz, mémoire " + Signe(memoire)
                  + " MHz. Il disparaît au redémarrage. Si le jeu affiche des artefacts ou si le "
                  + "pilote plante, redescends — c'est le signe qu'on est allé trop haut.", 2);
            return true;
        }

        /// <summary>Remet la carte à sa courbe d'usine.</summary>
        public static bool Reinitialiser(Action<string, int> log)
        {
            return Appliquer(0, 0, log);
        }

        private static void EcritHorloge(IntPtr buf, int off, int domaine, int mhz)
        {
            Marshal.WriteInt32(buf, off, domaine);
            Marshal.WriteInt32(buf, off + 4, TypeSimple);
            Marshal.WriteInt32(buf, off + 12, mhz * 1000);   // le pilote parle en kHz
        }

        private static void Efface(IntPtr buf, int taille)
        {
            for (int i = 0; i < taille; i += 4) Marshal.WriteInt32(buf, i, 0);
        }

        /// <summary>Décalage signé lisible : « +128 », « −50 », « 0 ».</summary>
        public static string Signe(int mhz)
        {
            return mhz > 0 ? "+" + mhz : mhz.ToString();
        }
    }
}
