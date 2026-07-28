using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// « Assistant opérateur » : tout ce qui reste quand le PC est hors de cause. Il identifie
    /// l'opérateur par le trajet réseau, tient un JOURNAL de mesures horodatées (la preuve d'une
    /// saturation aux heures de pointe), exporte un DOSSIER à remettre au support, et liste les
    /// réglages spécifiques à chaque opérateur (Box 5G Bouygues comprise).
    ///
    /// Honnêteté assumée : une application ne répare pas le réseau d'un opérateur. Elle règle ce
    /// qui est réglable, et elle PROUVE le reste pour que le dossier ne soit pas renvoyé au client.
    /// </summary>
    internal class OperatorHelpForm : Form
    {
        private readonly Action<string, int> _log;
        private Label _vOp, _vKind, _vLast;
        private Button _measure, _export, _openLog;
        private RichTextBox _tips;
        private ComboBox _pick;
        private MobileNet.Report _rep = new MobileNet.Report();
        private string _op, _trace;
        private volatile bool _busy;

        public OperatorHelpForm(Action<string, int> log)
        {
            _log = log ?? delegate { };
            Text = "ONYX — Assistant opérateur (dossier & réglages box)";
            ClientSize = new Size(700, 690);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            Theme.Apply(this);
            MobileNet.FillInterface(_rep);   // lecture locale seule : aucun trafic à l'ouverture
            RefreshRows();
        }

        private void BuildUi()
        {
            var banner = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(12, 14, 13) };
            banner.Controls.Add(new Label
            {
                Text = "  Assistant opérateur — prouver, puis obtenir gain de cause", Dock = DockStyle.Fill,
                ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 13f), TextAlign = ContentAlignment.MiddleLeft
            });
            Controls.Add(banner);

            int y = 76;
            _vOp = Row("Opérateur détecté (trajet réseau)", ref y);
            _vKind = Row("Type de lien estimé", ref y);
            _vLast = Row("Dernière mesure du journal", ref y);

            Controls.Add(new Label
            {
                Location = new Point(20, y + 2), Size = new Size(660, 34), ForeColor = Theme.InkDimColor,
                Text = "Une app ne répare pas le réseau d'un opérateur. Elle règle ce qui est réglable chez toi, "
                     + "puis elle PROUVE le reste — mesures datées à l'appui, pour que le support ne renvoie plus la faute sur ton PC."
            });
            y += 44;

            _measure = new Button
            {
                Text = "📏  Mesurer et ajouter au journal (~45 s)", Location = new Point(20, y), Size = new Size(280, 34),
                FlatStyle = FlatStyle.Flat, BackColor = Theme.AccentColor, ForeColor = Color.FromArgb(16, 13, 9)
            };
            _measure.FlatAppearance.BorderSize = 0;
            _measure.Click += (s, e) => Measure();
            Controls.Add(_measure);

            _export = new Button
            {
                Text = "📄  Exporter le dossier (Bureau)", Location = new Point(310, y), Size = new Size(220, 34),
                FlatStyle = FlatStyle.Flat
            };
            _export.Click += (s, e) => Export();
            Controls.Add(_export);

            _openLog = new Button
            {
                Text = "Voir le journal", Location = new Point(540, y), Size = new Size(140, 34), FlatStyle = FlatStyle.Flat
            };
            _openLog.Click += (s, e) => ShowLog();
            Controls.Add(_openLog);
            y += 46;

            Controls.Add(new Label { Text = "Réglages et démarches selon l'opérateur :", Location = new Point(20, y + 4), Size = new Size(280, 22), ForeColor = Theme.InkDimColor });
            _pick = new ComboBox { Location = new Point(304, y), Size = new Size(376, 24), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat };
            _pick.Items.AddRange(new object[] { "Bouygues Telecom — Box 5G", "Orange / Sosh", "SFR / RED", "Free", "Autre opérateur" });
            _pick.SelectedIndex = 0;
            _pick.SelectedIndexChanged += (s, e) => ShowTips();
            Controls.Add(_pick);
            y += 34;

            _tips = new RichTextBox
            {
                Location = new Point(20, y), Size = new Size(660, 278), ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle, ScrollBars = RichTextBoxScrollBars.Vertical
            };
            Controls.Add(_tips);
            ShowTips();

            // UseMnemonic=false : sans ça, WinForms lit le « & » comme un raccourci et souligne la suite.
            var mine = new Button { Text = "Ma connexion & ma box", Location = new Point(20, 640), Size = new Size(190, 32), FlatStyle = FlatStyle.Flat, UseMnemonic = false };
            mine.Click += (s, e) => { try { var f = new MobileNetForm(_log); AnimFx.HookDialog(f); using (f) f.ShowDialog(this); } catch { } };
            Controls.Add(mine);

            var dns = new Button { Text = "DNS rapide", Location = new Point(220, 640), Size = new Size(130, 32), FlatStyle = FlatStyle.Flat };
            dns.Click += (s, e) => { try { var f = new DnsForm(_log); AnimFx.HookDialog(f); using (f) f.ShowDialog(this); } catch { } };
            Controls.Add(dns);

            var close = new Button { Text = "Fermer", Location = new Point(580, 640), Size = new Size(100, 32), FlatStyle = FlatStyle.Flat };
            close.Click += (s, e) => Close();
            Controls.Add(close);
        }

        private Label Row(string caption, ref int y)
        {
            Controls.Add(new Label { Text = caption, Location = new Point(20, y), Size = new Size(260, 22), ForeColor = Theme.InkDimColor });
            var val = new Label
            {
                Text = "—", Location = new Point(284, y - 1), Size = new Size(396, 22),
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold), ForeColor = Theme.InkColor
            };
            Controls.Add(val);
            y += 30;
            return val;
        }

        private void RefreshRows()
        {
            _vOp.Text = _op ?? "— (lance une mesure)";
            _vOp.ForeColor = _op != null ? Theme.InkColor : Theme.InkDimColor;
            _vKind.Text = _rep.Kind != MobileNet.Access.Unknown ? MobileNet.AccessLabel(_rep.Kind) : "— (lance une mesure)";
            _vKind.ForeColor = _rep.Kind != MobileNet.Access.Unknown ? Theme.InkColor : Theme.InkDimColor;
            string[] last = MobileNet.ReadLog(1);
            _vLast.Text = last.Length > 0 ? last[0] : "journal vide";
            _vLast.ForeColor = last.Length > 0 ? Theme.InkColor : Theme.InkDimColor;
            _export.Enabled = true;
        }

        // ------------------------------------------------------------------
        //  Mesure complète + journalisation
        // ------------------------------------------------------------------
        private void Measure()
        {
            if (_busy) return;
            _busy = true;
            _measure.Enabled = false; _measure.Text = "mesure en cours…";
            Task.Run(() =>
            {
                var r = new MobileNet.Report();
                MobileNet.FillInterface(r);
                double avg, jit; int loss;
                var gw = MobileNet.GatewayAddress();
                if (gw != null && MobileNet.SampleTo(gw.ToString(), 6, 600, out avg, out jit, out loss))
                { r.GwPing = avg; r.GwJitter = jit; r.GwLoss = loss; }
                if (ChatActions.PingSample(6, 900, out avg, out jit, out loss))
                { r.PingIdle = avg; r.JitterIdle = jit; r.LossIdle = loss; }
                r.MtuOptimal = MobileNet.DiscoverMtu(_log);
                MobileNet.DetectCgnat(r);
                r.Ipv6 = MobileNet.CheckIpv6();
                r.PingLoaded = MobileNet.LoadedPing(_log);
                r.PingUpLoaded = MobileNet.UploadLoadedPing(_log);
                r.Kind = MobileNet.Classify(r);

                string trace;
                string op = MobileNet.DetectOperator(out trace);
                MobileNet.AppendLog(r);

                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _rep = r; _op = op; _trace = trace;
                        if (op != null) SelectOperator(op);
                        RefreshRows();
                        _measure.Enabled = true; _measure.Text = "📏  Mesurer et ajouter au journal (~45 s)";
                        _busy = false;
                        MessageBox.Show(this,
                            "Mesure enregistrée dans le journal.\n\n"
                            + "Refais-en une en soirée (20 h-23 h) : c'est l'écart entre les deux qui prouve une saturation "
                            + "du réseau, et c'est exactement ce que le support ne peut pas contester.",
                            "Assistant opérateur", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch { _busy = false; }
            });
        }

        private void SelectOperator(string op)
        {
            for (int i = 0; i < _pick.Items.Count; i++)
            {
                string item = _pick.Items[i].ToString();
                if (op.StartsWith("Bouygues") && item.StartsWith("Bouygues")) { _pick.SelectedIndex = i; return; }
                if (op == "Orange" && item.StartsWith("Orange")) { _pick.SelectedIndex = i; return; }
                if (op == "SFR" && item.StartsWith("SFR")) { _pick.SelectedIndex = i; return; }
                if (op == "Free" && item.StartsWith("Free")) { _pick.SelectedIndex = i; return; }
            }
        }

        private void Export()
        {
            string path = MobileNet.BuildOperatorReport(_rep, _op, _trace);
            if (path == null)
            {
                MessageBox.Show(this, "Impossible d'écrire le dossier sur le Bureau.", "Assistant opérateur",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { MessageBox.Show(this, "Dossier écrit :\n" + path, "Assistant opérateur", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }

        private void ShowLog()
        {
            string[] lines = MobileNet.ReadLog(30);
            MessageBox.Show(this,
                lines.Length == 0 ? "Le journal est vide : lance une première mesure."
                                  : string.Join(Environment.NewLine, lines),
                "Journal des mesures (les 30 dernières)", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ------------------------------------------------------------------
        //  Les démarches, par opérateur
        // ------------------------------------------------------------------
        private void ShowTips()
        {
            string k = _pick.SelectedItem != null ? _pick.SelectedItem.ToString() : "";
            _tips.Text = k.StartsWith("Bouygues") ? Bouygues()
                       : k.StartsWith("Orange") ? Orange()
                       : k.StartsWith("SFR") ? Sfr()
                       : k.StartsWith("Free") ? Free()
                       : Generic();
        }

        private static string Bouygues()
        {
            return "BOX 5G BOUYGUES TELECOM — ce qui se règle, et ce qui se réclame\n"
                 + "\n"
                 + "DANS L'INTERFACE DE LA BOX (http://192.168.1.1, identifiants sous la box)\n"
                 + "  • PC sur le port ROUGE « LAN/WAN » (2,5 Gb/s) tant qu'aucune fibre n'y est branchée.\n"
                 + "  • IPv6 : ACTIVÉE. Sur Box 5G l'IPv4 est PARTAGÉE (CGNAT) : sans IPv6, le NAT reste strict\n"
                 + "    en jeu et aucune redirection de port ne fonctionnera jamais — ce n'est pas un réglage PC.\n"
                 + "  • UPnP : ACTIVÉ (le NAT local suit ; le partage d'IPv4 côté opérateur, lui, ne bougera pas).\n"
                 + "  • Mode réseau : si l'interface propose « 5G / 4G+5G / auto », teste les deux : la 5G n78\n"
                 + "    (3,5 GHz) donne le débit, la 4G/700 MHz porte plus loin et peut être PLUS STABLE en intérieur.\n"
                 + "  • Wi-Fi coupé si tout le monde est en câble ; QoS/priorisation sur le PC de jeu si proposée.\n"
                 + "\n"
                 + "PLACEMENT (c'est souvent LE gain le plus fort)\n"
                 + "  • Relève RSRP et SINR dans l'interface. Objectif : RSRP > −100 dBm, SINR > 10 dB.\n"
                 + "  • Chaque mur, chaque étage et chaque vitrage traité coûtent du signal : approche la box\n"
                 + "    d'une fenêtre orientée vers l'antenne (repère l'antenne sur monreseaumobile.arcep.fr).\n"
                 + "  • Déplace de 1-2 m et RE-MESURE : le panneau « Ma connexion » chiffre le gain, sans deviner.\n"
                 + "  • Signal faible malgré tout : demande une antenne extérieure ou un déport (support 1064).\n"
                 + "\n"
                 + "GESTES QUI DÉBLOQUENT\n"
                 + "  • Redémarrage complet (30 s hors tension) : la box se raccroche parfois à une cellule\n"
                 + "    lointaine et n'en repart pas toute seule. À refaire dès que le ping s'envole sans raison.\n"
                 + "  • Compare toujours une mesure de journée et une de 20 h-23 h avant d'appeler.\n"
                 + "\n"
                 + "AUPRÈS DU SUPPORT (1064, appli Bouygues Telecom, boutique)\n"
                 + "  • Exporte le dossier (bouton ci-dessus) et transmets-le : mesures datées, réseau local déjà\n"
                 + "    écarté, historique jour/soir. C'est ce qui empêche le « c'est votre ordinateur ».\n"
                 + "  • Demande explicitement : saturation connue de la cellule de ton secteur ? intervention en\n"
                 + "    cours ? IPv6 bien activée sur la ligne ? débit/latence conformes à l'offre ? échange de box\n"
                 + "    ou antenne externe possibles ?\n"
                 + "  • Sans solution après relances : le service client écrit, puis le médiateur des communications\n"
                 + "    électroniques (gratuit) ; la 5G fixe n'a pas d'engagement de latence, l'argumentaire porte\n"
                 + "    sur l'inutilisabilité constatée, preuves à l'appui.\n"
                 + "\n"
                 + "À SAVOIR (honnêtement)\n"
                 + "  • Une box 5G partage une antenne avec tout le quartier : à 20 h, le ping monte pour tout le\n"
                 + "    monde. Aucun réglage PC ne compense une cellule saturée — seul l'opérateur peut agir.\n"
                 + "  • Si la fibre est éligible à l'adresse, c'est la seule vraie solution à un ping instable.";
        }

        private static string Orange()
        {
            return "ORANGE / SOSH\n"
                 + "  • Livebox : IPv6 activée, UPnP activé, PC sur le port le plus rapide (2,5 G sur Livebox 6/7).\n"
                 + "  • Wi-Fi coupé si tout est câblé ; décodeur TV sur son port dédié.\n"
                 + "  • Espace client / appli Orange : consulte les incidents connus sur ta zone avant d'appeler.\n"
                 + "  • Support 3900 : transmets le dossier exporté (mesures datées, réseau local écarté).\n"
                 + "  • Demande : incident ou saturation sur le NRO/cellule, conformité du débit et de la latence\n"
                 + "    à l'offre, remplacement de box, intervention technicien si la ligne décroche.";
        }

        private static string Sfr()
        {
            return "SFR / RED\n"
                 + "  • Box : IPv6 activée, UPnP activé, PC sur le port le plus rapide disponible.\n"
                 + "  • SFR partage aussi l'IPv4 sur certaines offres : sans IPv6, NAT strict en jeu.\n"
                 + "  • Support 1023 (SFR) / assistance en ligne (RED) : joins le dossier exporté.\n"
                 + "  • Demande : incident/saturation sur ta zone, conformité débit-latence, échange de box.";
        }

        private static string Free()
        {
            return "FREE\n"
                 + "  • Freebox OS (mafreebox.free.fr) : IPv6 activée, UPnP activé, PC sur le port le plus rapide\n"
                 + "    (2,5 G / 10 G selon le modèle).\n"
                 + "  • Free partage l'IPv4 sur certaines lignes : la « full-stack IPv4 » se demande dans Freebox OS\n"
                 + "    quand elle est disponible — sinon l'IPv6 reste la voie propre pour le jeu.\n"
                 + "  • Support 3244 / assistance Freebox : joins le dossier exporté.\n"
                 + "  • Demande : incident/saturation, conformité débit-latence, échange de Freebox.";
        }

        private static string Generic()
        {
            return "AUTRE OPÉRATEUR — la marche à suivre est la même\n"
                 + "  • Box : IPv6 activée, UPnP activé, PC sur le port le plus rapide, Wi-Fi coupé si tout est câblé.\n"
                 + "  • Mesure en journée ET en soirée : l'écart prouve une saturation côté réseau.\n"
                 + "  • Exporte le dossier et transmets-le au support : mesures datées, réseau local déjà écarté.\n"
                 + "  • Demande : incident ou saturation connue, conformité du débit et de la latence à l'offre,\n"
                 + "    échange de box, intervention si la ligne décroche.\n"
                 + "  • Sans réponse satisfaisante : réclamation écrite, puis médiateur des communications\n"
                 + "    électroniques (gratuit).";
        }
    }
}
