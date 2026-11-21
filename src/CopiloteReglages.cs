using System;
using System.Collections.Generic;

namespace BTOptimizer
{
    /// <summary>
    /// Actions en un clic du Copilote (pur, testé) : relie une demande comme « désactive la
    /// transparence » ou « remets les widgets » à un réglage du catalogue et au bon sens.
    ///
    /// Garde-fous : il faut un verbe d'action (une question n'est pas un ordre) ; le réglage
    /// doit ressortir seul (en cas d'égalité, rien) ; un mot court et vague ne suffit pas.
    /// Les réglages qui baissent une protection ou dont « Rétablir » n'est pas garanti sont
    /// reconnus mais refusés : ils restent dans la page Optimisations, avec leurs explications.
    /// </summary>
    internal static class CopiloteReglages
    {
        public sealed class Choix
        {
            public Tweak Reglage;
            public bool Retablir;
            /// <summary>Raison du refus (réglage risqué) ; null si l'action peut être proposée.</summary>
            public string Refus;
        }

        private static readonly HashSet<string> VerbesRetour = Mots(
            "retablis retablir retablit remets remet remettre annule annuler restaure restaurer reactive reactiver rallume rallumer defais");
        private static readonly HashSet<string> VerbesNegatifs = Mots(
            "desactive desactiver desactives coupe couper enleve enlever supprime supprimer vire virer retire retirer bloque bloquer eteins eteindre arrete arreter stoppe empeche empecher");
        private static readonly HashSet<string> VerbesPositifs = Mots(
            "active activer actives allume allumer mets met mettre applique appliquer accelere accelerer");

        // Mots des noms de réglages qui ne disent pas DE QUOI il s'agit.
        private static readonly HashSet<string> Vides = Mots(
            "desactiver activer empecher supprimer interdire bloquer reduire garder limiter lever regrouper repartir servir fermer "
          + "ne pas plus moins pour avec dans sans les des une sur par aux est tous toutes tout leur leurs que qui windows "
          + "regle reglage reglages lieu");

        // Début du nom qui dit que le réglage COUPE quelque chose (« Désactiver… », « Empêcher… »).
        private static readonly string[] DebutsNegatifs =
            { "desactiver", "empecher", "bloquer", "supprimer", "interdire", "ne ", "couper", "limiter" };

        /// <summary>Réglage visé par la demande (texte passé par CopiloteTexte.Prepare), ou null.</summary>
        public static Choix Trouve(string demande, IList<Tweak> catalogue)
        {
            if (string.IsNullOrWhiteSpace(demande) || catalogue == null) return null;
            bool retour = false, negatif = false, positif = false;
            var sujet = new List<string>();                          // la demande sans ses verbes d'action
            foreach (string m in CopiloteTexte.SplitWords(demande))
            {
                if (VerbesRetour.Contains(m)) retour = true;
                else if (VerbesNegatifs.Contains(m)) negatif = true;
                else if (VerbesPositifs.Contains(m)) positif = true;
                else sujet.Add(m);
            }
            if (!retour && !negatif && !positif) return null;
            if (demande.Contains("comme avant") || demande.Contains("par defaut")) retour = true;

            // Fréquence de chaque mot dans le catalogue : un mot long mais courant (« processus ») ne désigne rien seul.
            var listes = new List<KeyValuePair<Tweak, List<Cle>>>();
            var frequence = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Tweak t in catalogue)
            {
                if (t == null || string.IsNullOrEmpty(t.Name)) continue;
                List<Cle> cl = Cles(t.Name);
                if (cl.Count == 0) continue;
                listes.Add(new KeyValuePair<Tweak, List<Cle>>(t, cl));
                foreach (Cle c in cl) { int f; frequence.TryGetValue(c.Mot, out f); frequence[c.Mot] = f + 1; }
            }

            Tweak meilleur = null;
            int meilleurPoids = 0, meilleurTrouves = 0, meilleurForts = 0; double meilleurePart = 0;
            bool egalite = false;
            foreach (KeyValuePair<Tweak, List<Cle>> e in listes)
            {
                Tweak t = e.Key; List<Cle> cles = e.Value;
                int poids = 0, trouves = 0, forts = 0, horsPrefixe = 0; bool marqueExigee = false, marqueVue = false, seulNet = false;
                foreach (Cle c in cles)
                {
                    if (c.Marque && c.HorsParentheses && !c.Prefixe) marqueExigee = true;
                    if (!Correspond(sujet, c.Mot)) continue;
                    trouves++; poids += c.Fort ? 2 : 1;
                    if (c.Fort) forts++;
                    if (c.Marque) marqueVue = true;
                    if (!c.Prefixe) horsPrefixe++;
                    seulNet = c.Marque || (c.Fort && frequence[c.Mot] == 1);
                }
                if (trouves == 0 || (marqueExigee && !marqueVue)) continue;   // « Microsoft Edge » sans dire Edge : pas lui
                if (trouves < 2 && (horsPrefixe == 0 || !seulNet)) continue;   // « Bluetooth : … » ou « processus » seuls : trop vague
                double part = (double)trouves / cles.Count;
                if (poids > meilleurPoids)
                {
                    meilleur = t; meilleurPoids = poids; meilleurTrouves = trouves; meilleurForts = forts; meilleurePart = part; egalite = false;
                }
                else if (poids == meilleurPoids)
                {
                    // Départage par la part du nom couverte, seulement si la demande en dit assez (deux mots).
                    if (trouves >= 2 && meilleurTrouves >= 2 && Math.Abs(part - meilleurePart) > 1e-9)
                    {
                        if (part > meilleurePart) { meilleur = t; meilleurTrouves = trouves; meilleurForts = forts; meilleurePart = part; }
                    }
                    else egalite = true;
                }
            }
            if (meilleur == null || egalite) return null;                          // rien, ou égalité : on ne devine pas

