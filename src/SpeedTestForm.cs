using System;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Test de débit réseau : latence (ping) + débit descendant réel (téléchargement mesuré depuis
    /// un CDN Cloudflare). Utile pour savoir si la connexion tient la charge en jeu en ligne.
    /// Lecture seule : télécharge des données de test, n'envoie aucune donnée personnelle.
    /// </summary>
    internal class SpeedTestForm : Form
    {
        private readonly Action<string, int> _log;
        private static readonly Color Accent = Color.FromArgb(0, 150, 90);
        private const string DownUrl = "https://speed.cloudflare.com/__down?bytes=25000000"; // ~25 Mo

        private Button _btnRun, _btnClose;
        private double _mbps = double.NaN, _latency = double.NaN, _jitter = double.NaN;
        private string _status = "Prêt. Lance le test pour mesurer ta connexion.";
        private Panel _canvas;

        public SpeedTestForm(Action<string, int> log)
        {
            _log = log;
            Build();
            Theme.Apply(this);
        }

        private void Build()
        {
            Text = "Fluide — Test de débit réseau";
            ClientSize = new Size(560, 420);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var banner = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(12, 14, 13) };
            banner.Controls.Add(new Label
            {
                Text = "  Test de débit réseau — ta connexion tient-elle en jeu ?",
                Dock = DockStyle.Fill, ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 12.5f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            _canvas = new Panel { Location = new Point(0, 50), Size = new Size(560, 312), BackColor = Color.Transparent };
            _canvas.Paint += PaintResults;
            Controls.Add(_canvas);

            _btnRun = MakeBtn("Lancer le test", 18, 372, 200, 36, true);
            _btnRun.Click += (s, e) => Run();
            _btnClose = MakeBtn("Fermer", 452, 372, 90, 36, false);
            _btnClose.Click += (s, e) => Close();
            Controls.Add(_btnRun); Controls.Add(_btnClose);
        }

        private static Button MakeBtn(string text, int x, int y, int w, int h, bool primary)
        {
            var b = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(w, h), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Color.FromArgb(40, 44, 52),
                Font = primary ? new Font("Segoe UI Semibold", 9.5f) : new Font("Segoe UI", 9f)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 204, 210);
            return b;
        }

        private void Run()
        {
            _btnRun.Enabled = false;
            _mbps = _latency = _jitter = double.NaN;
            _status = "Mesure de la latence…";
            _canvas.Invalidate();
            Task.Run(() =>
            {
                double lat, jit;
                MeasureLatency(out lat, out jit);
                Ui(() => { _latency = lat; _jitter = jit; _status = "Téléchargement de test en cours…"; _canvas.Invalidate(); });
                double mbps = -1;
                try { mbps = DownloadMbps(); } catch (Exception ex) { if (_log != null) _log("Speed test : " + ex.Message, 2); }
                Ui(() =>
                {
                    _mbps = mbps;
                    _status = mbps < 0 ? "Échec du téléchargement (pas de connexion ?). Latence affichée si dispo." : null;
                    _btnRun.Enabled = true; _canvas.Invalidate();
                });
            });
        }

        private void Ui(Action a) { try { if (IsHandleCreated) BeginInvoke(a); } catch { } }

        private static void MeasureLatency(out double avg, out double jitter)
        {
            avg = jitter = double.NaN;
            try
            {
                using (var p = new Ping())
                {
                    var samples = new System.Collections.Generic.List<long>();
                    for (int i = 0; i < 5; i++)
                    {
                        try { var r = p.Send("1.1.1.1", 1000); if (r.Status == IPStatus.Success) samples.Add(r.RoundtripTime); }
                        catch { }
                    }
                    if (samples.Count == 0) return;
                    double sum = 0; foreach (long s in samples) sum += s; avg = sum / samples.Count;
                    double js = 0; foreach (long s in samples) js += Math.Abs(s - avg); jitter = js / samples.Count;
                }
            }
            catch { }
        }

        private static double DownloadMbps()
        {
            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(25);
                var sw = Stopwatch.StartNew();
                using (var stream = http.GetStreamAsync(DownUrl).GetAwaiter().GetResult())
                {
                    var buf = new byte[65536];
                    long total = 0; int n;
                    while ((n = stream.Read(buf, 0, buf.Length)) > 0)
                    {
                        total += n;
                        if (sw.Elapsed.TotalSeconds > 12) break;   // fenêtre de mesure suffisante
                    }
                    sw.Stop();
                    double sec = Math.Max(0.05, sw.Elapsed.TotalSeconds);
                    return total * 8.0 / 1_000_000.0 / sec;   // Mb/s
                }
            }
        }

        private void PaintResults(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = _canvas.Width;
            Color ink = Theme.InkColor, dim = Theme.InkDimColor;

            // Deux grands cadrans : DÉBIT et LATENCE.
            DrawMetric(g, 40, 24, "DÉBIT DESCENDANT",
                double.IsNaN(_mbps) ? "—" : (_mbps < 0 ? "échec" : _mbps.ToString("0.0")),
                double.IsNaN(_mbps) || _mbps < 0 ? "" : "Mb/s",
                double.IsNaN(_mbps) || _mbps < 0 ? dim : (_mbps >= 100 ? Accent : _mbps >= 25 ? Color.FromArgb(220, 170, 40) : Color.FromArgb(210, 90, 70)));

            DrawMetric(g, w / 2 + 20, 24, "LATENCE (PING)",
                double.IsNaN(_latency) ? "—" : _latency.ToString("0"),
                double.IsNaN(_latency) ? "" : "ms",
                double.IsNaN(_latency) ? dim : (_latency <= 20 ? Accent : _latency <= 60 ? Color.FromArgb(220, 170, 40) : Color.FromArgb(210, 90, 70)));

            if (!double.IsNaN(_jitter))
                TextRenderer.DrawText(g, "Gigue (jitter) : " + _jitter.ToString("0.0") + " ms", new Font("Segoe UI", 9f),
                    new Point(w / 2 + 20, 168), dim, TextFormatFlags.NoPadding);

            // Verdict / statut.
            string verdict = _status;
            if (verdict == null)
            {
                bool goodLat = !double.IsNaN(_latency) && _latency <= 40;
                bool goodBw = !double.IsNaN(_mbps) && _mbps >= 25;
                verdict = goodLat && goodBw
                    ? "✔ Connexion prête pour le jeu en ligne : latence basse et débit confortable."
                    : !goodLat
                        ? "⚠ Latence élevée : c'est ELLE qui compte le plus en jeu. Câble Ethernet > Wi-Fi, et vérifie le Trajet réseau."
                        : "⚠ Débit modeste, mais en jeu la latence prime. Ça peut suffire si le ping est bas.";
            }
            using (var f = new Font("Segoe UI", 9.5f))
                TextRenderer.DrawText(g, verdict, f, new Rectangle(40, 214, w - 80, 70), ink,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        private void DrawMetric(Graphics g, int x, int y, string label, string value, string unit, Color col)
        {
            TextRenderer.DrawText(g, label, new Font("Segoe UI Semibold", 9f), new Point(x, y), Theme.InkDimColor, TextFormatFlags.NoPadding);
            using (var vf = new Font("Segoe UI", 46f, FontStyle.Bold))
                TextRenderer.DrawText(g, value, vf, new Point(x - 4, y + 22), col, TextFormatFlags.NoPadding);
            if (unit.Length > 0)
                using (var uf = new Font("Segoe UI", 12f))
                    TextRenderer.DrawText(g, unit, uf, new Point(x, y + 108), Theme.InkColor, TextFormatFlags.NoPadding);
        }
    }
}
