using System;
using System.Collections.Generic;
using System.Text;

namespace BTOptimizer
{
    /// <summary>Génère un rapport de configuration HTML autonome (état des optimisations + matériel).</summary>
    internal static class Report
    {
        private static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        public static string BuildHtml(List<Tweak> tweaks, HwProfile hw)
        {
            int active = 0, total = 0;
            var body = new StringBuilder();

            foreach (string cat in Cat.Order)
            {
                var rows = new StringBuilder();
                int catCount = 0;
                foreach (Tweak t in tweaks)
                {
                    if (t.Category != cat) continue;
                    catCount++; total++;
                    bool? st = null;
                    if (t.Check != null) { try { st = t.Check(); } catch { st = null; } }
                    string badge, cls;
                    if (st == true) { badge = "Actif"; cls = "on"; active++; }
                    else if (st == false) { badge = "Inactif"; cls = "off"; }
                    else { badge = "—"; cls = "na"; }
                    string reboot = t.Reboot ? " <span class='rb'>redémarrage</span>" : "";
                    rows.Append("<tr><td>").Append(Esc(t.Name)).Append(reboot)
                        .Append("</td><td><span class='b ").Append(cls).Append("'>").Append(badge).Append("</span></td></tr>");
                }
                if (catCount == 0) continue;
                body.Append("<h2>").Append(Esc(cat)).Append("</h2><table>").Append(rows).Append("</table>");
            }

            string date = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            var head = new StringBuilder();
            head.Append("<div class='cards'>");
            head.Append(Card("Optimisations actives", active + " / " + total));
            head.Append(Card("Édition", Esc(License.Status())));
            head.Append(Card("Système", Esc(Sys.OsDescription())));
            head.Append(Card("Timer système", Native.CurrentTimerMs().ToString("0.0") + " ms"));
            if (hw != null)
            {
                head.Append(Card("Processeur", Esc(hw.CpuName)));
                head.Append(Card("Mémoire", hw.RamGB + " Go"));
                head.Append(Card("Carte graphique", Esc(hw.GpuName)));
                head.Append(Card("Disque", hw.AllSsd ? "SSD" : (hw.AnyHdd ? "SSD + HDD" : "?")));
            }
            head.Append("</div>");

            var html = new StringBuilder();
            html.Append("<!doctype html><html lang='fr'><head><meta charset='utf-8'>");
            html.Append("<meta name='viewport' content='width=device-width, initial-scale=1'>");
            html.Append("<title>DesTinGOOD — Rapport de configuration</title><style>");
            html.Append(Css());
            html.Append("</style></head><body><div class='wrap'>");
            html.Append("<header><div class='brand'>DesTinGOOD</div><div class='sub'>Rapport de configuration · ").Append(date).Append("</div></header>");
            html.Append(head);
            html.Append(Diagnostic());
            html.Append(body);
            html.Append("<footer>Logiciel fourni « en l'état », sans garantie. Non affilié à Microsoft, NVIDIA, AMD ou Intel. Toutes les modifications sont réversibles depuis l'application.</footer>");
            html.Append("</div></body></html>");
            return html.ToString();
        }

        private static string Card(string label, string value)
        {
            return "<div class='card'><div class='k'>" + value + "</div><div class='l'>" + label + "</div></div>";
        }

        // Section « Diagnostic santé » : audit rapide (crashs, réglages néfastes, bibliothèques, restauration, disque).
        private static string Diagnostic()
        {
            int nvl = 0, bsod = 0, badTweaks = 0, libMissing = 0, restorePts = 0;
            double freeGB = -1, pct = -1;
            try { nvl = CrashScan.CountProvider("nvlddmkm", 14); } catch { }
            try { bsod = CrashScan.Bsod(14); } catch { }
            try { foreach (Checkup.Item it in Checkup.Analyze()) if (it.Problem) badTweaks++; } catch { }
            try { foreach (LibScan.LibItem it in LibScan.Items()) { if (!it.Essential) continue; bool ok; try { ok = it.Installed(); } catch { ok = false; } if (!ok) libMissing++; } } catch { }
            try { restorePts = Sys.ListRestorePoints().Count; } catch { }
            try
            {
                var sys = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory));
                freeGB = sys.AvailableFreeSpace / 1073741824.0;
                pct = sys.TotalSize > 0 ? (double)sys.AvailableFreeSpace / sys.TotalSize * 100 : -1;
            }
            catch { }

