using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// L'ÉCRAN DE L'INVENTAIRE — CE QUI TOURNE ICI, CE QUE ÇA COÛTE, ET CE QU'ON PEUT EN FAIRE.
    ///
    /// Il n'y a AUCUN bouton « tout optimiser ». Le tableau montre d'abord, et rien n'est coché
    /// tant qu'on n'a pas demandé une sélection — parce qu'un bouton unique qui applique une liste
    /// invisible est exactement ce qui a laissé des machines sans page « Marche/Arrêt ».
    ///
    /// TROIS CHOSES QUE 318 LIGNES EXIGENT, ET QU'UN SIMPLE TABLEAU NE DONNE PAS :
    ///
    ///   UNE RECHERCHE. Chercher « Corsair » dans 318 lignes à la molette n'est pas une lecture,
    ///   c'est une fouille. Le filtre porte sur le nom, le libellé ET l'éditeur — on cherche
    ///   rarement le nom court d'un service, on cherche le logiciel qui l'a installé.
    ///
    ///   DES FILTRES QUI DISENT CE QU'ILS MONTRENT. « Arrêtables » ne veut pas dire « suspendables »
    ///   mais « suspendables ET en train de consommer » : c'est la règle 2 rendue cliquable.
    ///
    ///   LE GAIN DANS LE BOUTON. « Suspendre pour la partie » affiche le nombre de services cochés
    ///   et les mégaoctets qu'ils occupent VRAIMENT. Un bouton qui ne chiffre pas son effet laisse
    ///   croire que l'effet est grand.
    ///
    /// Et une phrase que les optimiseurs taisent : suspendre ne tient QUE jusqu'au redémarrage. Un
    /// service en démarrage automatique reviendra au prochain allumage. C'est une qualité — rien
    /// ne se dégrade en silence — mais il faut le dire, sinon on la prend pour une panne.
    ///
    /// Les lignes VITALES sont affichées, grisées, sans case cochable — les cacher laisserait croire
    /// qu'ONYX ne les a pas vues, alors qu'il les protège.
    /// </summary>
    internal sealed class InventaireServicesForm : Form
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int sz);

        private readonly Action<string, int> _log;
        private readonly ActionsServices _actions = new ActionsServices();

        private ListView _liste;
        private TextBox _recherche;
        private Panel _stats, _detail, _entete, _titres;
        private ToggleSwitch _auto;
        private Button _suspendre, _desactiver, _restaurer, _mesurer, _cocher, _fermer;
        private readonly List<Button> _puces = new List<Button>();

        private List<InventaireServices.Service> _tous = new List<InventaireServices.Service>();
        private InventaireServices.Service _selection;
        private int _survol = -1;
        private bool _silence;          // coche en série : on ne rafraîchit qu'une fois
        private bool _cpuMesure;

        /// <summary>Ce que montre le tableau. Chaque valeur est une PHRASE tenable, pas une
        /// catégorie technique : « arrêtables » exclut ce qui est déjà à l'arrêt.</summary>
        private enum Filtre { Tout, Arretables, Tiers, QuiTourne }
        private Filtre _filtre = Filtre.Tout;

        public InventaireServicesForm(Action<string, int> log)
        {
            _log = log ?? delegate (string m, int n) { };
            Construit();
            Relit(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int v = 1; DwmSetWindowAttribute(Handle, 20, ref v, 4); } catch { }   // barre de titre sombre
        }

        // ==================================================================
        //  Construction
        // ==================================================================

        private const int Marge = 22;

        private void Construit()
        {
            Text = "ONYX — inventaire des services";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1180, 760);
            MinimumSize = new Size(980, 620);
            BackColor = FpsUi.BgMain;
            Font = FpsUi.Body;
            DoubleBuffered = true;
            try { Icon = Logo.MakeIcon(32, FpsUi.Gold); } catch { }

            var pied = ConstruitPied();
            var detail = ConstruitDetail();
            var corps = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Marge, 0, Marge, 10), BackColor = Color.Transparent };

            _liste = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true,
                // AUCUN en-tête natif : même en OwnerDraw, Windows continue d'encadrer la bande
                // de titres avec son propre relief, et ce relief est le dernier morceau de l'écran
                // qui a l'air d'avoir vingt ans. On le remplace par une bande peinte (BandeTitres).
                HeaderStyle = ColumnHeaderStyle.None, BorderStyle = BorderStyle.None,
                OwnerDraw = true, MultiSelect = false, BackColor = FpsUi.Card, ForeColor = FpsUi.Ink,
                Font = FpsUi.Body
            };
            // Hauteur de ligne : une liste native se cale sur la police. Deux lignes par service
            // (nom + éditeur) demandent de la place, et le seul levier est une image fantôme.
            _liste.SmallImageList = new ImageList { ImageSize = new Size(1, 44) };
            _liste.Columns.Add("SERVICE", 330);
            _liste.Columns.Add("ÉTAT", 130);
            _liste.Columns.Add("COÛT MESURÉ", 200);
            _liste.Columns.Add("VERDICT", 130);
            _liste.Columns.Add("POURQUOI", 300);
            _liste.DrawItem += DessineLigne;
            _liste.DrawSubItem += delegate (object s, DrawListViewSubItemEventArgs e) { e.DrawDefault = false; };
            _liste.ItemCheck += SurCoche;
            _liste.SelectedIndexChanged += delegate { _selection = Choisi(); _detail.Invalidate(); };
            _liste.MouseMove += SurSurvol;
            _liste.MouseLeave += delegate { Survole(-1); };
            _liste.Resize += delegate { AjusteColonnes(); };
            corps.Controls.Add(_liste);
            corps.Controls.Add(BandeTitres());

            Controls.Add(corps);
            Controls.Add(detail);
            Controls.Add(pied);
            Controls.Add(ConstruitBarre());
            Controls.Add(ConstruitStats());
            Controls.Add(ConstruitTitre());
        }

        private Panel ConstruitTitre()
        {
            _entete = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Color.Transparent };
            _entete.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                FpsUi.DrawTracked(g, "INVENTAIRE DES SERVICES", FpsUi.H2, FpsUi.Gold, Marge, 42, 2.4f);
                // Le nombre vient de la MACHINE. Écrit en dur, il serait juste ici et faux partout
                // ailleurs — exactement le défaut que ce module reproche aux listes recopiées.
                string sous = _nTotal == 0
                    ? "Lecture des services…"
                    : _nTotal + " services installés, et seuls ceux qui consomment vraiment méritent ton attention.";
                TextRenderer.DrawText(g, sous,
                    FpsUi.Small, new Point(Marge, 60), FpsUi.Dim2, TextFormatFlags.NoPadding);
                // Le libellé de l'interrupteur est peint À DROITE de l'écran, aligné sur lui :
                // « automatique » sans dire QUOI ni QUAND est une case qu'on coche sans savoir.
                int xd = _entete.Width - Marge - 52;
                TextRenderer.DrawText(g, "MODE JEU AUTOMATIQUE", FpsUi.Tiny,
                    new Rectangle(xd - 320, 30, 312, 14), _auto != null && _auto.On ? FpsUi.Gold : FpsUi.Dim2,
                    TextFormatFlags.Right | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g,
                    _auto != null && _auto.On
                        ? "suspend dès qu'un jeu démarre, relance quand il se ferme"
                        : "tout reste manuel : ONYX ne suspend rien tout seul",
                    FpsUi.Tiny, new Rectangle(xd - 380, 46, 372, 14), FpsUi.Dim2,
                    TextFormatFlags.Right | TextFormatFlags.NoPadding);

                using (var p = new Pen(FpsUi.Border))
                    g.DrawLine(p, Marge, _entete.Height - 1, _entete.Width - Marge, _entete.Height - 1);
            };

            _auto = new ToggleSwitch { On = AutoJeu.Actif, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _auto.Location = new Point(_entete.Width - Marge - 46, 32);
            _auto.Toggled += delegate
            {
                AutoJeu.Actif = _auto.On;
                _entete.Invalidate();
                _log(_auto.On
                    ? "MODE JEU AUTO activé : les services de fond seront suspendus dès qu'un jeu démarre, "
                      + "et relancés quand il se ferme."
                    : "MODE JEU AUTO désactivé : plus aucune suspension automatique.", 0);
            };
            _entete.Controls.Add(_auto);
            _entete.Resize += delegate { _auto.Location = new Point(_entete.Width - Marge - 46, 32); };
            return _entete;
        }

        // ------------------------------------------------------------------ les quatre chiffres

        private int _nTotal, _nActifs, _nTiers, _nArretables;
        private double _moArretables;

        private Panel ConstruitStats()
        {
            _stats = new Panel { Dock = DockStyle.Top, Height = 104, Padding = new Padding(Marge, 16, Marge, 12), BackColor = Color.Transparent };
            _stats.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle z = _stats.ClientRectangle;
                z = new Rectangle(Marge, 16, Math.Max(40, z.Width - Marge * 2), z.Height - 28);
                const int ecart = 14;
                int l = (z.Width - ecart * 3) / 4;

                Tuile(g, new Rectangle(z.X, z.Y, l, z.Height), _nTotal.ToString(), "services installés", FpsUi.Ink);
                Tuile(g, new Rectangle(z.X + (l + ecart), z.Y, l, z.Height), _nActifs.ToString(), "en cours d'exécution", FpsUi.Ink);
                Tuile(g, new Rectangle(z.X + (l + ecart) * 2, z.Y, l, z.Height), _nTiers.ToString(), "venus de logiciels tiers", FpsUi.Ink);
                // Le seul chiffre en or est le seul sur lequel on peut agir. Mettre les 318 en
                // avant ferait croire qu'il y a 318 gestes à faire.
                Tuile(g, new Rectangle(z.X + (l + ecart) * 3, z.Y, l, z.Height),
                    _nArretables == 0 ? "—" : Math.Round(_moArretables).ToString("0") + " Mo",
                    _nArretables == 0 ? "rien à récupérer" : "récupérables sur " + _nArretables + " service(s)", FpsUi.Gold);
            };
            return _stats;
        }

        private static void Tuile(Graphics g, Rectangle r, string chiffre, string libelle, Color teinte)
        {
            FpsUi.PaintCard(g, r, FpsUi.Card, FpsUi.Border, 10f);
            TextRenderer.DrawText(g, chiffre, FpsUi.Num, new Point(r.X + 16, r.Y + 8), teinte, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, libelle, FpsUi.Tiny, new Point(r.X + 17, r.Y + 40), FpsUi.Dim2, TextFormatFlags.NoPadding);
        }

        // ------------------------------------------------------------------ recherche et filtres

        private Panel ConstruitBarre()
        {
            var barre = new Panel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(Marge, 8, Marge, 12), BackColor = Color.Transparent };

            var champ = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = Color.Transparent, Padding = new Padding(30, 8, 10, 8) };
            champ.Paint += delegate (object s, PaintEventArgs e)
            {
                FpsUi.PaintCard(e.Graphics, ((Panel)s).ClientRectangle, FpsUi.Card, FpsUi.Border, 8f);
                TextRenderer.DrawText(e.Graphics, "⌕", FpsUi.F(11f, false), new Point(10, 6), FpsUi.Dim2, TextFormatFlags.NoPadding);
            };
            _recherche = new TextBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = FpsUi.Card,
                ForeColor = FpsUi.Ink, Font = FpsUi.Body, PlaceholderText = "Chercher un service ou un éditeur…"
            };
            _recherche.TextChanged += delegate { Remplit(); };
            champ.Controls.Add(_recherche);
            barre.Controls.Add(champ);

            barre.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 12, BackColor = Color.Transparent });

            // À REBOURS, volontairement : avec Dock.Left, le dernier ajouté se place le plus à
            // gauche. Ajoutées dans l'ordre de lecture, les puces s'affichaient à l'envers.
            Puce("En cours", Filtre.QuiTourne, barre);
            Puce("Logiciels tiers", Filtre.Tiers, barre);
            Puce("Arrêtables", Filtre.Arretables, barre);
            Puce("Tout", Filtre.Tout, barre);

            _cocher = FpsUi.GhostButton("Cocher ce qui est sûr");
            _cocher.Dock = DockStyle.Right; _cocher.Width = 170;
            _cocher.Click += delegate { CocheLesSurs(); };
            barre.Controls.Add(_cocher);

            return barre;
        }

        /// <summary>Une puce de filtre. L'état actif se voit à l'or, pas à un enfoncement discret :
        /// un tableau filtré dont on ne voit pas qu'il est filtré fait croire à des données perdues.</summary>
        private void Puce(string texte, Filtre f, Panel hote)
        {
            var b = FpsUi.GhostButton(texte);
            b.Dock = DockStyle.Left;
            b.Width = TextRenderer.MeasureText(texte, FpsUi.Small).Width + 30;
            b.Margin = new Padding(0);
            b.Tag = f;
            b.Click += delegate { _filtre = f; PeintPuces(); Remplit(); };
            hote.Controls.Add(b);
            hote.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 6, BackColor = Color.Transparent });
            _puces.Add(b);
            PeintPuce(b);
        }

        private void PeintPuces() { foreach (Button b in _puces) PeintPuce(b); }

        private void PeintPuce(Button b)
        {
            bool actif = (Filtre)b.Tag == _filtre;
            b.ForeColor = actif ? FpsUi.Gold : FpsUi.Dim;
            b.BackColor = actif ? FpsUi.BlendGold(FpsUi.Card, 0.13f) : Color.FromArgb(31, 27, 22);
            b.FlatAppearance.BorderSize = actif ? 1 : 0;
            b.FlatAppearance.BorderColor = FpsUi.GoldDim;
        }

        // ------------------------------------------------------------------ détail et actions

        private Panel ConstruitDetail()
        {
            _detail = new Panel { Dock = DockStyle.Bottom, Height = 92, Padding = new Padding(Marge, 6, Marge, 10), BackColor = Color.Transparent };
            _detail.Paint += DessineDetail;
            return _detail;
        }

        private Panel ConstruitPied()
        {
            var pied = new Panel { Dock = DockStyle.Bottom, Height = 76, Padding = new Padding(Marge, 12, Marge, 22), BackColor = Color.Transparent };

            _fermer = FpsUi.GhostButton("Fermer");
            _fermer.Click += delegate { Close(); };

            _mesurer = FpsUi.GhostButton("Mesurer le processeur");
            _mesurer.Click += delegate { Relit(true); };

            _restaurer = FpsUi.GhostButton("Tout restaurer");
            _restaurer.Click += delegate { Restaure(); };

            _desactiver = FpsUi.GhostButton("Désactiver définitivement");
            _desactiver.Click += delegate { Applique(InventaireServices.Geste.Desactiver); };

            _suspendre = FpsUi.GoldButton("Suspendre pour la partie");
            _suspendre.Click += delegate { Applique(InventaireServices.Geste.Suspendre); };

            int[] largeurs = { 92, 190, 150, 210, 250 };
            Button[] boutons = { _fermer, _mesurer, _restaurer, _desactiver, _suspendre };
            for (int i = 0; i < boutons.Length; i++)
            {
                boutons[i].Dock = DockStyle.Right;
                boutons[i].Width = largeurs[i];
                pied.Controls.Add(boutons[i]);
                pied.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 10, BackColor = Color.Transparent });
            }
            return pied;
        }

        // ==================================================================
        //  Données
        // ==================================================================

        /// <summary>
        /// Relit la machine. La première ouverture ne mesure PAS le processeur : attendre une
        /// seconde devant une fenêtre vide pour un chiffre qu'on n'a pas encore demandé serait
        /// payer d'avance. « Mesurer le processeur » ouvre la fenêtre de mesure.
        /// </summary>
        private void Relit(bool avecCpu)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                _tous = InventaireServices.Analyse(avecCpu ? 800 : 0);
                _cpuMesure = avecCpu;
                Compte();
                Remplit();
            }
            catch (Exception ex)
            {
                JournalTechnique.Echec("InventaireServicesForm.Relit", ex);
                MessageBox.Show(this, "Lecture des services impossible : " + ex.Message, "ONYX",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { Cursor = Cursors.Default; }
        }

        private void Compte()
        {
            _nTotal = _tous.Count; _nActifs = 0; _nTiers = 0; _nArretables = 0; _moArretables = 0;
            foreach (InventaireServices.Service s in _tous)
            {
                if (s.EnCours) _nActifs++;
                if (InventaireServices.EstTiers(s.Chemin)) _nTiers++;
                if (InventaireServices.Proposable(s)) { _nArretables++; _moArretables += s.RamMo; }
            }
            _stats.Invalidate();
            _entete.Invalidate();   // le sous-titre porte le total : il change avec lui
            _mesurer.Text = _cpuMesure ? "Mesurer à nouveau" : "Mesurer le processeur";
        }

        /// <summary>PUR (sur l'état de l'écran) : cette ligne passe-t-elle le filtre et la recherche ?</summary>
        private bool Retenu(InventaireServices.Service s)
        {
            switch (_filtre)
            {
                case Filtre.Arretables: if (!InventaireServices.Proposable(s)) return false; break;
                case Filtre.Tiers: if (!InventaireServices.EstTiers(s.Chemin)) return false; break;
                case Filtre.QuiTourne: if (!s.EnCours) return false; break;
            }

            string q = _recherche == null ? "" : _recherche.Text.Trim();
            if (q.Length == 0) return true;
            // On cherche le LOGICIEL, pas le nom court du service : l'éditeur compte autant.
            return s.Nom.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || s.Libelle.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || (s.Editeur ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Remplit()
        {
            _liste.BeginUpdate();
            _silence = true;
            try
            {
                _liste.Items.Clear();
                foreach (InventaireServices.Service s in _tous)
                {
                    if (!Retenu(s)) continue;
                    var it = new ListViewItem(s.Libelle.Length > 0 ? s.Libelle : s.Nom);
                    it.SubItems.Add(""); it.SubItems.Add(""); it.SubItems.Add(""); it.SubItems.Add("");
                    it.Tag = s;
                    _liste.Items.Add(it);
                }
            }
            finally { _silence = false; _liste.EndUpdate(); }

            AjusteColonnes();
            MetAJourBoutons();
        }

        /// <summary>La dernière colonne prend ce qui reste : une barre de défilement horizontale
        /// sur un tableau qu'on lit de gauche à droite est un aveu de mise en page.</summary>
        private void AjusteColonnes()
        {
            if (_liste == null || _liste.Columns.Count < 5) return;
            int fixes = 0;
            for (int i = 0; i < 4; i++) fixes += _liste.Columns[i].Width;
            int reste = _liste.ClientSize.Width - fixes - 4;
            _liste.Columns[4].Width = Math.Max(160, reste);
            if (_titres != null) _titres.Invalidate();   // les titres suivent les colonnes
        }

        private InventaireServices.Service Choisi()
        {
            return _liste.SelectedItems.Count == 0
                ? null : _liste.SelectedItems[0].Tag as InventaireServices.Service;
        }

        // ==================================================================
        //  Dessin
        // ==================================================================

        private static Color Teinte(InventaireServices.Verdict v)
        {
            switch (v)
            {
                case InventaireServices.Verdict.Vital: return FpsUi.Dim2;
                case InventaireServices.Verdict.Inutile: return FpsUi.Ok;
                case InventaireServices.Verdict.Suspendable: return FpsUi.Warn;
                case InventaireServices.Verdict.Inconnu: return FpsUi.Gold;   // la seule à décider
                default: return FpsUi.Dim;
            }
        }

        private static string Mot(InventaireServices.Verdict v)
        {
            switch (v)
            {
                case InventaireServices.Verdict.Vital: return "protégé";
                case InventaireServices.Verdict.Utile: return "utile";
                case InventaireServices.Verdict.Suspendable: return "suspendable";
                case InventaireServices.Verdict.Inutile: return "inutile ici";
                default: return "à toi de voir";
            }
        }

        private static string Etat(InventaireServices.Service s)
        {
            string d = s.Start == 4 ? "désactivé" : s.Start == 2 ? (s.Retarde ? "auto différé" : "automatique") : "manuel";
            return s.EnCours ? d + " · actif" : d + " · arrêté";
        }

        /// <summary>La bande de titres, peinte par nous. Elle vit HORS de la liste : c'est le seul
        /// moyen d'échapper au relief que Windows dessine autour de son en-tête natif.</summary>
        private Panel BandeTitres()
        {
            _titres = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.Transparent };
            _titres.Paint += delegate (object s, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                int[] x = Colonnes(0);
                for (int i = 0; i < 5; i++)
                    TextRenderer.DrawText(g, _liste.Columns[i].Text, FpsUi.Tiny,
                        new Rectangle(x[i] + (i == 0 ? 12 : 10), 0, Math.Max(20, _liste.Columns[i].Width - 14), _titres.Height),
                        FpsUi.Dim2, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                using (var p = new Pen(FpsUi.Border))
                    g.DrawLine(p, 0, _titres.Height - 1, _titres.Width, _titres.Height - 1);
            };
            return _titres;
        }

        private void DessineLigne(object envoyeur, DrawListViewItemEventArgs e)
        {
            var s = e.Item.Tag as InventaireServices.Service;
            if (s == null) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = e.Bounds;

            // Sélection à l'accent, survol à la carte claire (le rôle que FpsUi.CardHi porte
            // partout ailleurs) : deux états distincts, sans jamais deux bleus de Windows.
            bool selectionne = e.Item.Selected;
            Color fond = selectionne ? FpsUi.BlendGold(FpsUi.Card, 0.10f)
                       : (e.ItemIndex == _survol ? FpsUi.CardHi : FpsUi.Card);
            g.FillRectangle(new SolidBrush(fond), r);
            using (var p = new Pen(Color.FromArgb(90, FpsUi.Border)))
                g.DrawLine(p, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);

            // Liseré gauche de la couleur du verdict : la teinte se lit avant le mot, et c'est
            // elle qui fait qu'on trouve les quatre lignes utiles sans lire les 318.
            if (s.Verdict != InventaireServices.Verdict.Utile)
                g.FillRectangle(new SolidBrush(Color.FromArgb(selectionne ? 230 : 150, Teinte(s.Verdict))),
                    new Rectangle(r.X, r.Y + 1, 3, r.Height - 2));

            int[] x = Colonnes(r.X);
            bool proposable = InventaireServices.Proposable(s);

            Case(g, new Rectangle(x[0] + 12, r.Y + r.Height / 2 - 8, 16, 16), e.Item.Checked,
                 s.Verdict == InventaireServices.Verdict.Vital);

            // ---- colonne 1 : le service, et QUI l'a installé
            Color encre = s.Verdict == InventaireServices.Verdict.Vital ? FpsUi.Dim2 : FpsUi.Ink;
            string nom = s.Libelle.Length > 0 ? s.Libelle : s.Nom;
            var zNom = new Rectangle(x[0] + 38, r.Y + 5, _liste.Columns[0].Width - 46, 18);
            TextRenderer.DrawText(g, nom, FpsUi.Body, zNom, encre,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            string sous = (s.Editeur ?? "").Length > 0 ? s.Editeur : s.Nom;
            TextRenderer.DrawText(g, sous, FpsUi.Tiny,
                new Rectangle(zNom.X, r.Y + 24, zNom.Width, 16), FpsUi.Dim2,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            // ---- colonne 2 : état
            TextRenderer.DrawText(g, Etat(s), FpsUi.Small,
                new Rectangle(x[1] + 10, r.Y, _liste.Columns[1].Width - 14, r.Height),
                s.EnCours ? FpsUi.Dim : FpsUi.Dim2,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            // ---- colonne 3 : coût, avec une barre — un chiffre seul ne se compare pas d'un coup d'œil
            var zCout = new Rectangle(x[2] + 10, r.Y, _liste.Columns[2].Width - 16, r.Height);
            if (s.EnCours && s.RamMo >= 1)
            {
                string chiffre = Math.Round(s.RamMo).ToString("0") + " Mo"
                               + (s.Cpu >= 0.5 ? "   ·   " + s.Cpu.ToString("0.#") + " % CPU" : "");
                TextRenderer.DrawText(g, chiffre, FpsUi.Small, new Rectangle(zCout.X, r.Y + 6, zCout.Width, 16),
                    proposable ? FpsUi.Ink : FpsUi.Dim, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                Barre(g, new Rectangle(zCout.X, r.Y + 27, zCout.Width, 5), s.RamMo,
                      proposable ? Teinte(s.Verdict) : FpsUi.Dim2);
                if (s.Partage > 1)
                    TextRenderer.DrawText(g, "estimé · processus partagé", FpsUi.Tiny,
                        new Rectangle(zCout.X, r.Y + 30, zCout.Width, 14), FpsUi.Dim2, TextFormatFlags.NoPadding);
            }
            else
            {
                // Un service ACTIF qui pèse moins d'un mégaoctet n'est pas « à l'arrêt » : le dire
                // serait faux, et c'est le genre de faux détail qui décrédibilise tout le tableau.
                string rien = s.EnCours ? "actif · moins de 1 Mo"
                            : s.Start == 4 ? "déjà désactivé" : "à l'arrêt · ne coûte rien";
                TextRenderer.DrawText(g, rien, FpsUi.Small,
                    zCout, FpsUi.Dim2, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            // ---- colonne 4 : verdict, en pastille
            Pastille(g, new Rectangle(x[3] + 10, r.Y + r.Height / 2 - 11, _liste.Columns[3].Width - 22, 22),
                     Mot(s.Verdict), Teinte(s.Verdict));

            // ---- colonne 5 : le motif
            TextRenderer.DrawText(g, s.Motif, FpsUi.Small,
                new Rectangle(x[4] + 10, r.Y, Math.Max(40, _liste.Columns[4].Width - 16), r.Height),
                FpsUi.Dim2, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private int[] Colonnes(int gauche)
        {
            var x = new int[5];
            int c = gauche;
            for (int i = 0; i < 5; i++) { x[i] = c; c += _liste.Columns[i].Width; }
            return x;
        }

        /// <summary>Case à cocher maison : la case native de Windows est bleue et carrée, elle
        /// jure avec tout le reste. Verrouillée = barrée, pas seulement grisée — un gris se lit
        /// « pas encore » là où il faut lire « jamais ».</summary>
        private static void Case(Graphics g, Rectangle r, bool cochee, bool verrouillee)
        {
            using (var path = FpsUi.Round(new RectangleF(r.X, r.Y, r.Width, r.Height), 4f))
            {
                g.FillPath(new SolidBrush(cochee ? FpsUi.BlendGold(FpsUi.Card, 0.5f) : Color.FromArgb(26, 23, 19)), path);
                using (var p = new Pen(verrouillee ? Color.FromArgb(60, FpsUi.Dim2) : (cochee ? FpsUi.Gold : FpsUi.Border)))
                    g.DrawPath(p, path);
            }
            if (verrouillee)
            {
                using (var p = new Pen(Color.FromArgb(120, FpsUi.Dim2), 1.4f))
                    g.DrawLine(p, r.X + 4, r.Bottom - 4, r.Right - 4, r.Y + 4);
                return;
            }
            if (!cochee) return;
            using (var p = new Pen(FpsUi.Gold, 1.9f))
            {
                g.DrawLine(p, r.X + 4, r.Y + 8, r.X + 7, r.Y + 11);
                g.DrawLine(p, r.X + 7, r.Y + 11, r.Right - 4, r.Y + 5);
            }
        }

        /// <summary>Barre de coût. L'échelle s'arrête à 120 Mo : au-delà, tout paraîtrait identique
        /// — et c'est entre 5 et 100 Mo que se joue la comparaison qui intéresse.</summary>
        private static void Barre(Graphics g, Rectangle r, double mo, Color teinte)
        {
            using (var fond = FpsUi.Round(new RectangleF(r.X, r.Y, r.Width, r.Height), r.Height / 2f))
                g.FillPath(new SolidBrush(Color.FromArgb(40, FpsUi.Dim2)), fond);
            double part = Math.Min(1.0, mo / 120.0);
            int l = (int)Math.Round(r.Width * part);
            if (l < 3) return;
            using (var barre = FpsUi.Round(new RectangleF(r.X, r.Y, l, r.Height), r.Height / 2f))
                g.FillPath(new SolidBrush(Color.FromArgb(210, teinte)), barre);
        }

        private static void Pastille(Graphics g, Rectangle r, string texte, Color teinte)
        {
            int l = Math.Min(r.Width, TextRenderer.MeasureText(texte, FpsUi.Tiny).Width + 20);
            var z = new Rectangle(r.X, r.Y, l, r.Height);
            using (var path = FpsUi.Round(new RectangleF(z.X, z.Y, z.Width, z.Height), z.Height / 2f))
            {
                g.FillPath(new SolidBrush(Color.FromArgb(34, teinte)), path);
                using (var p = new Pen(Color.FromArgb(110, teinte))) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, texte, FpsUi.Tiny, z, teinte,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void DessineDetail(object envoyeur, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(Marge, 4, Math.Max(40, _detail.Width - Marge * 2), _detail.Height - 16);
            FpsUi.PaintCard(g, r, FpsUi.Card, FpsUi.Border, 10f);

            InventaireServices.Service s = _selection;
            if (s == null)
            {
                TextRenderer.DrawText(g,
                    "Sélectionne une ligne pour lire ce que ce service fait, ce qu'il coûte, et d'où vient son binaire.",
                    FpsUi.Small, new Rectangle(r.X + 18, r.Y, r.Width - 36, r.Height),
                    FpsUi.Dim2, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return;
            }

            TextRenderer.DrawText(g, s.Nom, FpsUi.H3, new Point(r.X + 18, r.Y + 12), FpsUi.Ink, TextFormatFlags.NoPadding);
            int xd = r.X + 22 + TextRenderer.MeasureText(s.Nom, FpsUi.H3).Width;
            Pastille(g, new Rectangle(xd, r.Y + 11, 140, 20), Mot(s.Verdict), Teinte(s.Verdict));

            TextRenderer.DrawText(g, "Arrêt : " + InventaireServices.Gain(s), FpsUi.Small,
                new Rectangle(r.Right - 330, r.Y + 13, 312, 18), FpsUi.Dim,
                TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(g, s.Motif + ".", FpsUi.Small,
                new Rectangle(r.X + 18, r.Y + 36, r.Width - 36, 20), FpsUi.Dim,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            string binaire = InventaireServices.BinaireSeul(s.Chemin);
            if (binaire.Length > 0)
                TextRenderer.DrawText(g, binaire, FpsUi.Tiny,
                    new Rectangle(r.X + 18, r.Y + 58, r.Width - 36, 16), FpsUi.Dim2,
                    TextFormatFlags.NoPadding | TextFormatFlags.PathEllipsis);
        }

        // ==================================================================
        //  Interactions
        // ==================================================================

        private void Survole(int i)
        {
            if (_survol == i) return;
            _survol = i;
            _liste.Invalidate();
        }

        private void SurSurvol(object envoyeur, MouseEventArgs e)
        {
            ListViewItem it = _liste.GetItemAt(e.X, e.Y);
            Survole(it == null ? -1 : it.Index);
        }

        /// <summary>Une ligne VITALE ne se coche pas. Le refus est visible et expliqué : c'est une
        /// réponse, pas un silence.</summary>
        private void SurCoche(object envoyeur, ItemCheckEventArgs e)
        {
            var s = _liste.Items[e.Index].Tag as InventaireServices.Service;
            if (s == null) return;
            if (e.NewValue == CheckState.Checked && s.Verdict == InventaireServices.Verdict.Vital)
            {
                e.NewValue = CheckState.Unchecked;
                _selection = s; _detail.Invalidate();
                return;
            }
            // ItemCheck se déclenche AVANT que la case ne change : relire l'état tout de suite
            // donnerait celui d'avant. On repasse donc après, une seule fois — et jamais pendant
            // une coche en série, sinon on relit le journal une fois par ligne.
            if (!_silence && IsHandleCreated) BeginInvoke((MethodInvoker)MetAJourBoutons);
        }

        /// <summary>Ne coche QUE ce qui rapporte quelque chose et ne casse rien : ni les protégés,
        /// ni les utiles, ni les tiers inconnus, ni ce qui est déjà arrêté (règle 2).</summary>
        private void CocheLesSurs()
        {
            int n = 0;
            _silence = true;
            try
            {
                foreach (ListViewItem it in _liste.Items)
                {
                    var s = it.Tag as InventaireServices.Service;
                    bool sur = s != null && InventaireServices.Proposable(s);
                    it.Checked = sur;
                    if (sur) n++;
                }
            }
            finally { _silence = false; }

            MetAJourBoutons();
            if (n == 0)
                MessageBox.Show(this,
                    "Rien à proposer dans ce que tu regardes : aucun service arrêtable ne consomme quoi que ce soit "
                    + "en ce moment.\n\nC'est un bon résultat, pas une panne.", "ONYX",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private List<InventaireServices.Service> Coches()
        {
            var l = new List<InventaireServices.Service>();
            foreach (ListViewItem it in _liste.Items)
            {
                var s = it.Tag as InventaireServices.Service;
                if (it.Checked && s != null) l.Add(s);
            }
            return l;
        }

        /// <summary>Les boutons chiffrent leur effet. « Suspendre » sans nombre laisse croire à un
        /// grand geste ; « Suspendre (4) · 203 Mo » dit exactement ce qui va se passer.</summary>
        private void MetAJourBoutons()
        {
            List<InventaireServices.Service> c = Coches();
            double mo = 0;
            int inutiles = 0;
            foreach (InventaireServices.Service s in c)
            {
                mo += s.RamMo;
                if (s.Verdict == InventaireServices.Verdict.Inutile) inutiles++;
            }

            _suspendre.Enabled = c.Count > 0;
            _suspendre.Text = c.Count == 0
                ? "Suspendre pour la partie"
                : "Suspendre " + c.Count + " service(s) · " + Math.Round(mo).ToString("0") + " Mo";
            _suspendre.ForeColor = c.Count > 0 ? FpsUi.Gold : FpsUi.Dim2;

            _desactiver.Enabled = inutiles > 0;
            _desactiver.Text = inutiles > 0
                ? "Désactiver définitivement (" + inutiles + ")" : "Désactiver définitivement";
            _desactiver.ForeColor = inutiles > 0 ? FpsUi.Ink : FpsUi.Dim2;

            int traces = InventaireServices.Journal().Count;
            _restaurer.Enabled = traces > 0;
            _restaurer.Text = traces > 0 ? "Tout restaurer (" + traces + ")" : "Tout restaurer";
            _restaurer.ForeColor = traces > 0 ? FpsUi.Ink : FpsUi.Dim2;
        }

        // ==================================================================
        //  Gestes
        // ==================================================================

        private void Applique(InventaireServices.Geste geste)
        {
            List<InventaireServices.Service> choisis = Coches();

            if (geste == InventaireServices.Geste.Suspendre)
            {
                // Dire ce que « suspendre » ne fait PAS. Un service en démarrage automatique
                // reviendra au prochain allumage : c'est voulu, et le taire ferait passer un
                // comportement normal pour un échec de l'outil.
                double mo = 0;
                foreach (InventaireServices.Service s in choisis) mo += s.RamMo;
                string q = "Arrêter " + choisis.Count + " service(s) maintenant, pour environ "
                         + Math.Round(mo).ToString("0") + " Mo ?\n\n"
                         + "Rien n'est désactivé : Windows peut les relancer à la demande, « Tout restaurer » "
                         + "les relance tout de suite, et ceux qui démarrent automatiquement reviendront "
                         + "de toute façon au prochain redémarrage.";
                if (MessageBox.Show(this, q, "ONYX", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    return;
            }
            else
            {
                // On ne désactive DURABLEMENT que ce qui est catalogué inutile. Un service tiers
                // coché reste suspendable — désactiver ce qu'on n'a pas identifié, c'est justement
                // ce qui casse des machines trois semaines plus tard.
                var retenus = new List<InventaireServices.Service>();
                foreach (InventaireServices.Service s in choisis)
                    if (s.Verdict == InventaireServices.Verdict.Inutile) retenus.Add(s);

                int ecartes = choisis.Count - retenus.Count;
                if (retenus.Count == 0) return;

                string q = "Désactiver durablement " + retenus.Count + " service(s) ?\n\n"
                         + "C'est réversible : « Tout restaurer » remet chacun dans l'état exact où il était."
                         + (ecartes > 0
                            ? "\n\n" + ecartes + " service(s) coché(s) ne seront PAS désactivés : ils ne sont pas "
                              + "identifiés comme inutiles ici. Utilise « Suspendre » pour ceux-là."
                            : "");
                if (MessageBox.Show(this, q, "ONYX", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    return;
                choisis = retenus;
            }

            Cursor = Cursors.WaitCursor;
            try { InventaireServices.Applique(choisis, geste, _actions, _log); }
            finally { Cursor = Cursors.Default; }
            Relit(_cpuMesure);
        }

        private void Restaure()
        {
            Cursor = Cursors.WaitCursor;
            try { InventaireServices.Restaure(_actions, _log); }
            finally { Cursor = Cursors.Default; }
            Relit(_cpuMesure);
        }
    }
}
