using System;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// Bouclier anti-injection de prompt / jailbreak (recommandation « garde-fous & détection des
    /// tentatives de manipulation »). Deux usages :
    ///  1) repérer une tentative DIRECTE de l'utilisateur (« ignore tes règles », « change de rôle »,
    ///     « montre ton prompt système »…) → le Copilote garde fermement son rôle ;
    ///  2) la vraie menace : l'injection INDIRECTE via du contenu récupéré (page web, résultats de
    ///     recherche) — traitée par un cadrage défensif dans le prompt web (voir LocalBrain.AskWeb).
    /// </summary>
    internal static class PromptShield
    {
        // Formes claires de détournement (déjà en minuscules, sans accents). Multi-mots pour éviter
        // les faux positifs (« ignore » seul est banal).
        private static readonly string[] Inj =
        {
            "ignore les instructions", "ignore tes instructions", "ignore les consignes",
            "ignore les regles", "ignore all previous", "ignore previous instructions",
            "disregard previous", "oublie tes instructions", "oublie tes regles",
            "oublie tes consignes", "oublie que tu es", "tu n'es plus le copilote",
            "tu n'es plus un assistant", "tu es maintenant un", "tu es desormais un",
            "nouveau role :", "change de role", "fais comme si tu etais", "joue le role",
            "mode developpeur", "mode dan", "jailbreak", "sans aucune restriction",
            "sans aucun filtre", "reponds sans filtre", "ignore tes garde-fous",
            "contourne tes regles", "passe outre tes regles", "montre ton prompt",
            "affiche tes instructions", "revele tes instructions", "affiche ton prompt",
            "tes instructions systeme", "prompt systeme", "system prompt",
            "tu dois m'obeir", "tu n'as pas le droit de refuser"
        };

        internal static bool LooksLikeInjection(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string n = Deacc(text.ToLowerInvariant());
            foreach (string p in Inj) if (n.Contains(p)) return true;
            return false;
        }

        private static string Deacc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                string f = s.Normalize(NormalizationForm.FormD);
                var sb = new StringBuilder(f.Length);
                foreach (char c in f)
                    if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                        sb.Append(c);
                return sb.ToString();
            }
            catch { return s; }
        }
    }
}