            var rows = new StringBuilder();
            rows.Append(DiagRow("Crashs pilote GPU (14 jours)", nvl == 0 ? "aucun" : (nvl >= 200 ? "200+" : nvl.ToString()) + " erreur(s)", nvl == 0));
            rows.Append(DiagRow("Écrans bleus (14 jours)", bsod == 0 ? "aucun" : bsod.ToString(), bsod == 0));
            rows.Append(DiagRow("Réglages néfastes d'un ancien optimiseur", badTweaks == 0 ? "aucun" : badTweaks + " à corriger", badTweaks == 0));
            rows.Append(DiagRow("Bibliothèques de jeu essentielles", libMissing == 0 ? "toutes présentes" : libMissing + " manquante(s)", libMissing == 0));
            rows.Append(DiagRow("Points de restauration système", restorePts == 0 ? "aucun (protection à activer)" : restorePts.ToString(), restorePts > 0));
            if (freeGB >= 0)
                rows.Append(DiagRow("Espace disque système libre", freeGB.ToString("0") + " Go (" + pct.ToString("0") + " %)", pct >= 8 && freeGB >= 15));

            return "<h2>Diagnostic santé</h2><table>" + rows + "</table>";
        }

        private static string DiagRow(string label, string value, bool ok)
        {
            return "<tr><td>" + Esc(label) + "</td><td><span class='b " + (ok ? "on'>" : "warn'>") + Esc(value) + "</span></td></tr>";
        }

        private static string Css()
        {
            return
"*{box-sizing:border-box}body{margin:0;background:#0f1216;color:#d5dbe1;font-family:Segoe UI,system-ui,Arial,sans-serif;line-height:1.5}" +
".wrap{max-width:960px;margin:0 auto;padding:28px 22px 60px}" +
"header{border-bottom:1px solid #262c34;padding-bottom:16px;margin-bottom:24px}" +
".brand{font-size:26px;font-weight:800;letter-spacing:-.02em;color:#fff}" +
".sub{color:#8a97a0;font-size:13px;margin-top:4px}" +
".cards{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:28px}" +
"@media(max-width:720px){.cards{grid-template-columns:repeat(2,1fr)}}" +
".card{background:#161b21;border:1px solid #262c34;border-radius:12px;padding:14px}" +
".card .k{font-size:17px;font-weight:700;color:#fff;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}" +
".card .l{color:#8a97a0;font-size:12px;margin-top:3px}" +
"h2{font-size:15px;color:#7fb0ff;margin:26px 0 10px;letter-spacing:.02em}" +
"table{width:100%;border-collapse:collapse;background:#161b21;border:1px solid #262c34;border-radius:10px;overflow:hidden}" +
"td{padding:9px 14px;border-top:1px solid #21272f;font-size:14px}" +
"tr:first-child td{border-top:0}td:last-child{text-align:right;width:110px}" +
".b{display:inline-block;padding:2px 10px;border-radius:20px;font-size:12px;font-weight:600}" +
".b.on{background:rgba(0,190,120,.16);color:#3fe0a1}.b.off{background:rgba(140,150,160,.14);color:#9aa5ad}" +
".b.na{background:rgba(140,150,160,.10);color:#6b757d}" +
".b.warn{background:rgba(220,90,60,.16);color:#f0906f}" +
".rb{font-size:11px;color:#e0a93c;margin-left:6px}" +
"footer{color:#6b757d;font-size:12px;margin-top:34px;border-top:1px solid #262c34;padding-top:16px}";
        }
    }
}
