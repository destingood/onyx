using System;
using System.Globalization;

namespace BTOptimizer
{
    /// <summary>
    /// RAPPEL HEBDOMADAIRE DU POIDS MORT (logique PURE) : quand l'analyse est due, et ce que dit
    /// la notification. Rien n'est jamais supprimé : la notification renvoie vers Stockage.
    /// Fichier d'état bt-rappel-nettoyage.txt : ligne 1 « 1 »/« 0 » (case cochée), ligne 2 date
    /// de la dernière analyse au format aller-retour (« o »), vide si jamais analysé.
    /// </summary>
    internal static partial class RappelNettoyage
    {
        public const int Jours = 7;
        private const long UnGo = 1073741824L;
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public sealed class Etat { public bool Active; public DateTime? Derniere; }

        /// <summary>Due si jamais faite, si 7 jours sont passés, ou si la date est dans le futur (horloge reculée).</summary>
        public static bool Du(DateTime? derniere, DateTime maintenant)
        {
            if (derniere == null) return true;
            if (derniere.Value > maintenant) return true;
            return maintenant - derniere.Value >= TimeSpan.FromDays(Jours);
        }

        /// <summary>Texte de la notification ; null sous 1 Go ou sans élément (rien à signaler).</summary>
        public static string Texte(long octets, int elements)
        {
            if (octets < UnGo || elements <= 0) return null;
            double go = Math.Round(octets / (double)UnGo, 1);
            return go.ToString("0.#", Fr) + " Go à récupérer dans " + elements
                 + (elements > 1 ? " éléments" : " élément") + ". Ouvre Stockage pour choisir.";
        }

        public static Etat Lit(string texte)
        {
            var e = new Etat();
            if (string.IsNullOrEmpty(texte)) return e;
            string[] l = texte.Replace("\r", "").Split('\n');
            e.Active = l[0].Trim() == "1";
            DateTime d;
            if (l.Length > 1 && DateTime.TryParse(l[1].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d))
                e.Derniere = d;
            return e;
        }

        public static string Ecrit(Etat e)
        {
            if (e == null) return "0\n";
            return (e.Active ? "1" : "0") + "\n"
                 + (e.Derniere.HasValue ? e.Derniere.Value.ToString("o", CultureInfo.InvariantCulture) : "") + "\n";
        }
    }
}
