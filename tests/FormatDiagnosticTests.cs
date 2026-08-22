using System;

namespace BTOptimizer.Tests
{
    /// <summary>
    /// Vérifie <see cref="FormatDiagnostic"/> — la moitié pure du rapport de diagnostic.
    ///
    /// Ces fonctions décident de ce qui PART d'une machine. C'est la raison pour laquelle elles
    /// ont été sorties de RapportDiagnostic : tant qu'elles cohabitaient avec un appel à
    /// SelfCheck, les vérifier revenait à compiler tout ONYX ici.
    /// </summary>
    internal static class FormatDiagnosticTests
    {
        public static void Tout()
        {
            CodeDeReference();
            Sections();
            NettoyageDuTexte();
            CorpsDeRequete();
            Troncature();
            MessagesDIssue();
        }

        /// <summary>
        /// Le code se recopie à la main dans un message et se dit parfois à l'oral. Deux clics
        /// pour le même incident doivent donner le même code, sinon on ne retrouve rien.
        /// </summary>
        private static void CodeDeReference()
        {
            Banc.Titre("Code de référence");

            DateTime t = new DateTime(2026, 8, 15, 17, 15, 30);
            string a = FormatDiagnostic.Reference("machine-A", t);

            Banc.Verifie("il commence par ONYX-", true, a.StartsWith("ONYX-", StringComparison.Ordinal));
            Banc.Verifie("il fait 11 caractères", true, a.Length == 11);

            // Stable A LA MINUTE : deux clics de suite ne doivent pas ouvrir deux dossiers.
            Banc.Egal("stable à la seconde près dans la même minute",
                a, FormatDiagnostic.Reference("machine-A", t.AddSeconds(20)));
            Banc.Verifie("mais change de minute en minute", true,
                a != FormatDiagnostic.Reference("machine-A", t.AddMinutes(1)));
            Banc.Verifie("et change de machine en machine", true,
                a != FormatDiagnostic.Reference("machine-B", t));

            // I, O, 0 et 1 sont bannis : « ONYX-I0 » se lit de trois facons.
            string caracteres = a.Substring(5);
            bool ambigu = caracteres.IndexOf('I') >= 0 || caracteres.IndexOf('O') >= 0
                       || caracteres.IndexOf('0') >= 0 || caracteres.IndexOf('1') >= 0;
            Banc.Verifie("aucun caractère ambigu à l'oral ou à la recopie", false, ambigu);

            Banc.Verifie("une graine vide ne lève pas", true,
                FormatDiagnostic.Reference("", t).Length == 11);
            Banc.Verifie("une graine nulle non plus", true,
                FormatDiagnostic.Reference(null, t).Length == 11);
        }

        private static void Sections()
        {
            Banc.Titre("Sections");

            Banc.Verifie("une section pleine porte son titre", true,
                FormatDiagnostic.Section("TITRE", "contenu").Contains("===== TITRE"));

            // Une section vide dans un dossier de support fait croire a une panne de la collecte.
            Banc.Egal("une section vide n'existe pas", "", FormatDiagnostic.Section("TITRE", ""));
            Banc.Egal("une section d'espaces non plus", "", FormatDiagnostic.Section("TITRE", "   \r\n  "));
            Banc.Egal("un contenu nul non plus", "", FormatDiagnostic.Section("TITRE", null));
        }

        /// <summary>
        /// Le bloc « infos de support » promet noir sur blanc qu'il ne contient ni nom
        /// d'utilisateur ni chemin privé. Un rapport qu'on n'ose pas envoyer ne sert à rien.
        /// </summary>
        private static void NettoyageDuTexte()
        {
            Banc.Titre("Nettoyage avant envoi");

            string brut = "ligne 1 : C:\\Users\\Lucas\\Desktop\r\n"
                        + "ligne 2 : session de Lucas\r\n"
                        + "ligne 3 : rien à masquer";
            string net = FormatDiagnostic.Nettoie(brut, "Lucas", "C:\\Users\\Lucas");

            Banc.Verifie("plus aucune trace du nom", false, net.Contains("Lucas"));
            Banc.Verifie("le chemin de profil est masqué", true, net.Contains("%PROFIL%"));
            Banc.Verifie("le nom seul est masqué", true, net.Contains("%UTILISATEUR%"));
            Banc.Verifie("les lignes sans rien à masquer sont intactes",
                true, net.Contains("rien à masquer"));
            Banc.Verifie("le nombre de lignes est conservé", true,
                net.Split('\n').Length == 3);

            Banc.Egal("texte vide", "", FormatDiagnostic.Nettoie("", "Lucas", "C:\\Users\\Lucas"));
            Banc.Egal("texte nul ne lève pas", "", FormatDiagnostic.Nettoie(null, "Lucas", "C:\\Users\\Lucas"));
        }

