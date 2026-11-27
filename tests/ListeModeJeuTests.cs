namespace BTOptimizer.Tests
{
    /// <summary>Liste du Mode Jeu enregistrée dans un fichier : aller-retour, noms inconnus, faux fichiers.</summary>
    internal static class ListeModeJeuTests
    {
        public static void Tout()
        {
            Banc.Titre("Liste du Mode Jeu (fichier)");
            string[] svc = { "SysMain", "Spooler", "DiagTrack" };
            string[] apps = { "lanceurs", "rgb" };

            string f = ListeModeJeu.Ecrit(new[] { "Spooler", "DiagTrack", "Spooler" }, new[] { "rgb" });
            Banc.Egal("écriture triée, sans doublon",
                ListeModeJeu.Entete + "\n[services-exclus]\nDiagTrack\nSpooler\n[applis-exclues]\nrgb\n", f);

            var c = ListeModeJeu.Lit(f, svc, apps);
            Banc.Egal("aller-retour", "DiagTrack,Spooler|rgb|0", string.Join(",", c.Services) + "|" + string.Join(",", c.Applis) + "|" + c.Ignores);

            c = ListeModeJeu.Lit("﻿[services-exclus]\r\nspooler\r\nServiceInventé\r\n[applis-exclues]\r\nRGB\r\n", svc, apps);
            Banc.Egal("casse du catalogue, BOM, CRLF, nom inconnu compté", "Spooler|rgb|1",
                string.Join(",", c.Services) + "|" + string.Join(",", c.Applis) + "|" + c.Ignores);
            Banc.Egal("bilan", "1 service(s) et 1 famille(s) d'applications exclus. 1 nom(s) inconnu(s) de cette version ignoré(s).", ListeModeJeu.Bilan(c));

            Banc.Verifie("fichier quelconque refusé", true, ListeModeJeu.Lit("Bonjour\nSpooler\n", svc, apps) == null);
            Banc.Verifie("fichier vide refusé", true, ListeModeJeu.Lit("", svc, apps) == null && ListeModeJeu.Lit("# rien\n", svc, apps) == null);
            c = ListeModeJeu.Lit("[services-exclus]\n[applis-exclues]\n", svc, apps);
            Banc.Verifie("liste vide acceptée (tout actif)", true, c != null && c.Services.Count == 0 && c.Applis.Count == 0);
        }
    }
}
