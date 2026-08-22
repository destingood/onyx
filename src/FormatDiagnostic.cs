using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// LA MOITIÉ PURE DU RAPPORT DE DIAGNOSTIC.
    ///
    /// Les autres modules d'ONYX séparent l'analyse pure de la lecture machine par un simple
    /// commentaire, et ça suffit : leur moitié impure ne dépend que du registre ou d'un journal.
    ///
    /// Ici non. RapportDiagnostic appelle SelfCheck, qui tire Diagnostics, CrashScan, LocalBrain —
    /// la moitié de l'application. Tant que les décisions pures vivaient dans le même fichier, les
    /// vérifier revenait à compiler tout ONYX dans le banc d'essai. Le commentaire ne suffisait
    /// plus : il fallait une frontière de FICHIER.
    ///
    /// D'où cette classe. Elle ne lit rien, n'écrit rien, ne joint personne. Tout ce qui décide
    /// de la FORME d'un rapport et du SENS d'un envoi est ici, donc vérifiable en une milliseconde
    /// sans machine, sans réseau et sans droits.
    ///
    /// La règle qui la garde honnête : si une fonction d'ici a besoin d'un fichier, d'une horloge
    /// ou du réseau, elle n'a rien à y faire — c'est le signe qu'elle appartient à l'autre moitié.
    /// L'heure et le nom d'utilisateur entrent donc par paramètre, jamais par appel.
    /// </summary>
    internal static class FormatDiagnostic
    {
        // ==================================================================
        //  Référence
        // ==================================================================

        /// <summary>
        /// PUR : le code de référence. Court, sans ambiguïté à l'oral, et STABLE pour une même
        /// graine et une même minute — deux clics de suite ne doivent pas produire deux dossiers
        /// différents pour le même incident.
        ///
        /// Alphabet volontairement amputé de I, O, 0 et 1 : ce code se recopie à la main dans un
        /// message, et « ONYX-I0 » se lit de trois façons.
        /// </summary>
        public static string Reference(string graine, DateTime quand)
        {
            const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            string entree = (graine ?? "") + "|"
                          + quand.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("onyx-diag-v1:" + entree));
                var sb = new StringBuilder("ONYX-");
                for (int i = 0; i < 6; i++) sb.Append(Alphabet[h[i] % Alphabet.Length]);
                return sb.ToString();
            }
        }

        // ==================================================================
        //  Mise en forme
        // ==================================================================

        /// <summary>PUR : une section titrée, ou une chaîne vide si elle n'a rien à dire. Une
        /// section vide dans un dossier de support fait croire à une panne de la collecte.</summary>
        public static string Section(string titre, string contenu)
        {
            if (string.IsNullOrWhiteSpace(contenu)) return "";
            var sb = new StringBuilder();
            sb.Append("===== ").Append(titre).Append(Environment.NewLine);
            sb.Append(contenu.TrimEnd()).Append(Environment.NewLine).Append(Environment.NewLine);
            return sb.ToString();
        }

        /// <summary>PUR : masque, ligne à ligne, ce qui identifie le propriétaire de la machine.
        /// Ligne à ligne et non d'un bloc : le masquage travaille sur des chaînes courtes, et un
        /// texte de 50 Ko en une passe n'apporte rien de plus.</summary>
        public static string Nettoie(string texte, string utilisateur, string profil)
        {
            if (string.IsNullOrEmpty(texte)) return "";
            string[] lignes = texte.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder(texte.Length);
            for (int i = 0; i < lignes.Length; i++)
            {
                if (i > 0) sb.Append(Environment.NewLine);
                sb.Append(JournalTechnique.SansDonneesPerso(lignes[i], utilisateur, profil));
            }
            return sb.ToString();
        }

        // ==================================================================
        //  Corps de la requête d'envoi
        // ==================================================================

        /// <summary>Au-delà, le serveur refuserait de toute façon. Tronquer AVANT d'envoyer donne
        /// un message clair plutôt qu'une erreur HTTP incompréhensible.</summary>
        public const int MaxOctets = 256 * 1024;

        /// <summary>
        /// PUR : échappement JSON d'un texte libre.
        ///
        /// Audience.Echappe écarte tout ce qui n'est pas alphanumérique — parfait pour trois
        /// champs contrôlés, destructeur pour un rapport entier. Ici on échappe vraiment : les
        /// guillemets, l'antislash, les sauts de ligne, et les caractères de contrôle qui
        /// rendraient le JSON invalide sans qu'on comprenne pourquoi.
        /// </summary>
        public static string EchappeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 32);
            foreach (char c in s)
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            return sb.ToString();
        }

        /// <summary>PUR : le corps envoyé. Trois champs, et rien de plus — vérifiable ici même,
        /// ce qui est tout l'intérêt de garder cette fonction pure et hors du module qui poste.</summary>
        public static string Charge(string reference, string version, string texte)
        {
            return "{\"reference\":\"" + EchappeJson(reference) + "\","
                 + "\"version\":\"" + EchappeJson(version) + "\","
                 + "\"rapport\":\"" + EchappeJson(texte) + "\"}";
        }

        /// <summary>PUR : ce rapport est-il trop gros ? On compte les OCTETS après encodage,
        /// pas les caractères : un rapport plein d'accents pèse davantage qu'il n'en a l'air.</summary>
        public static bool TropGros(string texte)
        {
            if (string.IsNullOrEmpty(texte)) return false;
            return Encoding.UTF8.GetByteCount(texte) > MaxOctets;
        }

        /// <summary>PUR : tronque à la taille maximale et DIT qu'il a tronqué. Un dossier amputé
        /// de sa fin reste exploitable ; un dossier amputé en silence fait chercher une section
        /// qui n'a jamais été envoyée.</summary>
        public static string Tronque(string texte)
        {
            if (!TropGros(texte)) return texte;
            const string Mention = "[rapport tronqué : taille maximale atteinte]";
            int max = MaxOctets - Encoding.UTF8.GetByteCount(Mention) - 8;
            string t = texte;
            while (Encoding.UTF8.GetByteCount(t) > max && t.Length > 0)
                t = t.Substring(0, t.Length * 9 / 10);
            return t + Environment.NewLine + Mention;
        }

        // ==================================================================
        //  Issues d'un envoi
        // ==================================================================

        public enum Resultat
        {
            /// <summary>Aucun point de collecte : rien n'a été tenté, et ce n'est pas une erreur.</summary>
            Inerte,
            /// <summary>Des données personnelles subsistaient : envoi refusé.</summary>
            RefusePersonnel,
            /// <summary>Le rapport est parti et a été accepté.</summary>
            Envoye,
            /// <summary>Réseau, serveur ou délai : rien n'est parti, le fichier local demeure.</summary>
            Echec
        }

        /// <summary>PUR : le message montré à l'utilisateur pour chaque issue. Aucun code
        /// technique : la personne en face n'a pas à traduire un numéro d'erreur.</summary>
        public static string Explique(Resultat r, string reference)
        {
            switch (r)
            {
                case Resultat.Envoye:
                    return "Rapport envoyé. Communique cette référence : " + reference;
                case Resultat.RefusePersonnel:
                    return "Envoi REFUSÉ : le rapport contient encore des données personnelles. "
                         + "Il reste enregistré chez toi, et rien n'est parti.";
                case Resultat.Inerte:
                    return "Aucun point de collecte n'est configuré dans cette version : rien "
                         + "n'a été envoyé. Le rapport est enregistré, tu peux le transmettre "
                         + "toi-même.";
                default:
                    return "L'envoi a échoué (réseau ou serveur). Rien n'est parti. Le rapport "
                         + "est enregistré : tu peux le transmettre toi-même.";
            }
        }
    }
}
