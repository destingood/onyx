using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="CanauxWindows"/> — le balayage des 139 journaux que personne n'ouvre.
    ///
    /// Comme pour PilotesRefuses : les assertions valent partout, le constat lit cette machine
    /// et ne juge rien.
    /// </summary>
    internal static class CanauxWindowsTests
    {
        public static void Tout()
        {
            Classement();
            NomsDeCanaux();
            SilenceEtBruit();
            FusionParCanal();
            OrdreDuRapport();
            ConstatSurCetteMachine();
        }

        private static CanauxWindows.Groupe G(string canal, int nombre, DateTime dernier)
        {
            return new CanauxWindows.Groupe
            {
                Canal = canal, Fournisseur = "X", Id = 1,
                Niveau = CanauxWindows.NiveauErreur, Nombre = nombre, Dernier = dernier
            };
        }

        private static void Classement()
        {
            Banc.Titre("Ce qui mérite un regard, ce qui n'en mérite aucun");

            Banc.Verifie("CodeIntegrity est sérieux — c'est le canal qui a motivé le module",
                true, CanauxWindows.ClasseDe(G("Microsoft-Windows-CodeIntegrity/Operational", 7,
                    DateTime.Now)) == CanauxWindows.Classe.Serieux);
            Banc.Verifie("Kernel-PnP est sérieux", true,
                CanauxWindows.ClasseDe(G("Microsoft-Windows-Kernel-PnP/Configuration", 3,
                    DateTime.Now)) == CanauxWindows.Classe.Serieux);
            Banc.Verifie("WHEA est sérieux", true,
                CanauxWindows.ClasseDe(G("Microsoft-Windows-WHEA-Logger/Errors", 1,
                    DateTime.Now)) == CanauxWindows.Classe.Serieux);

            Banc.Verifie("CloudStore est du bruit", true,
                CanauxWindows.ClasseDe(G("Microsoft-Windows-CloudStore/Operational", 40,
                    DateTime.Now)) == CanauxWindows.Classe.Bruit);
            Banc.Verifie("AppXDeployment est du bruit", true,
                CanauxWindows.ClasseDe(G("Microsoft-Windows-AppXDeployment/Operational", 12,
                    DateTime.Now)) == CanauxWindows.Classe.Bruit);

            Banc.Verifie("un canal jamais vu reste INCONNU, il n'est pas deviné", true,
                CanauxWindows.ClasseDe(G("Microsoft-Windows-QuelqueChose/Operational", 2,
                    DateTime.Now)) == CanauxWindows.Classe.Inconnu);
            Banc.Verifie("null est inconnu, et ne lève pas", true,
                CanauxWindows.ClasseDe(null) == CanauxWindows.Classe.Inconnu);

            Banc.Verifie("un canal sérieux explique POURQUOI ça compte", true,
                CanauxWindows.Pourquoi(G("Microsoft-Windows-CodeIntegrity/Operational", 1,
                    DateTime.Now)).Length > 0);
            Banc.Egal("un canal inconnu n'explique rien", "",
                CanauxWindows.Pourquoi(G("Inconnu/Operational", 1, DateTime.Now)));
        }

        private static void NomsDeCanaux()
        {
            Banc.Titre("Noms de canaux");

            Banc.Egal("le préfixe commun est retiré", "CodeIntegrity/Operational",
                CanauxWindows.NomCourt("Microsoft-Windows-CodeIntegrity/Operational"));
            Banc.Egal("un canal sans préfixe est intact", "OAlerts",
                CanauxWindows.NomCourt("OAlerts"));
            Banc.Egal("vide reste vide", "", CanauxWindows.NomCourt(""));
            Banc.Egal("null ne lève pas", "", CanauxWindows.NomCourt(null));
        }

        private static void SilenceEtBruit()
        {
            Banc.Titre("Le silence est une réponse");

            Banc.Verifie("liste vide : aucun rapport", true,
                CanauxWindows.Rapport(new List<CanauxWindows.Groupe>(), 7).Length == 0);
            Banc.Verifie("liste nulle : aucun rapport, et aucune exception", true,
                CanauxWindows.Rapport(null, 7).Length == 0);

            // Du bruit et RIEN d'autre ne justifie pas un bloc : LogDoctor a deja sa section
            // « bruit connu ». En ajouter une seconde qui ne dit que « tout va bien » revient a
            // faire soi-meme le bruit qu'on pretend trier.
            var quIDuBruit = new List<CanauxWindows.Groupe> {
                G("Microsoft-Windows-CloudStore/Operational", 40, DateTime.Now),
                G("Microsoft-Windows-Store/Operational", 12, DateTime.Now)
            };
            Banc.Verifie("du bruit seul ne produit AUCUN rapport", true,
                CanauxWindows.Rapport(quIDuBruit, 7).Length == 0);

            // ... mais des qu'il y a du contenu reel, le bruit est nomme en contexte, pour que
            // l'utilisateur ne s'en inquiete pas en ouvrant l'Observateur d'evenements.
            var avecDuSerieux = new List<CanauxWindows.Groupe>(quIDuBruit) {
                G("Microsoft-Windows-CodeIntegrity/Operational", 7, DateTime.Now)
            };
            string r = CanauxWindows.Rapport(avecDuSerieux, 7);
            Banc.Verifie("avec du sérieux, le rapport paraît", true, r.Length > 0);
            Banc.Verifie("... et le bruit y est compté sans être détaillé", true,
                r.Contains("BRUIT CONNU") && !r.Contains("CloudStore"));
            Banc.Verifie("le canal sérieux, lui, est nommé", true, r.Contains("CodeIntegrity"));

            Banc.Verifie("aucune ligne ne dépasse 80 colonnes", true, ToutTientEn80(r));

            // La section « inconnus » n'etait couverte par AUCUN test de largeur, et c'est
            // justement elle qui debordait : le fournisseur y repetait le canal, poussant la
            // ligne a 95 colonnes. Attrape en regardant la sortie reelle.
            var inconnus = new List<CanauxWindows.Groupe> {
                new CanauxWindows.Groupe {
                    Canal = "Microsoft-Windows-Host-Network-Service-Admin",
                    Fournisseur = "Microsoft-Windows-Host-Network-Service",
                    Id = 1030, Nombre = 41, Dernier = DateTime.Now
                }
            };
            Banc.Verifie("un canal inconnu à nom long tient en 80 colonnes",
                true, ToutTientEn80(CanauxWindows.Rapport(inconnus, 7)));
            Banc.Egal("le fournisseur redondant n'est pas répété",
                "Host-Network-Service-Admin  id 1030  ×41  ("
                    + DateTime.Now.ToString("dd/MM à HH:mm") + ")",
                CanauxWindows.LigneInconnu(inconnus[0]));

            // ... mais un fournisseur qui apporte vraiment quelque chose reste affiche.
            Banc.Verifie("un fournisseur différent du canal est conservé", true,
                CanauxWindows.LigneInconnu(new CanauxWindows.Groupe {
                    Canal = "Microsoft-Windows-Truc/Operational",
                    Fournisseur = "AutreChose", Id = 7, Nombre = 1, Dernier = DateTime.Now
                }).Contains("AutreChose"));

            Banc.Egal("LigneInconnu(null) ne lève pas", "", CanauxWindows.LigneInconnu(null));
        }

        private static bool ToutTientEn80(string rapport)
        {
            foreach (string ligne in rapport.Split('\n'))
                if (ligne.TrimEnd('\r').Length > 80) return false;
            return true;
        }

        /// <summary>Un incident de ce matin compte plus qu'un incident de la semaine dernière,
        /// même répété : c'est le récent qui explique ce que l'utilisateur vit maintenant.</summary>
        /// <summary>
        /// Trois identifiants d'un même canal donnaient trois lignes portant la MÊME phrase et
        /// trois comptes différents — trois incidents à lire là où il n'y en a qu'un, et surtout
        /// quelque chose qui ressemble à un défaut d'affichage. Attrapé en regardant la sortie
        /// réelle, pas le code.
        /// </summary>
        private static void FusionParCanal()
        {
            Banc.Titre("Un canal, une ligne");

            var l = new List<CanauxWindows.Groupe> {
                G("Microsoft-Windows-Storage-ClassPnP/Operational", 29, new DateTime(2026, 8, 15, 17, 57, 0)),
                G("Microsoft-Windows-Storage-ClassPnP/Operational", 16, new DateTime(2026, 8, 15, 12, 0, 0)),
                G("Microsoft-Windows-Storage-ClassPnP/Operational", 15, new DateTime(2026, 8, 14, 9, 0, 0))
            };
            List<CanauxWindows.Groupe> f = CanauxWindows.FusionneParCanal(l);
            Banc.Verifie("trois identifiants d'un canal donnent UNE ligne", true, f.Count == 1);
            Banc.Verifie("les comptes sont additionnés (29+16+15)", true, f[0].Nombre == 60);
            Banc.Verifie("le nombre de types distincts est retenu", true, f[0].Types == 3);
            Banc.Verifie("la date retenue est la plus récente", true,
                f[0].Dernier == new DateTime(2026, 8, 15, 17, 57, 0));

            // La lecture est mise en cache et relue par d'autres appelants : la fusion doit
            // COPIER, jamais modifier les groupes qu'on lui donne.
            Banc.Verifie("les groupes d'origine ne sont pas modifiés", true,
                l[0].Nombre == 29 && l[0].Types == 1);

            Banc.Verifie("liste nulle : liste vide, aucune exception", true,
                CanauxWindows.FusionneParCanal(null).Count == 0);

            string r = CanauxWindows.Rapport(l, 7);
            Banc.Verifie("le rapport ne répète plus le canal", true,
                Occurrences(r, "Storage-ClassPnP") == 1);
            Banc.Verifie("... et dit sur combien de types", true, r.Contains("sur 3 types"));
        }

        private static int Occurrences(string texte, string motif)
        {
            int n = 0, i = 0;
            while ((i = texte.IndexOf(motif, i, StringComparison.Ordinal)) >= 0) { n++; i += motif.Length; }
            return n;
        }

        private static void OrdreDuRapport()
        {
            Banc.Titre("Le plus récent d'abord");

            var l = new List<CanauxWindows.Groupe> {
                G("Microsoft-Windows-Kernel-PnP/Configuration", 500, new DateTime(2026, 8, 9)),
                G("Microsoft-Windows-CodeIntegrity/Operational", 2, new DateTime(2026, 8, 15))
            };
            string r = CanauxWindows.Rapport(l, 7);
            int posRecent = r.IndexOf("CodeIntegrity", StringComparison.Ordinal);
            int posAncien = r.IndexOf("Kernel-PnP", StringComparison.Ordinal);
            Banc.Verifie("le récent passe devant l'ancien plus fréquent",
                true, posRecent >= 0 && posAncien >= 0 && posRecent < posAncien);
        }

        // ==================================================================
        //  Constat — lit CETTE machine, ne juge rien
        // ==================================================================

        private static void ConstatSurCetteMachine()
        {
            Banc.Titre("Constat sur cette machine (informatif, ne fait échouer aucun test)");

            const int Jours = 7;
            var chrono = Stopwatch.StartNew();
            List<CanauxWindows.Groupe> l = CanauxWindows.Lire(Jours);
            chrono.Stop();

            int serieux = 0, bruit = 0, inconnu = 0;
            foreach (CanauxWindows.Groupe g in l)
                switch (CanauxWindows.ClasseDe(g))
                {
                    case CanauxWindows.Classe.Serieux: serieux++; break;
                    case CanauxWindows.Classe.Bruit: bruit++; break;
                    default: inconnu++; break;
                }

            Console.WriteLine("  balayage de tous les journaux : "
                + chrono.Elapsed.TotalSeconds.ToString("0.0") + " s");
            Console.WriteLine("  " + l.Count + " groupe(s) : " + serieux + " sérieux, "
                + inconnu + " inconnu(s), " + bruit + " bruit");

            string r = CanauxWindows.Rapport(l, Jours);
            if (r.Length == 0)
            {
                Console.WriteLine("  rien à signaler — et c'est un résultat correct.");
                return;
            }
            Console.WriteLine();
            Console.WriteLine(r);
        }
    }
}
