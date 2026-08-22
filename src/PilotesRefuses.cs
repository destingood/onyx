using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace BTOptimizer
{
    /// <summary>
    /// LES PILOTES QUE WINDOWS REFUSE DE CHARGER.
    ///
    /// Ce module existe à cause d'une panne qui ne ressemble à rien. Un outil s'installe sans
    /// erreur, apparaît dans le menu Démarrer, se lance — et ne mesure rien, ou sort une boîte
    /// « Windows ne peut pas vérifier la signature numérique de ce fichier ». Le Gestionnaire de
    /// périphériques est vert. Le service existe. Rien dans l'interface de Windows ne dit que
    /// quoi que ce soit a été refusé.
    ///
    /// CE QUI SE PASSE VRAIMENT :
    ///
    ///   Windows applique des stratégies d'intégrité du code qu'il recharge à CHAQUE démarrage,
    ///   sans rien demander à personne. Un pilote signé il y a dix ans avec un certificat depuis
    ///   expiré, ou avec une chaîne de certificats croisés que Microsoft n'accepte plus, cesse
    ///   d'être chargeable du jour au lendemain. L'éditeur n'a rien fait. L'utilisateur non plus.
    ///   La machine a simplement reçu une mise à jour de stratégie.
    ///
    ///   Windows AFFICHE bien une notification — « Ce pilote a été bloqué : rspLLL64.sys ne
    ///   respecte pas la stratégie de… » — et il la réaffiche à chaque tentative. Il faut le
    ///   dire, parce qu'une première version de ce module prétendait que le refus n'était
    ///   visible nulle part, ce qui était faux.
    ///
    ///   Mais regarde ce que cette notification NE dit pas : quel logiciel a tenté de charger ce
    ///   pilote, pourquoi il a été refusé, et si c'est réparable. Elle est tronquée, elle
    ///   disparaît, et elle nomme un fichier .sys que personne ne peut relier à l'application
    ///   qu'il fait vivre. L'utilisateur voit passer un avertissement obscur, hausse les épaules,
    ///   et continue de croire que son outil de mesure fonctionne.
    ///
    ///   Le détail, lui, n'est QUE dans le journal d'intégrité du code : le nombre de refus,
    ///   depuis quand, sous quelle stratégie. C'est là qu'on va le chercher, et c'est en le
    ///   croisant avec le fichier lui-même qu'on peut enfin nommer le produit et la cause.
    ///
    /// POURQUOI ÇA NOUS REGARDE :
    ///
    ///   ONYX recommande des outils. Recommander un outil dont le pilote ne peut pas se charger,
    ///   c'est envoyer quelqu'un passer une heure à réinstaller, redémarrer, désinstaller,
    ///   réinstaller — pour une cause qui n'est pas de son côté et qu'aucun de ces gestes ne
    ///   touche. C'est le même défaut que proposer de répartir des interruptions qui ne peuvent
    ///   pas l'être : un conseil impossible coûte plus cher que pas de conseil du tout.
    ///
    /// CE QUE CE MODULE REFUSE DE FAIRE :
    ///
    ///   Contourner. Il existe des moyens de forcer le chargement — désactiver l'intégrité du
    ///   code, activer la signature de test, retirer la stratégie. Ce sont des abaissements de
    ///   sécurité durables, à l'échelle de toute la machine, pour faire tourner UN outil. Ce
    ///   module ne les propose pas, ne les documente pas et ne les applique pas. Quand un pilote
    ///   est refusé, la bonne réponse est de se passer de l'outil — surtout quand ONYX sait
    ///   faire la même mesure sans pilote du tout.
    ///
    ///   Confondre « audité » et « bloqué ». Windows tient DEUX familles de stratégies : celles
    ///   qui appliquent (événement 3077) et celles qui observent sans rien empêcher (3076). Le
    ///   même fichier déclenche souvent les deux, sous deux stratégies différentes. Compter les
    ///   3076 comme des blocages ferait crier au pilote cassé sur des machines où tout marche.
    ///   Seul le 3077 fait foi ; le 3076 est noté à part et dit à voix basse.
    ///
    ///   Dater le premier refus. Le journal d'événements est circulaire : son plus ancien
    ///   enregistrement n'est pas le premier qui a eu lieu, c'est le plus ancien qui n'a pas
    ///   encore été écrasé. On écrit donc « au moins depuis », jamais « depuis ».
    /// </summary>
    internal static class PilotesRefuses
    {
        // ==================================================================
        //  Ce que le journal d'intégrité du code sait dire
        // ==================================================================

        /// <summary>Refus APPLIQUÉ : le pilote n'a pas été chargé. Seul événement qui fait foi.</summary>
        public const int IdBloque = 3077;

        /// <summary>Stratégie en AUDIT : signalé, mais l'image a quand même été chargée.
        /// Ne jamais présenter comme une panne — voir la note d'en-tête.</summary>
        public const int IdAudite = 3076;

        /// <summary>Détail « impossible de vérifier l'intégrité de l'image ». C'est le texte que
        /// l'utilisateur voit dans la boîte de dialogue, mot pour mot.</summary>
        public const int IdIntegrite = 3004;

        private const string JournalIntegrite = "Microsoft-Windows-CodeIntegrity/Operational";

        /// <summary>Plafond de lecture. Le journal peut contenir des milliers d'entrées ; au-delà
        /// on n'apprend plus rien de neuf et on fait attendre l'utilisateur pour rien.</summary>
        private const int MaxEvenements = 4000;

        /// <summary>Non mesuré — à ne jamais confondre avec zéro.</summary>
        public const int Inconnu = -1;

        // ==================================================================
        //  Ce qu'on relève
        // ==================================================================

        public sealed class Refus
        {
            public string Fichier = "";          // rspLLL64.sys
            public string Chemin = "";           // résolu sur le disque, vide si introuvable
            public bool Present;                 // le fichier est-il encore là ?

            /// <summary>Nombre de refus APPLIQUÉS (3077). Zéro = pas un blocage.</summary>
            public int Blocages;
            /// <summary>Nombre de signalements en AUDIT (3076). N'empêche rien.</summary>
            public int Audits;

            public DateTime PlusAncien, PlusRecent;

            public string Produit = "";          // « LatMon »
            public string Editeur = "";          // « Resplendence Software Projects Sp. »
            public string Version = "";

            public string Signataire = "";       // CN du certificat de signature
            public string Algorithme = "";       // sha1RSA…
            public DateTime FinCertificat;       // date de fin de validité, défaut si inconnue

            public string Service = "";          // nom du service pilote, s'il en a un
            public string EtatService = "";
        }

        // ==================================================================
        //  Analyse — PURE, donc testable sans machine
        // ==================================================================

        /// <summary>PUR : ce refus empêche-t-il réellement le pilote de tourner ?</summary>
        public static bool EstBloquant(Refus r)
        {
            return r != null && r.Blocages > 0;
        }

        /// <summary>
        /// PUR : le certificat de signature était-il périmé au moment du refus ?
        ///
        /// C'est LA question qui sépare « à réparer » de « sans issue ». Un pilote signé avec un
        /// certificat expiré ne redeviendra pas chargeable : ni une réinstallation, ni une mise à
        /// jour de Windows, ni un redémarrage n'y changeront quoi que ce soit. Seul l'éditeur
        /// peut le resigner.
        /// </summary>
        public static bool CertificatPerime(Refus r)
        {
            return r != null
                && r.FinCertificat != default(DateTime)
                && r.FinCertificat < r.PlusRecent;
        }

        /// <summary>PUR : signature SHA-1 ? Windows la rejette désormais pour le mode noyau.</summary>
        public static bool SignatureObsolete(Refus r)
        {
            return r != null && r.Algorithme.IndexOf("sha1", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// PUR : y a-t-il quelque chose à tenter, ou est-ce définitif ?
        ///
        /// Définitif quand la cause est dans la signature elle-même. Dire « réinstalle » dans ce
        /// cas-là, c'est faire perdre une heure à quelqu'un pour rien.
        /// </summary>
        public static bool SansIssue(Refus r)
        {
            return EstBloquant(r) && (CertificatPerime(r) || SignatureObsolete(r));
        }

        /// <summary>PUR : la cause, dite en une phrase, ou vide si on ne sait pas.</summary>
        public static string Cause(Refus r)
        {
            if (r == null) return "";
            if (CertificatPerime(r) && SignatureObsolete(r))
                return "signature " + r.Algorithme + " et certificat expiré le "
                     + r.FinCertificat.ToString("dd/MM/yyyy");
            if (CertificatPerime(r))
                return "certificat de signature expiré le " + r.FinCertificat.ToString("dd/MM/yyyy");
            if (SignatureObsolete(r))
                return "signature " + r.Algorithme + ", que Windows n'accepte plus en mode noyau";
            if (!r.Present)
                return "le fichier n'est plus sur le disque";
            return "";
        }

        /// <summary>Largeur utile des lignes de détail, indentation comprise. Le reste du rapport
        /// est plié à la main dans le code ; ces lignes-ci portent des noms de produits et des
        /// dates, donc leur longueur n'est connue qu'à l'exécution.</summary>
        private const int LargeurDetail = 66;

        private const string IndentDetail = "        ";

        /// <summary>PUR : plie un texte à la largeur donnée sans couper de mot, chaque ligne
        /// préfixée. Un mot plus long que la largeur occupe sa ligne entière plutôt que d'être
        /// tronqué — un nom de fichier coupé en deux ne se retrouve dans aucune recherche.</summary>
        public static string Plie(string texte, int largeur, string prefixe)
        {
            if (string.IsNullOrEmpty(texte)) return "";
            var sb = new System.Text.StringBuilder();
            string ligne = "";
            foreach (string mot in texte.Split(' '))
            {
                if (mot.Length == 0) continue;
                if (ligne.Length == 0) ligne = mot;
                else if (ligne.Length + 1 + mot.Length <= largeur) ligne += " " + mot;
                else { sb.AppendLine(prefixe + ligne); ligne = mot; }
            }
            if (ligne.Length > 0) sb.AppendLine(prefixe + ligne);
            return sb.ToString();
        }

        /// <summary>PUR : comment nommer ce pilote à l'écran — le produit s'il est connu.</summary>
        public static string Nomme(Refus r)
        {
            if (r == null) return "";
            if (r.Produit.Length > 0 && r.Editeur.Length > 0) return r.Produit + " — " + r.Editeur;
            if (r.Produit.Length > 0) return r.Produit;
            if (r.Editeur.Length > 0) return r.Editeur;
            return r.Fichier;
        }

        /// <summary>
        /// PUR : le rapport, ou une chaîne vide s'il n'y a aucun refus APPLIQUÉ.
        ///
        /// Le silence est ici la bonne réponse. Une machine dont aucun pilote n'est bloqué n'a pas
        /// besoin d'un paragraphe pour le lui dire, et les signalements en audit ne sont pas des
        /// pannes : les sortir en gras produirait une alerte sur une machine parfaitement saine.
        /// </summary>
        public static string Rapport(List<Refus> l)
        {
            if (l == null) return "";
            var bloques = new List<Refus>();
            foreach (Refus r in l) if (EstBloquant(r)) bloques.Add(r);
            if (bloques.Count == 0) return "";

            bloques.Sort(delegate (Refus a, Refus b) { return b.PlusRecent.CompareTo(a.PlusRecent); });

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("PILOTES QUE WINDOWS REFUSE DE CHARGER");
            sb.AppendLine("   Windows te l'annonce par une notification tronquée qui nomme un");
            sb.AppendLine("   fichier .sys — sans dire quel logiciel s'en sert, ni pourquoi c'est");
            sb.AppendLine("   refusé, ni si c'est réparable. Rien non plus dans le Gestionnaire de");
            sb.AppendLine("   périphériques ni dans les Services. Voici ce qu'elle ne dit pas.");
            sb.AppendLine();

            foreach (Refus r in bloques)
            {
                sb.AppendLine("   ⚠ " + r.Fichier + "   (" + Nomme(r) + ")");
                sb.Append(Plie("refusé " + r.Blocages + " fois, au moins depuis le "
                    + r.PlusAncien.ToString("dd/MM/yyyy") + " — dernier refus le "
                    + r.PlusRecent.ToString("dd/MM/yyyy à HH:mm"), LargeurDetail, IndentDetail));

                string cause = Cause(r);
                if (cause.Length > 0)
                    sb.Append(Plie("CAUSE : " + cause + ".", LargeurDetail, IndentDetail));

                string outil = OutilQuiEnDepend(r);
                if (outil.Length > 0)
                {
                    sb.Append(Plie("Cet outil ne peut donc RIEN mesurer sur cette machine.",
                        LargeurDetail, IndentDetail));
                    string remp = Remplacement(outil);
                    if (remp.Length > 0)
                        sb.Append(Plie("À LA PLACE : " + remp, LargeurDetail, IndentDetail));
                }

                if (SansIssue(r))
                    sb.Append(Plie("C'est DÉFINITIF : la cause est dans la signature du fichier. "
                        + "Ni réinstaller, ni mettre à jour, ni redémarrer n'y changera quoi que ce "
                        + "soit — seul l'éditeur peut resigner son pilote.",
                        LargeurDetail, IndentDetail));
            }

            sb.AppendLine();
            sb.AppendLine("   Ce n'est PAS un réglage que tu aurais raté, et ça ne vient d'aucune");
            sb.AppendLine("   optimisation : Windows recharge ces stratégies à chaque démarrage.");
            sb.AppendLine("   ONYX ne propose pas de les contourner — désactiver l'intégrité du code");
            sb.AppendLine("   affaiblirait toute la machine, durablement, pour un seul outil.");
            return sb.ToString();
        }

        // ==================================================================
        //  Les outils qu'ONYX recommande, et ce qu'il sait faire sans eux
        // ==================================================================

        private sealed class Outil
        {
            public string Nom;
            /// <summary>Fragments de nom de pilote. Fragments, pas noms exacts : les éditeurs
            /// changent de suffixe entre deux versions (rspLLL64 / rspLLL32).</summary>
            public string[] Pilotes;
            public string Remplacement;
        }

        private static readonly Outil[] Outils =
        {
            new Outil {
                Nom = "LatencyMon",
                Pilotes = new[] { "rspLLL" },
                // Espaces INSÉCABLES dans les guillemets : Plie ne coupe que sur l'espace
                // ordinaire, donc le titre de la fenêtre ne se retrouve pas éclaté sur deux
                // lignes avec un guillemet orphelin au bout de la première.
                Remplacement = "la mesure de latence intégrée d'ONYX (fenêtre « Latence EN DIRECT ») : "
                             + "même relevé par pilote, DPC et ISR, par session ETW noyau — sans "
                             + "aucun pilote à installer, donc rien que Windows puisse refuser."
            }
        };

        /// <summary>PUR : quel outil recommandé par ONYX dépend de ce pilote ? Vide si aucun.</summary>
        public static string OutilQuiEnDepend(Refus r)
        {
            if (r == null) return "";
            foreach (Outil o in Outils)
                foreach (string motif in o.Pilotes)
                    if (r.Fichier.IndexOf(motif, StringComparison.OrdinalIgnoreCase) >= 0)
                        return o.Nom;
            return "";
        }

        /// <summary>PUR : par quoi remplacer cet outil. Vide si on n'a rien d'honnête à proposer.</summary>
        public static string Remplacement(string nomOutil)
        {
            foreach (Outil o in Outils)
                if (string.Equals(o.Nom, nomOutil, StringComparison.OrdinalIgnoreCase))
                    return o.Remplacement;
            return "";
        }

        /// <summary>PUR : cet outil est-il inutilisable sur cette machine ? Sert aux boutons qui
        /// proposent de l'installer : ne pas proposer vaut mieux que proposer en vain.</summary>
        public static bool OutilBloque(List<Refus> l, string nomOutil)
        {
            if (l == null) return false;
            foreach (Refus r in l)
                if (EstBloquant(r) && string.Equals(OutilQuiEnDepend(r), nomOutil,
                                                    StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        // ==================================================================
        //  Lecture machine — IMPURE, et qui n'a pas le droit de lever
        // ==================================================================

        /// <summary>Chemin de fichier dans un message d'événement. Les messages sont traduits,
        /// les chemins non : on cible le chemin, jamais les mots autour.</summary>
        private static readonly Regex MotifChemin = new Regex(
            @"\\(?:Device\\HarddiskVolume\d+|\?\?\\[A-Za-z]:)\\[^\s""]+?\.sys|[A-Za-z]:\\[^\s""]+?\.sys",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static List<Refus> _cache;
        private static readonly object _verrou = new object();

        /// <summary>Le relevé, calculé une seule fois par session. La lecture du journal coûte
        /// quelques centaines de millisecondes : à appeler en arrière-plan, jamais sur le fil
        /// de l'interface.</summary>
        public static List<Refus> LireEnCache()
        {
            lock (_verrou)
            {
                if (_cache == null) _cache = Lire();
                return _cache;
            }
        }

        /// <summary>
        /// Les refus réellement enregistrés sur cette machine. Ne lève jamais : sur une machine
        /// où le journal est désactivé ou inaccessible, une liste vide est la seule réponse
        /// honnête — une liste partielle produirait un diagnostic faux plutôt qu'aucun.
        /// </summary>
        public static List<Refus> Lire()
        {
            var parFichier = new Dictionary<string, Refus>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Filtrage côté journal : lire les milliers d'entrées pour en garder trois
                // ferait attendre l'utilisateur sans rien apporter.
                var requete = new EventLogQuery(JournalIntegrite, PathType.LogName,
                    "*[System[(EventID=" + IdBloque + " or EventID=" + IdAudite
                    + " or EventID=" + IdIntegrite + ")]]");
                requete.ReverseDirection = true;   // du plus récent au plus ancien

                using (var lecteur = new EventLogReader(requete))
                {
                    int lus = 0;
                    for (EventRecord ev = lecteur.ReadEvent(); ev != null; ev = lecteur.ReadEvent())
                    {
                        using (ev)
                        {
                            if (++lus > MaxEvenements) break;
                            Absorbe(parFichier, ev);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // On ne dira rien plutôt que faux — mais on NOTE. Sans cette ligne, un journal
                // désactivé et une machine saine rendent la même liste vide, et le rapport se
                // tait pour deux raisons opposées sans qu'on puisse jamais les distinguer.
                JournalTechnique.Echec("PilotesRefuses.Lire", ex);
            }

            var l = new List<Refus>(parFichier.Values);
            foreach (Refus r in l) Complete(r);
            return l;
        }

        /// <summary>Range un événement dans le relevé du fichier qu'il concerne.</summary>
        private static void Absorbe(Dictionary<string, Refus> parFichier, EventRecord ev)
        {
            try
            {
                string message = ev.FormatDescription();
                if (string.IsNullOrEmpty(message)) return;
                Match m = MotifChemin.Match(message);
                if (!m.Success) return;

                string fichier = Path.GetFileName(m.Value);
                if (fichier.Length == 0) return;

                Refus r;
                if (!parFichier.TryGetValue(fichier, out r))
                {
                    r = new Refus { Fichier = fichier };
                    parFichier[fichier] = r;
                }

                int id = ev.Id;
                if (id == IdBloque) r.Blocages++;
                else if (id == IdAudite) r.Audits++;

                // Les 3004 datent le refus au même titre que les 3077 : ils décrivent le même
                // incident sous un autre angle. Ils ne comptent PAS comme un blocage de plus.
                DateTime t = ev.TimeCreated.HasValue ? ev.TimeCreated.Value : default(DateTime);
                if (t == default(DateTime)) return;
                if (r.PlusRecent == default(DateTime) || t > r.PlusRecent) r.PlusRecent = t;
                if (r.PlusAncien == default(DateTime) || t < r.PlusAncien) r.PlusAncien = t;
            }
            catch { }
        }

        /// <summary>Complète un refus par ce que le fichier lui-même sait dire.</summary>
        private static void Complete(Refus r)
        {
            try
            {
                r.Chemin = Localise(r.Fichier);
                r.Present = r.Chemin.Length > 0 && File.Exists(r.Chemin);
                if (r.Present)
                {
                    LitIdentite(r);
                    LitSignature(r);
                }
                LitService(r);
            }
            catch (Exception ex) { JournalTechnique.Echec("PilotesRefuses.Complete " + r.Fichier, ex); }
        }

        /// <summary>Où est ce pilote ? Le chemin du journal est une forme noyau
        /// (\Device\HarddiskVolumeN\…) qu'on ne cherche pas à traduire : les pilotes vivent dans
        /// un dossier connu, et le service, s'il existe, donne le chemin exact.</summary>
        private static string Localise(string fichier)
        {
            try
            {
                string drivers = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers");
                string p = Path.Combine(drivers, fichier);
                if (File.Exists(p)) return p;

                p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), fichier);
                if (File.Exists(p)) return p;
            }
            catch { }
            return "";
        }

        private static void LitIdentite(Refus r)
        {
            try
            {
                FileVersionInfo fv = FileVersionInfo.GetVersionInfo(r.Chemin);
                r.Produit = fv.ProductName ?? "";
                r.Editeur = fv.CompanyName ?? "";
                r.Version = fv.FileVersion ?? "";
            }
            catch { }
        }

        /// <summary>
        /// Le certificat de signature du fichier — signataire, algorithme, date de fin.
        ///
        /// On lit le certificat EMBARQUÉ, sans demander à Windows s'il le juge valide : la
        /// question n'est pas « est-ce signé » (ça l'est) mais « avec quoi », et c'est cette
        /// réponse-là qui dit si la situation a une issue.
        /// </summary>
        private static void LitSignature(Refus r)
        {
            try
            {
                byte[] brut;
#pragma warning disable SYSLIB0057
                // Aucune API non dépréciée n'extrait le certificat de signature d'un exécutable
                // PE. On ne s'en sert que pour LIRE l'algorithme et la date de fin — jamais pour
                // juger la signature valide : Windows a déjà tranché, et c'est son verdict qu'on
                // rapporte, pas le nôtre.
                brut = X509Certificate.CreateFromSignedFile(r.Chemin).GetRawCertData();
#pragma warning restore SYSLIB0057
                using (X509Certificate2 cert = X509CertificateLoader.LoadCertificate(brut))
                {
                    r.Signataire = cert.Subject ?? "";
                    r.FinCertificat = cert.NotAfter;
                    if (cert.SignatureAlgorithm != null)
                        r.Algorithme = cert.SignatureAlgorithm.FriendlyName ?? "";
                }
            }
            catch { }   // non signé, ou signature illisible : Cause() se taira, et c'est correct
        }

        private static void LitService(Refus r)
        {
            try
            {
                string sans = Path.GetFileNameWithoutExtension(r.Fichier);
                using (var s = new System.Management.ManagementObjectSearcher(
                    "SELECT Name, State, PathName FROM Win32_SystemDriver"))
                    foreach (System.Management.ManagementObject mo in s.Get())
                    {
                        object chemin = mo["PathName"];
                        if (chemin == null) continue;
                        if (chemin.ToString().IndexOf(r.Fichier, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        object nom = mo["Name"], etat = mo["State"];
                        r.Service = nom != null ? nom.ToString() : sans;
                        r.EtatService = etat != null ? etat.ToString() : "";
                        return;
                    }
            }
            catch { }
        }
    }
}
