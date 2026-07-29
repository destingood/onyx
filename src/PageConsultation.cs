using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace BTOptimizer
{
    // Page Consultation : CHAT avec « Le Copilote » — assistant LOCAL (aucun réseau). Comprend la demande,
    // s'appuie sur les vraies données du PC, et ouvre le bon outil. Incarnation ONYX : anneau d'or en
    // avatar, accueil scénarisé quand le chat est vide, réponses qui s'écrivent en direct (clic = tout
    // afficher), cockpit à compteurs animés, colonne de conversation centrée.
    internal class PageConsultation : FpsPage
    {
        private FlowLayoutPanel _flow;
        private TextBox _input;
        private Button _send;
        private ScrollWheelFilter _wheel;
        private BadgeCatalog.Stats _stats;
        private DocAssistant.Reply _last;   // dernière réponse ACTIONNABLE : contexte du prochain « oui »/« non »
        private bool _greeted;
        private bool _seeded;
        private Panel _statsRow;                          // cockpit : tuiles d'état en direct
        private Label _vHealth, _vScreen, _vPing, _vGpu;  // valeurs des tuiles
        private Button _refresh;                          // ↻ du cockpit
        private Panel _inputBar;                          // barre de saisie (carte arrondie, focus doré)
        private FlowLayoutPanel _quick;                   // raccourcis permanents au-dessus de la saisie
        private bool _proactive;                          // accueil proactif déjà tenté
        private Panel _typingBubble;                      // bulle « écrit… » (élargie quand le journal parle)
        private Label _typingDots;                        // points qui pulsent OU dernière ligne du journal
        private string _typingProgress;                   // null = animation de points ; sinon texte affiché
        private Panel _hero;                              // accueil scénarisé (chat vide) — retiré au 1er message
        private FlowLayoutPanel _heroChips;               // suggestions de départ, centrées dans le héros
        private Label _status;                            // état vivant du Copilote (prêt / écrit / N causes)
        private int _statusCauses;                        // dernières causes identifiées (pour l'état au repos)

        // Fontes d'incarnation : le nom du Copilote se grave en Marcellus.
        private static readonly Font HeroTitle = Fonts.Make(Fonts.Marcellus, 21f, FontStyle.Regular, "Georgia");
        private static readonly Font NameFont = Fonts.Make(Fonts.Marcellus, 8.5f, FontStyle.Regular, "Georgia");

        public PageConsultation(DashboardForm host) : base(host)
        {
            Build();
        }

        private void Build()
        {
            _flow = new BufferedFlow { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = FpsUi.BgMain };
            _flow.Padding = new Padding(20, 12, 20, 12);
            Controls.Add(_flow);
            _wheel = new ScrollWheelFilter(_flow);
            try { Application.AddMessageFilter(_wheel); } catch { }

            BuildHero();

            // --- Cockpit : 4 tuiles d'état EN DIRECT (santé / écrans / ping / GPU) ---
            _statsRow = new Panel { BackColor = Color.Transparent };
            Controls.Add(_statsRow);
            _vHealth = StatTile("SANTÉ");
            _vScreen = StatTile("ÉCRANS");
            _vPing = StatTile("PING");
            _vGpu = StatTile("GPU");
            _refresh = FpsUi.GhostButton("↻");
            _refresh.Click += (s, e) => RefreshTiles();
            Controls.Add(_refresh);

            // --- État vivant : le Copilote dit ce qu'il fait (prêt / analyse / bilan). ---
            _status = new Label
            {
                AutoSize = false, Font = FpsUi.Small, BackColor = Color.Transparent,
                ForeColor = FpsUi.Dim, TextAlign = ContentAlignment.TopRight, Text = ""
            };
            Controls.Add(_status);
            SetStatus(StatusKind.Ready);

            // --- Barre de saisie « pro » : carte arrondie dont le liseré s'allume au focus. ---
            _inputBar = new Panel { BackColor = Color.Transparent };
            _inputBar.Paint += (s, e) =>
            {
                bool focus = _input != null && _input.Focused;
                FpsUi.PaintCard(e.Graphics, _inputBar.ClientRectangle, FpsUi.Card,
                    focus ? FpsUi.Gold : FpsUi.Border, 12f);
            };
            Controls.Add(_inputBar);
            _input = new TextBox { BorderStyle = BorderStyle.None, BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, Font = FpsUi.Body };
            try { _input.PlaceholderText = "Décris ton souci… (les fautes de frappe sont comprises)"; } catch { }
            _input.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendInput(); } };
            _input.GotFocus += (s, e) => { try { _inputBar.Invalidate(); } catch { } };
            _input.LostFocus += (s, e) => { try { _inputBar.Invalidate(); } catch { } };
            _inputBar.Controls.Add(_input);
            _send = FpsUi.GoldButton("→");
            _send.Click += (s, e) => SendInput();
            _inputBar.Controls.Add(_send);

            // --- Raccourcis PERMANENTS (pas seulement dans le message d'accueil) ---
            _quick = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent };
            (string, string)[] qs =
            {
                ("Enquête complète", "fais un bilan complet de mon pc"),
                ("Solutions gratuites", "trouve des solutions gratuites pour booster mon pc"),
                ("Test ping", "mesure mon ping"),
                ("Processus gourmands", "quel programme consomme mon cpu en fond"),
                ("Prépare ma partie", "prépare ma partie"),
                ("Pourquoi ?", "pourquoi"),
            };
            foreach (var qd in qs) { var c = Chip(qd.Item1); string txt = qd.Item2; c.Click += (s, e) => Send(txt); _quick.Controls.Add(c); }
            Controls.Add(_quick);

            Resize += (s, e) => DoLayout();
        }

        // ------------------------------------------------------------------
        //  L'état vivant du Copilote (sous-titre de droite)
        // ------------------------------------------------------------------
        private enum StatusKind { Ready, Working, Findings }

        private void SetStatus(StatusKind k)
        {
            if (_status == null) return;
            try
            {
                switch (k)
                {
                    case StatusKind.Working:
                        _status.ForeColor = FpsUi.Gold; _status.Text = "●  analyse en cours…"; break;
                    case StatusKind.Findings:
                        _status.ForeColor = _statusCauses > 0 ? FpsUi.Warn : FpsUi.Ok;
                        _status.Text = _statusCauses > 0
                            ? "●  " + _statusCauses + " cause(s) identifiée(s)"
                            : "●  rien à signaler";
                        break;
                    default:
                        _status.ForeColor = FpsUi.Dim; _status.Text = "●  prêt"; break;
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------
        //  Accueil scénarisé : le Copilote reçoit (chat encore vide)
        // ------------------------------------------------------------------
        private void BuildHero()
        {
            _hero = new BufferedPanel { BackColor = FpsUi.BgMain };
            _hero.Paint += OnPaintHero;
            Controls.Add(_hero);

            _heroChips = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight, WrapContents = true,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent
            };
            foreach (var st in Starters)
            {
                var c = Chip(st.Item1); string sendText = st.Item2;
                c.Click += (s, e) => Send(sendText);
                _heroChips.Controls.Add(c);
            }
            _hero.Controls.Add(_heroChips);
        }

        private void OnPaintHero(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = _hero.ClientSize.Width;
            int top = HeroTop();

            // Halo d'or très doux derrière l'anneau : l'écrin, pas un projecteur.
            int ring = 64, cx = w / 2, cy = top + HeroRingCy;
            var halo = new Rectangle(cx - 110, cy - 110, 220, 220);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(halo);
                using (var pgb = new PathGradientBrush(path))
                {
                    pgb.CenterColor = FpsUi.Accent(26);
                    pgb.SurroundColors = new[] { Color.FromArgb(0, FpsUi.Gold) };
                    g.FillEllipse(pgb, halo);
                }
            }
            Logo.Draw(g, new RectangleF(cx - ring / 2f, cy - ring / 2f, ring, ring), FpsUi.Gold, false);

            // « Le Copilote » gravé, puis sa promesse en une ligne.
            float tw = FpsUi.MeasureTracked(g, "LE COPILOTE", HeroTitle, 3f);
            FpsUi.DrawTracked(g, "LE COPILOTE", HeroTitle, FpsUi.Ink, cx - tw / 2f, cy + 68, 3f);
            const string tag = "Il mesure en direct, explique son diagnostic et corrige — toujours avec ton accord.";
            Size tsz = TextRenderer.MeasureText(g, tag, FpsUi.Body);
            TextRenderer.DrawText(g, tag, FpsUi.Body, new Point(cx - tsz.Width / 2, cy + 92), FpsUi.Dim, TextFormatFlags.NoPrefix);

            // L'invitation, sous les suggestions.
            const string hint = "…ou décris ton souci dans la barre en bas — les fautes de frappe sont comprises.";
            Size hsz = TextRenderer.MeasureText(g, hint, FpsUi.Small);
            int hy = _heroChips != null ? _heroChips.Bottom + 18 : cy + 130;
            TextRenderer.DrawText(g, hint, FpsUi.Small, new Point(cx - hsz.Width / 2, hy), FpsUi.Dim2, TextFormatFlags.NoPrefix);
        }

        // Verticales du héros : anneau (centre), nom, accroche, puis suggestions et invitation.
        private const int HeroRingCy = 46, HeroChipsY = 176;

        /// <summary>Y du bloc central du héros (l'ensemble respire, légèrement au-dessus du milieu).</summary>
        private int HeroTop()
        {
            int contentH = HeroChipsY + (_heroChips != null ? _heroChips.PreferredSize.Height : 60) + 52;
            return Math.Max(8, (_hero.ClientSize.Height - contentH) / 2);
        }

        private void LayoutHero()
        {
            if (_hero == null || _heroChips == null) return;
            int w = _hero.ClientSize.Width;
            int chipsW = Math.Min(660, Math.Max(240, w - 120));
            _heroChips.MaximumSize = new Size(chipsW, 0);
            _heroChips.Location = new Point((w - _heroChips.PreferredSize.Width) / 2, HeroTop() + HeroChipsY);
            _hero.Invalidate();
        }

        /// <summary>Premier message (envoi, conseil proactif ou capture) : l'accueil s'efface.</summary>
        private void RemoveHero()
        {
            if (_hero == null) return;
            var h = _hero; _hero = null; _heroChips = null;
            try { h.Visible = false; Controls.Remove(h); h.Dispose(); } catch { }
            try { _flow.Visible = true; } catch { }
        }

        /// <summary>Tuile du cockpit : légende + valeur colorée (— tant que la mesure n'est pas là).</summary>
        private Label StatTile(string title)
        {
            var tile = FpsUi.CardPanel(10f);
            var cap = FpsUi.Text(title, FpsUi.Tiny, FpsUi.Dim2); cap.Location = new Point(12, 6);
            var val = FpsUi.Text("—", FpsUi.F(12.5f, true), FpsUi.Dim); val.Location = new Point(12, 19);
            tile.Controls.Add(cap); tile.Controls.Add(val);
            _statsRow.Controls.Add(tile);
            return val;
        }

        /// <summary>Met à jour une tuile ; si l'ancienne et la nouvelle valeur commencent par un
        /// nombre, il COMPTE jusqu'à la cible (cockpit vivant) au lieu de sauter.</summary>
        private void Tile(Label l, string txt, Color c)
        {
            try
            {
                BeginInvoke((Action)(() =>
                {
                    int from, to; string suffix;
                    if (Anim.On && LeadingInt(l.Text, out from) && LeadingInt(txt, out to) && from != to
                        && TrySuffix(txt, out suffix))
                    {
                        l.ForeColor = c;
                        int start = from;
                        Anim.Tween(450, Ease.OutCubic,
                            p => { try { l.Text = ((int)Math.Round(start + (to - start) * p)) + suffix; } catch { } },
                            () => { try { l.Text = txt; } catch { } });
                    }
                    else { l.Text = txt; l.ForeColor = c; }
                }));
            }
            catch { }
        }

        private static bool LeadingInt(string s, out int v)
        {
            v = 0; if (string.IsNullOrEmpty(s)) return false;
            int i = 0; while (i < s.Length && char.IsDigit(s[i])) i++;
            return i > 0 && int.TryParse(s.Substring(0, i), out v);
        }

        private static bool TrySuffix(string s, out string suffix)
        {
            suffix = ""; if (string.IsNullOrEmpty(s)) return false;
            int i = 0; while (i < s.Length && char.IsDigit(s[i])) i++;
            suffix = s.Substring(i);
            return true;
        }

        /// <summary>Remplit le cockpit en tâche de fond : écrans, ping réel, température GPU.
        /// Sémantique ONYX : émeraude = bon, orange = attention, rouge = critique (jamais d'or).</summary>
        private void RefreshTiles()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                string scr = "n/d"; Color scrC = FpsUi.Dim2;
                try
                {
                    var list = DisplayInfo.Query(); int below = 0, max = 0;
                    if (list != null) foreach (var d in list) { if (d.BelowMax) below++; if (d.CurrentHz > max) max = d.CurrentHz; }
                    if (list != null && list.Count > 0)
                    { scr = below == 0 ? max + " Hz ✓" : below + " sous le max"; scrC = below == 0 ? FpsUi.Ok : FpsUi.Warn; }
                }
                catch { }
                Tile(_vScreen, scr, scrC);

                string png = "hors-ligne"; Color pngC = FpsUi.Dim2;
                try
                {
                    double avg, jit; int loss;
                    if (ChatActions.PingSample(3, 500, out avg, out jit, out loss))
                    { png = avg.ToString("0") + " ms"; pngC = avg < 40 && loss == 0 ? FpsUi.Ok : avg < 80 ? FpsUi.Warn : FpsUi.Err; }
                }
                catch { }
                Tile(_vPing, png, pngC);

                string gpu = "n/d"; Color gpuC = FpsUi.Dim2;
                try
                {
                    using (var mon = new HwMonitor())
                    {
                        HwSample smp = mon.Sample();
                        if (smp.Gpu != null && smp.Gpu.Ok && smp.Gpu.TempC > 0)
                        { gpu = smp.Gpu.TempC.ToString("0") + " °C"; gpuC = smp.Gpu.TempC < 70 ? FpsUi.Ok : smp.Gpu.TempC < 85 ? FpsUi.Warn : FpsUi.Err; }
                    }
                }
                catch { }
                Tile(_vGpu, gpu, gpuC);
            });
        }

        public override void OnShown()
        {
            DoLayout();
            try { _input.Focus(); } catch { }   // on peut taper directement, sans cliquer le champ
            AppStats.Get(a =>
            {
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _stats = new BadgeCatalog.Stats { OptiActive = a.OptiActive, OptiTotal = a.OptiTotal, GamesDet = a.GamesDet, Health = a.Health };
                        if (_vHealth != null)
                            Tile(_vHealth, a.Health + " %", a.Health >= 80 ? FpsUi.Ok : a.Health >= 60 ? FpsUi.Warn : FpsUi.Err);
                        Greet(); Invalidate(true);
                    }));
                }
                catch { }
            });
            RefreshTiles();
            Greet();
            // Accueil PROACTIF : il a déjà jeté un œil (mesures légères uniquement) et ne parle
            // que s'il y a quelque chose d'utile à proposer — sinon il se tait.
            try
            {
                if (!_proactive && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BT_UISHOT")))
                {
                    _proactive = true;
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        var tip = QuickTip();
                        if (tip != null) { try { BeginInvoke((Action)(() => AddBubble(true, tip.Text, tip))); } catch { } }
                    });
                }
            }
            catch { }
            // Démo pour la capture hors-écran : montre un échange complet (bulles alignées + avatar).
            // BT_UISHOT_MSG permet au harnais d'envoyer un AUTRE message (test des fautes de frappe…).
            try
            {
                if (!_seeded && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BT_UISHOT")))
                {
                    _seeded = true;
                    string demo = Environment.GetEnvironmentVariable("BT_UISHOT_MSG");
                    // « accueil » = ne rien envoyer : capture de la scène d'entrée (héros).
                    if (demo != "accueil") Send(string.IsNullOrEmpty(demo) ? "ça rame en jeu" : demo);
                }
            }
            catch { }
        }

        private void Greet()
        {
            if (_greeted) return;
            _greeted = true;
            // L'accueil n'est plus une bulle : c'est la scène d'entrée (héros). Le flux reste
            // masqué tant que la conversation n'a pas commencé.
            try { _flow.Visible = false; } catch { }
            LayoutHero();
        }

        private void SendInput()
        {
            string q = _input.Text.Trim();
            if (q.Length == 0) return;
            _input.Clear();
            Send(q);
        }

        private Timer _typingAnim;
        private Panel _typingRow;
        private int _dot;

        private void Send(string q)
        {
            AddBubble(false, q, null);
            var reply = DocAssistant.Answer(q, _stats, Host.Log, _last);
            // Sous capture : réponse immédiate (pas de message loop long). En vrai : « Le Copilote écrit… ».
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BT_UISHOT")))
            {
                AddBubble(true, reply.Text, reply);
                if (reply.Action != null && reply.Action.AutoRun)
                {
                    DocAssistant.Reply res;
                    try { res = reply.Action.Run(Host.Log); } catch { res = new DocAssistant.Reply { Text = "—" }; }
                    if (res != null) AddBubble(true, res.Text, res);
                }
                return;
            }
            ShowTyping();
            // Accroche PURE (sans donnée) + IA active → on la REFORMULE à chaque fois (jamais deux
            // fois la même phrase toute faite). Les résultats/chiffres/définitions gardent leur
            // texte exact (Dynamic = false). Fallback : la phrase d'origine si le modèle traîne.
            if (reply.Dynamic && LocalBrain.Enabled)
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    string line = null;
                    try { line = LocalBrain.Rephrase(reply.Text, LocalBrain.BestModel()); } catch { }
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            HideTyping();
                            AddBubble(true, string.IsNullOrEmpty(line) ? reply.Text : line, reply);
                            if (reply.Action != null && reply.Action.AutoRun) RunAction(reply.Action);
                        }));
                    }
                    catch { }
                });
                return;
            }
            var t = new Timer { Interval = 650 };
            t.Tick += (s, e) =>
            {
                t.Stop(); t.Dispose(); HideTyping();
                AddBubble(true, reply.Text, reply);
                // Mesure (lecture seule) : elle part d'elle-même. Un CHANGEMENT, lui, attend le clic.
                if (reply.Action != null && reply.Action.AutoRun) RunAction(reply.Action);
            };
            t.Start();
        }

        /// <summary>Exécute une action EN TÂCHE DE FOND (certaines durent une minute : point de
        /// restauration, nettoyage) puis affiche le compte-rendu dans la conversation, avec la
        /// correction correspondante s'il y a réellement quelque chose à corriger.
        /// 'src' = le bouton qui a lancé : il raconte la fin (« ✓ Terminé », ou ré-armé si échec).</summary>
        private void RunAction(DocAssistant.ChatAction a, Button src = null)
        {
            if (a == null || a.Run == null) return;
            ShowTyping();
            // Journal RELAYÉ dans la bulle « écrit… » : pendant une action longue (installation,
            // TOUT réparer), chaque étape s'affiche en direct au lieu de trois points muets.
            Action<string, int> live = delegate (string m, int l)
            {
                try { Host.Log(m, l); } catch { }
                if (string.IsNullOrEmpty(m)) return;
                try { BeginInvoke((Action)(() => TypingProgress(m))); } catch { }
            };
            System.Threading.Tasks.Task.Run(() =>
            {
                DocAssistant.Reply res; bool failed = false;
                try { res = a.Run(live); }
                catch (Exception ex) { failed = true; res = new DocAssistant.Reply { Text = "L'action n'a pas abouti : " + ex.Message }; }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        HideTyping();
                        if (src != null)
                        {
                            try
                            {
                                if (failed) { src.Text = "▶  " + a.Label; src.Enabled = true; }   // on peut retenter
                                else src.Text = "✓  Terminé";                                      // verrouillé : pas de double exécution
                            }
                            catch { }
                        }
                        if (res != null) AddBubble(true, res.Text, res);
                        if (a.IsChange) RefreshTiles();   // le cockpit suit la réalité après une correction
                        // Enchaînement automatique d'une MESURE portée par le résultat
                        // (ex. vérification complète après « TOUT réparer »).
                        if (res != null && res.Action != null && res.Action.AutoRun) RunAction(res.Action);
                    }));
                }
                catch { }
            });
        }

        /// <summary>Coup d'œil PROACTIF à l'arrivée : mesures légères seulement (écrans, disque,
        /// bibliothèques), UNE proposition maximum, et le silence si tout va bien.</summary>
        private DocAssistant.Reply QuickTip()
        {
            try
            {
                var list = DisplayInfo.Query();
                if (list != null)
                    foreach (var d in list)
                        if (d.BelowMax)
                            return new DocAssistant.Reply
                            {
                                Text = "Pendant que tu t'installais, j'ai jeté un œil : « " + d.Name + " » tourne à "
                                     + d.CurrentHz + " Hz alors qu'il peut faire " + d.MaxHz + " Hz. Correction gratuite, un clic :",
                                Action = ChatActions.FixScreen()
                            };
            }
            catch { }
            try
            {
                string root = System.IO.Path.GetPathRoot(Environment.SystemDirectory);
                var di = new System.IO.DriveInfo(root);
                double freeGb = di.AvailableFreeSpace / 1073741824.0;
                int pct = di.TotalSize > 0 ? (int)Math.Round(di.AvailableFreeSpace * 100.0 / di.TotalSize) : 100;
                if (pct < 12)
                {
                    var fix = ChatActions.FixDisk();
                    return new DocAssistant.Reply
                    {
                        Text = "Pendant que tu t'installais, j'ai jeté un œil : ton disque système n'a plus que "
                             + freeGb.ToString("0") + " Go libres (" + pct + " %). "
                             + (fix != null ? "Nettoyage gratuit (fichiers qui se régénèrent), un clic :" : "Le panneau Nettoyage disque t'aidera à faire de la place."),
                        Action = fix,
                        Tool = fix == null ? FindTool("Nettoyage disque") : null
                    };
                }
            }
            catch { }
            try
            {
                int missing = LibScan.MissingEssentialCount();
                if (missing > 0)
                    return new DocAssistant.Reply
                    {
                        Text = "Pendant que tu t'installais, j'ai vérifié tes bibliothèques de jeu : il en manque " + missing
                             + " (Visual C++, DirectX, .NET — gratuites, Microsoft). C'est la cause n°1 d'un jeu qui refuse de démarrer.",
                        Tool = FindTool("Bibliothèques de jeu")
                    };
            }
            catch { }
            return null;
        }

        private HelpCatalog.Entry FindTool(string name)
        {
            try { foreach (var e in HelpCatalog.Entries(Host.Log)) if (e.Tool == name) return e; } catch { }
            return null;
        }

        // Indicateur « Le Copilote écrit… » : mini-bulle avec 3 points qui pulsent.
        private void ShowTyping()
        {
            HideTyping();
            SetStatus(StatusKind.Working);
            var bubble = new Panel { Size = new Size(66, 38), BackColor = BubbleDoc };
            bubble.SizeChanged += (s, e) => { try { using (var p = RoundAsym(bubble.ClientRectangle, 5, 14, 14, 14)) bubble.Region = new Region(p); } catch { } };
            var dots = new Label { Dock = DockStyle.Fill, Font = FpsUi.H3, ForeColor = FpsUi.Gold, TextAlign = ContentAlignment.MiddleCenter, Text = "●··", BackColor = Color.Transparent };
            bubble.Controls.Add(dots);
            _typingBubble = bubble; _typingDots = dots; _typingProgress = null;
            var avatar = MakeAvatar();
            var row = new Panel { Size = new Size(AV + GAP + 66, Math.Max(AV, 38)), BackColor = Color.Transparent, Margin = new Padding(ColLeft(), 7, 10, 7), Tag = "typing" };
            avatar.Location = new Point(0, 0); bubble.Location = new Point(AV + GAP, 0);
            row.Controls.Add(avatar); row.Controls.Add(bubble);
            _typingRow = row; _flow.Controls.Add(row); try { _flow.ScrollControlIntoView(row); } catch { }
            _dot = 1;
            _typingAnim = new Timer { Interval = 320 };
            // Les points ne pulsent que tant que le journal ne parle pas (voir TypingProgress).
            _typingAnim.Tick += (s, e) => { if (_typingProgress == null) { _dot = _dot % 3 + 1; dots.Text = new string('●', _dot) + new string('·', 3 - _dot); } };
            _typingAnim.Start();
        }

        /// <summary>Relaye la DERNIÈRE ligne du journal dans la bulle « écrit… » : pendant une
        /// action longue (installation, TOUT réparer), on voit chaque étape en direct.</summary>
        private void TypingProgress(string m)
        {
            if (_typingDots == null || _typingRow == null) return;
            _typingProgress = m;
            if (_typingBubble != null && _typingBubble.Width < 380)
            {
                _typingBubble.Width = 380;
                _typingRow.Width = AV + GAP + 380;
                _typingDots.Font = FpsUi.Small;
                _typingDots.TextAlign = ContentAlignment.MiddleLeft;
                _typingDots.Padding = new Padding(12, 0, 10, 0);
            }
            _typingDots.Text = m.Length > 56 ? m.Substring(0, 56) + "…" : m;
        }

        private void HideTyping()
        {
            if (_typingAnim != null) { try { _typingAnim.Stop(); _typingAnim.Dispose(); } catch { } _typingAnim = null; }
            if (_typingRow != null) { try { _flow.Controls.Remove(_typingRow); _typingRow.Dispose(); } catch { } _typingRow = null; }
            _typingBubble = null; _typingDots = null; _typingProgress = null;
            SetStatus(StatusKind.Findings);
        }

        /// <summary>Avatar du Copilote : l'anneau d'or sur pastille carbone — le monogramme ONYX,
        /// pas une mascotte. (Le joueur, lui, n'a pas d'avatar : l'asymétrie structure la lecture.)</summary>
        private Panel MakeAvatar()
        {
            var avatar = new Panel { Size = new Size(AV, AV), BackColor = Color.Transparent };
            avatar.Paint += (s, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                var rr = new Rectangle(0, 0, AV - 1, AV - 1);
                using (var br = new SolidBrush(FpsUi.CardHi)) g.FillEllipse(br, rr);
                using (var pen = new Pen(Color.FromArgb(110, FpsUi.Gold), 1.2f)) g.DrawEllipse(pen, rr);
                Logo.Draw(g, new RectangleF(6.5f, 6.5f, AV - 14, AV - 14), FpsUi.Gold, false);
            };
            return avatar;
        }

        // Suggestions de démarrage (label affiché → texte envoyé au Copilote).
        private static readonly (string, string)[] Starters =
        {
            ("Ça rame en jeu", "ça rame et ça saccade en jeu"),
            ("FPS bas", "mes fps sont bas"),
            ("Ping / lag en ligne", "ça lag en ligne, ping élevé"),
            ("Un jeu ne démarre pas", "un jeu refuse de démarrer, dll manquante"),
            ("Écran bloqué à 60 Hz", "mon écran semble bloqué à 60 hz"),
            ("Bilan complet du PC", "fais un bilan complet de mon pc"),
            ("Le PC chauffe", "le pc ou le gpu chauffe et bride"),
            ("Réparer Windows", "répare windows, fichiers système corrompus"),
            ("Plus de son", "je n'ai plus de son"),
            ("Plus d'internet", "je n'ai plus d'internet, pas de connexion"),
            ("Qui bouffe mon CPU ?", "quel programme consomme mon cpu en fond"),
            ("Solutions gratuites", "trouve des solutions gratuites pour booster mon pc"),
            ("PC lent à s'allumer", "mon pc est long a demarrer, trop de programmes au boot"),
            ("Ça crash / écran bleu", "mes jeux crashent, parfois ecran bleu"),
            ("Libérer de l'espace", "libérer de l'espace disque"),
            // Nouvelles fonctions v15.18+ : visibles dès l'ouverture, plus besoin de deviner la phrase.
            ("Bilan mises à jour", "fais le bilan des mises à jour"),
            ("Hibernation : récupérer des Go", "désactive l'hibernation"),
            ("Fenêtres qui saccadent", "mes fenetres windows saccadent"),
            ("Jeux gratuits PC", "jeux gratuits"),
            ("Actus gaming", "actu jeux vidéo"),
        };

        private const int AV = 36, GAP = 10;
        private const int ColumnW = 860;                                   // largeur maxi de la conversation
        private static readonly Color BubbleDoc = Color.FromArgb(24, 20, 16);   // carbone chaud (Copilote)
        private static readonly Color BubbleUser = Color.FromArgb(42, 34, 22);  // bronze éteint (joueur)

        /// <summary>Marge gauche de la colonne : la conversation est CENTRÉE (pas collée au rail).</summary>
        private int ColLeft()
        {
            int w = _flow != null ? _flow.ClientSize.Width : 800;
            return Math.Max(6, (w - ColumnW) / 2);
        }

        private void AddBubble(bool doc, string text, DocAssistant.Reply reply)
        {
            RemoveHero();
            string body = doc && reply != null ? reply.Text : text;
            int flowW = _flow.ClientSize.Width;
            int colW = Math.Min(ColumnW, flowW - 12);
            int maxTextW = Math.Min(620, Math.Max(220, colW - AV - GAP - 90));

            // Bulle auto-dimensionnée : Copilote carbone / Toi bronze, coins arrondis asymétriques
            // (le coin proche de l'émetteur est serré : la bulle « pointe » vers qui parle).
            Color bg = doc ? BubbleDoc : BubbleUser;
            Color bord = doc ? FpsUi.Border : Color.FromArgb(120, FpsUi.GoldDim);
            var bubble = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = bg, Padding = new Padding(16, 12, 18, 14), Margin = new Padding(0) };
            bubble.SizeChanged += (s, e) =>
            {
                try
                {
                    using (var p = doc ? RoundAsym(bubble.ClientRectangle, 5, 14, 14, 14)
                                       : RoundAsym(bubble.ClientRectangle, 14, 5, 14, 14))
                        bubble.Region = new Region(p);
                }
                catch { }
            };
            bubble.Paint += (s, e) =>
            {
                try
                {
                    using (var pen = new Pen(bord))
                    using (var p = doc ? RoundAsym(new Rectangle(0, 0, bubble.Width - 1, bubble.Height - 1), 5, 14, 14, 14)
                                       : RoundAsym(new Rectangle(0, 0, bubble.Width - 1, bubble.Height - 1), 14, 5, 14, 14))
                        e.Graphics.DrawPath(pen, p);
                }
                catch { }
            };

            var col = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            // En-tête : qui parle + l'heure (repère utile quand la conversation s'allonge).
            // Le nom du Copilote se grave en Marcellus or — c'est sa signature.
            col.Controls.Add(new Label
            {
                AutoSize = true, Font = doc ? NameFont : FpsUi.Small,
                ForeColor = doc ? FpsUi.Gold : Color.FromArgb(216, 190, 130),
                // Même marge gauche que le corps du message (les Label d'un FlowLayoutPanel
                // ont 3 px par défaut) : sans ça l'en-tête débordait de 3 px vers la gauche
                // et la première lettre passait sous l'arrondi de la bulle.
                BackColor = Color.Transparent, Margin = new Padding(3, 0, 3, 5),
                Text = (doc ? "LE COPILOTE" : "TOI") + "   " + DateTime.Now.ToString("HH:mm")
            });
            var bodyLbl = new Label { AutoSize = true, MaximumSize = new Size(maxTextW, 0), Font = FpsUi.Body, ForeColor = FpsUi.Ink, BackColor = Color.Transparent, Text = body ?? "" };
            col.Controls.Add(bodyLbl);

            // Enfants « riches » (cartes, boutons, pied) : mémorisés pour n'apparaître qu'à la fin
            // de l'écriture en direct (comme une vraie rédaction), révélés d'un coup si clic.
            var lateKids = new List<Control>();

            // Diagnostic STRUCTURÉ : chaque cause en carte d'impact colorée, bouton intégré.
            if (reply != null && reply.Cards != null)
                foreach (var cd in reply.Cards) if (cd != null) { var k = MakeCard(cd, maxTextW); col.Controls.Add(k); lateKids.Add(k); }
            if (reply != null && !string.IsNullOrEmpty(reply.Footer))
            {
                var f = new Label
                {
                    AutoSize = true, MaximumSize = new Size(maxTextW, 0), Font = FpsUi.Small,
                    ForeColor = FpsUi.Dim, BackColor = Color.Transparent,
                    Margin = new Padding(3, 10, 3, 0), Text = reply.Footer
                };
                col.Controls.Add(f); lateKids.Add(f);
            }
            // Diagnostic exportable : un .txt propre sur le Bureau — le livrable à montrer/garder.
            if (reply != null && reply.Exportable)
            {
                var ex = FpsUi.GhostButton("📄  Enregistrer ce diagnostic (Bureau)");
                ex.AutoSize = false; ex.Size = new Size(Math.Min(maxTextW, 300), 30); ex.Margin = new Padding(0, 8, 0, 0);
                var rep = reply; string bodyTxt = body ?? "";
                ex.Click += (s, e) =>
                {
                    try
                    {
                        string path = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                            "ONYX-diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt");
                        System.IO.File.WriteAllText(path, ReplyFullText(rep, bodyTxt), new UTF8Encoding(false));
                        ex.Text = "✓  Enregistré sur le Bureau"; ex.Enabled = false;
                        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
                    }
                    catch { ex.Text = "⚠  Échec de l'enregistrement"; }
                };
                col.Controls.Add(ex); lateKids.Add(ex);
            }
            if (reply != null && reply.Tool != null)
            {
                var btn = FpsUi.GoldButton("Ouvrir « " + reply.Tool.Tool + " »  →");
                btn.AutoSize = false; btn.Size = new Size(Math.Min(maxTextW, 320), 34); btn.Margin = new Padding(0, 8, 0, 2);
                var entry = reply.Tool;
                btn.Click += (s, e) => { try { Host.OpenDialog(entry.Open()); } catch { } };
                col.Controls.Add(btn); lateKids.Add(btn);
            }
            // Action(s) qui MODIFIENT le système : bouton explicite + annonce de ce qui change.
            // Jamais d'exécution automatique ici — c'est la promesse d'ONYX.
            if (reply != null && reply.Action != null && reply.Action.IsChange)
                foreach (var k in AddActionButton(col, reply.Action, maxTextW)) lateKids.Add(k);
            // Le plan ne se rend en boutons que s'il n'est PAS déjà porté par des cartes.
            if (reply != null && reply.Plan != null && reply.Cards == null)
                foreach (var step in reply.Plan) if (step != null) foreach (var k in AddActionButton(col, step, maxTextW)) lateKids.Add(k);
            if (reply != null && reply.ShowStarters)
            {
                var chips = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new Size(maxTextW, 0), BackColor = Color.Transparent, Margin = new Padding(0, 8, 0, 0) };
                foreach (var st in Starters) { var c = Chip(st.Item1); string sendText = st.Item2; c.Click += (s, e) => Send(sendText); chips.Controls.Add(c); }
                col.Controls.Add(chips); lateKids.Add(chips);
            }
            // Clic droit n'importe où sur la bulle : copier le message (texte + cartes + pied).
            {
                var cms = new ContextMenuStrip();
                var rep = reply; string bodyTxt = body ?? "";
                cms.Items.Add("Copier ce message", null, (s, e) =>
                {
                    try
                    {
                        var t = new StringBuilder(bodyTxt);
                        if (rep != null && rep.Cards != null)
                        { int i = 0; foreach (var c in rep.Cards) { i++; t.Append("\r\n\r\n").Append(i).Append(". [IMPACT ").Append(c.Impact).Append("] ").Append(c.Text); } }
                        if (rep != null && !string.IsNullOrEmpty(rep.Footer)) t.Append("\r\n\r\n").Append(rep.Footer);
                        Clipboard.SetText(t.ToString().Trim());
                    }
                    catch { }
                });
                bubble.ContextMenuStrip = cms; col.ContextMenuStrip = cms;
                foreach (Control cc in col.Controls) if (cc is Label) cc.ContextMenuStrip = cms;
            }
            bubble.Controls.Add(col);

            var avatar = doc ? MakeAvatar() : null;

            // Ligne avatar+bulle : le Copilote à GAUCHE (avec anneau), Toi à DROITE (sans avatar).
            var row = new Panel { BackColor = Color.Transparent, Tag = doc ? "doc" : "user" };
            if (doc) { avatar.Location = new Point(0, 0); bubble.Location = new Point(AV + GAP, 0); row.Controls.Add(avatar); }
            else bubble.Location = new Point(0, 0);
            row.Controls.Add(bubble);

            // La ligne suit la taille réelle de la bulle (l'écriture en direct la fait grandir).
            Action sync = delegate
            {
                Size bs2 = bubble.PreferredSize;
                int rw = (doc ? AV + GAP : 0) + bs2.Width;
                int rh = Math.Max(doc ? AV : 0, bs2.Height);
                if (row.Width != rw || row.Height != rh)
                {
                    row.Size = new Size(rw, rh);
                    row.Margin = new Padding(doc ? ColLeft() : Math.Max(6, flowW - rw - ColLeft() - 22), 7, 10, 7);
                }
            };
            bubble.SizeChanged += (s, e) => { try { sync(); } catch { } };
            sync();
            _flow.Controls.Add(row);
            try { _flow.ScrollControlIntoView(row); } catch { }

            // Écriture EN DIRECT des réponses du Copilote : le texte se rédige (≈ 210 caractères/s),
            // un clic n'importe où sur la bulle affiche tout. Les cartes/boutons arrivent à la fin.
            bool typewrite = doc && Anim.On && body != null && body.Length > 24
                             && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BT_UISHOT"));
            if (typewrite)
            {
                foreach (Control k in lateKids) k.Visible = false;
                string full = body;
                bodyLbl.Text = "";
                var writer = new Timer { Interval = 16 };
                int idx = 0, tick = 0;
                Action finish = delegate
                {
                    try { writer.Stop(); writer.Dispose(); } catch { }
                    bodyLbl.Text = full;
                    foreach (Control k in lateKids) k.Visible = true;
                    bubble.Cursor = Cursors.Default; bodyLbl.Cursor = Cursors.Default;
                    sync();
                    try { _flow.ScrollControlIntoView(row); } catch { }
                };
                bubble.Cursor = Cursors.Hand; bodyLbl.Cursor = Cursors.Hand;
                EventHandler skip = (s, e) => { if (idx < full.Length) { idx = full.Length; finish(); } };
                bubble.Click += skip; bodyLbl.Click += skip; col.Click += skip;
                writer.Tick += (s, e) =>
                {
                    idx = Math.Min(full.Length, idx + 4);   // ~250 c/s : rapide, jamais pénible
                    bodyLbl.Text = full.Substring(0, idx);
                    if (++tick % 6 == 0) { try { _flow.ScrollControlIntoView(row); } catch { } }
                    if (idx >= full.Length) finish();
                };
                writer.Start();
            }
            else if (doc && Anim.On && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BT_UISHOT")))
            {
                // Messages courts : simple glissement d'entrée (la ligne « pousse » depuis le bas).
                int target = row.Height;
                row.Height = Math.Max(8, target / 3);
                Anim.Tween(160, Ease.OutCubic,
                    p => { try { row.Height = Math.Max(8, (int)(target * (0.33f + 0.67f * p))); } catch { } },
                    () => { try { sync(); _flow.ScrollControlIntoView(row); } catch { } });
            }

            // Suivi de conversation : la dernière réponse ACTIONNABLE (ou explicable) devient le
            // contexte du prochain « oui »/« non »/« pourquoi ? » ; un « oui » sur un outil vaut
            // clic → on l'ouvre.
            if (doc && reply != null)
            {
                _statusCauses = reply.Cards != null ? reply.Cards.Count : 0;
                SetStatus(StatusKind.Findings);
                if (reply.Tool != null || reply.Action != null || (reply.Plan != null && reply.Plan.Count > 0)
                    || !string.IsNullOrEmpty(reply.Explain)) _last = reply;
                if (reply.OpenToolNow && reply.Tool != null) { try { Host.OpenDialog(reply.Tool.Open()); } catch { } }
            }
        }

        /// <summary>Bouton d'une correction : libellé, puis en petit ce qu'elle va changer.
        /// Une fois lancée, le bouton se verrouille (pas de double exécution).
        /// Renvoie les contrôles créés (pour l'apparition différée pendant l'écriture).</summary>
        private List<Control> AddActionButton(Control col, DocAssistant.ChatAction act, int maxTextW)
        {
            var made = new List<Control>();
            var go = FpsUi.GoldButton("▶  " + act.Label);
            go.AutoSize = false; go.Size = new Size(Math.Min(maxTextW, 340), 36); go.Margin = new Padding(0, 10, 0, 2);
            go.Click += (s, e) => { go.Enabled = false; go.Text = "en cours…"; RunAction(act, go); };
            col.Controls.Add(go); made.Add(go);
            if (!string.IsNullOrEmpty(act.Warning))
            {
                var w = new Label
                {
                    AutoSize = true, MaximumSize = new Size(maxTextW, 0), Font = FpsUi.Small,
                    ForeColor = FpsUi.Dim2, BackColor = Color.Transparent,
                    Margin = new Padding(3, 5, 3, 0), Text = act.Warning
                };
                col.Controls.Add(w); made.Add(w);
            }
            return made;
        }

        /// <summary>Carte d'une cause : barre d'accent + pastille d'impact colorée (rouge ≥ 85,
        /// orange ≥ 65, jaune sinon), texte, et bouton de correction intégré quand il y en a une.</summary>
        private Control MakeCard(DocAssistant.Card cd, int maxW)
        {
            Color acc = cd.Impact >= 85 ? FpsUi.Err : cd.Impact >= 65 ? Color.FromArgb(255, 152, 64) : FpsUi.Warn;
            string level = cd.Impact >= 85 ? "CRITIQUE" : cd.Impact >= 65 ? "ÉLEVÉ" : "MOYEN";
            var pnl = new Panel { Width = Math.Min(maxW, 560), BackColor = Color.Transparent, Margin = new Padding(0, 8, 0, 0) };
            pnl.Paint += (s, e) =>
            {
                FpsUi.PaintCard(e.Graphics, pnl.ClientRectangle, Color.FromArgb(28, 24, 19), FpsUi.Border, 10f);
                using (var br = new SolidBrush(acc)) e.Graphics.FillRectangle(br, 1, 10, 3, pnl.Height - 20);
            };
            var pill = FpsUi.Text("IMPACT " + cd.Impact + "  ·  " + level, FpsUi.Tiny, acc);
            pill.Location = new Point(14, 8);
            var body = new Label
            {
                AutoSize = true, MaximumSize = new Size(pnl.Width - 28, 0), Font = FpsUi.Body,
                ForeColor = FpsUi.Ink, BackColor = Color.Transparent, Location = new Point(14, 25), Text = cd.Text ?? ""
            };
            pnl.Controls.Add(pill); pnl.Controls.Add(body);
            int y = 25 + body.PreferredSize.Height;
            if (cd.Fix != null)
            {
                var act = cd.Fix;
                var go = FpsUi.GoldButton("▶  " + act.Label);
                go.AutoSize = false; go.Size = new Size(Math.Min(pnl.Width - 28, 320), 30);
                go.Location = new Point(14, y + 8);
                go.Click += (s, e) => { go.Enabled = false; go.Text = "en cours…"; RunAction(act, go); };
                pnl.Controls.Add(go);
                y = go.Location.Y + go.Height;
                if (!string.IsNullOrEmpty(act.Warning))
                {
                    var warn = new Label
                    {
                        AutoSize = true, MaximumSize = new Size(pnl.Width - 28, 0), Font = FpsUi.Tiny,
                        ForeColor = FpsUi.Dim2, BackColor = Color.Transparent,
                        Location = new Point(14, y + 4), Text = act.Warning
                    };
                    pnl.Controls.Add(warn);
                    y = warn.Location.Y + warn.PreferredSize.Height;
                }
            }
            pnl.Height = y + 12;
            return pnl;
        }

        /// <summary>Le diagnostic complet mis en page pour un .txt : en-tête daté, causes avec
        /// impact, points sains, et le raisonnement mesure par mesure — le livrable client.</summary>
        private static string ReplyFullText(DocAssistant.Reply r, string body)
        {
            var sb = new StringBuilder();
            sb.AppendLine("DIAGNOSTIC ONYX — " + DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
            sb.AppendLine(new string('=', 48));
            sb.AppendLine();
            if (!string.IsNullOrEmpty(body)) { sb.AppendLine(body); sb.AppendLine(); }
            if (r != null && r.Cards != null)
            {
                int i = 0;
                foreach (var c in r.Cards) { i++; sb.AppendLine(i + ". [IMPACT " + c.Impact + "] " + c.Text); sb.AppendLine(); }
            }
            if (r != null && !string.IsNullOrEmpty(r.Footer)) { sb.AppendLine(r.Footer); sb.AppendLine(); }
            if (r != null && !string.IsNullOrEmpty(r.Explain))
            {
                sb.AppendLine(new string('-', 48));
                sb.AppendLine(r.Explain);
            }
            return sb.ToString().TrimEnd() + "\r\n";
        }

        // Ré-aligne la colonne quand la largeur change (Copilote à gauche de la colonne,
        // Toi à droite de la colonne — la colonne elle-même reste centrée).
        private void RealignUserRows()
        {
            if (_flow == null) return;
            int flowW = _flow.ClientSize.Width, left = ColLeft();
            foreach (Control c in _flow.Controls)
            {
                string tag = c.Tag as string;
                var m = c.Margin;
                if (tag == "user") m.Left = Math.Max(6, flowW - c.Width - left - 22);
                else m.Left = left;
                c.Margin = m;
            }
        }

        private static Button Chip(string text)
        {
            var b = new Button
            {
                Text = text, AutoSize = false, Height = 28,
                Width = TextRenderer.MeasureText(text, FpsUi.Small).Width + 24,
                FlatStyle = FlatStyle.Flat, Font = FpsUi.Small, Cursor = Cursors.Hand,
                BackColor = Color.FromArgb(25, 21, 16), ForeColor = FpsUi.Dim, Margin = new Padding(0, 0, 6, 6)
            };
            b.FlatAppearance.BorderColor = FpsUi.Border;
            b.MouseEnter += (s, e) => { b.ForeColor = FpsUi.Gold; b.FlatAppearance.BorderColor = FpsUi.Gold; };
            b.MouseLeave += (s, e) => { b.ForeColor = FpsUi.Dim; b.FlatAppearance.BorderColor = FpsUi.Border; };
            return b;
        }

        private void DoLayout()
        {
            if (_flow == null) return;
            int W = ClientSize.Width, H = ClientSize.Height, m = 34;

            // État vivant à droite de la ligne de titre.
            if (_status != null) _status.SetBounds(W - m - 260, 30, 260, 18);

            // Cockpit sous le sous-titre (le titre peint finit vers y ≈ 80) + ↻ à droite.
            if (_statsRow != null)
            {
                _statsRow.SetBounds(m, 88, Math.Max(220, W - 2 * m - 38), 46);
                int n = _statsRow.Controls.Count;
                if (n > 0)
                {
                    int gap = 8, tw = (_statsRow.Width - (n - 1) * gap) / n, x = 0;
                    foreach (Control t in _statsRow.Controls) { t.SetBounds(x, 0, tw, 46); x += tw + gap; }
                }
                if (_refresh != null) _refresh.SetBounds(W - m - 30, 96, 30, 30);
            }

            // Pile du bas : raccourcis permanents, puis barre de saisie.
            int inputH = 46, quickH = 30, pad = 16;
            int barY = H - pad - inputH;
            if (_inputBar != null)
            {
                _inputBar.SetBounds(m, barY, Math.Max(220, W - 2 * m), inputH);
                if (_send != null) _send.SetBounds(_inputBar.Width - 44, 7, 36, 32);
                if (_input != null) _input.SetBounds(16, 14, Math.Max(80, _inputBar.Width - 16 - 46 - 12), 20);
            }
            int quickY = barY - 6 - quickH;
            if (_quick != null) _quick.SetBounds(m - 4, quickY, Math.Max(220, W - 2 * (m - 4)), quickH);

            _flow.SetBounds(20, 142, W - 40, Math.Max(120, quickY - 10 - 142));
            if (_hero != null) { _hero.SetBounds(20, 142, W - 40, Math.Max(120, quickY - 10 - 142)); LayoutHero(); }
            RealignUserRows();
        }

        /// <summary>Rectangle arrondi à coins INDÉPENDANTS (tl/tr/br/bl) : le coin serré fait
        /// « pointer » la bulle vers son émetteur.</summary>
        private static GraphicsPath RoundAsym(Rectangle r, int tl, int tr, int br, int bl)
        {
            var p = new GraphicsPath();
            int maxR = Math.Min(r.Width, r.Height) / 2;
            tl = Math.Min(tl, maxR); tr = Math.Min(tr, maxR); br = Math.Min(br, maxR); bl = Math.Min(bl, maxR);
            if (tl > 0) p.AddArc(r.X, r.Y, tl * 2, tl * 2, 180, 90); else p.AddLine(r.X, r.Y, r.X, r.Y);
            if (tr > 0) p.AddArc(r.Right - tr * 2, r.Y, tr * 2, tr * 2, 270, 90); else p.AddLine(r.Right, r.Y, r.Right, r.Y);
            if (br > 0) p.AddArc(r.Right - br * 2, r.Bottom - br * 2, br * 2, br * 2, 0, 90); else p.AddLine(r.Right, r.Bottom, r.Right, r.Bottom);
            if (bl > 0) p.AddArc(r.X, r.Bottom - bl * 2, bl * 2, bl * 2, 90, 90); else p.AddLine(r.X, r.Bottom, r.X, r.Bottom);
            p.CloseFigure();
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { HideTyping(); } catch { } if (_wheel != null) { try { Application.RemoveMessageFilter(_wheel); } catch { } _wheel = null; } }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintTitle(e.Graphics, "COPILOTE", "Cockpit local : il mesure en direct, explique son diagnostic et corrige gratuitement — toujours avec ton accord.");
        }
    }
}
