using System;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="CompteurJeu"/> — réglages relus (ancien format compris), taille de la
    /// fenêtre selon les lignes, place à l'écran, textes des mesures, durée de la partie.
    /// </summary>
    internal static class CompteurJeuTests
    {
        public static void Tout()
        {
            Banc.Titre("Compteur en jeu");

            var o = CompteurJeu.Lit(null);
            Banc.Verifie("sans fichier : éteint, haut droit, taille moyenne, couleur automatique", true,
                !o.Actif && o.Position == 1 && o.Taille == 1 && o.Couleur == 0);
            Banc.Verifie("sans fichier : l'affichage d'avant (charges, températures, RAM, courbe)", true,
                o.A(CompteurJeu.Cpu) && o.A(CompteurJeu.GpuTemp) && o.A(CompteurJeu.Ram) && o.A(CompteurJeu.Courbe)
                && !o.A(CompteurJeu.Vram) && !o.A(CompteurJeu.Reseau));

            o = CompteurJeu.Lit("1;3\r\n");
            Banc.Verifie("ancien fichier « actif ; coin » : relu, lignes d'avant gardées", true,
                o.Actif && o.Position == 3 && o.Taille == 1 && o.A(CompteurJeu.Cpu) && CompteurJeu.NbLignes(o) == 3);

            o = CompteurJeu.Lit("1;5;2;4;vram,net,heure,inconnu, cpu ");
            Banc.Verifie("nouveau format relu, ligne inconnue ignorée", true,
                o.Position == 5 && o.Taille == 2 && o.Couleur == 4 && o.A(CompteurJeu.Vram) && o.A(CompteurJeu.Cpu)
                && !o.A(CompteurJeu.Ram) && o.Lignes.Count == 4);
            Banc.Egal("écrit dans l'ordre d'affichage", "1;5;2;4;cpu,vram,net,heure", CompteurJeu.Ecrit(o));
            Banc.Egal("aller-retour", CompteurJeu.Ecrit(o), CompteurJeu.Ecrit(CompteurJeu.Lit(CompteurJeu.Ecrit(o))));

            o = CompteurJeu.Lit("1;9;7;-2;");
            Banc.Verifie("valeurs hors bornes : valeurs par défaut ; liste vide : aucune ligne", true,
                o.Position == 1 && o.Taille == 1 && o.Couleur == 0 && o.Lignes.Count == 0 && CompteurJeu.NbLignes(o) == 0);

            // Taille de la fenêtre.
            int l, h;
            CompteurJeu.Dimensions(CompteurJeu.Lit(null), out l, out h);
            Banc.Egal("réglages par défaut : la fenêtre d'avant (222 × 178)", "222x178", l + "x" + h);
            CompteurJeu.Dimensions(CompteurJeu.Lit("1;1;1;0;"), out l, out h);
            Banc.Egal("FPS seuls : 222 × 78", "222x78", l + "x" + h);
            o = CompteurJeu.Lit("1;1;1;0;cputemp,gputemp");
            Banc.Egal("température seule : elle occupe sa ligne", "2", CompteurJeu.NbLignes(o).ToString());
            o = CompteurJeu.Lit("1;1;1;0;cpu,cputemp,gpu,gputemp,vram,gpuw,ram,net,heure,duree,courbe");
            Banc.Egal("tout coché : 8 lignes (la température partage la ligne de la charge)", "8", CompteurJeu.NbLignes(o).ToString());
            CompteurJeu.Dimensions(o, out l, out h);
            Banc.Egal("tout coché, taille moyenne", "222x318", l + "x" + h);
            o.Taille = 2; CompteurJeu.Dimensions(o, out l, out h);
            Banc.Egal("tout coché, grand (× 1,3)", "289x413", l + "x" + h);
            o.Taille = 0; CompteurJeu.Dimensions(o, out l, out h);
            Banc.Egal("tout coché, petit (× 0,85)", "189x270", l + "x" + h);

            // Style en ligne.
            o = CompteurJeu.Lit("1;1;1;0;cpu,ram");
            Banc.Egal("style bloc : le fichier reste celui des versions précédentes", "1;1;1;0;cpu,ram", CompteurJeu.Ecrit(o));
            o.Style = 1;
            Banc.Egal("style en ligne : un sixième champ", "1;1;1;0;cpu,ram;1", CompteurJeu.Ecrit(o));
            Banc.Egal("style en ligne relu", "1", CompteurJeu.Lit("1;1;1;0;cpu,ram;1").Style.ToString());
            Banc.Egal("style inconnu : bloc", "0", CompteurJeu.Lit("1;1;1;0;cpu,ram;7").Style.ToString());
            CompteurJeu.Dimensions(o, out l, out h);
            Banc.Egal("bandeau FPS + CPU + RAM, taille moyenne", "238x28", l + "x" + h);
            o.Lignes.Add(CompteurJeu.CpuTemp); o.Lignes.Add(CompteurJeu.Courbe);
            Banc.Egal("la température s'ajoute à la case du processeur ; la courbe ne prend pas de place", "272",
                CompteurJeu.LargeurEnLigne(o).ToString());
            o = CompteurJeu.Lit("1;1;1;0;cpumhz,disque,batterie");
            Banc.Egal("nouvelles mesures : une ligne chacune en bloc", "3", CompteurJeu.NbLignes(o).ToString());

            // Nouvelles mesures.
            Banc.Egal("fréquence : base × % de performance", "4,72 GHz", CompteurJeu.TexteFrequence(CompteurJeu.Frequence(3700, 127.57)));
            Banc.Egal("fréquence sous 1 GHz", "800 MHz", CompteurJeu.TexteFrequence(800));
            Banc.Verifie("fréquence : compteur absent ou nul → pas de mesure", true,
                double.IsNaN(CompteurJeu.Frequence(double.NaN, 100)) && double.IsNaN(CompteurJeu.Frequence(3700, 0)) && CompteurJeu.TexteFrequence(double.NaN) == "—");
            Banc.Egal("disque : 100 − inactivité", "12", CompteurJeu.ActiviteDisque(88).ToString());
            Banc.Egal("disque : bornes", "0|100", CompteurJeu.ActiviteDisque(130) + "|" + CompteurJeu.ActiviteDisque(-5));
            Banc.Egal("batterie", "82 %", CompteurJeu.TexteBatterie(0.82, false, false));
            Banc.Egal("batterie en charge", "40 % · en charge", CompteurJeu.TexteBatterie(0.4, true, false));
            Banc.Verifie("PC fixe ou niveau inconnu (255 %) : pas de mesure", true,
                CompteurJeu.TexteBatterie(1, false, true) == "—" && CompteurJeu.TexteBatterie(2.55, false, false) == "—");

            Banc.Egal("ventilateur : tours et consigne", "1 450 tr/min · 42 %", CompteurJeu.TexteVentilateur(1450.4, 42));
            Banc.Egal("ventilateur : tours seuls", "980 tr/min", CompteurJeu.TexteVentilateur(980, double.NaN));
            Banc.Egal("ventilateur : consigne seule", "42 %", CompteurJeu.TexteVentilateur(double.NaN, 42));
            Banc.Egal("ventilateur coupé au repos", "à l'arrêt", CompteurJeu.TexteVentilateur(0, 0));
            Banc.Egal("ventilateur non lu", "—", CompteurJeu.TexteVentilateur(double.NaN, double.NaN));
            Banc.Egal("ventilateur : une ligne en bloc", "1", CompteurJeu.NbLignes(CompteurJeu.Lit("1;1;1;0;gpufan")).ToString());
            Banc.Verifie("ventilateur : pas coché par défaut (affichage d'avant gardé)", false, CompteurJeu.Lit(null).A(CompteurJeu.Ventilo));
            Banc.Verifie("ventilateur : lu sans pilote noyau (carte graphique seule)", true,
                CatalogueTests.Source("GpuSensors.cs").Contains("case SensorType.Fan:") && CatalogueTests.Source("GpuSensors.cs").Contains("IsMotherboardEnabled= false"));

            // Place : zone de travail 1920 × 1040 décalée de (100, 50), fenêtre 222 × 178.
            int x, y;
            CompteurJeu.Place(0, 100, 50, 1920, 1040, 222, 178, out x, out y); Banc.Egal("haut gauche", "116,66", x + "," + y);
            CompteurJeu.Place(1, 100, 50, 1920, 1040, 222, 178, out x, out y); Banc.Egal("haut droit", "1782,66", x + "," + y);
            CompteurJeu.Place(2, 100, 50, 1920, 1040, 222, 178, out x, out y); Banc.Egal("bas gauche", "116,896", x + "," + y);
            CompteurJeu.Place(3, 100, 50, 1920, 1040, 222, 178, out x, out y); Banc.Egal("bas droit", "1782,896", x + "," + y);
            CompteurJeu.Place(4, 100, 50, 1920, 1040, 222, 178, out x, out y); Banc.Egal("haut centre", "949,66", x + "," + y);
            CompteurJeu.Place(5, 100, 50, 1920, 1040, 222, 178, out x, out y); Banc.Egal("bas centre", "949,896", x + "," + y);

            // Textes.
            Banc.Egal("débit : kilobits", "850 kb/s", CompteurJeu.TexteDebit(850e3));
            Banc.Egal("débit : mégabits avec une décimale", "12,4 Mb/s", CompteurJeu.TexteDebit(12.4e6));
            Banc.Egal("débit : centaines de mégabits sans décimale", "250 Mb/s", CompteurJeu.TexteDebit(250e6));
            Banc.Egal("débit : gigabits", "1,2 Gb/s", CompteurJeu.TexteDebit(1.2e9));
            Banc.Egal("débit inconnu", "—", CompteurJeu.TexteDebit(double.NaN));
            Banc.Egal("1 Mo reçu en 1 s = 8 Mb/s", "8,0 Mb/s", CompteurJeu.TexteDebit(CompteurJeu.Debit(5000000, 6000000, 1)));
            Banc.Verifie("compteur d'octets qui recule ou durée nulle : pas de mesure", true,
                double.IsNaN(CompteurJeu.Debit(6000000, 5000000, 1)) && double.IsNaN(CompteurJeu.Debit(1, 2, 0)) && double.IsNaN(CompteurJeu.Debit(-1, 2, 1)));
            Banc.Egal("mémoire vidéo", "5,2 / 12 Go", CompteurJeu.TexteVram(5320, 12288));
            Banc.Egal("mémoire vidéo, total inconnu", "5,2 Go", CompteurJeu.TexteVram(5320, 0));
            Banc.Egal("mémoire vidéo inconnue", "—", CompteurJeu.TexteVram(0, 12288));
            Banc.Egal("durée sous une heure", "42 min", CompteurJeu.TexteDuree(TimeSpan.FromMinutes(42.9)));
            Banc.Egal("durée au-delà d'une heure", "1 h 05", CompteurJeu.TexteDuree(TimeSpan.FromMinutes(65)));
            Banc.Egal("durée négative (horloge reculée)", "0 min", CompteurJeu.TexteDuree(TimeSpan.FromMinutes(-3)));

            // Durée de la partie.
            var c = new CompteurJeu.Chrono();
            DateTime t0 = new DateTime(2026, 11, 27, 20, 0, 0, DateTimeKind.Utc);
            Banc.Verifie("aucun jeu : pas de durée", true, c.Marque(null, t0) == null);
            Banc.Egal("le jeu apparaît : zéro", "0", c.Marque("Fortnite", t0).Value.TotalMinutes.ToString());
            Banc.Egal("dix minutes plus tard", "10", c.Marque("fortnite", t0.AddMinutes(10)).Value.TotalMinutes.ToString());
            Banc.Egal("Alt+Tab de 3 min : la partie continue", "13", c.Marque(null, t0.AddMinutes(13)).Value.TotalMinutes.ToString());
            Banc.Egal("retour au jeu", "14", c.Marque("Fortnite", t0.AddMinutes(14)).Value.TotalMinutes.ToString());
            Banc.Egal("autre jeu : repart de zéro", "0", c.Marque("Cyberpunk2077", t0.AddMinutes(15)).Value.TotalMinutes.ToString());
            Banc.Verifie("plus de 5 min sans jeu : partie finie", true, c.Marque(null, t0.AddMinutes(21)) == null);
            Banc.Egal("le même jeu revient : nouvelle partie", "0", c.Marque("Cyberpunk2077", t0.AddMinutes(30)).Value.TotalMinutes.ToString());

            foreach (string id in CompteurJeu.Toutes)
                if (CompteurJeu.Libelle(id) == id) Banc.Verifie("libellé manquant pour " + id, true, false);
            Banc.Verifie("chaque ligne a un libellé", true, true);
        }
    }
}
