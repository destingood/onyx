using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BTOptimizer
{
    /// <summary>Catégories coupées sur disque (bt-notifications.txt, dossier de données d'ONYX).</summary>
    internal static class NotifReglages
    {
        private const string Fichier = "bt-notifications.txt";
        private static readonly object _verrou = new object();
        private static HashSet<string> _coupees;

        private static HashSet<string> Coupees()
        {
            if (_coupees != null) return _coupees;
            string texte = "";
            try
            {
                string chemin = AppPaths.File(Fichier);
                if (File.Exists(chemin)) texte = File.ReadAllText(chemin, Encoding.UTF8);
            }
            catch { }
            return _coupees = NotifCategories.Lire(texte);
        }

        /// <summary>Vrai si la catégorie peut notifier. Appelable depuis n'importe quel thread.</summary>
        public static bool Active(string id)
        {
            lock (_verrou) return NotifCategories.Active(Coupees(), id);
        }

        /// <summary>Enregistre l'état de chaque catégorie ; faux si l'écriture échoue.</summary>
        public static bool Enregistre(IDictionary<string, bool> actives)
        {
            lock (_verrou)
            {
                var coupees = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in NotifCategories.Toutes)
                {
                    bool on;
                    if (actives != null && actives.TryGetValue(id, out on) && !on) coupees.Add(id);
                }
                try
                {
                    string chemin = AppPaths.File(Fichier), tmp = chemin + ".tmp";
                    File.WriteAllText(tmp, NotifCategories.Ecrire(coupees), new UTF8Encoding(false));
                    File.Move(tmp, chemin, true);
                    _coupees = coupees;
                    return true;
                }
                catch { return false; }
            }
        }
    }
}
