using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// LE DOSSIER COMPLET, EN UN SEUL TEXTE.
    ///
    /// Quand quelqu'un dit « ça marche pas », il manque toujours les mêmes choses : la version,
    /// l'état de la machine, ce qui a été changé récemment, et ce qui a échoué en silence. Les
    /// obtenir demande sinon dix allers-retours, et l'utilisateur abandonne avant le cinquième.
    ///
    /// Ce module rassemble tout ce qu'ONYX sait déjà — auto-diagnostic, échecs internes, pilotes
    /// refusés, journaux cachés, actions récentes, problèmes signalés — en UN texte, lisible tel
    /// quel, avec un CODE DE RÉFÉRENCE court que la personne peut citer.
    ///
    /// CE QUI N'Y ENTRE PAS, ET POURQUOI :
    ///
    ///   Rien qui identifie la personne. Chaque ligne passe par le masquage de
    ///   JournalTechnique — le nom de profil apparaît dans presque tous les chemins Windows, et
    ///   un rapport qu'on n'ose pas envoyer ne sert à rien. Ce qui reste est ensuite relu par
    ///   PrivacyGuard : s'il trouve une adresse, un IBAN ou un numéro, l'envoi est REFUSÉ, pas
    ///   « signalé ».
    ///
    ///   Aucune liste de jeux, aucun nom de machine, aucune adresse. Le sujet est « pourquoi
    ///   ONYX se comporte ainsi », pas « qui est cette personne ».
    ///
    /// CE RAPPORT NE PART JAMAIS TOUT SEUL. Il se construit, s'affiche, s'enregistre. L'envoi est
    /// un geste distinct, explicite, et traité dans EnvoiDiagnostic — voir la note qui s'y trouve.
    /// </summary>
    internal static class RapportDiagnostic
    {
        /// <summary>Au-delà, on n'apprend plus rien et le texte devient impossible à lire.</summary>
        public const int MaxEchecs = 40;
        public const int MaxActions = 25;
        public const int JoursDeJournaux = 7;

        public sealed class Rapport
        {
            /// <summary>Code court à citer — « ONYX-4F2A9C ».</summary>
            public string Reference = "";
            /// <summary>Le texte complet, déjà nettoyé.</summary>
            public string Texte = "";
            /// <summary>Ce que PrivacyGuard a trouvé MALGRÉ le nettoyage. Vide = rien.</summary>
            public List<string> DonneesPerso = new List<string>();
            public DateTime Quand = DateTime.Now;

            /// <summary>Un rapport porteur de données personnelles ne s'envoie pas.</summary>
            public bool Envoyable { get { return DonneesPerso.Count == 0 && Texte.Length > 0; } }
        }

        // ==================================================================
        //  Parties pures : voir FormatDiagnostic
        //
        //  Reference, Section et Nettoie vivaient ici. Elles en sont sorties non par goût du
        //  découpage, mais parce que ce fichier appelle SelfCheck — qui tire Diagnostics,
        //  CrashScan, LocalBrain, la moitié de l'application. Tant qu'elles y étaient, les
        //  vérifier revenait à compiler tout ONYX dans le banc d'essai.
        // ==================================================================
        //  Assemblage — IMPUR, et qui n'a pas le droit de lever
        // ==================================================================

        /// <summary>
        /// Construit le dossier. Ne lève jamais : chaque source est interrogée séparément, et une
        /// source muette laisse simplement sa section absente. Un dossier partiel reste utile ;
        /// une exception ici priverait l'utilisateur du seul moyen de se faire aider.
        /// </summary>
        public static Rapport Construire()
        {
            var r = new Rapport();
            var sb = new StringBuilder();

            r.Reference = Ref();
            sb.Append("RAPPORT DE DIAGNOSTIC ONYX").Append(Environment.NewLine);
            sb.Append("Référence : ").Append(r.Reference).Append(Environment.NewLine);
            sb.Append("Établi le : ").Append(r.Quand.ToString("dd/MM/yyyy HH:mm")).Append(Environment.NewLine);
            sb.Append(Environment.NewLine);

            sb.Append(FormatDiagnostic.Section("IDENTITÉ ET MACHINE", Sur(() => SelfCheck.SupportInfo())));
            sb.Append(FormatDiagnostic.Section("AUTO-DIAGNOSTIC", Sur(() => SelfCheck.Text())));
            sb.Append(FormatDiagnostic.Section("ÉCHECS INTERNES D'ONYX", EchecsInternes()));
            sb.Append(FormatDiagnostic.Section("PILOTES REFUSÉS PAR WINDOWS",
                Sur(() => PilotesRefuses.Rapport(PilotesRefuses.LireEnCache()))));
            sb.Append(FormatDiagnostic.Section("AUTRES JOURNAUX DE WINDOWS",
                Sur(() => CanauxWindows.Rapport(CanauxWindows.LireEnCache(JoursDeJournaux), JoursDeJournaux))));
            sb.Append(FormatDiagnostic.Section("DERNIÈRES ACTIONS", Actions()));
            sb.Append(FormatDiagnostic.Section("PROBLÈMES SIGNALÉS PAR L'UTILISATEUR", Problemes()));

            // Nettoyage EN DERNIER, sur le texte entier : une seule passe, et surtout aucune
            // section ne peut être ajoutée plus tard en oubliant de passer par là.
            r.Texte = FormatDiagnostic.Nettoie(sb.ToString(), Utilisateur(), Profil());

            try { r.DonneesPerso = PrivacyGuard.Detect(r.Texte); }
            catch (Exception ex) { JournalTechnique.Echec("RapportDiagnostic.PrivacyGuard", ex); }

            return r;
        }

        /// <summary>Enregistre le dossier à côté des données d'ONYX et rend son chemin, ou une
        /// chaîne vide si l'écriture a échoué — auquel cas l'utilisateur garde le presse-papiers.</summary>
        public static string Enregistre(Rapport r)
        {
            if (r == null || r.Texte.Length == 0) return "";
            try
            {
                string nom = "bt-diagnostic-" + r.Reference + ".txt";
                string chemin = AppPaths.File(nom);
                File.WriteAllText(chemin, r.Texte, Encoding.UTF8);
                return chemin;
            }
            catch (Exception ex)
            {
                JournalTechnique.Echec("RapportDiagnostic.Enregistre", ex);
                return "";
            }
        }

        // ------------------------------------------------------------------ sources

        private static string EchecsInternes()
        {
            try
            {
                List<string> l = JournalTechnique.Dernieres(MaxEchecs);
                if (l.Count == 0)
                    return "Aucun échec interne noté — tout ce qu'ONYX a tenté a abouti.";
                var sb = new StringBuilder();
                sb.Append(JournalTechnique.EchecsDistincts()).Append(" échec(s) distinct(s) ; ")
                  .Append(l.Count).Append(" dernière(s) ligne(s) :").Append(Environment.NewLine);
                foreach (string x in l) sb.Append("  ").Append(x).Append(Environment.NewLine);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                JournalTechnique.Echec("RapportDiagnostic.EchecsInternes", ex);
                return "";
            }
        }

        private static string Actions()
        {
            try
            {
                List<string> l = Journal.Tail(MaxActions);
                if (l.Count == 0) return "";
                var sb = new StringBuilder();
                foreach (string x in l) sb.Append("  ").Append(x).Append(Environment.NewLine);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                JournalTechnique.Echec("RapportDiagnostic.Actions", ex);
                return "";
            }
        }

        private static string Problemes()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (ProblemLog.Entree e in ProblemLog.Charger())
                    sb.Append("  ").Append(e.Date.ToString("dd/MM/yyyy")).Append(" | ")
                      .Append(e.Etat == ProblemLog.Etat.Regle ? "réglé " : "à régler")
                      .Append(" | ").Append(e.Description).Append(Environment.NewLine);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                JournalTechnique.Echec("RapportDiagnostic.Problemes", ex);
                return "";
            }
        }

        // ------------------------------------------------------------------ utilitaires

        /// <summary>Appelle une source sans lui laisser emporter le rapport entier.</summary>
        private static string Sur(Func<string> f)
        {
            try { return f() ?? ""; }
            catch (Exception ex) { JournalTechnique.Echec("RapportDiagnostic.Source", ex); return ""; }
        }

        private static string Ref()
        {
            string graine = "";
            try { graine = Convert.ToString(Sys.GetMachine(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid")) ?? ""; }
            catch { }
            return FormatDiagnostic.Reference(graine, DateTime.Now);
        }

        private static string Utilisateur()
        {
            try { return Environment.UserName; } catch { return ""; }
        }

        private static string Profil()
        {
            try { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
            catch { return ""; }
        }
    }
}
