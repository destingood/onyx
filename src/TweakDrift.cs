using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace BTOptimizer
{
    /// <summary>
    /// « MES RÉGLAGES ONT-ILS TENU ? » — Windows annule régulièrement ce qu'on lui a appliqué :
    /// mise à jour de fonctionnalité, réinstallation du pilote graphique, réinitialisation d'un
    /// composant, autre « optimiseur » passé derrière. L'utilisateur, lui, ne voit rien : il croit
    /// son PC réglé alors que la moitié des optimisations est retombée — et il attribue la perte
    /// d'images à autre chose (exactement comme un profil XMP qui saute dans le BIOS).
    ///
    /// On tient donc un JOURNAL de ce qui a été appliqué, puis on relit l'état réel via le Check()
    /// de chaque réglage. Écart entre les deux = dérive, signalée et réparable en un clic.
    /// Les fonctions de lecture/écriture du journal sont PURES (testables sans disque).
    /// </summary>
    internal static class TweakDrift
    {
        /// <summary>Une ligne du journal : un réglage appliqué, et quand.</summary>
        public sealed class Entry
        {
            public string Id;
            public DateTime When;
        }

        /// <summary>Un réglage qui était appliqué et qui ne l'est plus.</summary>
        public sealed class Drifted
        {
            public string Id;
            public string Name;
            public DateTime When;
            public bool Reboot;
        }

        public static string JournalPath { get { return AppPaths.File("bt-appliques.txt"); } }

        // ---------- Journal : format « id|yyyy-MM-dd », une ligne par réglage ----------

        /// <summary>Lecture PURE du journal (tolère lignes vides, dates absentes ou illisibles).</summary>
        public static List<Entry> Parse(string content)
        {
            var list = new List<Entry>();
            if (string.IsNullOrEmpty(content)) return list;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in content.Replace("\r\n", "\n").Split('\n'))
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string id = line, date = null;
                int bar = line.IndexOf('|');
                if (bar > 0)
                {
                    id = line.Substring(0, bar).Trim();
                    date = line.Substring(bar + 1).Trim();
                }
                if (id.Length == 0 || !seen.Add(id)) continue;   // doublon : la 1re ligne fait foi
                DateTime when;
                if (date == null || !DateTime.TryParseExact(date, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                    when = DateTime.MinValue;                     // date inconnue : on garde l'entrée
                list.Add(new Entry { Id = id, When = when });
            }
            return list;
        }

        /// <summary>Écriture PURE du journal.</summary>
        public static string Render(List<Entry> entries)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("# ONYX — réglages appliqués (id|date). Sert à détecter ceux que Windows annule.\n");
            if (entries != null)
                foreach (var e in entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.Id)) continue;
                    sb.Append(e.Id).Append('|')
                      .Append(e.When == DateTime.MinValue ? "" : e.When.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                      .Append('\n');
                }
            return sb.ToString();
        }

        /// <summary>Fusion PURE : les identifiants appliqués aujourd'hui écrasent leur date précédente.</summary>
        public static List<Entry> Merge(List<Entry> existing, IEnumerable<string> appliedIds, DateTime when)
        {
            var outp = new List<Entry>();
            var added = new HashSet<string>(StringComparer.Ordinal);
            if (appliedIds != null)
                foreach (var id in appliedIds)
                {
                    if (string.IsNullOrEmpty(id) || !added.Add(id)) continue;
                    outp.Add(new Entry { Id = id, When = when });
                }
            if (existing != null)
                foreach (var e in existing)
                {
                    if (e == null || string.IsNullOrEmpty(e.Id) || added.Contains(e.Id)) continue;
                    added.Add(e.Id);
                    outp.Add(e);
                }
            return outp;
        }

        /// <summary>Retrait PUR : un réglage rétabli volontairement sort du journal (sinon on
        /// signalerait comme « dérive » un choix délibéré de l'utilisateur).</summary>
        public static List<Entry> Remove(List<Entry> existing, IEnumerable<string> removedIds)
        {
            var drop = new HashSet<string>(StringComparer.Ordinal);
            if (removedIds != null) foreach (var id in removedIds) if (!string.IsNullOrEmpty(id)) drop.Add(id);
            var outp = new List<Entry>();
            if (existing != null)
                foreach (var e in existing)
                    if (e != null && !string.IsNullOrEmpty(e.Id) && !drop.Contains(e.Id)) outp.Add(e);
            return outp;
        }

        // ---------- Disque ----------

        public static List<Entry> Load()
        {
            try { return Parse(File.Exists(JournalPath) ? File.ReadAllText(JournalPath) : null); }
            catch { return new List<Entry>(); }
        }

        private static void Save(List<Entry> entries)
        {
            try { File.WriteAllText(JournalPath, Render(entries)); }
            catch { }   // journal indisponible : la détection de dérive se tait, l'app continue
        }

        /// <summary>À appeler après une APPLICATION réussie.</summary>
        public static void Record(IEnumerable<string> appliedIds)
        {
            if (appliedIds == null) return;
            Save(Merge(Load(), appliedIds, DateTime.Now.Date));
        }

        /// <summary>À appeler après un RÉTABLISSEMENT réussi (choix délibéré : plus de suivi).</summary>
        public static void Forget(IEnumerable<string> revertedIds)
        {
            if (revertedIds == null) return;
            Save(Remove(Load(), revertedIds));
        }

        // ---------- Détection ----------

        /// <summary>Comparaison PURE journal ↔ état réel. Un Check() qui répond null (état
        /// indéterminé) n'est JAMAIS compté comme une dérive : on ne crie pas au loup sans preuve.</summary>
        public static List<Drifted> Compare(List<Entry> journal, List<Tweak> catalog)
        {
            var outp = new List<Drifted>();
            if (journal == null || journal.Count == 0 || catalog == null) return outp;
            var byId = new Dictionary<string, Tweak>(StringComparer.Ordinal);
            foreach (var t in catalog) if (t != null && !string.IsNullOrEmpty(t.Id)) byId[t.Id] = t;

            foreach (var e in journal)
            {
                Tweak t;
                if (e == null || !byId.TryGetValue(e.Id, out t) || t.Check == null) continue;
                bool? state;
                try { state = t.Check(); }
                catch { continue; }                    // lecture impossible : pas un constat
                if (state.HasValue && !state.Value)
                    outp.Add(new Drifted { Id = t.Id, Name = t.Name, When = e.When, Reboot = t.Reboot });
            }
            return outp;
        }

        /// <summary>Dérive constatée sur la machine (journal + catalogue réels).</summary>
        public static List<Drifted> Detect()
        {
            try { return Compare(Load(), Catalog.All()); }
            catch { return new List<Drifted>(); }
        }

        /// <summary>Identifiants des réglages à ré-appliquer.</summary>
        public static List<string> Ids(List<Drifted> drifted)
        {
            var ids = new List<string>();
            if (drifted != null) foreach (var d in drifted) if (d != null) ids.Add(d.Id);
            return ids;
        }

        /// <summary>Mise en forme PURE : ce que l'utilisateur lit avant de ré-appliquer.</summary>
        public static string Format(List<Drifted> drifted, int top)
        {
            if (drifted == null || drifted.Count == 0)
                return "✅ Aucune dérive : tous les réglages appliqués sont toujours en place.";
            var sb = new System.Text.StringBuilder();
            sb.Append(drifted.Count).Append(" réglage(s) appliqué(s) par ONYX ont été ANNULÉS depuis :\n\n");
            int n = 0;
            foreach (var d in drifted)
            {
                if (n++ >= top) break;
                sb.Append("• ").Append(d.Name);
                if (d.When != DateTime.MinValue)
                    sb.Append("   (appliqué le ").Append(d.When.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)).Append(')');
                sb.Append('\n');
            }
            if (drifted.Count > top) sb.Append("…et ").Append(drifted.Count - top).Append(" autre(s).\n");
            return sb.ToString();
        }
    }
}
