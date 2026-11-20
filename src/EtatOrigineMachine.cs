using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using Microsoft.Win32;

namespace BTOptimizer
{
    /// <summary>
    /// Côté MACHINE du journal d'état d'origine (la logique pure est dans EtatOrigine.cs) :
    /// capture de l'état « avant » par les primitives d'écriture de Sys pendant l'Apply d'un
    /// réglage, restauration de cet état par « Rétablir », lecture et sauvegarde du fichier.
    /// Registre, paramètres d'alimentation, services et tâches planifiées. Non compilé par le banc
    /// (accès registre, disque et commandes système).
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
            EtatOrigine.Item avant = LitValeur(cle, cle.Name, nom);
            string id = EtatOrigine.Identite(avant);
            if (_entree.Items.Exists(x => EtatOrigine.Identite(x) == id)) return;
            // Type que SetValue ne sait pas réécrire (Unknown : REG_RESOURCE_LIST…) : le restaurer en
            // Binary changerait son type. Pas d'origine notée, l'ancien Revert() servira.
            if (avant.Existe && !EtatOrigine.KindRestaurable(Enum.Parse<RegistryValueKind>(avant.Kind)))
            {
                NonCouvert("valeur " + cle.Name + "\\" + (nom ?? "") + " de type " + avant.Kind);
                return;
            }
            EtatOrigine.Note(_journal, _entree, avant);
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

        /// <summary>
        /// À appeler avant de modifier un paramètre d'alimentation du plan actif. Le plan actif est lu
        /// AVANT le test de doublon : il fait partie de l'identité (même paramètre, autre plan = autre
        /// élément). Plan ou valeur illisible : NonCouvert.
        /// </summary>
        public static void AvantAlim(string sousGroupe, string parametre)
        {
            if (_entree == null) return;
            string plan;
            try
            {
                using (Couvert()) plan = PlanActif();
            }
            catch (Exception)
            {
                plan = null;
            }
            if (plan == null) { NonCouvert("alimentation " + parametre); return; }
            Capture(new EtatOrigine.Item { Genre = "alim", Cible = EtatOrigine.CibleAlim(plan, sousGroupe), Nom = parametre },
                "alimentation " + parametre);
        }

        /// <summary>À appeler avant de modifier le démarrage ou l'état d'un service. Un service absent est noté tel quel.</summary>
        public static void AvantService(string nom)
        {
            if (_entree == null) return;
            Capture(new EtatOrigine.Item { Genre = "service", Cible = nom, Nom = "" }, "service " + nom);
        }

        /// <summary>À appeler avant d'activer ou désactiver une tâche planifiée. État illisible : NonCouvert.</summary>
        public static void AvantTache(string chemin)
        {
            if (_entree == null) return;
            Capture(new EtatOrigine.Item { Genre = "tache", Cible = chemin, Nom = "" }, "tâche " + chemin);
        }

        /// <summary>
        /// Lit l'état actuel de modele et le note, sauf s'il l'est déjà (réapplication). Les lectures
        /// (powercfg /query, sc query, schtasks /query) passent par Sys.Run : sous Couvert(), sinon
        /// chaque capture se marquerait elle-même non couverte. Lecture impossible : NonCouvert(quoi).
        /// </summary>
        private static void Capture(EtatOrigine.Item modele, string quoi)
        {
            string id = EtatOrigine.Identite(modele);
            if (_entree.Items.Exists(x => EtatOrigine.Identite(x) == id)) return;
            EtatOrigine.Item avant;
            try
            {
                using (Couvert()) avant = Lit(modele);
            }
            catch (Exception)
            {
                avant = null;
            }
            // Service absent (Existe = false) : noté, il n'y aura rien à remettre.
            bool lisible = avant != null && (avant.Existe || avant.Genre == "service");
            if (!lisible) { NonCouvert(quoi); return; }
            EtatOrigine.Note(_journal, _entree, avant);
        }

