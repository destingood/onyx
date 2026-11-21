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
        private readonly ContextMenuStrip _topics = new ContextMenuStrip();
        private readonly Dictionary<Control, Action<int>> _messageLayouts = new Dictionary<Control, Action<int>>();
        private Label _inputHint, _inputPlaceholder, _contextNote;
        private Button _openHardware;
        private bool _reflowing;
        private static readonly Font MessageFont = FpsUi.F(10.5f, false);
        private Label _status;                            // état vivant du Copilote (prêt / écrit / N causes)
        private int _statusCauses;                        // dernières causes identifiées (pour l'état au repos)

        // Typographie commune au reste de l'interface ONYX.
        private static readonly Font HeroTitle = FpsUi.F(23f, true);
        private static readonly Font NameFont = FpsUi.H3;

        public PageConsultation(DashboardForm host) : base(host)
        {
            Build();
        }

        private void Build()
        {
            _flow = new BufferedFlow { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = FpsUi.BgMain };
            _flow.Name = "copilot-conversation";
            _flow.Padding = new Padding(0, 12, 0, 16);
            Controls.Add(_flow);
            _wheel = new ScrollWheelFilter(_flow);
            try { Application.AddMessageFilter(_wheel); } catch { }

            BuildHero();

            // --- Cockpit : 4 tuiles d'état EN DIRECT (santé / écrans / ping / GPU) ---
            _statsRow = new BufferedPanel { BackColor = Color.Transparent };
            _statsRow.Paint += (s, e) =>
            {
                OnyxDesign.PaintCard(e.Graphics, _statsRow.ClientRectangle, FpsUi.RailBg, FpsUi.Border, 14);
                OnyxDesign.Text(e.Graphics, "TON PC", FpsUi.H3, FpsUi.Ink, new Rectangle(18, 19, _statsRow.Width - 70, 24));
                OnyxDesign.Text(e.Graphics, "Le contexte du diagnostic", FpsUi.Small, FpsUi.Dim, new Rectangle(18, 47, _statsRow.Width - 36, 22));
            };
            Controls.Add(_statsRow);
            _vHealth = StatTile("SANTÉ DU PC", NavIcons.CheckUp);
            _vScreen = StatTile("FRÉQUENCE ÉCRAN", NavIcons.Systeme);
            _vPing = StatTile("LATENCE RÉSEAU", NavIcons.Optimisations);
            _vGpu = StatTile("TEMPÉRATURE GPU", NavIcons.Jeux);
            _refresh = OnyxDesign.Button("↻");
            _refresh.Click += (s, e) => RefreshTiles();
            _refresh.AccessibleName = "Actualiser les mesures du PC";
            _statsRow.Controls.Add(_refresh);
            _contextNote = FpsUi.Text("Mesures actualisées à l’ouverture. Utilise ↻ pour les rafraîchir.", FpsUi.Small, FpsUi.Dim);
            _contextNote.AutoSize = false; _statsRow.Controls.Add(_contextNote);
            _openHardware = OnyxDesign.Button("Voir mon matériel  →");
            _openHardware.Click += (s, e) => Host.Goto(7); _statsRow.Controls.Add(_openHardware);

            // --- État vivant : le Copilote dit ce qu'il fait (prêt / analyse / bilan). ---
            _status = new Label
            {
                AutoSize = false, Font = FpsUi.Small, BackColor = Color.Transparent,
                ForeColor = FpsUi.Dim, TextAlign = ContentAlignment.TopRight, Text = ""
            };
            Controls.Add(_status);
            SetStatus(StatusKind.Ready);

            // --- Barre de saisie « pro » : carte arrondie dont le liseré s'allume au focus. ---
            _inputBar = new BufferedPanel { Name = "copilot-composer", BackColor = Color.Transparent };
            _inputBar.Paint += (s, e) =>
            {
                bool focus = _input != null && _input.Focused;
                OnyxDesign.PaintCard(e.Graphics, _inputBar.ClientRectangle, FpsUi.Card,
                    focus ? FpsUi.Gold : FpsUi.Border, 12f);
            };
            Controls.Add(_inputBar);
            _input = new TextBox { Name = "copilot-input", Multiline = true, AcceptsReturn = true, BorderStyle = BorderStyle.None,
                BackColor = FpsUi.Card, ForeColor = FpsUi.Ink, Font = MessageFont, ScrollBars = ScrollBars.None };
            _input.AccessibleName = "Ton message au Copilote";
            try { _input.PlaceholderText = "Décris ce qui te gêne ou pose ta question…"; } catch { }
            _input.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; SendInput(); } };
            _input.GotFocus += (s, e) => { try { _inputBar.Invalidate(); } catch { } };
            _input.LostFocus += (s, e) => { try { _inputBar.Invalidate(); } catch { } };
            _inputBar.Controls.Add(_input);
            _inputPlaceholder = FpsUi.Text("Décris ce qui te gêne ou pose ta question…", MessageFont, FpsUi.Dim);
            _inputPlaceholder.AutoSize = false; _inputPlaceholder.BackColor = FpsUi.Card;
            _inputPlaceholder.Click += (s, e) => _input.Focus();
            _inputBar.Controls.Add(_inputPlaceholder);
            _inputPlaceholder.Cursor = Cursors.IBeam;
            _inputPlaceholder.BringToFront();
            _send = OnyxDesign.PrimaryButton("Envoyer  ↑");
            _send.Name = "copilot-send"; _send.Enabled = false;
            _input.TextChanged += (s, e) =>
            {
                _send.Enabled = !string.IsNullOrWhiteSpace(_input.Text);
                _inputPlaceholder.Visible = _input.Text.Length == 0;
            };
            _send.Click += (s, e) => SendInput();
            _inputBar.Controls.Add(_send);
            _inputHint = FpsUi.Text("Entrée : envoyer · Maj+Entrée : nouvelle ligne", FpsUi.Tiny, FpsUi.Dim2);
            _inputHint.AutoSize = false; _inputBar.Controls.Add(_inputHint);

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
            foreach (int index in new[] { 0, 4, 5 })
            {
                var qd = qs[index]; var chip = Chip(qd.Item1); string text = qd.Item2;
                chip.Click += (s, e) => Send(text); _quick.Controls.Add(chip);
            }
            _topics.BackColor = FpsUi.Card; _topics.ForeColor = FpsUi.Ink; _topics.Font = FpsUi.Body;
            var quickMenu = new ToolStripMenuItem("Actions rapides");
            foreach (var qd in qs) { string text = qd.Item2; quickMenu.DropDownItems.Add(qd.Item1, null, (s, e) => Send(text)); }
            _topics.Items.Add(quickMenu);
            foreach (var group in StarterGroups)
            {
                var menu = new ToolStripMenuItem(group.Title);
                foreach (var starter in group.Items) { string text = starter.Item2; menu.DropDownItems.Add(starter.Item1, null, (s, e) => Send(text)); }
                _topics.Items.Add(menu);
            }
            var more = Chip("Tous les sujets  ⌄"); more.Name = "copilot-topics";
            more.Click += (s, e) => _topics.Show(more, new Point(0, more.Height));
            _quick.Controls.Add(more); Controls.Add(_quick);

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
                            ? "●  " + _statusCauses + (_statusCauses == 1 ? " cause identifiée" : " causes identifiées")
                            : "●  réponse prête";
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
            _hero = new BufferedPanel { Name = "copilot-welcome", BackColor = FpsUi.BgMain };
            _hero.Paint += OnPaintHero; Controls.Add(_hero);
            _heroChips = new FlowLayoutPanel { WrapContents = true, BackColor = Color.Transparent };
            _hero.Controls.Add(_heroChips);
            AddStarter(NavIcons.Jeux, "Retrouver de la fluidité", "FPS bas, saccades, latence en jeu", "ça rame et ça saccade en jeu");
            AddStarter(NavIcons.Systeme, "Comprendre mon PC", "CPU, GPU et ralentissements", "c'est mon cpu ou mon gpu qui me limite ?");
            AddStarter(NavIcons.Optimisations, "Résoudre le lag", "Ping élevé et connexion instable", "ça lag en ligne, ping élevé");
            AddStarter(NavIcons.Stockage, "Faire de la place", "Disques, caches et applications", "libérer de l'espace disque");
        }
        private void AddStarter(int icon, string title, string subtitle, string query)
        {
            var card = new StarterCard(icon, title, subtitle);
            card.Click += (s, e) => Send(query); _heroChips.Controls.Add(card);
        }
        private void OnPaintHero(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; int w = _hero.Width;
            OnyxDesign.PaintCard(g, _hero.ClientRectangle, Color.FromArgb(23, 22, 19), OnyxDesign.Edge, 14);
            Logo.Draw(g, new RectangleF(24, 24, 42, 42), FpsUi.Gold, false);
            OnyxDesign.Text(g, "ASSISTANT ONYX", FpsUi.Small, FpsUi.Gold, new Rectangle(80, 26, w - 104, 21));
            OnyxDesign.Text(g, "Mesures · explications · actions", FpsUi.Small, FpsUi.Dim, new Rectangle(80, 49, w - 104, 20));
            OnyxDesign.Text(g, "Qu’est-ce qui te ralentit ?", HeroTitle, FpsUi.Ink, new Rectangle(23, 88, w - 46, 42));
            OnyxDesign.Text(g, "Décris ton problème. Je t’aide à le comprendre et à choisir la suite.",
                MessageFont, FpsUi.Dim, new Rectangle(24, 137, w - 48, 42), true);
        }
        private void LayoutHero()
        {
            if (_hero == null || _heroChips == null) return;
            _heroChips.SetBounds(24, 190, _hero.Width - 48, 200);
            int width = (_heroChips.Width - 12) / 2;
            int index = 0;
            foreach (Control card in _heroChips.Controls)
            {
                card.Size = new Size(width, 88);
                card.Margin = new Padding(0, 0, ++index % 2 == 0 ? 0 : 12, 12);
            }
            _hero.Invalidate();
        }

        private sealed class StarterCard : Button
        {
            private readonly int _icon;
            private readonly string _subtitle;
            private bool _hot;
            public StarterCard(int icon, string title, string subtitle)
            {
                _icon = icon; Text = title; _subtitle = subtitle; AccessibleName = title; AccessibleDescription = subtitle;
                BackColor = FpsUi.Card; Cursor = Cursors.Hand; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                MouseEnter += (s, e) => { _hot = true; Invalidate(); }; MouseLeave += (s, e) => { _hot = false; Invalidate(); };
                GotFocus += (s, e) => Invalidate(); LostFocus += (s, e) => Invalidate();
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(Color.FromArgb(23, 22, 19));
                bool active = _hot || (Focused && ShowFocusCues);
                OnyxDesign.PaintCard(g, ClientRectangle, active ? FpsUi.CardHi : FpsUi.Card, active ? FpsUi.GoldDim : FpsUi.Border, 10);
                NavIcons.Draw(g, _icon, new RectangleF(15, 17, 23, 23), FpsUi.Gold);
                OnyxDesign.Text(g, Text, FpsUi.H3, FpsUi.Ink, new Rectangle(48, 17, Width - 62, 23));
                OnyxDesign.Text(g, _subtitle, FpsUi.Small, FpsUi.Dim, new Rectangle(15, 53, Width - 30, 23));
            }
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
        private Label StatTile(string title, int icon)
        {
            var tile = new BufferedPanel { BackColor = Color.Transparent, Tag = "stat" };
            tile.Paint += (s, e) =>
            {
                OnyxDesign.PaintCard(e.Graphics, tile.ClientRectangle, FpsUi.Card, FpsUi.Border, 10);
                NavIcons.Draw(e.Graphics, icon, new RectangleF(tile.Width - 39, 26, 22, 22), FpsUi.GoldDim);
            };
            var cap = FpsUi.Text(title, FpsUi.Tiny, FpsUi.Dim);
            cap.SetBounds(14, 12, 160, 18); cap.AutoSize = false;
            var value = FpsUi.Text("—", FpsUi.F(18, true), FpsUi.Dim);
            value.AutoSize = false; value.SetBounds(12, 31, 160, 32);
            tile.Controls.Add(cap); tile.Controls.Add(value); _statsRow.Controls.Add(tile);
            return value;
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
            var reply = DocAssistant.SafeAnswer(q, _stats, Host.Log, _last);
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
                        if (a.IsChange && !failed) { try { Journal.Add(a.Label); } catch { } }   // journal de bord
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
                // Le seuil est celui que l'utilisateur a réglé dans Stockage → Configurer (0 = jamais).
                int alert = 12;
                try { alert = StorageSettings.Load().AlertPct; } catch { }
                if (alert > 0 && pct < alert)
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

        // Outils TRIÉS par familles — l'ordre suit la vraie vie : le problème en jeu d'abord,
        // la santé du PC ensuite, l'entretien, puis les bonus. Chaque pastille envoie une
        // phrase déjà couverte par le harnais (aucune logique nouvelle ici, que du rangement).
        private static readonly (string Title, (string, string)[] Items)[] StarterGroups =
        {
            ("🎮 Problèmes en jeu", new[]
            {
                ("Ça rame en jeu", "ça rame et ça saccade en jeu"),
                ("FPS bas", "mes fps sont bas"),
                ("Ping / lag en ligne", "ça lag en ligne, ping élevé"),
                ("CPU ou GPU : qui me limite ?", "c'est mon cpu ou mon gpu qui me limite ?"),
                ("Ça crash / écran bleu", "mes jeux crashent, parfois ecran bleu"),
                ("Un jeu ne démarre pas", "un jeu refuse de démarrer, dll manquante"),
                ("Écran bloqué à 60 Hz", "mon écran semble bloqué à 60 hz"),
            }),
            ("🖥️ PC & Windows", new[]
            {
                ("Fenêtres qui saccadent", "mes fenetres windows saccadent"),
                ("Le PC chauffe", "le pc ou le gpu chauffe et bride"),
                ("PC lent à s'allumer", "mon pc est long a demarrer, trop de programmes au boot"),
                ("Qui bouffe mon CPU ?", "quel programme consomme mon cpu en fond"),
                ("Plus de son", "je n'ai plus de son"),
                ("Plus d'internet", "je n'ai plus d'internet, pas de connexion"),
                ("Réparer Windows", "répare windows, fichiers système corrompus"),
            }),
            ("🧰 Entretien & espace", new[]
            {
                ("Bilan complet du PC", "fais un bilan complet de mon pc"),
                ("Bilan mises à jour", "fais le bilan des mises à jour"),
                ("Libérer de l'espace", "libérer de l'espace disque"),
                ("Applis les plus lourdes", "quelles applications prennent le plus de place"),
                ("Mes plus gros fichiers", "quels sont mes plus gros fichiers"),
                ("Hibernation : récupérer des Go", "désactive l'hibernation"),
                ("Solutions gratuites", "trouve des solutions gratuites pour booster mon pc"),
            }),
            ("🎁 Bonus", new[]
            {
                ("Jeux gratuits PC", "jeux gratuits"),
                ("Actus gaming", "actu jeux vidéo"),
            }),
        };

        // Liste à plat (pastilles affichées SOUS une réponse) : même contenu, sans les en-têtes.
        private static readonly (string, string)[] Starters = Flatten(StarterGroups);
        private static (string, string)[] Flatten((string Title, (string, string)[] Items)[] groups)
        {
            var all = new System.Collections.Generic.List<(string, string)>();
            foreach (var g in groups) all.AddRange(g.Items);
            return all.ToArray();
        }

        private const int AV = 36, GAP = 10;
        private const int ColumnW = 860;                                   // largeur maxi de la conversation
        private static readonly Color BubbleDoc = FpsUi.Card;
        private static readonly Color BubbleUser = FpsUi.BlendGold(FpsUi.Card, 0.1f);

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
            int maxTextW = MessageWidth();

            // Bulle auto-dimensionnée : Copilote carbone / Toi bronze, coins arrondis asymétriques
            // (le coin proche de l'émetteur est serré : la bulle « pointe » vers qui parle).
            Color bg = doc ? BubbleDoc : BubbleUser;
            Color bord = doc ? FpsUi.Border : Color.FromArgb(120, FpsUi.GoldDim);
            var bubble = new BufferedPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = bg, Padding = new Padding(16, 12, 18, 14), Margin = new Padding(0) };
            bubble.SizeChanged += (s, e) =>
            {
                try
                {
                    using (var p = doc ? RoundAsym(bubble.ClientRectangle, 5, 14, 14, 14)
                                       : RoundAsym(bubble.ClientRectangle, 14, 5, 14, 14))
                    {
                        var oldRegion = bubble.Region; bubble.Region = new Region(p);
                        if (oldRegion != null) oldRegion.Dispose();
                    }
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

            var col = new FlowLayoutPanel { Location = new Point(16, 12), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            // En-tête : qui parle + l'heure (repère utile quand la conversation s'allonge).
            // Le nom du Copilote se grave en Marcellus or — c'est sa signature.
            col.Controls.Add(new Label
            {
                AutoSize = true, Font = doc ? NameFont : FpsUi.Small,
                ForeColor = doc ? FpsUi.Gold : FpsUi.Ink,
                // Même marge gauche que le corps du message (les Label d'un FlowLayoutPanel
                // ont 3 px par défaut) : sans ça l'en-tête débordait de 3 px vers la gauche
                // et la première lettre passait sous l'arrondi de la bulle.
                BackColor = Color.Transparent, Margin = new Padding(3, 0, 3, 5),
                Text = (doc ? "Copilote" : "Toi") + "   " + DateTime.Now.ToString("HH:mm")
            });
            var bodyLbl = new Label { AutoSize = true, MaximumSize = new Size(maxTextW, 0), Font = MessageFont, ForeColor = FpsUi.Ink, BackColor = Color.Transparent, Text = body ?? "" };
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
                var ex = OnyxDesign.Button("📄  Enregistrer ce diagnostic (Bureau)");
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
                var btn = OnyxDesign.AccentButton("Ouvrir « " + reply.Tool.Tool + " »  →");
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
                bubble.Disposed += (s, e) => cms.Dispose();
                if (doc)
                {
                    var copy = OnyxDesign.Button("Copier la réponse");
                    copy.Name = "copilot-copy"; copy.Tag = "compact";
                    copy.Size = new Size(150, 28); copy.Margin = new Padding(0, 12, 0, 0);
                    copy.Click += (s, e) => ((ToolStripMenuItem)cms.Items[0]).PerformClick();
                    col.Controls.Add(copy); lateKids.Add(copy);
                }
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
                    row.Margin = new Padding(doc ? ColLeft() : Math.Max(6, _flow.ClientSize.Width - rw - ColLeft() - 22), 7, 10, 7);
                }
            };
            bubble.SizeChanged += (s, e) => { try { sync(); } catch { } };
            _messageLayouts[row] = width =>
            {
                col.SuspendLayout();
                foreach (Control child in col.Controls)
                {
                    if (child is Label || child is FlowLayoutPanel) child.MaximumSize = new Size(width, 0);
                    else if (child is Button) child.Width = Math.Min(width, (string)child.Tag == "compact" ? 150 : 380);
                    else if ((string)child.Tag == "cause") child.Width = Math.Min(width, 560);
                }
                col.ResumeLayout(true); bubble.PerformLayout(); sync();
            };
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
                bubble.Disposed += (s, e) => { try { writer.Stop(); writer.Dispose(); } catch { } };
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
            var go = OnyxDesign.AccentButton("▶  " + act.Label);
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
            Color accent = cd.Impact >= 85 ? FpsUi.Err : cd.Impact >= 65 ? Color.FromArgb(255, 152, 64) : FpsUi.Warn;
            string level = cd.Impact >= 85 ? "CRITIQUE" : cd.Impact >= 65 ? "ÉLEVÉ" : "MOYEN";
            var panel = new BufferedPanel { Tag = "cause", Width = Math.Min(maxW, 560), BackColor = Color.Transparent, Margin = new Padding(0, 12, 0, 0) };
            panel.Paint += (s, e) =>
            {
                OnyxDesign.PaintCard(e.Graphics, panel.ClientRectangle, FpsUi.CardHi, FpsUi.Border, 10);
                using (var brush = new SolidBrush(accent)) e.Graphics.FillRectangle(brush, 1, 12, 3, panel.Height - 24);
            };
            var pill = FpsUi.Text("IMPACT " + cd.Impact + "  ·  " + level, FpsUi.Tiny, accent);
            pill.AutoSize = false; panel.Controls.Add(pill);
            var body = new Label { AutoSize = true, Font = MessageFont, ForeColor = FpsUi.Ink, BackColor = Color.Transparent, Text = cd.Text ?? "" };
            panel.Controls.Add(body);
            Button action = null; Label warning = null;
            if (cd.Fix != null)
            {
                var fix = cd.Fix;
                action = OnyxDesign.AccentButton(fix.Label + "  →");
                action.Click += (s, e) => { action.Enabled = false; action.Text = "En cours…"; RunAction(fix, action); };
                panel.Controls.Add(action);
                if (!string.IsNullOrEmpty(fix.Warning))
                {
                    warning = new Label { AutoSize = true, Font = FpsUi.Small, ForeColor = FpsUi.Dim, BackColor = Color.Transparent, Text = fix.Warning };
                    panel.Controls.Add(warning);
                }
            }
            bool arranging = false;
            Action layout = () =>
            {
                if (arranging) return; arranging = true;
                try
                {
                    int width = Math.Max(160, panel.Width - 32);
                    pill.SetBounds(16, 12, width, 18);
                    body.Location = new Point(16, 38); body.MaximumSize = new Size(width, 0);
                    int y = 38 + body.PreferredSize.Height;
                    if (action != null) { action.SetBounds(16, y + 14, width, 36); y = action.Bottom; }
                    if (warning != null) { warning.Location = new Point(16, y + 8); warning.MaximumSize = new Size(width, 0); y = warning.Top + warning.PreferredSize.Height; }
                    panel.Height = y + 16;
                }
                finally { arranging = false; }
            };
            panel.SizeChanged += (s, e) => layout(); layout();
            return panel;
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
        private int MessageWidth()
        {
            return Math.Min(620, Math.Max(220, _flow.ClientSize.Width - AV - GAP - 64));
        }
        private void RealignUserRows()
        {
            if (_flow == null || _reflowing) return;
            _reflowing = true;
            _flow.SuspendLayout();
            try
            {
                int flowW = _flow.ClientSize.Width, left = ColLeft(), width = MessageWidth();
                foreach (Control row in _flow.Controls)
                {
                    Action<int> layout;
                    if (_messageLayouts.TryGetValue(row, out layout)) layout(width);
                    var margin = row.Margin;
                    margin.Left = (string)row.Tag == "user" ? Math.Max(6, flowW - row.Width - left - 22) : left;
                    row.Margin = margin;
                }
            }
            finally { _flow.ResumeLayout(true); _reflowing = false; }
        }

        private static Button Chip(string text)
        {
            var button = OnyxDesign.Button(text);
            button.Size = new Size(TextRenderer.MeasureText(text, FpsUi.Small).Width + 24, 30);
            button.Margin = new Padding(0, 0, 8, 6);
            return button;
        }

        private void DoLayout()
        {
            if (_flow == null || _statsRow == null) return;
            int width = ClientSize.Width, height = ClientSize.Height, side = width >= 1200 ? 244 : 220;
            int main = width - 68 - side - 18;
            _status.SetBounds(width - 34 - side, 28, side, 24);
            _statsRow.SetBounds(34 + main + 18, 96, side, Math.Max(480, height - 114));
            int y = 80;
            foreach (Control tile in _statsRow.Controls)
                if ((string)tile.Tag == "stat") { tile.SetBounds(14, y, side - 28, 74); y += 86; }
            _refresh.SetBounds(side - 48, 14, 32, 32);
            _contextNote.SetBounds(18, 430, side - 36, 54);
            _openHardware.SetBounds(14, _statsRow.Height - 48, side - 28, 34);
            int inputY = height - 122;
            _inputBar.SetBounds(34, inputY, main, 104);
            _input.SetBounds(16, 14, main - 32, 37);
            _inputPlaceholder.SetBounds(16, 14, main - 32, 37);
            _send.SetBounds(main - 122, 60, 106, 32);
            _inputHint.SetBounds(16, 71, main - 154, 20);
            _quick.SetBounds(34, inputY - 40, main, 34);
            _flow.SetBounds(34, 96, main, Math.Max(120, inputY - 148));
            if (_hero != null) { _hero.SetBounds(34, 96, main, Math.Max(398, _flow.Height)); LayoutHero(); }
            RealignUserRows();
        }

#if BTTEST
        private bool _previewActionRan;
        internal void PreviewConversation()
        {
            AddBubble(false, "Comment savoir si le problème vient du CPU ou du GPU ?", null);
            var reply = new DocAssistant.Reply
            {
                Text = "Commence par observer l’utilisation du CPU et du GPU pendant la même scène de jeu. Compare ensuite les FPS et les temps de rendu avant et après un seul changement.\n\nLes mesures du PC donnent du contexte : elles ne remplacent pas un test dans le jeu concerné.",
                Footer = "Aperçu de conversation pour vérifier la mise en page. Aucun réglage n’est appliqué.",
                Cards = new List<DocAssistant.Card>
                {
                    new DocAssistant.Card
                    {
                        Impact = 70,
                        Text = "Exemple de carte de diagnostic avec une explication longue : le texte, le bouton et la description de l’action doivent rester lisibles lorsque la fenêtre devient plus étroite.",
                        Fix = new DocAssistant.ChatAction
                        {
                            Label = "Vérifier les réglages proposés pour ce diagnostic",
                            Warning = "Action fictive du test d’interface, sans modification du système.",
                            IsChange = true,
                            Run = log => { _previewActionRan = true; return null; }
                        }
                    }
                }
            };
            AddBubble(true, reply.Text, reply);
            RealignUserRows();
        }
        internal void PreviewConversationTop() { _flow.AutoScrollPosition = Point.Empty; }
        internal void PreviewConversationBottom() { _flow.AutoScrollPosition = new Point(0, 100000); }
        internal void ValidateCopilotUi()
        {
            DoLayout();
            int messages = _flow.Controls.Count;
            _input.Text = "   "; SendInput();
            if (_flow.Controls.Count != messages || _send.Enabled) throw new InvalidOperationException("Envoi vide autorisé.");
            _input.Text = "Première ligne\r\nDeuxième ligne";
            if (!_send.Enabled || !_input.Multiline || _input.Lines.Length != 2) throw new InvalidOperationException("Saisie multiligne incorrecte.");
            _input.Clear();
            if (!_inputPlaceholder.Visible || _inputBar.GetChildAtPoint(new Point(20, 20)) != _inputPlaceholder)
                throw new InvalidOperationException("Invitation de saisie masquée.");
            if (_inputBar.Right >= _statsRow.Left || _inputBar.Bottom > Height || _quick.Bottom > _inputBar.Top)
                throw new InvalidOperationException("Zones du Copilote superposées.");
            if (_hero != null && (_hero.Bottom > _quick.Top || _heroChips.Bottom > _hero.Height))
                throw new InvalidOperationException("Accueil du Copilote rogné.");
            foreach (Control row in _flow.Controls)
                if (row.Width + row.Margin.Horizontal > _flow.ClientSize.Width)
                    throw new InvalidOperationException("Message trop large : " + row.Width + " / " + _flow.ClientSize.Width);
            if (_previewActionRan) throw new InvalidOperationException("Une action a été exécutée sans clic.");
        }
#endif

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
            if (disposing) { _topics.Dispose(); _messageLayouts.Clear(); try { HideTyping(); } catch { } if (_wheel != null) { try { Application.RemoveMessageFilter(_wheel); } catch { } _wheel = null; } }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintPremiumTitle(e.Graphics, "COPILOTE", "Comprendre ton PC. Trouver la bonne action.");
        }
    }
}
