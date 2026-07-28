using System;
using System.Collections.Generic;
using System.Text;

namespace BTOptimizer
{
    /// <summary>
    /// « TOUT optimiser le réseau » : enchaîne, dans l'ordre qui a du sens, TOUTES les
    /// optimisations réseau que Windows permet — avec un filet à chaque étape et une mesure
    /// AVANT/APRÈS pour dire la vérité sur le gain.
    ///
    /// Ordre : point de restauration → mesure avant → optimisations registre (catalogue, avec
    /// sauvegarde .reg) → réglages TCP/IP (netsh) → MTU du lien mobile → DNS le plus rapide
    /// (vérifié, retour arrière automatique s'il ne répond pas) → mesure après.
    ///
    /// Deux garde-fous d'honnêteté :
    ///  • sur un lien mobile ou en CGNAT, on N'applique PAS la coupure des tunnels IPv6 :
    ///    l'IPv6 est justement la sortie de secours du NAT partagé ;
    ///  • un DNS n'est adopté que s'il est NETTEMENT plus rapide ET qu'il répond après bascule.
    /// </summary>
    internal static class NetAllFix
    {
        public class Outcome
        {
            public double PingBefore = -1, JitterBefore = -1, PingAfter = -1, JitterAfter = -1;
            public int LossBefore = -1, LossAfter = -1;
            public readonly List<string> Done = new List<string>();
            public readonly List<string> Skipped = new List<string>();
            public bool RebootNeeded;
        }

        private static readonly string[][] TcpSettings =
        {
            new[] { "autotuninglevel", "normal",   "fenêtre TCP automatique (débloque les téléchargements)" },
            new[] { "rss",            "enabled",  "réception répartie sur plusieurs cœurs" },
            new[] { "ecncapability",  "disabled", "ECN coupé (routeurs qui le gèrent mal)" },
            new[] { "timestamps",     "disabled", "horodatages RFC 1323 coupés" },
            new[] { "rsc",            "disabled", "coalescence RSC coupée (latence plus basse)" },
        };

        // Résolveurs publics testés (le FAI reste la référence à battre).
        private static readonly string[][] Resolvers =
        {
            new[] { "Cloudflare", "1.1.1.1", "1.0.0.1" },
            new[] { "Google",     "8.8.8.8", "8.8.4.4" },
            new[] { "Quad9",      "9.9.9.9", "149.112.112.112" },
        };

        public static Outcome RunAll(Action<string, int> log)
        {
            Action<string, int> L = log ?? delegate { };
            var o = new Outcome();

            L("① Filet de sécurité : point de restauration…", 0);
            try { Sys.CreateRestorePoint("ONYX — avant TOUT optimiser le réseau", L); }
            catch { L("   (point de restauration indisponible — la sauvegarde .reg reste faite)", 2); }

            L("② Mesure AVANT (pings de référence)…", 0);
            double avg, jit; int loss;
            if (ChatActions.PingSample(6, 900, out avg, out jit, out loss))
            { o.PingBefore = avg; o.JitterBefore = jit; o.LossBefore = loss; }

            // Le contexte décide de ce qu'on applique ET de ce qu'on s'interdit : on identifie
            // donc le lien AVANT de toucher à quoi que ce soit (MTU mesurée = l'indice le plus sûr).
            L("③ Reconnaissance du lien (CGNAT, MTU, type d'accès)…", 0);
            var rep = new MobileNet.Report();
            MobileNet.FillInterface(rep);
            try { MobileNet.DetectCgnat(rep); } catch { }
            rep.PingIdle = o.PingBefore;
            rep.MtuOptimal = MobileNet.DiscoverMtu(L);
            rep.Kind = MobileNet.Classify(rep);
            bool sharedIp = rep.Cgnat || rep.CgnatProbable;
            bool mobile = rep.Kind == MobileNet.Access.Mobile;
            L("   Lien : " + MobileNet.AccessLabel(rep.Kind) + (sharedIp ? " · IPv4 partagée (CGNAT)" : ""), 0);

            L("④ Optimisations réseau du catalogue (sauvegarde .reg automatique)…", 0);
            ApplyCatalog(o, sharedIp, L);

            if (mobile || sharedIp)
            {
                L("⑤ Réglages spécifiques 4G/5G…", 0);
                ApplyMobileExtras(o, mobile, sharedIp, L);
            }
            else o.Skipped.Add("Réglages spéciaux 4G/5G : non appliqués — ton lien n'en est pas un (c'est très bien).");

            L("⑥ Réglages TCP/IP (netsh, réversibles)…", 0);
            ApplyTcp(o, L);

            L("⑦ MTU du lien…", 0);
            ApplyMtu(o, rep, L);

            L("⑧ DNS : on garde le plus rapide (vérifié)…", 0);
            ApplyDns(o, L);

            L("⑨ Mesure APRÈS…", 0);
            if (ChatActions.PingSample(6, 900, out avg, out jit, out loss))
            { o.PingAfter = avg; o.JitterAfter = jit; o.LossAfter = loss; }

            L("Terminé.", 1);
            return o;
        }

