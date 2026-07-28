using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// Panneau « Connexion 4G/5G » : pour les box mobiles (Bouygues/Orange/SFR/Free) et le
    /// partage de connexion. Mesure ce qui fait VRAIMENT mal en jeu sur ces liens — MTU rabotée
    /// par le transport mobile, latence sous charge (bufferbloat), CGNAT, IPv6 — et applique la
    /// seule correction Windows qui vaille (MTU mesurée), réversible en un clic. Le reste est
    /// expliqué honnêtement : placement de la box, Ethernet, IPv6, heures pleines.
    /// </summary>
    internal class MobileNetForm : Form
    {
        private readonly Action<string, int> _log;
        private Label _vIface, _vLink, _vGw, _vIdle, _vLoaded, _vUpLoaded, _vMtu, _vCgnat, _vIpv6, _verdict;
        private Button _measure, _applyMtu, _revertMtu;
        private RichTextBox _journal;   // comme les autres journaux de l'app : se thème (le TextBox restait blanc)
        private MobileNet.Report _rep = new MobileNet.Report();
        private volatile bool _busy;

        public MobileNetForm(Action<string, int> log)
        {
            _log = log ?? delegate { };
            Text = "ONYX — Connexion 4G/5G (box mobile)";
            ClientSize = new Size(660, 742);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            Theme.Apply(this);

            // Lecture LOCALE seulement à l'ouverture (interface + MTU actuelle) : les mesures
            // réseau attendent le clic — jamais de trafic à la construction (harnais compris).
            MobileNet.FillInterface(_rep);
            RefreshRows();
        }

        private void BuildUi()
        {
            var banner = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(12, 14, 13) };
            banner.Controls.Add(new Label
            {
                Text = "  Connexion 4G/5G — box mobile & partage de connexion", Dock = DockStyle.Fill,
                ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            int y = 76;
            _vIface = Row("Interface active", ref y);
            _vLink = Row("Câble : débit négocié (1 Gb/s attendu)", ref y);
            _vGw = Row("Ping vers la BOX (juge ton câble/LAN)", ref y);
            _vIdle = Row("Ping vers Internet (radio + opérateur)", ref y);
            _vLoaded = Row("Sous charge RÉCEPTION (bufferbloat)", ref y);
            _vUpLoaded = Row("Sous charge ENVOI (talon d'Achille 5G)", ref y);
            _vMtu = Row("MTU (actuelle → mesurée)", ref y);
            _vCgnat = Row("CGNAT (adresse partagée opérateur)", ref y);
            _vIpv6 = Row("IPv6 (le chemin sans CGNAT)", ref y);

            // Le verdict : UNE phrase qui dit OÙ naît le lag — c'est elle qu'on est venu chercher.
            _verdict = new Label
            {
                Location = new Point(20, y + 2), Size = new Size(620, 40), Text = "",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold), ForeColor = Theme.InkDimColor
            };
            Controls.Add(_verdict);
            y += 46;

            _measure = new Button
            {
                Text = "📏  Mesurer ce lien (~45 s)", Location = new Point(20, y + 6), Size = new Size(210, 34),
                FlatStyle = FlatStyle.Flat, BackColor = Theme.AccentColor, ForeColor = Color.FromArgb(16, 13, 9)
            };
            _measure.FlatAppearance.BorderSize = 0;
            _measure.Click += (s, e) => Measure();
            Controls.Add(_measure);

            _applyMtu = new Button
            {
                Text = "Optimiser la MTU", Location = new Point(240, y + 6), Size = new Size(200, 34),
                FlatStyle = FlatStyle.Flat, Enabled = false
            };
            _applyMtu.Click += (s, e) => ApplyMtu();
            Controls.Add(_applyMtu);

            _revertMtu = new Button
            {
                Text = "↩  Rétablir la MTU d'origine", Location = new Point(450, y + 6), Size = new Size(190, 34),
                FlatStyle = FlatStyle.Flat, Enabled = MobileNet.HasBackup
            };
            _revertMtu.Click += (s, e) => { if (MobileNet.RevertMtu(Journal)) { MobileNet.FillInterface(_rep); RefreshRows(); } };
            Controls.Add(_revertMtu);
            y += 50;

            _journal = new RichTextBox
            {
                Location = new Point(20, y), Size = new Size(620, 86), ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle,
                Text = "Prêt. « Mesurer ce lien » n'écrit RIEN : uniquement des pings et un court téléchargement de test."
            };
            Controls.Add(_journal);
            y += 96;

            Controls.Add(new Label
            {
                Location = new Point(20, y), Size = new Size(620, 112), ForeColor = Theme.InkDimColor,
                Text = "Ce que Windows ne peut PAS faire à ta place (mais qui change tout sur une box 4G/5G) :\n"
                     + "• Regarde le SIGNAL dans l'interface de la box : RSRP > −100 dBm et SINR > 10 dB, sinon déplace-la\n"
                     + "  (près d'une fenêtre, côté antenne — un site de couverture indique la direction) ;\n"
                     + "• Redémarre la box : elle raccroche parfois une cellule lointaine et n'en repart plus ;\n"
                     + "• Active l'IPv6 dans la box si la ligne ci-dessus dit « absente » ;\n"
                     + "• Les soirées 20 h-23 h chargent l'antenne : un ping qui double à ces heures vient de là, pas du PC."
            });
            y += 120;

            var wiring = LinkBtn("📷 Schéma de branchement", 20, y, () => Host(new BoxWiringForm()));
            wiring.Width = 190;
            var dns = LinkBtn("DNS rapide", 220, y, () => Host(new DnsForm(_log)));
            dns.Width = 110;
            var tcp = LinkBtn("Réglages TCP/IP", 340, y, () => Host(new NetTuneForm(_log)));
            tcp.Width = 130;
            var net = LinkBtn("Qualité réseau", 480, y, () => Host(new NetworkForm(_log)));
            net.Width = 120;
            var close = new Button { Text = "Fermer", Location = new Point(20, y + 38), Size = new Size(100, 30), FlatStyle = FlatStyle.Flat };
            close.Click += (s, e) => Close();
            Controls.Add(close);
        }

        private Label Row(string caption, ref int y)
        {
            Controls.Add(new Label { Text = caption, Location = new Point(20, y), Size = new Size(280, 22), ForeColor = Theme.InkDimColor });
            var val = new Label
            {
                Text = "—", Location = new Point(304, y - 1), Size = new Size(336, 22),
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold), ForeColor = Theme.InkColor
            };
            Controls.Add(val);
            y += 30;
            return val;
        }

        private Button LinkBtn(string label, int x, int y, Action open)
        {
            var b = new Button { Text = label, Location = new Point(x, y), Size = new Size(140, 30), FlatStyle = FlatStyle.Flat };
            b.Click += (s, e) => { try { open(); } catch { } };
            Controls.Add(b);
            return b;
        }

        private void Host(Form f)
        {
            try { AnimFx.HookDialog(f); using (f) f.ShowDialog(this); } catch { }
        }

        private void Journal(string m, int lvl)
        {
            try { _log(m, lvl); } catch { }
            if (string.IsNullOrEmpty(m)) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    _journal.AppendText((_journal.TextLength > 0 ? "\r\n" : "") + m);
                }));
            }
            catch { }
        }

        // ------------------------------------------------------------------
        //  La mesure complète (en tâche de fond, fenêtre vivante)
        // ------------------------------------------------------------------
        private void Measure()
        {
            if (_busy) return;
            _busy = true;
            _measure.Enabled = false; _measure.Text = "mesure en cours…";
            _journal.Clear();
            Journal("Mesure du lien en cours (rien n'est modifié)…", 0);

            Task.Run(() =>
            {
                var r = new MobileNet.Report();
                MobileNet.FillInterface(r);

                // 1) La BOX d'abord : si ce ping-là est déjà haut, inutile d'accuser la 5G.
                var gw = MobileNet.GatewayAddress();
                double avg, jit; int loss;
                if (gw != null && MobileNet.SampleTo(gw.ToString(), 6, 600, out avg, out jit, out loss))
                { r.GwPing = avg; r.GwJitter = jit; r.GwLoss = loss; }
                Journal(r.GwPing >= 0
                    ? "Vers la box (" + gw + ") : " + r.GwPing.ToString("0") + " ms · gigue " + r.GwJitter.ToString("0") + " ms"
                    : "Vers la box : pas de réponse (certaines box ignorent le ping).", 0);

                if (ChatActions.PingSample(6, 900, out avg, out jit, out loss))
                { r.PingIdle = avg; r.JitterIdle = jit; r.LossIdle = loss; }
                Journal(r.PingIdle >= 0
                    ? "Vers Internet : " + r.PingIdle.ToString("0") + " ms · gigue " + r.JitterIdle.ToString("0") + " ms · perte " + r.LossIdle + " %"
                    : "Vers Internet : hors-ligne ?", 0);

                r.MtuOptimal = MobileNet.DiscoverMtu(Journal);
                MobileNet.DetectCgnat(r);
                Journal(r.Cgnat ? "CGNAT détecté (100.64.0.0/10) : adresse partagée opérateur."
                      : r.CgnatProbable ? "CGNAT probable (adressage privé après la box)."
                      : "Pas de CGNAT visible sur les premiers sauts.", r.Cgnat || r.CgnatProbable ? 2 : 0);
                r.Ipv6 = MobileNet.CheckIpv6();
                Journal(r.Ipv6 ? "IPv6 opérationnelle — le chemin direct est disponible."
                               : "IPv6 absente — à activer dans la box si possible.", r.Ipv6 ? 1 : 2);

                Journal("Latence sous charge RÉCEPTION (téléchargement de test ~8 s)…", 0);
                r.PingLoaded = MobileNet.LoadedPing(Journal);
                if (r.PingLoaded >= 0 && r.PingIdle >= 0)
                    Journal("Réception chargée : " + r.PingLoaded.ToString("0") + " ms (+"
                          + Math.Max(0, r.PingLoaded - r.PingIdle).ToString("0") + " ms).",
                          r.PingLoaded - r.PingIdle > 80 ? 2 : 0);

                Journal("Latence sous charge ENVOI (téléversement de test ~8 s)…", 0);
                r.PingUpLoaded = MobileNet.UploadLoadedPing(Journal);
                if (r.PingUpLoaded >= 0 && r.PingIdle >= 0)
                    Journal("Envoi chargé : " + r.PingUpLoaded.ToString("0") + " ms (+"
                          + Math.Max(0, r.PingUpLoaded - r.PingIdle).ToString("0") + " ms).",
                          r.PingUpLoaded - r.PingIdle > 80 ? 2 : 0);

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _rep = r;
                        RefreshRows();
                        _measure.Enabled = true; _measure.Text = "📏  Mesurer ce lien (~30 s)";
                        _busy = false;
                        Journal("Mesure terminée.", 0);
                    }));
                }
                catch { _busy = false; }
            });
        }

        private void ApplyMtu()
        {
            if (_rep == null || _rep.MtuOptimal < 996 || _rep.MtuOptimal >= _rep.MtuCurrent) return;
            if (MessageBox.Show(this,
                    "Appliquer la MTU mesurée (" + _rep.MtuOptimal + " au lieu de " + _rep.MtuCurrent + ") sur « " + _rep.IfName + " » ?\n\n"
                    + "• Effet immédiat, persistant au redémarrage.\n"
                    + "• La valeur d'origine est sauvegardée : « Rétablir » la remet en un clic.\n"
                    + "• Utile UNIQUEMENT sur les liens 4G/5G : sur une fibre/ADSL classique la mesure dira 1500 et ce bouton restera gris.",
                    "Connexion 4G/5G", MessageBoxButtons.OKCancel, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            if (MobileNet.ApplyMtu(_rep, Journal))
            {
                MobileNet.FillInterface(_rep);
                RefreshRows();
            }
        }

        // ------------------------------------------------------------------
        //  Rendu des verdicts (sémantique ONYX : émeraude / orange / rouge)
        // ------------------------------------------------------------------
        private void RefreshRows()
        {
            Color okC = Theme.OkColor, warnC = Color.FromArgb(220, 140, 50), errC = Color.FromArgb(210, 80, 70);

            _vIface.Text = _rep.IfName != null ? _rep.IfName + "  (MTU " + _rep.MtuCurrent + ")" : "aucune connexion détectée";
            _vIface.ForeColor = _rep.IfName != null ? Theme.InkColor : errC;

            // Débit négocié : la santé PHYSIQUE du câble, lue sans rien envoyer.
            if (_rep.IsWireless) { _vLink.Text = "Wi-Fi — branche un câble pour juger le lien"; _vLink.ForeColor = warnC; }
            else if (_rep.LinkMbps >= 2000) { _vLink.Text = (_rep.LinkMbps / 1000.0).ToString("0.#") + " Gb/s ✓"; _vLink.ForeColor = okC; }
            else if (_rep.LinkMbps >= 1000) { _vLink.Text = "1 Gb/s ✓"; _vLink.ForeColor = okC; }
            else if (_rep.LinkMbps == 100) { _vLink.Text = "100 Mb/s — câble ou port en cause !"; _vLink.ForeColor = errC; }
            else if (_rep.LinkMbps > 0) { _vLink.Text = _rep.LinkMbps + " Mb/s"; _vLink.ForeColor = warnC; }
            else { _vLink.Text = "—"; _vLink.ForeColor = Theme.InkDimColor; }

            if (_rep.GwPing >= 0)
            {
                _vGw.Text = _rep.GwPing.ToString("0") + " ms · gigue " + _rep.GwJitter.ToString("0") + " ms";
                _vGw.ForeColor = _rep.GwPing < 3 ? okC : _rep.GwPing < 10 ? warnC : errC;
            }
            else { _vGw.Text = "— (mesure à lancer)"; _vGw.ForeColor = Theme.InkDimColor; }

            if (_rep.PingIdle >= 0)
            {
                _vIdle.Text = _rep.PingIdle.ToString("0") + " ms · " + _rep.JitterIdle.ToString("0") + " ms · " + _rep.LossIdle + " %";
                _vIdle.ForeColor = _rep.PingIdle < 40 && _rep.LossIdle == 0 ? okC : _rep.PingIdle < 80 ? warnC : errC;
            }
            else { _vIdle.Text = "— (mesure à lancer)"; _vIdle.ForeColor = Theme.InkDimColor; }

            if (_rep.PingLoaded >= 0 && _rep.PingIdle >= 0)
            {
                double bloat = Math.Max(0, _rep.PingLoaded - _rep.PingIdle);
                _vLoaded.Text = _rep.PingLoaded.ToString("0") + " ms  (+" + bloat.ToString("0") + " ms)";
                _vLoaded.ForeColor = bloat < 30 ? okC : bloat < 80 ? warnC : errC;
            }
            else { _vLoaded.Text = "—"; _vLoaded.ForeColor = Theme.InkDimColor; }

            if (_rep.PingUpLoaded >= 0 && _rep.PingIdle >= 0)
            {
                double bloat = Math.Max(0, _rep.PingUpLoaded - _rep.PingIdle);
                _vUpLoaded.Text = _rep.PingUpLoaded.ToString("0") + " ms  (+" + bloat.ToString("0") + " ms)";
                _vUpLoaded.ForeColor = bloat < 30 ? okC : bloat < 80 ? warnC : errC;
            }
            else { _vUpLoaded.Text = "—"; _vUpLoaded.ForeColor = Theme.InkDimColor; }

            if (_rep.MtuOptimal > 0)
            {
                bool tight = _rep.MtuOptimal < _rep.MtuCurrent;
                _vMtu.Text = _rep.MtuCurrent + " → " + _rep.MtuOptimal + (tight ? "  (lien mobile : à raboter)" : "  (déjà optimale)");
                _vMtu.ForeColor = tight ? warnC : okC;
            }
            else { _vMtu.Text = _rep.MtuCurrent > 0 ? _rep.MtuCurrent + " (mesure à lancer)" : "—"; _vMtu.ForeColor = Theme.InkDimColor; }

            // Pas de faux verdict : tant que la mesure n'a pas tourné, ces lignes restent muettes.
            bool measured = _rep.PingIdle >= 0 || _rep.MtuOptimal > 0;
            if (measured)
            {
                _vCgnat.Text = _rep.Cgnat ? "détecté — NAT strict (voir IPv6)"
                            : _rep.CgnatProbable ? "probable (adressage privé après la box)"
                            : "non détecté";
                _vCgnat.ForeColor = _rep.Cgnat ? errC : _rep.CgnatProbable ? warnC : okC;
                _vIpv6.Text = _rep.Ipv6 ? "opérationnelle" : "absente (à activer dans la box)";
                _vIpv6.ForeColor = _rep.Ipv6 ? okC : warnC;
            }
            else
            {
                _vCgnat.Text = "— (mesure à lancer)"; _vCgnat.ForeColor = Theme.InkDimColor;
                _vIpv6.Text = "— (mesure à lancer)"; _vIpv6.ForeColor = Theme.InkDimColor;
            }

            _applyMtu.Enabled = _rep.MtuOptimal >= 996 && _rep.MtuOptimal < _rep.MtuCurrent;
            _applyMtu.Text = _applyMtu.Enabled ? "Optimiser la MTU (→ " + _rep.MtuOptimal + ")" : "Optimiser la MTU";
            _revertMtu.Enabled = MobileNet.HasBackup;

            RefreshVerdict(okC, warnC, errC);
        }

        /// <summary>La phrase qui LOCALISE le lag — box/LAN, radio-opérateur ou bufferbloat.</summary>
        private void RefreshVerdict(Color okC, Color warnC, Color errC)
        {
            // Un câble qui négocie à 100 Mb/s se voit AVANT toute mesure : paire coupée dans le
            // Cat 5e (sertissage) ou port fatigué — aucun logiciel ne rattrape du cuivre cassé.
            if (!_rep.IsWireless && _rep.LinkMbps == 100)
            {
                _verdict.ForeColor = errC;
                _verdict.Text = "→ Le câble négocie à 100 Mb/s au lieu de 1 Gb/s : une paire du Cat 5e est morte (connecteur) "
                              + "ou le port est en cause. Change de câble/port — voir le schéma de branchement (bouton en bas).";
                return;
            }
            if (_rep.PingIdle < 0 && _rep.GwPing < 0) { _verdict.Text = ""; return; }

            if (_rep.GwPing >= 10)
            {
                _verdict.ForeColor = errC;
                _verdict.Text = "→ Le problème commence AVANT la box : un lien local sain répond en ~1 ms. "
                              + "Vérifie le câble (croqué/mal clipsé), le port de la box, la carte réseau (panneau Carte réseau).";
                return;
            }
            double dBloat = _rep.PingLoaded >= 0 && _rep.PingIdle >= 0 ? Math.Max(0, _rep.PingLoaded - _rep.PingIdle) : 0;
            double uBloat = _rep.PingUpLoaded >= 0 && _rep.PingIdle >= 0 ? Math.Max(0, _rep.PingUpLoaded - _rep.PingIdle) : 0;
            if (dBloat > 80 || uBloat > 80)
            {
                _verdict.ForeColor = warnC;
                _verdict.Text = "→ Bufferbloat " + (uBloat >= dBloat ? "en ENVOI" : "en réception") + " : dès que quelque chose "
                              + (uBloat >= dBloat ? "téléverse (cloud, sauvegarde, stream)" : "télécharge") + ", la box stocke et le ping explose. "
                              + "Pendant le jeu : couper les synchronisations, et limiter le débit dans les applis qui envoient.";
                return;
            }
            if (_rep.PingIdle >= 60)
            {
                _verdict.ForeColor = warnC;
                _verdict.Text = "→ Ton câble est hors de cause (box en " + (_rep.GwPing >= 0 ? _rep.GwPing.ToString("0") : "~1")
                              + " ms) : le lag naît sur le segment RADIO/opérateur. Voir le signal dans l'interface de la box "
                              + "(RSRP/SINR), sa position, et les conseils ci-dessous.";
                return;
            }
            if (_rep.PingIdle >= 0)
            {
                _verdict.ForeColor = okC;
                _verdict.Text = "→ Lien sain au moment de la mesure. Si ça se dégrade en soirée, c'est l'antenne qui sature "
                              + "(refais la mesure à ce moment-là pour le prouver).";
            }
        }
    }
}
