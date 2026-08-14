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
        private static readonly Color Accent = Theme.AccentColor;

        private Button _btnRun, _btnClose;
        private double _mbps = double.NaN, _latency = double.NaN, _jitter = double.NaN;
        private double _chargeMs = double.NaN;   // ping mesure PENDANT le telechargement
        private double _fenetreS = 0;            // duree pendant laquelle des octets ont reellement circule
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
            Text = "ONYX — Test de débit réseau";
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
            _mbps = _latency = _jitter = _chargeMs = double.NaN;
            _status = "Mesure de la latence…";
            _canvas.Invalidate();
            Task.Run(() =>
            {
                double lat, jit;
                MeasureLatency(out lat, out jit);
                Ui(() => { _latency = lat; _jitter = jit; _status = "Téléchargement de test en cours…"; _canvas.Invalidate(); });

                // BUFFERBLOAT : on mesure le ping PENDANT le téléchargement, pas seulement avant.
                // Le transfert a lieu de toute façon — la mesure ne coûte donc pas un octet de plus,
                // et c'est le seul chiffre qui explique « mon ping explose quand ça télécharge ».
                double sousCharge = double.NaN;
                var sonde = new System.Threading.Thread(() => { sousCharge = PingMoyenPendant(11000); });
                sonde.IsBackground = true;
                sonde.Start();

                double mbps = -1, fenetre = 0;
                try { mbps = DownloadMbps(out fenetre); } catch (Exception ex) { if (_log != null) _log("Speed test : " + ex.Message, 2); }
                try { sonde.Join(3000); } catch { }

                double charge = sousCharge;
                Ui(() =>
                {
                    _mbps = mbps;
                    _fenetreS = fenetre;
                    _chargeMs = mbps < 0 ? double.NaN : charge;   // sans transfert, la mesure ne veut rien dire
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

        /// <summary>Ping moyen pendant que le lien travaille. Rend NaN si aucune réponse — on
        /// préfère l'absence de chiffre à un chiffre inventé.</summary>
        private static double PingMoyenPendant(int dureeMs)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var samples = new System.Collections.Generic.List<long>();
                using (var p = new Ping())
                {
                    while (sw.ElapsedMilliseconds < dureeMs)
                    {
                        try
                        {
                            var r = p.Send("1.1.1.1", 2000);
                            if (r.Status == IPStatus.Success) samples.Add(r.RoundtripTime);
                        }
                        catch { }
                        System.Threading.Thread.Sleep(200);
                    }
                }
                if (samples.Count == 0) return double.NaN;
                double sum = 0; foreach (long s in samples) sum += s;
                return sum / samples.Count;
            }
            catch { return double.NaN; }
        }

        /// <summary>
        /// Débit descendant. Le chronomètre ne tourne QUE pendant que des octets circulent :
        /// voir DebitReseau pour ce que l'ancienne version comptait à tort.
        /// <paramref name="secondesMesurees"/> ressort pour que l'affichage puisse dire si la
        /// fenêtre a été assez longue pour que le chiffre veuille dire quelque chose.
        /// </summary>
        private static double DownloadMbps(out double secondesMesurees)
        {
            // On essaie la plus grande taille d'abord, puis on se rabat : un refus du serveur sur
            // un gros fichier ne doit pas se transformer en « pas de connexion ».
            Exception derniere = null;
            foreach (long taille in DebitReseau.TaillesAEssayer)
            {
                try { return UnEssai(DebitReseau.Adresse(taille), out secondesMesurees); }
                catch (Exception ex) { derniere = ex; }
            }
            secondesMesurees = 0;
            throw derniere ?? new Exception("aucune taille de test acceptée par le serveur");
        }

        private static double UnEssai(string url, out double secondesMesurees)
        {
            secondesMesurees = 0;
            using (var http = new HttpClient())
            {
                http.Timeout = TimeSpan.FromSeconds(25);
                using (var stream = http.GetStreamAsync(url).GetAwaiter().GetResult())
                {
                    var buf = new byte[65536];
                    long total = 0, octetsEchauffement = 0;
                    int n;
                    var depuisPremierOctet = new Stopwatch();
                    bool regimeAtteint = false;
                    double debutRegime = 0;

                    while ((n = stream.Read(buf, 0, buf.Length)) > 0)
                    {
                        // Le chronomètre part au PREMIER OCTET, pas avant la connexion.
                        if (!depuisPremierOctet.IsRunning) depuisPremierOctet.Start();
                        total += n;

                        // On écarte la montée en régime de TCP : les octets qui arrivent pendant
                        // qu'il accélère encore tireraient la moyenne vers le bas.
                        if (!regimeAtteint && depuisPremierOctet.Elapsed.TotalSeconds >= DebitReseau.EchauffementSecondes)
                        {
                            regimeAtteint = true;
                            octetsEchauffement = total;
                            debutRegime = depuisPremierOctet.Elapsed.TotalSeconds;
                        }
                        if (depuisPremierOctet.Elapsed.TotalSeconds > 12) break;
                    }
                    depuisPremierOctet.Stop();

                    double fin = depuisPremierOctet.Elapsed.TotalSeconds;
                    // Si le transfert s'est terminé avant la fin de l'échauffement, il n'y a pas de
                    // régime établi à isoler : on mesure tout, et la réserve dira que c'est court.
                    long octets = regimeAtteint ? total - octetsEchauffement : total;
                    double secondes = regimeAtteint ? fin - debutRegime : fin;

                    secondesMesurees = secondes;
                    return DebitReseau.Mbps(octets, secondes);
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

            // BUFFERBLOAT : la hausse de ping SOUS CHARGE. C'est ce chiffre, et lui seul, qui
            // explique « mon ping explose quand quelqu'un télécharge ».
            string note = Bufferbloat.Note(Bufferbloat.Hausse(_latency, _chargeMs));
            if (note.Length > 0)
            {
                Color c = note == "A+" || note == "A" ? Accent
                        : note == "B" ? Color.FromArgb(220, 170, 40)
                        : Color.FromArgb(210, 90, 70);
                TextRenderer.DrawText(g, "Sous charge : " + _chargeMs.ToString("0") + " ms  (note "
                    + note + ")", new Font("Segoe UI Semibold", 9f),
                    new Point(w / 2 + 20, 186), c, TextFormatFlags.NoPadding);
            }

            // Verdict / statut.
            string verdict = _status;
            if (verdict == null)
            {
                // Le bufferbloat passe DEVANT le reste quand il est mauvais : c'est la seule cause
                // qui rend une connexion par ailleurs excellente injouable dès qu'elle est chargée.
                string bb = Bufferbloat.Verdict(_latency, _chargeMs);
                if (bb != null && Bufferbloat.EstProblematique(note)) verdict = "⚠ " + bb;
                else
                {
                    bool goodLat = !double.IsNaN(_latency) && _latency <= 40;
                    bool goodBw = !double.IsNaN(_mbps) && _mbps >= 25;
                    verdict = goodLat && goodBw
                        ? "✔ Connexion prête pour le jeu en ligne : latence basse et débit confortable."
                        : !goodLat
                            ? "⚠ Latence élevée : c'est ELLE qui compte le plus en jeu. Câble Ethernet > Wi-Fi, et vérifie le Trajet réseau."
                            : "⚠ Débit modeste, mais en jeu la latence prime. Ça peut suffire si le ping est bas.";
                    if (bb != null) verdict += "  " + bb;
                    // Une connexion très rapide vide le fichier de test avant qu'on ait eu le temps
                    // de la mesurer. Le dire, plutôt que d'afficher un chiffre précis qui ne l'est pas.
                    if (_mbps > 0)
                    {
                        string reserve = DebitReseau.Reserve(_fenetreS);
                        if (reserve.Length > 0) verdict += "  " + reserve;
                    }
                }
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