            bool reglageNegatif = Negatif(meilleur.Name);
            bool retablir = retour || (negatif && !reglageNegatif) || (positif && !negatif && reglageNegatif);
            return new Choix { Reglage = meilleur, Retablir = retablir, Refus = Refus(meilleur) };
        }

        // Plus strict que FuzzyWord : une seule lettre d'écart (« defender » n'est pas « demander »).
        private static bool Correspond(List<string> mots, string cle)
        {
            foreach (string w in mots)
            {
                if (w == cle) return true;
                // Pluriel ou fin de mot : « animations », « lags » ; une clé courte n'avale pas un mot long (« win » ≠ « windows »).
                if (cle.Length >= 3 && w.Length > cle.Length && (cle.Length >= 5 || w.Length == cle.Length + 1)
                    && w.StartsWith(cle, StringComparison.Ordinal)) return true;
                if (cle.Length >= 5 && Math.Abs(w.Length - cle.Length) <= 1 && CopiloteTexte.Lev(w, cle, 1) <= 1) return true;
            }
            return false;
        }

        private static string Refus(Tweak t)
        {
            if (t.Risque == Risque.Securite)
                return "ce réglage baisse une protection de Windows : il se fait depuis la page Optimisations, où le risque est expliqué";
            if (t.Risque == Risque.AnnulationNonGarantie)
                return "« Rétablir » ne garantit pas l'état d'origine pour ce réglage : il se fait depuis la page Optimisations";
            return null;
        }

        private static bool Negatif(string nom)
        {
            string n = CopiloteTexte.Norm(nom);
            int deuxPoints = n.IndexOf(" : ", StringComparison.Ordinal);       // « Bluetooth : désactiver… »
            if (deuxPoints > 0 && Negatif(nom.Substring(nom.IndexOf(" : ", StringComparison.Ordinal) + 3))) return true;
            foreach (string d in DebutsNegatifs) if (n.StartsWith(d, StringComparison.Ordinal)) return true;
            return n.Contains("(sans ") || n.Contains(" sans ");                 // « Recherche … locale (sans Bing) »
        }

        private struct Cle
        {
            public string Mot;
            public bool Fort;             // sigle, nom propre ou mot long
            public bool Marque;           // sigle ou nom propre (« Edge », « MPO »)
            public bool HorsParentheses;  // dans le nom lui-même, pas dans la précision entre parenthèses
            public bool Prefixe;          // catégorie avant « : » (« Bluetooth », « Explorateur ») : ne désigne pas le réglage seule
        }

        /// <summary>Mots qui disent de quoi parle le réglage.</summary>
        private static List<Cle> Cles(string nom)
        {
            var cles = new List<Cle>();
            var vus = new HashSet<string>(StringComparer.Ordinal);
            var brut = new System.Text.StringBuilder();
            int profondeur = 0, rang = 0; bool prefixeVu = false, citation = false;
            Action ajoute = () =>
            {
                if (brut.Length == 0) return;
                string b = brut.ToString(); brut.Clear();
                bool debutCitation = citation; citation = false;
                int i = rang++;
                string m = CopiloteTexte.Norm(b);
                if (m.Length < 3 || Vides.Contains(m) || !char.IsLetter(m[0]) || !vus.Add(m)) return;
                bool sigle = b.Length >= 2 && b.ToUpperInvariant() == b && b.ToLowerInvariant() != b;
                bool nomPropre = i > 0 && char.IsUpper(b[0]) && !debutCitation;   // « Meilleures performances » : majuscule de citation
                cles.Add(new Cle { Mot = m, Fort = sigle || nomPropre || m.Length >= 7, Marque = sigle || nomPropre, HorsParentheses = profondeur == 0 });
            };
            foreach (char ch in nom)
            {
                if (char.IsLetterOrDigit(ch)) { brut.Append(ch); continue; }
                ajoute();
                if (ch == '«' || ch == '"') citation = true;
                if (ch == ':' && profondeur == 0 && !prefixeVu)
                {
                    prefixeVu = true;
                    for (int k = 0; k < cles.Count; k++) { Cle c = cles[k]; c.Prefixe = true; cles[k] = c; }
                }
                if (ch == '(') profondeur++;
                else if (ch == ')' && profondeur > 0) profondeur--;
            }
            ajoute();
            return cles;
        }

        private static HashSet<string> Mots(string liste)
        {
            return new HashSet<string>(liste.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        }
    }
}
