using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace BTOptimizer
{
    /// <summary>
    /// LE GESTE du mode Compétition — la moitié qui touche la machine. Les règles vivent dans
    /// Competition.cs (pures, testées au banc).
    ///
    /// L'état « d'avant » est écrit sur le disque AVANT le premier changement, puis réécrit après
    /// chaque étape : une fermeture brutale en route laisse un état qui décrit exactement ce qui
    /// a été posé, et « Annuler Compétition » reste possible au lancement suivant.
    /// </summary>
    internal static partial class Competition
    {
        public sealed class Choix
        {
            public bool Profils = true, ModeJeu = true, Esport = true, Plafond = false;
        }

        private static readonly object _verrou = new object();

        private static string Chemin { get { return AppPaths.File(Fichier); } }

        /// <summary>L'état de la compétition en cours, null s'il n'y en a pas.</summary>
        public static Etat Active()
        {
            try { return File.Exists(Chemin) ? Lit(File.ReadAllText(Chemin)) : null; }
            catch (Exception ex) { JournalTechnique.Echec("Competition.Active", ex); return null; }
        }

        private static void Enregistre(Etat e)
        {
            File.WriteAllText(Chemin, Ecrit(e), new UTF8Encoding(false));
        }

        /// <summary>
        /// Active la compétition. Rend le rapport de la preuve eSport, ou null si elle n'a pas
        /// tourné (case décochée, refus, erreur avant le premier changement).
        /// </summary>
        public static PreuveRun.Rapport Active(List<Tweak> all, Choix c, Action<string, int> log, Func<bool> arret)
        {
            if (!Monitor.TryEnter(_verrou)) { log("Une opération Compétition est déjà en cours.", 2); return null; }
            try
            {
                if (!PeutActiver(Active()))
                {
                    log("Compétition déjà active : annule-la d'abord (sinon l'état d'avant serait perdu).", 2);
                    return null;
                }
                Func<bool> stop = arret ?? (() => false);

                // 1) L'état d'avant, avant tout changement.
                var e = new Etat { Active = DateTime.Now, AutoJeuAvant = AutoJeu.Actif };
                if (c.Plafond)
                {
                    int pose = -1;
                    try { pose = FrameCap.Pose(); } catch { }
                    e.PlafondAvant = pose;
                    if (pose < 0) log("Plafond actuel illisible : le plafond ne sera pas touché.", 2);
                }
                try { Enregistre(e); }
                catch (Exception ex) { log("État de la compétition non enregistré (" + ex.Message + ") : rien n'est modifié.", 3); return null; }

                // 2) Point de restauration : celui de la preuve suffit quand elle tourne.
                if (!c.Esport) { try { Sys.CreateRestorePoint("ONYX — Compétition", log); } catch { } }

                // 3) Profils Auto pour les jeux installés qui n'en ont pas.
                if (c.Profils && !stop())
                {
                    List<GameScan.GameInfo> jeux = GameScan.Known();
                    GameScan.Detect(jeux);
                    Dictionary<string, Tuple<string, long>> profils = Sys.LoadGameAffinity();
                    List<string> sans = JeuxSansProfil(jeux.Where(j => j.Detected).Select(j => j.Name), profils);
                    int n = Math.Min(Environment.ProcessorCount, 63);
                    long masqueAuto = ((1L << n) - 1) & ~1L;   // même valeur que GameProfileForm ; ignorée en Auto
                    foreach (string j in sans) profils[j] = Tuple.Create("Auto", masqueAuto);
                    if (sans.Count > 0) Sys.SaveGameAffinity(profils);
                    e.Profils = sans;
                    Enregistre(e);
                    log(sans.Count == 0 ? "Profils de jeu : tous les jeux installés en ont déjà un."
                                        : "Profil Auto ajouté pour : " + string.Join(", ", sans) + ".", 1);
                }

                // 4) Mode Jeu automatique.
                if (c.ModeJeu && !stop())
                {
                    AutoJeu.Actif = true;
                    log("Mode Jeu automatique activé.", 1);
                }

                // 5) Plafond FPS recommandé pour l'écran principal.
                if (c.Plafond && e.PlafondAvant >= 0 && !stop())
                {
                    int hz = FrameCap.FrequenceEcran(), fps = FrameCap.Recommande(hz);
                    if (fps > 0 && FrameCap.Applique(fps, log)) log("Plafond FPS : " + fps + " (écran " + hz + " Hz).", 1);
                    else { e.PlafondAvant = -1; log("Plafond FPS non posé (fréquence d'écran inconnue ou refus du pilote).", 2); }
                    Enregistre(e);
                }

                // 6) Réglages eSport, prouvés (appliqués par lots, annulés s'ils dégradent).
                PreuveRun.Rapport rap = null;
                if (c.Esport && !stop())
                {
                    List<string> ids = all.Where(t => t.Esport && !string.IsNullOrEmpty(t.Id)).Select(t => t.Id).Distinct().ToList();
                    rap = PreuveRun.Lance(all, log, stop, ids);
                    if (rap != null)
                        e.Reglages = rap.Gardes.Concat(rap.EnAttente).Select(t => t.Id).Distinct().ToList();
                }

                Enregistre(e);
                log("Compétition active. « Annuler Compétition » remet tout ce qu'elle a changé.", 1);
                return rap;
            }
            finally { Monitor.Exit(_verrou); }
        }

        /// <summary>
        /// Remet ce que la compétition a posé, et seulement ça. Faux s'il n'y avait rien à annuler
        /// ou si des réglages n'ont pas pu être rétablis.
        /// </summary>
        public static bool Annule(List<Tweak> all, Action<string, int> log)
        {
            if (!Monitor.TryEnter(_verrou)) { log("Une opération Compétition est déjà en cours.", 2); return false; }
            try
            {
                Etat e = Active();
                if (e == null) { log("Aucune compétition active.", 0); return false; }
                bool ok = true;

                // 1) Réglages eSport encore actifs. Un réglage que Windows dit déjà inactif n'est
                //    pas « rétabli » : Revert impose des valeurs en dur.
                var lot = all.Where(t => e.Reglages.Contains(t.Id))
                             .Where(t => { try { return t.Check == null || t.Check() != false; } catch { return true; } })
                             .ToList();
                if (lot.Count > 0)
                {
                    EngineResult r = null;
                    try { r = Engine.Run(lot, false, true, false, log); }
                    catch (Exception ex) { log("Erreur pendant l'annulation des réglages : " + ex.Message, 3); ok = false; }
                    if (r != null && r.Failed.Count > 0)
                    {
                        ok = false;
                        log(r.Failed.Count + " réglage(s) n'ont pas pu être annulés : la sauvegarde .reg reste disponible.", 3);
                    }
                    if (r != null && r.RebootNeeded) log("Redémarre le PC pour finir l'annulation.", 2);
                }

                // 2) Profils ajoutés, s'ils sont encore tels que posés.
                Dictionary<string, Tuple<string, long>> profils = Sys.LoadGameAffinity();
                List<string> retirer = ProfilsARetirer(e, profils);
                if (retirer.Count > 0)
                {
                    foreach (string j in retirer) profils.Remove(j);
                    Sys.SaveGameAffinity(profils);
                    log("Profils retirés : " + string.Join(", ", retirer) + ".", 0);
                }

                // 3) Mode Jeu automatique tel qu'il était.
                AutoJeu.Actif = e.AutoJeuAvant;

                // 4) Plafond FPS d'avant (0 rend la main au pilote).
                if (e.PlafondAvant >= 0 && !FrameCap.Applique(e.PlafondAvant, log))
                {
                    ok = false;
                    log("Plafond FPS d'avant (" + e.PlafondAvant + ") non rétabli.", 3);
                }

                try { File.Delete(Chemin); } catch (Exception ex) { JournalTechnique.Echec("Competition.Annule/fichier", ex); }
                log(ok ? "Compétition annulée : état d'avant rétabli." : "Compétition annulée, avec des erreurs (voir ci-dessus).", ok ? 1 : 2);
                return ok;
            }
            finally { Monitor.Exit(_verrou); }
        }
    }
}
