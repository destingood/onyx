using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BTOptimizer
{
    /// <summary>
    /// FENÊTRE DE MISE À JOUR — tout le parcours au même endroit, en un clic.
    ///
    /// Avant : une boîte de dialogue « Ouvrir ? », puis une seconde « Télécharger et installer ? »,
    /// puis 58 Mo téléchargés sans rien à l'écran (la progression n'allait que dans le journal),
    /// puis l'assistant de l'installateur, puis ONYX à rouvrir à la main.
    ///
    /// Maintenant : la fenêtre s'ouvre tout de suite (« Recherche… ») pendant que le réseau
    /// travaille en fond, montre les nouveautés lisibles, et un seul bouton télécharge avec une
    /// barre de progression et un bouton Annuler. Le fichier est contrôlé (taille et empreinte
    /// SHA256 publiée par GitHub), puis installé sans assistant ; l'installateur rouvre ONYX.
    ///
    /// Rien n'est téléchargé sans le clic sur « Mettre à jour ». Non modale : l'application reste
    /// utilisable pendant le téléchargement.
    /// </summary>
    internal sealed class MajForm : Form
    {
        /// <summary>Harnais : remplace le téléchargement (fichier, erreur).</summary>
        internal static Func<Updater.Release, Action<long, long>, CancellationToken, Tuple<string, string>> TelechargePourTest;

        private readonly Func<Tuple<Updater.Release, string>> _verif;
        private readonly Func<string, Updater.Release, string> _lanceur;
        private readonly Label _titre, _statut, _detail;
        private readonly TextBox _notes;
        private readonly Barre _barre;
        private readonly PillButton _principal, _secondaire;
        private Updater.Release _rel;
        private CancellationTokenSource _annulation;
        private Action _actionPrincipale, _actionSecondaire;

        /// <summary>recherche, ajour, indispo, dispo, telechargement, erreur, installation.</summary>
        internal string Etat { get; private set; }
        internal string TexteNotes { get { return _notes.Visible ? _notes.Text : ""; } }
        internal string TexteStatut { get { return _statut.Text; } }
        internal string TexteDetail { get { return _detail.Text; } }
        internal string LibellePrincipal { get { return _principal.Visible ? _principal.Text : null; } }
        internal string LibelleSecondaire { get { return _secondaire.Visible ? _secondaire.Text : null; } }
        internal void ClicPrincipal() { var a = _actionPrincipale; if (a != null) a(); }
        internal void ClicSecondaire() { var a = _actionSecondaire; if (a != null) a(); }

        /// <param name="verif">Interroge le serveur des mises à jour (appelé hors du fil d'interface).</param>
        /// <param name="lanceur">Lance l'installateur téléchargé : null si c'est parti, sinon l'erreur.</param>
        public MajForm(Func<Tuple<Updater.Release, string>> verif, Func<string, Updater.Release, string> lanceur)
        {
            _verif = verif;
            _lanceur = lanceur;

            Text = "ONYX — mise à jour";
            ClientSize = new Size(540, 380);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = FpsUi.BgMain;
            ForeColor = FpsUi.Ink;
            Font = FpsUi.Body;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _titre = new Label { Location = new Point(22, 18), Size = new Size(496, 26), Font = FpsUi.H2, ForeColor = FpsUi.Gold, Text = "Mise à jour d'ONYX", UseMnemonic = false };
            _statut = new Label { Location = new Point(22, 50), Size = new Size(496, 40), ForeColor = FpsUi.Ink, UseMnemonic = false };
            _notes = new TextBox
            {
                Location = new Point(22, 96), Size = new Size(496, 170), Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = FpsUi.Card,
                ForeColor = FpsUi.Dim, Font = FpsUi.Body, TabStop = false, Visible = false
            };
            _barre = new Barre { Location = new Point(22, 284), Size = new Size(496, 8), Visible = false };
            _detail = new Label { Location = new Point(22, 298), Size = new Size(496, 32), ForeColor = FpsUi.Dim2, Font = FpsUi.Small, UseMnemonic = false };
            _principal = new PillButton("Mettre à jour") { Size = new Size(150, 32), Location = new Point(368, 334) };
            _secondaire = new PillButton("Fermer") { Size = new Size(120, 32), Location = new Point(240, 334) };
            _principal.Click += (s, e) => ClicPrincipal();
            _secondaire.Click += (s, e) => ClicSecondaire();
            Controls.AddRange(new Control[] { _titre, _statut, _notes, _barre, _detail, _principal, _secondaire });
            AcceptButton = _principal;

            FormClosing += (s, e) =>
            {
                // Fermer pendant le téléchargement = annuler : le fichier partiel est supprimé.
                try { if (_annulation != null) _annulation.Cancel(); } catch { }
            };
            // Au chargement, pas dans le constructeur : un résultat immédiat arriverait avant que la
            // fenêtre existe, et BeginInvoke le perdrait.
            Etat = "recherche";
            _statut.Text = "Recherche d'une nouvelle version…";
            Boutons(null, null, "Fermer", Close);
            Load += (s, e) => Recherche();
        }

        private void Boutons(string principal, Action ap, string secondaire, Action asec)
        {
            _principal.Visible = principal != null;
            if (principal != null) _principal.Text = principal;
            _actionPrincipale = ap;
            _secondaire.Visible = secondaire != null;
            if (secondaire != null) _secondaire.Text = secondaire;
            _actionSecondaire = asec;
            // Sans bouton principal, le secondaire prend sa place à droite.
            _secondaire.Left = principal != null ? 240 : 398;
        }

        private void Recherche()
        {
            Etat = "recherche";
            _titre.Text = "Mise à jour d'ONYX";
            _statut.Text = "Recherche d'une nouvelle version…";
            _notes.Visible = false; _barre.Visible = false;
            _detail.Text = "";
            Boutons(null, null, "Fermer", Close);
            Task.Run(() =>
            {
                Updater.Release rel = null; string status = "";
                try { var t = _verif(); rel = t.Item1; status = t.Item2; }
                catch (Exception ex) { status = "La vérification a échoué : " + ex.Message; }
                try { BeginInvoke((Action)(() => Resultat(rel, status))); } catch { }
            });
        }

        private static string Court(Version v) { return v == null ? "?" : v.Major + "." + v.Minor.ToString("00"); }

        private void Resultat(Updater.Release rel, string status)
        {
            if (IsDisposed) return;
            _rel = rel;
            var cur = Updater.CurrentVersion();
            if (rel == null)
            {
                Etat = "erreur";
                _statut.Text = string.IsNullOrEmpty(status) ? "Aucune information de mise à jour n'a pu être obtenue." : status;
                _statut.Height = 180;
                Boutons("Réessayer", () => { _statut.Height = 40; Recherche(); }, "Fermer", Close);
                return;
            }
            if (!Updater.IsNewer(cur, rel.Ver))
            {
                Etat = "ajour";
                _titre.Text = "✅ ONYX est à jour";
                _statut.Text = "Tu as la version " + Court(cur) + ", la dernière publiée est la " + Court(rel.Ver) + ".";
                Boutons(null, null, "Fermer", Close);
                return;
            }
            _titre.Text = "Version " + Court(rel.Ver) + " disponible";
            _statut.Text = "Tu as la version " + Court(cur) + ". Tes réglages, la mémoire du Copilote et ton journal sont conservés.";
            string notes = MajLogique.NotesLisibles(rel.Notes, 1500);
            _notes.Text = notes.Length > 0 ? notes.Replace("\n", "\r\n") : "Aucune note de version publiée.";
            _notes.Visible = true;
            _notes.SelectionLength = 0;
            if (rel.AssetUrl == null || !Updater.IsTrustedUrl(rel.AssetUrl, Updater.ManifestUrl))
            {
                Etat = "indispo";
                _detail.Text = rel.AssetUrl == null
                    ? "Cette version n'a pas de programme d'installation attaché : télécharge-la depuis GitHub."
                    : "Le lien de téléchargement ne vient pas de GitHub : ONYX refuse de le lancer.";
                Boutons(null, null, "Fermer", Close);
                return;
            }
            Etat = "dispo";
            _detail.Text = "Téléchargement de " + SteamGames.Human(rel.Size) + " depuis GitHub, empreinte vérifiée, "
                         + "puis installation sans assistant. ONYX se ferme et se rouvre tout seul.";
            Boutons("Mettre à jour", Telecharge, "Plus tard", Close);
        }

        private void Telecharge()
        {
            var rel = _rel;
            if (rel == null) return;
            Etat = "telechargement";
            _barre.Valeur = 0; _barre.Visible = true;
            _detail.Text = MajLogique.Progression(0, rel.Size);
            var cts = new CancellationTokenSource();
            _annulation = cts;
            Boutons(null, null, "Annuler", () => { try { cts.Cancel(); } catch { } });
            Action<long, long> prog = (fait, total) =>
            {
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (IsDisposed || Etat != "telechargement") return;
                        _barre.Valeur = total > 0 ? (float)fait / total : 0f;
                        _detail.Text = MajLogique.Progression(fait, total);
                    }));
                }
                catch { }
            };
            Task.Run(() =>
            {
                string file = null, erreur = "";
                try
                {
                    if (TelechargePourTest != null) { var t = TelechargePourTest(rel, prog, cts.Token); file = t.Item1; erreur = t.Item2; }
                    else file = Updater.Download(rel, prog, cts.Token, out erreur);
                }
                catch (Exception ex) { erreur = "Le téléchargement a échoué : " + ex.Message; }
                try { BeginInvoke((Action)(() => FinTelechargement(file, erreur, cts.IsCancellationRequested))); } catch { }
            });
        }

        private void FinTelechargement(string file, string erreur, bool annule)
        {
            if (IsDisposed) return;
            _annulation = null;
            _barre.Visible = false;
            if (file == null)
            {
                Resultat(_rel, "");
                if (annule)
                    _detail.Text = "Téléchargement annulé : rien n'a été installé.";
                else
                {
                    Etat = "erreur";
                    _detail.Text = string.IsNullOrEmpty(erreur) ? "Le téléchargement a échoué : rien n'a été installé." : erreur;
                    Boutons("Réessayer", Telecharge, "Plus tard", Close);
                }
                return;
            }
            Etat = "installation";
            _titre.Text = "Installation de la version " + Court(_rel.Ver);
            _detail.Text = "ONYX se ferme pendant l'installation et se rouvre tout seul dans une minute environ.";
            Boutons(null, null, null, null);
            string err = null;
            try { err = _lanceur(file, _rel); } catch (Exception ex) { err = ex.Message; }
            if (err != null)
            {
                Etat = "erreur";
                _detail.Text = "Impossible de lancer l'installateur (" + err + "). Fichier : " + file;
                Boutons("Réessayer", Telecharge, "Fermer", Close);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { if (_annulation != null) _annulation.Cancel(); } catch { } }
            base.Dispose(disposing);
        }

        /// <summary>Barre de progression aux couleurs d'ONYX (celle de Windows reste claire et verte).</summary>
        private sealed class Barre : Control
        {
            private float _v;
            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public float Valeur { get { return _v; } set { _v = Math.Max(0f, Math.Min(1f, value)); Invalidate(); } }
            public Barre()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(FpsUi.Card);
                int w = (int)(Width * _v);
                if (w > 0) using (var b = new SolidBrush(FpsUi.Gold)) e.Graphics.FillRectangle(b, 0, 0, w, Height);
            }
        }
    }
}
