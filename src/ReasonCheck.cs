using System;
using System.Collections.Generic;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Vérification par « raisonnement automatique » (inspiré des Automated Reasoning checks
    /// d'Amazon Bedrock Guardrails) : au lieu d'un raisonnement probabiliste, on valide la réponse
    /// de l'IA contre des RÈGLES DÉTERMINISTES du domaine PC/gaming. Chaque règle a un ID traçable ;
    /// si la réponse recommande quelque chose de DANGEREUX ou un MYTHE connu, on la marque « invalide »
    /// et on AMÉLIORE la réponse avec la correction factuelle (comme le préconise l'article : « when a
    /// response is found invalid, the result is used to rewrite or enhance the answer »).
    ///
    /// C'est volontairement une base de règles (pas un solveur SMT) : sur SA portée, un contrôle par
    /// règle est déterministe donc fiable à 100 %, sans coût ni dépendance.
    /// </summary>
    internal static class ReasonCheck
    {
        internal sealed class Rule
        {
            public string Id;
            public string[] Triggers;   // formes de RECOMMANDATION (déjà en minuscules sans accents)
            public string Fix;          // la correction factuelle affichée à l'utilisateur
        }

        // Base de connaissances formelle : mythes tenaces + conseils dangereux que même une IA
        // « répond avec assurance » peut sortir. Triggers = tournures impératives de RECOMMANDATION.
        private static readonly Rule[] Rules =
        {
            new Rule { Id = "AR-PAGEFILE", Fix = "Désactiver le fichier d'échange (pagefile) fait planter les jeux gourmands (« out of memory ») — à LAISSER géré par Windows.",
                Triggers = new[] { "desactive le pagefile", "desactive ton pagefile", "desactive votre pagefile",
                                   "desactiver le pagefile", "desactiver ton pagefile", "desactiver votre pagefile",
                                   "desactive le fichier d'echange", "desactive ton fichier d'echange",
                                   "desactiver le fichier d'echange", "desactiver ton fichier d'echange",
                                   "supprime le fichier d'echange", "supprime ton fichier d'echange",
                                   "pagefile a 0", "fichier d'echange a zero" } },
            new Rule { Id = "AR-TRIM", Fix = "Couper le TRIM ralentit le SSD dans la durée — à laisser ACTIVÉ.",
                Triggers = new[] { "desactive le trim", "desactive ton trim", "desactiver le trim", "desactiver ton trim", "coupe le trim", "couper le trim" } },
            new Rule { Id = "AR-DEFRAG-SSD", Fix = "On ne défragmente PAS un SSD (usure inutile) : le TRIM s'en charge. La défragmentation, c'est pour les disques durs mécaniques.",
                Triggers = new[] { "defragmente ton ssd", "defragmenter le ssd", "defragmenter ton ssd", "defrag du ssd", "defragmentation du ssd", "defragmenter votre ssd" } },
            new Rule { Id = "AR-HPET", Fix = "Forcer le HPET AJOUTE de la latence (vieux mythe des « guides boost ») — à laisser sur auto.",
                Triggers = new[] { "force le hpet", "forcer le hpet", "active le hpet pour gagner", "activer le hpet pour", "hpet ameliore les fps", "hpet augmente les fps" } },
            new Rule { Id = "AR-WINUPDATE", Fix = "Désactiver les mises à jour de sécurité expose ton PC — à garder actives (tu peux seulement différer les redémarrages).",
                Triggers = new[] { "desactive les mises a jour de securite", "desactiver windows update", "coupe les mises a jour de securite", "desactive windows update" } },
            new Rule { Id = "AR-ANTIVIRUS", Fix = "Désactiver l'antivirus pour quelques FPS n'en vaut pas le risque — utilise plutôt le mode jeu de Windows Defender.",
                Triggers = new[] { "desactive ton antivirus", "desactiver l'antivirus", "desactiver ton antivirus", "coupe ton antivirus", "desactive windows defender" } },
            new Rule { Id = "AR-REGCLEANER", Fix = "Les « nettoyeurs de registre » n'améliorent pas les FPS et peuvent casser Windows — inutile, évite.",
                Triggers = new[] { "utilise un nettoyeur de registre", "installe un nettoyeur de registre", "nettoyeur de registre pour", "nettoie ton registre pour", "nettoyer le registre ameliore" } },
            new Rule { Id = "AR-MSCONFIG-CORES", Fix = "Le réglage « nombre de processeurs » de msconfig ne débride RIEN (mythe) : Windows utilise déjà tous les cœurs. À laisser décoché.",
                Triggers = new[] { "active tous les coeurs dans msconfig", "coche tous les coeurs", "nombre de processeurs dans msconfig", "tous les coeurs dans msconfig" } },
            new Rule { Id = "AR-DESTRUCTIF", Fix = "NE FAIS PAS ça : c'est destructeur (perte de Windows ou de tes données), sans aucun gain — c'est une mauvaise blague d'internet.",
                Triggers = new[] { "supprime system32", "supprimer system32", "efface system32", "format c:", "formater c:", "supprime le dossier windows" } },
            new Rule { Id = "AR-VCORE", Fix = "Un tel voltage peut endommager le CPU — reste dans les limites sûres du constructeur (souvent bien en dessous de 1,4 V selon la puce).",
                Triggers = new[] { "1.5v sur le cpu", "1,5v sur le cpu", "monte le vcore a 1.5", "vcore a 1.5", "voltage cpu a 1.5", "1.6v sur le cpu" } },
            new Rule { Id = "AR-GRATUIT", Fix = "Rappel : le Copilote ne recommande QUE du gratuit — il existe forcément une alternative libre/gratuite.",
                Triggers = new[] { "achete la version", "achete la licence", "acheter le logiciel", "version payante", "abonnement premium", "version pro payante" } },
        };

        // Cues de NÉGATION : si l'une précède de peu le trigger, la réponse conseille CONTRE la
        // mauvaise pratique → pas une violation.
        private static readonly string[] Neg = { "ne ", "n'", "pas", "jamais", "evite", "eviter", "sans ", "surtout", "surtout pas", "faut pas", "deconseille" };

        /// <summary>Renvoie les IDs de règles VIOLÉES par la réponse (vide = valide).</summary>
        internal static List<string> Check(string answer)
        {
            var hits = new List<string>();
            string n = Deaccent((answer ?? "").ToLowerInvariant());
            if (n.Length == 0) return hits;
            foreach (var r in Rules)
            {
                foreach (string t in r.Triggers)
                {
                    int idx = n.IndexOf(t, StringComparison.Ordinal);
                    if (idx < 0) continue;
                    // Garde de négation : fenêtre courte avant le trigger.
                    int from = Math.Max(0, idx - 18);
                    string before = n.Substring(from, idx - from);
                    bool negated = false;
                    foreach (string ng in Neg) if (before.Contains(ng)) { negated = true; break; }
                    if (negated) continue;
                    if (!hits.Contains(r.Id)) hits.Add(r.Id);
                    break;   // une occurrence suffit pour cette règle
                }
            }
            return hits;
        }

        /// <summary>Si la réponse enfreint des règles, lui ADJOINT la ou les corrections factuelles
        /// (verdict « invalide » de l'article). Sinon renvoie la réponse inchangée.</summary>
        internal static string Enhance(string answer)
        {
            if (string.IsNullOrEmpty(answer)) return answer;
            var hits = Check(answer);
            if (hits.Count == 0) return answer;
            var sb = new StringBuilder(answer.TrimEnd());
            sb.Append("\n\n🧮 Vérification automatique — j'ai repéré un conseil à corriger :");
            foreach (string id in hits)
            {
                Rule r = Find(id);
                if (r != null) sb.Append("\n• [").Append(id).Append("] ").Append(r.Fix);
            }
            return sb.ToString();
        }

        private static Rule Find(string id)
        {
            foreach (var r in Rules) if (r.Id == id) return r;
            return null;
        }

        private static string Deaccent(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            try
            {
                string f = s.Normalize(NormalizationForm.FormD);
                var sb = new StringBuilder(f.Length);
                foreach (char c in f)
                    if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                        sb.Append(c);
                return sb.ToString().Normalize(NormalizationForm.FormC);
            }
            catch { return s; }
        }
    }
}
