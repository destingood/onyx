using System.Collections.Generic;
using System.Linq;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Cohérence plafond FPS / rafraîchissement / V-Sync pilote / Reflex : le plafond que l'on
    /// croit avoir n'est pas toujours celui que l'on a (cas réel : 600 FPS demandés dans
    /// Overwatch, V-Sync forcé + G-SYNC + Reflex sur un écran 500 Hz → ≈ 430 FPS).
    /// </summary>
    internal static class CoherenceFpsTests
    {
        private const string OwLucas =
            "[Render.13]\r\nCpuForceSyncEnabled = \"1\"\r\nFrameRateCap = \"600\"\r\nFullScreenRefresh = \"500\"\r\n"
          + "ReflexMode = \"2\"\r\nUseCustomFrameRates = \"1\"\r\nWindowedRefresh = \"500\"\r\n";

        public static void Tout()
        {
            Banc.Titre("Plafond automatique de Reflex (V-Sync + G-SYNC)");
            Banc.Verifie("144 Hz → 138 FPS (valeur relevée par Blur Busters)", true, CoherenceFps.CapReflex(144) == 138);
            Banc.Verifie("240 Hz → 224 FPS", true, CoherenceFps.CapReflex(240) == 224);
            Banc.Verifie("500 Hz → 430 FPS", true, CoherenceFps.CapReflex(500) == 430);
            Banc.Verifie("60 Hz → 59 FPS", true, CoherenceFps.CapReflex(60) == 59);
            Banc.Verifie("fréquence inconnue → 0", true, CoherenceFps.CapReflex(0) == 0);
            Banc.Verifie("plafond G-SYNC sans Reflex : Hz − 3", true, CoherenceFps.CapVrr(144) == 141);

            Banc.Titre("V-Sync lu dans le pilote NVIDIA");
            Banc.Verifie("0x47814940 = forcé", true, CoherenceFps.DepuisPilote(0x47814940u) == CoherenceFps.VSync.ForceOn);
            Banc.Verifie("0x08416747 = coupé", true, CoherenceFps.DepuisPilote(0x08416747u) == CoherenceFps.VSync.ForceOff);
            Banc.Verifie("0x60925292 = application", true, CoherenceFps.DepuisPilote(0x60925292u) == CoherenceFps.VSync.Application);
            Banc.Verifie("0x18888888 = rapide", true, CoherenceFps.DepuisPilote(0x18888888u) == CoherenceFps.VSync.Rapide);
            Banc.Verifie("absent = inconnu", true, CoherenceFps.DepuisPilote(null) == CoherenceFps.VSync.Inconnu);
            Banc.Verifie("valeur non documentée = autre", true, CoherenceFps.DepuisPilote(0x32610244u) == CoherenceFps.VSync.Autre);

            Banc.Titre("Réglages Overwatch relus");
            CoherenceFps.ReglagesJeu ow = CoherenceFps.LitOverwatch(OwLucas);
            Banc.Verifie("plafond personnalisé 600", true, ow != null && ow.Plafond == 600);
            Banc.Verifie("rafraîchissement 500", true, ow != null && ow.Hz == 500);
            Banc.Verifie("Reflex actif", true, ow != null && ow.Reflex == true);
            CoherenceFps.ReglagesJeu owAuto = CoherenceFps.LitOverwatch(OwLucas.Replace("UseCustomFrameRates = \"1\"", "UseCustomFrameRates = \"0\""));
            Banc.Verifie("plafond basé sur l'écran : pas de plafond personnalisé", true, owAuto.Plafond == 0);
            Banc.Verifie("fichier absent → null", true, CoherenceFps.LitOverwatch(null) == null);
            Banc.Verifie("fichier sans les clés → rien d'inventé", true,
                CoherenceFps.LitOverwatch("[Render.13]\r\n").Plafond == 0 && CoherenceFps.LitOverwatch("[Render.13]\r\n").Reflex == null);

            Banc.Titre("Constats de cohérence");
            var cas = new CoherenceFps.Contexte { Jeu = "Overwatch", HzEcran = 500, Plafond = 600, HzJeu = 500,
                                                  Reflex = true, VSync = CoherenceFps.VSync.ForceOn, GSync = true };
            List<string> c = CoherenceFps.Constats(cas);
            Banc.Verifie("cas réel : le plafond de 600 FPS est signalé inatteignable", true,
                c.Count == 1 && c[0].Contains("600") && c[0].Contains("430"));
            Banc.Verifie("cas réel : le gain annoncé est chiffré en ms (0,7)", true, c[0].Contains("0,7 ms"));

            cas.VSync = CoherenceFps.VSync.Application;
            Banc.Verifie("V-Sync laissé au jeu : rien", true, CoherenceFps.Constats(cas).Count == 0);

            cas.VSync = CoherenceFps.VSync.ForceOn; cas.Plafond = 400;
            Banc.Verifie("plafond déjà sous le plafond Reflex : rien", true, CoherenceFps.Constats(cas).Count == 0);

            cas.Plafond = 0;
            Banc.Verifie("sans plafond personnalisé : le plafond Reflex est quand même annoncé", true,
                CoherenceFps.Constats(cas).Any(x => x.Contains("430")));

            var sansReflex = new CoherenceFps.Contexte { Jeu = "Overwatch", HzEcran = 144, Plafond = 0,
                                                         Reflex = false, VSync = CoherenceFps.VSync.ForceOn, GSync = true };
            Banc.Verifie("G-SYNC + V-Sync sans Reflex ni plafond : plafonner à 141", true,
                CoherenceFps.Constats(sansReflex).Any(x => x.Contains("141")));
            sansReflex.Plafond = 141;
            Banc.Verifie("plafond déjà à Hz − 3 : rien", true, CoherenceFps.Constats(sansReflex).Count == 0);

            var sansGsync = new CoherenceFps.Contexte { Jeu = "Overwatch", HzEcran = 240, Plafond = 600,
                                                        Reflex = true, VSync = CoherenceFps.VSync.ForceOn, GSync = false };
            Banc.Verifie("V-Sync forcé sans G-SYNC : bloqué au rafraîchissement, latence ajoutée", true,
                CoherenceFps.Constats(sansGsync).Any(x => x.Contains("240") && x.Contains("latence")));

            var hzBas = new CoherenceFps.Contexte { Jeu = "Overwatch", HzEcran = 500, HzJeu = 144, Plafond = 600,
                                                    Reflex = true, VSync = CoherenceFps.VSync.Application };
            Banc.Verifie("jeu réglé à 144 Hz sur un écran 500 Hz : signalé", true,
                CoherenceFps.Constats(hzBas).Any(x => x.Contains("144 Hz") && x.Contains("500 Hz")));
            hzBas.HzJeu = 499;
            Banc.Verifie("écart de 1 Hz (499/500) : toléré", true, CoherenceFps.Constats(hzBas).Count == 0);

            var limiteur = new CoherenceFps.Contexte { Jeu = "Overwatch", HzEcran = 500, Plafond = 600,
                                                       Reflex = true, VSync = CoherenceFps.VSync.Application, LimiteurPilote = 300 };
            Banc.Verifie("limiteur du pilote à 300 sous le plafond du jeu : signalé", true,
                CoherenceFps.Constats(limiteur).Any(x => x.Contains("300")));
            limiteur.LimiteurPilote = 0;
            Banc.Verifie("limiteur du pilote coupé : rien", true, CoherenceFps.Constats(limiteur).Count == 0);

            var plafondBas = new CoherenceFps.Contexte { Jeu = "Overwatch", HzEcran = 500, Plafond = 144,
                                                         Reflex = true, VSync = CoherenceFps.VSync.Application };
            Banc.Verifie("plafond 144 sur écran 500 Hz : signalé", true,
                CoherenceFps.Constats(plafondBas).Any(x => x.Contains("144 FPS")));

            var inconnu = new CoherenceFps.Contexte { Jeu = "Overwatch", HzEcran = 0, Plafond = 600,
                                                      Reflex = true, VSync = CoherenceFps.VSync.ForceOn };
            Banc.Verifie("écran illisible : aucune accusation", true, CoherenceFps.Constats(inconnu).Count == 0);
            Banc.Verifie("contexte absent : rien", true, CoherenceFps.Constats(null).Count == 0);
        }
    }
}
