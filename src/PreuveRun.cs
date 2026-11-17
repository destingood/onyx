using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace BTOptimizer
{
    /// <summary>
    /// « Optimiser et prouver » : applique la sélection Auto (niveau équilibré) PAR LOTS, mesure
    /// la latence du PC au repos après chaque lot, et annule ce qui la dégrade. Le jugement est
    /// dans Preuve (logique pure, testée) ; ici on ne fait qu'orchestrer la machine.
    ///
    /// Règle : on n'annule QUE sur régression confirmée par une seconde mesure. Un lot neutre est
    /// gardé — beaucoup de réglages n'agissent qu'en jeu.
    ///
    /// Le rapport décrit l'état RÉEL : un réglage dont Apply a échoué n'est pas « gardé », un
    /// réglage dont l'annulation a échoué n'est pas « annulé ».
    /// </summary>
    internal static class PreuveRun
    {
        public const string FichierAttente = "bt-preuve-attente.txt";
        private const int SecondesMesure = 12;     // + ~1 s de test Sleep(1)
        private const int AttenteApresLotMs = 3000;

        public sealed class Rapport
        {
            public Preuve.Mesure Depart, Fin;
            public readonly List<string> Lignes = new List<string>();   // « catégorie : verdict »
            public readonly List<Tweak> Gardes = new List<Tweak>();
            public readonly List<Tweak> Annules = new List<Tweak>();
            public readonly List<Tweak> EnAttente = new List<Tweak>();
            public readonly List<Tweak> NonFaits = new List<Tweak>();          // jamais appliqués (arrêt, erreur)
            public readonly List<Tweak> EchecsApplication = new List<Tweak>();  // Apply a levé une erreur
            public readonly List<Tweak> EchecsAnnulation = new List<Tweak>();   // Revert a levé une erreur : RESTE appliqué
            public readonly List<Tweak> SansSauvegarde = new List<Tweak>();     // clé non sauvegardable : écarté
            public int DejaActifs;
            public bool Arrete, Echec, RedemarrageRequis, SansPointRestauration;
            public string Erreur;        // Echec : rien n'a été fait ; sinon : déroulé interrompu en route
            public string DossierSauvegarde;

            public string Texte()
            {
                var sb = new StringBuilder();
                if (Echec) { sb.Append("Rien n'a été modifié : ").Append(Erreur).Append("\r\n"); return sb.ToString(); }
                if (Depart != null) sb.Append("Avant : ").Append(Depart.Resume()).Append("\r\n");
                if (Fin != null) sb.Append("Après : ").Append(Fin.Resume()).Append("\r\n");
                if (Depart != null && Fin != null)
                    sb.Append("Bilan au repos : ").Append(Preuve.Libelle(Preuve.Juge(Depart, Fin))).Append("\r\n");
                sb.Append("\r\n");
                foreach (string l in Lignes) sb.Append("• ").Append(l).Append("\r\n");
                sb.Append("\r\n");
                sb.Append(Gardes.Count).Append(" réglage(s) gardé(s), ")
                  .Append(Annules.Count).Append(" annulé(s) pour régression, ")
                  .Append(DejaActifs).Append(" déjà actif(s).\r\n");
                Liste(sb, "annulé (régression)", Annules);
                Liste(sb, "échec d'application, état inchangé ou partiel", EchecsApplication);
                Liste(sb, "ANNULATION ÉCHOUÉE, reste appliqué (restaure la sauvegarde .reg si besoin)", EchecsAnnulation);
                Liste(sb, "écarté, sa clé de registre n'a pas pu être sauvegardée", SansSauvegarde);
                if (EnAttente.Count > 0)
                    sb.Append(EnAttente.Count).Append(" réglage(s) à redémarrage appliqué(s) : ONYX les vérifiera au premier lancement après un vrai redémarrage.\r\n");
                if (NonFaits.Count > 0)
                    sb.Append(NonFaits.Count).Append(" réglage(s) non appliqué(s)").Append(Arrete ? " (arrêt demandé)" : "").Append(".\r\n");
                if (Erreur != null) sb.Append("Déroulé interrompu par une erreur : ").Append(Erreur).Append(" — le lot en cours a été annulé.\r\n");
                if (Fin != null && !Fin.Etw)
                    sb.Append("Mesure sans session ETW (droits ou pilote) : seuls les compteurs système et Sleep(1) ont servi.\r\n");
                if (SansPointRestauration)
                    sb.Append("Point de restauration non créé (voir journal) : la sauvegarde .reg reste le filet.\r\n");
                if (DossierSauvegarde != null) sb.Append("Sauvegarde .reg : ").Append(DossierSauvegarde).Append("\r\n");
                return sb.ToString();
            }

            private static void Liste(StringBuilder sb, string quoi, List<Tweak> l)
            {
                foreach (Tweak t in l) sb.Append("  ✗ ").Append(quoi).Append(" : ").Append(t.Name).Append("\r\n");
            }
        }

        private sealed class Arret : Exception { }

        // ------------------------------------------------------------------
        //  Mesure
        // ------------------------------------------------------------------

        private static Preuve.Mesure Mesurer(EtwLive etw, bool etwOk, Action<string, int> log)
        {
            Action<string, int> muet = (msg, l) => { if (l >= 2) log(msg, l); };
            var m = new Preuve.Mesure { Quand = DateTime.Now };
            bool avecEtw = etwOk && etw != null && etw.Running;
            if (avecEtw) etw.Reset();
            var sw = Stopwatch.StartNew();
            BenchResult b = Bench.Run(SecondesMesure, muet);
            double sec = sw.Elapsed.TotalSeconds;
            m.SleepMoyMs = b.SleepAvgMs;
            m.SleepMaxMs = b.SleepMaxMs;
            if (avecEtw)
            {
                DpcIsrReport r = etw.Snapshot();
                double total = 0;
                foreach (DriverStat d in r.Drivers) total += d.DpcTotalUs + d.IsrTotalUs;
                m.ChargeUsParS = sec > 0 ? total / sec : -1;
                m.PireUs = r.WorstUs;
                m.Etw = true;
            }
            else if (b.DpcAvg >= 0 && b.IrqAvg >= 0)
            {
                // % de temps moyen par cœur → µs par seconde (même unité, autre source : Etw=false
                // empêche de comparer cette mesure à une mesure ETW).
                m.ChargeUsParS = (b.DpcAvg + b.IrqAvg) * 10000.0;
            }
            log("Mesure : " + m.Resume(), 0);
            return m;
        }

        /// <summary>Mesure, et si elle dit « régression », re-mesure une fois pour écarter le bruit.</summary>
        private static Preuve.Verdict JugeConfirme(Preuve.Mesure reference, EtwLive etw, bool etwOk,
                                                   Action<string, int> log, out Preuve.Mesure retenue)
        {
            retenue = Mesurer(etw, etwOk, log);
            Preuve.Verdict v = Preuve.Juge(reference, retenue);
            if (v != Preuve.Verdict.Regression) return v;
            log("Régression possible : seconde mesure pour confirmer…", 2);
            Thread.Sleep(AttenteApresLotMs);
            retenue = Mesurer(etw, etwOk, log);
            return Preuve.Juge(reference, retenue);
        }

        // ------------------------------------------------------------------
        //  Déroulé
        // ------------------------------------------------------------------

        private static bool Actif(Tweak t)
        {
            try { return t.Check != null && t.Check() == true; } catch { return false; }
        }

        private static void Ajoute(List<Tweak> l, IEnumerable<Tweak> quoi)
        {
            foreach (Tweak t in quoi) if (!l.Contains(t)) l.Add(t);
        }

        public static Rapport Lance(List<Tweak> all, Action<string, int> log, Func<bool> arretDemande)
        {
            return Lance(all, log, arretDemande, null);
        }

        /// <summary>
        /// <paramref name="idsImposes"/> non null : cette sélection remplace celle du profil
        /// matériel (le mode Compétition prouve SES réglages eSport, pas le profil équilibré).
        /// </summary>
        public static Rapport Lance(List<Tweak> all, Action<string, int> log, Func<bool> arretDemande, IList<string> idsImposes)
        {
            var rap = new Rapport();
            DateTime debut = DateTime.Now;
            Func<bool> stop = arretDemande ?? (() => false);

            HashSet<string> ids;
            if (idsImposes != null) ids = new HashSet<string>(idsImposes);
            else
            {
                try { ids = Hardware.AutoTuneIds(all, Hardware.Detect(), Hardware.LevelBalanced); }
                catch (Exception ex) { rap.Echec = true; rap.Erreur = "détection du matériel impossible (" + ex.Message + ")"; return rap; }
            }

            var candidats = new List<Tweak>();
            foreach (Tweak t in all)
            {
                if (!ids.Contains(t.Id)) continue;
                if (Actif(t)) rap.DejaActifs++; else candidats.Add(t);
            }
            log(candidats.Count + " réglage(s) à essayer, " + rap.DejaActifs + " déjà actif(s).", 0);
            if (candidats.Count == 0) return rap;

            // Un seul filet de sécurité pour tout le déroulé. ExportBackup ne lève pas d'erreur sur
            // une clé refusée : il la journalise en niveau 3. On capte ces lignes pour écarter les
            // réglages dont la sauvegarde est incomplète — on ne touche pas à ce qu'on ne peut
            // pas restaurer.
            var refus = new List<string>();
            try
            {
                rap.DossierSauvegarde = Sys.ExportBackup(candidats, (msg, l) => { if (l >= 3) refus.Add(msg); log(msg, l); });
                log("Sauvegarde du registre créée : " + rap.DossierSauvegarde, 1);
            }
            catch (Exception ex)
            {
                rap.Echec = true; rap.Erreur = "sauvegarde impossible (" + ex.Message + ")";
                log("ÉCHEC de la sauvegarde : rien n'a été modifié.", 3);
                return rap;
            }
            if (refus.Count > 0)
            {
                foreach (Tweak t in candidats)
                    foreach (string k in t.BackupKeys ?? new string[0])
                    {
                        string fin = k.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) ? k.Substring(4) : k;
                        if (refus.Any(r => r.IndexOf(fin, StringComparison.OrdinalIgnoreCase) >= 0)) { Ajoute(rap.SansSauvegarde, new[] { t }); break; }
                    }
                candidats = candidats.Where(t => !rap.SansSauvegarde.Contains(t)).ToList();
                log(rap.SansSauvegarde.Count + " réglage(s) écarté(s) : sauvegarde incomplète.", 2);
                if (candidats.Count == 0) return rap;
            }
            Sys.CreateRestorePoint("ONYX — avant optimisation prouvée", (msg, l) => { if (l >= 2) rap.SansPointRestauration = true; log(msg, l); });

            // Réglages appliqués mais pas encore jugés : c'est ce qu'il faut défaire si tout casse.
            var enCours = new List<Tweak>();
            Func<List<Tweak>, List<Tweak>> applique = l =>
            {
                EngineResult r = Engine.Run(l, true, false, false, log);
                Ajoute(rap.EchecsApplication, r.Failed);
                List<Tweak> ok = l.Where(t => !r.Failed.Contains(t)).ToList();
                enCours.AddRange(ok);
                return ok;
            };
            Action<List<Tweak>> retablit = l =>
            {
                EngineResult r = Engine.Run(l, false, false, false, log);
                Ajoute(rap.EchecsAnnulation, r.Failed);
                foreach (Tweak t in l) enCours.Remove(t);
            };

            using (var etw = new EtwLive())
            {
                bool etwOk = etw.Start();
                if (!etwOk) log("Session ETW indisponible (" + etw.LastError + ") : mesure réduite.", 2);
                else Thread.Sleep(1000);   // laisse la session se stabiliser

                Preuve.Mesure reference;
                try
                {
                    log("Mesure de référence…", 0);
                    reference = Mesurer(etw, etwOk, log);
                }
                catch (Exception ex)
                {
                    rap.Echec = true; rap.Erreur = "mesure de référence impossible (" + ex.Message + ")";
                    return rap;
                }
                rap.Depart = reference;

                var sansReboot = candidats.Where(t => !t.Reboot).ToList();
                var avecReboot = candidats.Where(t => t.Reboot).ToList();
                var categories = new List<string>(Cat.Order);
                foreach (Tweak t in sansReboot) if (!categories.Contains(t.Category)) categories.Add(t.Category);

                try
                {
                    foreach (string cat in categories)
                    {
                        List<Tweak> lot = sansReboot.Where(t => t.Category == cat).ToList();
                        if (lot.Count == 0) continue;
                        // L'arrêt est honoré ENTRE deux lots : un lot commencé est jugé jusqu'au bout,
                        // pour ne jamais laisser un réglage appliqué sans verdict.
                        if (stop()) { rap.Arrete = true; break; }

                        log("Lot « " + cat + " » : " + lot.Count + " réglage(s).", 0);
                        List<Tweak> ok = applique(lot);
                        if (ok.Count == 0) { rap.Lignes.Add(cat + " : aucun réglage n'a pu être appliqué"); continue; }
                        Thread.Sleep(AttenteApresLotMs);
                        Preuve.Mesure m;
                        Preuve.Verdict v = JugeConfirme(reference, etw, etwOk, log, out m);

                        if (v != Preuve.Verdict.Regression)
                        {
                            Ajoute(rap.Gardes, ok);
                            enCours.Clear();
                            rap.Lignes.Add(cat + " : " + Preuve.Libelle(v) + ", gardé");
                            reference = m;
                            continue;
                        }

                        log("Régression confirmée : lot « " + cat + " » annulé, recherche du réglage fautif.", 2);
                        retablit(ok);
                        Thread.Sleep(AttenteApresLotMs);

                        var gardesDuLot = new List<Tweak>();
                        Preuve.Mesure refLocale = reference;
                        Func<List<Tweak>, bool> essai = sous =>
                        {
                            if (stop()) throw new Arret();
                            // Un réglage dont l'annulation a échoué est resté appliqué : ne pas le
                            // ré-appliquer ni le compter « gardé », il est déjà signalé.
                            List<Tweak> okSous = applique(sous.Where(t => !rap.EchecsAnnulation.Contains(t)).ToList());
                            if (okSous.Count == 0) return true;   // rien d'appliqué : rien à juger
                            Thread.Sleep(AttenteApresLotMs);
                            Preuve.Mesure ms;
                            if (JugeConfirme(refLocale, etw, etwOk, log, out ms) == Preuve.Verdict.Regression)
                            {
                                retablit(okSous);
                                Thread.Sleep(AttenteApresLotMs);
                                return false;
                            }
                            Ajoute(gardesDuLot, okSous);
                            Ajoute(rap.Gardes, okSous);
                            enCours.Clear();
                            refLocale = ms;
                            return true;
                        };

                        try
                        {
                            List<Tweak> fautifs = Preuve.TrouveFautifs(ok, essai)
                                .Where(t => !rap.EchecsApplication.Contains(t) && !rap.EchecsAnnulation.Contains(t)).ToList();
                            Ajoute(rap.Annules, fautifs);
                            foreach (Tweak f in fautifs) log("Annulé (régression) : " + f.Name, 2);
                            rap.Lignes.Add(fautifs.Count == 0
                                ? cat + " : régression du lot non reproduite par ses moitiés (bruit ou interaction), gardé"
                                : cat + " : régression, " + fautifs.Count + " réglage(s) annulé(s), " + gardesDuLot.Count + " gardé(s)");
                        }
                        finally { reference = refLocale; }
                    }
                }
                catch (Arret)
                {
                    rap.Arrete = true;
                    rap.Lignes.Add("Arrêt demandé pendant la recherche du réglage fautif");
                }
                catch (Exception ex)
                {
                    rap.Erreur = ex.Message;
                    log("Erreur pendant le déroulé (" + ex.Message + ") : annulation du lot en cours.", 3);
                }

                // Quoi qu'il soit arrivé, rien ne reste appliqué sans verdict.
                if (enCours.Count > 0)
                {
                    try { retablit(new List<Tweak>(enCours)); }
                    catch (Exception ex) { log("Annulation du lot en cours impossible : " + ex.Message, 3); }
                }

                bool continuer = rap.Erreur == null && !rap.Arrete && !stop();
                if (avecReboot.Count > 0 && continuer)
                    AppliqueAvecRedemarrage(avecReboot, reference, applique, retablit, rap, log);
                if (!continuer && avecReboot.Count > 0 && stop()) rap.Arrete = true;

                rap.Fin = reference;
            }

            // Tout candidat qui n'a ni verdict ni échec n'a jamais été appliqué.
            foreach (Tweak t in candidats)
                if (!rap.Gardes.Contains(t) && !rap.Annules.Contains(t) && !rap.EnAttente.Contains(t)
                    && !rap.EchecsApplication.Contains(t) && !rap.EchecsAnnulation.Contains(t))
                    rap.NonFaits.Add(t);
            // Pour la validation en jeu : ce que cette preuve a laissé appliqué.
            try { PreuveJeu.EnregistreDernierePreuve(debut, rap.Gardes.Concat(rap.EnAttente)); } catch { }
            try { AppStats.Invalidate(); } catch { }
            return rap;
        }

        private static void AppliqueAvecRedemarrage(List<Tweak> lot, Preuve.Mesure reference,
            Func<List<Tweak>, List<Tweak>> applique, Action<List<Tweak>> retablit, Rapport rap, Action<string, int> log)
        {
            log("Lot à redémarrage : " + lot.Count + " réglage(s), vérifiés après redémarrage.", 0);
            List<Tweak> ok = applique(lot);
            if (ok.Count == 0) return;

            // Une attente pas encore jugée (redémarrage pas fait depuis la dernière preuve) est
            // FUSIONNÉE : l'écraser ferait perdre le suivi de ses réglages.
            var attente = new Preuve.Attente { Applique = DateTime.Now, Reference = reference };
            try
            {
                string chemin = AppPaths.File(FichierAttente);
                Preuve.Attente ancienne = File.Exists(chemin) ? Preuve.Lit(File.ReadAllText(chemin)) : null;
                if (ancienne != null) attente.Ids.AddRange(ancienne.Ids);
                foreach (Tweak t in ok) if (!attente.Ids.Contains(t.Id)) attente.Ids.Add(t.Id);
                File.WriteAllText(chemin, Preuve.Ecrit(attente), new UTF8Encoding(false));
                Ajoute(rap.EnAttente, ok);
                rap.RedemarrageRequis = true;
                rap.Lignes.Add("Réglages à redémarrage : en attente de preuve après redémarrage");
            }
            catch (Exception ex)
            {
                log("Attente de preuve non enregistrée (" + ex.Message + ") : lot à redémarrage annulé.", 3);
                retablit(ok);
            }
        }

        // ------------------------------------------------------------------
        //  Après redémarrage
        // ------------------------------------------------------------------

        public static bool AttenteExiste()
        {
            try { return File.Exists(AppPaths.File(FichierAttente)); } catch { return false; }
        }

        /// <summary>
        /// Heure du dernier VRAI démarrage. Avec le démarrage rapide, « Arrêter » puis rallumer
        /// ne remet pas ce compteur à zéro : c'est voulu, car ce n'est pas un redémarrage du noyau
        /// et plusieurs réglages à redémarrage (bcdedit, pilotes) n'y prennent pas effet. Le
        /// verdict attend donc un « Redémarrer ».
        /// </summary>
        private static DateTime DernierDemarrage()
        {
            return DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
        }

        /// <summary>
        /// À appeler au lancement d'ONYX, hors du fil d'interface. Rend null s'il n'y a rien à juger
        /// (pas d'attente, ou Windows n'a pas encore redémarré). Sinon : mesure, verdict, et
        /// annulation du lot à redémarrage si la régression est confirmée.
        /// </summary>
        public static Rapport VerifieAttente(List<Tweak> all, Action<string, int> log)
        {
            string chemin = AppPaths.File(FichierAttente);
            Preuve.Attente a;
            try { a = File.Exists(chemin) ? Preuve.Lit(File.ReadAllText(chemin)) : null; }
            catch { return null; }
            if (a == null) { try { if (File.Exists(chemin)) File.Delete(chemin); } catch { } return null; }
            if (!Preuve.AttenteMesurable(a, DernierDemarrage())) return null;

            // Le démarrage de Windows est bruyant (mises à jour, analyse antivirus) : on laisse
            // passer les 3 premières minutes avant de mesurer.
            double reste = 180 - TimeSpan.FromMilliseconds(Environment.TickCount64).TotalSeconds;
            if (reste > 0) Thread.Sleep(TimeSpan.FromSeconds(reste));

            var lot = all.Where(t => a.Ids.Contains(t.Id)).ToList();
            var rap = new Rapport { Depart = a.Reference };
            try
            {
                using (var etw = new EtwLive())
                {
                    bool etwOk = etw.Start();
                    if (etwOk) Thread.Sleep(1000);
                    Preuve.Mesure m;
                    Preuve.Verdict v = JugeConfirme(a.Reference, etw, etwOk, log, out m);
                    rap.Fin = m;
                    if (v == Preuve.Verdict.Regression)
                    {
                        log("Réglages à redémarrage : régression confirmée, annulation.", 2);
                        EngineResult r = Engine.Run(lot, false, false, false, log);
                        Ajoute(rap.EchecsAnnulation, r.Failed);
                        Ajoute(rap.Annules, lot.Where(t => !r.Failed.Contains(t)));
                        rap.RedemarrageRequis = true;
                        rap.Lignes.Add("Réglages à redémarrage : régression, annulés (redémarre pour finir)");
                    }
                    else
                    {
                        Ajoute(rap.Gardes, lot);
                        rap.Lignes.Add("Réglages à redémarrage : " + Preuve.Libelle(v) + ", gardés");
                    }
                }
            }
            catch (Exception ex)
            {
                // Mesure impossible : on garde le fichier, la vérification sera retentée au
                // prochain lancement.
                log("Vérification après redémarrage impossible : " + ex.Message, 3);
                return null;
            }
            try { File.Delete(chemin); } catch { }
            try { AppStats.Invalidate(); } catch { }
            return rap;
        }
    }
}