        /// <summary>Écriture que les primitives ne savent pas capturer : le réglage n'est plus annulable à l'identique.</summary>
        public static void NonCouvert(string quoi)
        {
            if (_entree == null || _couvert > 0) return;
            _entree.Complet = false;
            if (!_entree.NonCouvert.Contains(quoi)) _entree.NonCouvert.Add(quoi);
        }

        /// <summary>
        /// Réglage dont « Rétablir » doit lancer l'ancien Revert() avant de rejouer le journal, sans
        /// le déclarer non couvert (il reste « Sûr ») : jeton DirectX partagé entre deux réglages,
        /// démarrage rapide qui exige la veille prolongée. Ne fait rien hors capture.
        /// </summary>
        public static void RevertRequis()
        {
            if (_entree == null) return;
            _entree.RevertRequis = true;
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
                case "alim":
                {
                    // Le plan visé : celui de l'identité (Cible = "plan\sous-groupe") ; ancien journal sans
                    // plan dans Cible : celui de la valeur, sinon le plan actif.
                    string plan, sousGroupe;
                    if (!EtatOrigine.LitCibleAlim(modele.Cible, out plan, out sousGroupe))
                    {
                        uint acO, dcO;
                        if (!EtatOrigine.LitAlim(modele.Valeur, out acO, out dcO, out plan)) plan = PlanActif();
                    }
                    int[] v = null;
                    if (plan != null)
                    {
                        // /query n'affiche pas les paramètres masqués (CPMINCORES, PERFBOOSTMODE… par défaut) :
                        // on retente avec /qh, même sortie, masqués compris. Toujours illisible → Existe = false.
                        string ids = " " + plan + " " + sousGroupe + " " + modele.Nom;
                        NativeResult r = Sys.Run(Sys.Sys32("powercfg.exe"), "/query" + ids);
                        v = r.ExitCode == 0 ? EtatOrigine.LitPowercfg(r.Output) : null;
                        if (v == null)
                        {
                            r = Sys.Run(Sys.Sys32("powercfg.exe"), "/qh" + ids);
                            v = r.ExitCode == 0 ? EtatOrigine.LitPowercfg(r.Output) : null;
                        }
                    }
                    return new EtatOrigine.Item
                    {
                        Genre = "alim", Cible = modele.Cible, Nom = modele.Nom ?? "", Existe = v != null,
                        Valeur = v == null ? null : EtatOrigine.ValeurAlim(v[0], v[1], plan)
                    };
                }
                case "service":
                {
                    int start = Sys.GetServiceStart(modele.Cible);
                    bool differe = Sys.IntEquals(Sys.GetMachine(@"SYSTEM\CurrentControlSet\Services\" + modele.Cible, "DelayedAutostart"), 1);
                    bool enMarche = false;
                    if (start != -1)
                    {
                        // État de marche illisible : on lève (à la capture → NonCouvert) plutôt que de
                        // noter « arrêté » et faire arrêter un service qui tournait au rétablissement.
                        bool? m = EnMarche(modele.Cible);
                        if (m == null) throw new InvalidOperationException("État du service " + modele.Cible + " illisible (sc query).");
                        enMarche = m.Value;
                    }
                    return new EtatOrigine.Item
                    {
                        Genre = "service", Cible = modele.Cible, Nom = modele.Nom ?? "", Existe = start != -1,
                        Valeur = EtatOrigine.ValeurService(start, differe, enMarche)
                    };
                }
                case "tache":
                {
                    bool? inactive = Sys.ScheduledTaskDisabled(modele.Cible);
                    return new EtatOrigine.Item
                    {
                        Genre = "tache", Cible = modele.Cible, Nom = modele.Nom ?? "", Existe = inactive.HasValue,
                        Valeur = inactive == null ? "inconnue" : inactive.Value ? "inactive" : "active"
                    };
                }
                default:
                    throw new NotSupportedException("Genre non pris en charge : " + modele.Genre);
            }
        }

        /// <summary>
        /// reg : réécrit la valeur avec son type, ou la supprime si elle n'existait pas.
        /// cle : si elle n'existait pas, la supprime seulement si elle n'a plus ni valeur ni sous-clé.
        /// alim : index AC puis DC du plan capturé, puis /setactive s'il est le plan actif.
        /// service : type de démarrage (sc config), puis démarrage (refus = erreur) ou arrêt (refus toléré).
        /// tache : réactivée ou désactivée, puis relue.
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
                case "alim":
                {
                    uint ac, dc;
                    string plan, planValeur, sousGroupe;
                    if (!EtatOrigine.LitAlim(origine.Valeur, out ac, out dc, out planValeur))
                        throw new FormatException("Valeur d'alimentation illisible : " + origine.Valeur);
                    // Le plan de l'identité ; ancien journal (Cible = sous-groupe seul) : celui de la valeur.
                    if (!EtatOrigine.LitCibleAlim(origine.Cible, out plan, out sousGroupe)) plan = planValeur;
                    // Écrit dans le plan capturé, même s'il n'est plus actif ; plan supprimé → RunThrow lève, journal intact.
                    string ids = plan + " " + sousGroupe + " " + origine.Nom + " ";
                    Sys.RunThrow(Sys.Sys32("powercfg.exe"), "/setacvalueindex " + ids + ac.ToString(CultureInfo.InvariantCulture),
                        "Rétablissement alimentation (secteur)");
                    Sys.RunThrow(Sys.Sys32("powercfg.exe"), "/setdcvalueindex " + ids + dc.ToString(CultureInfo.InvariantCulture),
                        "Rétablissement alimentation (batterie)");
                    // Réactiver seulement s'il est le plan actif : sinon /setactive changerait de plan.
                    if (string.Equals(PlanActif(), plan, StringComparison.OrdinalIgnoreCase))
                        Sys.RunThrow(Sys.Sys32("powercfg.exe"), "/setactive " + plan, "Application du plan");
                    return;
                }
                case "service":
                {
                    int start; bool differe, enMarche;
                    if (!EtatOrigine.LitService(origine.Valeur, out start, out differe, out enMarche))
                        throw new FormatException("Valeur de service illisible : " + origine.Valeur);
                    if (start == -1) return;   // absent à l'origine : rien à remettre
                    string type = EtatOrigine.TypeDemarrage(start, differe);
                    if (type == null)
                        throw new NotSupportedException("Démarrage " + start + " du service " + origine.Cible
                            + " (pilote boot/system) non restaurable par sc config.");
                    Sys.ConfigureService(origine.Cible, type, false, false);
                    if (enMarche)
                    {
                        // Refus de démarrer : on lève, le journal reste intact et un second « Rétablir » réessaie.
                        if (!Sys.StartService(origine.Cible))
                            throw new InvalidOperationException("Le service " + origine.Cible + " n'a pas pu être redémarré.");
                    }
                    else
                    {
                        // Refus d'arrêter toléré : un arrêt bloqué par des services dépendants bloquerait
                        // l'entrée pour toujours. Le type de démarrage, lui, est bien remis.
                        Sys.StopService(origine.Cible);
                    }
                    return;
                }
                case "tache":
                {
                    if (origine.Valeur != "active" && origine.Valeur != "inactive") return;   // inconnue : jamais notée
                    bool active = origine.Valeur == "active";
                    Sys.SetScheduledTask(origine.Cible, active);
                    // schtasks /change ne lève pas : on relit pour qu'un refus garde le journal intact.
                    if (Sys.ScheduledTaskDisabled(origine.Cible) == active)
                        throw new InvalidOperationException("Tâche " + origine.Cible + " : " + (active ? "réactivation" : "désactivation") + " refusée.");
                    return;
                }
                default:
                    throw new NotSupportedException("Genre non pris en charge : " + origine.Genre);
            }
        }

        /// <summary>GUID du plan d'alimentation actif (powercfg /getactivescheme) ; null si illisible.</summary>
        private static string PlanActif()
        {
            NativeResult r = Sys.Run(Sys.Sys32("powercfg.exe"), "/getactivescheme");
            return r.ExitCode == 0 ? EtatOrigine.LitPlanActif(r.Output) : null;
        }

        /// <summary>sc query : vrai si RUNNING, faux sinon ; null si la commande échoue (état inconnu).</summary>
        private static bool? EnMarche(string nom)
        {
            NativeResult r;
            using (Couvert())   // lecture seule : jamais « non couvert »
                r = Sys.Run(Sys.Sys32("sc.exe"), "query \"" + nom + "\"");
            if (r.ExitCode != 0 || string.IsNullOrEmpty(r.Output)) return null;
            return r.Output.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0;
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
            // Type non restaurable : pas d'encodage (l'appelant le déclare non couvert).
            item.Valeur = EtatOrigine.KindRestaurable(kind) ? EtatOrigine.EncodeValeur(v, kind) : null;
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

        /// <summary>
        /// Applique le réglage en capturant l'état d'origine de ce qu'il écrit. Rend null, ou un
        /// avertissement à afficher (l'application a réussi mais l'origine n'a pas pu être enregistrée).
        /// </summary>
        public static string Applique(Tweak t)
        {
            lock (Verrou)
            using (VerrouPartage.Prend())
            {
                if (string.IsNullOrEmpty(t.Id)) { t.Apply(); return null; }
                EtatOrigine.Journal j = Charge();
                bool entreeExiste = j.Entrees.Exists(e => e.Id == t.Id);
                bool dejaApplique = !entreeExiste && DejaApplique(t.Id);
                bool dejaEnPlace = !entreeExiste && !dejaApplique && DejaEnPlace(t);
                if (!EtatOrigine.DoitCapturer(entreeExiste, dejaApplique, dejaEnPlace)) { t.Apply(); return null; }

                // Rien de capté (Apply n'a rien écrit) : l'entrée vide reste, comme marqueur, pour qu'un
                // Apply réel plus tard soit capturé dedans (sinon bt-appliques.txt l'en empêcherait).
                EtatOrigine.Entree entree = EtatOrigine.Ouvre(j, t.Id, DateTime.Now);
                _journal = j;
                _entree = entree;
                Exception erreurApply = null;
                try
                {
                    t.Apply();
                }
                catch (Exception ex)
                {
                    erreurApply = ex;
                }
                finally
                {
                    _journal = null;
                    _entree = null;
                }

                // Même si Apply a levé : les écritures déjà faites doivent rester annulables.
                string avertissement = null;
                try
                {
                    Sauve(j);
                }
                catch (Exception exSauve)
                {
                    JournalTechnique.Echec("EtatOrigine.Sauve", exSauve);
                    if (erreurApply == null)
                    {
                        // Apply a réussi mais son origine est perdue : le noter appliqué TOUT DE SUITE, sinon
                        // le prochain Apply (gardien d'ouverture de session) capturerait les valeurs d'ONYX
                        // comme « origine ». « Rétablir » prendra l'ancien Revert().
                        TweakDrift.Record(new[] { t.Id });
                        avertissement = "état d'origine non enregistré (" + exSauve.Message
                            + ") : « Rétablir » utilisera l'ancienne méthode d'annulation";
                    }
                }
                // L'erreur d'Apply, pas celle du fichier : c'est elle qui explique l'échec.
                if (erreurApply != null) ExceptionDispatchInfo.Capture(erreurApply).Throw();
                return avertissement;
            }
        }

        /// <summary>
        /// Rétablit le réglage : rejoue l'état d'origine capturé, sinon l'ancien Revert(). Rend null, ou
        /// « maintenu par &lt;id&gt; » quand rien n'a été remis parce que d'autres réglages appliqués
        /// tiennent encore les mêmes valeurs.
        /// </summary>
        public static string Retablit(Tweak t)
        {
            lock (Verrou)
            using (VerrouPartage.Prend())
            {
                EtatOrigine.Journal j = string.IsNullOrEmpty(t.Id) ? null : Charge();
                EtatOrigine.Entree entree = j == null ? null : j.Entrees.Find(e => e.Id == t.Id);
                if (entree == null) { t.Revert(); return null; }

                // Écritures non capturées, ou réglage qui le demande (RevertRequis) : l'ancien Revert()
                // d'abord, puis l'origine connue. Entrée vide et complète : rien du tout.
                bool ancienRevert = EtatOrigine.AncienRevertDabord(entree);
                if (ancienRevert) t.Revert();

                List<EtatOrigine.Item> aRestaurer;
                List<string> heritiers;
                EtatOrigine.Journal nouveau = EtatOrigine.Retire(j, t.Id, out aRestaurer, out heritiers);
                var echecs = new List<string>();
                foreach (EtatOrigine.Item item in aRestaurer)
                {
                    try { Restaure(item); }
                    catch (Exception ex) { echecs.Add(EtatOrigine.Identite(item) + " (" + ex.Message + ")"); }
                }
                // Échec partiel : journal intact, un second « Rétablir » pourra finir le travail.
                if (echecs.Count > 0)
                    throw new InvalidOperationException("Rétablissement incomplet, élément(s) en échec : " + string.Join(" ; ", echecs));
                try
                {
                    Sauve(nouveau);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Restauration faite, journal inchangé : un second « Rétablir » la rejoue à l'identique.
                    throw new InvalidOperationException("État d'origine remis, mais le journal " + Path.GetFileName(Chemin)
                        + " n'a pas pu être mis à jour (" + ex.Message + ") : relance « Rétablir » plus tard.", ex);
                }
                return !ancienRevert && aRestaurer.Count == 0 && heritiers.Count > 0 ? EtatOrigine.MaintenuPar(heritiers) : null;
            }
        }

        /// <summary>Copie profonde de l'entrée du journal pour ce réglage ; null s'il n'y en a pas (test aller-retour).</summary>
        internal static EtatOrigine.Entree EntreeDe(string id)
        {
            lock (Verrou)
            using (VerrouPartage.Prend())
            {
                EtatOrigine.Entree e = Charge().Entrees.Find(x => x.Id == id);
                if (e == null) return null;
                var copie = new EtatOrigine.Entree
                {
                    Id = e.Id, Date = e.Date, Complet = e.Complet, RevertRequis = e.RevertRequis, NonCouvert = new List<string>(e.NonCouvert)
                };
                foreach (EtatOrigine.Item i in e.Items)
                    copie.Items.Add(new EtatOrigine.Item { Genre = i.Genre, Cible = i.Cible, Nom = i.Nom, Existe = i.Existe, Kind = i.Kind, Valeur = i.Valeur, Seq = i.Seq });
                return copie;
            }
        }

        /// <summary>Check() vrai avant l'Apply : la valeur visée est déjà là. Pas de Check, null ou exception : faux (on capture).</summary>
        private static bool DejaEnPlace(Tweak t)
        {
            if (t.Check == null) return false;
            try
            {
                return t.Check() == true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ------------------------------------------------------------------
        //  Verrou entre processus (interface et gardien d'ouverture de session)
        // ------------------------------------------------------------------

        /// <summary>
        /// Mutex nommé autour de chargement → Apply/Restaure → sauvegarde : l'interface et le gardien
        /// (« -apply » à l'ouverture de session) sont deux processus, le lock seul ne les sépare pas.
        /// Global\ (toutes sessions), Local\ si l'accès est refusé. Attente max 60 s, puis erreur.
        /// </summary>
        private sealed class VerrouPartage : IDisposable
        {
            private Mutex _mutex;

            public static VerrouPartage Prend()
            {
                Mutex m;
                try
                {
                    m = new Mutex(false, @"Global\ONYX-EtatOrigine");
                }
                catch (UnauthorizedAccessException)
                {
                    m = new Mutex(false, @"Local\ONYX-EtatOrigine");
                }
                bool acquis;
                try
                {
                    acquis = m.WaitOne(TimeSpan.FromSeconds(60));
                }
                catch (AbandonedMutexException)
                {
                    acquis = true;   // l'autre processus s'est arrêté en le tenant : il est à nous
                }
                if (!acquis)
                {
                    m.Dispose();
                    throw new InvalidOperationException("Une autre fenêtre ou tâche d'ONYX modifie le journal d'état d'origine depuis plus "
                        + "de 60 secondes : rien n'a été modifié pour ce réglage. Réessaie dans un instant.");
                }
                return new VerrouPartage { _mutex = m };
            }

            /// <summary>Libéré sur le même thread que Prend() (bloc using).</summary>
            public void Dispose()
            {
                if (_mutex == null) return;
                try
                {
                    _mutex.ReleaseMutex();
                }
                finally
                {
                    _mutex.Dispose();
                    _mutex = null;
                }
            }
        }

        // ------------------------------------------------------------------
        //  Fichiers
        // ------------------------------------------------------------------

        /// <summary>Fichier verrouillé un instant (autre processus, antivirus) : 3 essais à 100 ms.</summary>
        private const int Essais = 3;
        private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(100);

        /// <summary>Texte du fichier, null s'il n'existe pas ; relancé sur IOException.</summary>
        private static string LitFichier(string chemin)
        {
            return EtatOrigine.Reessaie(() => File.Exists(chemin) ? File.ReadAllText(chemin) : null, Essais, Pause);
        }

        private static bool DejaApplique(string id)
        {
            string chemin = TweakDrift.JournalPath;
            string texte;
            try
            {
                texte = LitFichier(chemin);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Liste des réglages appliqués (" + Path.GetFileName(chemin)
                    + ") illisible : " + ex.Message + ". Rien n'a été modifié pour ce réglage.", ex);
            }
            return texte != null && TweakDrift.Parse(texte).Exists(e => e.Id == id);
        }

        /// <summary>
        /// Journal du disque ; illisible (JSON) : mis de côté (.corrompu-AAAAMMJJ-HHmmss) et remplacé par
        /// un journal vide. Fichier inaccessible (verrouillé, accès refusé) : erreur claire, rien n'est modifié.
        /// </summary>
        private static EtatOrigine.Journal Charge()
        {
            string chemin = Chemin;
            try
            {
                string texte = LitFichier(chemin);
                if (texte == null) return new EtatOrigine.Journal();
                EtatOrigine.Journal j = EtatOrigine.Lire(texte);
                if (!j.Corrompu) return j;
                string aCote = chemin + ".corrompu-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                EtatOrigine.Reessaie(() => { File.Move(chemin, aCote); return true; }, Essais, Pause);
                return new EtatOrigine.Journal();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new InvalidOperationException("Journal d'état d'origine (" + Path.GetFileName(chemin)
                    + ") inaccessible : " + ex.Message + ". Rien n'a été modifié pour ce réglage.", ex);
            }
        }

        private static void Sauve(EtatOrigine.Journal j)
        {
            string chemin = Chemin;
            string tmp = chemin + ".tmp";
            string texte = EtatOrigine.Ecrire(j);
            EtatOrigine.Reessaie(() => { File.WriteAllText(tmp, texte); return true; }, Essais, Pause);
            EtatOrigine.Reessaie(() => { File.Move(tmp, chemin, true); return true; }, Essais, Pause);
        }
    }
}
