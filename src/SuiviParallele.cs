using System.Collections.Generic;

namespace BTOptimizer
{
    /// <summary>
    /// Suit des éléments traités en parallèle (les modules de la page Stockage). À chaque fin,
    /// renvoie un instantané cohérent : les éléments terminés DANS L'ORDRE D'ORIGINE, le nombre
    /// fait et le premier encore attendu. Sûr entre fils d'exécution.
    /// </summary>
    internal sealed class SuiviParallele<T>
    {
        public sealed class Etat
        {
            public List<T> Finis = new List<T>();
            public int Faits;
            public T EnAttente;   // premier élément pas encore fini (valable si Reste)
            public bool Reste;
        }

        private readonly List<T> _items;
        private readonly bool[] _fini;
        private int _faits;
        private readonly object _verrou = new object();

        public SuiviParallele(IList<T> items)
        {
            _items = new List<T>(items);
            _fini = new bool[_items.Count];
        }

        /// <summary>Note la fin de l'élément <paramref name="index"/> (une seconde fin est ignorée).</summary>
        public Etat Fini(int index)
        {
            lock (_verrou)
            {
                if (index >= 0 && index < _fini.Length && !_fini[index]) { _fini[index] = true; _faits++; }
                var e = new Etat { Faits = _faits };
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_fini[i]) e.Finis.Add(_items[i]);
                    else if (!e.Reste) { e.Reste = true; e.EnAttente = _items[i]; }
                }
                return e;
            }
        }
    }
}