        // ------------------------------------------------------------------
        private static void ApplyCatalog(Outcome o, bool sharedIp, Action<string, int> L)
        {
            try
            {
                var sel = new List<Tweak>();
                foreach (Tweak t in Catalog.All())
                {
                    if (t.Category != Cat.Reseau) continue;
                    if (!t.Recommended && !t.Esport) continue;
                    if (sharedIp && t.Id == "ipv6_tunnels_off")
                    {
                        o.Skipped.Add("Tunnels IPv6 laissés en place — ton adresse IPv4 est partagée (CGNAT), "
                                    + "l'IPv6 est ta seule sortie propre pour le jeu.");
                        continue;
                    }
                    bool? already = null;
                    try { if (t.Check != null) already = t.Check(); } catch { }
                    if (already == true) continue;         // déjà actif : on n'y touche pas
                    sel.Add(t);
                }
                if (sel.Count == 0) { o.Done.Add("Optimisations réseau : déjà toutes actives."); return; }

                Engine.Run(sel, true, true, false, L);     // apply, backup .reg, pas de 2e point de restauration
                foreach (Tweak t in sel) if (t.Reboot) o.RebootNeeded = true;
                o.Done.Add(sel.Count + " optimisation(s) réseau appliquée(s) (réversibles depuis l'optimiseur).");
            }
            catch (Exception ex) { L("   Catalogue réseau : " + ex.Message, 3); }
        }

        /// <summary>Les réglages qui n'ont de sens QUE sur un accès mobile ou en IPv4 partagée :
        /// BBR2 (lien radio), IPv6 complète et Teredo (sortie du CGNAT). Hors presets par
        /// nature — c'est ici, une fois le lien reconnu, qu'ils prennent tout leur sens.</summary>
        private static void ApplyMobileExtras(Outcome o, bool mobile, bool sharedIp, Action<string, int> L)
        {
            var ids = new List<string>();
            if (mobile) ids.Add("tcp_bbr2");                 // congestion adaptée au radio
            if (sharedIp) { ids.Add("ipv6_restore"); ids.Add("teredo_client"); }

            foreach (string id in ids)
            {
                Tweak t = null;
                try { foreach (Tweak x in Catalog.All()) if (x.Id == id) { t = x; break; } }
                catch { }
                if (t == null) continue;
                bool? already = null;
                try { if (t.Check != null) already = t.Check(); } catch { }
                if (already == true) continue;
                try
                {
                    Engine.Run(new List<Tweak> { t }, true, true, false, L);
                    if (t.Reboot) o.RebootNeeded = true;
                    o.Done.Add(t.Name);
                }
                catch (Exception ex) { o.Skipped.Add(t.Name + " — refusé : " + ex.Message); }
            }
            if (ids.Count == 0) o.Skipped.Add("Réglages 4G/5G : aucun ne s'applique à ce lien.");
        }

        private static void ApplyTcp(Outcome o, Action<string, int> L)
        {
            int ok = 0;
            foreach (string[] s in TcpSettings)
            {
                try
                {
                    var r = Sys.Run(Sys.Sys32("netsh.exe"), "int tcp set global " + s[0] + "=" + s[1]);
                    if (r != null && r.ExitCode == 0) { ok++; L("   ✓ " + s[2], 0); }
                }
                catch { }
            }
            if (ok > 0) o.Done.Add(ok + " réglage(s) TCP/IP appliqué(s) (panneau Réglages TCP/IP pour revenir en arrière).");
            else o.Skipped.Add("Réglages TCP/IP : refusés par Windows (droits administrateur requis).");
        }

