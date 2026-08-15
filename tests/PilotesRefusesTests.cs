using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="PilotesRefuses"/>.
    ///
    /// Deux parties, et la distinction compte. Les ASSERTIONS portent sur l'analyse pure : elles
    /// valent sur n'importe quelle machine et doivent toutes passer, toujours. Le CONSTAT, lui,
    /// lit la machine courante et ne juge rien — sur un poste où aucun pilote n'est refusé, il
    /// n'affiche rien, et c'est un résultat correct, pas un échec. Faire échouer le banc parce
    /// que la machine va bien serait le meilleur moyen de le voir désactivé au premier CI.
    /// </summary>
    internal static class PilotesRefusesTests
    {
        public static void Tout()
        {
            AuditNEstPasBlocage();
            CauseEtIssue();
            RienADire();
            RattachementDesOutils();
            Pliage();
            ConstatSurCetteMachine();
        }

        // ==================================================================
        //  Assertions — vraies sur toute machine
        // ==================================================================

        /// <summary>
        /// LA distinction du module. Windows tient des stratégies qui APPLIQUENT (3077) et des
        /// stratégies qui OBSERVENT (3076) ; le même fichier déclenche souvent les deux. Compter
        /// les secondes comme des blocages ferait crier au pilote cassé sur des machines où tout
        /// fonctionne.
        /// </summary>
        private static void AuditNEstPasBlocage()
        {
            Banc.Titre("Un signalement en audit n'est pas un blocage");

            var audite = new PilotesRefuses.Refus
            {
                Fichier = "observe.sys", Audits = 12, Blocages = 0,
                PlusRecent = new DateTime(2026, 8, 15)
            };
            Banc.Verifie("audité seul : pas bloquant", false, PilotesRefuses.EstBloquant(audite));
            Banc.Verifie("audité seul : aucun rapport", true,
                PilotesRefuses.Rapport(new List<PilotesRefuses.Refus> { audite }).Length == 0);

            var lesDeux = new PilotesRefuses.Refus
            {
                Fichier = "rspLLL64.sys", Audits = 7, Blocages = 7,
                PlusAncien = new DateTime(2026, 7, 24), PlusRecent = new DateTime(2026, 8, 15)
            };
            Banc.Verifie("audité ET bloqué : bloquant", true, PilotesRefuses.EstBloquant(lesDeux));
        }

        /// <summary>
        /// Ce qui sépare « à réparer » d'un « sans issue ». Envoyer réinstaller quand la cause
        /// est dans la signature fait perdre une heure pour rien.
        /// </summary>
        private static void CauseEtIssue()
        {
            Banc.Titre("Cause et issue");

            var perime = new PilotesRefuses.Refus
            {
                Fichier = "vieux.sys", Blocages = 3, Present = true,
                PlusAncien = new DateTime(2026, 7, 24), PlusRecent = new DateTime(2026, 8, 15),
                FinCertificat = new DateTime(2019, 5, 27), Algorithme = "sha1RSA"
            };
            Banc.Verifie("certificat expiré avant le refus : périmé",
                true, PilotesRefuses.CertificatPerime(perime));
            Banc.Verifie("sha1RSA : signature obsolète",
                true, PilotesRefuses.SignatureObsolete(perime));
            Banc.Verifie("les deux réunis : sans issue", true, PilotesRefuses.SansIssue(perime));

            var valide = new PilotesRefuses.Refus
            {
                Fichier = "neuf.sys", Blocages = 1, Present = true,
                PlusAncien = new DateTime(2026, 8, 1), PlusRecent = new DateTime(2026, 8, 15),
                FinCertificat = new DateTime(2030, 1, 1), Algorithme = "sha256RSA"
            };
            Banc.Verifie("certificat encore valide : pas périmé",
                false, PilotesRefuses.CertificatPerime(valide));
            Banc.Verifie("sha256RSA : pas obsolète",
                false, PilotesRefuses.SignatureObsolete(valide));
            Banc.Verifie("bloqué sans cause connue : pas « sans issue »",
                false, PilotesRefuses.SansIssue(valide));
            Banc.Verifie("... mais bien bloquant", true, PilotesRefuses.EstBloquant(valide));

            // Un certificat qui EXPIRE APRÈS le dernier refus n'explique pas ce refus. Le dire
            // quand même enverrait accuser l'éditeur à tort.
            var expireApres = new PilotesRefuses.Refus
            {
                Fichier = "apres.sys", Blocages = 1,
                PlusRecent = new DateTime(2026, 8, 15), FinCertificat = new DateTime(2027, 1, 1),
                Algorithme = "sha256RSA", Present = true
            };
            Banc.Verifie("certificat expirant après le refus : pas la cause",
                false, PilotesRefuses.CertificatPerime(expireApres));
            Banc.Egal("... et aucune cause affirmée", "", PilotesRefuses.Cause(expireApres));

            var absent = new PilotesRefuses.Refus
            {
                Fichier = "parti.sys", Blocages = 2, Present = false,
                PlusRecent = new DateTime(2026, 8, 15)
            };
            Banc.Egal("fichier disparu : la cause le dit",
                "le fichier n'est plus sur le disque", PilotesRefuses.Cause(absent));
        }

        /// <summary>Le silence est une réponse. Une machine saine n'a pas besoin d'un paragraphe
        /// pour le lui dire.</summary>
        private static void RienADire()
        {
            Banc.Titre("Quand il n'y a rien à dire");

            Banc.Verifie("liste vide : aucun rapport", true,
                PilotesRefuses.Rapport(new List<PilotesRefuses.Refus>()).Length == 0);
            Banc.Verifie("liste nulle : aucun rapport, et aucune exception", true,
                PilotesRefuses.Rapport(null).Length == 0);
            Banc.Verifie("EstBloquant(null) ne lève pas", false, PilotesRefuses.EstBloquant(null));
            Banc.Egal("Cause(null) ne rend rien", "", PilotesRefuses.Cause(null));
            Banc.Verifie("OutilBloque sur liste nulle", false,
                PilotesRefuses.OutilBloque(null, "LatencyMon"));
        }

        private static void RattachementDesOutils()
        {
            Banc.Titre("Rattachement aux outils recommandés par ONYX");

            var latmon = new PilotesRefuses.Refus { Fichier = "rspLLL64.sys", Blocages = 1 };
            Banc.Egal("rspLLL64.sys est le pilote de LatencyMon",
                "LatencyMon", PilotesRefuses.OutilQuiEnDepend(latmon));
            // Le motif est un FRAGMENT : la variante 32 bits doit être reconnue aussi.
            Banc.Egal("rspLLL32.sys également",
                "LatencyMon", PilotesRefuses.OutilQuiEnDepend(
                    new PilotesRefuses.Refus { Fichier = "rspLLL32.sys", Blocages = 1 }));
            Banc.Verifie("LatencyMon a un remplacement à proposer",
                true, PilotesRefuses.Remplacement("LatencyMon").Length > 0);
            Banc.Egal("un pilote inconnu n'est rattaché à rien",
                "", PilotesRefuses.OutilQuiEnDepend(
                    new PilotesRefuses.Refus { Fichier = "inconnu.sys", Blocages = 1 }));
            Banc.Egal("aucun remplacement pour un outil inconnu",
                "", PilotesRefuses.Remplacement("OutilQuiNExistePas"));

            Banc.Verifie("OutilBloque voit LatencyMon bloqué", true,
                PilotesRefuses.OutilBloque(new List<PilotesRefuses.Refus> { latmon }, "LatencyMon"));
            Banc.Verifie("OutilBloque ne le voit PAS s'il n'est qu'audité", false,
                PilotesRefuses.OutilBloque(new List<PilotesRefuses.Refus> {
                    new PilotesRefuses.Refus { Fichier = "rspLLL64.sys", Blocages = 0, Audits = 5 } },
                    "LatencyMon"));
        }

        private static void Pliage()
        {
            Banc.Titre("Pliage des lignes");

            Banc.Egal("ne coupe aucun mot",
                "> alpha beta" + Environment.NewLine + "> gamma delta" + Environment.NewLine,
                PilotesRefuses.Plie("alpha beta gamma delta", 11, "> "));
            Banc.Egal("un mot plus long que la largeur garde sa ligne entière",
                "rspLLL64.sys" + Environment.NewLine,
                PilotesRefuses.Plie("rspLLL64.sys", 4, ""));
            Banc.Egal("texte vide : rien", "", PilotesRefuses.Plie("", 10, "  "));
            Banc.Egal("texte nul : rien, et aucune exception", "", PilotesRefuses.Plie(null, 10, "  "));

            // Le rapport s'affiche dans une zone de texte à chasse fixe : une ligne trop longue
            // y ajoute une barre de défilement horizontale et rend le tout illisible.
            var perime = new PilotesRefuses.Refus
            {
                Fichier = "rspLLL64.sys", Blocages = 7, Present = true,
                Produit = "LatMon", Editeur = "Resplendence Software Projects Sp.",
                PlusAncien = new DateTime(2026, 7, 24), PlusRecent = new DateTime(2026, 8, 15, 17, 15, 0),
                FinCertificat = new DateTime(2019, 5, 27), Algorithme = "sha1RSA"
            };
            int tropLongues = 0;
            foreach (string ligne in PilotesRefuses.Rapport(
                         new List<PilotesRefuses.Refus> { perime }).Split('\n'))
                if (ligne.TrimEnd('\r').Length > 80) tropLongues++;
            Banc.Verifie("aucune ligne du rapport ne dépasse 80 colonnes", true, tropLongues == 0);
        }

        // ==================================================================
        //  Constat — lit CETTE machine, ne juge rien
        // ==================================================================

        private static void ConstatSurCetteMachine()
        {
            Banc.Titre("Constat sur cette machine (informatif, ne fait échouer aucun test)");

            var chrono = Stopwatch.StartNew();
            List<PilotesRefuses.Refus> l = PilotesRefuses.Lire();
            chrono.Stop();

            Console.WriteLine("  lecture du journal d'intégrité : " + chrono.ElapsedMilliseconds
                + " ms, " + l.Count + " fichier(s) concerné(s)");

            if (l.Count == 0)
            {
                Console.WriteLine("  aucun refus enregistré — rien à signaler, et c'est correct.");
                return;
            }

            foreach (PilotesRefuses.Refus r in l)
            {
                Console.WriteLine();
                Console.WriteLine("  " + r.Fichier
                    + "   bloqué " + r.Blocages + " / audité " + r.Audits);
                Console.WriteLine("     " + (r.Present ? r.Chemin : "(fichier introuvable)"));
                Console.WriteLine("     " + r.Produit + " | " + r.Editeur + " | " + r.Version);
                Console.WriteLine("     " + r.Algorithme + ", fin "
                    + r.FinCertificat.ToString("dd/MM/yyyy")
                    + " | service " + r.Service + " (" + r.EtatService + ")");
                Console.WriteLine("     cause : " + PilotesRefuses.Cause(r));
            }

            string rapport = PilotesRefuses.Rapport(l);
            if (rapport.Length > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  --- rapport tel que l'utilisateur le verra ---");
                Console.WriteLine(rapport);
            }
        }
    }
}
