using System;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie la LIAISON AUTOMATIQUE d'une clé à son PC.
    ///
    /// Le cœur du sujet n'est pas technique, il est arbitral, et ce banc est là pour le figer :
    /// hors ligne, « le client a réinstallé Windows » et « le client a donné sa clé » produisent le
    /// MÊME signal — même clé, identifiant de machine différent. L'identifiant vient du MachineGuid,
    /// que Windows régénère à chaque réinstallation : le cas légitime EST le cas suspect.
    ///
    /// Le produit choisit d'accepter la re-liaison et d'écrire l'histoire. Le jour où quelqu'un
    /// voudra « durcir » ça, c'est ce fichier qui dira ce que ça casse — un client qui a payé,
    /// bloqué le soir où il vient de réinstaller son PC.
    ///
    /// Rien ici ne touche au disque : les fonctions vérifiées sont pures.
    /// </summary>
    internal static class LicenceLiaisonTests
    {
        private const string PcA = "A3F7K-M2NPQ-R8TUV-W9XYZ";
        private const string PcB = "K4M9P-QR2ST-UV7WX-YZ34B";
        private const string Jeton = "ZGVzdGluZ29vZB9nS01v";   // forme seule, le contenu importe peu ici

        public static void Tout()
        {
            AllerRetour();
            AncienFormat();
            Politique();
            Historique();
        }

        private static void AllerRetour()
        {
            Banc.Titre("L'enregistrement de licence");

            var e = new LicenceLiaison.Enregistrement { Token = Jeton, Machine = PcA, Depuis = new DateTime(2026, 8, 22) };
            e.Machines.Add(PcA);

            LicenceLiaison.Enregistrement relu = LicenceLiaison.Lit(LicenceLiaison.Rend(e));
            Banc.Egal("le jeton survit à l'aller-retour", Jeton, relu.Token);
            Banc.Egal("le PC lié aussi", PcA, relu.Machine);
            Banc.Verifie("la date de première activation aussi", true, relu.Depuis == new DateTime(2026, 8, 22));
            Banc.Verifie("l'historique aussi", true, relu.Machines.Count == 1 && relu.Machines[0] == PcA);

            Banc.Verifie("un texte vide ne rend rien, sans exception", true, LicenceLiaison.Lit("") == null);
            Banc.Verifie("un texte sans jeton non plus", true, LicenceLiaison.Lit("machine=" + PcA) == null);
            Banc.Egal("rendre un enregistrement nul ne lève pas d'exception", "", LicenceLiaison.Rend(null));
        }

        /// <summary>Toutes les installations existantes contiennent un fichier d'UNE ligne : le
        /// jeton seul. Ne pas le relire, c'est déconnecter tout le parc d'un coup.</summary>
        private static void AncienFormat()
        {
            Banc.Titre("L'ancien format — un jeton tout seul");

            LicenceLiaison.Enregistrement e = LicenceLiaison.Lit(Jeton);
            Banc.Verifie("un fichier d'une seule ligne reste lisible", true, e != null);
            Banc.Egal("et c'est bien le jeton", Jeton, e.Token);
            Banc.Verifie("il n'est lié à aucun PC — donc la prochaine activation le liera", true,
                LicenceLiaison.Decide(e, PcA) == LicenceLiaison.Liaison.Premiere);

            // Les espaces et les fins de ligne d'un copier-coller ne doivent rien casser.
            LicenceLiaison.Enregistrement f = LicenceLiaison.Lit("  " + Jeton + "  \r\n\r\n");
            Banc.Egal("les espaces d'un copier-coller sont absorbés", Jeton, f.Token);
        }

        private static void Politique()
        {
            Banc.Titre("La politique de liaison");

            var neuf = new LicenceLiaison.Enregistrement { Token = Jeton };
            Banc.Verifie("une clé jamais liée se lie à la première activation", true,
                LicenceLiaison.Decide(neuf, PcA) == LicenceLiaison.Liaison.Premiere);

            LicenceLiaison.Relie(neuf, PcA, new DateTime(2026, 8, 22));
            Banc.Egal("elle retient le PC", PcA, neuf.Machine);
            Banc.Verifie("et la date", true, neuf.Depuis == new DateTime(2026, 8, 22));

            Banc.Verifie("relancée sur le même PC, rien ne bouge", true,
                LicenceLiaison.Decide(neuf, PcA) == LicenceLiaison.Liaison.Meme);

            // LE cas : Windows réinstallé (ou clé partagée — indiscernable hors ligne).
            Banc.Verifie("sur un autre PC, elle se RELIE au lieu de refuser", true,
                LicenceLiaison.Decide(neuf, PcB) == LicenceLiaison.Liaison.Rebind);

            LicenceLiaison.Relie(neuf, PcB, new DateTime(2026, 9, 1));
            Banc.Egal("le PC courant devient le nouveau", PcB, neuf.Machine);
            Banc.Verifie("mais la date de PREMIÈRE activation ne bouge pas", true,
                neuf.Depuis == new DateTime(2026, 8, 22));

            Banc.Verifie("relier sans identifiant de machine ne casse rien", true,
                LicenceLiaison.Relie(neuf, "", DateTime.Now).Machine == PcB);
            Banc.Verifie("décider sur un enregistrement nul ne lève pas d'exception", true,
                LicenceLiaison.Decide(null, PcA) == LicenceLiaison.Liaison.Premiere);
        }

        /// <summary>L'histoire est la SEULE chose que ce système offre au vendeur. Si elle ne
        /// s'accumule pas, la re-liaison devient un blanc-seing muet.</summary>
        private static void Historique()
        {
            Banc.Titre("L'histoire des PC, seule trace exploitable");

            var e = new LicenceLiaison.Enregistrement { Token = Jeton };
            LicenceLiaison.Relie(e, PcA, DateTime.Now);
            LicenceLiaison.Relie(e, PcB, DateTime.Now);
            LicenceLiaison.Relie(e, PcA, DateTime.Now);   // retour sur le premier PC

            Banc.Verifie("deux PC distincts sont retenus", true, e.Machines.Count == 2);
            Banc.Verifie("un PC déjà vu n'est pas compté deux fois", true,
                e.Machines[0] == PcA && e.Machines[1] == PcB);

            LicenceLiaison.Enregistrement relu = LicenceLiaison.Lit(LicenceLiaison.Rend(e));
            Banc.Verifie("l'histoire survit à l'écriture et à la relecture", true, relu.Machines.Count == 2);
        }
    }
}