        private static void ApplyMtu(Outcome o, MobileNet.Report rep, Action<string, int> L)
        {
            try
            {
                int mtu = rep.MtuOptimal;   // déjà mesurée à l'étape de reconnaissance du lien
                if (mtu < 996) { o.Skipped.Add("MTU : non mesurable sur ce lien (ICMP filtré) — rien touché."); return; }
                if (mtu >= rep.MtuCurrent)
                {
                    o.Done.Add("MTU déjà optimale (" + rep.MtuCurrent + ") — c'est le cas normal en fibre/ADSL.");
                    return;
                }
                if (MobileNet.ApplyMtu(rep, L))
                    o.Done.Add("MTU ramenée à " + mtu + " (lien mobile) — annulable dans « Ma connexion & ma box ».");
                else
                    o.Skipped.Add("MTU : application refusée (droits administrateur requis).");
            }
            catch (Exception ex) { L("   MTU : " + ex.Message, 3); }
        }

        private static void ApplyDns(Outcome o, Action<string, int> L)
        {
            try
            {
                double best = DnsBench.QueryMs("", "www.google.com", 800, 3);   // "" = résolveur courant
                if (best <= 0) best = 9999;
                string bestName = "ton DNS actuel";
                string[] bestServers = null;
                foreach (string[] r in Resolvers)
                {
                    double ms = DnsBench.QueryMs(r[1], "www.google.com", 800, 3);
                    if (ms <= 0) continue;
                    L("   " + r[0] + " : " + ms.ToString("0") + " ms", 0);
                    if (ms < best * 0.7)                                       // net : 30 % plus rapide au moins
                    { best = ms; bestName = r[0]; bestServers = new[] { r[1], r[2] }; }
                }
                if (bestServers == null)
                {
                    o.Done.Add("DNS : le tien est déjà bon — aucun changement (le plus sûr).");
                    return;
                }

                var snap = Sys.SnapshotDns();                                   // filet : état d'origine
                Sys.SetDns(bestServers, L);
                double check = DnsBench.QueryMs(bestServers[0], "www.microsoft.com", 1200, 2);
                if (check <= 0)
                {
                    Sys.RestoreDnsSnapshot(snap, L);
                    o.Skipped.Add("DNS : bascule annulée automatiquement (le résolveur ne répondait plus).");
                    return;
                }
                o.Done.Add("DNS basculé sur " + bestName + " (" + best.ToString("0") + " ms) — « DNS rapide » pour revenir au DNS du FAI.");
            }
            catch (Exception ex) { L("   DNS : " + ex.Message, 3); }
        }

        /// <summary>Le compte-rendu lisible, avec le gain RÉEL (ou l'absence de gain, dite franchement).</summary>
        public static string Summary(Outcome o)
        {
            var sb = new StringBuilder();
            sb.AppendLine("RÉSEAU — TOUT OPTIMISER : compte-rendu");
            sb.AppendLine();
            foreach (string d in o.Done) sb.AppendLine("  ✓ " + d);
            foreach (string s in o.Skipped) sb.AppendLine("  • " + s);
            sb.AppendLine();
            if (o.PingBefore >= 0 && o.PingAfter >= 0)
            {
                double dp = o.PingBefore - o.PingAfter, dj = o.JitterBefore - o.JitterAfter;
                sb.AppendLine("  Ping  : " + o.PingBefore.ToString("0") + " ms → " + o.PingAfter.ToString("0") + " ms");
                sb.AppendLine("  Gigue : " + o.JitterBefore.ToString("0") + " ms → " + o.JitterAfter.ToString("0") + " ms");
                sb.AppendLine();
                if (dp >= 3 || dj >= 3)
                    sb.AppendLine("  → Gain mesuré sur ce test.");
                else
                    sb.AppendLine("  → Pas de gain visible sur CE test, et c'est normal : ces réglages retirent surtout"
                                + "\r\n     des à-coups (gigue, saccades sous charge) que six pings ne montrent pas."
                                + "\r\n     Le vrai juge, c'est une partie de jeu.");
            }
            if (o.RebootNeeded) sb.AppendLine("\r\n  ⚠ Un redémarrage est nécessaire pour une partie des réglages.");
            sb.AppendLine("\r\n  Tout est réversible : optimiseur (annuler), Réglages TCP/IP, DNS rapide,"
                        + "\r\n  et « Rétablir la MTU » dans Ma connexion & ma box.");
            return sb.ToString();
        }
    }
}
