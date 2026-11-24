namespace BTOptimizer.Tests
{
    /// <summary>
    /// Latence réelle entrée → affichage, lue dans le CSV de PresentMon (Intel, MIT).
    /// On mesure au lieu de deviner : c'est la seule façon de savoir quel réglage gagne.
    /// </summary>
    internal static class LatenceReelleTests
    {
        private const string Entete =
            "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,"
          + "CPUStartTime,MsCPUBusy,MsCPUWait,MsGPULatency,MsGPUTime,MsGPUBusy,MsGPUWait,DisplayLatency,DisplayedTime,"
          + "MsClickToPhotonLatency,MsAllInputToPhotonLatency,MsBetweenPresents,MsBetweenDisplayChange,MsPCLatency\r\n";

        private static string Ligne(string app, string entree, string pcl, string affichage, string entreImages)
        {
            return app + ",1234,0x1,DXGI,0,512,1,Hardware: Independent Flip,1.0,1.0,0.1,0.2,1.0,1.0,0.0,"
                 + affichage + ",1.0,NA," + entree + "," + entreImages + "," + entreImages + "," + pcl + "\r\n";
        }

        public static void Tout()
        {
            // 10 images Overwatch : entrées 5..14 ms (médiane 9,5), PCL 4..13, affichage 3 ms, 2 ms entre images (500 FPS)
            string csv = Entete;
            for (int i = 0; i < 10; i++)
                csv += Ligne("Overwatch.exe", (5 + i).ToString(), (4 + i).ToString(), "3.0", "2.0");
            csv += Ligne("Overwatch.exe", "NA", "NA", "NA", "2.0");      // image sans entrée : comptée, pas mesurée
            csv += Ligne("Discord.exe", "50", "NA", "20", "16.6");       // autre appli : ignorée

            Banc.Titre("Latence réelle (CSV PresentMon)");
            LatenceReelle.Releve r = LatenceReelle.Analyse(csv, "Overwatch.exe");
            Banc.Verifie("11 images du jeu, Discord ignoré", true, r.Images == 11);
            Banc.Verifie("10 images avec une entrée mesurée (NA ignoré)", true, r.Entrees == 10);
            Banc.Verifie("médiane entrée → affichage = 9,5 ms", true, Proche(r.EntreeMediane, 9.5));
            Banc.Verifie("95e centile ≈ 13,55 ms", true, Proche(r.EntreeP95, 13.55));
            Banc.Verifie("médiane PC Latency (Reflex) = 8,5 ms", true, Proche(r.PcLatMediane, 8.5));
            Banc.Verifie("FPS déduits des écarts entre images = 500", true, Proche(r.Fps, 500));
            Banc.Verifie("application retenue", true, r.Application == "Overwatch.exe");

            LatenceReelle.Releve auto = LatenceReelle.Analyse(csv, null);
            Banc.Verifie("sans nom : l'appli qui a le plus d'images", true, auto.Application == "Overwatch.exe");

            LatenceReelle.Releve casse = LatenceReelle.Analyse(csv, "overwatch.EXE");
            Banc.Verifie("nom insensible à la casse", true, casse.Images == 11);

            LatenceReelle.Releve vide = LatenceReelle.Analyse("", "Overwatch.exe");
            Banc.Verifie("CSV vide : aucun relevé, pas d'exception", true, vide.Images == 0 && vide.EntreeMediane == null);

            string sansPcl = "Application,MsAllInputToPhotonLatency,MsBetweenPresents\r\nJeu.exe,7.5,4.0\r\nJeu.exe,8.5,4.0\r\n";
            LatenceReelle.Releve sp = LatenceReelle.Analyse(sansPcl, "Jeu.exe");
            Banc.Verifie("colonnes cherchées par nom, PCL absent = null", true, Proche(sp.EntreeMediane, 8.0) && sp.PcLatMediane == null);

            string guillemets = "\"Application\",\"MsAllInputToPhotonLatency\"\r\n\"Jeu.exe\",\"6.25\"\r\n";
            Banc.Verifie("valeurs entre guillemets acceptées", true, Proche(LatenceReelle.Analyse(guillemets, "Jeu.exe").EntreeMediane, 6.25));

            Banc.Titre("Comparaison avant / après un réglage");
            var avant = new LatenceReelle.Releve { Entrees = 500, EntreeMediane = 12.0, EntreeP95 = 18.0 };
            var mieux = new LatenceReelle.Releve { Entrees = 500, EntreeMediane = 10.0, EntreeP95 = 15.0 };
            var pareil = new LatenceReelle.Releve { Entrees = 500, EntreeMediane = 11.9, EntreeP95 = 18.1 };
            var pire = new LatenceReelle.Releve { Entrees = 500, EntreeMediane = 13.5, EntreeP95 = 20.0 };
            Banc.Verifie("−2 ms : gain", true, LatenceReelle.Verdict(avant, mieux) == LatenceReelle.Sens.Gain);
            Banc.Verifie("−0,1 ms : dans le bruit", true, LatenceReelle.Verdict(avant, pareil) == LatenceReelle.Sens.Bruit);
            Banc.Verifie("+1,5 ms : perte", true, LatenceReelle.Verdict(avant, pire) == LatenceReelle.Sens.Perte);
            var peu = new LatenceReelle.Releve { Entrees = 20, EntreeMediane = 8.0, EntreeP95 = 12.0 };
            Banc.Verifie("trop peu d'entrées : pas de verdict", true, LatenceReelle.Verdict(avant, peu) == LatenceReelle.Sens.Insuffisant);
            Banc.Verifie("texte du gain en ms (virgule)", true, LatenceReelle.Texte(avant, mieux).Contains("2,0 ms"));
        }

        private static bool Proche(double? v, double attendu) { return v != null && System.Math.Abs(v.Value - attendu) < 0.01; }
    }
}