        /// <summary>
        /// Le corps exact de la requête est ce qu'on veut pouvoir vérifier : c'est lui qui part
        /// de la machine. Trois champs, et rien d'autre.
        /// </summary>
        private static void CorpsDeRequete()
        {
            Banc.Titre("Corps de la requête d'envoi");

            Banc.Egal("les guillemets sont échappés",
                "il a dit \\\"non\\\"", FormatDiagnostic.EchappeJson("il a dit \"non\""));
            Banc.Egal("l'antislash aussi",
                "C:\\\\Windows", FormatDiagnostic.EchappeJson("C:\\Windows"));
            Banc.Egal("les sauts de ligne aussi",
                "a\\r\\nb", FormatDiagnostic.EchappeJson("a\r\nb"));
            Banc.Egal("les caractères de contrôle passent en \\u",
                "a\\u0001b", FormatDiagnostic.EchappeJson("a\u0001b"));

            // Les accents NE sont PAS echappes : ils sont valides en JSON UTF-8, et les detruire
            // rendrait un rapport francais illisible a l'arrivee.
            Banc.Egal("les accents sont préservés", "éàç", FormatDiagnostic.EchappeJson("éàç"));
            Banc.Egal("texte vide", "", FormatDiagnostic.EchappeJson(""));
            Banc.Egal("texte nul ne lève pas", "", FormatDiagnostic.EchappeJson(null));

            string c = FormatDiagnostic.Charge("ONYX-ABCDEF", "15.74.0.0", "du \"texte\"");
            Banc.Verifie("la charge porte les trois champs, et rien d'autre", true,
                c.Contains("\"reference\":\"ONYX-ABCDEF\"")
                && c.Contains("\"version\":\"15.74.0.0\"")
                && c.Contains("\"rapport\":\"du \\\"texte\\\"\""));
            Banc.Verifie("elle est bien fermée", true, c.StartsWith("{") && c.EndsWith("}"));
        }

        private static void Troncature()
        {
            Banc.Titre("Troncature");

            Banc.Verifie("un texte normal n'est pas trop gros",
                false, FormatDiagnostic.TropGros("court"));
            Banc.Verifie("texte nul : pas trop gros, et pas d'exception",
                false, FormatDiagnostic.TropGros(null));

            // On compte les OCTETS : un rapport plein d'accents pese plus qu'il n'en a l'air.
            string accents = new string('é', FormatDiagnostic.MaxOctets / 2 + 10);
            Banc.Verifie("les accents comptent double en UTF-8",
                true, FormatDiagnostic.TropGros(accents));

            string gros = new string('x', FormatDiagnostic.MaxOctets + 5000);
            string coupe = FormatDiagnostic.Tronque(gros);
            Banc.Verifie("le texte coupé tient sous la limite",
                false, FormatDiagnostic.TropGros(coupe));
            // Un dossier ampute EN SILENCE fait chercher une section jamais envoyee.
            Banc.Verifie("et il DIT qu'il a été coupé", true, coupe.Contains("tronqué"));

            Banc.Egal("un texte qui tient n'est pas touché", "court", FormatDiagnostic.Tronque("court"));
        }

        private static void MessagesDIssue()
        {
            Banc.Titre("Messages d'issue");

            Banc.Verifie("un envoi réussi communique la référence", true,
                FormatDiagnostic.Explique(FormatDiagnostic.Resultat.Envoye, "ONYX-ABCDEF")
                    .Contains("ONYX-ABCDEF"));

            // Les trois issues NON réussies doivent toutes rassurer sur le meme point : rien
            // n'est parti, et le rapport reste chez la personne.
            FormatDiagnostic.Resultat[] ratees = {
                FormatDiagnostic.Resultat.RefusePersonnel,
                FormatDiagnostic.Resultat.Inerte,
                FormatDiagnostic.Resultat.Echec
            };
            foreach (FormatDiagnostic.Resultat r in ratees)
            {
                string m = FormatDiagnostic.Explique(r, "ONYX-ABCDEF");
                Banc.Verifie("« " + r + " » dit que rien n'est parti", true,
                    m.Contains("rien n'est parti") || m.Contains("n'a été envoyé")
                    || m.Contains("Rien n'est parti"));
                Banc.Verifie("« " + r + " » dit que le rapport reste chez l'utilisateur", true,
                    m.Contains("enregistré") || m.Contains("chez toi"));
            }

            Banc.Verifie("aucun message ne contient de code technique", false,
                FormatDiagnostic.Explique(FormatDiagnostic.Resultat.Echec, "X").Contains("HTTP"));
        }
    }
}
