using System;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// L'ENVOI D'UN RAPPORT DE DIAGNOSTIC — ET SES QUATRE VERROUS.
    ///
    /// JournalTechnique porte cette phrase : « ce n'est pas une télémétrie, rien ne part d'ici ».
    /// Elle reste vraie. Ce module ne l'annule pas, il ouvre une porte à sens unique que
    /// l'utilisateur est seul à pouvoir franchir, et seulement en le voulant.
    ///
    /// VERROU 1 — RIEN N'EST AUTOMATIQUE. Il n'existe volontairement AUCUNE méthode du genre
    /// « EnvoieSiActif » appelée au démarrage, contrairement à Audience.PingSiActive. Le seul
    /// point d'entrée exige un rapport déjà construit, que l'appelant vient de montrer.
    ///
    /// VERROU 2 — L'UTILISATEUR VOIT CE QU'IL ENVOIE. Le rapport est un texte lisible, affiché
    /// et enregistrable AVANT tout envoi. Un consentement donné sans voir le contenu n'est pas
    /// un consentement ; c'est la seule raison pour laquelle ce module ne construit pas lui-même
    /// le rapport qu'il transmet.
    ///
    /// VERROU 3 — DES DONNÉES PERSONNELLES BLOQUENT L'ENVOI. Elles ne le « signalent » pas. Si
    /// PrivacyGuard trouve une adresse, un IBAN, un téléphone ou un numéro de sécurité sociale
    /// malgré le masquage, la réponse est un refus, et la personne garde son fichier local.
    ///
    /// VERROU 4 — SANS POINT DE COLLECTE, LE MODULE EST INERTE. L'adresse vit dans un fichier à
    /// côté de l'exécutable, jamais dans le code, exactement comme pour Audience. Une version
    /// d'ONYX compilée telle quelle n'envoie donc rien nulle part, et c'est l'état par défaut.
    ///
    /// SUR LE MOT « ANONYME », ENCORE. Un rapport de diagnostic contient l'état d'une machine
    /// précise. Même masqué, il reste une donnée personnelle au sens du RGPD dès lors qu'il est
    /// rattaché à quelqu'un qui demande de l'aide — parce que c'est justement le but. On ne
    /// prétend donc pas l'anonymiser : on le montre, on le minimise, et on ne l'envoie que sur
    /// un geste explicite.
    /// </summary>
    internal static class EnvoiDiagnostic
    {
        /// <summary>Délai volontairement court : l'utilisateur attend devant une fenêtre.</summary>
        private const int DelaiSecondes = 15;

        /// <summary>
        /// Adresse du point de collecte. VIDE = module inerte.
        ///
        /// Séparée du code pour la même raison qu'Audience : le service se déploie à part (voir
        /// tools/diagnostic-worker.js), et tant que ce fichier est absent, ONYX n'envoie rien.
        /// </summary>
        public static string PointDeCollecte
        {
            get
            {
                try
                {
                    string f = AppPaths.File("bt-diagnostic-url.txt");
                    if (File.Exists(f))
                    {
                        string s = File.ReadAllText(f).Trim();
                        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return s;
                    }
                }
                catch (Exception ex) { JournalTechnique.Echec("EnvoiDiagnostic.PointDeCollecte", ex); }
                return "";
            }
        }

        /// <summary>Y a-t-il seulement quelque part où envoyer ? Sert à ne pas proposer un
        /// bouton qui ne peut rien faire — le même défaut que le bouton LatencyMon.</summary>
        public static bool Configure { get { return PointDeCollecte.Length > 0; } }

        // ==================================================================
        //  Parties pures : voir FormatDiagnostic
        //
        //  EchappeJson, Charge, TropGros, Explique et l'énumération Resultat vivaient ici. Elles
        //  ne dépendent de rien — mais ce fichier, lui, référence RapportDiagnostic.Rapport, et
        //  ce seul lien suffisait à rendre tout le fichier incompilable hors de l'application.
        //  Le corps exact d'une requête d'envoi est justement ce qu'on veut pouvoir vérifier.
        // ==================================================================
        //  Envoi — IMPUR, explicite, et qui n'a pas le droit de lever
        // ==================================================================

        /// <summary>
        /// Envoie un rapport DÉJÀ CONSTRUIT et DÉJÀ MONTRÉ à l'utilisateur.
        ///
        /// À n'appeler que depuis un geste explicite. La signature l'impose autant qu'une
        /// signature le peut : ce module ne sait pas fabriquer de rapport, il faut donc être
        /// passé par RapportDiagnostic — c'est-à-dire par l'écran qui l'affiche.
        /// </summary>
        public static FormatDiagnostic.Resultat Envoie(RapportDiagnostic.Rapport rapport)
        {
            if (rapport == null || rapport.Texte.Length == 0) return FormatDiagnostic.Resultat.Echec;

            // VERROU 3 avant tout le reste : on ne veut même pas construire la requête.
            if (!rapport.Envoyable) return FormatDiagnostic.Resultat.RefusePersonnel;

            string url = PointDeCollecte;
            if (url.Length == 0) return FormatDiagnostic.Resultat.Inerte;          // VERROU 4

            // Tronquer plutôt que refuser : un dossier amputé de sa fin reste exploitable, et la
            // mention ajoutée évite qu'on cherche une section jamais envoyée.
            string texte = FormatDiagnostic.Tronque(rapport.Texte);

            try
            {
                string version = "";
                try { version = Convert.ToString(typeof(EnvoiDiagnostic).Assembly.GetName().Version); }
                catch { }

                using (var http = new System.Net.Http.HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(DelaiSecondes);
                    var contenu = new System.Net.Http.StringContent(
                        FormatDiagnostic.Charge(rapport.Reference, version, texte),
                        Encoding.UTF8, "application/json");
                    var rep = http.PostAsync(url, contenu).GetAwaiter().GetResult();
                    return rep.IsSuccessStatusCode
                         ? FormatDiagnostic.Resultat.Envoye : FormatDiagnostic.Resultat.Echec;
                }
            }
            catch (Exception ex)
            {
                JournalTechnique.Echec("EnvoiDiagnostic.Envoie", ex);
                return FormatDiagnostic.Resultat.Echec;
            }
        }
    }
}
