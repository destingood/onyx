using System.Collections.Generic;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Mode Efficacité du Mode Jeu : décision « déjà en Efficacité », identité d'un processus,
    /// journal de rétablissement (aller-retour, tolérance) et texte affiché.
    /// </summary>
    internal static class EfficaciteTests
    {
        public static void Tout()
        {
            Banc.Titre("Mode Efficacité (Mode Jeu)");

            // ---- Déjà en Efficacité : rien à écrire, rien à rétablir
            Banc.Verifie("Idle + EcoQoS contrôlé et actif : déjà en Efficacité", true,
                Efficacite.DejaEnEfficacite(Efficacite.PrioriteIdle, 1, 1));
            Banc.Verifie("priorité Normal (0x20) : pas en Efficacité", false,
                Efficacite.DejaEnEfficacite(0x20, 1, 1));
            Banc.Verifie("EcoQoS non contrôlé (0/0) : pas en Efficacité", false,
                Efficacite.DejaEnEfficacite(Efficacite.PrioriteIdle, 0, 0));
            Banc.Verifie("HighQoS (contrôlé, état 0) : pas en Efficacité", false,
                Efficacite.DejaEnEfficacite(Efficacite.PrioriteIdle, 1, 0));

            // ---- Même processus : un PID réutilisé n'est pas le nôtre
            var e = new Efficacite.Entree { Pid = 42, DebutTicks = 1000, Nom = "node" };
            Banc.Verifie("même pid, même début : même processus", true, Efficacite.MemeProcessus(e, 42, 1000));
            Banc.Verifie("même pid, autre début : PID réutilisé", false, Efficacite.MemeProcessus(e, 42, 2000));
            Banc.Verifie("autre pid : pas le même", false, Efficacite.MemeProcessus(e, 43, 1000));
            Banc.Verifie("début 0 (illisible) : jamais le même", false,
                Efficacite.MemeProcessus(new Efficacite.Entree { Pid = 42, DebutTicks = 0 }, 42, 0));
            Banc.Verifie("entrée absente : pas le même", false, Efficacite.MemeProcessus(null, 42, 1000));

            // ---- Journal : aller-retour
            var deux = new List<Efficacite.Entree>
            {
                new Efficacite.Entree { Pid = 100, DebutTicks = 638000000000000001L, Nom = "Code", Priorite = 0x20, Controle = 0, Etat = 0 },
                new Efficacite.Entree { Pid = 200, DebutTicks = 638000000000000002L, Nom = "a|b\r\nc", Priorite = 0x4000, Controle = 1, Etat = 0 }
            };
            List<Efficacite.Entree> relu = Efficacite.Lit(Efficacite.Ecrit(deux));
            Banc.Verifie("aller-retour : deux entrées", true, relu.Count == 2);
            Banc.Verifie("aller-retour : première intacte", true,
                relu.Count == 2 && relu[0].Pid == 100 && relu[0].DebutTicks == 638000000000000001L
                && relu[0].Nom == "Code" && relu[0].Priorite == 0x20 && relu[0].Controle == 0 && relu[0].Etat == 0);
            Banc.Verifie("aller-retour : seconde intacte, nom nettoyé de | et des sauts de ligne", true,
                relu.Count == 2 && relu[1].Pid == 200 && relu[1].Nom == "abc"
                && relu[1].Priorite == 0x4000 && relu[1].Controle == 1 && relu[1].Etat == 0);
            Banc.Verifie("Lit(\"\") : liste vide", true, Efficacite.Lit("").Count == 0);
            Banc.Verifie("Lit(null) : liste vide", true, Efficacite.Lit(null).Count == 0);
            List<Efficacite.Entree> abime = Efficacite.Lit("n'importe quoi\r\n7|123|64|1|1|node\r\n8|x|64|1|1|cassee\r\n");
            Banc.Verifie("ligne abîmée ignorée, la bonne gardée (CRLF accepté)", true,
                abime.Count == 1 && abime[0].Pid == 7 && abime[0].Nom == "node");

            // ---- Texte du journal
            Banc.Verifie("rien : « rien à mettre en Efficacité »", true,
                Efficacite.Texte(0, 0, 0, 0) == "Applications de fond : rien à mettre en Efficacité.");
            string seul = Efficacite.Texte(3, 0, 0, 0);
            Banc.Verifie("mis seul : le compte, rien n'est fermé, pas de refus", true,
                seul.Contains("3 processus en mode Efficacité") && seul.Contains("rien n'est fermé")
                && !seul.Contains("refus") && !seul.Contains("déjà") && !seul.Contains("service"));
            string plein = Efficacite.Texte(2, 1, 4, 5);
            Banc.Verifie("déjà + refusés + services : tous nommés", true,
                plein.Contains("1 déjà en Efficacité") && plein.Contains("4 refusé(s)") && plein.Contains("5 service(s) suspendu(s)")
                && plein.EndsWith("."));
        }
    }
}
