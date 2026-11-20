using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Côté MACHINE du journal d'état d'origine (la logique pure est dans EtatOrigine.cs) :
    /// capture de l'état « avant » par les primitives d'écriture de Sys pendant l'Apply d'un
    /// réglage, restauration de cet état par « Rétablir », lecture et sauvegarde du fichier.
    /// Registre seulement pour l'instant. Non compilé par le banc (accès registre et disque).
    /// </summary>
    internal static class EtatOrigineMachine
    {
        /// <summary>Journal et entrée du réglage en cours d'application ; null hors capture.</summary>
        [ThreadStatic] private static EtatOrigine.Journal _journal;
        [ThreadStatic] private static EtatOrigine.Entree _entree;
        /// <summary>Profondeur des blocs Couvert() : écritures déjà capturées par la primitive appelante.</summary>
        [ThreadStatic] private static int _couvert;

        /// <summary>Un seul chargement-modification-sauvegarde du journal à la fois (lots lancés en parallèle).</summary>
        private static readonly object Verrou = new object();

        private static string Chemin { get { return AppPaths.File("bt-etat-origine.json"); } }

        // ------------------------------------------------------------------
        //  Capture (appelée par les primitives de Sys juste avant d'écrire)
        // ------------------------------------------------------------------

        /// <summary>À appeler juste avant SetValue/DeleteValue sur cle. Ne fait rien hors capture.</summary>
        public static void AvantValeur(RegistryKey cle, string nom)
        {
            if (_entree == null || cle == null) return;
            EtatOrigine.Note(_journal, _entree, LitValeur(cle, cle.Name, nom));
        }

        /// <summary>
        /// À appeler avant CreateSubKey(sousCle) sur racine. Note l'existence de la clé ; si elle
        /// manque, note aussi chaque parent manquant que CreateSubKey va créer (du plus haut au plus
        /// bas : rejouées à l'envers, les clés créées sont retirées de la plus profonde à la plus haute).
        /// </summary>
        public static void AvantCle(RegistryKey racine, string sousCle)
        {
            if (_entree == null || racine == null || string.IsNullOrEmpty(sousCle)) return;
            var manquantes = new List<string>();
            string chemin = sousCle.Trim('\\');
            while (chemin.Length > 0 && !CleExiste(racine, chemin))
            {
                manquantes.Add(chemin);
                int p = chemin.LastIndexOf('\\');
                chemin = p < 0 ? "" : chemin.Substring(0, p);
            }
            if (manquantes.Count == 0)
            {
                EtatOrigine.Note(_journal, _entree, new EtatOrigine.Item
                {
                    Genre = "cle", Cible = racine.Name + "\\" + sousCle.Trim('\\'), Nom = "", Existe = true
                });
                return;
            }
            for (int i = manquantes.Count - 1; i >= 0; i--)
                EtatOrigine.Note(_journal, _entree, new EtatOrigine.Item
                {
                    Genre = "cle", Cible = racine.Name + "\\" + manquantes[i], Nom = "", Existe = false
                });
        }

        /// <summary>Écriture que les primitives ne savent pas capturer : le réglage n'est plus annulable à l'identique.</summary>
        public static void NonCouvert(string quoi)
        {
            if (_entree == null || _couvert > 0) return;
            _entree.Complet = false;
            if (!_entree.NonCouvert.Contains(quoi)) _entree.NonCouvert.Add(quoi);
        }

        /// <summary>Bloc dont les écritures sont déjà capturées par la primitive appelante (NonCouvert y est sans effet).</summary>
        public static IDisposable Couvert()
        {
            _couvert++;
            return new FinCouvert();
        }

        private sealed class FinCouvert : IDisposable
        {
            private bool _fait;
            public void Dispose()
            {
                if (_fait) return;
                _fait = true;
                _couvert--;
            }
        }

        // ------------------------------------------------------------------
        //  Lecture et restauration d'un élément
        // ------------------------------------------------------------------

        /// <summary>État actuel pour la même identité que modele.</summary>
        public static EtatOrigine.Item Lit(EtatOrigine.Item modele)
        {
            switch (modele.Genre)
            {
                case "reg":
                {
                    string sous;
                    RegistryKey racine = Racine(modele.Cible, out sous);
                    using (RegistryKey k = racine.OpenSubKey(sous))
                    {
                        if (k == null)
                            return new EtatOrigine.Item { Genre = "reg", Cible = modele.Cible, Nom = modele.Nom ?? "", Existe = false };
                        return LitValeur(k, modele.Cible, modele.Nom);
                    }
                }
                case "cle":
                {
                    string sous;
                    RegistryKey racine = Racine(modele.Cible, out sous);
                    return new EtatOrigine.Item { Genre = "cle", Cible = modele.Cible, Nom = modele.Nom ?? "", Existe = CleExiste(racine, sous) };
                }
                default:
                    throw new NotSupportedException("Genre non pris en charge : " + modele.Genre);
            }
        }

        /// <summary>
        /// reg : réécrit la valeur avec son type, ou la supprime si elle n'existait pas.
        /// cle : si elle n'existait pas, la supprime seulement si elle n'a plus ni valeur ni sous-clé.
        /// </summary>
        public static void Restaure(EtatOrigine.Item origine)
        {
            string sous;
            RegistryKey racine;
            switch (origine.Genre)
            {
                case "reg":
                    racine = Racine(origine.Cible, out sous);
                    string nom = origine.Nom ?? "";
                    if (origine.Existe)
                    {
                        var kind = Enum.Parse<RegistryValueKind>(origine.Kind);
                        using (RegistryKey k = racine.CreateSubKey(sous))
                            k.SetValue(nom, EtatOrigine.DecodeValeur(origine.Valeur, kind), kind);
                    }
                    else
                    {
                        using (RegistryKey k = racine.OpenSubKey(sous, true))
                            if (k != null) k.DeleteValue(nom, false);
                    }
                    return;
                case "cle":
                    if (origine.Existe) return;
                    racine = Racine(origine.Cible, out sous);
                    using (RegistryKey k = racine.OpenSubKey(sous))
                    {
                        if (k == null || k.ValueCount > 0 || k.SubKeyCount > 0) return;
                    }
                    racine.DeleteSubKey(sous, false);
                    return;
                default:
                    throw new NotSupportedException("Genre non pris en charge : " + origine.Genre);
            }
        }

        private static EtatOrigine.Item LitValeur(RegistryKey k, string cible, string nom)
        {
            nom = nom ?? "";
            var item = new EtatOrigine.Item { Genre = "reg", Cible = cible, Nom = nom, Existe = false };
            object v = k.GetValue(nom, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (v == null) return item;
            RegistryValueKind kind = k.GetValueKind(nom);
            item.Existe = true;
            item.Kind = kind.ToString();
            item.Valeur = EtatOrigine.EncodeValeur(v, kind);
            return item;
        }

        /// <summary>Vrai si la clé existe ; illisible (accès refusé) compte comme existante, par prudence.</summary>
        private static bool CleExiste(RegistryKey racine, string sous)
        {
            try
            {
                using (RegistryKey k = racine.OpenSubKey(sous))
                    return k != null;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>Chemin complet (RegistryKey.Name) → ruche de base et sous-chemin.</summary>
        private static RegistryKey Racine(string chemin, out string sous)
        {
            int p = (chemin ?? "").IndexOf('\\');
            if (p <= 0 || p == chemin.Length - 1) throw new ArgumentException("Chemin de registre incomplet : " + chemin);
            sous = chemin.Substring(p + 1);
            switch (chemin.Substring(0, p).ToUpperInvariant())
            {
                case "HKEY_LOCAL_MACHINE": return Registry.LocalMachine;
                case "HKEY_USERS": return Registry.Users;
                case "HKEY_CURRENT_USER": return Registry.CurrentUser;
                case "HKEY_CLASSES_ROOT": return Registry.ClassesRoot;
                default: throw new ArgumentException("Ruche de registre non prise en charge : " + chemin);
            }
        }

        // ------------------------------------------------------------------
        //  Branchement sur Engine
        // ------------------------------------------------------------------

        /// <summary>Applique le réglage en capturant l'état d'origine de ce qu'il écrit.</summary>
        public static void Applique(Tweak t)
        {
            lock (Verrou)
            {
                if (string.IsNullOrEmpty(t.Id)) { t.Apply(); return; }
                EtatOrigine.Journal j = Charge();
                bool entreeExiste = j.Entrees.Exists(e => e.Id == t.Id);
                if (!EtatOrigine.DoitCapturer(entreeExiste, DejaApplique(t.Id))) { t.Apply(); return; }

                EtatOrigine.Entree entree = EtatOrigine.Ouvre(j, t.Id, DateTime.Now);
                _journal = j;
                _entree = entree;
                try
                {
                    t.Apply();
                }
                finally
                {
                    // Même si Apply a levé : les écritures déjà faites doivent rester annulables.
                    _journal = null;
                    _entree = null;
                    // Rien de capté : pas d'entrée vide, « Rétablir » garde l'ancien Revert().
                    if (entree.Items.Count == 0 && entree.NonCouvert.Count == 0) j.Entrees.Remove(entree);
                    Sauve(j);
                }
            }
        }

        /// <summary>Rétablit le réglage : rejoue l'état d'origine capturé, sinon l'ancien Revert().</summary>
        public static void Retablit(Tweak t)
        {
            lock (Verrou)
            {
                EtatOrigine.Journal j = string.IsNullOrEmpty(t.Id) ? null : Charge();
                EtatOrigine.Entree entree = j == null ? null : j.Entrees.Find(e => e.Id == t.Id);
                if (entree == null) { t.Revert(); return; }

                // Une partie des écritures n'a pas été capturée : l'ancien Revert() d'abord, puis l'origine connue.
                if (!entree.Complet) t.Revert();

                List<EtatOrigine.Item> aRestaurer;
                EtatOrigine.Journal nouveau = EtatOrigine.Retire(j, t.Id, out aRestaurer);
                var echecs = new List<string>();
                foreach (EtatOrigine.Item item in aRestaurer)
                {
                    try { Restaure(item); }
                    catch (Exception ex) { echecs.Add(EtatOrigine.Identite(item) + " (" + ex.Message + ")"); }
                }
                // Échec partiel : journal intact, un second « Rétablir » pourra finir le travail.
                if (echecs.Count > 0)
                    throw new InvalidOperationException("Rétablissement incomplet, élément(s) en échec : " + string.Join(" ; ", echecs));
                Sauve(nouveau);
            }
        }

        // ------------------------------------------------------------------
        //  Fichiers
        // ------------------------------------------------------------------

        private static bool DejaApplique(string id)
        {
            string chemin = TweakDrift.JournalPath;
            if (!File.Exists(chemin)) return false;
            return TweakDrift.Parse(File.ReadAllText(chemin)).Exists(e => e.Id == id);
        }

        /// <summary>Journal du disque ; illisible : mis de côté (.corrompu-AAAAMMJJ-HHmmss) et remplacé par un journal vide.</summary>
        private static EtatOrigine.Journal Charge()
        {
            string chemin = Chemin;
            if (!File.Exists(chemin)) return new EtatOrigine.Journal();
            EtatOrigine.Journal j = EtatOrigine.Lire(File.ReadAllText(chemin));
            if (!j.Corrompu) return j;
            File.Move(chemin, chemin + ".corrompu-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            return new EtatOrigine.Journal();
        }

        private static void Sauve(EtatOrigine.Journal j)
        {
            string chemin = Chemin;
            string tmp = chemin + ".tmp";
            File.WriteAllText(tmp, EtatOrigine.Ecrire(j));
            File.Move(tmp, chemin, true);
        }
    }
}
